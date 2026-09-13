using CodeMetrics.AI.Output;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal sealed record SolutionProjectSelection(
    int TotalProjectCount,
    IReadOnlyList<Project> ActiveProjects,
    IReadOnlyList<Project> CompilationProjects,
    List<SkippedProjectInfo> SkippedProjects,
    HashSet<ProjectId> AnalyzedProjectIds)
{
    internal static SolutionProjectSelection Create(
        Solution solution, string root, ProjectId? entryProjectId, SolutionScope? scope)
    {
        // References remain in the workspace for semantic resolution; project entry points score only that project.
        var projects = solution.Projects.Where(project => entryProjectId == null || project.Id == entryProjectId).ToList();
        var (skipped, analyzedProjectIds) = ClassifyProjects(projects, root, scope);
        var activeProjects = projects.Where(p => p.FilePath == null || scope == null ||
            (scope.ProjectPaths.Contains(p.FilePath) && !scope.DisabledPaths.Contains(p.FilePath))).ToList();
        // Preserve the existing exact-name match for declared test projects, without
        // scanning all skipped entries for each compilation candidate.
        var testNames = skipped.Where(s => s.Reason == "Test project").Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var compilationProjects = activeProjects.Where(p => analyzedProjectIds.Contains(p.Id) || testNames.Contains(p.Name)).ToList();
        return new SolutionProjectSelection(projects.Count, activeProjects, compilationProjects, skipped, analyzedProjectIds);
    }

    internal void ExcludeSemanticTests(
        IEnumerable<(Project Project, Compilation? Compilation)> compiledProjects, string root)
    {
        foreach (var (candidate, compilation) in compiledProjects)
        {
            if (compilation != null && AnalyzedProjectIds.Contains(candidate.Id) && ProjectFilter.HasTestMethods(compilation, root))
            {
                AnalyzedProjectIds.Remove(candidate.Id);
                SkippedProjects.Add(new SkippedProjectInfo { Name = candidate.Name, Reason = "Test project (semantic attributes)" });
            }
        }
    }

    private static (List<SkippedProjectInfo> Skipped, HashSet<ProjectId> AnalyzedProjectIds) ClassifyProjects(
        IEnumerable<Project> projects, string root, SolutionScope? scope)
    {
        var skipped = new List<SkippedProjectInfo>();
        var analyzedProjectIds = new HashSet<ProjectId>();
        foreach (var project in projects)
        {
            if (project.FilePath != null && scope?.DisabledPaths.Contains(project.FilePath) == true)
                skipped.Add(new SkippedProjectInfo { Name = project.Name, Reason = "Excluded by solution build configuration" });
            else if (project.FilePath != null && scope != null && !scope.ProjectPaths.Contains(project.FilePath))
                skipped.Add(new SkippedProjectInfo { Name = project.Name, Reason = "Reference outside selected solution" });
            else if (ProjectFilter.ShouldSkip(project.Name, project.FilePath, root, out var reason))
                skipped.Add(new SkippedProjectInfo { Name = project.Name, Reason = reason });
            else
                analyzedProjectIds.Add(project.Id);
        }

        return (skipped, analyzedProjectIds);
    }
}
