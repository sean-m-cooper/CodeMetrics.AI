using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

internal static class FunctionMaintainabilityProbe
{
    public const string Policy = "dotnet/maintainability/source-functions-quintile-40-60-v1";
    public const string MeasurementPolicy = "source-function-own-mi-v1";

    public static DimensionResult Analyze(IReadOnlyList<TypeMetrics> types)
    {
        var observations = types.SelectMany(type => type.ExecutableMetrics!.MaintainabilityFunctions
            .Select(function => new Observation(type, function))).ToArray();
        var missing = observations.Count(item => item.Function.Maintainability == null);
        if (missing > 0)
            return new()
            {
                Status = "failed",
                Basis = $"Own-function MI unavailable for {missing} observations; no score assigned.",
                Extra = new() { ["measurementPolicy"] = MeasurementPolicy, ["missingMeasurements"] = missing }
            };
        if (observations.Length == 0)
            return new()
            {
                Status = "skipped",
                Basis = "No executable functions; maintainability is unmeasured.",
                Extra = new() { ["measurementPolicy"] = MeasurementPolicy, ["eligibleFunctions"] = 0 }
            };

        var functions = observations.Select((item, index) => (item, key: SourceKey(item.Function, index)))
            .GroupBy(item => item.key, StringComparer.Ordinal)
            .Select(group => new SourceFunction(group.Select(item => item.item).ToArray()))
            .OrderBy(item => item.Score).ThenBy(item => item.Mi)
            .ThenBy(item => item.Representative.Function.File, StringComparer.Ordinal)
            .ThenBy(item => item.Representative.Function.SourceSpanStart)
            .ThenBy(item => item.Representative.Function.Name, StringComparer.Ordinal).ToArray();
        int weakestCount = (functions.Length + 4) / 5;
        var weakest = functions.Take(weakestCount).ToArray();
        var remaining = functions.Skip(weakestCount).ToArray();
        decimal weakestWeight = remaining.Length == 0 ? 1m : .4m;
        decimal remainingWeight = 1m - weakestWeight;
        decimal weakestMean = weakest.Average(item => item.Score);
        decimal? remainingMean = remaining.Length == 0 ? null : remaining.Average(item => item.Score);
        var decision = CreateDecision(weakestMean, remainingMean, weakestWeight, remainingWeight);
        AddPopulationInputs(decision, functions, observations, weakestCount);
        return new()
        {
            Status = "scored",
            Score = decision.FinalScore,
            ScoringDecision = decision,
            Basis = $"Distinct executable functions: {functions.Length}. Weakest {weakestCount}: {weakestMean:F2}; " +
                $"remaining {remaining.Length}: {remainingMean:F2}. Each function contributes once; distribution statistics are diagnostic only.",
            Extra = new()
            {
                ["measurementPolicy"] = MeasurementPolicy,
                ["metrics"] = new Dictionary<string, object?>(decision.Inputs) { ["score"] = decision.FinalScore },
                ["topOffenders"] = functions.Take(5).Select(item => item.Describe()).ToArray(),
                ["functionContributions"] = functions.Select((item, index) => new
                {
                    file = item.Representative.Function.File,
                    line = item.Representative.Function.Line,
                    sourceSpanStart = item.Representative.Function.SourceSpanStart,
                    kind = item.Representative.Function.Kind,
                    name = item.Representative.Function.Name,
                    mi = item.Mi,
                    individualScore = item.Score,
                    group = index < weakestCount ? "weakest" : "remaining",
                    weightNumerator = index < weakestCount ? weakestWeight : remainingWeight,
                    weightDenominator = index < weakestCount ? weakestCount : remaining.Length,
                    observationCount = item.Observations.Length
                }).ToArray()
            }
        };
    }

    private static ScoringDecision CreateDecision(decimal weakestMean, decimal? remainingMean,
        decimal weakestWeight, decimal remainingWeight)
    {
        var score = WeightedScore.Calculate(weakestMean, remainingMean, weakestWeight, remainingWeight);
        var weakestPenalty = score.FirstPenalty;
        var remainingPenalty = score.RemainingPenalty;
        var unrounded = score.Unrounded;
        return new()
        {
            Policy = Policy,
            Operation = "deductions",
            FinalScore = score.Rounded,
            Inputs = new()
            {
                ["measurementPolicy"] = MeasurementPolicy,
                ["countingUnit"] = "distinctSourceFunction",
                ["weakestMeanScore"] = weakestMean,
                ["remainingMeanScore"] = remainingMean,
                ["weakestWeight"] = weakestWeight,
                ["remainingWeight"] = remainingWeight,
                ["singleFunctionScope"] = remainingMean == null,
                ["formula"] = remainingMean == null ? "individualFunctionScore" : "0.4 * weakestQuintileMean + 0.6 * remainingMean",
                ["startingScore"] = 10,
                ["unroundedScore"] = unrounded,
                ["roundingDecimals"] = 1,
                ["roundingMode"] = "AwayFromZero",
                ["roundingArithmetic"] = "decimal",
                ["individualAnchors"] = new[] { new { mi = 40, score = 0 }, new { mi = 52, score = 2 },
                    new { mi = 58, score = 4 }, new { mi = 65, score = 6 }, new { mi = 70, score = 8 }, new { mi = 75, score = 10 } },
                ["individualInterpolation"] = "piecewiseLinearClamped0To10",
                ["entryPointMiAdjustment"] = 0,
                ["distributionStatisticsDisposition"] = "diagnosticOnly"
            },
            Steps = [Penalty("weakest", weakestPenalty, true), Penalty("remaining", remainingPenalty, remainingMean != null)]
        };
    }

