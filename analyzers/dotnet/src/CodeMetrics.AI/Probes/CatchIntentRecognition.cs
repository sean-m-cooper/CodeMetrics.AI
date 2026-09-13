using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class CatchIntentRecognition
{
    // A local prose comment is a declaration of intent, not a correctness proof.
    // Ignore deferred/nested handlers, task markers and commented-out statements.
    public static bool HasExplanation(CatchClauseSyntax clause) =>
        clause.Block.DescendantTrivia(node => CatchObservation.ShouldDescend(node) && node is not CatchClauseSyntax)
            .Concat(clause.GetTrailingTrivia())
            .Any(IsExplanation);

    private static bool IsExplanation(SyntaxTrivia trivia)
    {
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            return false;
        var text = trivia.ToString().Trim('/', '*', ' ', '\r', '\n', '\t');
        var words = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        return words.Count(word => word.Any(char.IsLetter)) >= 2 &&
            !words.Any(word => word.Trim(':', '.', ',', '*').ToUpperInvariant() is "TODO" or "FIXME" or "HACK") &&
            !text.Contains("codemetrics-ignore:", StringComparison.OrdinalIgnoreCase) &&
            SyntaxFactory.ParseStatement(text).ContainsDiagnostics;
    }

    public static bool HasErrorOutput(CatchObservation observation)
    {
        // Only direct, unconditional writes followed by a direct return count.
        // A local variable, deferred write, reset output or null/default is not reporting.
        var statements = observation.Clause.Block.Statements;
        if (statements.LastOrDefault() is not ReturnStatementSyntax)
            return false;
        foreach (var statement in statements.OfType<ExpressionStatementSyntax>())
        {
            if (statement.Expression is not AssignmentExpressionSyntax assignment ||
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                observation.SemanticModel.GetSymbolInfo(assignment.Left).Symbol is not IParameterSymbol
                    { RefKind: RefKind.Out or RefKind.Ref } parameter || !IsErrorName(parameter.Name) ||
                !HasDiagnosticValue(assignment.Right))
                continue;
            var laterWrites = observation.ActiveNodes.OfType<AssignmentExpressionSyntax>()
                .Any(later => later.SpanStart > assignment.SpanStart &&
                    SymbolEqualityComparer.Default.Equals(observation.SemanticModel.GetSymbolInfo(later.Left).Symbol, parameter));
            var laterRefWrites = observation.ActiveNodes.OfType<ArgumentSyntax>().Any(argument =>
                argument.SpanStart > assignment.SpanStart &&
                argument.RefKindKeyword.Kind() is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword &&
                SymbolEqualityComparer.Default.Equals(observation.SemanticModel.GetSymbolInfo(argument.Expression).Symbol, parameter));
            if (!laterWrites && !laterRefWrites) return true;
        }
        return false;
    }

    private static bool IsErrorName(string name) =>
        name.Contains("error", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("message", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("diagnostic", StringComparison.OrdinalIgnoreCase);

    private static bool HasDiagnosticValue(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression) &&
            !string.IsNullOrWhiteSpace(literal.Token.ValueText),
        CollectionExpressionSyntax collection => collection.Elements.OfType<ExpressionElementSyntax>()
            .Any(element => HasDiagnosticValue(element.Expression)),
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) =>
            HasDiagnosticValue(binary.Left) || HasDiagnosticValue(binary.Right),
        InterpolatedStringExpressionSyntax interpolated => interpolated.Contents
            .OfType<InterpolatedStringTextSyntax>().Any(text => !string.IsNullOrWhiteSpace(text.TextToken.ValueText)),
        // Localizers conventionally return the supplied nonempty diagnostic template.
        ElementAccessExpressionSyntax element => element.ArgumentList.Arguments
            .Any(argument => HasDiagnosticValue(argument.Expression)),
        _ => false
    };
}
