using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

public static class DocumentationProbe
{
    public static DimensionResult Analyze(
        string solutionDir,
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> projects)
    {
        var metrics = DocumentationMetricsCollector.Collect(solutionDir, projects);
        return DocumentationResultBuilder.Build(metrics);
    }
}
