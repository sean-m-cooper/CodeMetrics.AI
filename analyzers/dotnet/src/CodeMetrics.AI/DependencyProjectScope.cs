using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal static class DependencyProjectScope
{
    public static IReadOnlyDictionary<string, string> Create(SolutionProjectSelection selection, string root) =>
        selection.ActiveProjects.Where(p => p.FilePath != null)
            .GroupBy(p => p.FilePath!, SolutionScope.PathComparer)
            .ToDictionary(g => Path.GetFullPath(g.Key), g => Classify(g, selection, root), SolutionScope.PathComparer);

    private static string Classify(IEnumerable<Project> projects, SolutionProjectSelection selection, string root)
    {
        // Any production variant wins. Never exclude a whole physical project because only one TFM is a test.
        if (projects.Any(p => selection.AnalyzedProjectIds.Contains(p.Id))) return "production";
        var project = projects.First();
        // Skipped reasons are keyed by display name. A name collision across physical
        // projects cannot establish development-only scope for either project.
        if (projects.Any(p => selection.ActiveProjects.Where(other => other.Name == p.Name)
                .Select(other => other.FilePath).Distinct(SolutionScope.PathComparer).Skip(1).Any())) return "unknown";
        var segments = Path.GetRelativePath(root, project.FilePath!).Replace('\\', '/').Split('/');
        if (!segments.Contains("..") && segments.SkipLast(1).Any(BenchmarkProjectRecognition.IsDirectory)) return "benchmark";
        var reasons = projects.SelectMany(p => selection.SkippedProjects.Where(s => s.Name == p.Name)).Select(s => s.Reason).ToArray();
        if (reasons.Any(r => r.StartsWith("Benchmark", StringComparison.Ordinal))) return "benchmark";
        if (reasons.Any(r => r.StartsWith("Test", StringComparison.Ordinal))) return "test";
        // Hosts, samples and unknown exclusions are not proof of development-only use.
        return "unknown";
    }
}
