using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

public static class LinesOfCodeCounter
{
    public static int CountSourceLines(SyntaxNode node)
    {
        // For method declarations, count only the body block contents (not the signature line)
        var body = node is MethodDeclarationSyntax m ? (SyntaxNode?)m.Body : node;
        var text = (body ?? node).ToFullString();
        var lines = text.Split('\n');
        int count = 0;
        bool inBlockComment = false;

        foreach (var rawLine in lines)
        {
            var code = RemoveComments(rawLine, ref inBlockComment);
            if (IsSourceLine(code))
                count++;
        }

        return count;
    }

    private static string RemoveComments(string rawLine, ref bool inBlockComment)
    {
        var line = rawLine.Trim();
        if (inBlockComment)
        {
            var blockEnd = line.IndexOf("*/", StringComparison.Ordinal);
            if (blockEnd < 0)
                return "";

            inBlockComment = false;
            line = line[(blockEnd + 2)..].Trim();
        }

        while (line.IndexOf("/*", StringComparison.Ordinal) is var blockStart && blockStart >= 0)
        {
            var blockEnd = line.IndexOf("*/", blockStart + 2, StringComparison.Ordinal);
            if (blockEnd < 0)
            {
                inBlockComment = true;
                line = line[..blockStart];
                break;
            }

            line = line[..blockStart] + line[(blockEnd + 2)..];
        }

        var lineComment = line.IndexOf("//", StringComparison.Ordinal);
        return (lineComment >= 0 ? line[..lineComment] : line).Trim();
    }

    private static bool IsSourceLine(string line)
    {
        return !string.IsNullOrWhiteSpace(line) && line is not ("{" or "}" or "};");
    }

    public static int CountExecutableLines(SyntaxNode node)
    {
        return node.DescendantNodes()
            .OfType<StatementSyntax>()
            .Count(s => s is not BlockSyntax);
    }
}
