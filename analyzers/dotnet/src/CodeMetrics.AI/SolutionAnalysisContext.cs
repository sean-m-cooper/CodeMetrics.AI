using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Output;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal sealed record SolutionAnalysisContext(
    int TotalProjectCount,
    IReadOnlyList<string> AnalyzedProjectNames,
    IReadOnlyList<SkippedProjectInfo> SkippedProjects,
    IReadOnlyList<(string Name, Compilation Compilation)> AllProjectCompilations,
    IReadOnlyList<(string ProjectName, Compilation Compilation)> AnalyzedProjectCompilations,
    IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> ProjectsWithPaths,
    IReadOnlyList<TypeMetrics> TypeMetrics,
    IReadOnlyList<MemberMetrics> MemberMetrics);

internal static class SolutionCompilationLoader
{
    public static async Task<SolutionAnalysisContext> LoadAsync(
        Solution solution,
        string solutionDir,
        CancellationToken cancellationToken)
    {
        var projects = solution.Projects.ToList();
        var (skipped, analyzedProjectIds) = ClassifyProjects(projects);
        var compiledProjects = await CompileAsync(projects, cancellationToken);
        var loaded = CollectMetrics(compiledProjects, analyzedProjectIds, solutionDir);

        return new SolutionAnalysisContext(
            projects.Count,
            projects.Where(project => analyzedProjectIds.Contains(project.Id))
                .Select(project => project.Name)
                .ToList(),
            skipped,
            loaded.AllCompilations,
            loaded.AnalyzedCompilations,
            loaded.ProjectsWithPaths,
            loaded.Types,
            loaded.Members);
    }

    private static (
        List<SkippedProjectInfo> Skipped,
        HashSet<ProjectId> AnalyzedProjectIds) ClassifyProjects(IEnumerable<Project> projects)
    {
        var skipped = new List<SkippedProjectInfo>();
        var analyzedProjectIds = new HashSet<ProjectId>();
        foreach (var project in projects)
        {
            if (ProjectFilter.ShouldSkip(project.Name, out var reason))
                skipped.Add(new SkippedProjectInfo { Name = project.Name, Reason = reason });
            else
                analyzedProjectIds.Add(project.Id);
        }

        return (skipped, analyzedProjectIds);
    }

    private static async Task<(Project Project, Compilation? Compilation)[]> CompileAsync(
        IEnumerable<Project> projects,
        CancellationToken cancellationToken)
    {
        var compilationTasks = projects.Select(async project =>
            (Project: project, Compilation: await project.GetCompilationAsync(cancellationToken)));
        return await Task.WhenAll(compilationTasks);
    }

    private static LoadedProjectData CollectMetrics(
        IEnumerable<(Project Project, Compilation? Compilation)> compiledProjects,
        IReadOnlySet<ProjectId> analyzedProjectIds,
        string solutionDir)
    {
        var allCompilations = new List<(string Name, Compilation Compilation)>();
        var analyzedCompilations = new List<(string ProjectName, Compilation Compilation)>();
        var projectsWithPaths = new List<(
            string Name, Compilation Compilation, string? ProjectFilePath)>();
        var types = new List<TypeMetrics>();
        var members = new List<MemberMetrics>();

        foreach (var (project, compilation) in compiledProjects)
            AddCompiledProject(
                project,
                compilation,
                analyzedProjectIds,
                solutionDir,
                allCompilations,
                analyzedCompilations,
                projectsWithPaths,
                types,
                members);

        return new LoadedProjectData(
            allCompilations,
            analyzedCompilations,
            projectsWithPaths,
            types,
            members);
    }

    private static void AddCompiledProject(
        Project project,
        Compilation? compilation,
        IReadOnlySet<ProjectId> analyzedProjectIds,
        string solutionDir,
        ICollection<(string Name, Compilation Compilation)> allCompilations,
        ICollection<(string ProjectName, Compilation Compilation)> analyzedCompilations,
        ICollection<(string Name, Compilation Compilation, string? ProjectFilePath)> projectsWithPaths,
        ICollection<TypeMetrics> types,
        ICollection<MemberMetrics> members)
    {
        if (compilation == null)
            return;

        allCompilations.Add((project.Name, compilation));
        if (!analyzedProjectIds.Contains(project.Id))
            return;

        analyzedCompilations.Add((project.Name, compilation));
        projectsWithPaths.Add((project.Name, compilation, project.FilePath));
        var projectMetrics = MetricsCollector.Collect(project.Name, compilation, solutionDir);
        foreach (var type in projectMetrics.Types)
            types.Add(type);
        foreach (var member in projectMetrics.Members)
            members.Add(member);
    }

    private sealed record LoadedProjectData(
        IReadOnlyList<(string Name, Compilation Compilation)> AllCompilations,
        IReadOnlyList<(string ProjectName, Compilation Compilation)> AnalyzedCompilations,
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> ProjectsWithPaths,
        IReadOnlyList<TypeMetrics> Types,
        IReadOnlyList<MemberMetrics> Members);
}
