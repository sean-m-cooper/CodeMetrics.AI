using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal sealed class ReturnedExceptionLocalAnalysis
{
    private readonly SemanticModel semanticModel;
    private readonly ReturnStatementSyntax[] returns;
    private readonly (ISymbol? Symbol, int Start)[] writes;

    public ReturnedExceptionLocalAnalysis(CatchClauseSyntax clause, SemanticModel semanticModel)
    {
        this.semanticModel = semanticModel;
        var scope = clause.Ancestors().FirstOrDefault(node => node is
            BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or
            LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax);
        returns = scope?.DescendantNodes(CatchObservation.ShouldDescend)
            .OfType<ReturnStatementSyntax>().ToArray() ?? [];
        // The existing mutation check includes deferred bodies. Preserve that
        // conservative boundary, while resolving each write reference only once.
        writes = scope?.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(IsWrittenReference)
            .Select(identifier => (semanticModel.GetSymbolInfo(identifier).Symbol, identifier.SpanStart))
            .ToArray() ?? [];
    }

    public bool IsReturned(ILocalSymbol local, int start) => returns.Any(statement =>
        statement.SpanStart > start && statement.Expression is { } value && ReturnsLocal(value, local) &&
        !writes.Any(write => write.Start >= start && write.Start < statement.SpanStart &&
            SymbolEqualityComparer.Default.Equals(write.Symbol, local)));

    private bool ReturnsLocal(ExpressionSyntax expression, ILocalSymbol local)
    {
        expression = ExceptionPropagationAnalysis.Unwrap(expression);
        if (IsLocal(expression, local))
            return true;

        // Same-outcome-type operations can carry context. Unrelated projections,
        // such as outcome.ToString(), do not establish an exception-bearing return.
        return expression is InvocationExpressionSyntax invocation &&
            semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
            SymbolEqualityComparer.Default.Equals(method.ReturnType, local.Type) &&
            (invocation.Expression is MemberAccessExpressionSyntax access && IsLocal(access.Expression, local) ||
             invocation.ArgumentList.Arguments.Any(argument =>
                 argument.RefKindKeyword.IsKind(SyntaxKind.None) && IsLocal(argument.Expression, local)));
    }

    private bool IsLocal(ExpressionSyntax expression, ILocalSymbol local) =>
        ExceptionPropagationAnalysis.Unwrap(expression) is IdentifierNameSyntax identifier &&
        SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier).Symbol, local);

    private static bool IsWrittenReference(IdentifierNameSyntax identifier) =>
        identifier.Ancestors().TakeWhile(node => node is not StatementSyntax).Any(node =>
            node is AssignmentExpressionSyntax write && write.Left.Span.Contains(identifier.Span) ||
            node is PrefixUnaryExpressionSyntax prefix &&
                prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression ||
            node is PostfixUnaryExpressionSyntax postfix &&
                postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression ||
            node is ArgumentSyntax argument && argument.RefKindKeyword.Kind() is
                SyntaxKind.RefKeyword or SyntaxKind.OutKeyword);
}
