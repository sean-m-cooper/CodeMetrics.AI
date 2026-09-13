using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class CorsPolicyAnalysis
{
    public static IEnumerable<SyntaxNode> FindUnsafePairs(SyntaxNode root, SemanticModel model)
    {
        var calls = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => Name(call) is "AllowAnyOrigin" or "AllowCredentials").ToList();
        var reported = new HashSet<SyntaxNode>();
        foreach (var origin in calls.Where(call => Name(call) == "AllowAnyOrigin"))
        foreach (var credentials in calls.Where(call => Name(call) == "AllowCredentials"))
        {
            if (Scope(origin) != Scope(credentials) || !SameBuilder(origin, credentials, model)) continue;
            var conditions = Conditions(origin).Concat(Conditions(credentials)).ToList();
            if (HasOppositeBranches(origin, credentials) || RejectedByGuard(origin, credentials, conditions)) continue;
            var location = origin.AncestorsAndSelf().OfType<ExpressionStatementSyntax>().FirstOrDefault() ?? (SyntaxNode)origin;
            if (reported.Add(location)) yield return location;
        }
    }

    private static string? Name(InvocationExpressionSyntax call) =>
        (call.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText;

    private static SyntaxNode? Scope(SyntaxNode node) => node.Ancestors().FirstOrDefault(ancestor =>
        ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax);

    private static ExpressionSyntax? Receiver(InvocationExpressionSyntax call)
    {
        var receiver = (call.Expression as MemberAccessExpressionSyntax)?.Expression;
        while (receiver is InvocationExpressionSyntax parent)
            receiver = (parent.Expression as MemberAccessExpressionSyntax)?.Expression;
        return receiver;
    }

    private static bool SameBuilder(InvocationExpressionSyntax first, InvocationExpressionSyntax second, SemanticModel model)
    {
        // A fluent chain has one receiver evaluation. Separate calls require a local
        // receiver spelling in the same lexical scope; arbitrary factory calls do not.
        if (first.Ancestors().Contains(second) || second.Ancestors().Contains(first)) return true;
        return Receiver(first) is IdentifierNameSyntax left && Receiver(second) is IdentifierNameSyntax right &&
            model.GetSymbolInfo(left).Symbol is { } symbol &&
            SymbolEqualityComparer.Default.Equals(symbol, model.GetSymbolInfo(right).Symbol) &&
            !HasWritesBetween(first, second);
    }

    private static IEnumerable<ExpressionSyntax> Conditions(SyntaxNode node) =>
        node.Ancestors().OfType<IfStatementSyntax>()
            .Where(branch => branch.Statement.Span.Contains(node.Span)).SelectMany(branch => Conjuncts(branch.Condition));

    private static IEnumerable<ExpressionSyntax> Conjuncts(ExpressionSyntax expression)
    {
        if (expression is ParenthesizedExpressionSyntax parent) return Conjuncts(parent.Expression);
        return expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.LogicalAndExpression)
            ? Conjuncts(binary.Left).Concat(Conjuncts(binary.Right)) : [expression];
    }

    private static bool HasOppositeBranches(SyntaxNode first, SyntaxNode second) =>
        first.Ancestors().OfType<IfStatementSyntax>().Any(branch => branch.Else is { } alternative &&
            (branch.Statement.Span.Contains(first.Span) && alternative.Span.Contains(second.Span) ||
             alternative.Span.Contains(first.Span) && branch.Statement.Span.Contains(second.Span)));

    private static bool RejectedByGuard(SyntaxNode first, SyntaxNode second, List<ExpressionSyntax> conditions)
    {
        foreach (var block in first.Ancestors().OfType<BlockSyntax>().Where(block => block.Span.Contains(second.Span)))
        foreach (var guard in block.Statements.OfType<IfStatementSyntax>())
        {
            if (guard.Span.End > Math.Min(first.SpanStart, second.SpanStart) ||
                !Exits(guard.Statement) || HasWritesBetween(guard, first) || HasWritesBetween(guard, second)) continue;
            var required = Conjuncts(guard.Condition).ToList();
            if (required.Count > 0 && required.All(condition =>
                    IsStableCondition(condition) && conditions.Any(actual => SyntaxFactory.AreEquivalent(actual, condition))))
                return true;
        }
        return false;
    }

    private static bool IsStableCondition(ExpressionSyntax condition) => condition switch
    {
        IdentifierNameSyntax => true,
        MemberAccessExpressionSyntax member => IsStableCondition(member.Expression),
        _ => false
    };

    private static bool Exits(StatementSyntax statement) => statement switch
    {
        ContinueStatementSyntax or ReturnStatementSyntax or ThrowStatementSyntax => true,
        BlockSyntax block => block.Statements.LastOrDefault() is { } last && Exits(last),
        _ => false
    };

    private static bool HasWritesBetween(SyntaxNode first, SyntaxNode second)
    {
        var start = Math.Min(first.Span.End, second.Span.End);
        var end = Math.Max(first.SpanStart, second.SpanStart);
        return first.SyntaxTree.GetRoot().DescendantNodes().Any(node => node.SpanStart >= start && node.Span.End <= end &&
            (node is AssignmentExpressionSyntax || node is PostfixUnaryExpressionSyntax postfix &&
                postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression ||
             node is PrefixUnaryExpressionSyntax prefix &&
                prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression));
    }
}
