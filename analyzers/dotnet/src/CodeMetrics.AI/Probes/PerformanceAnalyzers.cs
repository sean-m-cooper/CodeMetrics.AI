using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class SyncOverAsyncAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var ma in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var memberName = ma.Name.Identifier.Text;
            if (memberName == "Result")
                AddFinding(ma, "'.Result' blocks the calling thread synchronously. Use 'await' instead.", filePath, projectName, findings);
            else if (IsGetAwaiterGetResult(ma))
                AddFinding(ma, "'.GetAwaiter().GetResult()' blocks the calling thread synchronously. Use 'await' instead.", filePath, projectName, findings);
        }

        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.Text == "Wait")
                AddFinding(inv, "'.Wait()' blocks the calling thread synchronously. Use 'await' instead.", filePath, projectName, findings);
        }
    }

    private static bool IsGetAwaiterGetResult(MemberAccessExpressionSyntax ma)
    {
        return ma.Name.Identifier.Text == "GetResult" &&
               ma.Expression is InvocationExpressionSyntax inv &&
               inv.Expression is MemberAccessExpressionSyntax innerMa &&
               innerMa.Name.Identifier.Text == "GetAwaiter";
    }

    private static void AddFinding(SyntaxNode node, string message, string filePath, string projectName, List<Finding> findings)
    {
        findings.Add(new Finding
        {
            Category = "syncOverAsync",
            Severity = PerformanceSyntaxHelpers.SyncOverAsyncSeverity(node),
            File = filePath,
            Line = PerformanceSyntaxHelpers.GetLine(node),
            Project = projectName,
            Type = PerformanceSyntaxHelpers.GetContainingTypeName(node),
            Message = message
        });
    }
}

internal static class BlockingCallAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Expression is IdentifierNameSyntax id &&
                id.Identifier.Text == "Thread" &&
                ma.Name.Identifier.Text == "Sleep")
            {
                findings.Add(new Finding
                {
                    Category = "threadSleep",
                    Severity = "warning",
                    File = filePath,
                    Line = PerformanceSyntaxHelpers.GetLine(inv),
                    Project = projectName,
                    Type = PerformanceSyntaxHelpers.GetContainingTypeName(inv),
                    Message = "'Thread.Sleep' blocks the thread. Use 'await Task.Delay' instead."
                });
            }
        }
    }
}

internal static class EfLoopAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma)
                continue;

            var memberName = ma.Name.Identifier.Text;
            if ((memberName == "SaveChanges" || memberName == "SaveChangesAsync") &&
                PerformanceSyntaxHelpers.IsInsideLoop(inv))
            {
                findings.Add(new Finding
                {
                    Category = "saveChangesInsideLoop",
                    Severity = "error",
                    File = filePath,
                    Line = PerformanceSyntaxHelpers.GetLine(inv),
                    Project = projectName,
                    Type = PerformanceSyntaxHelpers.GetContainingTypeName(inv),
                    Message = $"'{memberName}' called inside a loop. Batch changes and call once outside the loop."
                });
            }
        }
    }
}

