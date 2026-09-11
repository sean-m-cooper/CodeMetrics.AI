using System.Text.Json;
using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

public static class CodeQualityProbe
{
    public static DimensionResult Analyze(IReadOnlyList<TypeMetrics> types)
    {
        var excludedDataCarriers = types.Count(t => t.IsDataCarrier);
        var eligible = types.Where(t => t.MemberCount > 0 && !t.IsDataCarrier).ToList();
        if (eligible.Count == 0)
            return CreateEmptyResult(excludedDataCarriers);

        var decomposition = AnalyzeDecomposition(eligible);
        var complexity = AnalyzeComplexity(eligible);
        return CreateResult(
            eligible,
            excludedDataCarriers,
            decomposition,
            complexity);
    }

    private static DimensionResult CreateEmptyResult(int excludedDataCarriers)
    {
        return new DimensionResult
        {
            Status = "scored",
            Score = 10,
            ScoringDecision = ScoringDecision.FirstMatch("dotnet/codeQuality/v1", new() { ["eligibleTypes"] = 0 },
                ScoringStep.Rule("emptyPopulation", "eligibleTypes == 0", true, 10)),
            Basis = excludedDataCarriers > 0
                ? $"No behavior-bearing types with members. Passive data carriers excluded: {excludedDataCarriers}."
                : "No types with members."
        };
    }

    private static DecompositionScores AnalyzeDecomposition(IReadOnlyList<TypeMetrics> eligible)
    {
        var decompositionEligible = eligible.Where(t => t.MemberCount >= 2).ToList();
        var ratios = decompositionEligible.Select(t => t.DecompositionRatio).ToList();
        var populationOver4 = decompositionEligible.Count > 0
            ? decompositionEligible.Count(t => t.DecompositionRatio > 4) * 100.0 / decompositionEligible.Count
            : 0.0;
        var p90Ratio = Percentile(ratios, 90);
        var extremeOver15 = decompositionEligible.Count > 0
            ? decompositionEligible.Count(t => t.DecompositionRatio > 15) * 100.0 / decompositionEligible.Count
            : 0.0;
        var decision = ScoringDecision.Mean("dotnet/codeQuality/decomposition/v1",
            ScoringDecision.Threshold("decomposition/population", populationOver4, [1, 3, 6, 10, 15]),
            ScoringDecision.Threshold("decomposition/tail", p90Ratio, [2.0, 2.5, 3.5, 5.0, 7.0]),
            ScoringDecision.Threshold("decomposition/extreme", extremeOver15, [0.1, 0.5, 1.0, 2.0, 4.0]));
        var populationScore = (int)decision.Steps[0].Score;
        var p90Score = (int)decision.Steps[1].Score;
        var extremeScore = (int)decision.Steps[2].Score;
        return new DecompositionScores(
            populationOver4,
            populationScore,
            p90Ratio,
            p90Score,
            extremeOver15,
            extremeScore,
            decision);
    }

    private static ComplexityScores AnalyzeComplexity(IReadOnlyList<TypeMetrics> eligible)
    {
        var maxCCs = eligible.Select(t => (double)t.MaxMemberCyclomaticComplexity).ToList();
        var populationOver15 = eligible.Count(t => t.MaxMemberCyclomaticComplexity > 15) * 100.0 / eligible.Count;
        var p90MaxCc = Percentile(maxCCs, 90);
        var extremeOver30 = eligible.Count(t => t.MaxMemberCyclomaticComplexity > 30) * 100.0 / eligible.Count;
        var decision = ScoringDecision.Mean("dotnet/codeQuality/complexity/v1",
            ScoringDecision.Threshold("complexity/population", populationOver15, [0.5, 2, 4, 7, 10]),
            ScoringDecision.Threshold("complexity/tail", p90MaxCc, [4, 6, 9, 12, 16]),
            ScoringDecision.Threshold("complexity/extreme", extremeOver30, [0.2, 0.6, 1.2, 2.5, 4.0]));
        var populationScore = (int)decision.Steps[0].Score;
        var p90Score = (int)decision.Steps[1].Score;
        var extremeScore = (int)decision.Steps[2].Score;
        return new ComplexityScores(
            populationOver15,
            populationScore,
            p90MaxCc,
            p90Score,
            extremeOver30,
            extremeScore,
            decision);
    }

