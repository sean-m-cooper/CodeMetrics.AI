using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class CancellationInputRecognition
{
    public static bool HasInput(MethodDeclarationSyntax method, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(method) is not IMethodSymbol symbol) return false;
        return symbol.Parameters.Any(parameter => IsToken(parameter.Type) ||
            CarriesToken(parameter.Type) && ForwardsContext(method, parameter, model));
    }

    private static bool IsToken(ITypeSymbol type) => type.ToDisplayString() == "System.Threading.CancellationToken";

    private static bool CarriesToken(ITypeSymbol type)
    {
        // One accessible, instance member is a cancellation channel, not a promise of
        // end-to-end propagation. Do not infer channels from a parameter's name.
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.GetMembers().Any(member => member is IPropertySymbol
                { IsStatic: false, DeclaredAccessibility: Accessibility.Public, GetMethod: { DeclaredAccessibility: Accessibility.Public }, Parameters.Length: 0 } property && IsToken(property.Type) ||
                member is IFieldSymbol { IsStatic: false, DeclaredAccessibility: Accessibility.Public } field && IsToken(field.Type)))
                return true;
        }
        return false;
    }

    private static bool ForwardsContext(MethodDeclarationSyntax method, IParameterSymbol parameter, SemanticModel model) =>
        method.DescendantNodes(node => node == method || !PerformanceFindingContext.IsFunction(node))
            .OfType<ArgumentSyntax>().Any(argument =>
                argument.Parent?.Parent is InvocationExpressionSyntax invocation &&
                model.GetSymbolInfo(invocation).Symbol is IMethodSymbol target && TaskTypes.IsTaskLike(target.ReturnType) &&
                (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(argument.Expression).Symbol, parameter) ||
                 argument.Expression is MemberAccessExpressionSyntax member &&
                 SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(member.Expression).Symbol, parameter) &&
                 model.GetTypeInfo(member).Type is { } type && IsToken(type)));
}
