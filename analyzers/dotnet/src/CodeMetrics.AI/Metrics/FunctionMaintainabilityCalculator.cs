using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetrics.AI.Metrics;

internal static class FunctionMaintainabilityCalculator
{
    public static FunctionMaintainabilityMetrics Measure(SyntaxNode function, int ownCc)
    {
        var excluded = new List<TextSpan>();
        var tokens = Bodies(function).SelectMany(body => OwnedTokens(body, function, excluded)).ToArray();
        var lines = SourceLines(tokens, excluded);
        var volume = HalsteadCalculator.ComputeVolume(tokens);
        var raw = lines == 0 ? 100 : Math.Clamp((171 - 5.2 * Math.Log(Math.Max(volume, 1))
            - .23 * ownCc - 16.2 * Math.Log(lines)) * 100 / 171, 0, 100);
        return new(lines, volume, (decimal)raw);
    }

    private static IEnumerable<SyntaxNode> Bodies(SyntaxNode function)
    {
        // Constructor arguments execute in the constructor and must retain ownership.
        if (function is ConstructorDeclarationSyntax { Initializer: { } initializer }) yield return initializer;
        var body = function switch
        {
            BaseMethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
            PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
            LambdaExpressionSyntax lambda => lambda.Body,
            AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
            EqualsValueClauseSyntax value => value.Value,
            _ => null
        };
        if (body != null) yield return body;
    }

    private static IEnumerable<SyntaxToken> OwnedTokens(SyntaxNode node, SyntaxNode owner, List<TextSpan> excluded)
    {
        if (node != owner && node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseTypeDeclarationSyntax)
        {
            excluded.Add(node.Span);
            yield break;
        }
        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.IsToken) yield return child.AsToken();
            else foreach (var token in OwnedTokens(child.AsNode()!, owner, excluded)) yield return token;
        }
    }

    private static int SourceLines(IEnumerable<SyntaxToken> tokens, IReadOnlyList<TextSpan> excluded)
    {
        var lines = new HashSet<int>();
        int removedLines = 0, exclusion = 0;
        foreach (var token in tokens)
        {
            // Project out nested declarations, including their line breaks. Expanding a
            // nested body must not split an enclosing source line into two counted lines.
            while (exclusion < excluded.Count && excluded[exclusion].End <= token.SpanStart)
            {
                var removed = token.SyntaxTree!.GetLineSpan(excluded[exclusion++]);
                removedLines += removed.EndLinePosition.Line - removed.StartLinePosition.Line;
            }
            if (token.IsMissing || token.Kind() is SyntaxKind.OpenBraceToken or SyntaxKind.CloseBraceToken or SyntaxKind.SemicolonToken)
                continue;
            var span = token.SyntaxTree!.GetLineSpan(token.Span);
            for (var line = span.StartLinePosition.Line; line <= span.EndLinePosition.Line; line++) lines.Add(line - removedLines);
        }
        return lines.Count;
    }
}
