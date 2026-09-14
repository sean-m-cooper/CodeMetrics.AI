using System.Text.Json;
using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

public static class CodeQualityProbe
{
    // Null executable metrics identify explicitly supplied legacy TypeMetrics.
    // The production collector always supplies executable metrics, including empty populations.
    private static int FunctionCount(TypeMetrics type) => type.ExecutableMetrics?.FunctionCount ?? type.MemberCount;
    private static int DecompositionCount(TypeMetrics type) => type.ExecutableMetrics?.DecompositionFunctionCount ?? type.MemberCount;
    private static int MaxComplexity(TypeMetrics type) => type.ExecutableMetrics?.MaxComplexity ?? type.MaxMemberCyclomaticComplexity;
    private static double Ratio(TypeMetrics type) => type.ExecutableMetrics?.DecompositionRatio ?? type.DecompositionRatio;

    public static DimensionResult Analyze(IReadOnlyList<TypeMetrics> types)
    {
        var excludedDataCarriers = types.Count(t => t.IsDataCarrier);
        var eligible = types.Where(t => FunctionCount(t) > 0 && !t.IsDataCarrier).ToList();
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
            ScoringDecision = ScoringDecision.FirstMatch("dotnet/codeQuality/method-population-v3", new() { ["eligibleTypes"] = 0 },
                ScoringStep.Rule("emptyPopulation", "eligibleTypes == 0", true, 10)),
            Basis = excludedDataCarriers > 0
                ? $"No behavior-bearing types with members. Passive data carriers excluded: {excludedDataCarriers}."
                : "No types with members.",
            Extra = new Dictionary<string, object?>
            {
                ["displayName"] = "Complexity & Decomposition",
                ["measurementPolicy"] = "executable-function-ownership-v1",
                ["componentDetails"] = CreateComponentDetails([], null, null)
            }
        };
    }

    private static DecompositionScores AnalyzeDecomposition(IReadOnlyList<TypeMetrics> eligible)
    {
        var decompositionEligible = eligible.Where(t => DecompositionCount(t) >= 2).ToList();
        var ratios = decompositionEligible.Select(t => Ratio(t)).ToList();
        var populationOver4 = decompositionEligible.Count > 0
            ? decompositionEligible.Count(t => Ratio(t) > 4) * 100.0 / decompositionEligible.Count
            : 0.0;
        var p90Ratio = Percentile(ratios, 90);
        var extremeOver15 = decompositionEligible.Count > 0
            ? decompositionEligible.Count(t => Ratio(t) > 15) * 100.0 / decompositionEligible.Count
            : 0.0;
        var decision = ScoringDecision.Mean("dotnet/codeQuality/decomposition/executable-functions-v2",
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
        if (eligible.All(type => type.ExecutableMetrics != null))
        {
            var population = MethodComplexityProbe.Analyze(eligible);
            return new ComplexityScores(population.Decision, population.Metrics, population.Details);
        }

        // Explicit legacy/mixed caller input cannot supply a complete function population.
        // Keep its former type-maximum policy and disclose the fallback; never invent functions.
        var maxCCs = eligible.Select(t => (double)MaxComplexity(t)).ToList();
        var populationOver15 = eligible.Count(t => MaxComplexity(t) > 15) * 100.0 / eligible.Count;
        var p90MaxCc = Percentile(maxCCs, 90);
        var extremeOver30 = eligible.Count(t => MaxComplexity(t) > 30) * 100.0 / eligible.Count;
        var decision = ScoringDecision.Mean("dotnet/codeQuality/complexity/executable-functions-v2",
            ScoringDecision.Threshold("complexity/population", populationOver15, [0.5, 2, 4, 7, 10]),
            ScoringDecision.Threshold("complexity/tail", p90MaxCc, [4, 6, 9, 12, 16]),
            ScoringDecision.Threshold("complexity/extreme", extremeOver30, [0.2, 0.6, 1.2, 2.5, 4.0]));
        var populationScore = (int)decision.Steps[0].Score;
        var p90Score = (int)decision.Steps[1].Score;
        var extremeScore = (int)decision.Steps[2].Score;
        decision.Inputs["measurementPolicy"] = "legacy-type-maxima-v1";
        return new ComplexityScores(decision, new
        {
            populationPercentOver15 = Math.Round(populationOver15, 2),
            populationPercentOver15Score = populationScore,
            p90MaxCC = Math.Round(p90MaxCc, 2),
            p90MaxCCScore = p90Score,
            extremePercentOver30 = Math.Round(extremeOver30, 2),
            extremePercentOver30Score = extremeScore,
            ccScore = decision.FinalScore
        });
    }

    private static object CreateOffenders(IEnumerable<TypeMetrics> eligible, bool methodComplexity = false)
    {
        var ranked = methodComplexity
            ? eligible.OrderByDescending(type => MaxComplexity(type)).ThenByDescending(type => Ratio(type))
            : eligible.Where(type => DecompositionCount(type) >= 2).OrderByDescending(type => Ratio(type)).ThenByDescending(type => MaxComplexity(type));
        return ranked
            .ThenBy(type => type.Type, StringComparer.Ordinal)
            .ThenBy(type => type.Project, StringComparer.Ordinal)
            .ThenBy(type => type.Namespace, StringComparer.Ordinal)
            .ThenBy(type => type.FilePath, StringComparer.Ordinal)
            .Take(5)
            .Select(type => new
            {
                project = type.Project,
                @namespace = type.Namespace,
                type = type.Type,
                typeId = type.TypeId,
                sourceFiles = type.SourceFiles,
                decompositionRatio = Ratio(type),
                maxMemberCc = MaxComplexity(type),
                classCc = type.CyclomaticComplexity,
                memberCount = type.MemberCount,
                rawDecompositionRatio = type.DecompositionRatio,
                rawMaxMemberCc = type.MaxMemberCyclomaticComplexity,
                executableFunctionCount = FunctionCount(type),
                decompositionFunctionCount = DecompositionCount(type),
                decompositionComplexity = type.ExecutableMetrics?.DecompositionComplexity ?? type.CyclomaticComplexity,
                functions = type.ExecutableMetrics?.Functions.OrderByDescending(function => function.OwnCyclomaticComplexity)
                    .ThenBy(function => function.File, StringComparer.Ordinal).ThenBy(function => function.Line)
                    .Take(5).ToArray(),
                mi = type.MaintainabilityIndex,
                coupling = type.ClassCoupling,
                loc = type.LinesOfSource
            })
            .ToList();
    }

    private static JsonElement CreateComponentDetails(IReadOnlyList<TypeMetrics> eligible, double? decomposition, double? complexity, object? complexityDetails = null) =>
        JsonSerializer.SerializeToElement(new
        {
            methodComplexity = complexityDetails ?? new
            {
                label = "Method complexity",
                score = complexity,
                eligibleTypes = eligible.Count,
                measure = "Distribution of each eligible type's maximum own executable-function CC, including local functions and callbacks separately; not the percentage of all functions. Legacy input types retain member maxima.",
                limitation = "Branching is a review signal, not proof of incorrectness or avoidable complexity.",
                topOffenders = CreateOffenders(eligible, methodComplexity: true)
            },
            decomposition = new
            {
                label = "Decomposition",
                score = decomposition,
                eligibleTypes = eligible.Count(type => DecompositionCount(type) >= 2),
                measure = "Distribution of executable complexity divided by decomposition-function count (at least two). Fields, bodyless members and branch-free callbacks/initializers do not increase the denominator. Legacy input types retain member ratios.",
                limitation = "Does not establish cohesion or readability. Extracting trivial helpers can improve the ratio without improving the code. A zero eligible population is unmeasured, even when policy supplies a default score.",
                topOffenders = CreateOffenders(eligible)
            }
        });

    private static DimensionResult CreateResult(
        IReadOnlyList<TypeMetrics> eligible,
        int excludedDataCarriers,
        DecompositionScores decomposition,
        ComplexityScores complexity)
    {
        var decision = ScoringDecision.Mean(complexity.Details == null ? "dotnet/codeQuality/executable-functions-v2" : "dotnet/codeQuality/method-population-v3",
            ScoringStep.Component("decomposition", decomposition.Score, decision: decomposition.Decision),
            ScoringStep.Component("complexity", complexity.Score, decision: complexity.Decision));
        decision.Inputs["eligibleTypes"] = eligible.Count;
        decision.Inputs["measurementPolicy"] = "executable-function-ownership-v1";
        decision.Inputs["complexityMeasurementPolicy"] = complexity.Details == null ? "legacy-type-maxima-v1" : MethodComplexityProbe.MeasurementPolicy;
        decision.Inputs["percentileInterpolation"] = "linear-decimal-v1";
        decision.Inputs["legacyInputTypes"] = eligible.Count(type => type.ExecutableMetrics == null);
        decision.Inputs["executableFunctions"] = eligible.Sum(FunctionCount);
        decision.Inputs["decompositionFunctions"] = eligible.Sum(DecompositionCount);
        decision.Inputs["decompositionEligibleTypes"] = eligible.Count(type => DecompositionCount(type) >= 2);
        var finalScore = decision.FinalScore;
        var metrics = new Dictionary<string, object?>
        {
            ["filtering"] = new
            {
                passiveDataCarriersExcluded = excludedDataCarriers
            },
            ["decomposition"] = new
            {
                populationPercentOver4 = Math.Round(decomposition.PopulationPercent, 2),
                populationPercentOver4Score = decomposition.PopulationScore,
                p90Ratio = Math.Round(decomposition.P90, 2),
                p90RatioScore = decomposition.P90Score,
                extremePercentOver15 = Math.Round(decomposition.ExtremePercent, 2),
                extremePercentOver15Score = decomposition.ExtremeScore,
                decompScore = decomposition.Score
            },
            [complexity.Details == null ? "maxMemberCyclomaticComplexity" : "methodComplexity"] = complexity.Metrics
        };

        var extra = new Dictionary<string, object?>
        {
            ["displayName"] = "Complexity & Decomposition",
            ["measurementPolicy"] = "executable-function-ownership-v1",
            ["complexityMeasurementPolicy"] = decision.Inputs["complexityMeasurementPolicy"],
            ["componentDetails"] = CreateComponentDetails(eligible, decomposition.Score, complexity.Score, complexity.Details),
            ["metrics"] = JsonSerializer.SerializeToElement(metrics),
            ["topOffenders"] = JsonSerializer.SerializeToElement(CreateOffenders(eligible))
        };

        var basis = $"Eligible types: {eligible.Count}. Passive data carriers excluded: {excludedDataCarriers}. " +
                    $"Method complexity: {complexity.Score}, Decomposition: {decomposition.Score}. Combined: {finalScore}.";

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

    private sealed record ComplexityScores(ScoringDecision Decision, object Metrics, object? Details = null)
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
        // Inputs are integer CC/MI or finite decimal decomposition ratios. Decimal
        // interpolation preserves exact threshold boundaries without rounding to display precision.
        decimal rank = (decimal)p / 100m * (sorted.Count - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sorted[lower];
        decimal fraction = rank - lower;
        return (double)((decimal)sorted[lower] + fraction * ((decimal)sorted[upper] - (decimal)sorted[lower]));
    }
}
