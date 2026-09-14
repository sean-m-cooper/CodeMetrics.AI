namespace CodeMetrics.AI.Metrics;

public sealed record ExecutableFunctionMetrics(
    string Name, string Kind, string File, int Line, int OwnCyclomaticComplexity)
{
    public int? SourceSpanStart { get; init; }
    public int? SourceSpanLength { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FunctionMaintainabilityMetrics? Maintainability { get; init; }

    // Anonymous/initializer sites without decisions are observations, not extra
    // decomposition units. Named local helpers follow the same rule as methods.
    public bool CountsForDecomposition => Kind is not ("callback" or "initializer") || OwnCyclomaticComplexity > 1;
}

public sealed class ExecutableTypeMetrics(IReadOnlyList<ExecutableFunctionMetrics> functions)
{
    public IReadOnlyList<ExecutableFunctionMetrics> Functions { get; } = functions;
    public IReadOnlyList<ExecutableFunctionMetrics> MaintainabilityFunctions { get; init; } = functions;
    public int FunctionCount => Functions.Count;
    public int MaxComplexity => Functions.Count == 0 ? 0 : Functions.Max(function => function.OwnCyclomaticComplexity);
    public int DecisionCount => Functions.Sum(function => function.OwnCyclomaticComplexity - 1);
    public int DecompositionFunctionCount => Functions.Count(function => function.CountsForDecomposition);
    public int DecompositionComplexity => 1 + Functions.Where(function => function.CountsForDecomposition)
        .Sum(function => function.OwnCyclomaticComplexity);
    public double DecompositionRatio => DecompositionFunctionCount == 0 ? 0
        : Math.Round((double)DecompositionComplexity / DecompositionFunctionCount, 4);
}

public sealed record FunctionMaintainabilityMetrics(int SourceLines, double HalsteadVolume, decimal Index);
