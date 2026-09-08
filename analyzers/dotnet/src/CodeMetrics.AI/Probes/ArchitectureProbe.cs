using CodeMetrics.AI.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

public static class ArchitectureProbe
{
    private const int StructuralCouplingThreshold = 10;
    private const int ControllerStructuralCouplingThreshold = 8;
    private const double HighComplexityDensityThreshold = 8;
    private const int LegacyRawCouplingThresholdValue = 30;
    private const int LegacyControllerRawCouplingThreshold = 50;

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
        int StructuralTypeCoupling,
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
        var cycles = ProjectCycleDetector.Find(solutionDir);
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
        var couplingExclusionCounts = typeMetrics
            .SelectMany(metric => metric.CouplingExclusions)
            .GroupBy(exclusion => exclusion.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(exclusion => exclusion.Value.Count),
                StringComparer.Ordinal);

        // 4. Scoring
        var errorFindings = findings.Where(f => f.Severity == "error").ToList();
        var warningFindings = findings.Where(f => f.Severity == "warning").ToList();
        var hasErrors = errorFindings.Count > 0;
        var warningCount = warningFindings.Count - hotspots.Count;

        // Preserve graph/layering caps; metric warnings have their own population policy.
        var hasCycles = cycles.Count > 0;
        double layeringCap;
        if (hasCycles)
            layeringCap = 0;
        else if (hasErrors)
            layeringCap = 2;
        else if (warningCount > 2)
            layeringCap = 4;
        else if (warningCount > 1)
            layeringCap = 6;
        else if (warningCount >= 1)
            layeringCap = 8;
        else
            layeringCap = 10;

        var eligibleTypes = typeMetrics.Where(metric => !metric.IsDataCarrier &&
            !dependencyInjectionExtensionTypes.Contains(GetTypeKey(metric.Project, metric.Namespace, metric.Type))).ToList();
        var components = new[]
        {
            ScoreMetricPopulation("coupling", eligibleTypes.Where(metric => IsCouplingEligible(
                metric, dependencyInjectionExtensionTypes, frameworkCouplingArchetypeTypes, applicationProjects)),
                metric => (double)ScoredCoupling(metric) / CouplingThreshold(metric)),
            ScoreMetricPopulation("complexity", eligibleTypes,
                metric => Math.Min(metric.CyclomaticComplexity / 80d, metric.DecompositionRatio / HighComplexityDensityThreshold)),
            ScoreMetricPopulation("size", eligibleTypes, metric => metric.LinesOfSource / 500d)
        };
        var metricScore = components.Min(component => component.Score);
        var score = Math.Round(Math.Min(metricScore, layeringCap), 1, MidpointRounding.AwayFromZero);

