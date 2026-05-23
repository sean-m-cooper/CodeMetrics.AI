namespace CodeMetrics.AI;

public sealed record SkippedProject(string Name, string Reason);

public static class ProjectFilter
{
    private static readonly (Func<string, bool> Match, string Reason)[] Rules =
    [
        (n => n.Contains("Tests", StringComparison.OrdinalIgnoreCase), "Test project"),
        (n => n.EndsWith(".AppHost", StringComparison.OrdinalIgnoreCase), "Aspire orchestration host"),
        (n => n.EndsWith(".ServiceDefaults", StringComparison.OrdinalIgnoreCase), "Aspire service defaults"),
        (n => n.EndsWith(".Hosting", StringComparison.OrdinalIgnoreCase), "Aspire / generic hosting"),
        (n => n.EndsWith(".Benchmarks", StringComparison.OrdinalIgnoreCase), "Benchmark project"),
        (n => n.EndsWith(".Samples", StringComparison.OrdinalIgnoreCase), "Sample / demo code"),
        (n => n.EndsWith(".Demo", StringComparison.OrdinalIgnoreCase), "Demo code"),
        (n => n.EndsWith(".Playground", StringComparison.OrdinalIgnoreCase), "Playground / spike project"),
    ];

    public static bool ShouldSkip(string projectName, out string reason)
    {
        foreach (var (match, r) in Rules)
        {
            if (match(projectName))
            {
                reason = r;
                return true;
            }
        }
        reason = "";
        return false;
    }
}
