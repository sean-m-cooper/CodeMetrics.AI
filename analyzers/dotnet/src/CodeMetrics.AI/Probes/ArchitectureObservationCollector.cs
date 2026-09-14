using CodeMetrics.AI.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class ArchitectureObservationCollector
{
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

    public static ArchitectureObservations Collect(
        IReadOnlyList<(string Name, Compilation Compilation)> projects,
        string solutionDir, IReadOnlyList<string>? projectPaths)
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
        var cycles = ProjectCycleDetector.Find(solutionDir, projectPaths);
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

                ArchitectureLayeringAnalysis.Analyze(root, semanticModel, filePath, projectName, findings);
                CollectDependencyInjectionExtensionTypes(
                    root, semanticModel, projectName, dependencyInjectionExtensionTypes);
                CollectFrameworkCouplingArchetypeTypes(
                    root, semanticModel, projectName, frameworkCouplingArchetypeTypes);
                ControllerActionCollector.Collect(
                    root, semanticModel, projectName, controllerActionObservations);
            }
        }

        return new(cycles, findings,
            new(dependencyInjectionExtensionTypes, frameworkCouplingArchetypeTypes, applicationProjects),
            controllerActionObservations);
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

            result.Add(ArchitectureTypeScope.Key(
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

                result.Add(ArchitectureTypeScope.Key(
                    projectName,
                    typeSymbol.ContainingNamespace?.ToDisplayString() ?? "",
                    typeSymbol.Name));
                break;
            }
        }
    }

}
