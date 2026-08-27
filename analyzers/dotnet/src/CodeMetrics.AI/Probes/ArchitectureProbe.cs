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

    // Framework contracts whose required surface makes raw class coupling a poor
    // architecture signal. Only coupling is suppressed; complexity and size still apply.
    private static readonly HashSet<string> FrameworkCouplingArchetypes =
    [
        "Microsoft.AspNetCore.Authentication.AuthenticationHandler`1",
        "Microsoft.EntityFrameworkCore.DbContext"
    ];

    private sealed record ControllerActionObservation(
        string Project,
        string Namespace,
        string Type,
        string Method,
        int Line,
        int TypeCoupling,
        int FromServicesParameters,
        IReadOnlyList<string> ConstructorDependencyTypes);

    public static DimensionResult Analyze(
        IReadOnlyList<(string Name, Compilation Compilation)> projects,
        IReadOnlyList<TypeMetrics> typeMetrics,
        string solutionDir)
    {
        var findings = new List<Finding>();
        var dependencyInjectionExtensionTypes = new HashSet<string>(StringComparer.Ordinal);
        var frameworkCouplingArchetypeTypes = new HashSet<string>(StringComparer.Ordinal);
        var controllerActionObservations = new List<ControllerActionObservation>();
        var applicationProjects = projects
            .Where(project => project.Compilation.Options.OutputKind == OutputKind.ConsoleApplication)
            .Select(project => project.Name)
            .ToHashSet(StringComparer.Ordinal);

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
                CollectFrameworkCouplingArchetypeTypes(
                    root, semanticModel, projectName, frameworkCouplingArchetypeTypes);
                CollectControllerActionCoupling(
                    root, semanticModel, projectName, controllerActionObservations);
            }
        }

        // 3. Static metric hotspots
        var hotspots = FindMetricHotspots(
            typeMetrics,
            dependencyInjectionExtensionTypes,
            frameworkCouplingArchetypeTypes,
            applicationProjects);
        findings.AddRange(hotspots);

        const int hotspotDisplayLimit = 10;
        var displayedHotspots = hotspots.Take(hotspotDisplayLimit).ToList();

        var excludedDataCarrierCount = typeMetrics.Count(metric => metric.IsDataCarrier);
        var excludedDependencyInjectionExtensionCount = typeMetrics.Count(metric =>
            dependencyInjectionExtensionTypes.Contains(
                GetTypeKey(metric.Project, metric.Namespace, metric.Type)));
        var excludedFrameworkCouplingArchetypeCount = typeMetrics.Count(metric =>
            frameworkCouplingArchetypeTypes.Contains(
                GetTypeKey(metric.Project, metric.Namespace, metric.Type)));
        var excludedCompositionRootCouplingCount = typeMetrics.Count(metric =>
            IsApplicationCompositionRoot(metric, applicationProjects));

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

        var hotspotBasis = hotspots.Count > displayedHotspots.Count
            ? $"hotspots: {hotspots.Count} (showing {displayedHotspots.Count})"
            : $"hotspots: {hotspots.Count}";
        var basis = $"Findings: {findings.Count} (errors: {errorFindings.Count}, warnings: {warningFindings.Count}). " +
                    $"Cycles: {cycles.Count}, {hotspotBasis}. " +
                    $"Excluded passive data carriers: {excludedDataCarrierCount}, " +
                    $"DI extension types: {excludedDependencyInjectionExtensionCount}, " +
                    $"framework coupling archetypes: {excludedFrameworkCouplingArchetypeCount}, " +
                    $"application composition roots: {excludedCompositionRootCouplingCount}.";

        // Extra data
        var cycleList = cycles.Select(c => string.Join(" → ", c) + " → " + c[0]).ToList();
        var hotspotSummary = displayedHotspots
            .Select(h => new { h.Project, h.Type, h.Category, h.Message })
            .ToList<object>();
        var controllerActionCoupling = controllerActionObservations
            .GroupBy(observation => new
            {
                observation.Project,
                observation.Namespace,
                observation.Type
            })
            .Select(group => new
            {
                group.Key.Project,
                group.Key.Namespace,
                group.Key.Type,
                ConstructorDependencyCount = group
                    .SelectMany(observation => observation.ConstructorDependencyTypes)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                MaxActionTypeCoupling = group.Max(observation => observation.TypeCoupling),
                MaxFromServicesParameters = group.Max(observation => observation.FromServicesParameters),
                Actions = group
                    .OrderBy(observation => observation.Method, StringComparer.Ordinal)
                    .ThenBy(observation => observation.Line)
                    .Select(observation => new
                    {
                        observation.Method,
                        observation.Line,
                        observation.TypeCoupling,
                        observation.FromServicesParameters
                    })
                    .ToList()
            })
            .OrderByDescending(summary => summary.MaxActionTypeCoupling)
            .ThenBy(summary => summary.Project, StringComparer.Ordinal)
            .ThenBy(summary => summary.Namespace, StringComparer.Ordinal)
            .ThenBy(summary => summary.Type, StringComparer.Ordinal)
            .ToList<object>();
        var couplingProvenance = typeMetrics
            .Where(metric => IsCouplingHotspot(
                metric,
                dependencyInjectionExtensionTypes,
                frameworkCouplingArchetypeTypes,
                applicationProjects))
            .OrderByDescending(metric => metric.ClassCoupling)
            .ThenBy(metric => metric.Project, StringComparer.Ordinal)
            .ThenBy(metric => metric.Namespace, StringComparer.Ordinal)
            .ThenBy(metric => metric.Type, StringComparer.Ordinal)
            .Select(metric => new
            {
                metric.Project,
                metric.Namespace,
                metric.Type,
                metric.ClassCoupling,
                CoupledTypes = metric.CoupledTypes
            })
            .ToList<object>();

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
                ["hotspotCount"] = hotspots.Count,
                ["hotspotsTruncated"] = hotspots.Count > displayedHotspots.Count,
                ["excludedPassiveDataCarriers"] = excludedDataCarrierCount,
                ["excludedDependencyInjectionExtensionTypes"] = excludedDependencyInjectionExtensionCount,
                ["excludedFrameworkCouplingArchetypeTypes"] = excludedFrameworkCouplingArchetypeCount,
                ["excludedApplicationCompositionRoots"] = excludedCompositionRootCouplingCount,
                ["couplingProvenance"] = couplingProvenance,
                // Supplemental evidence only. These values intentionally do not affect
                // the architecture score until they have been calibrated on a corpus.
                ["controllerActionCoupling"] = controllerActionCoupling
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
                foreach (var (paramTypeName, _, _, line) in constructorParams)
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
                foreach (var (paramTypeName, paramNamespace, paramTypeSymbol, line) in constructorParams)
                {
                    // Only concrete classes are actionable. Naming conventions such as an
                    // I-prefix and namespace fragments such as ".Interfaces" are not type
                    // facts and can produce both false positives and false negatives.
                    if (paramTypeSymbol is not INamedTypeSymbol namedType ||
                        namedType.TypeKind != TypeKind.Class ||
                        namedType.IsAbstract)
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

    private static List<(string TypeName, string? Namespace, ITypeSymbol? TypeSymbol, int Line)>
        GetAllConstructorParameterTypeNames(
        TypeDeclarationSyntax typeDecl, SemanticModel semanticModel)
    {
        var result = new List<(string, string?, ITypeSymbol?, int)>();

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
        List<(string TypeName, string? Namespace, ITypeSymbol? TypeSymbol, int Line)> result)
    {
        var typeName = parameter.Type?.ToString();
        if (string.IsNullOrEmpty(typeName))
            return;

        var typeSymbol = semanticModel.GetTypeInfo(parameter.Type!).Type;
        var namespaceName = typeSymbol?.ContainingNamespace?.ToDisplayString();
        var line = parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        result.Add((typeName!, namespaceName, typeSymbol, line));
    }

    private static void CollectControllerActionCoupling(
        SyntaxNode root,
        SemanticModel semanticModel,
        string projectName,
        List<ControllerActionObservation> observations)
    {
        foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                     .Where(candidate => candidate.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal)))
        {
            if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol controllerSymbol)
                continue;

            var constructorDependencyTypes = GetAllConstructorParameterTypeNames(declaration, semanticModel)
                .Select(parameter => parameter.TypeSymbol)
                .OfType<INamedTypeSymbol>()
                .Select(type => type.OriginalDefinition.ToDisplayString())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var action in declaration.Members.OfType<MethodDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(action) is not IMethodSymbol actionSymbol ||
                    actionSymbol.MethodKind != MethodKind.Ordinary ||
                    actionSymbol.DeclaredAccessibility != Accessibility.Public ||
                    actionSymbol.IsStatic ||
                    HasAttribute(actionSymbol, "NonActionAttribute"))
                {
                    continue;
                }

                var fromServicesCount = actionSymbol.Parameters.Count(parameter =>
                    HasAttribute(parameter, "FromServicesAttribute"));
                observations.Add(new ControllerActionObservation(
                    projectName,
                    controllerSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                    controllerSymbol.Name,
                    actionSymbol.Name,
                    action.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    ClassCouplingCalculator.CalculateAction(action, semanticModel),
                    fromServicesCount,
                    constructorDependencyTypes));
            }
        }
    }

    private static bool HasAttribute(ISymbol symbol, string attributeClassName)
    {
        return symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name == attributeClassName);
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

    private static void CollectFrameworkCouplingArchetypeTypes(
        SyntaxNode root,
        SemanticModel semanticModel,
        string projectName,
        HashSet<string> result)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol typeSymbol)
                continue;

            for (var baseType = typeSymbol.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                var definition = baseType.OriginalDefinition;
                var qualifiedMetadataName = $"{definition.ContainingNamespace?.ToDisplayString()}.{definition.MetadataName}";
                if (!FrameworkCouplingArchetypes.Contains(qualifiedMetadataName))
                    continue;

                result.Add(GetTypeKey(
                    projectName,
                    typeSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                    typeSymbol.Name));
                break;
            }
        }
    }

    private static string GetTypeKey(string project, string namespaceName, string typeName)
    {
        return $"{project}\0{namespaceName}\0{typeName}";
    }

    private static List<Finding> FindMetricHotspots(
        IReadOnlyList<TypeMetrics> typeMetrics,
        IReadOnlySet<string> dependencyInjectionExtensionTypes,
        IReadOnlySet<string> frameworkCouplingArchetypeTypes,
        IReadOnlySet<string> applicationProjects)
    {
        var hotspots = new List<(Finding Finding, int Cc, int Coupling, int Loc)>();

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
                hotspots.Add((new Finding
                {
                    Category = "highCyclomaticComplexity",
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has cyclomatic complexity of {tm.CyclomaticComplexity} (threshold: 80)."
                }, tm.CyclomaticComplexity, 0, 0));
            }

            var couplingThreshold = CouplingThreshold(tm);
            if (IsCouplingHotspot(
                    tm,
                    dependencyInjectionExtensionTypes,
                    frameworkCouplingArchetypeTypes,
                    applicationProjects))
            {
                hotspots.Add((new Finding
                {
                    Category = "highCoupling",
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has class coupling of {tm.ClassCoupling} (threshold: {couplingThreshold})."
                }, 0, tm.ClassCoupling, 0));
            }

            if (tm.LinesOfSource >= 500)
            {
                hotspots.Add((new Finding
                {
                    Category = "largeClass",
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has {tm.LinesOfSource} lines of source (threshold: 500)."
                }, 0, 0, tm.LinesOfSource));
            }
        }

        // Preserve the complete census. Presentation limits belong to the output sample,
        // not to the population used by basis, findings, or scoring.
        return hotspots
            .OrderByDescending(candidate => candidate.Cc)
            .ThenByDescending(candidate => candidate.Coupling)
            .ThenByDescending(candidate => candidate.Loc)
            .ThenBy(candidate => candidate.Finding.Project, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Finding.Type, StringComparer.Ordinal)
            .Select(candidate => candidate.Finding)
            .ToList();
    }

    private static bool IsCouplingHotspot(
        TypeMetrics metric,
        IReadOnlySet<string> dependencyInjectionExtensionTypes,
        IReadOnlySet<string> frameworkCouplingArchetypeTypes,
        IReadOnlySet<string> applicationProjects)
    {
        var typeKey = GetTypeKey(metric.Project, metric.Namespace, metric.Type);
        return !metric.IsDataCarrier &&
               !dependencyInjectionExtensionTypes.Contains(typeKey) &&
               !frameworkCouplingArchetypeTypes.Contains(typeKey) &&
               !IsApplicationCompositionRoot(metric, applicationProjects) &&
               metric.ClassCoupling >= CouplingThreshold(metric);
    }

    private static int CouplingThreshold(TypeMetrics metric)
    {
        return metric.Type.EndsWith("Controller", StringComparison.Ordinal) ? 50 : 30;
    }

    private static bool IsApplicationCompositionRoot(
        TypeMetrics metric,
        IReadOnlySet<string> applicationProjects)
    {
        return applicationProjects.Contains(metric.Project) &&
               (metric.Type is "Program" or "Startup" ||
                metric.FilePath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) ||
                metric.FilePath.EndsWith("Startup.cs", StringComparison.OrdinalIgnoreCase));
    }
}
