using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

public static class PerformanceAsyncProbe
{
    public static DimensionResult Analyze(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        string? solutionDir = null)
    {
        var findings = new List<Finding>();
        var backpressureMethods = BackpressureMethodClassifier.Build(projects, solutionDir);

        foreach (var (projectName, compilation) in projects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                var root = tree.GetRoot();
                var filePath = tree.FilePath;
                var semanticModel = compilation.GetSemanticModel(tree);

                AnalyzeSyncOverAsync(root, semanticModel, filePath, projectName, findings);
                AnalyzeThreadSleep(root, filePath, projectName, findings);
                AnalyzeSaveChangesInsideLoop(root, filePath, projectName, findings);
                AnalyzeMissingCancellationToken(root, filePath, projectName, findings);
                AnalyzeMaterializationBeforeQueryShape(root, filePath, projectName, findings);
                AnalyzeAwaitedIoInsideLoop(
                    root, semanticModel, backpressureMethods, filePath, projectName, findings);
                AnalyzeUnboundedWhenAll(root, semanticModel, filePath, projectName, findings);
            }
        }

        findings.AddRange(ConcurrentFanOutProbe.Analyze(projects, solutionDir));

        var errors = findings.Count(f => f.Severity == "error");
        var warnings = findings.Count(f => f.Severity == "warning");
        var hasSyncOverAsync = findings.Any(f => f.Category == "syncOverAsync" && f.Severity == "error");
        var hasSaveChangesInsideLoop = findings.Any(f => f.Category == "saveChangesInsideLoop");

        // Ladder rungs. The warning tail spans 4/6/8 rather than SecurityProbe's 6/8
        // because rung 4 has no structural condition of its own here — collapsing
        // 'warnings > 3' upward would make 4 unreachable while fixing the missing 8.
        //   0  systemic  — five or more error-severity findings
        //   2  errors    — sync-over-async, SaveChanges in a loop, or any error finding
        //   4  noisy     — more than three advisory warnings
        //   6  several   — two or three advisory warnings
        //   8  minor     — a single advisory warning, no errors
        //  10  clean     — no findings
        double score;
        if (errors >= 5)
            score = 0;
        else if (hasSyncOverAsync || hasSaveChangesInsideLoop || errors > 0)
            score = 2;
        else if (warnings > 3)
            score = 4;
        else if (warnings > 1)
            score = 6;
        else if (warnings > 0)
            score = 8;
        else
            score = 10;

