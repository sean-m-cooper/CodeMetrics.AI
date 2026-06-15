using CodeMetrics.AI.Metrics;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal sealed record MetricsAnalysisResult(
    List<TypeMetrics> TypeMetrics,
    List<MemberMetrics> MemberMetrics,
    List<(string ProjectName, Compilation Compilation)> ProjectCompilations,
    List<(string Name, Compilation Compilation)> ProjectNameCompilations,
    List<(string Name, Compilation Compilation, string? ProjectFilePath)> ProjectsWithPaths);
