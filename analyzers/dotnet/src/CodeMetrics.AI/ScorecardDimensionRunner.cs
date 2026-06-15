using CodeMetrics.AI.Probes;

namespace CodeMetrics.AI;

internal static class ScorecardDimensionRunner
{
    public static async Task<Dictionary<string, object>> RunAsync(
        CliOptions options,
        string solutionPath,
        string solutionDir,
        MetricsAnalysisResult metrics,
        IReadOnlyCollection<CompiledProject> allProjects,
        IReadOnlyCollection<string> analyzedProjectNames,
        CancellationToken cancellationToken)
    {
        var dimensions = new Dictionary<string, object>
        {
            ["codeQuality"] = CodeQualityProbe.Analyze(metrics.TypeMetrics),
            ["maintainability"] = MaintainabilityProbe.Analyze(metrics.TypeMetrics),
            ["errorHandling"] = ErrorHandlingProbe.Analyze(metrics.ProjectCompilations, solutionDir),
            ["performanceAsync"] = PerformanceAsyncProbe.Analyze(metrics.ProjectCompilations, solutionDir),
        };

        var dependencyResult = await AnalyzeDependenciesAsync(
            options,
            solutionPath,
            solutionDir,
            cancellationToken);

        dimensions["dependencyManagement"] = dependencyResult;
        dimensions["security"] = SecurityProbe.Analyze(
            metrics.ProjectCompilations,
            CountVulnerabilities(dependencyResult),
            solutionDir);
        dimensions["testing"] = TestingProbe.Analyze(
            ToProbeInputs(allProjects),
            analyzedProjectNames.ToList(),
            solutionDir);
        dimensions["documentation"] = DocumentationProbe.Analyze(solutionDir, metrics.ProjectsWithPaths);
        dimensions["architecture"] = ArchitectureProbe.Analyze(
            metrics.ProjectNameCompilations,
            metrics.TypeMetrics,
            solutionDir);

        return dimensions;
    }

    private static async Task<DimensionResult> AnalyzeDependenciesAsync(
        CliOptions options,
        string solutionPath,
        string solutionDir,
        CancellationToken cancellationToken)
    {
        if (!options.SkipDependencyProbe)
            return await DependencyProbe.AnalyzeAsync(solutionPath, solutionDir, cancellationToken);

        return new DimensionResult
        {
            Status = "skipped",
            Basis = "Dependency probe skipped via --skip-dependency-probe."
        };
    }

    private static int CountVulnerabilities(DimensionResult result)
    {
        return result.Findings.Count(f =>
            f.Category.Contains("vulnerable", StringComparison.OrdinalIgnoreCase));
    }

    private static List<(string Name, Microsoft.CodeAnalysis.Compilation Compilation)> ToProbeInputs(
        IReadOnlyCollection<CompiledProject> projects)
    {
        return projects
            .Select(project => (project.Name, project.Compilation))
            .ToList();
    }
}
