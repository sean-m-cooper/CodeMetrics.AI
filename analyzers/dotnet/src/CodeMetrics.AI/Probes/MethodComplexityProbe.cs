using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

internal static class MethodComplexityProbe
{
    public const string Policy = "dotnet/codeQuality/complexity/source-functions-40-60-v1";
    public const string MeasurementPolicy = "source-function-own-cc-v1";

    public static MethodComplexityResult Analyze(IReadOnlyList<TypeMetrics> types)
    {
        var observations = types.SelectMany(type => type.ExecutableMetrics!.Functions
            .Select(function => new Observation(type, function))).ToList();
        var functions = observations.Select((observation, index) => (observation, key: SourceKey(observation.Function, index)))
            .GroupBy(item => item.key, StringComparer.Ordinal)
            .Select(group => new SourceFunction(group.Select(item => item.observation).ToArray()))
            .OrderByDescending(function => function.Complexity)
            .ThenBy(function => function.Representative.Function.File, StringComparer.Ordinal)
            .ThenBy(function => function.Representative.Function.SourceSpanStart)
            .ThenBy(function => function.Representative.Function.Name, StringComparer.Ordinal)
            .ThenBy(function => function.Representative.Type.Project, StringComparer.Ordinal)
            .ToList();

        // The caller supplies a nonempty executable population. Remove exactly one
        // worst method; tied worst methods remain in the population contribution.
        var worst = functions[0];
        var remaining = functions.Skip(1).ToList();
        decimal worstWeight = remaining.Count == 0 ? 1m : .4m;
        decimal remainingWeight = 1m - worstWeight;
        decimal? remainingMean = remaining.Count == 0 ? null : remaining.Average(function => function.Score);
        decimal worstPenalty = worstWeight * (10m - worst.Score);
        decimal remainingPenalty = remainingWeight * (10m - (remainingMean ?? 10m));
        decimal unrounded = 10m - worstPenalty - remainingPenalty;
        decimal ceiling = 10m - worstPenalty;
        var decision = new ScoringDecision
        {
            Policy = Policy,
            // Express the weighted mean as equivalent deductions using the existing
            // schema operation; step scores are weighted shortfalls, not method scores.
            Operation = "deductions",
            FinalScore = (double)Math.Round(unrounded, 1, MidpointRounding.AwayFromZero),
            Inputs = new()
            {
                ["measurementPolicy"] = MeasurementPolicy,
                ["countingUnit"] = "distinctSourceFunction",
                ["variantAggregation"] = "maximumOwnCcPerSourceFunction",
                ["eligibleFunctions"] = functions.Count,
                ["functionObservations"] = observations.Count,
                ["repeatedObservations"] = observations.Count - functions.Count,
                ["unidentifiedObservations"] = observations.Count(item => !HasSourceIdentity(item.Function)),
                ["variantComplexityDifferences"] = functions.Count(function => function.Observations.Select(item => item.Function.OwnCyclomaticComplexity).Distinct().Count() > 1),
                ["lowFunctions"] = functions.Count(function => function.Complexity <= 5),
                ["moderateFunctions"] = functions.Count(function => function.Complexity is >= 6 and <= 10),
                ["highFunctions"] = functions.Count(function => function.Complexity is >= 11 and <= 20),
                ["severeFunctions"] = functions.Count(function => function.Complexity >= 21),
                ["individualAnchors"] = new[] { new { cc = 3, score = 10 }, new { cc = 5, score = 8 }, new { cc = 10, score = 6 }, new { cc = 20, score = 4 }, new { cc = 40, score = 0 } },
                ["individualInterpolation"] = "piecewiseLinearClamped0To10",
                ["worstOwnCc"] = worst.Complexity,
                ["worstMethodScore"] = worst.Score,
                ["worstWeight"] = worstWeight,
                ["remainingFunctions"] = remaining.Count,
                ["remainingMeanScore"] = remainingMean,
                ["remainingWeight"] = remainingWeight,
                ["singleFunctionScope"] = remaining.Count == 0,
                ["aggregateCeiling"] = ceiling,
                ["formula"] = remaining.Count == 0 ? "individualMethodScore" : "0.4 * worstMethodScore + 0.6 * remainingMeanScore",
                ["startingScore"] = 10,
                ["unclampedScore"] = unrounded,
                ["unroundedScore"] = unrounded,
                ["minimumScore"] = 0,
                ["maximumScore"] = 10,
                ["roundingDecimals"] = 1,
                ["roundingMode"] = "AwayFromZero",
                ["roundingArithmetic"] = "decimal"
            },
            Steps = [
                Penalty("worstMethod", worstPenalty, true),
                Penalty("remainingPopulation", remainingPenalty, remaining.Count > 0)]
        };
        var metrics = new Dictionary<string, object?>(decision.Inputs) { ["score"] = decision.FinalScore };
        var details = new
        {
            label = "Method complexity",
            score = decision.FinalScore,
            measurementPolicy = MeasurementPolicy,
            eligibleTypes = types.Count,
            eligibleFunctions = functions.Count,
            functionObservations = observations.Count,
            singleFunctionScope = remaining.Count == 0,
            measure = "40% worst individual-function score plus 60% mean of the remaining functions; one worst function is excluded from that mean. A single-function scope uses its individual score. Shared source observations count once, using maximum own CC across variants.",
            limitation = "Product-defined branching burden, not proof of defects, readability or runtime performance. Small populations need context. Missing source identities remain separate observations. Decomposition retains its type-instance population.",
            worstFunction = worst.Describe(),
            topOffenders = functions.Take(5).Select(function => function.Describe()).ToArray()
        };
        return new MethodComplexityResult(decision, metrics, details);
    }

