using CodeMetrics.AI.Output;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal sealed record ProjectSelection(
    IReadOnlyList<Project> AllProjects,
    IReadOnlyList<Project> AnalyzedProjects,
    List<SkippedProjectInfo> Skipped)
{
    public static ProjectSelection Create(IEnumerable<Project> projects)
    {
        var allProjects = projects.ToList();
        var analyzed = new List<Project>();
        var skipped = new List<SkippedProjectInfo>();

        foreach (var project in allProjects)
        {
            if (ProjectFilter.ShouldSkip(project.Name, out var reason))
                skipped.Add(new SkippedProjectInfo { Name = project.Name, Reason = reason });
            else
                analyzed.Add(project);
        }

        return new ProjectSelection(allProjects, analyzed, skipped);
    }
}
