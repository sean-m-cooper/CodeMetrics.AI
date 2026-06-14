namespace CodeMetrics.AI.Metrics;

public sealed class TypeMetrics
{
    public required string Project { get; init; }
    public required string Namespace { get; init; }
    public required string Type { get; init; }
    public required string FilePath { get; init; }
    public int CyclomaticComplexity { get; set; }
    public int MaintainabilityIndex { get; set; }
    public int DepthOfInheritance { get; set; }
    public int ClassCoupling { get; set; }
    public int LinesOfSource { get; set; }
    public int LinesOfExecutable { get; set; }
    public int MemberCount { get; set; }
    public int MaxMemberCyclomaticComplexity { get; set; }
    public double DecompositionRatio { get; set; }
}
