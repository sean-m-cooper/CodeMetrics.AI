using System.Xml.Linq;
using CodeMetrics.AI.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

public static class ArchitectureProbe
{
    // Cross-cutting types that are acceptable in controllers
    private static readonly string[] CrossCuttingPrefixes =
    [
        "ILogger", "IMapper", "IMediator", "IConfiguration", "IOptions",
        "IHttpClientFactory", "IMemoryCache", "IDistributedCache"
    ];

    // Infrastructure keywords for service concrete dependency check
    private static readonly string[] InfrastructureKeywords =
    [
        "Gateway", "Client", "Context", "Repository", "Infrastructure"
    ];

    // Data-related keywords for controller dependency check
    private static readonly string[] DataKeywords =
    [
        "DbContext", "Context", "Repository", "DAL"
    ];

    private static readonly HashSet<string> DependencyInjectionExtensionReceivers =
    [
        "Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "Microsoft.Extensions.Hosting.IHostApplicationBuilder",
        "Microsoft.Extensions.Hosting.IHostBuilder",
        "Microsoft.Extensions.Hosting.HostApplicationBuilder",
        "Microsoft.AspNetCore.Builder.WebApplicationBuilder",
        "Microsoft.AspNetCore.Hosting.IWebHostBuilder"
    ];

    public static DimensionResult Analyze(
        IReadOnlyList<(string Name, Compilation Compilation)> projects,
        IReadOnlyList<TypeMetrics> typeMetrics,
        string solutionDir)
    {
        var findings = new List<Finding>();
        var dependencyInjectionExtensionTypes = new HashSet<string>(StringComparer.Ordinal);

        // 1. Project graph cycle detection
        var cycles = DetectProjectCycles(solutionDir);
        foreach (var cycle in cycles)
        {
            findings.Add(new Finding
            {
                Category = "projectCycle",
                Severity = "error",
                Message = $"Circular project reference detected: {string.Join(" → ", cycle)} → {cycle[0]}"
            });
        }

        // 2. Convention-based layering findings
        foreach (var (projectName, compilation) in projects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                var root = tree.GetRoot();
                var filePath = tree.FilePath;
                var semanticModel = compilation.GetSemanticModel(tree);

                AnalyzeLayeringViolations(root, semanticModel, filePath, projectName, findings);
                CollectDependencyInjectionExtensionTypes(
                    root, semanticModel, projectName, dependencyInjectionExtensionTypes);
            }
        }

        // 3. Static metric hotspots
        var hotspots = FindMetricHotspots(typeMetrics, dependencyInjectionExtensionTypes);
        findings.AddRange(hotspots);

        var excludedDataCarrierCount = typeMetrics.Count(metric => metric.IsDataCarrier);
        var excludedDependencyInjectionExtensionCount = typeMetrics.Count(metric =>
            dependencyInjectionExtensionTypes.Contains(
                GetTypeKey(metric.Project, metric.Namespace, metric.Type)));

        // 4. Scoring
        var errorFindings = findings.Where(f => f.Severity == "error").ToList();
        var warningFindings = findings.Where(f => f.Severity == "warning").ToList();
        var hasErrors = errorFindings.Count > 0;
        var hasHotspots = hotspots.Count > 0;
        var warningCount = warningFindings.Count;

        // Ladder rungs. The warning tail spans 4/6/8 for the same reason as
        // PerformanceAsyncProbe: rung 4 has no structural condition of its own.
        //   0  broken   — a circular project reference
        //   2  errors   — layering errors or metric hotspots
        //   4  noisy    — more than two advisory warnings
        //   6  several   — two advisory warnings
        //   8  minor    — a single advisory warning, no errors or hotspots
        //  10  clean    — no findings
        // Cycles get rung 0 of their own rather than sharing 2 with everything else. A
        // cyclic project graph is not a poor architecture but an absent one: the
        // dependency direction the layering rules are checked against does not exist, so
        // the remaining findings are measured against nothing. Without this rung the
        // worst achievable score was 2, leaving a catastrophically broken architecture
        // indistinguishable from a merely untidy one — the mirror of the missing-8 bug.
        var hasCycles = cycles.Count > 0;
        double score;
        if (hasCycles)
            score = 0;
        else if (hasErrors || hasHotspots)
            score = 2;
        else if (warningCount > 2)
            score = 4;
        else if (warningCount > 1)
            score = 6;
        else if (warningCount >= 1)
            score = 8;
        else
            score = 10;

