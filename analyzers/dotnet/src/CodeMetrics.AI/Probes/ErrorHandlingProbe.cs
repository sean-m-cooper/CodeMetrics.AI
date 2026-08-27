using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

public static class ErrorHandlingProbe
{
    public static DimensionResult Analyze(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        string? solutionDir = null)
    {
        var findings = new List<Finding>();

        foreach (var (projectName, compilation) in projects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                var root = tree.GetRoot();
                var filePath = tree.FilePath;
                var semanticModel = compilation.GetSemanticModel(tree);

                AnalyzeCatchBlocks(root, semanticModel, filePath, projectName, findings);
                AnalyzeSyncBlockingCalls(root, semanticModel, filePath, projectName, findings);
                AnalyzeConsoleWriteLine(root, filePath, projectName, findings);
                AnalyzeMissingLoggerForMultipleCatches(root, filePath, projectName, findings);
            }
        }

        var emptyCatches = findings.Count(f => f.Category == "emptyCatch");
        var throwExes = findings.Count(f => f.Category == "throwEx");
        var broadDefaults = findings.Count(f => f.Category == "broadCatchReturnsDefault");
        var hasBroadDefault = broadDefaults > 0;
        var hasSyncBlock = findings.Any(f => f.Category == "syncBlockingCall");
        var warnings = findings.Count(f => f.Severity == "warning");
        var errors = findings.Count(f => f.Severity == "error");

        // Ladder rungs, matching SecurityProbe's convention of reserving 0/2/4 for
        // error-severity and structural findings and 6/8 for the warning tail:
        //   0  systemic       — five or more empty catches or default-returning broad catches
        //   2  errors         — any empty catch or 'throw ex;'
        //   4  structural     — a default-returning broad catch or a sync-blocking call
        //   6  noisy          — more than three advisory warnings
        //   8  minor          — one to three advisory warnings, no errors
        //  10  clean          — no findings
        double score;
        if (emptyCatches >= 5 || broadDefaults >= 5)
            score = 0;
        else if (emptyCatches > 0 || throwExes > 0)
            score = 2;
        else if (hasBroadDefault || hasSyncBlock)
            score = 4;
        else if (warnings > 3)
            score = 6;
        else if (warnings > 0)
            score = 8;
        else
            score = 10;

