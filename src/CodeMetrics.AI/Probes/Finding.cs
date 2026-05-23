namespace CodeMetrics.AI.Probes;

public sealed class Finding
{
    public required string Category { get; init; }
    public required string Severity { get; init; }
    public string? File { get; init; }
    public int? Line { get; init; }
    public string? Project { get; init; }
    public string? Type { get; init; }
    public string? Package { get; init; }
    public required string Message { get; init; }
    public string Confidence { get; init; } = "high";
}
