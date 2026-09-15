using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class CompletedTaskAccess
{
    public static bool IsKnownCompleted(
        SyntaxNode access, ExpressionSyntax receiver, SemanticModel semanticModel)
    {
        if (CompletedTaskReturn.IsCompleted(receiver, semanticModel))
            return true;
        var symbol = semanticModel.GetSymbolInfo(receiver).Symbol;
        return symbol is ILocalSymbol or IParameterSymbol &&
            (HasCompletionGuard(access, symbol, semanticModel) || HasConditionalGuard(access, symbol, semanticModel) ||
             HasSwitchGuard(access, symbol, semanticModel) || HasShortCircuitGuard(access, symbol, semanticModel) ||
             HasPrecedingCompletion(access, symbol, semanticModel));
    }

    private static bool HasShortCircuitGuard(SyntaxNode access, ISymbol receiver, SemanticModel model)
    {
        foreach (var binary in access.Ancestors().TakeWhile(node => !PerformanceFindingContext.IsFunction(node))
                     .OfType<BinaryExpressionSyntax>())
        {
            if (!binary.Right.Span.Contains(access.Span) ||
                model.GetOperation(binary) is not Microsoft.CodeAnalysis.Operations.IBinaryOperation
                { OperatorMethod: null, Type.SpecialType: SpecialType.System_Boolean } ||
                binary.DescendantNodes().Any(node => WritesReceiver(node, receiver, model))) continue;

            var left = Unwrap(binary.Left);
            if (binary.IsKind(SyntaxKind.LogicalAndExpression) && ProvesCompletion(left, receiver, model)) return true;
            if (binary.IsKind(SyntaxKind.LogicalOrExpression) &&
                left is PrefixUnaryExpressionSyntax negative && negative.IsKind(SyntaxKind.LogicalNotExpression) &&
                ProvesCompletion(negative.Operand, receiver, model)) return true;
        }
        return false;
    }

    private static bool HasSwitchGuard(SyntaxNode access, ISymbol receiver, SemanticModel model)
    {
        foreach (var ancestor in access.Ancestors().TakeWhile(node => !PerformanceFindingContext.IsFunction(node)))
        {
            if (ancestor is SwitchSectionSyntax section && section.Parent is SwitchStatementSyntax statement &&
                !statement.DescendantNodes().OfType<GotoStatementSyntax>().Any() &&
                !statement.DescendantNodes().Any(node => WritesReceiver(node, receiver, model)) &&
                section.Labels.All(label => LabelProvesCompletion(label, statement.Expression, receiver, model))) return true;
            if (ancestor is SwitchExpressionArmSyntax arm && arm.Parent is SwitchExpressionSyntax expression &&
                !expression.DescendantNodes().Any(node => WritesReceiver(node, receiver, model)) &&
                PatternProvesCompletion(arm.Pattern, expression.GoverningExpression, receiver, model)) return true;
        }
        return false;
    }

    private static bool LabelProvesCompletion(SwitchLabelSyntax label, ExpressionSyntax value, ISymbol receiver, SemanticModel model) =>
        label switch
        {
            CaseSwitchLabelSyntax constant => IsCompletedStatus(Unwrap(value), constant.Value, receiver, model),
            CasePatternSwitchLabelSyntax pattern => PatternProvesCompletion(pattern.Pattern, value, receiver, model),
            _ => false
        };

    private static bool PatternProvesCompletion(PatternSyntax pattern, ExpressionSyntax value, ISymbol receiver, SemanticModel model) =>
        pattern switch
        {
            ConstantPatternSyntax constant => IsCompletedStatus(Unwrap(value), constant.Expression, receiver, model),
            ParenthesizedPatternSyntax parentheses => PatternProvesCompletion(parentheses.Pattern, value, receiver, model),
            BinaryPatternSyntax binary when binary.IsKind(SyntaxKind.OrPattern) =>
                PatternProvesCompletion(binary.Left, value, receiver, model) && PatternProvesCompletion(binary.Right, value, receiver, model),
            _ => false
        };

    private static bool HasConditionalGuard(SyntaxNode access, ISymbol receiver, SemanticModel model)
    {
        foreach (var conditional in access.Ancestors().OfType<ConditionalExpressionSyntax>())
        {
            if (access.Ancestors().TakeWhile(node => node != conditional)
                .Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                continue;
            var condition = Unwrap(conditional.Condition);
            var negative = condition is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.LogicalNotExpression);
            var tested = negative ? ((PrefixUnaryExpressionSyntax)condition).Operand : condition;
            var branch = negative ? conditional.WhenFalse : conditional.WhenTrue;
            if (branch.Span.Contains(access.Span) && ProvesCompletion(tested, receiver, model) &&
                !condition.DescendantNodesAndSelf().Any(node => WritesReceiver(node, receiver, model)) &&
                !branch.DescendantNodesAndSelf().Where(node => node.SpanStart < access.SpanStart)
                    .Any(node => WritesReceiver(node, receiver, model)))
                return true;
        }
        return false;
    }

    private static bool HasCompletionGuard(SyntaxNode access, ISymbol receiver, SemanticModel model)
    {
        return access.Ancestors().OfType<IfStatementSyntax>().Any(guard =>
            GuardAppliesToAccess(guard, access) &&
            !guard.Condition.DescendantNodesAndSelf().Any(node => WritesReceiver(node, receiver, model)) &&
            ProvesCompletion(guard.Condition, receiver, model) &&
            !guard.Statement.DescendantNodesAndSelf().Where(node => node.SpanStart < access.SpanStart)
                .Any(node => WritesReceiver(node, receiver, model)));
    }

    private static bool GuardAppliesToAccess(IfStatementSyntax guard, SyntaxNode access) =>
        !access.Ancestors().TakeWhile(node => node != guard)
            .Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax) &&
        guard.Statement.Span.Contains(access.Span);

    private static bool HasPrecedingCompletion(SyntaxNode access, ISymbol receiver, SemanticModel model)
    {
        var statement = access.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (statement?.Parent is not BlockSyntax block)
            return false;
        if (access.Ancestors().TakeWhile(node => node != statement)
            .Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            return false;
        if (statement.DescendantNodes().Where(node => node.SpanStart < access.SpanStart)
            .Any(node => WritesReceiver(node, receiver, model)))
            return false;

        // Search backwards: a write invalidates earlier completion evidence, while
        // assignment of an awaited WhenAny result establishes a new completed value.
        for (var index = block.Statements.IndexOf(statement) - 1; index >= 0; index--)
        {
            var preceding = block.Statements[index];
            if (AssignsAwaitedWhenAny(preceding, receiver, model))
                return true;
            if (WritesSymbol(preceding, receiver, model))
                return false;
            if (AwaitsWhenAll(preceding, receiver, model))
                return true;
            if (ExitsUnlessCompleted(preceding, receiver, model))
                return true;
            if (WaitCompleted(preceding, receiver, model))
                return true;
        }
        return false;
    }

    private static bool ExitsUnlessCompleted(StatementSyntax statement, ISymbol receiver, SemanticModel model)
    {
        if (statement is not IfStatementSyntax { Else: null } guard ||
            Unwrap(guard.Condition) is not PrefixUnaryExpressionSyntax negative ||
            !negative.IsKind(SyntaxKind.LogicalNotExpression) ||
            !ProvesCompletion(negative.Operand, receiver, model))
            return false;
        // Only an unconditional return/throw from this branch establishes the fast path.
        var terminal = guard.Statement is BlockSyntax block ? block.Statements.LastOrDefault() : guard.Statement;
        return terminal is ReturnStatementSyntax or ThrowStatementSyntax;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression) =>
        expression is ParenthesizedExpressionSyntax parentheses ? Unwrap(parentheses.Expression) : expression;

    private static bool WaitCompleted(StatementSyntax statement, ISymbol receiver, SemanticModel model)
    {
        if (statement is TryStatementSyntax attempt)
        {
            // A handled failed wait cannot prove completion. Only catches that leave the
            // current flow allow the following result read to rely on a successful wait.
            return attempt.Block.Statements.LastOrDefault() is { } last &&
                WaitCompleted(last, receiver, model) && attempt.Catches.All(clause =>
                    ExitsFlow(clause.Block.Statements.LastOrDefault(), model));
        }
        if (statement is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } ||
            invocation.Expression is not MemberAccessExpressionSyntax member ||
            !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(member.Expression).Symbol, receiver) ||
            model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return false;
        // Void Wait() / Wait(CancellationToken) complete or throw. Timed waits return
        // bool and do not establish completion merely because the call returned.
        return method is { Name: "Wait", ReturnsVoid: true } &&
            method.ContainingType.ToDisplayString() == "System.Threading.Tasks.Task";
    }

    private static bool ExitsFlow(StatementSyntax? statement, SemanticModel model) => statement switch
    {
        ReturnStatementSyntax or ThrowStatementSyntax => true,
        ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation } =>
            model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "Throw" } method &&
            method.ContainingType.ToDisplayString() == "System.Runtime.ExceptionServices.ExceptionDispatchInfo",
        _ => false
    };

    private static bool ProvesCompletion(ExpressionSyntax expression, ISymbol receiver, SemanticModel model)
    {
        return expression switch
        {
            ParenthesizedExpressionSyntax parentheses => ProvesCompletion(parentheses.Expression, receiver, model),
            BinaryExpressionSyntax binary => BinaryProvesCompletion(binary, receiver, model),
            MemberAccessExpressionSyntax member => IsCompletionProperty(member, receiver, model),
            InvocationExpressionSyntax call => HelperProvesCompletion(call, receiver, model),
            _ => false
        };
    }

    private static bool BinaryProvesCompletion(BinaryExpressionSyntax binary, ISymbol receiver, SemanticModel model)
    {
        // Either conjunct can prove completion, but both alternatives of an OR must.
        return binary.Kind() switch
        {
            SyntaxKind.LogicalAndExpression => ProvesCompletion(binary.Left, receiver, model) || ProvesCompletion(binary.Right, receiver, model),
            SyntaxKind.LogicalOrExpression => ProvesCompletion(binary.Left, receiver, model) && ProvesCompletion(binary.Right, receiver, model),
            SyntaxKind.EqualsExpression => IsCompletedStatus(binary.Left, binary.Right, receiver, model) ||
                                           IsCompletedStatus(binary.Right, binary.Left, receiver, model),
            _ => false
        };
    }

    private static bool IsCompletionProperty(MemberAccessExpressionSyntax member, ISymbol receiver, SemanticModel model)
    {
        return TaskPropertyName(member, receiver, model) is "IsCompletedSuccessfully" or "IsCompleted";
    }

    private static string? TaskPropertyName(MemberAccessExpressionSyntax member, ISymbol receiver, SemanticModel model)
    {
        if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(member.Expression).Symbol, receiver) ||
            model.GetSymbolInfo(member).Symbol is not IPropertySymbol property ||
            !TaskTypes.IsTaskLike(property.ContainingType))
            return null;
        return property.Name;
    }

    private static bool HelperProvesCompletion(InvocationExpressionSyntax call, ISymbol receiver, SemanticModel model)
    {
        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol method)
            return false;
        var definition = method.ReducedFrom ?? method;
        if (!definition.IsStatic || definition.Parameters.Length != 1 ||
            definition.ReturnType.SpecialType != SpecialType.System_Boolean)
            return false;
        var argument = HelperArgument(call, method);
        if (argument == null || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(argument).Symbol, receiver))
            return false;
        if (definition.DeclaringSyntaxReferences.Length != 1 ||
            definition.DeclaringSyntaxReferences[0].GetSyntax() is not MethodDeclarationSyntax declaration)
            return false;
        return HelperBodyProvesCompletion(declaration, model);
    }

    private static ExpressionSyntax? HelperArgument(InvocationExpressionSyntax call, IMethodSymbol method)
    {
        return method.ReducedFrom != null && call.Expression is MemberAccessExpressionSyntax extension
            ? extension.Expression : call.ArgumentList.Arguments.FirstOrDefault()?.Expression;
    }

    private static ExpressionSyntax? SingleReturnedExpression(MethodDeclarationSyntax declaration)
    {
        return declaration.ExpressionBody?.Expression ??
               (declaration.Body?.Statements.Count == 1 && declaration.Body.Statements[0] is ReturnStatementSyntax result
                   ? result.Expression : null);
    }

    private static bool HelperBodyProvesCompletion(MethodDeclarationSyntax declaration, SemanticModel model)
    {
        var returned = SingleReturnedExpression(declaration);
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
               TaskPropertyName(status, receiver, model) == "Status" &&
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
        return target != null && target.DescendantNodesAndSelf().Any(candidate =>
            SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(candidate).Symbol, receiver));
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
