using System.Text.Json.Serialization;

namespace CodeMetrics.AI.Probes;

public sealed class DimensionResult
{
    public required string Status { get; init; } // "scored", "skipped", "failed"
    public double Score { get; init; }
    public required string Basis { get; init; }
    public List<Finding> Findings { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, object?> Extra { get; init; } = [];
}
