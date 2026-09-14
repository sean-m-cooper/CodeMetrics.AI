using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

internal static class MethodComplexityProbe
{
    public const string Policy = MethodComplexityScoring.Policy;
    public const string MeasurementPolicy = MethodComplexityScoring.MeasurementPolicy;

    public static MethodComplexityResult Analyze(IReadOnlyList<TypeMetrics> types)
    {
        var population = MethodComplexityPopulation.Collect(types);
        var assessment = MethodComplexityScoring.Evaluate(population);
        return MethodComplexityEvidence.Create(population, assessment);
    }
}

internal sealed record MethodComplexityResult(ScoringDecision Decision, object Metrics, object Details);
