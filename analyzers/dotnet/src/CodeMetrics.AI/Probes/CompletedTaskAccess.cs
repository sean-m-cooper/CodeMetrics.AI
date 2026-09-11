using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

        foreach (var guard in access.Ancestors().OfType<IfStatementSyntax>())
        {
            if (!access.Ancestors().TakeWhile(node => node != guard).Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) &&
                guard.Statement.Span.Contains(access.Span) &&
                !guard.Condition.DescendantNodesAndSelf().Any(node => WritesReceiver(node, receiverSymbol, semanticModel)) &&
                ProvesCompletion(guard.Condition, receiverSymbol, semanticModel) &&
                !guard.Statement.DescendantNodesAndSelf().Where(node => node.SpanStart < access.SpanStart)
                    .Any(node => WritesReceiver(node, receiverSymbol, semanticModel)))
                return true;
        }

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

    private static bool ProvesCompletion(ExpressionSyntax expression, ISymbol receiver, SemanticModel model)
    {
        if (expression is ParenthesizedExpressionSyntax parentheses)
            return ProvesCompletion(parentheses.Expression, receiver, model);
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.LogicalAndExpression))
            return ProvesCompletion(binary.Left, receiver, model) || ProvesCompletion(binary.Right, receiver, model);
        if (expression is BinaryExpressionSyntax either && either.IsKind(SyntaxKind.LogicalOrExpression))
            return ProvesCompletion(either.Left, receiver, model) && ProvesCompletion(either.Right, receiver, model);
        if (expression is MemberAccessExpressionSyntax member &&
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(member.Expression).Symbol, receiver) &&
            model.GetSymbolInfo(member).Symbol is IPropertySymbol property &&
            property.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task" &&
            property.Name is "IsCompletedSuccessfully" or "IsCompleted")
            return true;
        if (expression is BinaryExpressionSyntax equality && equality.IsKind(SyntaxKind.EqualsExpression))
        {
            return IsCompletedStatus(equality.Left, equality.Right, receiver, model) ||
                   IsCompletedStatus(equality.Right, equality.Left, receiver, model);
        }
        if (expression is not InvocationExpressionSyntax call ||
            model.GetSymbolInfo(call).Symbol is not IMethodSymbol method)
            return false;
        var definition = method.ReducedFrom ?? method;
        if (!definition.IsStatic || definition.Parameters.Length != 1 ||
            definition.ReturnType.SpecialType != SpecialType.System_Boolean)
            return false;
        var argument = method.ReducedFrom != null && call.Expression is MemberAccessExpressionSyntax extension
            ? extension.Expression : call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (argument == null || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(argument).Symbol, receiver))
            return false;
        if (definition.DeclaringSyntaxReferences.Length != 1 ||
            definition.DeclaringSyntaxReferences[0].GetSyntax() is not MethodDeclarationSyntax declaration)
            return false;
        var returned = declaration.ExpressionBody?.Expression ??
            (declaration.Body?.Statements.Count == 1 && declaration.Body.Statements[0] is ReturnStatementSyntax result
                ? result.Expression : null);
        // Do not recursively trust wrappers or names: the helper must directly prove the
        // completed state of its parameter without preceding side effects.
        if (returned == null || returned.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any())
            return false;
        var helperModel = model.Compilation.GetSemanticModel(declaration.SyntaxTree);
        var parameter = helperModel.GetDeclaredSymbol(declaration.ParameterList.Parameters[0]);
        return parameter != null &&
               !returned.DescendantNodesAndSelf().Any(node => WritesReceiver(node, parameter, helperModel)) &&
               ProvesCompletion(returned, parameter, helperModel);
    }

    private static bool IsCompletedStatus(ExpressionSyntax left, ExpressionSyntax right, ISymbol receiver, SemanticModel model)
    {
        return left is MemberAccessExpressionSyntax status &&
               SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(status.Expression).Symbol, receiver) &&
               model.GetSymbolInfo(status).Symbol is IPropertySymbol property &&
               property.Name == "Status" && property.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task" &&
               model.GetSymbolInfo(right).Symbol is IFieldSymbol field &&
               field.ContainingType.ToDisplayString() == "System.Threading.Tasks.TaskStatus" &&
               field.Name is "RanToCompletion" or "Faulted" or "Canceled";
    }

    private static bool WritesReceiver(SyntaxNode node, ISymbol receiver, SemanticModel model)
    {
        var target = node switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left,
            ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) => argument.Expression,
            _ => null
        };
        return target != null && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(target).Symbol, receiver);
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