        var basis = $"Findings: {findings.Count} (errors: {errors}, warnings: {warnings}). " +
                    $"emptyCatch={emptyCatches}, throwEx={throwExes}, broadDefaults={broadDefaults}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = score,
            Basis = basis,
            Findings = findings
        };
    }

    private static void AnalyzeCatchBlocks(
        SyntaxNode root, SemanticModel semanticModel,
        string filePath, string projectName, List<Finding> findings)
    {
        var catchClauses = root.DescendantNodes().OfType<CatchClauseSyntax>();

        foreach (var catchClause in catchClauses)
        {
            var block = catchClause.Block;
            var stmts = block.Statements;
            var containingType = GetContainingTypeName(catchClause);

            // 1. emptyCatch
            if (stmts.Count == 0)
            {
                if (!FindingSuppression.IsSuppressed(catchClause, "emptyCatch") &&
                    !IsDocumentedNarrowFallbackCatch(catchClause, semanticModel))
                {
                    findings.Add(new Finding
                    {
                        Category = "emptyCatch",
                        Severity = "error",
                        File = filePath,
                        Line = GetLine(catchClause),
                        Project = projectName,
                        Type = containingType,
                        Message = "Empty catch block suppresses exceptions silently."
                    });
                }
                continue; // no further analysis on an empty block
            }

            var caughtVarName = catchClause.Declaration?.Identifier.Text;

            // 2. throwEx — throw ex; where ex matches caught variable
            if (!string.IsNullOrEmpty(caughtVarName))
            {
                var throwStatements = block.DescendantNodes().OfType<ThrowStatementSyntax>();
                foreach (var throwStmt in throwStatements)
                {
                    if (throwStmt.Expression is IdentifierNameSyntax id &&
                        id.Identifier.Text == caughtVarName)
                    {
                        findings.Add(new Finding
                        {
                            Category = "throwEx",
                            Severity = "error",
                            File = filePath,
                            Line = GetLine(throwStmt),
                            Project = projectName,
                            Type = containingType,
                            Message = $"'throw {caughtVarName};' loses the original stack trace. Use bare 'throw;' instead."
                        });
                    }
                }
            }

            // 3 & 4. Broad catch checks (catch (Exception) or bare catch, no when filter)
            bool isBroad = IsBroadCatch(catchClause);
            if (isBroad)
            {
                // A broad catch only swallows when nothing observes or propagates the
                // exception. Both broad-catch rules share that test so a catch cannot
                // be silent for one rule and handled for the other.
                bool isHandled = IsHandledBroadCatch(catchClause, semanticModel);

                // 3. broadCatchWithoutLoggingOrRethrow
                if (!isHandled)
                {
                    findings.Add(new Finding
                    {
                        Category = "broadCatchWithoutLoggingOrRethrow",
                        Severity = "warning",
                        File = filePath,
                        Line = GetLine(catchClause),
                        Project = projectName,
                        Type = containingType,
                        Message = "Broad catch block without logging or rethrow swallows exceptions."
                    });
                }

                // 4. broadCatchReturnsDefault
                if (!isHandled && ReturnsDefault(block))
                {
                    findings.Add(new Finding
                    {
                        Category = "broadCatchReturnsDefault",
                        Severity = "error",
                        File = filePath,
                        Line = GetLine(catchClause),
                        Project = projectName,
                        Type = containingType,
                        Message = "Broad catch block returns a default value without logging or " +
                                  "rethrowing, hiding exceptions behind a successful-looking result."
                    });
                }
            }
        }
    }

    private static bool IsDocumentedNarrowFallbackCatch(
        CatchClauseSyntax catchClause,
        SemanticModel semanticModel)
    {
        if (catchClause.Declaration?.Type is not { } catchType ||
            catchClause.Parent is not TryStatementSyntax tryStatement ||
            tryStatement.Parent is not BlockSyntax containingBlock)
        {
            return false;
        }

        if (semanticModel.GetTypeInfo(catchType).Type is not INamedTypeSymbol caughtType ||
            caughtType.ToDisplayString() is "System.Exception" or "System.SystemException" ||
            !DerivesFromException(caughtType))
            return false;

        var hasExplanation = catchClause.Block.DescendantTrivia(descendIntoTrivia: true)
            .Any(trivia =>
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));
        if (!hasExplanation ||
            !tryStatement.Block.DescendantNodes().OfType<ReturnStatementSyntax>().Any())
        {
            return false;
        }

        var tryIndex = containingBlock.Statements.IndexOf(tryStatement);
        return tryIndex >= 0 &&
               tryIndex + 1 < containingBlock.Statements.Count &&
               containingBlock.Statements[tryIndex + 1] is ReturnStatementSyntax;
    }

    private static bool DerivesFromException(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() == "System.Exception")
                return true;
        }

        return false;
    }

    private static void AnalyzeSyncBlockingCalls(
        SyntaxNode root, SemanticModel semanticModel, string filePath, string projectName,
        List<Finding> findings)
    {
        foreach (var access in SyncBlockingDetector.Find(
                     root,
                     semanticModel,
                     "syncBlockingCall"))
        {
            var operation = access.Kind switch
            {
                SyncBlockingKind.Result => ".Result",
                SyncBlockingKind.GetAwaiterGetResult => ".GetAwaiter().GetResult()",
                SyncBlockingKind.Wait => ".Wait()",
                _ => throw new ArgumentOutOfRangeException()
            };
            findings.Add(new Finding
            {
                Category = "syncBlockingCall",
                Severity = "warning",
                File = filePath,
                Line = GetLine(access.Node),
                Project = projectName,
                Type = GetContainingTypeName(access.Node),
                Message = $"'{operation}' blocks the calling thread and can cause deadlocks. Use 'await' instead."
            });
        }
    }

    private static void AnalyzeConsoleWriteLine(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Expression is IdentifierNameSyntax id &&
                id.Identifier.Text == "Console" &&
                ma.Name.Identifier.Text == "WriteLine")
            {
                findings.Add(new Finding
                {
                    Category = "consoleWriteLine",
                    Severity = "info",
                    File = filePath,
                    Line = GetLine(inv),
                    Project = projectName,
                    Type = GetContainingTypeName(inv),
                    Message = "Console.WriteLine found. Prefer structured logging."
                });
            }
        }
    }

    private static void AnalyzeMissingLoggerForMultipleCatches(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

        foreach (var typeDecl in typeDeclarations)
        {
            var catchCount = typeDecl.DescendantNodes()
                .OfType<CatchClauseSyntax>()
                .Count(RequiresLoggingSupport);
            if (catchCount < 2)
                continue;

            bool hasLogger = HasLoggerMember(typeDecl);
            if (!hasLogger)
            {
                findings.Add(new Finding
                {
                    Category = "missingLoggerForMultipleCatches",
                    Severity = "warning",
                    File = filePath,
                    Line = GetLine(typeDecl),
                    Project = projectName,
                    Type = typeDecl.Identifier.Text,
                    Message = $"Type '{typeDecl.Identifier.Text}' has {catchCount} catch blocks but no ILogger field/property/parameter."
                });
            }
        }
    }

    private static bool RequiresLoggingSupport(CatchClauseSyntax catchClause)
    {
        return !catchClause.Block.DescendantNodes(ShouldDescendIntoCatchNode).Any(node =>
            node is ReturnStatementSyntax or ContinueStatementSyntax or ThrowStatementSyntax);
    }

    private static bool ShouldDescendIntoCatchNode(SyntaxNode node)
    {
        return node is not LocalFunctionStatementSyntax and
               not AnonymousFunctionExpressionSyntax;
    }

    // --- Helpers ---

    private static bool IsBroadCatch(CatchClauseSyntax catchClause)
    {
        // Has a when filter → not broad
        if (catchClause.Filter != null)
            return false;

        // Bare catch (no declaration)
        if (catchClause.Declaration == null)
            return true;

        // catch (Exception) or catch (Exception ex)
        var typeName = catchClause.Declaration.Type.ToString();
        return typeName == "Exception" || typeName == "System.Exception";
    }

    /// <summary>
    /// A broad catch is handled — not swallowing — when the exception is recorded or
    /// propagated. DescendantNodes is used throughout so a log call or throw nested in
    /// an if, using or local function inside the catch body still counts.
    /// </summary>
    private static bool IsHandledBroadCatch(
        CatchClauseSyntax catchClause, SemanticModel semanticModel)
    {
        var block = catchClause.Block;
        return HasLoggingCall(block)
               || HasRethrow(block)
               || HasPrecedingCancellationRethrow(catchClause)
               || HasDeferredLogging(catchClause, semanticModel);
    }

    private static bool HasLoggingCall(BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsLoggingCall);
    }

    private static bool HasDeferredLogging(
        CatchClauseSyntax catchClause, SemanticModel semanticModel)
    {
        var caughtSymbol = catchClause.Declaration == null
            ? null
            : semanticModel.GetDeclaredSymbol(catchClause.Declaration);

        if (caughtSymbol == null)
            return false;

        var capturedSymbols = catchClause.Block.DescendantNodes()
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

    /// <summary>
    /// Any throw statement propagates. A bare 'throw;' preserves the stack trace and a
    /// 'throw new Wrapped(ex);' surfaces the failure to the caller; neither swallows.
    /// 'throw ex;' is reported separately by the throwEx rule, so counting it here
    /// stops one catch from being penalised twice for a shape that does propagate.
    /// </summary>
    private static bool HasRethrow(BlockSyntax block)
    {
        return block.DescendantNodes().OfType<ThrowStatementSyntax>().Any();
    }

    /// <summary>
    /// True when an earlier catch clause on the same try rethrows cancellation, as in
    /// 'catch (OperationCanceledException) { throw; }' ahead of a broad catch. The
    /// broad catch is then a deliberate degrade-gracefully handler for real faults
    /// rather than a blanket suppressor, because cancellation never reaches it.
    /// </summary>
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

    private static bool ReturnsDefault(BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Any(ret =>
            {
                if (ret.Expression == null) return false;
                var expr = ret.Expression;
                return expr is LiteralExpressionSyntax lit &&
                           (lit.IsKind(SyntaxKind.NullLiteralExpression) ||
                            lit.IsKind(SyntaxKind.FalseLiteralExpression) ||
                            (lit.IsKind(SyntaxKind.NumericLiteralExpression) &&
                             lit.Token.ValueText == "0"))
                       || expr is DefaultExpressionSyntax
                       || expr is LiteralExpressionSyntax lit2 &&
                          lit2.IsKind(SyntaxKind.DefaultLiteralExpression)
                       || (expr is MemberAccessExpressionSyntax ma &&
                           ma.Expression.ToString() == "string" &&
                           ma.Name.Identifier.Text == "Empty");
            });
    }

    private static bool HasLoggerMember(TypeDeclarationSyntax typeDecl)
    {
        // Check fields
        bool inFields = typeDecl.Members
            .OfType<FieldDeclarationSyntax>()
            .Any(f => f.Declaration.Type.ToString().Contains("ILogger"));

        if (inFields) return true;

        // Check properties
        bool inProps = typeDecl.Members
            .OfType<PropertyDeclarationSyntax>()
            .Any(p => p.Type.ToString().Contains("ILogger"));

        if (inProps) return true;

        // Check constructor parameters
        bool inCtorParams = typeDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .SelectMany(c => c.ParameterList.Parameters)
            .Any(p => p.Type?.ToString().Contains("ILogger") == true);

        if (inCtorParams) return true;

        // Check method parameters. Static helper types commonly receive their logger per call,
        // so a method parameter is as valid a logging path as a field or constructor parameter.
        bool inMethodParams = typeDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .Any(m => HasLoggerParameter(m.ParameterList));

        if (inMethodParams) return true;

        // Check primary constructor parameters. TypeDeclarationSyntax.ParameterList covers
        // C# 12 class/struct primary constructors as well as record positional parameters,
        // none of which appear in Members as a ConstructorDeclarationSyntax.
        return HasLoggerParameter(typeDecl.ParameterList);
    }

    private static bool HasLoggerParameter(ParameterListSyntax? parameterList)
    {
        return parameterList?.Parameters
            .Any(p => p.Type?.ToString().Contains("ILogger") == true) == true;
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