internal static class CancellationTokenAnalyzer
{
    private static readonly string[] AsyncIoSuffixes =
    [
        "SaveAsync", "GetAsync", "ReadAsync", "WriteAsync",
        "SendAsync", "PostAsync", "PutAsync", "DeleteAsync",
        "ExecuteAsync"
    ];

    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!RequiresCancellationToken(method))
                continue;

            findings.Add(new Finding
            {
                Category = "missingCancellationToken",
                Severity = "warning",
                File = filePath,
                Line = PerformanceSyntaxHelpers.GetLine(method),
                Project = projectName,
                Type = PerformanceSyntaxHelpers.GetContainingTypeName(method),
                Message = $"Method '{method.Identifier.Text}' performs async I/O but has no CancellationToken parameter."
            });
        }
    }

    private static bool RequiresCancellationToken(MethodDeclarationSyntax method)
    {
        return method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)) &&
               !IsAspNetCoreMiddlewareInvokeAsync(method) &&
               HasAsyncShape(method) &&
               HasAsyncIoCalls(method) &&
               !HasCancellationToken(method);
    }

    private static bool HasAsyncShape(MethodDeclarationSyntax method)
    {
        return method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)) ||
               IsTaskReturnType(method.ReturnType) ||
               method.Identifier.Text.EndsWith("Async", StringComparison.Ordinal);
    }

    private static bool HasCancellationToken(MethodDeclarationSyntax method)
    {
        return method.ParameterList.Parameters
            .Any(p => p.Type?.ToString().Contains("CancellationToken") == true);
    }

    private static bool IsAspNetCoreMiddlewareInvokeAsync(MethodDeclarationSyntax method)
    {
        if (method.Identifier.Text != "InvokeAsync" || method.ParameterList.Parameters.Count != 1)
            return false;

        var typeName = method.ParameterList.Parameters[0].Type?.ToString();
        return typeName == "HttpContext" ||
               typeName?.EndsWith(".HttpContext", StringComparison.Ordinal) == true;
    }

    private static bool IsTaskReturnType(TypeSyntax returnType)
    {
        var typeStr = returnType.ToString();
        return typeStr == "Task" ||
               typeStr.StartsWith("Task<", StringComparison.Ordinal) ||
               typeStr == "ValueTask" ||
               typeStr.StartsWith("ValueTask<", StringComparison.Ordinal);
    }

    private static bool HasAsyncIoCalls(MethodDeclarationSyntax method)
    {
        return method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(GetInvocationName)
            .Where(name => name != null)
            .Any(name => AsyncIoSuffixes.Any(suffix => name!.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? GetInvocationName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
    }
}

internal static class QueryMaterializationAnalyzer
{
    private static readonly HashSet<string> QueryShapingMethods = new(StringComparer.Ordinal)
    {
        "Where", "OrderBy", "OrderByDescending", "Skip", "Take"
    };

    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!IsMaterializedBeforeQueryShape(inv, out var methodName))
                continue;

            findings.Add(new Finding
            {
                Category = "materializationBeforeQueryShape",
                Severity = "warning",
                File = filePath,
                Line = PerformanceSyntaxHelpers.GetLine(inv),
                Project = projectName,
                Type = PerformanceSyntaxHelpers.GetContainingTypeName(inv),
                Message = $"'.ToList()' called before '.{methodName}()' forces in-memory evaluation. Apply query operators before materializing."
            });
        }
    }

    private static bool IsMaterializedBeforeQueryShape(InvocationExpressionSyntax invocation, out string methodName)
    {
        methodName = "";
        if (invocation.Expression is not MemberAccessExpressionSyntax outerMa)
            return false;

        methodName = outerMa.Name.Identifier.Text;
        return QueryShapingMethods.Contains(methodName) &&
               outerMa.Expression is InvocationExpressionSyntax innerInv &&
               innerInv.Expression is MemberAccessExpressionSyntax innerMa &&
               innerMa.Name.Identifier.Text == "ToList";
    }
}

