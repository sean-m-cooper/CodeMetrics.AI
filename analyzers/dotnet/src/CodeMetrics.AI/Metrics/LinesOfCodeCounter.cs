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
            var line = rawLine.Trim();

            if (inBlockComment)
            {
                var endIdx = line.IndexOf("*/", StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    inBlockComment = false;
                    line = line[(endIdx + 2)..].Trim();
                }
                else continue;
            }

            // Remove inline block comments
            while (true)
            {
                var blockStart = line.IndexOf("/*", StringComparison.Ordinal);
                if (blockStart < 0) break;
                var blockEnd = line.IndexOf("*/", blockStart + 2, StringComparison.Ordinal);
                if (blockEnd >= 0)
                    line = (line[..blockStart] + line[(blockEnd + 2)..]).Trim();
                else
                {
                    inBlockComment = true;
                    line = line[..blockStart].Trim();
                    break;
                }
            }

            // Remove single-line comments
            var commentIdx = line.IndexOf("//", StringComparison.Ordinal);
            if (commentIdx >= 0)
                line = line[..commentIdx].Trim();

            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line is "{" or "}" or "};") continue;

            count++;
        }

        return count;
    }

    public static int CountExecutableLines(SyntaxNode node)
    {
        return node.DescendantNodes()
            .OfType<StatementSyntax>()
            .Count(s => s is not BlockSyntax);
    }
}
