namespace CodeMetrics.AI;

public sealed class CliOptions
{
    public string? Solution { get; set; }
    public string Output { get; set; } = ".scorecard/metrics.csv";
    public string ScorecardOutput { get; set; } = ".scorecard/evidence.json";
    public string Configuration { get; set; } = "Debug";
    public bool SkipDependencyProbe { get; set; }
}