        var basis = $"Findings: {findings.Count} (errors: {errorFindings.Count}, warnings: {warningFindings.Count}). " +
                    $"Cycles: {cycles.Count}, hotspots: {hotspots.Count}. " +
                    $"Excluded passive data carriers: {excludedDataCarrierCount}, " +
                    $"DI extension types: {excludedDependencyInjectionExtensionCount}.";

        // Extra data
        var cycleList = cycles.Select(c => string.Join(" → ", c) + " → " + c[0]).ToList();
        var hotspotSummary = hotspots.Select(h => new { h.Type, h.Category, h.Message }).ToList<object>();

        return new DimensionResult
        {
            Status = "scored",
            Score = score,
            Basis = basis,
            Findings = findings,
            Extra =
            {
                ["cycles"] = cycleList,
                ["hotspots"] = hotspotSummary,
                ["excludedPassiveDataCarriers"] = excludedDataCarrierCount,
                ["excludedDependencyInjectionExtensionTypes"] = excludedDependencyInjectionExtensionCount
            }
        };
    }

    // ── Project cycle detection ───────────────────────────────────────────────

    private static List<List<string>> DetectProjectCycles(string solutionDir)
    {
        if (!Directory.Exists(solutionDir))
            return [];

        // Find all .csproj files, skipping inaccessible directories
        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };
        var csprojFiles = Directory.GetFiles(solutionDir, "*.csproj", enumOptions);

        // Build adjacency list: projectName → list of referenced project names
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var csprojFile in csprojFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(csprojFile);
            if (!adjacency.ContainsKey(projectName))
                adjacency[projectName] = [];

            try
            {
                var doc = XDocument.Load(csprojFile);
                var projectRefs = doc.Descendants()
                    .Where(e => e.Name.LocalName == "ProjectReference")
                    .Select(e => e.Attribute("Include")?.Value)
                    .Where(v => v != null)
                    .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))
                    .Where(n => !string.IsNullOrEmpty(n));

                foreach (var refName in projectRefs)
                {
                    if (!adjacency[projectName].Contains(refName!, StringComparer.OrdinalIgnoreCase))
                        adjacency[projectName].Add(refName!);
                }
            }
            catch
            {
                // Skip malformed .csproj files
            }
        }

        // DFS cycle detection with white/gray/black coloring
        // white = 0 (unvisited), gray = 1 (in progress), black = 2 (done)
        var color = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var parent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var allCycles = new List<List<string>>();
        var cycleSignatures = new HashSet<string>();

        foreach (var node in adjacency.Keys)
        {
            color[node] = 0;
            parent[node] = null;
        }

        var stack = new List<string>();

        void Dfs(string node)
        {
            color[node] = 1; // gray
            stack.Add(node);

            if (adjacency.TryGetValue(node, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!color.ContainsKey(neighbor))
                    {
                        // Neighbor not in graph (external), skip
                        continue;
                    }

                    if (color[neighbor] == 1) // gray → cycle found
                    {
                        // Extract cycle path from stack
                        var cycleStart = stack.IndexOf(neighbor);
                        if (cycleStart >= 0)
                        {
                            var cycle = stack.Skip(cycleStart).ToList();
                            var signature = string.Join(",", cycle.OrderBy(x => x));
                            if (cycleSignatures.Add(signature))
                            {
                                allCycles.Add(cycle);
                            }
                        }
                    }
                    else if (color[neighbor] == 0) // white → visit
                    {
                        parent[neighbor] = node;
                        Dfs(neighbor);
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            color[node] = 2; // black
        }

        foreach (var node in adjacency.Keys)
        {
            if (color[node] == 0)
                Dfs(node);
        }

        return allCycles;
    }

    // ── Layering violation detection ──────────────────────────────────────────

    private static void AnalyzeLayeringViolations(
        SyntaxNode root, SemanticModel semanticModel, string filePath, string projectName, List<Finding> findings)
    {
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

        foreach (var typeDecl in typeDeclarations)
        {
            var typeName = typeDecl.Identifier.Text;

            // Collect all constructor parameter types
            var constructorParams = GetAllConstructorParameterTypeNames(typeDecl, semanticModel);

            if (typeName.EndsWith("Controller", StringComparison.Ordinal))
            {
                // Check for data-layer dependencies in controllers
                foreach (var (paramTypeName, _, line) in constructorParams)
                {
                    if (IsCrossCuttingType(paramTypeName))
                        continue;

                    if (DataKeywords.Any(kw =>
                            paramTypeName.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    {
                        findings.Add(new Finding
                        {
                            Category = "controllerDataDependency",
                            Severity = "error",
                            File = filePath,
                            Line = line,
                            Project = projectName,
                            Type = typeName,
                            Message = $"Controller '{typeName}' directly depends on data-layer type '{paramTypeName}'. " +
                                      "Controllers should not depend on DbContext, Repository, or DAL types."
                        });
                    }
                }
            }
            else if (typeName.EndsWith("Service", StringComparison.Ordinal))
            {
                // Check for concrete infrastructure dependencies in services
                foreach (var (paramTypeName, paramNamespace, line) in constructorParams)
                {
                    // Concrete = does NOT start with "I"
                    if (GetUnqualifiedTypeName(paramTypeName).StartsWith("I", StringComparison.Ordinal))
                        continue;

                    if (IsFrameworkNamespace(paramNamespace))
                        continue;

                    if (InfrastructureKeywords.Any(kw =>
                            paramTypeName.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                    {
                        findings.Add(new Finding
                        {
                            Category = "concreteInfrastructureDependency",
                            Severity = "warning",
                            File = filePath,
                            Line = line,
                            Project = projectName,
                            Type = typeName,
                            Message = $"Service '{typeName}' depends on concrete infrastructure type '{paramTypeName}'. " +
                                      "Prefer depending on abstractions (interfaces)."
                        });
                    }
                }
            }
        }
    }

    private static bool IsCrossCuttingType(string typeName)
    {
        return CrossCuttingPrefixes.Any(prefix =>
            typeName.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsFrameworkNamespace(string? namespaceName)
    {
        return namespaceName?.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) == true ||
               namespaceName?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true;
    }

    private static string GetUnqualifiedTypeName(string typeName)
    {
        var genericTick = typeName.IndexOf('<');
        var withoutGeneric = genericTick >= 0 ? typeName[..genericTick] : typeName;
        var dot = withoutGeneric.LastIndexOf('.');
        return dot >= 0 ? withoutGeneric[(dot + 1)..] : withoutGeneric;
    }

    private static List<(string TypeName, string? Namespace, int Line)> GetAllConstructorParameterTypeNames(
        TypeDeclarationSyntax typeDecl, SemanticModel semanticModel)
    {
        var result = new List<(string, string?, int)>();

        // Regular constructor parameters
        var constructors = typeDecl.Members.OfType<ConstructorDeclarationSyntax>();
        foreach (var ctor in constructors)
        {
            foreach (var param in ctor.ParameterList.Parameters)
            {
                AddParameterType(param, semanticModel, result);
            }
        }

        // Primary constructor parameters (on the type declaration itself)
        if (typeDecl is RecordDeclarationSyntax record && record.ParameterList != null)
        {
            foreach (var param in record.ParameterList.Parameters)
            {
                AddParameterType(param, semanticModel, result);
            }
        }

        // Class with primary constructor (C# 12+)
        if (typeDecl is ClassDeclarationSyntax classDecl && classDecl.ParameterList != null)
        {
            foreach (var param in classDecl.ParameterList.Parameters)
            {
                AddParameterType(param, semanticModel, result);
            }
        }

        return result;
    }

    private static void AddParameterType(
        ParameterSyntax parameter,
        SemanticModel semanticModel,
        List<(string TypeName, string? Namespace, int Line)> result)
    {
        var typeName = parameter.Type?.ToString();
        if (string.IsNullOrEmpty(typeName))
            return;

        var typeSymbol = semanticModel.GetTypeInfo(parameter.Type!).Type;
        var namespaceName = typeSymbol?.ContainingNamespace?.ToDisplayString();
        var line = parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        result.Add((typeName!, namespaceName, line));
    }

    // ── Static metric hotspots ────────────────────────────────────────────────

    private static void CollectDependencyInjectionExtensionTypes(
        SyntaxNode root,
        SemanticModel semanticModel,
        string projectName,
        HashSet<string> result)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol typeSymbol ||
                !IsDependencyInjectionExtensionType(typeSymbol))
            {
                continue;
            }

            result.Add(GetTypeKey(
                projectName,
                typeSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                typeSymbol.Name));
        }
    }

    private static bool IsDependencyInjectionExtensionType(INamedTypeSymbol typeSymbol)
    {
        if (!typeSymbol.IsStatic)
            return false;

        var nonPrivateMethods = typeSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method =>
                !method.IsImplicitlyDeclared &&
                method.MethodKind == MethodKind.Ordinary &&
                method.DeclaredAccessibility != Accessibility.Private)
            .ToList();

        return nonPrivateMethods.Count > 0 &&
               nonPrivateMethods.All(IsDependencyInjectionExtensionMethod);
    }

    private static bool IsDependencyInjectionExtensionMethod(IMethodSymbol method)
    {
        return method.IsExtensionMethod &&
               method.Parameters.Length > 0 &&
               IsDependencyInjectionExtensionReceiver(method.Parameters[0].Type);
    }

    private static bool IsDependencyInjectionExtensionReceiver(ITypeSymbol receiverType)
    {
        if (DependencyInjectionExtensionReceivers.Contains(
                receiverType.OriginalDefinition.ToDisplayString()))
        {
            return true;
        }

        return receiverType is INamedTypeSymbol named &&
               named.AllInterfaces.Any(interfaceType =>
                   DependencyInjectionExtensionReceivers.Contains(
                       interfaceType.OriginalDefinition.ToDisplayString()));
    }

    private static string GetTypeKey(string project, string namespaceName, string typeName)
    {
        return $"{project}\0{namespaceName}\0{typeName}";
    }

    private static List<Finding> FindMetricHotspots(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlySet<string> dependencyInjectionExtensionTypes)
    {
        var hotspots = new List<Finding>();

        foreach (var tm in typeMetrics)
        {
            if (tm.IsDataCarrier)
                continue;

            if (dependencyInjectionExtensionTypes.Contains(
                    GetTypeKey(tm.Project, tm.Namespace, tm.Type)))
            {
                continue;
            }

            if (tm.CyclomaticComplexity >= 80)
            {
                hotspots.Add(new Finding
                {
                    Category = "highCyclomaticComplexity",
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has cyclomatic complexity of {tm.CyclomaticComplexity} (threshold: 80)."
                });
            }

            var couplingThreshold = tm.Type.EndsWith("Controller", StringComparison.Ordinal) ? 50 : 30;
            if (tm.ClassCoupling >= couplingThreshold)
            {
                hotspots.Add(new Finding
                {
                    Category = "highCoupling",
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has class coupling of {tm.ClassCoupling} (threshold: {couplingThreshold})."
                });
            }

            if (tm.LinesOfSource >= 500)
            {
                hotspots.Add(new Finding
                {
                    Category = "largeClass",
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has {tm.LinesOfSource} lines of source (threshold: 500)."
                });
            }
        }

        // Order: CC desc → coupling desc → LinesOfSource desc, take top 10
        return hotspots
            .OrderByDescending(f => f.Category == "highCyclomaticComplexity"
                ? typeMetrics.FirstOrDefault(t => t.Type == f.Type)?.CyclomaticComplexity ?? 0 : 0)
            .ThenByDescending(f => f.Category == "highCoupling"
                ? typeMetrics.FirstOrDefault(t => t.Type == f.Type)?.ClassCoupling ?? 0 : 0)
            .ThenByDescending(f => f.Category == "largeClass"
                ? typeMetrics.FirstOrDefault(t => t.Type == f.Type)?.LinesOfSource ?? 0 : 0)
            .Take(10)
            .ToList();
    }
}
