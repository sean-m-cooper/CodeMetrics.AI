namespace CodeMetrics.AI;

internal static class ConsoleReporter
{
    public static void WriteProjectSummary(ProjectSelection selection)
    {
        Console.WriteLine(
            $"Projects: {selection.AllProjects.Count} total, {selection.AnalyzedProjects.Count} analyzed, {selection.Skipped.Count} skipped");
    }

    public static void WritePopulationSummary(MetricsAnalysisResult metrics)
    {
        Console.WriteLine($"Types: {metrics.TypeMetrics.Count}, Members: {metrics.MemberMetrics.Count}");
    }
}
