using CodeMetrics.AI.Metrics;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

public static class ArchitectureProbe
{
    public static DimensionResult Analyze(
        IReadOnlyList<(string Name, Compilation Compilation)> projects,
        IReadOnlyList<TypeMetrics> typeMetrics,
        string solutionDir,
        IReadOnlyList<string>? projectPaths = null)
    {
        var observations = ArchitectureObservationCollector.Collect(projects, solutionDir, projectPaths);
        var assessment = ArchitectureScoring.Evaluate(typeMetrics, observations);
        return ArchitectureEvidence.Create(typeMetrics, observations, assessment);
    }
}
