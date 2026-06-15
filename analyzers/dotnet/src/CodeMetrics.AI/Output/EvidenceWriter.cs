using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeMetrics.AI.Output;

public static class EvidenceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteAsync(
        string path,
        EvidenceModel model,
        CancellationToken cancellationToken = default)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, model, JsonOptions, cancellationToken);
    }
}
