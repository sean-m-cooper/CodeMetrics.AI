using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class ArchitectureLayeringAnalysis
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

    public static void Analyze(
        SyntaxNode root, SemanticModel semanticModel, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            // Controller classification takes precedence over the service naming convention.
            // Resolve constructor dependencies only after a layering rule applies to this type.
            if (WebTypeClassifier.IsController(semanticModel.GetDeclaredSymbol(declaration) as INamedTypeSymbol))
                AnalyzeController(declaration, semanticModel, filePath, projectName, findings);
            else if (declaration.Identifier.Text.EndsWith("Service", StringComparison.Ordinal))
                AnalyzeService(declaration, semanticModel, filePath, projectName, findings);
        }
    }

    private static void AnalyzeController(
        TypeDeclarationSyntax declaration, SemanticModel semanticModel,
        string filePath, string projectName, List<Finding> findings)
    {
        var typeName = declaration.Identifier.Text;
        foreach (var (paramTypeName, _, paramTypeSymbol, line) in ConstructorDependencyCollector.Collect(declaration, semanticModel))
        {
            if (CrossCuttingPrefixes.Any(prefix => paramTypeName.StartsWith(prefix, StringComparison.Ordinal)) ||
                !WebTypeClassifier.IsDataDependency(paramTypeSymbol))
                continue;

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

    private static void AnalyzeService(
        TypeDeclarationSyntax declaration, SemanticModel semanticModel,
        string filePath, string projectName, List<Finding> findings)
    {
        var typeName = declaration.Identifier.Text;
        foreach (var (paramTypeName, paramNamespace, paramTypeSymbol, line) in ConstructorDependencyCollector.Collect(declaration, semanticModel))
        {
            if (!IsConcreteInfrastructure(paramTypeName, paramNamespace, paramTypeSymbol))
                continue;

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

    private static bool IsConcreteInfrastructure(string typeName, string? namespaceName, ITypeSymbol? typeSymbol)
    {
        // Only concrete classes are actionable. An I-prefix or an Interfaces namespace
        // does not establish whether a dependency is an abstraction.
        if (typeSymbol is not INamedTypeSymbol namedType ||
            namedType.TypeKind != TypeKind.Class || namedType.IsAbstract)
            return false;

        if (namespaceName?.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) == true ||
            namespaceName?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true)
            return false;

        return InfrastructureKeywords.Any(keyword => typeName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
