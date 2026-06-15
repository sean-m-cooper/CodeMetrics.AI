namespace CodeMetrics.AI;

internal sealed record SolutionProjectContext(
    ProjectSelection Selection,
    List<CompiledProject> AllCompiledProjects,
    HashSet<string> AnalyzedNames,
    List<CompiledProject> AnalyzedCompiledProjects);

internal static class SolutionProjectContextBuilder
{
    public static async Task<SolutionProjectContext> BuildAsync(
        Microsoft.CodeAnalysis.Solution solution,
        CancellationToken cancellationToken)
    {
        var selection = ProjectSelection.Create(solution.Projects);
        ConsoleReporter.WriteProjectSummary(selection);

        var allCompiledProjects = await ProjectCompilationCollector.CollectAsync(
            selection.AllProjects,
            cancellationToken);
        var analyzedNames = ProjectNameSet.Create(selection.AnalyzedProjects);
        var analyzedCompiledProjects = CompiledProjectFilter.AnalyzedOnly(allCompiledProjects, analyzedNames);

        return new SolutionProjectContext(
            selection,
            allCompiledProjects,
            analyzedNames,
            analyzedCompiledProjects);
    }
}
