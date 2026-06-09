using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Metrics;

public static class HalsteadCalculator
{
    public static double ComputeVolume(SyntaxNode node)
    {
        var operators = new HashSet<string>();
        var operands = new HashSet<string>();
        int totalOperators = 0;
        int totalOperands = 0;

        foreach (var token in node.DescendantTokens())
        {
            var kind = token.Kind();
            if (kind == SyntaxKind.EndOfFileToken || kind == SyntaxKind.None)
                continue;

            if (IsOperand(kind))
            {
                operands.Add(token.ValueText);
                totalOperands++;
            }
            else
            {
                operators.Add(token.Text);
                totalOperators++;
            }
        }

        int vocabulary = operators.Count + operands.Count;
        int length = totalOperators + totalOperands;

        if (vocabulary <= 1) return 0;

        return length * Math.Log2(vocabulary);
    }

    private static bool IsOperand(SyntaxKind kind) => kind is
        SyntaxKind.IdentifierToken or
        SyntaxKind.NumericLiteralToken or
        SyntaxKind.StringLiteralToken or
        SyntaxKind.CharacterLiteralToken or
        SyntaxKind.InterpolatedStringTextToken or
        SyntaxKind.Utf8StringLiteralToken or
        SyntaxKind.SingleLineRawStringLiteralToken or
        SyntaxKind.MultiLineRawStringLiteralToken;
}
