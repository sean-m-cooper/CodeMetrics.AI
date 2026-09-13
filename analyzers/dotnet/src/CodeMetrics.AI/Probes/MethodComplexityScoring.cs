namespace CodeMetrics.AI.Probes;

internal static class MethodComplexityScoring
{
    public const string Policy = "dotnet/codeQuality/complexity/source-functions-40-60-v1";
    public const string MeasurementPolicy = "source-function-own-cc-v1";

    public static Assessment Evaluate(MethodComplexityPopulation population)
    {
        // The caller supplies a nonempty executable population. Remove exactly one
        // worst method; tied worst methods remain in the population contribution.
        var functions = population.Functions;
        var worst = functions[0];
        var worstScore = IndividualScore(worst.Complexity);
        var remaining = functions.Skip(1).ToList();
        decimal worstWeight = remaining.Count == 0 ? 1m : .4m;
        decimal remainingWeight = 1m - worstWeight;
        decimal? remainingMean = remaining.Count == 0 ? null : remaining.Average(function => IndividualScore(function.Complexity));
        decimal worstPenalty = worstWeight * (10m - worstScore);
        decimal remainingPenalty = remainingWeight * (10m - (remainingMean ?? 10m));
        decimal unrounded = 10m - worstPenalty - remainingPenalty;
        decimal ceiling = 10m - worstPenalty;
        return new(worst, worstScore, remaining.Count, worstWeight, remainingWeight,
            remainingMean, worstPenalty, remainingPenalty, unrounded, ceiling,
            (double)Math.Round(unrounded, 1, MidpointRounding.AwayFromZero));
    }

    internal static decimal IndividualScore(int cc) => cc switch
    {
        <= 3 => 10m,
        <= 5 => 10m - (cc - 3),
        <= 10 => 8m - (cc - 5) * .4m,
        _ => Math.Max(0m, 6m - (cc - 10) * .2m)
    };

    internal sealed record Assessment(MethodComplexityPopulation.SourceFunction Worst, decimal WorstScore,
        int RemainingCount, decimal WorstWeight, decimal RemainingWeight, decimal? RemainingMean,
        decimal WorstPenalty, decimal RemainingPenalty, decimal Unrounded, decimal Ceiling, double FinalScore);
}
