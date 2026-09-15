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
    public IReadOnlyList<string> ScopedProjectPaths { get; init; } = [];
    public IReadOnlyDictionary<string, string> DependencyProjectScopes { get; init; } = new Dictionary<string, string>();
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Kind != "workspaceWarning");
}

internal static class SolutionCompilationLoader
{
    public static async Task<SolutionAnalysisContext> LoadAsync(
        Solution solution,
        string solutionDir,
        CancellationToken cancellationToken,
        ProjectId? entryProjectId = null,
        SolutionScope? scope = null)
    {
        var selection = SolutionProjectSelection.Create(solution, solutionDir, entryProjectId, scope);
        var compiledProjects = await CompileAsync(selection.CompilationProjects, cancellationToken);
        selection.ExcludeSemanticTests(compiledProjects, solutionDir);
        var loaded = CollectMetrics(compiledProjects, selection.AnalyzedProjectIds, solutionDir);

        var context = new SolutionAnalysisContext(
            selection.TotalProjectCount,
            loaded.AnalyzedCompilations.Select(project => project.ProjectName).ToList(),
            selection.SkippedProjects,
            loaded.AllCompilations,
            loaded.AnalyzedCompilations,
            loaded.ProjectsWithPaths,
            loaded.Types,
            loaded.Members)
        {
            ScopedProjectPaths = selection.ActiveProjects.Select(p => p.FilePath).OfType<string>().Distinct(SolutionScope.PathComparer).ToArray(),
            DependencyProjectScopes = DependencyProjectScope.Create(selection, solutionDir)
        };
        await SolutionCompilationDiagnostics.AppendAsync(context, compiledProjects, selection, cancellationToken);
        return context;
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
