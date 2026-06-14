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

    public static DimensionResult Analyze(
        IReadOnlyList<(string Name, Compilation Compilation)> projects,
        IReadOnlyList<TypeMetrics> typeMetrics,
        string solutionDir)
    {
        var findings = new List<Finding>();

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
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                    continue;

                var root = tree.GetRoot();
                var filePath = tree.FilePath;
                var semanticModel = compilation.GetSemanticModel(tree);

                AnalyzeLayeringViolations(root, semanticModel, filePath, projectName, findings);
            }
        }

        // 3. Static metric hotspots
        var hotspots = FindMetricHotspots(typeMetrics);
        findings.AddRange(hotspots);

        // 4. Scoring
        var errorFindings = findings.Where(f => f.Severity == "error").ToList();
        var warningFindings = findings.Where(f => f.Severity == "warning").ToList();
        var hasCycles = cycles.Count > 0;
        var hasErrors = errorFindings.Count > 0;
        var hasHotspots = hotspots.Count > 0;
        var warningCount = warningFindings.Count;

        double score;
        if (hasCycles)
            score = 2;
        else if (hasErrors || hasHotspots)
            score = 2;
        else if (warningCount > 2)
            score = 4;
        else if (warningCount >= 1)
            score = 6;
        else
            score = 10;

        var basis = $"Findings: {findings.Count} (errors: {errorFindings.Count}, warnings: {warningFindings.Count}). " +
                    $"Cycles: {cycles.Count}, hotspots: {hotspots.Count}.";

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
                ["hotspots"] = hotspotSummary
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

    private static List<Finding> FindMetricHotspots(IReadOnlyList<TypeMetrics> typeMetrics)
    {
        var hotspots = new List<Finding>();

        foreach (var tm in typeMetrics)
        {
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
