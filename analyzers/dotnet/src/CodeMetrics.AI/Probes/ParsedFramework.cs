namespace CodeMetrics.AI.Probes;

internal sealed record ParsedFramework(
    FrameworkFamily Family,
    Version Version,
    string? Platform = null,
    Version? PlatformVersion = null);

internal enum FrameworkFamily
{
    ModernDotNet,
    NetCoreApp,
    NetStandard,
    NetFramework,
    Any
}
