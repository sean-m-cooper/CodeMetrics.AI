using System.Reflection;

namespace CodeMetrics.AI.Output;

public sealed class EvidenceModel
{
    public int SchemaVersion { get; init; } = 3;
    public string GeneratedAtUtc { get; init; } = DateTime.UtcNow.ToString("O");
    public ToolInfo Tool { get; init; } = new();
    public SubjectInfo Subject { get; init; } = new();
    public FilterInfo Filters { get; init; } = new();
    public PopulationInfo Population { get; init; } = new();
    public Dictionary<string, object> Dimensions { get; init; } = [];
    public AnalysisInfo Analysis { get; init; } = new();
}

public sealed record AnalysisDiagnostic(string Kind, string Message, string? Project = null);

public sealed class AnalysisInfo
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("D");
    public string? AuditId { get; init; }
    public string Status { get; init; } = "complete";
    public string Ruleset { get; init; } = "dotnet-2026-09-05";
    public string Calibration { get; init; } = "baseline";
    public string ConfigurationFingerprint { get; init; } = "default";
    public List<AnalysisDiagnostic> Diagnostics { get; init; } = [];
    public List<object> Suppressions { get; init; } = [];
}

public sealed class ToolInfo
{
    public string Name { get; init; } = "CodeMetrics.AI";
    public string Version { get; init; } = GetPackageVersion();
    public string Ecosystem { get; init; } = "dotnet";

    private static string GetPackageVersion()
    {
        var informationalVersion = typeof(ToolInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+', 2)[0];

        return typeof(ToolInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}

public sealed class SubjectInfo
{
    public string Root { get; init; } = "";
    public string EntryPoint { get; init; } = "";
    public string? Name { get; init; }
    public string? Variant { get; init; }
}

public sealed class FilterInfo
{
    public int TotalUnits { get; init; }
    public int AnalyzedUnits { get; init; }
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
