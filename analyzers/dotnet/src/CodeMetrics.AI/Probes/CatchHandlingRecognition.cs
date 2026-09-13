using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodeMetrics.AI.Probes;

internal static class CatchHandlingRecognition
{
    // These are recognized handling paths, not proof of handling on every path.
    public static bool IsHandled(CatchObservation observation) =>
        CatchIntentRecognition.HasExplanation(observation.Clause) ||
        CatchIntentRecognition.HasErrorOutput(observation) ||
        HasLoggingCall(observation) || observation.ActiveNodes.OfType<ThrowStatementSyntax>().Any() ||
        HasPrecedingCancellationRethrow(observation.Clause) || HasDeferredLogging(observation) ||
        ExceptionPropagationAnalysis.Recognizes(observation);

    private static bool HasLoggingCall(CatchObservation observation)
    {
        return observation.ActiveNodes
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => IsLoggingCall(invocation) || IsStandardErrorWrite(invocation, observation.SemanticModel));
    }

    private static bool IsStandardErrorWrite(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Resolve both the receiver and method: a lookalike Console.Error or an
        // arbitrary TextWriter is not evidence of reporting to standard error.
        if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation ||
            operation.TargetMethod.Name is not ("Write" or "WriteLine") ||
            operation.Arguments.Length == 0)
            return false;

        var receiver = operation.Instance;
        while (receiver is IConversionOperation conversion)
            receiver = conversion.Operand;

        return receiver is IPropertyReferenceOperation { Property.Name: "Error" } property &&
            SymbolEqualityComparer.Default.Equals(property.Property.ContainingType,
                semanticModel.Compilation.GetTypeByMetadataName("System.Console")) &&
            SymbolEqualityComparer.Default.Equals(operation.TargetMethod.ContainingType,
                semanticModel.Compilation.GetTypeByMetadataName("System.IO.TextWriter"));
    }

    private static bool HasDeferredLogging(
        CatchObservation observation)
    {
        var catchClause = observation.Clause;
        var semanticModel = observation.SemanticModel;
        var caughtSymbol = observation.CaughtSymbol;

        if (caughtSymbol == null)
            return false;

        var capturedSymbols = observation.ActiveNodes
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment =>
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(assignment.Right).Symbol,
                    caughtSymbol))
            .Select(assignment => semanticModel.GetSymbolInfo(assignment.Left).Symbol)
            .Where(symbol => symbol != null)
            .Cast<ISymbol>()
            .ToList();

        if (capturedSymbols.Count == 0)
            return false;

        var containingScope = catchClause.Ancestors().FirstOrDefault(node =>
            node is BaseMethodDeclarationSyntax or
                AccessorDeclarationSyntax or
                LocalFunctionStatementSyntax or
                AnonymousFunctionExpressionSyntax);

        if (containingScope == null)
            return false;

        // Preserve the existing deferred-logging recognition, including callbacks
        // registered after the catch with the captured exception.
        return containingScope.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation =>
                invocation.SpanStart > catchClause.Span.End &&
                IsLoggingCall(invocation))
            .SelectMany(invocation => invocation.ArgumentList.Arguments)
            .SelectMany(argument => argument.Expression.DescendantNodesAndSelf())
            .OfType<ExpressionSyntax>()
            .Select(expression => semanticModel.GetSymbolInfo(expression).Symbol)
            .Any(symbol => capturedSymbols.Any(captured =>
                SymbolEqualityComparer.Default.Equals(symbol, captured)));
    }

    private static bool IsLoggingCall(InvocationExpressionSyntax invocation)
    {
        var text = invocation.Expression.ToString();
        return text.IndexOf("Log", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Any active throw propagates; throw-ex remains a separate stack-trace finding.
    private static bool HasRethrow(BlockSyntax block)
    {
        return block.DescendantNodes(CatchObservation.ShouldDescend).OfType<ThrowStatementSyntax>().Any();
    }

    private static bool HasPrecedingCancellationRethrow(CatchClauseSyntax catchClause)
    {
        if (catchClause.Parent is not TryStatementSyntax tryStatement)
            return false;

        foreach (var preceding in tryStatement.Catches)
        {
            if (preceding == catchClause)
                break;

            if (IsCancellationCatch(preceding) && HasRethrow(preceding.Block))
                return true;
        }

        return false;
    }

    private static bool IsCancellationCatch(CatchClauseSyntax catchClause)
    {
        // A 'when' filter means the clause may decline the exception, so it cannot be
        // relied on to propagate cancellation.
        if (catchClause.Filter != null)
            return false;

        var typeName = catchClause.Declaration?.Type.ToString();
        if (typeName == null)
            return false;

        // Strip any namespace qualifier: System.OperationCanceledException → OperationCanceledException.
        var simpleName = typeName[(typeName.LastIndexOf('.') + 1)..];
        return simpleName is "OperationCanceledException" or "TaskCanceledException";
    }
}