internal static class AwaitedIoLoopAnalyzer
{
    private static readonly HashSet<string> IoVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get", "Read", "Write", "Send", "Post", "Put", "Delete", "Execute", "Query", "Fetch"
    };

    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var awaitExpr in root.DescendantNodes().OfType<AwaitExpressionSyntax>())
        {
            if (!IsFlaggableAwait(awaitExpr, out var methodName))
                continue;

            findings.Add(new Finding
            {
                Category = "awaitedIoInsideLoop",
                Severity = "warning",
                File = filePath,
                Line = PerformanceSyntaxHelpers.GetLine(awaitExpr),
                Project = projectName,
                Type = PerformanceSyntaxHelpers.GetContainingTypeName(awaitExpr),
                Message = $"'await {methodName}(...)' inside a loop causes sequential I/O. Consider batching or using Task.WhenAll."
            });
        }
    }

    private static bool IsFlaggableAwait(AwaitExpressionSyntax awaitExpr, out string methodName)
    {
        var awaitedMethodName = GetAwaitedMethodName(awaitExpr) ?? "";
        methodName = awaitedMethodName;
        return PerformanceSyntaxHelpers.IsInsideLoop(awaitExpr) &&
               !HasSuppression(awaitExpr) &&
               awaitedMethodName.EndsWith("Async", StringComparison.Ordinal) &&
               awaitedMethodName != "SaveChangesAsync" &&
               !IsCursorPaginationLoop(awaitExpr) &&
               IoVerbs.Any(verb => awaitedMethodName.IndexOf(verb, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool HasSuppression(AwaitExpressionSyntax awaitExpr)
    {
        var method = awaitExpr.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        return method != null && PerformanceSyntaxHelpers.HasSyncRequiredSuppression(method);
    }

    private static string? GetAwaitedMethodName(AwaitExpressionSyntax awaitExpr)
    {
        if (awaitExpr.Expression is not InvocationExpressionSyntax inv)
            return null;

        return inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
    }

    private static bool IsCursorPaginationLoop(AwaitExpressionSyntax awaitExpr)
    {
        var resultVariable = GetAwaitedResultVariable(awaitExpr);
        var loop = awaitExpr.Ancestors().FirstOrDefault(PerformanceSyntaxHelpers.IsLoopNode);

        return resultVariable != null &&
               loop != null &&
               HasPaginationTokenAssignment(loop, resultVariable);
    }

    private static bool HasPaginationTokenAssignment(SyntaxNode loop, string resultVariable)
    {
        var tokenNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "NextToken", "ContinuationToken", "NextPageToken", "PageToken", "Cursor"
        };

        return loop.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Any(assignment =>
                assignment.Right is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression is IdentifierNameSyntax id &&
                id.Identifier.Text == resultVariable &&
                tokenNames.Contains(memberAccess.Name.Identifier.Text));
    }

    private static string? GetAwaitedResultVariable(AwaitExpressionSyntax awaitExpr)
    {
        var variable = awaitExpr.Ancestors()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Initializer?.Value == awaitExpr);

        return variable?.Identifier.Text;
    }
}

internal static class WhenAllAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!IsUnboundedWhenAll(inv))
                continue;

            findings.Add(new Finding
            {
                Category = "unboundedWhenAll",
                Severity = "info",
                File = filePath,
                Line = PerformanceSyntaxHelpers.GetLine(inv),
                Project = projectName,
                Type = PerformanceSyntaxHelpers.GetContainingTypeName(inv),
                Message = "Task.WhenAll called with an unbounded sequence. Consider using a bounded collection to limit concurrency."
            });
        }
    }

    private static bool IsUnboundedWhenAll(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax ma ||
            ma.Name.Identifier.Text != "WhenAll" ||
            ma.Expression is not IdentifierNameSyntax id ||
            id.Identifier.Text != "Task")
        {
            return false;
        }

        var args = invocation.ArgumentList.Arguments;
        return args.Count == 1 && !IsMaterializedCollection(args[0].Expression);
    }

    private static bool IsMaterializedCollection(ExpressionSyntax expression)
    {
        if (expression is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax)
            return true;

        return expression is InvocationExpressionSyntax argInv &&
               argInv.Expression is MemberAccessExpressionSyntax argMa &&
               argMa.Name.Identifier.Text is "ToList" or "ToArray";
    }
}

internal static class PerformanceSyntaxHelpers
{
    public static bool IsInsideLoop(SyntaxNode node)
    {
        return node.Ancestors().Any(IsLoopNode);
    }

    public static bool IsLoopNode(SyntaxNode node)
    {
        return node is ForStatementSyntax or
            ForEachStatementSyntax or
            WhileStatementSyntax or
            DoStatementSyntax;
    }

    public static string SyncOverAsyncSeverity(SyntaxNode node)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method == null)
            return "error";

        return HasSyncRequiredSuppression(method) ||
               IsOverrideOrExplicitInterfaceImplementation(method)
            ? "info"
            : "error";
    }

    public static bool HasSyncRequiredSuppression(MethodDeclarationSyntax method)
    {
        return method.GetLeadingTrivia()
            .Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
                           trivia.ToString().Contains("amp-metrics: sync-required", StringComparison.OrdinalIgnoreCase));
    }

    public static string? GetContainingTypeName(SyntaxNode node)
    {
        return node.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.Text;
    }

    public static int GetLine(SyntaxNode node)
    {
        return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }

    private static bool IsOverrideOrExplicitInterfaceImplementation(MethodDeclarationSyntax method)
    {
        return method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)) ||
               method.ExplicitInterfaceSpecifier != null;
    }
}
