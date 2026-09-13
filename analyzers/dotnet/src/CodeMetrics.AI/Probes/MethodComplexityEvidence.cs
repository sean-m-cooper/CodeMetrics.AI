namespace CodeMetrics.AI.Probes;

internal static class MethodComplexityEvidence
{
    public static MethodComplexityResult Create(MethodComplexityPopulation population, MethodComplexityScoring.Assessment assessment)
    {
        var decision = CreateDecision(population, assessment);
        var metrics = new Dictionary<string, object?>(decision.Inputs) { ["score"] = decision.FinalScore };
        var details = new
        {
            label = "Method complexity",
            score = decision.FinalScore,
            measurementPolicy = MethodComplexityScoring.MeasurementPolicy,
            eligibleTypes = population.TypeCount,
            eligibleFunctions = population.Functions.Count,
            functionObservations = population.Observations.Count,
            singleFunctionScope = assessment.RemainingCount == 0,
            measure = "40% worst individual-function score plus 60% mean of the remaining functions; one worst function is excluded from that mean. A single-function scope uses its individual score. Shared source observations count once, using maximum own CC across variants.",
            limitation = "Product-defined branching burden, not proof of defects, readability or runtime performance. Small populations need context. Missing source identities remain separate observations. Decomposition retains its type-instance population.",
            worstFunction = Describe(assessment.Worst),
            topOffenders = population.Functions.Take(5).Select(Describe).ToArray()
        };
        return new MethodComplexityResult(decision, metrics, details);
    }

    private static ScoringDecision CreateDecision(MethodComplexityPopulation population, MethodComplexityScoring.Assessment assessment)
    {
        return new ScoringDecision
        {
            Policy = MethodComplexityScoring.Policy,
            // Express the weighted mean as equivalent deductions using the existing
            // schema operation; step scores are weighted shortfalls, not method scores.
            Operation = "deductions",
            FinalScore = assessment.FinalScore,
            Inputs = new()
            {
                ["measurementPolicy"] = MethodComplexityScoring.MeasurementPolicy,
                ["countingUnit"] = "distinctSourceFunction",
                ["variantAggregation"] = "maximumOwnCcPerSourceFunction",
                ["eligibleFunctions"] = population.Functions.Count,
                ["functionObservations"] = population.Observations.Count,
                ["repeatedObservations"] = population.Observations.Count - population.Functions.Count,
                ["unidentifiedObservations"] = population.Observations.Count(item => !MethodComplexityPopulation.HasSourceIdentity(item.Function)),
                ["variantComplexityDifferences"] = population.Functions.Count(function => function.Observations.Select(item => item.Function.OwnCyclomaticComplexity).Distinct().Count() > 1),
                ["lowFunctions"] = population.Functions.Count(function => function.Complexity <= 5),
                ["moderateFunctions"] = population.Functions.Count(function => function.Complexity is >= 6 and <= 10),
                ["highFunctions"] = population.Functions.Count(function => function.Complexity is >= 11 and <= 20),
                ["severeFunctions"] = population.Functions.Count(function => function.Complexity >= 21),
                ["individualAnchors"] = new[] { new { cc = 3, score = 10 }, new { cc = 5, score = 8 }, new { cc = 10, score = 6 }, new { cc = 20, score = 4 }, new { cc = 40, score = 0 } },
                ["individualInterpolation"] = "piecewiseLinearClamped0To10",
                ["worstOwnCc"] = assessment.Worst.Complexity,
                ["worstMethodScore"] = assessment.WorstScore,
                ["worstWeight"] = assessment.WorstWeight,
                ["remainingFunctions"] = assessment.RemainingCount,
                ["remainingMeanScore"] = assessment.RemainingMean,
                ["remainingWeight"] = assessment.RemainingWeight,
                ["singleFunctionScope"] = assessment.RemainingCount == 0,
                ["aggregateCeiling"] = assessment.Ceiling,
                ["formula"] = assessment.RemainingCount == 0 ? "individualMethodScore" : "0.4 * worstMethodScore + 0.6 * remainingMeanScore",
                ["startingScore"] = 10,
                ["unclampedScore"] = assessment.Unrounded,
                ["unroundedScore"] = assessment.Unrounded,
                ["minimumScore"] = 0,
                ["maximumScore"] = 10,
                ["roundingDecimals"] = 1,
                ["roundingMode"] = "AwayFromZero",
                ["roundingArithmetic"] = "decimal"
            },
            Steps = [
                Penalty("worstMethod", assessment.WorstPenalty, true),
                Penalty("remainingPopulation", assessment.RemainingPenalty, assessment.RemainingCount > 0)]
        };
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

    private static object Describe(MethodComplexityPopulation.SourceFunction function) => new
    {
        project = function.Representative.Type.Project,
        @namespace = function.Representative.Type.Namespace,
        type = function.Representative.Type.Type,
        name = function.Representative.Function.Name,
        kind = function.Representative.Function.Kind,
        file = function.Representative.Function.File,
        line = function.Representative.Function.Line,
        sourceSpanStart = function.Representative.Function.SourceSpanStart,
        sourceSpanLength = function.Representative.Function.SourceSpanLength,
        ownCc = function.Complexity,
        individualScore = MethodComplexityScoring.IndividualScore(function.Complexity),
        classification = function.Complexity switch { <= 5 => "low", <= 10 => "moderate", <= 20 => "high", _ => "severe" },
        observationCount = function.Observations.Length,
        affectedProjects = function.Observations.Select(item => item.Type.Project).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
        projectFrameworkObservations = function.Observations.Select(item => new { project = item.Type.Project, name = item.Function.Name, ownCc = item.Function.OwnCyclomaticComplexity }).ToArray()
    };
}
