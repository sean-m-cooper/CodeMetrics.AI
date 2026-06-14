using System.Text.Json;
using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

public static class MaintainabilityProbe
{
    public static DimensionResult Analyze(IReadOnlyList<TypeMetrics> types)
    {
        if (types.Count == 0)
        {
            return new DimensionResult
            {
                Status = "scored",
                Score = 10,
                Basis = "No types found."
            };
        }

        var miValues = types.Select(t => (double)t.MaintainabilityIndex).ToList();

        double popBelow60 = types.Count(t => t.MaintainabilityIndex < 60) * 100.0 / types.Count;
        double p10MI = CodeQualityProbe.Percentile(miValues, 10);
        double extremeBelow40 = types.Count(t => t.MaintainabilityIndex < 40) * 100.0 / types.Count;

        int popBelow60Score = CodeQualityProbe.ScoreThreshold(popBelow60, [1, 3, 6, 10, 15]);
        int p10Score = ScoreThresholdReverse(p10MI, [75, 70, 65, 58, 52]);
        int extremeBelow40Score = CodeQualityProbe.ScoreThreshold(extremeBelow40, [0.2, 0.5, 1.2, 2.5, 4.0]);

        double finalScore = Math.Round((popBelow60Score + p10Score + extremeBelow40Score) / 3.0, 1);

        // Top 5 offenders: sort by MI asc, Type name
        var offenders = types
            .Where(t => !IsEntryPointType(t))
            .OrderBy(t => t.MaintainabilityIndex)
            .ThenBy(t => t.Type)
            .Take(5)
            .Select(t => new
            {
                project = t.Project,
                @namespace = t.Namespace,
                type = t.Type,
                mi = t.MaintainabilityIndex,
                classCc = t.CyclomaticComplexity,
                memberCount = t.MemberCount,
                coupling = t.ClassCoupling,
                loc = t.LinesOfSource
            })
            .ToList();

        var metrics = new
        {
            maintainabilityIndex = new
            {
                populationPercentBelow60 = Math.Round(popBelow60, 2),
                populationPercentBelow60Score = popBelow60Score,
                p10MI = Math.Round(p10MI, 2),
                p10Score,
                extremePercentBelow40 = Math.Round(extremeBelow40, 2),
                extremePercentBelow40Score = extremeBelow40Score,
                finalScore
            }
        };

        var extra = new Dictionary<string, object?>
        {
            ["metrics"] = JsonSerializer.SerializeToElement(metrics),
            ["topOffenders"] = JsonSerializer.SerializeToElement(offenders)
        };

        var basis = $"Total types: {types.Count}. PopBelow60Score: {popBelow60Score}, P10Score: {p10Score}, ExtremeBelow40Score: {extremeBelow40Score}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = finalScore,
            Basis = basis,
            Extra = extra
        };
    }

    private static bool IsEntryPointType(TypeMetrics type)
    {
        return type.Type is "Program" or "Startup" ||
               type.FilePath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) ||
               type.FilePath.EndsWith("Startup.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scores a value against descending thresholds [t10, t8, t6, t4, t2].
    /// value &gt;= t10 → 10, value &gt;= t8 → 8, ..., value &gt;= t2 → 2, else → 0.
    /// </summary>
    private static int ScoreThresholdReverse(double value, double[] thresholds)
    {
        // thresholds = [t10, t8, t6, t4, t2] (descending)
        int[] scores = [10, 8, 6, 4, 2];
        for (int i = 0; i < thresholds.Length; i++)
        {
            if (value >= thresholds[i])
                return scores[i];
        }
        return 0;
    }
}
