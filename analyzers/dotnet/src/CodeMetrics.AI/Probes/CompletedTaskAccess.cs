using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class CompletedTaskAccess
{
    public static bool IsKnownCompleted(
        SyntaxNode access,
        ExpressionSyntax receiver,
        SemanticModel semanticModel)
    {
        var receiverSymbol = semanticModel.GetSymbolInfo(receiver).Symbol;
        if (receiverSymbol is not (ILocalSymbol or IParameterSymbol))
            return false;

        var statement = access.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement?.Parent is not BlockSyntax block)
            return false;

        var statementIndex = block.Statements.IndexOf(statement);
        if (statementIndex < 0)
            return false;

        for (var index = statementIndex - 1; index >= 0; index--)
        {
            var preceding = block.Statements[index];
            if (AssignsAwaitedWhenAny(preceding, receiverSymbol, semanticModel))
                return true;

            if (WritesSymbol(preceding, receiverSymbol, semanticModel))
                return false;

            if (AwaitsWhenAll(preceding, receiverSymbol, semanticModel))
                return true;
        }

        return false;
    }

    private static bool AwaitsWhenAll(
        StatementSyntax statement,
        ISymbol receiverSymbol,
        SemanticModel semanticModel)
    {
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AwaitExpressionSyntax
                {
                    Expression: InvocationExpressionSyntax invocation
                }
            } ||
            !IsTaskCombinator(invocation, "WhenAll", semanticModel))
        {
            return false;
        }

        return invocation.ArgumentList.Arguments.Any(argument =>
            SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(argument.Expression).Symbol,
                receiverSymbol));
    }

    private static bool AssignsAwaitedWhenAny(
        StatementSyntax statement,
        ISymbol receiverSymbol,
        SemanticModel semanticModel)
    {
        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            return localDeclaration.Declaration.Variables.Any(variable =>
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(variable), receiverSymbol) &&
                IsAwaitedWhenAny(variable.Initializer?.Value, semanticModel));
        }

        return statement is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax assignment
        } &&
               SymbolEqualityComparer.Default.Equals(
                   semanticModel.GetSymbolInfo(assignment.Left).Symbol, receiverSymbol) &&
               IsAwaitedWhenAny(assignment.Right, semanticModel);
    }

    private static bool IsAwaitedWhenAny(ExpressionSyntax? expression, SemanticModel semanticModel)
    {
        return expression is AwaitExpressionSyntax
        {
            Expression: InvocationExpressionSyntax invocation
        } &&
               IsTaskCombinator(invocation, "WhenAny", semanticModel);
    }

    private static bool IsTaskCombinator(
        InvocationExpressionSyntax invocation,
        string methodName,
        SemanticModel semanticModel)
    {
        return semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
               method.Name == methodName &&
               method.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task";
    }

    private static bool WritesSymbol(
        StatementSyntax statement,
        ISymbol receiverSymbol,
        SemanticModel semanticModel)
    {
        if (statement.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>().Any(variable =>
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetDeclaredSymbol(variable), receiverSymbol)))
        {
            return true;
        }

        if (statement.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Left).Symbol, receiverSymbol)))
        {
            return true;
        }

        return statement.DescendantNodesAndSelf().OfType<ArgumentSyntax>().Any(argument =>
            !argument.RefKindKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None) &&
            SymbolEqualityComparer.Default.Equals(
                semanticModel.GetSymbolInfo(argument.Expression).Symbol, receiverSymbol));
    }
}
