using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal static class ProjectCompilationCollector
{
    public static async Task<List<CompiledProject>> CollectAsync(
        IEnumerable<Project> projects,
        CancellationToken cancellationToken)
    {
        var compilationTasks = projects
            .Select(project => CompileAsync(project, cancellationToken))
            .ToList();
        var compilations = await Task.WhenAll(compilationTasks.ToArray());

        return compilations
            .OfType<CompiledProject>()
            .ToList();
    }

    private static async Task<CompiledProject?> CompileAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var compilation = await project.GetCompilationAsync(cancellationToken);
        return compilation == null
            ? null
            : new CompiledProject(project.Name, compilation, project.FilePath);
    }
}
