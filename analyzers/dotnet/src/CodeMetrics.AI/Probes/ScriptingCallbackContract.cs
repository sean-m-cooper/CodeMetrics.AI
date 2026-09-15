using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

/// <summary>Recognizes the synchronous delegate returned by a cataloged scripting API factory.</summary>
internal static class ScriptingCallbackContract
{
    public static bool IsRecognized(AnonymousFunctionExpressionSyntax callback, SemanticModel model)
    {
        if (!callback.AsyncKeyword.IsKind(SyntaxKind.None) ||
            model.GetTypeInfo(callback).ConvertedType is not INamedTypeSymbol { DelegateInvokeMethod: { } invoke } ||
            SynchronousBoundaryContext.IsAwaitable(invoke.ReturnType)) return false;

        // A directly returned delegate is the script entry point. Arbitrary nested lambdas,
        // statement-bodied factories and arbitrary properties do not establish this boundary.
        var returned = OutsideConversions(callback);
        if (returned.Parent is not LambdaExpressionSyntax factory || factory.Body != returned ||
            !factory.AsyncKeyword.IsKind(SyntaxKind.None)) return false;
        var value = OutsideConversions(factory);
        if (value.Parent is not AssignmentExpressionSyntax assignment || assignment.Right != value ||
            assignment.Parent is not InitializerExpressionSyntax initializer || !initializer.IsKind(SyntaxKind.ObjectInitializerExpression) ||
            model.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol { IsStatic: false, Name: "Method" } property)
            return false;

        return property.ContainingType.ToDisplayString() == "OrchardCore.Scripting.GlobalMethod" &&
            property.Type is INamedTypeSymbol { DelegateInvokeMethod: { Parameters.Length: 1 } creator } &&
            creator.Parameters[0].Type.ToDisplayString() == "System.IServiceProvider" &&
            creator.ReturnType.ToDisplayString() == "System.Delegate";
    }

    private static ExpressionSyntax OutsideConversions(ExpressionSyntax expression)
    {
        while (true)
        {
            if (expression.Parent is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized;
            else if (expression.Parent is CastExpressionSyntax cast) expression = cast;
            else return expression;
        }
    }
}
