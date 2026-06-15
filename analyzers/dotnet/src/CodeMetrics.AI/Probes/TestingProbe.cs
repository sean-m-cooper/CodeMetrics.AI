using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

public static class TestingProbe
{
    public static DimensionResult Analyze(
        IReadOnlyList<(string Name, Compilation Compilation)> allProjects,
        IReadOnlyList<string> analyzedProjectNames,
        string solutionDir)
    {
        var findings = new List<Finding>();
        var projectSet = TestProjectClassifier.Classify(allProjects, solutionDir);
        var metrics = TestMetricsCollector.Collect(projectSet.TestProjects, solutionDir, findings);
        var testProjectNames = projectSet.TestProjects.Select(p => p.Name).ToList();
        var uncoveredProjects = TestCoverageInspector.FindUncoveredProductionProjects(
            analyzedProjectNames,
            testProjectNames);
        var coverageFileFound = TestCoverageInspector.CoverageFileExists(solutionDir);

        findings.AddRange(CreateUncoveredProjectFindings(uncoveredProjects));

        return BuildResult(
            findings,
            metrics,
            analyzedProjectNames.Count,
            projectSet.TestProjects.Count,
            uncoveredProjects,
            coverageFileFound);
    }

    private static IEnumerable<Finding> CreateUncoveredProjectFindings(IEnumerable<string> uncoveredProjects)
    {
        return uncoveredProjects.Select(uncovered => new Finding
        {
            Category = "uncoveredProject",
            Severity = "warning",
            Project = uncovered,
            Message = $"Production project '{uncovered}' has no matching test project."
        });
    }

    private static DimensionResult BuildResult(
        List<Finding> findings,
        TestMetrics metrics,
        int productionProjectCount,
        int testProjectCount,
        List<string> uncoveredProjects,
        bool coverageFileFound)
    {
        var assertionDensity = AssertionDensity(metrics);

        return new DimensionResult
        {
            Status = "scored",
            Score = Score(metrics, testProjectCount, assertionDensity, uncoveredProjects.Count),
            Basis = $"testProjects={testProjectCount}, testMethods={metrics.TestMethodCount}, " +
                    $"skipped={metrics.SkippedTests}, placeholders={metrics.PlaceholderTests}, " +
                    $"assertions={metrics.AssertionCount}, assertionDensity={assertionDensity:F2}, " +
                    $"uncoveredProjects={uncoveredProjects.Count}, coverageFile={coverageFileFound}.",
            Findings = findings,
            Extra =
            {
                ["testMetrics"] = new
                {
                    testProjects = testProjectCount,
                    productionProjects = productionProjectCount,
                    testMethods = metrics.TestMethodCount,
                    skippedTests = metrics.SkippedTests,
                    placeholderTests = metrics.PlaceholderTests,
                    assertions = metrics.AssertionCount,
                    assertionDensity,
                    uncoveredProjects,
                    coverageFileFound
                }
            }
        };
    }

    private static double AssertionDensity(TestMetrics metrics)
    {
        return metrics.TestMethodCount > 0
            ? (double)metrics.AssertionCount / metrics.TestMethodCount
            : 0.0;
    }

    private static double Score(
        TestMetrics metrics,
        int testProjectCount,
        double assertionDensity,
        int uncoveredProjectCount)
    {
        if (testProjectCount == 0 || metrics.TestMethodCount == 0)
            return 0;
        if (assertionDensity == 0.0 || metrics.PlaceholderTests >= metrics.TestMethodCount)
            return 2;
        if (metrics.PlaceholderTests > 0 || metrics.SkippedTests > 2)
            return 4;
        if (uncoveredProjectCount > 0 || assertionDensity < 1.0)
            return 6;
        if (metrics.SkippedTests > 0)
            return 8;
        return 10;
    }
}
