using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

/// <summary>Proves completion of immediate task expressions without assuming arbitrary wrappers are safe.</summary>
internal static class CompletedTaskReturn
{
    public static bool IsCompleted(ExpressionSyntax expression, SemanticModel model)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses)
            expression = parentheses.Expression;
        if (IsCompletedFactory(expression, model))
            return true;
        if (expression is not InvocationExpressionSyntax invocation ||
            model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method ||
            method.IsAsync || !TaskTypes.IsTaskLike(method.ReturnType) ||
            ((method.IsVirtual || method.IsOverride || method.IsAbstract) &&
             !method.IsSealed && !method.ContainingType.IsSealed) ||
            method.DeclaringSyntaxReferences.Length != 1 ||
            method.DeclaringSyntaxReferences[0].GetSyntax() is not MethodDeclarationSyntax declaration)
            return false;

        if (!model.Compilation.ContainsSyntaxTree(declaration.SyntaxTree))
            return false;
        var helperModel = model.Compilation.GetSemanticModel(declaration.SyntaxTree);
        if (declaration.ExpressionBody is { } body)
            return IsCompletedFactory(body.Expression, helperModel);
        if (declaration.Body is not { } block ||
            helperModel.AnalyzeControlFlow(block) is not { Succeeded: true, EndPointIsReachable: false })
            return false;
        var returns = block.DescendantNodes(node => node is not AnonymousFunctionExpressionSyntax and
            not LocalFunctionStatementSyntax).OfType<ReturnStatementSyntax>().ToArray();
        // Every normal return must directly supply a completed task. Throws and synchronous
        // work before return do not create an incomplete task; nested returns are unrelated.
        return returns.Length > 0 && returns.All(result =>
            result.Expression is { } value && IsCompletedFactory(value, helperModel));
    }

    private static bool IsCompletedFactory(ExpressionSyntax expression, SemanticModel model)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses)
            expression = parentheses.Expression;
        return model.GetSymbolInfo(expression).Symbol switch
        {
            IPropertySymbol { IsStatic: true, Name: "CompletedTask" } property =>
                property.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task",
            IMethodSymbol { IsStatic: true, Name: "FromResult" } method when expression is InvocationExpressionSyntax =>
                method.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task",
            _ => false
        };
    }
}
