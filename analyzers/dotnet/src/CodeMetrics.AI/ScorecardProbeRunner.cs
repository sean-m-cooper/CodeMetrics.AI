using CodeMetrics.AI.Probes;

namespace CodeMetrics.AI;

internal static class ScorecardProbeRunner
{
    public static async Task<Dictionary<string, object>> AnalyzeAsync(
        SolutionAnalysisContext context,
        string solutionPath,
        string solutionDir,
        bool skipDependencyProbe,
        CancellationToken cancellationToken,
        string? coveragePath = null)
    {
        var dimensions = CreateCoreDimensions(context, solutionDir);
        var dependency = await AnalyzeDependenciesAsync(
            solutionPath,
            solutionDir,
            skipDependencyProbe,
            cancellationToken);
        dimensions["dependencyManagement"] = dependency;
        AddDependentDimensions(dimensions, context, dependency, solutionDir, coveragePath);
        Output.EvidenceEnricher.Enrich(dimensions, context, solutionDir);
        if (context.Diagnostics.Count > 0)
        {
            foreach (var key in dimensions.Keys.Where(key => key != "dependencyManagement").ToList())
            {
                var previous = (DimensionResult)dimensions[key];
                previous.Extra.Remove("scoring");
                dimensions[key] = new DimensionResult
                {
                    Status = "failed",
                    Basis = "Incomplete source analysis; findings are partial and no score is assigned.",
                    Findings = previous.Findings,
                    Extra = previous.Extra
                };
            }
        }
        return dimensions;
    }

    private static Dictionary<string, object> CreateCoreDimensions(
        SolutionAnalysisContext context,
        string solutionDir)
    {
        return new Dictionary<string, object>
        {
            ["codeQuality"] = CodeQualityProbe.Analyze(context.TypeMetrics),
            ["maintainability"] = MaintainabilityProbe.Analyze(context.TypeMetrics),
            ["errorHandling"] = ErrorHandlingProbe.Analyze(
                context.AnalyzedProjectCompilations, solutionDir),
            ["performanceAsync"] = PerformanceAsyncProbe.Analyze(
                context.AnalyzedProjectCompilations, solutionDir)
        };
    }

    private static async Task<DimensionResult> AnalyzeDependenciesAsync(
        string solutionPath,
        string solutionDir,
        bool skipDependencyProbe,
        CancellationToken cancellationToken)
    {
        return skipDependencyProbe
            ? new DimensionResult
            {
                Status = "skipped",
                Basis = "Dependency probe skipped via --skip-dependency-probe."
            }
            : await DependencyProbe.AnalyzeAsync(
                solutionPath, solutionDir, cancellationToken);
    }

    private static void AddDependentDimensions(
        IDictionary<string, object> dimensions,
        SolutionAnalysisContext context,
        DimensionResult dependency,
        string solutionDir,
        string? coveragePath)
    {
        var vulnerabilityCount = dependency.Findings.Count(finding =>
            finding.Category.Contains("vulnerable", StringComparison.OrdinalIgnoreCase));
        dimensions["security"] = SecurityProbe.Analyze(
            context.AnalyzedProjectCompilations, vulnerabilityCount, solutionDir);
        dimensions["testing"] = TestingProbe.Analyze(
            context.AllProjectCompilations, context.AnalyzedProjectNames, solutionDir, coveragePath);
        dimensions["documentation"] = DocumentationProbe.Analyze(
            solutionDir, context.ProjectsWithPaths);
        dimensions["architecture"] = ArchitectureProbe.Analyze(
            context.AnalyzedProjectCompilations, context.TypeMetrics, solutionDir);
    }
}
