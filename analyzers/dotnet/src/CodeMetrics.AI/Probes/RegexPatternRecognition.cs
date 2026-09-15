using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodeMetrics.AI.Probes;

/// <summary>Recognizes private constants used exclusively as BCL regex patterns.</summary>
internal static class RegexPatternRecognition
{
    public static bool IsPattern(VariableDeclaratorSyntax variable, SemanticModel model)
    {
        var symbol = model.GetDeclaredSymbol(variable);
        if (symbol is not IFieldSymbol { IsConst: true, DeclaredAccessibility: Accessibility.Private } &&
            symbol is not ILocalSymbol { IsConst: true }) return false;

        var used = false;
        foreach (var tree in model.Compilation.SyntaxTrees)
        {
            var semanticModel = model.Compilation.GetSemanticModel(tree);
            foreach (var identifier in tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>()
                         .Where(node => node.Identifier.ValueText == symbol.Name))
            {
                if (!SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier).Symbol, symbol)) continue;
                var expression = identifier.Parent is MemberAccessExpressionSyntax member && member.Name == identifier
                    ? (SyntaxNode)member : identifier;
                if (!IsPatternArgument(expression, semanticModel)) return false;
                used = true;
            }
        }
        return used;
    }

    private static bool IsPatternArgument(SyntaxNode expression, SemanticModel model)
    {
        if (expression.Parent is AttributeArgumentSyntax attributeArgument &&
            attributeArgument.Parent?.Parent is AttributeSyntax attribute &&
            model.GetSymbolInfo(attribute).Symbol is IMethodSymbol constructor &&
            constructor.ContainingType.ToDisplayString() == "System.Text.RegularExpressions.GeneratedRegexAttribute")
        {
            return attributeArgument.NameEquals == null &&
                (attributeArgument.NameColon?.Name.Identifier.ValueText == "pattern" ||
                 attributeArgument.NameColon == null && attribute.ArgumentList!.Arguments.IndexOf(attributeArgument) == 0);
        }
        return expression.Parent is ArgumentSyntax argument &&
            model.GetOperation(argument) is IArgumentOperation { Parameter: { Name: "pattern" } parameter } &&
            parameter.ContainingSymbol.ContainingType.ToDisplayString() == "System.Text.RegularExpressions.Regex";
    }
}
