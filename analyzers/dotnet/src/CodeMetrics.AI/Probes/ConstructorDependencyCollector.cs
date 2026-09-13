using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class ConstructorDependencyCollector
{
    public static List<(string TypeName, string? Namespace, ITypeSymbol? TypeSymbol, int Line)>
        Collect(
        TypeDeclarationSyntax typeDecl, SemanticModel semanticModel)
    {
        var result = new List<(string, string?, ITypeSymbol?, int)>();

        var regular = typeDecl.Members.OfType<ConstructorDeclarationSyntax>()
            .SelectMany(constructor => constructor.ParameterList.Parameters);
        // Preserve regular-before-primary order and the existing class/record scope.
        foreach (var parameter in regular.Concat(PrimaryParameters(typeDecl)))
            AddParameterType(parameter, semanticModel, result);

        return result;
    }

    private static IEnumerable<ParameterSyntax> PrimaryParameters(TypeDeclarationSyntax declaration)
    {
        var list = declaration switch
        {
            RecordDeclarationSyntax record => record.ParameterList,
            ClassDeclarationSyntax type => type.ParameterList,
            _ => null
        };
        return list?.Parameters ?? [];
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
}
