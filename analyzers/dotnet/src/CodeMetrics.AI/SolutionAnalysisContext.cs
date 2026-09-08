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
    IReadOnlyList<MemberMetrics> MemberMetrics)
{
    public List<AnalysisDiagnostic> Diagnostics { get; } = [];
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Kind != "workspaceWarning");
}

internal static class SolutionCompilationLoader
{
    public static async Task<SolutionAnalysisContext> LoadAsync(
        Solution solution,
        string solutionDir,
        CancellationToken cancellationToken,
        ProjectId? entryProjectId = null)
    {
        // References remain in the workspace for semantic resolution; project entry points score only that project.
        var projects = solution.Projects.Where(project => entryProjectId == null || project.Id == entryProjectId).ToList();
        var (skipped, analyzedProjectIds) = ClassifyProjects(projects);
        var compiledProjects = await CompileAsync(projects, cancellationToken);
        var loaded = CollectMetrics(compiledProjects, analyzedProjectIds, solutionDir);

        var context = new SolutionAnalysisContext(
            projects.Count,
            loaded.AnalyzedCompilations.Select(project => project.ProjectName).ToList(),
            skipped,
            loaded.AllCompilations,
            loaded.AnalyzedCompilations,
            loaded.ProjectsWithPaths,
            loaded.Types,
            loaded.Members);
        foreach (var (project, compilation) in compiledProjects)
        {
            if (compilation == null)
            {
                context.Diagnostics.Add(new AnalysisDiagnostic("compilationUnavailable", "No compilation was available.", project.Name));
                if (analyzedProjectIds.Contains(project.Id))
                    skipped.Add(new SkippedProjectInfo { Name = project.Name, Reason = "Compilation unavailable" });
            }
            else
            {
                foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Take(20))
                    context.Diagnostics.Add(new AnalysisDiagnostic("compilationError", diagnostic.ToString(), project.Name));
            }
        }
        if (context.AnalyzedProjectNames.Count == 0)
            context.Diagnostics.Add(new AnalysisDiagnostic("emptyPopulation", "No production projects could be analyzed."));
        return context;
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
        using var concurrency = new SemaphoreSlim(Math.Min(Environment.ProcessorCount, 4));
        var compilationTasks = projects.Select(async project =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try { return (Project: project, Compilation: await project.GetCompilationAsync(cancellationToken)); }
            finally { concurrency.Release(); }
        });
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
