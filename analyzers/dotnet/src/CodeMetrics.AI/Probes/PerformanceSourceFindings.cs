using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

/// <summary>Counts authored async finding sites once while retaining framework-specific evidence.</summary>
internal static class PerformanceSourceFindings
{
    private sealed record Site(string File, int Start, int Length, string Category, int UnknownIdentity);

    public static Dictionary<string, object?> Location(SyntaxNode node) => new()
    {
        ["sourceSpanStart"] = node.SpanStart,
        ["sourceSpanLength"] = node.Span.Length
    };

    public static List<Finding> Collapse(IEnumerable<Finding> observations, string? solutionDir)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(solutionDir) ? "." : solutionDir);
        return observations.Select((finding, index) => (Finding: finding, Index: index))
            .GroupBy(item => Identity(item.Finding, item.Index, root))
            .OrderBy(group => group.Key.File, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Start).ThenBy(group => group.Key.Length)
            .ThenBy(group => group.Key.Category, StringComparer.Ordinal)
            .ThenBy(group => group.Key.UnknownIdentity)
            .Select(group => Summarize(group.Select(item => item.Finding)))
            .ToList();
    }

    private static Site Identity(Finding finding, int index, string root)
    {
        if (string.IsNullOrWhiteSpace(finding.File) ||
            !finding.Observations.TryGetValue("sourceSpanStart", out var start) || start is not int offset || offset < 0 ||
            !finding.Observations.TryGetValue("sourceSpanLength", out var length) || length is not int size || size <= 0)
        {
            // Missing physical identity must never turn unrelated observations into one finding.
            return new Site("", 0, 0, finding.Category, index);
        }

        var file = Path.GetFullPath(finding.File, root);
        if (OperatingSystem.IsWindows())
            file = file.ToUpperInvariant();
        return new Site(file, offset, size, finding.Category, -1);
    }

    private static Finding Summarize(IEnumerable<Finding> observations)
    {
        var ordered = observations.OrderBy(finding => SeverityOrder(finding.Severity))
            .ThenBy(finding => finding.Project, StringComparer.Ordinal)
            .ThenBy(finding => finding.Message, StringComparer.Ordinal)
            .ThenBy(finding => finding.File, StringComparer.Ordinal).ToList();
        var representative = ordered[0];
        representative.Observations["countingUnit"] = "distinctSourceFinding";
        representative.Observations["variantAggregation"] = "maximumSeverityPerSourceSite";
        representative.Observations["observationCount"] = ordered.Count;
        representative.Observations["affectedProjects"] = ordered.Select(finding => finding.Project)
            .Distinct(StringComparer.Ordinal).OrderBy(project => project, StringComparer.Ordinal).ToArray();
        representative.Observations["projectFrameworkObservations"] = ordered.Select(finding => new
        {
            project = finding.Project,
            severity = finding.Severity,
            message = finding.Message
        }).ToArray();
        return representative;
    }

    private static int SeverityOrder(string severity) => severity switch
    {
        "error" => 0,
        "warning" => 1,
        _ => 2
    };
}
