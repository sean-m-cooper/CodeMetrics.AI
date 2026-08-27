namespace CodeMetrics.AI.Metrics;

public sealed record CouplingAnalysis(
    IReadOnlyList<string> RawTypes,
    IReadOnlyList<string> StructuralTypes,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ExcludedTypes);
