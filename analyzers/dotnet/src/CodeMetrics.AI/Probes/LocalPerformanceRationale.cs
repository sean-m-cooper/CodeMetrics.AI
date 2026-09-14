using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class LocalPerformanceRationale
{
    public static bool HasExplanation(SyntaxNode node)
    {
        // Restrict intent to the operation's statement or enclosing branch. Comments in
        // siblings, nested functions, and general method documentation do not transfer.
        foreach (var scope in node.AncestorsAndSelf().TakeWhile(scope => !PerformanceFindingContext.IsFunction(scope)))
        {
            if (scope is StatementSyntax and not BlockSyntax && Explains(scope.GetLeadingTrivia()))
                return true;
            if (scope is BlockSyntax block && Explains(block.OpenBraceToken.TrailingTrivia))
                return true;
        }
        return false;
    }

    private static bool Explains(IEnumerable<SyntaxTrivia> trivia)
    {
        var comments = trivia.Where(item => item.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
            item.IsKind(SyntaxKind.MultiLineCommentTrivia)).Select(item => item.ToString());
        var text = string.Join(" ", comments).Trim('/', '*', ' ', '\r', '\n', '\t');
        var words = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return words.Count(word => word.Any(char.IsLetter)) >= 2 &&
            !words.Any(word => word.Trim(':', '.', ',', '*').ToUpperInvariant() is "TODO" or "FIXME" or "HACK") &&
            !text.Contains("codemetrics-ignore:", StringComparison.OrdinalIgnoreCase) &&
            new[] { "synchronous", "sync-over-async", "blocking", "cancellable" }
                .Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)) &&
            SyntaxFactory.ParseStatement(text).ContainsDiagnostics;
    }
}
