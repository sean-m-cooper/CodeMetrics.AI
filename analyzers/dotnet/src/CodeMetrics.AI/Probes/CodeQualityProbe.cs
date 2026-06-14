using System.Text.Json;
using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

public static class CodeQualityProbe
{
    public static DimensionResult Analyze(IReadOnlyList<TypeMetrics> types)
    {
        var eligible = types.Where(t => t.MemberCount > 0).ToList();

        if (eligible.Count == 0)
        {
            return new DimensionResult
            {
                Status = "scored",
                Score = 10,
                Basis = "No types with members."
            };
        }

        // --- Decomposition signals ---
        var decompositionEligible = eligible.Where(t => t.MemberCount >= 2).ToList();
        var ratios = decompositionEligible.Select(t => t.DecompositionRatio).ToList();
        double popOver4 = decompositionEligible.Count > 0
            ? decompositionEligible.Count(t => t.DecompositionRatio > 4) * 100.0 / decompositionEligible.Count
            : 0.0;
        double p90Ratio = Percentile(ratios, 90);
        double extremeOver15 = decompositionEligible.Count > 0
            ? decompositionEligible.Count(t => t.DecompositionRatio > 15) * 100.0 / decompositionEligible.Count
            : 0.0;

        int popOver4Score = ScoreThreshold(popOver4, [1, 3, 6, 10, 15]);
        int p90RatioScore = ScoreThreshold(p90Ratio, [2.0, 2.5, 3.5, 5.0, 7.0]);
        int extremeOver15Score = ScoreThreshold(extremeOver15, [0.1, 0.5, 1.0, 2.0, 4.0]);
        double decompScore = Math.Round((popOver4Score + p90RatioScore + extremeOver15Score) / 3.0, 1);

        // --- MaxMemberCC signals ---
        var maxCCs = eligible.Select(t => (double)t.MaxMemberCyclomaticComplexity).ToList();
        double popOver15 = eligible.Count(t => t.MaxMemberCyclomaticComplexity > 15) * 100.0 / eligible.Count;
        double p90MaxCC = Percentile(maxCCs, 90);
        double extremeOver30 = eligible.Count(t => t.MaxMemberCyclomaticComplexity > 30) * 100.0 / eligible.Count;

        int popOver15Score = ScoreThreshold(popOver15, [0.5, 2, 4, 7, 10]);
        int p90MaxCCScore = ScoreThreshold(p90MaxCC, [4, 6, 9, 12, 16]);
        int extremeOver30Score = ScoreThreshold(extremeOver30, [0.2, 0.6, 1.2, 2.5, 4.0]);
        double ccScore = Math.Round((popOver15Score + p90MaxCCScore + extremeOver30Score) / 3.0, 1);

        double finalScore = Math.Round((decompScore + ccScore) / 2.0, 1);

        // Top 5 offenders: sort by DecompositionRatio desc, MaxMemberCC desc, Type name
        var offenders = decompositionEligible
            .OrderByDescending(t => t.DecompositionRatio)
            .ThenByDescending(t => t.MaxMemberCyclomaticComplexity)
            .ThenBy(t => t.Type)
            .Take(5)
            .Select(t => new
            {
                project = t.Project,
                @namespace = t.Namespace,
                type = t.Type,
                decompositionRatio = t.DecompositionRatio,
                maxMemberCc = t.MaxMemberCyclomaticComplexity,
                classCc = t.CyclomaticComplexity,
                memberCount = t.MemberCount,
                mi = t.MaintainabilityIndex,
                coupling = t.ClassCoupling,
                loc = t.LinesOfSource
            })
            .ToList();

        var metrics = new
        {
            decomposition = new
            {
                populationPercentOver4 = Math.Round(popOver4, 2),
                populationPercentOver4Score = popOver4Score,
                p90Ratio = Math.Round(p90Ratio, 2),
                p90RatioScore = p90RatioScore,
                extremePercentOver15 = Math.Round(extremeOver15, 2),
                extremePercentOver15Score = extremeOver15Score,
                decompScore
            },
            maxMemberCyclomaticComplexity = new
            {
                populationPercentOver15 = Math.Round(popOver15, 2),
                populationPercentOver15Score = popOver15Score,
                p90MaxCC = Math.Round(p90MaxCC, 2),
                p90MaxCCScore = p90MaxCCScore,
                extremePercentOver30 = Math.Round(extremeOver30, 2),
                extremePercentOver30Score = extremeOver30Score,
                ccScore
            }
        };

        var extra = new Dictionary<string, object?>
        {
            ["metrics"] = JsonSerializer.SerializeToElement(metrics),
            ["topOffenders"] = JsonSerializer.SerializeToElement(offenders)
        };

        var basis = $"Eligible types: {eligible.Count}. DecompScore: {decompScore}, CCScore: {ccScore}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = finalScore,
            Basis = basis,
            Extra = extra
        };
    }

    /// <summary>
    /// Scores a value against ascending thresholds [t10, t8, t6, t4, t2].
    /// value &lt;= t10 → 10, value &lt;= t8 → 8, ..., value &lt;= t2 → 2, else → 0.
    /// </summary>
    public static int ScoreThreshold(double value, double[] thresholds)
    {
        // thresholds = [t10, t8, t6, t4, t2]
        int[] scores = [10, 8, 6, 4, 2];
        for (int i = 0; i < thresholds.Length; i++)
        {
            if (value <= thresholds[i])
                return scores[i];
        }
        return 0;
    }

    /// <summary>
    /// Computes the p-th percentile of values using linear interpolation.
    /// </summary>
    public static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return 0;
        if (values.Count == 1) return values[0];

        var sorted = values.OrderBy(v => v).ToList();
        double rank = (p / 100.0) * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        double fraction = rank - lower;
        return sorted[lower] + fraction * (sorted[upper] - sorted[lower]);
    }
}
