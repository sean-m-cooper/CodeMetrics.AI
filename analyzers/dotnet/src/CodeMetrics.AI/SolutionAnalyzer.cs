using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Output;

namespace CodeMetrics.AI;

public class SolutionAnalyzer
{
    public async Task RunAsync(CliOptions options)
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

        using var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
            Console.Error.WriteLine($"Workspace warning: {e.Diagnostic.Message}"));
        var solution = await workspace.OpenSolutionAsync(Path.GetFullPath(solutionPath));

        var allProjects = solution.Projects.ToList();
        var analyzed = new List<Microsoft.CodeAnalysis.Project>();
        var skipped = new List<SkippedProjectInfo>();

        foreach (var project in allProjects)
        {
            if (ProjectFilter.ShouldSkip(project.Name, out var reason))
                skipped.Add(new SkippedProjectInfo { Name = project.Name, Reason = reason });
            else
                analyzed.Add(project);
        }

        Console.WriteLine($"Projects: {allProjects.Count} total, {analyzed.Count} analyzed, {skipped.Count} skipped");

        // Collect metrics and build probe inputs
        var allTypeMetrics = new List<TypeMetrics>();
        var allMemberMetrics = new List<MemberMetrics>();
        var projectCompilations = new List<(string ProjectName, Compilation Compilation)>();
        var projectNameCompilations = new List<(string Name, Compilation Compilation)>();
        var projectsWithPaths = new List<(string Name, Compilation Compilation, string? ProjectFilePath)>();
        var allProjectCompilations = new List<(string Name, Compilation Compilation)>();

        // Compile ALL projects (for TestingProbe which needs all, not just analyzed)
        foreach (var project in allProjects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;
            allProjectCompilations.Add((project.Name, compilation));
        }

        foreach (var project in analyzed)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            var (types, members) = MetricsCollector.Collect(project.Name, compilation);
            allTypeMetrics.AddRange(types);
            allMemberMetrics.AddRange(members);

            projectCompilations.Add((project.Name, compilation));
            projectNameCompilations.Add((project.Name, compilation));
            projectsWithPaths.Add((project.Name, compilation, project.FilePath));
        }

        Console.WriteLine($"Types: {allTypeMetrics.Count}, Members: {allMemberMetrics.Count}");

        await CsvWriter.WriteAsync(options.Output, allTypeMetrics, allMemberMetrics);
        Console.WriteLine($"CSV: {options.Output}");

        // Run probes
        var dimensions = new Dictionary<string, object>();

        dimensions["codeQuality"] = CodeQualityProbe.Analyze(allTypeMetrics);
        dimensions["maintainability"] = MaintainabilityProbe.Analyze(allTypeMetrics);
        dimensions["errorHandling"] = ErrorHandlingProbe.Analyze(projectCompilations);
        dimensions["performanceAsync"] = PerformanceAsyncProbe.Analyze(projectCompilations);

        // Dependency probe (may be skipped)
        DimensionResult depResult;
        if (options.SkipDependencyProbe)
        {
            depResult = new DimensionResult
            {
                Status = "skipped",
                Basis = "Dependency probe skipped via --skip-dependency-probe."
            };
        }
        else
        {
            depResult = await DependencyProbe.AnalyzeAsync(solutionPath, solutionDir);
        }
        dimensions["dependencyManagement"] = depResult;

        // Security probe - pass vulnerability count from dependency probe
        int vulnCount = depResult.Findings.Count(f =>
            f.Category.Contains("vulnerable", StringComparison.OrdinalIgnoreCase));
        dimensions["security"] = SecurityProbe.Analyze(projectCompilations, vulnCount);

        dimensions["testing"] = TestingProbe.Analyze(
            allProjectCompilations,
            analyzed.Select(p => p.Name).ToList(),
            solutionDir);
        dimensions["documentation"] = DocumentationProbe.Analyze(solutionDir, projectsWithPaths);
        dimensions["architecture"] = ArchitectureProbe.Analyze(
            projectNameCompilations, allTypeMetrics, solutionDir);

        // Build evidence
        var evidence = new EvidenceModel
        {
            Subject = new SubjectInfo
            {
                Root = solutionDir,
                EntryPoint = Path.GetFullPath(solutionPath),
                Name = Path.GetFileNameWithoutExtension(solutionPath),
                Variant = options.Configuration
            },
            Filters = new FilterInfo
            {
                TotalUnits = allProjects.Count,
                AnalyzedUnits = analyzed.Count,
                Skipped = skipped,
            },
            Population = new PopulationInfo
            {
                Types = allTypeMetrics.Count,
                Members = allMemberMetrics.Count,
            },
            Dimensions = dimensions,
        };

        await EvidenceWriter.WriteAsync(options.ScorecardOutput, evidence);
        Console.WriteLine($"Evidence: {options.ScorecardOutput}");
        Console.WriteLine("Done.");
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
