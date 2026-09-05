using System.CommandLine;
using CodeMetrics.AI;
using CodeMetrics.AI.Output;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;

System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

var solutionOption = new Option<string?>("--solution")
{
    Description = "Path to .sln, .slnx, or .csproj file"
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
var coverageOption = new Option<string?>("--coverage") { Description = "Path to a Cobertura coverage report" };
rootCommand.Options.Add(coverageOption);
var runIdOption = new Option<string?>("--run-id") { Description = "UUID identifying this analysis invocation (generated when omitted)" };
var auditIdOption = new Option<string?>("--audit-id") { Description = "UUID shared by ecosystem runs in one audit (defaults to run ID)" };
rootCommand.Options.Add(runIdOption);
rootCommand.Options.Add(auditIdOption);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var options = new CliOptions
    {
        Solution = parseResult.GetValue(solutionOption),
        Output = parseResult.GetValue(outputOption)!,
        ScorecardOutput = parseResult.GetValue(scorecardOutputOption)!,
        Configuration = parseResult.GetValue(configOption)!,
        SkipDependencyProbe = parseResult.GetValue(skipDepsOption),
        Coverage = parseResult.GetValue(coverageOption),
        RunId = parseResult.GetValue(runIdOption),
        AuditId = parseResult.GetValue(auditIdOption)
    };

    try
    {
        return await AnalyzeSolutionAsync(options, cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        Console.Error.WriteLine("Analysis cancelled.");
        return 130;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Analysis failed: {ex.Message}");
        return 2;
    }
});

return await rootCommand.Parse(args).InvokeAsync();

static async Task<int> AnalyzeSolutionAsync(CliOptions options, CancellationToken cancellationToken)
{
    var runId = NormalizeId(options.RunId, "--run-id") ?? Guid.NewGuid().ToString("D");
    var auditId = NormalizeId(options.AuditId, "--audit-id") ?? runId;
    if (!MSBuildLocator.IsRegistered)
        MSBuildLocator.RegisterDefaults();

    var solutionPath = ResolveSolutionPath(options.Solution);
    if (solutionPath == null)
    {
        Console.Error.WriteLine("Select one existing .sln, .slnx, or .csproj file with --solution (automatic discovery requires exactly one solution).");
        return 2;
    }

    solutionPath = Path.GetFullPath(solutionPath);
    var solutionDir = Path.GetDirectoryName(solutionPath)!;
    Console.WriteLine($"Solution: {solutionPath}");

    using var workspace = MSBuildWorkspace.Create(new Dictionary<string, string>
    {
        ["Configuration"] = options.Configuration
    });
    var workspaceFailures = new System.Collections.Concurrent.ConcurrentQueue<AnalysisDiagnostic>();
    workspace.RegisterWorkspaceFailedHandler(error =>
    {
        Console.Error.WriteLine($"Workspace warning: {error.Diagnostic.Message}");
        if (error.Diagnostic.Kind == Microsoft.CodeAnalysis.WorkspaceDiagnosticKind.Failure)
            workspaceFailures.Enqueue(new AnalysisDiagnostic("workspace", error.Diagnostic.Message));
    });
    var project = Path.GetExtension(solutionPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
        ? await workspace.OpenProjectAsync(solutionPath, cancellationToken: cancellationToken) : null;
    var solution = project?.Solution ?? await workspace.OpenSolutionAsync(
        solutionPath, cancellationToken: cancellationToken);
    var context = await SolutionCompilationLoader.LoadAsync(
        solution, solutionDir, cancellationToken, project?.Id);
    context.Diagnostics.AddRange(workspaceFailures);
    var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    var inputs = solution.Projects.Select(project => project.FilePath)
        .Concat(solution.Projects.SelectMany(project => project.Documents).Select(document => document.FilePath))
        .Concat(context.AllProjectCompilations.SelectMany(project => project.Compilation.SyntaxTrees).Select(tree => tree.FilePath))
        .Append(solutionPath).Append(options.Coverage)
        .Where(path => !string.IsNullOrEmpty(path)).Select(path => Path.GetFullPath(path!)).ToHashSet(comparer);
    var outputs = new[] { Path.GetFullPath(options.Output), Path.GetFullPath(options.ScorecardOutput) };
    if (comparer.Equals(outputs[0], outputs[1]) || outputs.Any(inputs.Contains))
        throw new ArgumentException("Output paths must be distinct and must not overwrite source inputs.");

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
        cancellationToken,
        options.Coverage);
    var evidence = EvidenceFactory.Create(
        context, solutionPath, solutionDir, options.Configuration, dimensions, runId, auditId);

    await EvidenceWriter.WriteAsync(options.ScorecardOutput, evidence, cancellationToken);
    Console.WriteLine($"Evidence: {options.ScorecardOutput}");
    Console.WriteLine("Done.");
    return context.Diagnostics.Count > 0 || dimensions.Values.OfType<CodeMetrics.AI.Probes.DimensionResult>()
        .Any(dimension => dimension.Status == "failed") ? 2 : 0;
}

static string? NormalizeId(string? value, string option)
{
    if (value == null) return null;
    if (!Guid.TryParseExact(value, "D", out var id)) throw new ArgumentException($"{option} must be a UUID.");
    return id.ToString("D");
}

static string? ResolveSolutionPath(string? explicitPath)
{
    if (!string.IsNullOrEmpty(explicitPath))
        return File.Exists(explicitPath) && new[] { ".sln", ".slnx", ".csproj" }
            .Contains(Path.GetExtension(explicitPath), StringComparer.OrdinalIgnoreCase) ? explicitPath : null;

    var solutionFiles = Directory.GetFiles(".", "*.sln")
        .Concat(Directory.GetFiles(".", "*.slnx"))
        .ToList();
    return solutionFiles.Count == 1 ? solutionFiles[0] : null;
}
