using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

public static class PerformanceAsyncProbe
{
    public static DimensionResult Analyze(IReadOnlyList<(string ProjectName, Compilation Compilation)> projects)
    {
        var findings = new List<Finding>();

        foreach (var (projectName, compilation) in projects)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var root = tree.GetRoot();
                var filePath = tree.FilePath;

                AnalyzeSyncOverAsync(root, filePath, projectName, findings);
                AnalyzeThreadSleep(root, filePath, projectName, findings);
                AnalyzeSaveChangesInsideLoop(root, filePath, projectName, findings);
                AnalyzeMissingCancellationToken(root, filePath, projectName, findings);
                AnalyzeMaterializationBeforeQueryShape(root, filePath, projectName, findings);
                AnalyzeAwaitedIoInsideLoop(root, filePath, projectName, findings);
                AnalyzeUnboundedWhenAll(root, filePath, projectName, findings);
            }
        }

        var errors = findings.Count(f => f.Severity == "error");
        var warnings = findings.Count(f => f.Severity == "warning");
        var hasSyncOverAsync = findings.Any(f => f.Category == "syncOverAsync");
        var hasSaveChangesInsideLoop = findings.Any(f => f.Category == "saveChangesInsideLoop");

        double score;
        if (errors >= 5)
            score = 0;
        else if (hasSyncOverAsync || hasSaveChangesInsideLoop || errors > 0)
            score = 2;
        else if (warnings > 3)
            score = 4;
        else if (warnings > 0)
            score = 6;
        else
            score = 10;

        var basis = $"Findings: {findings.Count} (errors: {errors}, warnings: {warnings}). " +
                    $"syncOverAsync={findings.Count(f => f.Category == "syncOverAsync")}, " +
                    $"threadSleep={findings.Count(f => f.Category == "threadSleep")}, " +
                    $"saveChangesInsideLoop={findings.Count(f => f.Category == "saveChangesInsideLoop")}, " +
                    $"missingCancellationToken={findings.Count(f => f.Category == "missingCancellationToken")}, " +
                    $"materializationBeforeQueryShape={findings.Count(f => f.Category == "materializationBeforeQueryShape")}, " +
                    $"awaitedIoInsideLoop={findings.Count(f => f.Category == "awaitedIoInsideLoop")}, " +
                    $"unboundedWhenAll={findings.Count(f => f.Category == "unboundedWhenAll")}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = score,
            Basis = basis,
            Findings = findings
        };
    }

    // 1. syncOverAsync: .Result, .Wait(), .GetAwaiter().GetResult()
    private static void AnalyzeSyncOverAsync(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

        foreach (var ma in memberAccesses)
        {
            var memberName = ma.Name.Identifier.Text;

            if (memberName == "Result")
            {
                findings.Add(new Finding
                {
                    Category = "syncOverAsync",
                    Severity = "error",
                    File = filePath,
                    Line = GetLine(ma),
                    Project = projectName,
                    Type = GetContainingTypeName(ma),
                    Message = "'.Result' blocks the calling thread synchronously. Use 'await' instead."
                });
            }
            else if (memberName == "GetResult")
            {
                if (ma.Expression is InvocationExpressionSyntax inv &&
                    inv.Expression is MemberAccessExpressionSyntax innerMa &&
                    innerMa.Name.Identifier.Text == "GetAwaiter")
                {
                    findings.Add(new Finding
                    {
                        Category = "syncOverAsync",
                        Severity = "error",
                        File = filePath,
                        Line = GetLine(ma),
                        Project = projectName,
                        Type = GetContainingTypeName(ma),
                        Message = "'.GetAwaiter().GetResult()' blocks the calling thread synchronously. Use 'await' instead."
                    });
                }
            }
        }

        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma2 &&
                ma2.Name.Identifier.Text == "Wait")
            {
                findings.Add(new Finding
                {
                    Category = "syncOverAsync",
                    Severity = "error",
                    File = filePath,
                    Line = GetLine(inv),
                    Project = projectName,
                    Type = GetContainingTypeName(inv),
                    Message = "'.Wait()' blocks the calling thread synchronously. Use 'await' instead."
                });
            }
        }
    }

    // 2. threadSleep: Thread.Sleep(...)
    private static void AnalyzeThreadSleep(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
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
                    Line = GetLine(inv),
                    Project = projectName,
                    Type = GetContainingTypeName(inv),
                    Message = "'Thread.Sleep' blocks the thread. Use 'await Task.Delay' instead."
                });
            }
        }
    }

    // 3. saveChangesInsideLoop: SaveChanges()/SaveChangesAsync() inside for/foreach/while/do
    private static void AnalyzeSaveChangesInsideLoop(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma)
            {
                var memberName = ma.Name.Identifier.Text;
                if (memberName == "SaveChanges" || memberName == "SaveChangesAsync")
                {
                    if (IsInsideLoop(inv))
                    {
                        findings.Add(new Finding
                        {
                            Category = "saveChangesInsideLoop",
                            Severity = "error",
                            File = filePath,
                            Line = GetLine(inv),
                            Project = projectName,
                            Type = GetContainingTypeName(inv),
                            Message = $"'{memberName}' called inside a loop. Batch changes and call once outside the loop."
                        });
                    }
                }
            }
        }
    }

    // 4. missingCancellationToken: public async/Task-returning/*Async method with async I/O but no CancellationToken
    private static void AnalyzeMissingCancellationToken(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var method in methods)
        {
            // Must be public
            if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
                continue;

            // Must be async OR return Task/Task<T> OR have *Async suffix
            bool isAsync = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword));
            bool returnsTask = IsTaskReturnType(method.ReturnType);
            bool hasAsyncSuffix = method.Identifier.Text.EndsWith("Async", StringComparison.Ordinal);

            if (!isAsync && !returnsTask && !hasAsyncSuffix)
                continue;

            // Must contain async I/O-like calls
            bool hasAsyncIo = HasAsyncIoCalls(method);
            if (!hasAsyncIo)
                continue;

            // Must NOT already have a CancellationToken parameter
            bool hasCt = method.ParameterList.Parameters
                .Any(p => p.Type?.ToString().Contains("CancellationToken") == true);

            if (!hasCt)
            {
                findings.Add(new Finding
                {
                    Category = "missingCancellationToken",
                    Severity = "warning",
                    File = filePath,
                    Line = GetLine(method),
                    Project = projectName,
                    Type = GetContainingTypeName(method),
                    Message = $"Method '{method.Identifier.Text}' performs async I/O but has no CancellationToken parameter."
                });
            }
        }
    }

    // 5. materializationBeforeQueryShape: .ToList() followed by .Where()/.OrderBy()/.Skip()/.Take()
    private static void AnalyzeMaterializationBeforeQueryShape(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        var queryShapingMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "Where", "OrderBy", "OrderByDescending", "Skip", "Take"
        };

        foreach (var inv in invocations)
        {
            // The current invocation should be a query-shaping method
            if (inv.Expression is not MemberAccessExpressionSyntax outerMa)
                continue;

            if (!queryShapingMethods.Contains(outerMa.Name.Identifier.Text))
                continue;

            // The expression before it should be an invocation of ToList()
            if (outerMa.Expression is not InvocationExpressionSyntax innerInv)
                continue;

            if (innerInv.Expression is MemberAccessExpressionSyntax innerMa &&
                innerMa.Name.Identifier.Text == "ToList")
            {
                findings.Add(new Finding
                {
                    Category = "materializationBeforeQueryShape",
                    Severity = "warning",
                    File = filePath,
                    Line = GetLine(inv),
                    Project = projectName,
                    Type = GetContainingTypeName(inv),
                    Message = $"'.ToList()' called before '.{outerMa.Name.Identifier.Text}()' forces in-memory evaluation. Apply query operators before materializing."
                });
            }
        }
    }

    // 6. awaitedIoInsideLoop: await <IoMethod>Async inside a loop
    private static void AnalyzeAwaitedIoInsideLoop(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var awaitExpressions = root.DescendantNodes().OfType<AwaitExpressionSyntax>();
        var ioVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Get", "Read", "Write", "Send", "Post", "Put", "Delete", "Execute", "Query", "Fetch"
        };

        foreach (var awaitExpr in awaitExpressions)
        {
            if (!IsInsideLoop(awaitExpr))
                continue;

            // Get the method name being awaited
            string? methodName = null;
            if (awaitExpr.Expression is InvocationExpressionSyntax inv)
            {
                if (inv.Expression is MemberAccessExpressionSyntax ma)
                    methodName = ma.Name.Identifier.Text;
                else if (inv.Expression is IdentifierNameSyntax id)
                    methodName = id.Identifier.Text;
            }

            if (methodName == null || !methodName.EndsWith("Async", StringComparison.Ordinal))
                continue;

            // Exclude SaveChanges (covered separately)
            if (methodName == "SaveChangesAsync")
                continue;

            // Check if name contains one of the IO verbs
            bool hasIoVerb = ioVerbs.Any(verb =>
                methodName.IndexOf(verb, StringComparison.OrdinalIgnoreCase) >= 0);

            if (hasIoVerb)
            {
                findings.Add(new Finding
                {
                    Category = "awaitedIoInsideLoop",
                    Severity = "warning",
                    File = filePath,
                    Line = GetLine(awaitExpr),
                    Project = projectName,
                    Type = GetContainingTypeName(awaitExpr),
                    Message = $"'await {methodName}(...)' inside a loop causes sequential I/O. Consider batching or using Task.WhenAll."
                });
            }
        }
    }

    // 7. unboundedWhenAll: Task.WhenAll(...) where argument is not array literal, .ToList(), or .ToArray()
    private static void AnalyzeUnboundedWhenAll(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
        {
            if (inv.Expression is not MemberAccessExpressionSyntax ma)
                continue;

            if (ma.Name.Identifier.Text != "WhenAll")
                continue;

            // Verify it is Task.WhenAll
            if (ma.Expression is not IdentifierNameSyntax id || id.Identifier.Text != "Task")
                continue;

            var args = inv.ArgumentList.Arguments;
            if (args.Count == 0)
                continue;

            // Single argument — check what it is
            if (args.Count == 1)
            {
                var argExpr = args[0].Expression;

                // Array creation expression is fine
                if (argExpr is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax)
                    continue;

                // .ToList() or .ToArray() is fine
                if (argExpr is InvocationExpressionSyntax argInv &&
                    argInv.Expression is MemberAccessExpressionSyntax argMa)
                {
                    var name = argMa.Name.Identifier.Text;
                    if (name == "ToList" || name == "ToArray")
                        continue;
                }

                findings.Add(new Finding
                {
                    Category = "unboundedWhenAll",
                    Severity = "info",
                    File = filePath,
                    Line = GetLine(inv),
                    Project = projectName,
                    Type = GetContainingTypeName(inv),
                    Message = "Task.WhenAll called with an unbounded sequence. Consider using a bounded collection to limit concurrency."
                });
            }
            // Multiple inline arguments (e.g. Task.WhenAll(t1, t2)) — explicit array of tasks, OK
        }
    }

    // --- Helpers ---

    private static bool IsInsideLoop(SyntaxNode node)
    {
        return node.Ancestors().Any(a =>
            a is ForStatementSyntax or
            ForEachStatementSyntax or
            WhileStatementSyntax or
            DoStatementSyntax);
    }

    private static bool IsTaskReturnType(TypeSyntax returnType)
    {
        var typeStr = returnType.ToString();
        return typeStr == "Task" ||
               typeStr.StartsWith("Task<", StringComparison.Ordinal) ||
               typeStr == "ValueTask" ||
               typeStr.StartsWith("ValueTask<", StringComparison.Ordinal);
    }

    private static readonly string[] AsyncIoSuffixes =
    [
        "SaveAsync", "GetAsync", "ReadAsync", "WriteAsync",
        "SendAsync", "PostAsync", "PutAsync", "DeleteAsync",
        "ExecuteAsync"
    ];

    private static bool HasAsyncIoCalls(MethodDeclarationSyntax method)
    {
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
        {
            string? name = null;
            if (inv.Expression is MemberAccessExpressionSyntax ma)
                name = ma.Name.Identifier.Text;
            else if (inv.Expression is IdentifierNameSyntax id)
                name = id.Identifier.Text;

            if (name == null) continue;

            if (AsyncIoSuffixes.Any(suffix =>
                    name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static string? GetContainingTypeName(SyntaxNode node)
    {
        return node.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.Text;
    }

    private static int GetLine(SyntaxNode node)
    {
        return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }
}
