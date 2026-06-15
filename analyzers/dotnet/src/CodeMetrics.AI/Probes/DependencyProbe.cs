namespace CodeMetrics.AI.Probes;

public static class DependencyProbe
{
    public static async Task<DimensionResult> AnalyzeAsync(
        string solutionPath,
        string solutionDir,
        CancellationToken cancellationToken = default)
    {
        var output = await DotnetPackageListRunner.RunAsync(solutionPath, cancellationToken);

        return AnalyzeOutput(
            output.VulnerableOutput,
            output.OutdatedOutput,
            output.DeprecatedOutput,
            solutionDir,
            output.AnyCommandFailed);
    }

    public static DimensionResult AnalyzeOutput(
        string vulnerableOutput,
        string outdatedOutput,
        string deprecatedOutput,
        string solutionDir,
        bool anyCommandFailed)
    {
        var (metrics, findings) = DependencyOutputAnalyzer.Analyze(
            vulnerableOutput,
            outdatedOutput,
            deprecatedOutput,
            solutionDir,
            anyCommandFailed);

        return DependencyResultBuilder.Build(metrics, findings);
    }
}
