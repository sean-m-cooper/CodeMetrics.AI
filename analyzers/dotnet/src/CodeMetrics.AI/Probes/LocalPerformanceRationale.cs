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

    public static bool HasTaskDeclarationExplanation(SyntaxNode node, SemanticModel model)
    {
        if (!SyncBlockingDetector.TryGetTaskReceiver(node, model, out var receiver) ||
            model.GetSymbolInfo(receiver).Symbol is not ILocalSymbol { RefKind: RefKind.None } local)
            return false;
        var statement = node.Ancestors().TakeWhile(scope => !PerformanceFindingContext.IsFunction(scope))
            .OfType<StatementSyntax>().FirstOrDefault();
        if (statement?.Parent is not BlockSyntax block) return false;
        var index = block.Statements.IndexOf(statement);
        if (index <= 0 || block.Statements[index - 1] is not LocalDeclarationStatementSyntax declaration ||
            declaration.Declaration.Variables.Count != 1 || !Explains(declaration.GetLeadingTrivia()))
            return false;
        var variable = declaration.Declaration.Variables[0];
        if (variable.Initializer == null || !SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(variable), local))
            return false;

        // Only the adjacent task declaration transfers intent. Reject writes/escapes and
        // unrelated calls, including calls that could mutate a captured local indirectly.
        return !statement.DescendantNodes().Any(candidate => candidate switch
        {
            AssignmentExpressionSyntax assignment => ReferencesLocal(assignment.Left, local, model),
            ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) =>
                ReferencesLocal(argument.Expression, local, model),
            InvocationExpressionSyntax invocation => !IsAccessInvocation(invocation, node),
            _ => false
        });
    }

    private static bool ReferencesLocal(SyntaxNode node, ISymbol local, SemanticModel model) =>
        node.DescendantNodesAndSelf().Any(candidate =>
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(candidate).Symbol, local));

    private static bool IsAccessInvocation(InvocationExpressionSyntax invocation, SyntaxNode access) =>
        invocation == access || invocation.Expression == access ||
        access is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "GetResult", Expression: InvocationExpressionSyntax getAwaiter } &&
        invocation == getAwaiter;

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
