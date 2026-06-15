using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal static class ProjectNameSet
{
    public static HashSet<string> Create(IEnumerable<Project> projects)
    {
        return projects
            .Select(project => project.Name)
            .ToHashSet(StringComparer.Ordinal);
    }
}
