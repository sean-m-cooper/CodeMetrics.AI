namespace CodeMetrics.AI.Probes;

public sealed class Finding
{
    public required string Category { get; init; }
    public required string Severity { get; init; }
    public string? File { get; set; }
    public int? Line { get; set; }
    public string? Member { get; set; }
    public string? Project { get; set; }
    public string? Type { get; init; }
    public string? Package { get; init; }
    public required string Message { get; init; }
    public string Confidence { get; set; } = "medium";
    public string? RuleId { get; set; }
    public string? Fingerprint { get; set; }
    public Dictionary<string, object?> Observations { get; init; } = [];
}
