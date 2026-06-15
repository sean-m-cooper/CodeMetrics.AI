using Microsoft.Build.Locator;

namespace CodeMetrics.AI;

public class SolutionAnalyzer
{
    public async Task RunAsync(CliOptions options, CancellationToken cancellationToken = default)
    {
        if (!MSBuildLocator.IsRegistered)
            MSBuildLocator.RegisterDefaults();

        var solutionPath = ResolveSolutionPath(options.Solution);
        if (solutionPath == null)
        {
            Console.Error.WriteLine("No .sln or .slnx file found.");
            return;
        }
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        Console.WriteLine($"Solution: {solutionPath}");

        await SolutionAnalysisWorkflow.RunAsync(
            options,
            solutionPath,
            solutionDir,
            cancellationToken);
    }

    private static string? ResolveSolutionPath(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath))
            return File.Exists(explicitPath) ? explicitPath : null;

        var slnFiles = Directory.GetFiles(".", "*.sln")
            .Concat(Directory.GetFiles(".", "*.slnx"))
            .ToList();

        return slnFiles.Count == 1 ? slnFiles[0] : null;
    }
}
