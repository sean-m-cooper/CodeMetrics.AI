using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class CatchClassifier
{
    public static IReadOnlyList<CatchIssue> Classify(CatchObservation observation)
    {
        var clause = observation.Clause;
        if (clause.Block.Statements.Count == 0)
            return !FindingSuppression.IsSuppressed(clause, "emptyCatch") && !CatchIntentRecognition.HasExplanation(clause)
                ? [new(CatchIssueKind.Empty, clause)] : [];

        var issues = new List<CatchIssue>();
        var caughtName = clause.Declaration?.Identifier.Text;
        if (!string.IsNullOrEmpty(caughtName))
            issues.AddRange(observation.AllNodes.OfType<ThrowStatementSyntax>()
                .Where(statement => statement.Expression is IdentifierNameSyntax id && id.Identifier.Text == caughtName)
                .Select(statement => new CatchIssue(CatchIssueKind.ThrowCaught, statement)));

        if (IsBroadCatch(clause) && !observation.IsHandled)
        {
            issues.Add(new(CatchIssueKind.UnhandledBroad, clause));
            if (ReturnsDefault(observation.AllNodes))
                issues.Add(new(CatchIssueKind.BroadDefault, clause));
        }
        return issues;
    }

    private static bool IsBroadCatch(CatchClauseSyntax catchClause)
    {
        // Has a when filter → not broad
        if (catchClause.Filter != null)
            return false;

        // Bare catch (no declaration)
        if (catchClause.Declaration == null)
            return true;

        // catch (Exception) or catch (Exception ex)
        var typeName = catchClause.Declaration.Type.ToString();
        return typeName == "Exception" || typeName == "System.Exception";
    }

    private static bool ReturnsDefault(IReadOnlyList<SyntaxNode> nodes)
    {
        return nodes
            .OfType<ReturnStatementSyntax>()
            .Select(statement => statement.Expression)
            .Any(IsDefaultValue);
    }

    private static bool IsDefaultValue(ExpressionSyntax? expression) => expression switch
    {
        LiteralExpressionSyntax literal => literal.Kind() is SyntaxKind.NullLiteralExpression or
            SyntaxKind.FalseLiteralExpression or SyntaxKind.DefaultLiteralExpression ||
            literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.ValueText == "0",
        DefaultExpressionSyntax => true,
        MemberAccessExpressionSyntax member => member.Expression.ToString() == "string" && member.Name.Identifier.Text == "Empty",
        _ => false
    };

}

internal enum CatchIssueKind { Empty, ThrowCaught, UnhandledBroad, BroadDefault }

internal sealed record CatchIssue(CatchIssueKind Kind, SyntaxNode Node);
