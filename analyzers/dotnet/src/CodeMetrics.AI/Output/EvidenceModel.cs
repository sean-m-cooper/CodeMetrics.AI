namespace CodeMetrics.AI.Output;

public sealed class EvidenceModel
{
    public int SchemaVersion { get; init; } = 1;
    public string GeneratedAtUtc { get; init; } = DateTime.UtcNow.ToString("O");
    public ToolInfo Tool { get; init; } = new();
    public SolutionInfo Solution { get; init; } = new();
    public FilterInfo Filters { get; init; } = new();
    public PopulationInfo Population { get; init; } = new();
    public Dictionary<string, object> Dimensions { get; init; } = [];
}

public sealed class ToolInfo
{
    public string Name { get; init; } = "CodeMetrics.AI";
    public string Version { get; init; } = typeof(ToolInfo).Assembly.GetName().Version?.ToString() ?? "1.0.0";
}

public sealed class SolutionInfo
{
    public string Path { get; init; } = "";
    public string Configuration { get; init; } = "Debug";
}

public sealed class FilterInfo
{
    public int TotalProjects { get; init; }
    public int AnalyzedProjects { get; init; }
    public List<SkippedProjectInfo> Skipped { get; init; } = [];
}

public sealed class SkippedProjectInfo
{
    public required string Name { get; init; }
    public required string Reason { get; init; }
}

public sealed class PopulationInfo
{
    public int Types { get; init; }
    public int Members { get; init; }
}