        var hotspotBasis = hotspots.Count > displayedHotspots.Count
            ? $"hotspots: {hotspots.Count} (showing {displayedHotspots.Count})"
            : $"hotspots: {hotspots.Count}";
        var basis = $"Findings: {findings.Count} (errors: {errorFindings.Count}, warnings: {warningFindings.Count}). " +
                    $"Cycles: {cycles.Count}, {hotspotBasis}. " +
                    $"Excluded passive data carriers: {excludedDataCarrierCount}, " +
                    $"DI extension types: {excludedDependencyInjectionExtensionCount}, " +
                    $"framework coupling archetypes: {excludedFrameworkCouplingArchetypeCount}, " +
                    $"application composition roots: {excludedCompositionRootCouplingCount}. " +
                    string.Join("; ", components.Select(component =>
                        FormattableString.Invariant($"{component.Metric}: {component.HotspotCount}/{component.EligibleTypeCount} eligible types, score {component.Score:F2}"))) +
                    FormattableString.Invariant($". Final = min(metric score {metricScore:F2}, graph/layering cap {layeringCap:F1}), rounded to 1 decimal.");

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
                MaxStructuralActionCoupling = group.Max(observation => observation.StructuralTypeCoupling),
                MaxFromServicesParameters = group.Max(observation => observation.FromServicesParameters),
                Actions = group
                    .OrderBy(observation => observation.Method, StringComparer.Ordinal)
                    .ThenBy(observation => observation.Line)
                    .Select(observation => new
                    {
                        observation.Method,
                        observation.Line,
                        observation.TypeCoupling,
                        observation.StructuralTypeCoupling,
                        observation.FromServicesParameters
                    })
                    .ToList()
            })
            .OrderByDescending(summary => summary.MaxStructuralActionCoupling)
            .ThenByDescending(summary => summary.MaxActionTypeCoupling)
            .ThenBy(summary => summary.Project, StringComparer.Ordinal)
            .ThenBy(summary => summary.Namespace, StringComparer.Ordinal)
            .ThenBy(summary => summary.Type, StringComparer.Ordinal)
            .ToList<object>();
        var couplingProvenance = typeMetrics
            .Where(metric =>
                metric.ClassCoupling >= LegacyRawCouplingThreshold(metric) ||
                IsCouplingHotspot(
                    metric,
                    dependencyInjectionExtensionTypes,
                    frameworkCouplingArchetypeTypes,
                    applicationProjects))
            .OrderByDescending(ScoredCoupling)
            .ThenByDescending(metric => metric.ClassCoupling)
            .ThenBy(metric => metric.Project, StringComparer.Ordinal)
            .ThenBy(metric => metric.Namespace, StringComparer.Ordinal)
            .ThenBy(metric => metric.Type, StringComparer.Ordinal)
            .Select(metric => new
            {
                metric.Project,
                metric.Namespace,
                metric.Type,
                metric.ClassCoupling,
                CoupledTypes = metric.CoupledTypes,
                StructuralClassCoupling = ScoredCoupling(metric),
                StructuralCoupledTypes = metric.StructuralCoupledTypes,
                CouplingExclusions = metric.CouplingExclusions,
                WouldExceedRawThreshold = metric.ClassCoupling >= LegacyRawCouplingThreshold(metric)
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
                ["architectureMetrics"] = new
                {
                    policy = "population-severity-v1",
                    formula = "component = 10 - min(6, 12 * hotspotRate) - min(4, 2 * max(0, worstThresholdRatio - 1)); final = round(min(components, graphLayeringCap), 1, awayFromZero)",
                    components,
                    metricScore,
                    graphLayeringCap = layeringCap,
                    graphLayeringReason = hasCycles ? "projectCycle" : hasErrors ? "layeringError" : warningCount > 0 ? "layeringWarnings" : "none",
                    finalScore = score
                },
                ["cycles"] = cycleList,
                ["hotspots"] = hotspotSummary,
                ["hotspotCount"] = hotspots.Count,
                ["hotspotsTruncated"] = hotspots.Count > displayedHotspots.Count,
                ["excludedPassiveDataCarriers"] = excludedDataCarrierCount,
                ["excludedDependencyInjectionExtensionTypes"] = excludedDependencyInjectionExtensionCount,
                ["excludedFrameworkCouplingArchetypeTypes"] = excludedFrameworkCouplingArchetypeCount,
                ["excludedApplicationCompositionRoots"] = excludedCompositionRootCouplingCount,
                ["excludedCouplingReferencesByReason"] = couplingExclusionCounts,
                ["couplingProvenance"] = couplingProvenance,
                // Supplemental evidence only. These values intentionally do not affect
                // the architecture score until they have been calibrated on a corpus.
                ["controllerActionCoupling"] = controllerActionCoupling
            }
        };
    }

    // ── Project cycle detection ───────────────────────────────────────────────

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
                    ClassCouplingCalculator.CalculateStructuralAction(action, semanticModel),
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

            if (tm.CyclomaticComplexity >= 80 &&
                tm.DecompositionRatio >= HighComplexityDensityThreshold)
            {
                hotspots.Add((new Finding
                {
                    Category = "highCyclomaticComplexity",
                    Observations = { ["measured"] = tm.CyclomaticComplexity, ["threshold"] = 80, ["density"] = tm.DecompositionRatio, ["densityThreshold"] = HighComplexityDensityThreshold },
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has cyclomatic complexity of {tm.CyclomaticComplexity} " +
                              $"and complexity density {tm.DecompositionRatio:F1} " +
                              $"(thresholds: 80 and {HighComplexityDensityThreshold:F1})."
                }, tm.CyclomaticComplexity, 0, 0));
            }

            var couplingThreshold = CouplingThreshold(tm);
            var scoredCoupling = ScoredCoupling(tm);
            if (IsCouplingHotspot(
                    tm,
                    dependencyInjectionExtensionTypes,
                    frameworkCouplingArchetypeTypes,
                    applicationProjects))
            {
                hotspots.Add((new Finding
                {
                    Category = "highCoupling",
                    Observations = { ["measured"] = scoredCoupling, ["threshold"] = couplingThreshold, ["rawCoupling"] = tm.ClassCoupling, ["provenance"] = "architecture.couplingProvenance" },
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has structural coupling of {scoredCoupling} " +
                              $"(raw class coupling: {tm.ClassCoupling}, threshold: {couplingThreshold})."
                }, 0, scoredCoupling, 0));
            }

            if (tm.LinesOfSource >= 500)
            {
                hotspots.Add((new Finding
                {
                    Category = "largeClass",
                    Observations = { ["measured"] = tm.LinesOfSource, ["threshold"] = 500, ["metric"] = "linesOfSource" },
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
        return IsCouplingEligible(metric, dependencyInjectionExtensionTypes, frameworkCouplingArchetypeTypes, applicationProjects) &&
               ScoredCoupling(metric) >= CouplingThreshold(metric);
    }

    private static bool IsCouplingEligible(
        TypeMetrics metric,
        IReadOnlySet<string> dependencyInjectionExtensionTypes,
        IReadOnlySet<string> frameworkCouplingArchetypeTypes,
        IReadOnlySet<string> applicationProjects)
    {
        var typeKey = GetTypeKey(metric.Project, metric.Namespace, metric.Type);
        return !metric.IsDataCarrier &&
               !dependencyInjectionExtensionTypes.Contains(typeKey) &&
               !frameworkCouplingArchetypeTypes.Contains(typeKey) &&
               !IsApplicationCompositionRoot(metric, applicationProjects);
    }

    private sealed record MetricPopulationScore(
        string Metric, int EligibleTypeCount, int HotspotCount, double HotspotRate,
        double WorstThresholdRatio, double PopulationPenalty, double SeverityPenalty, double Score);

    private static MetricPopulationScore ScoreMetricPopulation(
        string metric, IEnumerable<TypeMetrics> population, Func<TypeMetrics, double> thresholdRatio)
    {
        var ratios = population.Select(thresholdRatio).ToArray();
        var count = ratios.Count(ratio => ratio >= 1);
        var rate = ratios.Length == 0 ? 0 : (double)count / ratios.Length;
        var worst = ratios.DefaultIfEmpty(0).Max();
        var populationPenalty = Math.Min(6, 12 * rate);
        var severityPenalty = Math.Min(4, 2 * Math.Max(0, worst - 1));
        return new MetricPopulationScore(metric, ratios.Length, count, rate, worst,
            populationPenalty, severityPenalty, 10 - populationPenalty - severityPenalty);
    }

    private static int CouplingThreshold(TypeMetrics metric)
    {
        if (!metric.StructuralClassCoupling.HasValue)
        {
            return metric.Type.EndsWith("Controller", StringComparison.Ordinal)
                ? LegacyControllerRawCouplingThreshold
                : LegacyRawCouplingThresholdValue;
        }

        return metric.Type.EndsWith("Controller", StringComparison.Ordinal)
            ? ControllerStructuralCouplingThreshold
            : StructuralCouplingThreshold;
    }

    private static int LegacyRawCouplingThreshold(TypeMetrics metric)
    {
        return metric.Type.EndsWith("Controller", StringComparison.Ordinal)
            ? LegacyControllerRawCouplingThreshold
            : LegacyRawCouplingThresholdValue;
    }

    private static int ScoredCoupling(TypeMetrics metric)
    {
        return metric.StructuralClassCoupling ?? metric.ClassCoupling;
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