        var basis = $"Findings: {findings.Count} (errors: {errors}, warnings: {warnings}). " +
                    $"syncOverAsync={findings.Count(f => f.Category == "syncOverAsync")}, " +
                    $"threadSleep={findings.Count(f => f.Category == "threadSleep")}, " +
                    $"saveChangesInsideLoop={findings.Count(f => f.Category == "saveChangesInsideLoop")}, " +
                    $"missingCancellationToken={findings.Count(f => f.Category == "missingCancellationToken")}, " +
                    $"materializationBeforeQueryShape={findings.Count(f => f.Category == "materializationBeforeQueryShape")}, " +
                    $"awaitedIoInsideLoop={findings.Count(f => f.Category == "awaitedIoInsideLoop")}, " +
                    $"unboundedWhenAll={findings.Count(f => f.Category == "unboundedWhenAll")}, " +
                    $"sharedStateMutationInFanOut={findings.Count(f => f.Category == "sharedStateMutationInFanOut")}.";

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
        SyntaxNode root, SemanticModel semanticModel, string filePath, string projectName,
        List<Finding> findings)
    {
        var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

        foreach (var ma in memberAccesses)
        {
            var memberName = ma.Name.Identifier.Text;

            if (memberName == "Result" && IsTaskLikeReceiver(semanticModel, ma.Expression))
            {
                findings.Add(new Finding
                {
                    Category = "syncOverAsync",
                    Severity = SyncOverAsyncSeverity(ma),
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
                    innerMa.Name.Identifier.Text == "GetAwaiter" &&
                    IsTaskLikeReceiver(semanticModel, innerMa.Expression))
                {
                    findings.Add(new Finding
                    {
                        Category = "syncOverAsync",
                        Severity = SyncOverAsyncSeverity(ma),
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
                ma2.Name.Identifier.Text == "Wait" &&
                IsTaskLikeReceiver(semanticModel, ma2.Expression))
            {
                findings.Add(new Finding
                {
                    Category = "syncOverAsync",
                    Severity = SyncOverAsyncSeverity(inv),
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

            if (IsAspNetCoreMiddlewareInvokeAsync(method))
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
        SyntaxNode root, SemanticModel semanticModel, IReadOnlySet<string> backpressureMethods,
        string filePath, string projectName,
        List<Finding> findings)
    {
        var awaitExpressions = root.DescendantNodes().OfType<AwaitExpressionSyntax>();
        var ioVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Get", "Read", "Write", "Send", "Post", "Put", "Delete", "Execute", "Query", "Fetch"
        };

        foreach (var awaitExpr in awaitExpressions)
        {
            // The innermost loop is the one whose iterations this await would have to be
            // batched across. Judging the await against an outer loop instead would let a
            // cursor-driven outer loop hide a genuine N+1 nested inside it.
            var loop = InnermostLoop(awaitExpr);
            if (loop == null)
                continue;

            var containingMethod = awaitExpr.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (containingMethod != null && HasSyncRequiredSuppression(containingMethod))
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

            // The rule's premise is that N sequential awaits should have been one batched
            // call. Where each iteration causally requires the previous response, or where
            // the blocking await is itself the mechanism the author wanted, there is nothing
            // to batch and the finding would be a false positive.
            if (IsContinuationDependentLoop(loop))
                continue;

            if (IsBoundedFallbackSequence(loop))
                continue;

            if (IsBackpressurePrimitive(semanticModel, awaitExpr, backpressureMethods))
                continue;

            if (IsOrderedPipelineStageLoop(loop, awaitExpr, semanticModel))
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

    // 7. unboundedWhenAll: a visible deferred task projection over an input-sized source.
    // Task.WhenAll over an existing task collection does not create concurrency, so unknown
    // collection provenance is not evidence of an unbounded fan-out.
    private static void AnalyzeUnboundedWhenAll(
        SyntaxNode root, SemanticModel semanticModel, string filePath, string projectName,
        List<Finding> findings)
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

            if (args.Count != 1 ||
                !TryGetTaskProjectionSource(args[0].Expression, out var source) ||
                IsFixedCardinalitySource(source, semanticModel))
            {
                continue;
            }

            findings.Add(new Finding
            {
                Category = "unboundedWhenAll",
                Severity = "info",
                Confidence = "medium",
                File = filePath,
                Line = GetLine(inv),
                Project = projectName,
                Type = GetContainingTypeName(inv),
                Message = "Task.WhenAll enumerates a task-producing projection whose input size is not bounded here. Consider explicit concurrency control."
            });
            // Multiple inline arguments (e.g. Task.WhenAll(t1, t2)) — explicit array of tasks, OK
        }
    }

    // --- Helpers ---

    private static bool IsTaskLikeReceiver(SemanticModel semanticModel, ExpressionSyntax receiver)
    {
        return TaskTypes.IsTaskLike(semanticModel.GetTypeInfo(receiver).Type);
    }

    private static bool IsInsideLoop(SyntaxNode node)
    {
        return InnermostLoop(node) != null;
    }

    private static SyntaxNode? InnermostLoop(SyntaxNode node)
    {
        return node.Ancestors().FirstOrDefault(a =>
            a is ForStatementSyntax or
            ForEachStatementSyntax or
            WhileStatementSyntax or
            DoStatementSyntax);
    }

    private static bool IsAspNetCoreMiddlewareInvokeAsync(MethodDeclarationSyntax method)
    {
        if (method.Identifier.Text != "InvokeAsync")
            return false;

        if (method.ParameterList.Parameters.Count != 1)
            return false;

        var typeName = method.ParameterList.Parameters[0].Type?.ToString();
        return typeName == "HttpContext" ||
               typeName?.EndsWith(".HttpContext", StringComparison.Ordinal) == true;
    }

    private static string SyncOverAsyncSeverity(SyntaxNode node)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method == null)
            return "error";

        return HasSyncRequiredSuppression(method) ||
               IsOverrideOrExplicitInterfaceImplementation(method)
            ? "info"
            : "error";
    }

    private static bool HasSyncRequiredSuppression(MethodDeclarationSyntax method)
    {
        return method.GetLeadingTrivia()
            .Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
                           trivia.ToString().Contains("amp-metrics: sync-required", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOverrideOrExplicitInterfaceImplementation(MethodDeclarationSyntax method)
    {
        return method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)) ||
               method.ExplicitInterfaceSpecifier != null;
    }

    // ── Sequential-by-necessity loop shapes ───────────────────────────────────
    // These recognisers exist to remove false positives, so each one is a
    // conjunction of signals rather than a single suggestive name, and each bails
    // out to "flag it" whenever the shape is not unmistakable.

    /// <summary>
    /// True when the loop cannot decide whether to continue without the previous
    /// iteration's awaited result — the cursor / continuation-token shape. Iteration
    /// N+1's request is built from iteration N's response, so requesting page 2 before
    /// reading page 1 is not merely slower but impossible, and there is no batched form
    /// to suggest.
    /// <para>
    /// Only condition-driven loops qualify. A <c>foreach</c> iterates a sequence that is
    /// fully determined before the first await, so it can never be cursor-driven — which
    /// is what keeps the ordinary <c>foreach (var id in ids) await GetAsync(id)</c> N+1
    /// firing.
    /// </para>
    /// </summary>
    private static bool IsContinuationDependentLoop(SyntaxNode loop)
    {
        var condition = LoopCondition(loop);
        if (condition == null)
            return false;

        var body = LoopBody(loop);
        if (body == null)
            return false;

        var conditionReads = ReadLocations(condition);
        if (conditionReads.Count == 0)
            return false;

        return conditionReads.Overlaps(AwaitDerivedLocations(body));
    }

    /// <summary>
    /// True for a <c>foreach</c> over a short inline literal whose body can exit early —
    /// <c>foreach (var url in new[] { https, http }) { ... return; }</c>. The candidates
    /// are fixed at authoring time so there is no N to multiply, and the loop stops at the
    /// first success so issuing them concurrently would do strictly more work than the
    /// sequential form. Both halves are required: a short literal that always visits every
    /// element still batches cleanly and stays flagged.
    /// </summary>
    private static bool IsBoundedFallbackSequence(SyntaxNode loop)
    {
        if (loop is not ForEachStatementSyntax forEach)
            return false;

        var candidates = InlineLiteralElementCount(forEach.Expression);
        if (candidates is null || candidates > MaxFallbackCandidates)
            return false;

        return forEach.Statement.DescendantNodesAndSelf()
            .Any(n => n is ReturnStatementSyntax or BreakStatementSyntax);
    }

    /// <summary>
    /// True when the awaited member belongs to a type whose whole purpose is to block:
    /// a bounded channel write waits precisely to apply back-pressure, and a semaphore
    /// wait blocks precisely to cap concurrency. Hoisting these into
    /// <c>Task.WhenAll</c> defeats the bound the author asked for.
    /// <para>
    /// Resolved through the semantic model against real framework types, the same way
    /// <see cref="IsTaskLikeReceiver"/> settles sync-over-async, so a user type merely
    /// named "…Channel" is not matched.
    /// </para>
    /// </summary>
    private static bool IsBackpressurePrimitive(
        SemanticModel semanticModel,
        AwaitExpressionSyntax awaitExpr,
        IReadOnlySet<string> backpressureMethods)
    {
        if (awaitExpr.Expression is not InvocationExpressionSyntax invocation)
            return false;

        return BackpressureMethodClassifier.IsBackpressureInvocation(
            semanticModel, invocation, backpressureMethods);
    }

    private const int MaxFallbackCandidates = 4;

    private static bool IsOrderedPipelineStageLoop(
        SyntaxNode loop,
        AwaitExpressionSyntax awaitExpression,
        SemanticModel semanticModel)
    {
        if (awaitExpression.Expression is not InvocationExpressionSyntax invocation)
            return false;

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol awaitedMethod ||
            awaitedMethod.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        IEnumerable<ExpressionSyntax> loopElements = loop switch
        {
            ForStatementSyntax => invocation.ArgumentList.Arguments
                .SelectMany(argument => argument.Expression.DescendantNodesAndSelf()
                    .OfType<ElementAccessExpressionSyntax>()),
            ForEachStatementSyntax forEach => invocation.ArgumentList.Arguments
                .SelectMany(argument => argument.Expression.DescendantNodesAndSelf()
                    .OfType<IdentifierNameSyntax>()
                    .Where(identifier => identifier.Identifier.Text == forEach.Identifier.Text)),
            _ => []
        };

        return loopElements.Any(expression =>
        {
            var type = semanticModel.GetTypeInfo(expression).Type as INamedTypeSymbol;
            return type != null && IsPipelineStageType(type);
        });
    }

    private static bool IsPipelineStageType(INamedTypeSymbol type)
    {
        return type.Name.EndsWith("PipelineStage", StringComparison.Ordinal) ||
               type.AllInterfaces.Any(interfaceType =>
                   interfaceType.Name.EndsWith("PipelineStage", StringComparison.Ordinal));
    }

    private static bool TryGetTaskProjectionSource(
        ExpressionSyntax expression,
        out ExpressionSyntax source)
    {
        var current = expression;
        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.Text is "Select" or "SelectMany")
            {
                source = memberAccess.Expression;
                return true;
            }

            current = memberAccess.Expression;
        }

        source = expression;
        return false;
    }

    private static bool IsFixedCardinalitySource(ExpressionSyntax source, SemanticModel semanticModel)
    {
        if (InlineLiteralElementCount(source) != null)
            return true;

        if (semanticModel.GetSymbolInfo(source).Symbol is not IFieldSymbol field)
            return false;

        return IsStartupMaterializedField(field, semanticModel.Compilation);
    }

    private static bool IsStartupMaterializedField(IFieldSymbol field, Compilation compilation)
    {
        foreach (var typeReference in field.ContainingType.DeclaringSyntaxReferences)
        {
            if (typeReference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration)
                continue;

            var model = compilation.GetSemanticModel(typeDeclaration.SyntaxTree);
            foreach (var assignment in typeDeclaration.DescendantNodes()
                         .OfType<AssignmentExpressionSyntax>())
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        model.GetSymbolInfo(assignment.Left).Symbol, field))
                {
                    continue;
                }

                if (IsMaterializedConstructorParameter(assignment.Right, model))
                    return true;
            }

            foreach (var declarator in typeDeclaration.DescendantNodes()
                         .OfType<VariableDeclaratorSyntax>()
                         .Where(declarator => declarator.Initializer != null))
            {
                if (!SymbolEqualityComparer.Default.Equals(
                        model.GetDeclaredSymbol(declarator), field))
                {
                    continue;
                }

                if (InlineLiteralElementCount(declarator.Initializer!.Value) != null ||
                    IsMaterializedConstructorParameter(declarator.Initializer.Value, model))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsMaterializedConstructorParameter(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        var current = expression;
        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.Text is "ToList" or "ToArray")
            {
                return semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is IParameterSymbol parameter &&
                       parameter.ContainingSymbol is IMethodSymbol { MethodKind: MethodKind.Constructor };
            }

            current = memberAccess.Expression;
        }

        return false;
    }

    /// <summary>
    /// Locations in the loop body whose value comes, directly or through a chain of local
    /// assignments, from an awaited call in the same body. The walk runs to a fixed point so
    /// a multi-hop chain resolves: <c>json = await Read(); page = Deserialize(json);
    /// token = page.NextPageToken</c> yields all three, which is what connects the awaited
    /// response to the loop condition that reads <c>token</c>.
    /// </summary>
    private static HashSet<string> AwaitDerivedLocations(SyntaxNode body)
    {
        var writes = new List<(string Target, ExpressionSyntax Value)>();

        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case VariableDeclaratorSyntax { Initializer: { } initializer } declarator:
                    writes.Add((declarator.Identifier.Text, initializer.Value));
                    break;
                case AssignmentExpressionSyntax assignment when LocationKey(assignment.Left) is { } target:
                    writes.Add((target, assignment.Right));
                    break;
            }
        }

        var derived = new HashSet<string>(StringComparer.Ordinal);

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (target, value) in writes)
            {
                if (derived.Contains(target))
                    continue;

                bool fromAwait =
                    value.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>().Any() ||
                    ReadLocations(value).Overlaps(derived);

                if (fromAwait)
                    changed |= derived.Add(target);
            }
        }

        return derived;
    }

    /// <summary>
    /// Location keys — normalised source text — for every value an expression reads. A member
    /// access contributes both its full path and its rooted prefixes, so <c>page?.NextPageToken</c>
    /// yields "page" and "page.NextPageToken"; the prefix is what lets the derivation walk connect
    /// a write of <c>token</c> to the awaited value held in <c>page</c>.
    /// </summary>
    private static HashSet<string> ReadLocations(SyntaxNode expression)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in expression.DescendantNodesAndSelf())
        {
            if (node is IdentifierNameSyntax id)
            {
                // The member half of 'a.B' is not a location of its own; only 'a' and the
                // full path "a.B" are. Keeping 'B' would let unrelated same-named members
                // collide and silently suppress a real finding.
                if (id.Parent is MemberAccessExpressionSyntax parent && parent.Name == id)
                    continue;

                keys.Add(id.Identifier.Text);
            }
            else if (node is MemberAccessExpressionSyntax memberAccess &&
                     LocationKey(memberAccess) is { } key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>Dotted path for an expression rooted at a plain identifier, else null.</summary>
    private static string? LocationKey(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax ma when ma.IsKind(SyntaxKind.SimpleMemberAccessExpression) =>
            LocationKey(ma.Expression) is { } root ? $"{root}.{ma.Name.Identifier.Text}" : null,
        _ => null
    };

    private static int? InlineLiteralElementCount(ExpressionSyntax expression) => expression switch
    {
        ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions.Count,
        ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer.Expressions.Count,
        CollectionExpressionSyntax collection => collection.Elements.Count,
        _ => null
    };

    private static ExpressionSyntax? LoopCondition(SyntaxNode loop) => loop switch
    {
        WhileStatementSyntax w => w.Condition,
        DoStatementSyntax d => d.Condition,
        ForStatementSyntax f => f.Condition,
        _ => null
    };

    private static StatementSyntax? LoopBody(SyntaxNode loop) => loop switch
    {
        WhileStatementSyntax w => w.Statement,
        DoStatementSyntax d => d.Statement,
        ForStatementSyntax f => f.Statement,
        ForEachStatementSyntax fe => fe.Statement,
        _ => null
    };

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