    private static void AddPopulationInputs(ScoringDecision decision, SourceFunction[] functions,
        Observation[] observations, int weakestCount)
    {
        var inputs = decision.Inputs;
        inputs["eligibleFunctions"] = functions.Length;
        inputs["functionObservations"] = observations.Length;
        inputs["repeatedObservations"] = observations.Length - functions.Length;
        inputs["unidentifiedObservations"] = observations.Count(item => !HasSourceIdentity(item.Function));
        inputs["variantAggregation"] = "minimumOwnMiPerSourceFunction";
        inputs["variantMiDifferences"] = functions.Count(item => item.Observations.Select(o => o.Function.Maintainability!.Index).Distinct().Count() > 1);
        inputs["weakestFunctions"] = weakestCount;
        inputs["remainingFunctions"] = functions.Length - weakestCount;
        inputs["populationPercentBelow60"] = functions.Count(item => item.Mi < 60) * 100m / functions.Length;
        inputs["populationPercentBelow40"] = functions.Count(item => item.Mi < 40) * 100m / functions.Length;
        inputs["p10Mi"] = CodeQualityProbe.Percentile(functions.Select(item => (double)item.Mi).ToList(), 10);
    }

    private static ScoringStep Penalty(string id, decimal value, bool present) => new()
    {
        Id = id,
        Kind = "rule",
        Condition = id + "Functions > 0",
        Matched = present,
        Disposition = present ? "applied" : "notMatched",
        Score = (double)value
    };

    private static decimal IndividualScore(decimal mi) => mi switch
    {
        <= 40 => 0,
        <= 52 => (mi - 40) / 6m,
        <= 58 => 2m + (mi - 52) / 3m,
        <= 65 => 4m + (mi - 58) * 2m / 7m,
        <= 70 => 6m + (mi - 65) * .4m,
        < 75 => 8m + (mi - 70) * .4m,
        _ => 10
    };

    private static bool HasSourceIdentity(ExecutableFunctionMetrics function) =>
        !string.IsNullOrWhiteSpace(function.File) && function.SourceSpanStart is >= 0;

    private static string SourceKey(ExecutableFunctionMetrics function, int index)
    {
        if (!HasSourceIdentity(function)) return "observation:" + index;
        var file = Path.GetFullPath(function.File).Replace('\\', '/');
        if (OperatingSystem.IsWindows()) file = file.ToUpperInvariant();
        return $"source:{file}|{function.SourceSpanStart}|{function.Kind}";
    }

    private sealed record Observation(TypeMetrics Type, ExecutableFunctionMetrics Function);

    private sealed class SourceFunction(Observation[] observations)
    {
        public Observation[] Observations { get; } = observations.OrderBy(item => item.Function.Maintainability!.Index)
            .ThenBy(item => item.Type.Project, StringComparer.Ordinal)
            .ThenBy(item => item.Function.Name, StringComparer.Ordinal).ToArray();
        public Observation Representative => Observations[0];
        public decimal Mi => Representative.Function.Maintainability!.Index;
        public decimal Score => IndividualScore(Mi);
        public object Describe() => new
        {
            project = Representative.Type.Project,
            type = Representative.Type.Type,
            name = Representative.Function.Name,
            kind = Representative.Function.Kind,
            file = Representative.Function.File,
            line = Representative.Function.Line,
            sourceSpanStart = Representative.Function.SourceSpanStart,
            mi = Mi,
            individualScore = Score,
            ownCc = Representative.Function.OwnCyclomaticComplexity,
            sourceLines = Representative.Function.Maintainability!.SourceLines,
            halsteadVolume = Representative.Function.Maintainability.HalsteadVolume,
            observationCount = Observations.Length,
            projectFrameworkObservations = Observations.Select(item => new
            { project = item.Type.Project, mi = item.Function.Maintainability!.Index }).ToArray()
        };
    }
}
