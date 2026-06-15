using System.CommandLine;
using CodeMetrics.AI;

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

    var analyzer = new SolutionAnalyzer();
    await analyzer.RunAsync(options, cancellationToken);
});

return await rootCommand.Parse(args).InvokeAsync();
