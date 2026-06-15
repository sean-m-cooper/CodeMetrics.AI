using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI;

internal static class MetricsAnalysisCollector
{
    public static MetricsAnalysisResult Collect(
        IReadOnlyCollection<CompiledProject> analyzedProjects,
        string solutionDir)
    {
        var allTypeMetrics = new List<TypeMetrics>();
        var allMemberMetrics = new List<MemberMetrics>();
        var projectCompilations = new List<(string ProjectName, Microsoft.CodeAnalysis.Compilation Compilation)>();
        var projectNameCompilations = new List<(string Name, Microsoft.CodeAnalysis.Compilation Compilation)>();
        var projectsWithPaths = new List<(string Name, Microsoft.CodeAnalysis.Compilation Compilation, string? ProjectFilePath)>();

        foreach (var project in analyzedProjects)
        {
            var (types, members) = MetricsCollector.Collect(project.Name, project.Compilation, solutionDir);
            allTypeMetrics.AddRange(types);
            allMemberMetrics.AddRange(members);

            projectCompilations.Add((project.Name, project.Compilation));
            projectNameCompilations.Add((project.Name, project.Compilation));
            projectsWithPaths.Add((project.Name, project.Compilation, project.ProjectFilePath));
        }

        return new MetricsAnalysisResult(
            allTypeMetrics,
            allMemberMetrics,
            projectCompilations,
            projectNameCompilations,
            projectsWithPaths);
    }
}
