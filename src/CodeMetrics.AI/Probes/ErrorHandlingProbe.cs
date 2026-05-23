using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

public static class ErrorHandlingProbe
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

                AnalyzeCatchBlocks(root, filePath, projectName, findings);
                AnalyzeSyncBlockingCalls(root, filePath, projectName, findings);
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

        double score;
        if (emptyCatches >= 5 || broadDefaults >= 5)
            score = 0;
        else if (emptyCatches > 0 || throwExes > 0)
            score = 2;
        else if (hasBroadDefault || hasSyncBlock || warnings > 3)
            score = 4;
        else if (warnings > 0)
            score = 6;
        else if (errors == 0 && warnings == 0)
            score = 10;
        else
            score = 6;

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
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
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
                bool hasLogging = HasLoggingCall(block);
                bool hasBareRethrow = HasBareRethrow(block);

                // 3. broadCatchWithoutLoggingOrRethrow
                if (!hasLogging && !hasBareRethrow)
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
                if (ReturnsDefault(block))
                {
                    findings.Add(new Finding
                    {
                        Category = "broadCatchReturnsDefault",
                        Severity = "error",
                        File = filePath,
                        Line = GetLine(catchClause),
                        Project = projectName,
                        Type = containingType,
                        Message = "Broad catch block returns a default value, hiding exceptions."
                    });
                }
            }
        }
    }

    private static void AnalyzeSyncBlockingCalls(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        // .Result and .Wait() via member access expressions
        var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();

        foreach (var ma in memberAccesses)
        {
            var memberName = ma.Name.Identifier.Text;

            // .Result
            if (memberName == "Result")
            {
                findings.Add(new Finding
                {
                    Category = "syncBlockingCall",
                    Severity = "warning",
                    File = filePath,
                    Line = GetLine(ma),
                    Project = projectName,
                    Type = GetContainingTypeName(ma),
                    Message = "'.Result' blocks the calling thread and can cause deadlocks. Use 'await' instead."
                });
            }
            // .GetAwaiter().GetResult() — the outer .GetResult() member access
            else if (memberName == "GetResult")
            {
                // Check that the expression is GetAwaiter()
                if (ma.Expression is InvocationExpressionSyntax inv &&
                    inv.Expression is MemberAccessExpressionSyntax innerMa &&
                    innerMa.Name.Identifier.Text == "GetAwaiter")
                {
                    findings.Add(new Finding
                    {
                        Category = "syncBlockingCall",
                        Severity = "warning",
                        File = filePath,
                        Line = GetLine(ma),
                        Project = projectName,
                        Type = GetContainingTypeName(ma),
                        Message = "'.GetAwaiter().GetResult()' blocks the calling thread and can cause deadlocks. Use 'await' instead."
                    });
                }
            }
        }

        // .Wait() via invocations
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var inv in invocations)
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma2 &&
                ma2.Name.Identifier.Text == "Wait")
            {
                findings.Add(new Finding
                {
                    Category = "syncBlockingCall",
                    Severity = "warning",
                    File = filePath,
                    Line = GetLine(inv),
                    Project = projectName,
                    Type = GetContainingTypeName(inv),
                    Message = "'.Wait()' blocks the calling thread and can cause deadlocks. Use 'await' instead."
                });
            }
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
            var catchCount = typeDecl.DescendantNodes().OfType<CatchClauseSyntax>().Count();
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

    private static bool HasLoggingCall(BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv =>
            {
                var text = inv.Expression.ToString();
                return text.IndexOf("Log", StringComparison.OrdinalIgnoreCase) >= 0;
            });
    }

    private static bool HasBareRethrow(BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<ThrowStatementSyntax>()
            .Any(t => t.Expression == null);
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

        return inCtorParams;
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
