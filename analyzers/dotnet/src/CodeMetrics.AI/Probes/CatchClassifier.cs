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
            return ClassifyEmpty(clause, observation.SemanticModel);

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

    private static IReadOnlyList<CatchIssue> ClassifyEmpty(CatchClauseSyntax clause, SemanticModel model)
    {
        return !FindingSuppression.IsSuppressed(clause, "emptyCatch") &&
               !IsDocumentedNarrowFallbackCatch(clause, model)
            ? [new(CatchIssueKind.Empty, clause)] : [];
    }

    private static bool IsDocumentedNarrowFallbackCatch(
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel)
    {
        if (catchClause.Declaration?.Type is not { } catchType ||
            catchClause.Parent is not TryStatementSyntax tryStatement)
        {
            return false;
        }

        if (!IsNarrowException(catchType, semanticModel))
            return false;

        var hasExplanation = catchClause.Block.DescendantTrivia(descendIntoTrivia: true)
            .Any(trivia => trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia);
        if (!hasExplanation ||
            !tryStatement.Block.DescendantNodes().OfType<ReturnStatementSyntax>().Any())
        {
            return false;
        }

        return HasFollowingReturn(tryStatement);
    }

    private static bool HasFollowingReturn(TryStatementSyntax tryStatement)
    {
        if (tryStatement.Parent is not BlockSyntax containingBlock)
            return false;
        var tryIndex = containingBlock.Statements.IndexOf(tryStatement);
        return tryIndex >= 0 &&
               tryIndex + 1 < containingBlock.Statements.Count &&
               containingBlock.Statements[tryIndex + 1] is ReturnStatementSyntax;
    }

    private static bool IsNarrowException(TypeSyntax syntax, SemanticModel model)
    {
        return model.GetTypeInfo(syntax).Type is INamedTypeSymbol caughtType &&
               caughtType.ToDisplayString() is not ("System.Exception" or "System.SystemException") &&
               DerivesFromException(caughtType);
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
