using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

internal sealed record MethodComplexityPopulation(
    int TypeCount, IReadOnlyList<MethodComplexityPopulation.Observation> Observations,
    IReadOnlyList<MethodComplexityPopulation.SourceFunction> Functions)
{
    public static MethodComplexityPopulation Collect(IReadOnlyList<TypeMetrics> types)
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
        return new(types.Count, observations, functions);
    }

    internal static bool HasSourceIdentity(ExecutableFunctionMetrics function) =>
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

    internal sealed record Observation(TypeMetrics Type, ExecutableFunctionMetrics Function);

    internal sealed class SourceFunction(Observation[] observations)
    {
        public Observation[] Observations { get; } = observations.OrderByDescending(item => item.Function.OwnCyclomaticComplexity)
            .ThenBy(item => item.Type.Project, StringComparer.Ordinal)
            .ThenBy(item => item.Function.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Function.File, StringComparer.Ordinal).ToArray();
        public Observation Representative => Observations[0];
        public int Complexity => Representative.Function.OwnCyclomaticComplexity;

    }
}
