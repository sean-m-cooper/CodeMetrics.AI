using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal sealed class ExceptionPropagationAnalysis
{
    private readonly CatchObservation observation;
    private readonly SemanticModel semanticModel;
    private readonly ISymbol caughtSymbol;
    private readonly Lazy<ReturnedExceptionLocalAnalysis> returnedLocals;

    private ExceptionPropagationAnalysis(CatchObservation observation, ISymbol caughtSymbol)
    {
        this.observation = observation;
        semanticModel = observation.SemanticModel;
        this.caughtSymbol = caughtSymbol;
        returnedLocals = new(() => new(observation.Clause, semanticModel));
    }

    public static bool Recognizes(CatchObservation observation)
    {
        if (observation.CaughtSymbol is not { } caughtSymbol)
            return false;

        // Symbol identity prevents unrelated exceptions from qualifying. A rewritten
        // catch variable cannot establish propagation under this bounded analysis.
        var flow = observation.SemanticModel.AnalyzeDataFlow(observation.Clause.Block);
        if (flow is not { Succeeded: true } ||
            flow.WrittenInside.Contains(caughtSymbol, SymbolEqualityComparer.Default))
            return false;

        var analysis = new ExceptionPropagationAnalysis(observation, caughtSymbol);
        return analysis.HasDirectReturn() || analysis.HasReturnedLocal() || analysis.HasInvokedCallback();
    }

    private bool HasDirectReturn() => observation.ActiveNodes.OfType<ReturnStatementSyntax>()
        .Any(statement => statement.Expression is { } expression && CarriesException(expression));

    private bool HasReturnedLocal()
    {
        // Only direct catch statements establish stored outcomes. Recognition of
        // later returns retains the existing scope and intervening-write boundaries.
        foreach (var statement in observation.Clause.Block.Statements)
        {
            if (statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
                IsReturnedAssignment(assignment))
                return true;

            if (statement is LocalDeclarationStatementSyntax declaration &&
                declaration.Declaration.Variables.Any(IsReturnedDeclaration))
                return true;
        }
        return false;
    }

    private bool IsReturnedAssignment(AssignmentExpressionSyntax assignment) =>
        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && CarriesException(assignment.Right) &&
        semanticModel.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol local &&
        returnedLocals.Value.IsReturned(local, assignment.Span.End);

    private bool IsReturnedDeclaration(VariableDeclaratorSyntax variable) =>
        variable.Initializer is { } initializer && CarriesException(initializer.Value) &&
        semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol local &&
        returnedLocals.Value.IsReturned(local, variable.Span.End);

    private bool HasInvokedCallback() => observation.ActiveNodes.OfType<InvocationExpressionSyntax>().Any(invocation =>
        semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } method &&
        invocation.ArgumentList.Arguments.Any(argument => IsCaughtException(argument.Expression)) &&
        (method.ReturnsVoid || invocation.Ancestors().TakeWhile(node => node != observation.Clause)
            .Any(node => node is AwaitExpressionSyntax)));

    private bool IsCaughtException(ExpressionSyntax expression) =>
        Unwrap(expression) is IdentifierNameSyntax identifier &&
        SymbolEqualityComparer.Default.Equals(semanticModel.GetSymbolInfo(identifier).Symbol, caughtSymbol);

    private bool CarriesException(ExpressionSyntax expression)
    {
        expression = Unwrap(expression);
        if (IsCaughtException(expression))
            return true;

        // Follow returned values and exception-bearing arguments, not arbitrary
        // descendant references such as ex.Message.Length or deferred lambdas.
        return expression switch
        {
            AwaitExpressionSyntax awaited => CarriesException(awaited.Expression),
            InvocationExpressionSyntax invocation =>
                semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol { ReturnsVoid: false } &&
                invocation.ArgumentList.Arguments.Any(argument => CarriesException(argument.Expression)),
            BaseObjectCreationExpressionSyntax creation => CreationCarriesException(creation),
            TupleExpressionSyntax tuple => tuple.Arguments.Any(argument => IsCaughtException(argument.Expression)),
            _ => false
        };
    }

    private bool CreationCarriesException(BaseObjectCreationExpressionSyntax creation) =>
        semanticModel.GetSymbolInfo(creation).Symbol is IMethodSymbol &&
        (creation.ArgumentList?.Arguments.Any(argument => IsCaughtException(argument.Expression)) == true ||
         creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
             .Any(assignment => IsCaughtException(assignment.Right)) == true);

    internal static ExpressionSyntax Unwrap(ExpressionSyntax expression) => expression switch
    {
        ParenthesizedExpressionSyntax parenthesized => Unwrap(parenthesized.Expression),
        CastExpressionSyntax cast => Unwrap(cast.Expression),
        PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression)
            => Unwrap(postfix.Operand),
        _ => expression
    };
}