    private static ScoringStep Penalty(string id, decimal value, bool matched) => new()
    {
        Id = id,
        Kind = "rule",
        Condition = id == "worstMethod" ? "eligibleFunctions > 0" : "remainingFunctions > 0",
        Matched = matched,
        Disposition = matched ? "applied" : "notMatched",
        Score = (double)value
    };

    private static decimal IndividualScore(int cc) => cc switch
    {
        <= 3 => 10m,
        <= 5 => 10m - (cc - 3),
        <= 10 => 8m - (cc - 5) * .4m,
        _ => Math.Max(0m, 6m - (cc - 10) * .2m)
    };

    private static bool HasSourceIdentity(ExecutableFunctionMetrics function) =>
        !string.IsNullOrWhiteSpace(function.File) && function.SourceSpanStart is >= 0;

    private static string SourceKey(ExecutableFunctionMetrics function, int observationIndex)
    {
        if (!HasSourceIdentity(function)) return "observation:" + observationIndex;
        var file = Path.GetFullPath(function.File).Replace('\\', '/');
        if (OperatingSystem.IsWindows()) file = file.ToUpperInvariant();
        // Start and kind identify authored functions even when conditional compilation
        // changes their bodies or signatures. Different sites on one line stay distinct.
        return $"source:{file}|{function.SourceSpanStart}|{function.Kind}";
    }

    private sealed record Observation(TypeMetrics Type, ExecutableFunctionMetrics Function);

    private sealed class SourceFunction(Observation[] observations)
    {
        public Observation[] Observations { get; } = observations.OrderByDescending(item => item.Function.OwnCyclomaticComplexity)
            .ThenBy(item => item.Type.Project, StringComparer.Ordinal)
            .ThenBy(item => item.Function.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Function.File, StringComparer.Ordinal).ToArray();
        public Observation Representative => Observations[0];
        public int Complexity => Representative.Function.OwnCyclomaticComplexity;
        public decimal Score => IndividualScore(Complexity);

        public object Describe() => new
        {
            project = Representative.Type.Project,
            @namespace = Representative.Type.Namespace,
            type = Representative.Type.Type,
            name = Representative.Function.Name,
            kind = Representative.Function.Kind,
            file = Representative.Function.File,
            line = Representative.Function.Line,
            sourceSpanStart = Representative.Function.SourceSpanStart,
            sourceSpanLength = Representative.Function.SourceSpanLength,
            ownCc = Complexity,
            individualScore = Score,
            classification = Complexity switch { <= 5 => "low", <= 10 => "moderate", <= 20 => "high", _ => "severe" },
            observationCount = Observations.Length,
            affectedProjects = Observations.Select(item => item.Type.Project).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            projectFrameworkObservations = Observations.Select(item => new { project = item.Type.Project, name = item.Function.Name, ownCc = item.Function.OwnCyclomaticComplexity }).ToArray()
        };
    }
}

internal sealed record MethodComplexityResult(ScoringDecision Decision, object Metrics, object Details);
