namespace CodeMetrics.AI.Probes;

/// <summary>Separates report occurrences, package/version scoring identities and project scope.</summary>
internal static class DependencyFindingPopulation
{
    internal const string CountingUnit = "distinct package IDs and resolved versions per scoring category; project/TFM rows retained";

    public static int Count(IEnumerable<Finding> findings) => Groups(findings).Count();

    public static bool IsVulnerability(Finding finding) =>
        finding.Category is "vulnerableDirectDependency" or "vulnerableTransitiveDependency";

    public static bool IsSecurityInput(Finding finding) => IsVulnerability(finding) &&
        finding.Observations.GetValueOrDefault("securityScoreDisposition") as string != "excludedDevelopmentScope";

    public static void Attach(DimensionResult result, string root, IReadOnlyDictionary<string, string>? projectScopes)
    {
        var packages = result.Findings.Where(f => f.Package != null).ToArray();
        foreach (var finding in packages)
        {
            var scope = ResolveScope(finding.Project, root, projectScopes);
            finding.Observations["dependencyScope"] = scope;
            finding.Observations["dependencyScopeBasis"] = scope == "unknown"
                ? "No unambiguous loaded project classification; retained as a security input."
                : "Loaded project selection; describes project use, not proof of published package contents.";
            if (IsVulnerability(finding))
                finding.Observations["securityScoreDisposition"] = scope is "test" or "benchmark"
                    ? "excludedDevelopmentScope" : "included";
        }
        foreach (var group in packages.GroupBy(f => f.Category).SelectMany(Groups))
            foreach (var finding in group)
            {
                finding.Observations["countingUnit"] = "distinctPackageVersion";
                finding.Observations["packageObservationCount"] = group.Count();
            }
        result.Extra["dependencyPopulation"] = new
        {
            countingUnit = CountingUnit,
            packageObservations = packages.Length,
            securityImportedVulnerabilities = Count(packages.Where(IsSecurityInput)),
            securityExcludedDevelopmentObservations = packages.Count(f => IsVulnerability(f) && !IsSecurityInput(f)),
            scopes = packages.GroupBy(f => (string)f.Observations["dependencyScope"]!)
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new
                {
                    scope = g.Key,
                    observations = g.Count(),
                    vulnerabilityPackageVersions = Count(g.Where(IsVulnerability)),
                    projects = g.Select(f => f.Project).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray()
                }).ToArray()
        };
    }

    private static string ResolveScope(string? project, string root, IReadOnlyDictionary<string, string>? scopes)
    {
        if (string.IsNullOrWhiteSpace(project) || scopes == null) return "unknown";
        var path = Path.GetFullPath(project, Path.GetFullPath(root));
        if (!scopes.TryGetValue(path, out var scope)) return "unknown";
        return scope is "production" or "test" or "benchmark" ? scope : "unknown";
    }

    private static IEnumerable<IGrouping<string, Finding>> Groups(IEnumerable<Finding> findings) =>
        findings.Select((finding, index) => (Finding: finding, Index: index)).GroupBy(item =>
            item.Finding.Package is { Length: > 0 } package &&
            item.Finding.Observations.GetValueOrDefault("resolvedVersion") is string { Length: > 0 } version
                ? package.ToUpperInvariant() + "\0" + version.ToUpperInvariant()
                : "unknown\0" + item.Index,
            item => item.Finding, StringComparer.Ordinal);
}
