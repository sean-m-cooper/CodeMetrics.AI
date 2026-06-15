namespace CodeMetrics.AI;

internal static class SolutionAnalysisWorkflow
{
    public static async Task RunAsync(
        CliOptions options,
        string solutionPath,
        string solutionDir,
        CancellationToken cancellationToken)
    {
        using var workspace = WorkspaceLoader.CreateWorkspace();
        var solution = await WorkspaceLoader.OpenSolutionAsync(workspace, solutionPath, cancellationToken);
        var projectContext = await SolutionProjectContextBuilder.BuildAsync(solution, cancellationToken);
        var metrics = MetricsAnalysisCollector.Collect(projectContext.AnalyzedCompiledProjects, solutionDir);

        ConsoleReporter.WritePopulationSummary(metrics);

        await AnalysisOutputWriter.WriteCsvAsync(options, metrics, cancellationToken);

        var dimensions = await ScorecardDimensionRunner.RunAsync(
            options,
            solutionPath,
            solutionDir,
            metrics,
            projectContext.AllCompiledProjects,
            projectContext.AnalyzedNames,
            cancellationToken);
        var evidence = EvidenceFactory.Create(
            solutionPath,
            solutionDir,
            options,
            projectContext.Selection,
            metrics,
            dimensions);

        await AnalysisOutputWriter.WriteEvidenceAsync(options, evidence, cancellationToken);
        Console.WriteLine("Done.");
    }
}
