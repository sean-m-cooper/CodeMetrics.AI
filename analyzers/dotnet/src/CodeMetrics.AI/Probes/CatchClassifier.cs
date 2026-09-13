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
            return !FindingSuppression.IsSuppressed(clause, "emptyCatch") &&
                !IsDocumentedNarrowFallbackCatch(clause, observation.SemanticModel)
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

    private static bool IsDocumentedNarrowFallbackCatch(
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel)
    {
        if (catchClause.Declaration?.Type is not { } catchType ||
            catchClause.Parent is not TryStatementSyntax tryStatement ||
            tryStatement.Parent is not BlockSyntax containingBlock)
        {
            return false;
        }

        if (semanticModel.GetTypeInfo(catchType).Type is not INamedTypeSymbol caughtType ||
            caughtType.ToDisplayString() is "System.Exception" or "System.SystemException" ||
            !DerivesFromException(caughtType))
            return false;

        var hasExplanation = catchClause.Block.DescendantTrivia(descendIntoTrivia: true)
            .Any(trivia =>
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));
        if (!hasExplanation ||
            !tryStatement.Block.DescendantNodes().OfType<ReturnStatementSyntax>().Any())
        {
            return false;
        }

        var tryIndex = containingBlock.Statements.IndexOf(tryStatement);
        return tryIndex >= 0 &&
               tryIndex + 1 < containingBlock.Statements.Count &&
               containingBlock.Statements[tryIndex + 1] is ReturnStatementSyntax;
    }

    private static bool DerivesFromException(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
        }

        return false;
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
            .Any(ret =>
            {
                if (ret.Expression == null) return false;
                var expr = ret.Expression;
                return expr is LiteralExpressionSyntax lit &&
                           (lit.IsKind(SyntaxKind.NullLiteralExpression) ||
                            lit.IsKind(SyntaxKind.FalseLiteralExpression) ||
                            (lit.IsKind(SyntaxKind.NumericLiteralExpression) &&
                             lit.Token.ValueText == "0"))
                       || expr is DefaultExpressionSyntax
                       || expr is LiteralExpressionSyntax lit2 &&
                          lit2.IsKind(SyntaxKind.DefaultLiteralExpression)
                       || (expr is MemberAccessExpressionSyntax ma &&
                           ma.Expression.ToString() == "string" &&
                           ma.Name.Identifier.Text == "Empty");
            });
    }

}

internal enum CatchIssueKind { Empty, ThrowCaught, UnhandledBroad, BroadDefault }

internal sealed record CatchIssue(CatchIssueKind Kind, SyntaxNode Node);
