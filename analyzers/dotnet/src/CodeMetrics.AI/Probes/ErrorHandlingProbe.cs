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

                var catches = root.DescendantNodes().OfType<CatchClauseSyntax>()
                    .ToDictionary(clause => clause, clause => new CatchObservation(clause, semanticModel));
                AnalyzeCatchBlocks(catches.Values, filePath, projectName, findings);
                AnalyzeSyncBlockingCalls(root, semanticModel, filePath, projectName, findings);
                AnalyzeConsoleWriteLine(root, filePath, projectName, findings);
                AnalyzeMissingLoggerForMultipleCatches(root, catches, filePath, projectName, findings);
            }
        }

        var observationCount = findings.Count;
        findings = CollapseSourceFindings(findings, solutionDir);
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
        var decision = ScoringDecision.FirstMatch("dotnet/errorHandling/source-findings-v1", new()
        {
            ["countingUnit"] = "distinctSourceFinding",
            ["handlingRecognition"] = "exception-propagation-v1",
            ["stderrReportingRecognition"] = "system-console-error-v1",
            ["sourceFindings"] = findings.Count,
            ["projectFrameworkObservations"] = observationCount,
            ["emptyCatches"] = emptyCatches,
            ["throwExes"] = throwExes,
            ["broadDefaults"] = broadDefaults,
            ["hasSyncBlock"] = hasSyncBlock,
            ["warnings"] = warnings
        },
        ScoringStep.Rule("systemicEmptyCatches", "emptyCatches >= 5", emptyCatches >= 5, 0, ["emptyCatch"]),
        ScoringStep.Rule("systemicBroadDefaults", "broadDefaults >= 5", broadDefaults >= 5, 0, ["broadCatchReturnsDefault"]),
        ScoringStep.Rule("emptyCatch", "emptyCatches > 0", emptyCatches > 0, 2, ["emptyCatch"]),
        ScoringStep.Rule("throwEx", "throwExes > 0", throwExes > 0, 2, ["throwEx"]),
        ScoringStep.Rule("broadDefault", "broadDefaults > 0", hasBroadDefault, 4, ["broadCatchReturnsDefault"]),
        ScoringStep.Rule("syncBlocking", "hasSyncBlock", hasSyncBlock, 4, ["syncBlockingCall"]),
        ScoringStep.Rule("manyWarnings", "warnings > 3", warnings > 3, 6, findings.Where(f => f.Severity == "warning").Select(f => f.Category).Distinct().ToArray()),
        ScoringStep.Rule("warnings", "warnings > 0", warnings > 0, 8, findings.Where(f => f.Severity == "warning").Select(f => f.Category).Distinct().ToArray()),
        ScoringStep.Rule("clean", "otherwise", true, 10, []));

        var basis = $"Distinct source findings: {findings.Count} across {observationCount} project/framework observations " +
                    $"(errors: {errors}, warnings: {warnings}). " +
                    $"emptyCatch={emptyCatches}, throwEx={throwExes}, broadDefaults={broadDefaults}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = decision.FinalScore,
            ScoringDecision = decision,
            Basis = basis,
            Findings = findings
        };
    }

    private static Dictionary<string, object?> SourceLocation(SyntaxNode node) => new()
    {
        ["sourceSpanStart"] = node.SpanStart,
        ["sourceSpanLength"] = node.Span.Length
    };

    private static List<Finding> CollapseSourceFindings(List<Finding> observations, string? solutionDir)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(solutionDir) ? "." : solutionDir);
        return observations.Select((finding, index) => (Finding: finding, Index: index))
            .GroupBy(item =>
            {
                var finding = item.Finding;
                var hasLocation = !string.IsNullOrWhiteSpace(finding.File);
                var file = hasLocation ? Path.GetFullPath(finding.File!, root) : "";
                if (OperatingSystem.IsWindows())
                    file = file.ToUpperInvariant();
                return (File: file, Start: (int)finding.Observations["sourceSpanStart"]!,
                    Length: (int)finding.Observations["sourceSpanLength"]!, finding.Category, finding.Severity,
                    UnknownLocation: hasLocation ? -1 : item.Index);
            })
            .OrderBy(group => group.Key.File, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Start).ThenBy(group => group.Key.Length)
            .ThenBy(group => group.Key.Category, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Severity, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.Select(item => item.Finding)
                    .OrderBy(finding => finding.Project, StringComparer.Ordinal)
                    .ThenBy(finding => finding.Message, StringComparer.Ordinal)
                    .ThenBy(finding => finding.File, StringComparer.Ordinal).ToList();
                var finding = ordered[0];
                finding.Observations["countingUnit"] = "distinctSourceFinding";
                finding.Observations["observationCount"] = ordered.Count;
                finding.Observations["affectedProjects"] = ordered.Select(item => item.Project)
                    .Distinct(StringComparer.Ordinal).ToArray();
                finding.Observations["projectFrameworkObservations"] = ordered.Select(item => new
                {
                    project = item.Project,
                    message = item.Message
                }).ToArray();
                return finding;
            }).ToList();
    }

    private static void AnalyzeCatchBlocks(IEnumerable<CatchObservation> catches,
        string filePath, string projectName, List<Finding> findings)
    {
        foreach (var observation in catches)
            foreach (var issue in CatchClassifier.Classify(observation))
                findings.Add(CreateCatchFinding(issue, observation.Clause, filePath, projectName));
    }

    private static Finding CreateCatchFinding(CatchIssue issue, CatchClauseSyntax clause, string filePath, string projectName)
    {
        var (category, severity, message) = issue.Kind switch
        {
            CatchIssueKind.Empty => ("emptyCatch", "error", "Empty catch block suppresses exceptions silently."),
            CatchIssueKind.ThrowCaught => ("throwEx", "error",
                $"'throw {clause.Declaration!.Identifier.Text};' loses the original stack trace. Use bare 'throw;' instead."),
            CatchIssueKind.UnhandledBroad => ("broadCatchWithoutLoggingOrRethrow", "warning",
                "Broad catch has no recognized logging, rethrow, exception-bearing return, " +
                "or error callback. Review whether the exception is observed or propagated."),
            CatchIssueKind.BroadDefault => ("broadCatchReturnsDefault", "error",
                "Broad catch returns a default value with no recognized exception-handling path. " +
                "Review whether the result hides a failure."),
            _ => throw new ArgumentOutOfRangeException(nameof(issue))
        };
        return new Finding
        {
            Category = category,
            Severity = severity,
            Message = message,
            File = filePath,
            Line = GetLine(issue.Node),
            Observations = SourceLocation(issue.Node),
            Project = projectName,
            Type = GetContainingTypeName(clause)
        };
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
                Observations = SourceLocation(access.Node),
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
                    Observations = SourceLocation(inv),
                    Project = projectName,
                    Type = GetContainingTypeName(inv),
                    Message = "Console.WriteLine found. Prefer structured logging."
                });
            }
        }
    }

    private static void AnalyzeMissingLoggerForMultipleCatches(
        SyntaxNode root, IReadOnlyDictionary<CatchClauseSyntax, CatchObservation> catches, string filePath, string projectName, List<Finding> findings)
    {
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

        foreach (var typeDecl in typeDeclarations)
        {
            var catchCount = typeDecl.DescendantNodes()
                .OfType<CatchClauseSyntax>()
                .Count(catchClause => catches[catchClause].RequiresLoggingSupport);
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
                    Observations = SourceLocation(typeDecl),
                    Project = projectName,
                    Type = typeDecl.Identifier.Text,
                    Message = $"Type '{typeDecl.Identifier.Text}' has {catchCount} catch blocks without a recognized " +
                              "handling path and no ILogger field/property/parameter. Review error reporting."
                });
            }
        }
    }

    // --- Helpers ---

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
