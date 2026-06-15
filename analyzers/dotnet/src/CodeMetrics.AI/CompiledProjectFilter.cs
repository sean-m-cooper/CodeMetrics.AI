namespace CodeMetrics.AI;

internal static class CompiledProjectFilter
{
    public static List<CompiledProject> AnalyzedOnly(
        IEnumerable<CompiledProject> projects,
        HashSet<string> analyzedNames)
    {
        return projects
            .Where(project => analyzedNames.Contains(project.Name))
            .ToList();
    }
}
