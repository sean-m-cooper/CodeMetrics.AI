using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

public static class PerformanceAsyncProbe
{
    public static DimensionResult Analyze(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        string? solutionDir = null)
    {
        var findings = new List<Finding>();

        foreach (var (projectName, compilation) in projects)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (solutionDir != null && !SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                    continue;

                var root = tree.GetRoot();
                var filePath = tree.FilePath;

                SyncOverAsyncAnalyzer.Analyze(root, filePath, projectName, findings);
                BlockingCallAnalyzer.Analyze(root, filePath, projectName, findings);
                EfLoopAnalyzer.Analyze(root, filePath, projectName, findings);
                CancellationTokenAnalyzer.Analyze(root, filePath, projectName, findings);
                QueryMaterializationAnalyzer.Analyze(root, filePath, projectName, findings);
                AwaitedIoLoopAnalyzer.Analyze(root, filePath, projectName, findings);
                WhenAllAnalyzer.Analyze(root, filePath, projectName, findings);
            }
        }

        return BuildResult(findings);
    }

    private static DimensionResult BuildResult(List<Finding> findings)
    {
        var errors = findings.Count(f => f.Severity == "error");
        var warnings = findings.Count(f => f.Severity == "warning");
        var hasSyncOverAsync = findings.Any(f => f.Category == "syncOverAsync" && f.Severity == "error");
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
}
