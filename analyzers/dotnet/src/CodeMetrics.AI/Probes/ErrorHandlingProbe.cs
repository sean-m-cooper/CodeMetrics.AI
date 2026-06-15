using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

public static class ErrorHandlingProbe
{
    public static DimensionResult Analyze(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        string? solutionDir = null)
    {
        var findings = ErrorHandlingProjectAnalyzer.Analyze(projects, solutionDir);
        var counts = ErrorHandlingFindingCounts.From(findings);

        return new DimensionResult
        {
            Status = "scored",
            Score = Score(counts),
            Basis = $"Findings: {findings.Count} (errors: {counts.Errors}, warnings: {counts.Warnings}). " +
                    $"emptyCatch={counts.EmptyCatches}, throwEx={counts.ThrowExes}, broadDefaults={counts.BroadDefaults}.",
            Findings = findings
        };
    }

    private static double Score(ErrorHandlingFindingCounts counts)
    {
        if (counts.EmptyCatches >= 5 || counts.BroadDefaults >= 5)
            return 0;
        if (counts.EmptyCatches > 0 || counts.ThrowExes > 0)
            return 2;
        if (counts.BroadDefaults > 0 || counts.HasSyncBlockingCall || counts.Warnings > 3)
            return 4;
        if (counts.Warnings > 0)
            return 6;
        if (counts.Errors == 0)
            return 10;
        return 6;
    }
}