    private static object CreateOffenders(IEnumerable<TypeMetrics> eligible)
    {
        return eligible
            .Where(type => type.MemberCount >= 2)
            .OrderByDescending(type => type.DecompositionRatio)
            .ThenByDescending(type => type.MaxMemberCyclomaticComplexity)
            .ThenBy(type => type.Type)
            .Take(5)
            .Select(type => new
            {
                project = type.Project,
                @namespace = type.Namespace,
                type = type.Type,
                decompositionRatio = type.DecompositionRatio,
                maxMemberCc = type.MaxMemberCyclomaticComplexity,
                classCc = type.CyclomaticComplexity,
                memberCount = type.MemberCount,
                mi = type.MaintainabilityIndex,
                coupling = type.ClassCoupling,
                loc = type.LinesOfSource
            })
            .ToList();
    }

    private static DimensionResult CreateResult(
        IReadOnlyList<TypeMetrics> eligible,
        int excludedDataCarriers,
        DecompositionScores decomposition,
        ComplexityScores complexity)
    {
        var decision = ScoringDecision.Mean("dotnet/codeQuality/v1",
            ScoringStep.Component("decomposition", decomposition.Score, decision: decomposition.Decision),
            ScoringStep.Component("complexity", complexity.Score, decision: complexity.Decision));
        decision.Inputs["eligibleTypes"] = eligible.Count;
        decision.Inputs["decompositionEligibleTypes"] = eligible.Count(type => type.MemberCount >= 2);
        var finalScore = decision.FinalScore;
        var metrics = new
        {
            filtering = new
            {
                passiveDataCarriersExcluded = excludedDataCarriers
            },
            decomposition = new
            {
                populationPercentOver4 = Math.Round(decomposition.PopulationPercent, 2),
                populationPercentOver4Score = decomposition.PopulationScore,
                p90Ratio = Math.Round(decomposition.P90, 2),
                p90RatioScore = decomposition.P90Score,
                extremePercentOver15 = Math.Round(decomposition.ExtremePercent, 2),
                extremePercentOver15Score = decomposition.ExtremeScore,
                decompScore = decomposition.Score
            },
            maxMemberCyclomaticComplexity = new
            {
                populationPercentOver15 = Math.Round(complexity.PopulationPercent, 2),
                populationPercentOver15Score = complexity.PopulationScore,
                p90MaxCC = Math.Round(complexity.P90, 2),
                p90MaxCCScore = complexity.P90Score,
                extremePercentOver30 = Math.Round(complexity.ExtremePercent, 2),
                extremePercentOver30Score = complexity.ExtremeScore,
                ccScore = complexity.Score
            }
        };

        var extra = new Dictionary<string, object?>
        {
            ["metrics"] = JsonSerializer.SerializeToElement(metrics),
            ["topOffenders"] = JsonSerializer.SerializeToElement(CreateOffenders(eligible))
        };

        var basis = $"Eligible types: {eligible.Count}. Passive data carriers excluded: {excludedDataCarriers}. " +
                    $"DecompScore: {decomposition.Score}, CCScore: {complexity.Score}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = finalScore,
            ScoringDecision = decision,
            Basis = basis,
            Extra = extra
        };
    }

    private sealed record DecompositionScores(
        double PopulationPercent,
        int PopulationScore,
        double P90,
        int P90Score,
        double ExtremePercent,
        int ExtremeScore,
        ScoringDecision Decision)
    {
        public double Score => Decision.FinalScore;
    }

    private sealed record ComplexityScores(
        double PopulationPercent,
        int PopulationScore,
        double P90,
        int P90Score,
        double ExtremePercent,
        int ExtremeScore,
        ScoringDecision Decision)
    {
        public double Score => Decision.FinalScore;
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
