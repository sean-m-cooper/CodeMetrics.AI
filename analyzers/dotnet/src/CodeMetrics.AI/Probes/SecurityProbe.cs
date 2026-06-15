using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

public static class SecurityProbe
{
    public static DimensionResult Analyze(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        int importedVulnerabilities = 0,
        string? solutionDir = null)
    {
        var findings = SecurityProjectAnalyzer.Analyze(projects, solutionDir);
        var counts = SecurityFindingCounts.From(findings);

        return new DimensionResult
        {
            Status = "scored",
            Score = Score(counts, importedVulnerabilities),
            Basis = $"Findings: {findings.Count} (errors: {counts.Errors}, warnings: {counts.Warnings}). " +
                    $"hardcodedSecrets={counts.HardcodedSecrets}, rawSql={counts.RawSqlInterpolation}, " +
                    $"unsafeDeserialization={counts.UnsafeDeserialization}, allowAnyOriginWithCredentials={counts.AllowAnyOriginWithCredentials}, " +
                    $"importedVulnerabilities={importedVulnerabilities}.",
            Findings = findings
        };
    }

    private static double Score(SecurityFindingCounts counts, int importedVulnerabilities)
    {
        if (counts.HardcodedSecrets > 2 || counts.AllowAnyOriginWithCredentials > 0)
            return 0;
        if (counts.HardcodedSecrets > 0 || counts.RawSqlInterpolation > 0 || importedVulnerabilities > 0)
            return 2;
        if (counts.UnsafeDeserialization > 0)
            return 4;
        if (counts.Warnings > 2)
            return 6;
        if (counts.Warnings > 0)
            return 8;
        return 10;
    }
}
