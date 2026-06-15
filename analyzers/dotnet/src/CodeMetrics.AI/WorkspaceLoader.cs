using Microsoft.CodeAnalysis.MSBuild;

namespace CodeMetrics.AI;

internal static class WorkspaceLoader
{
    public static MSBuildWorkspace CreateWorkspace()
    {
        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
            Console.Error.WriteLine($"Workspace warning: {e.Diagnostic.Message}"));
        return workspace;
    }

    public static async Task<Microsoft.CodeAnalysis.Solution> OpenSolutionAsync(
        MSBuildWorkspace workspace,
        string solutionPath,
        CancellationToken cancellationToken)
    {
        return await workspace.OpenSolutionAsync(
            Path.GetFullPath(solutionPath),
            cancellationToken: cancellationToken);
    }
}
