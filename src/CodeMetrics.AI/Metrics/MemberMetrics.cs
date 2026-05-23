namespace CodeMetrics.AI.Metrics;

public sealed class MemberMetrics
{
    public required string Project { get; init; }
    public required string Namespace { get; init; }
    public required string Type { get; init; }
    public required string Member { get; init; }
    public int CyclomaticComplexity { get; set; }
    public int LinesOfSource { get; set; }
    public int LinesOfExecutable { get; set; }
    public int MaintainabilityIndex { get; set; }
    public bool HasBody { get; set; } = true;
}
