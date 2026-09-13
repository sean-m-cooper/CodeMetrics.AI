using CodeMetrics.AI.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class ArchitectureObservationCollector
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

                AnalyzeLayeringViolations(root, semanticModel, filePath, projectName, findings);
                CollectDependencyInjectionExtensionTypes(
                    root, semanticModel, projectName, dependencyInjectionExtensionTypes);
                CollectFrameworkCouplingArchetypeTypes(
                    root, semanticModel, projectName, frameworkCouplingArchetypeTypes);
                CollectControllerActionCoupling(
                    root, semanticModel, projectName, controllerActionObservations);
            }
        }

        return new(cycles, findings,
            new(dependencyInjectionExtensionTypes, frameworkCouplingArchetypeTypes, applicationProjects),
            controllerActionObservations);
    }

    private static void AnalyzeLayeringViolations(
        SyntaxNode root, SemanticModel semanticModel, string filePath, string projectName, List<Finding> findings)
    {
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

        foreach (var typeDecl in typeDeclarations)
        {
            var typeName = typeDecl.Identifier.Text;

            // Collect all constructor parameter types
            var constructorParams = GetAllConstructorParameterTypeNames(typeDecl, semanticModel);

            if (WebTypeClassifier.IsController(semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol))
            {
                // Check for data-layer dependencies in controllers
                foreach (var (paramTypeName, _, paramTypeSymbol, line) in constructorParams)
                {
                    if (IsCrossCuttingType(paramTypeName))
                        continue;

                    if (WebTypeClassifier.IsDataDependency(paramTypeSymbol))
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
        foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol controllerSymbol ||
                !WebTypeClassifier.IsController(controllerSymbol))
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
