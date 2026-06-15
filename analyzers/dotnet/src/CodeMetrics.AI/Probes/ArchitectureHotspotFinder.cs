using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

internal static class ArchitectureHotspotFinder
{
    public static List<Finding> Find(IReadOnlyList<TypeMetrics> typeMetrics)
    {
        return typeMetrics
            .SelectMany(FindForType)
            .OrderByDescending(f => CyclomaticComplexity(typeMetrics, f))
            .ThenByDescending(f => ClassCoupling(typeMetrics, f))
            .ThenByDescending(f => LinesOfSource(typeMetrics, f))
            .Take(10)
            .ToList();
    }

    private static IEnumerable<Finding> FindForType(TypeMetrics typeMetrics)
    {
        if (typeMetrics.CyclomaticComplexity >= 80)
            yield return CreateFinding(typeMetrics, "highCyclomaticComplexity", $"Type '{typeMetrics.Type}' has cyclomatic complexity of {typeMetrics.CyclomaticComplexity} (threshold: 80).");

        var couplingThreshold = typeMetrics.Type.EndsWith("Controller", StringComparison.Ordinal) ? 50 : 30;
        if (typeMetrics.ClassCoupling >= couplingThreshold)
            yield return CreateFinding(typeMetrics, "highCoupling", $"Type '{typeMetrics.Type}' has class coupling of {typeMetrics.ClassCoupling} (threshold: {couplingThreshold}).");

        if (typeMetrics.LinesOfSource >= 500)
            yield return CreateFinding(typeMetrics, "largeClass", $"Type '{typeMetrics.Type}' has {typeMetrics.LinesOfSource} lines of source (threshold: 500).");
    }

    private static Finding CreateFinding(TypeMetrics metrics, string category, string message)
    {
        return new Finding
        {
            Category = category,
            Severity = "warning",
            File = metrics.FilePath,
            Project = metrics.Project,
            Type = metrics.Type,
            Message = message
        };
    }

    private static int CyclomaticComplexity(IReadOnlyList<TypeMetrics> typeMetrics, Finding finding)
    {
        return finding.Category == "highCyclomaticComplexity"
            ? FindType(typeMetrics, finding)?.CyclomaticComplexity ?? 0
            : 0;
    }

    private static int ClassCoupling(IReadOnlyList<TypeMetrics> typeMetrics, Finding finding)
    {
        return finding.Category == "highCoupling"
            ? FindType(typeMetrics, finding)?.ClassCoupling ?? 0
            : 0;
    }

    private static int LinesOfSource(IReadOnlyList<TypeMetrics> typeMetrics, Finding finding)
    {
        return finding.Category == "largeClass"
            ? FindType(typeMetrics, finding)?.LinesOfSource ?? 0
            : 0;
    }

    private static TypeMetrics? FindType(IReadOnlyList<TypeMetrics> typeMetrics, Finding finding)
    {
        return typeMetrics.FirstOrDefault(type => type.Type == finding.Type);
    }
}
