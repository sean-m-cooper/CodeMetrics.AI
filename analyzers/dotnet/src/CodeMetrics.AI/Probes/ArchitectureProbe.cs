using CodeMetrics.AI.Metrics;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

public static class ArchitectureProbe
{
    public static DimensionResult Analyze(
        IReadOnlyList<(string Name, Compilation Compilation)> projects,
        IReadOnlyList<TypeMetrics> typeMetrics,
        string solutionDir)
    {
        var findings = new List<Finding>();
        var cycles = ProjectCycleDetector.Detect(solutionDir);
        var hotspots = ArchitectureHotspotFinder.Find(typeMetrics);

        findings.AddRange(CreateCycleFindings(cycles));
        findings.AddRange(FindLayeringViolations(projects, solutionDir));
        findings.AddRange(hotspots);

        return BuildResult(findings, cycles, hotspots);
    }

    private static IEnumerable<Finding> CreateCycleFindings(List<List<string>> cycles)
    {
        return cycles.Select(cycle => new Finding
        {
            Category = "projectCycle",
            Severity = "error",
            Message = $"Circular project reference detected: {string.Join(" -> ", cycle)} -> {cycle[0]}"
        });
    }

    private static List<Finding> FindLayeringViolations(
        IReadOnlyList<(string Name, Compilation Compilation)> projects,
        string solutionDir)
    {
        var findings = new List<Finding>();

        foreach (var (projectName, compilation) in projects)
            AnalyzeProject(projectName, compilation, solutionDir, findings);

        return findings;
    }

    private static void AnalyzeProject(
        string projectName,
        Compilation compilation,
        string solutionDir,
        List<Finding> findings)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                continue;

            ArchitectureLayeringAnalyzer.Analyze(
                tree.GetRoot(),
                compilation.GetSemanticModel(tree),
                tree.FilePath,
                projectName,
                findings);
        }
    }

    private static DimensionResult BuildResult(
        List<Finding> findings,
        List<List<string>> cycles,
        List<Finding> hotspots)
    {
        var errorCount = findings.Count(f => f.Severity == "error");
        var warningCount = findings.Count(f => f.Severity == "warning");

        return new DimensionResult
        {
            Status = "scored",
            Score = Score(cycles.Count, hotspots.Count, errorCount, warningCount),
            Basis = $"Findings: {findings.Count} (errors: {errorCount}, warnings: {warningCount}). " +
                    $"Cycles: {cycles.Count}, hotspots: {hotspots.Count}.",
            Findings = findings,
            Extra =
            {
                ["cycles"] = cycles.Select(c => string.Join(" -> ", c) + " -> " + c[0]).ToList(),
                ["hotspots"] = hotspots.Select(h => new { h.Type, h.Category, h.Message }).ToList<object>()
            }
        };
    }

    private static double Score(int cycleCount, int hotspotCount, int errorCount, int warningCount)
    {
        if (cycleCount > 0 || errorCount > 0 || hotspotCount > 0)
            return 2;
        if (warningCount > 2)
            return 4;
        if (warningCount >= 1)
            return 6;
        return 10;
    }
}
