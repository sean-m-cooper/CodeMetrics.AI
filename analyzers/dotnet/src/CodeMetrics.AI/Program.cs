using System.CommandLine;
using CodeMetrics.AI;
using CodeMetrics.AI.Output;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;

var solutionOption = new Option<string?>("--solution")
{
    Description = "Path to .sln or .slnx file"
};

var outputOption = new Option<string>("--output")
{
    Description = "CSV output path",
    DefaultValueFactory = _ => ".scorecard/dotnet/metrics.csv"
};

var scorecardOutputOption = new Option<string>("--scorecard-output")
{
    Description = "JSON evidence output path",
    DefaultValueFactory = _ => ".scorecard/dotnet/evidence.json"
};

var configOption = new Option<string>("--configuration")
{
    Description = "Build configuration",
    DefaultValueFactory = _ => "Debug"
};

var skipDepsOption = new Option<bool>("--skip-dependency-probe")
{
    Description = "Skip dependency management checks"
};

var rootCommand = new RootCommand("CodeMetrics.AI — deterministic code metrics and scorecard evidence")
{
    solutionOption,
    outputOption,
    scorecardOutputOption,
    configOption,
    skipDepsOption
};

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var options = new CliOptions
    {
        Solution = parseResult.GetValue(solutionOption),
        Output = parseResult.GetValue(outputOption)!,
        ScorecardOutput = parseResult.GetValue(scorecardOutputOption)!,
        Configuration = parseResult.GetValue(configOption)!,
        SkipDependencyProbe = parseResult.GetValue(skipDepsOption)
    };

    await AnalyzeSolutionAsync(options, cancellationToken);
});

return await rootCommand.Parse(args).InvokeAsync();

static async Task AnalyzeSolutionAsync(CliOptions options, CancellationToken cancellationToken)
{
    if (!MSBuildLocator.IsRegistered)
        MSBuildLocator.RegisterDefaults();

    var solutionPath = ResolveSolutionPath(options.Solution);
    if (solutionPath == null)
    {
        Console.Error.WriteLine("No .sln or .slnx file found.");
        return;
    }

    solutionPath = Path.GetFullPath(solutionPath);
    var solutionDir = Path.GetDirectoryName(solutionPath)!;
    Console.WriteLine($"Solution: {solutionPath}");

    using var workspace = MSBuildWorkspace.Create();
    workspace.RegisterWorkspaceFailedHandler(error =>
        Console.Error.WriteLine($"Workspace warning: {error.Diagnostic.Message}"));
    var solution = await workspace.OpenSolutionAsync(
        solutionPath,
        cancellationToken: cancellationToken);
    var context = await SolutionCompilationLoader.LoadAsync(
        solution, solutionDir, cancellationToken);

    Console.WriteLine(
        $"Projects: {context.TotalProjectCount} total, " +
        $"{context.AnalyzedProjectNames.Count} analyzed, {context.SkippedProjects.Count} skipped");
    Console.WriteLine($"Types: {context.TypeMetrics.Count}, Members: {context.MemberMetrics.Count}");

    await CsvWriter.WriteAsync(
        options.Output, context.TypeMetrics, context.MemberMetrics, cancellationToken);
    Console.WriteLine($"CSV: {options.Output}");

    var dimensions = await ScorecardProbeRunner.AnalyzeAsync(
        context,
        solutionPath,
        solutionDir,
        options.SkipDependencyProbe,
        cancellationToken);
    var evidence = EvidenceFactory.Create(
        context, solutionPath, solutionDir, options.Configuration, dimensions);

    await EvidenceWriter.WriteAsync(options.ScorecardOutput, evidence, cancellationToken);
    Console.WriteLine($"Evidence: {options.ScorecardOutput}");
    Console.WriteLine("Done.");
}

static string? ResolveSolutionPath(string? explicitPath)
{
    if (!string.IsNullOrEmpty(explicitPath))
        return File.Exists(explicitPath) ? explicitPath : null;

    var solutionFiles = Directory.GetFiles(".", "*.sln")
        .Concat(Directory.GetFiles(".", "*.slnx"))
        .ToList();
    return solutionFiles.Count == 1 ? solutionFiles[0] : null;
}
