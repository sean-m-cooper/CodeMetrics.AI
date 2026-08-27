using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class FindingSuppression
{
    private const string Directive = "codemetrics-ignore:";

    public static bool IsSuppressed(SyntaxNode node, string category)
    {
        foreach (var current in node.AncestorsAndSelf())
        {
            if (ContainsDirective(current.GetLeadingTrivia(), category))
                return true;

            if (current is MemberDeclarationSyntax)
                break;
        }

        return false;
    }

    private static bool ContainsDirective(SyntaxTriviaList triviaList, string category)
    {
        return triviaList.Any(trivia =>
            (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
             trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) &&
            MatchesDirective(trivia.ToString(), category));
    }

    private static bool MatchesDirective(string comment, string category)
    {
        var marker = comment.IndexOf(Directive, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
            return false;

        var categories = comment[(marker + Directive.Length)..];
        var reasonDelimiter = categories.IndexOf('—');
        if (reasonDelimiter >= 0)
            categories = categories[..reasonDelimiter];

        reasonDelimiter = categories.IndexOf(" -- ", StringComparison.Ordinal);
        if (reasonDelimiter >= 0)
            categories = categories[..reasonDelimiter];

        return categories
            .Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Any(candidate =>
                candidate.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                candidate.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                candidate == "*");
    }
}
