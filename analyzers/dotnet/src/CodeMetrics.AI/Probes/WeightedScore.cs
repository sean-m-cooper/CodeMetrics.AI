namespace CodeMetrics.AI.Probes;

// Shared arithmetic only. Each dimension still selects its own population and weights.
internal sealed record WeightedScore(decimal FirstPenalty, decimal RemainingPenalty, decimal Unrounded,
    decimal Ceiling, double Rounded)
{
    public static WeightedScore Calculate(decimal firstMean, decimal? remainingMean,
        decimal firstWeight, decimal remainingWeight)
    {
        var firstPenalty = firstWeight * (10m - firstMean);
        var remainingPenalty = remainingWeight * (10m - (remainingMean ?? 10m));
        var unrounded = 10m - firstPenalty - remainingPenalty;
        return new(firstPenalty, remainingPenalty, unrounded, 10m - firstPenalty,
            (double)Math.Round(unrounded, 1, MidpointRounding.AwayFromZero));
    }
}
