using System.Text.RegularExpressions;

namespace CodeMetrics.AI.Probes;

public sealed record OutdatedPackageUpgrade(
    string? Project,
    string? TargetFramework,
    string Package,
    string LatestVersion);

public static class PackageFrameworkCompatibility
{
    internal const string NoCandidateText = "Not found at the sources";

    internal static bool HasNoReportedCandidate(string? version) =>
        string.Equals(version?.Trim(), NoCandidateText, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<OutdatedPackageUpgrade> ParseOutdatedOutput(string output)
    {
        if (PackageReport.IsJson(output))
            return PackageReport.Parse(output).Select(package => package.Upgrade).ToArray();

        var upgrades = new List<OutdatedPackageUpgrade>();
        string? currentProject = null;
        string? currentFramework = null;

        foreach (var line in SplitLines(output))
        {
            var trimmed = line.Trim();
            var projectMatch = Regex.Match(
                trimmed,
                "^Project\\s+(?:[`'\"](?<quoted>.+?)[`'\"]|(?<plain>\\S+))\\s+has\\b",
                RegexOptions.IgnoreCase);
            if (projectMatch.Success)
            {
                currentProject = projectMatch.Groups["quoted"].Success
                    ? projectMatch.Groups["quoted"].Value
                    : projectMatch.Groups["plain"].Value;
                currentFramework = null;
                continue;
            }

            var frameworkMatch = Regex.Match(trimmed, "^\\[(?<tfm>[^]]+)]\\s*:$");
            if (frameworkMatch.Success)
            {
                currentFramework = frameworkMatch.Groups["tfm"].Value.Trim();
                continue;
            }

            if (!trimmed.StartsWith('>'))
                continue;

            var columns = trimmed.TrimStart('>', ' ')
                .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length < 2)
                continue;

            upgrades.Add(new OutdatedPackageUpgrade(
                currentProject,
                currentFramework,
                columns[0],
                trimmed.EndsWith(NoCandidateText, StringComparison.OrdinalIgnoreCase) ? NoCandidateText : columns[^1]));
        }

        return upgrades;
    }

    /// <summary>
    /// Returns true when at least one package asset framework is compatible, false when all
    /// parsed assets require a different framework, and null when either side cannot be parsed.
    /// An empty package-framework list represents a framework-agnostic package.
    /// </summary>
    public static bool? IsCompatible(
        string projectTargetFramework,
        IEnumerable<string> packageTargetFrameworks)
    {
        return TargetFrameworkCompatibility.IsCompatible(
            projectTargetFramework,
            packageTargetFrameworks);
    }

    public static bool? IsPackageCompatible(
        string projectTargetFramework,
        IEnumerable<string> packageAssetPaths,
        string? nuspecXml = null)
    {
        var frameworks = PackageAssetInspector.Inspect(packageAssetPaths, nuspecXml);
        return frameworks.IsFrameworkAgnostic
            ? true
            : IsCompatible(projectTargetFramework, frameworks.Frameworks);
    }

    internal static async Task<PackageCompatibilityAssessment> AssessAsync(
        IReadOnlyList<OutdatedPackageUpgrade> upgrades,
        string commandOutput,
        CancellationToken cancellationToken = default,
        TimeSpan? budget = null,
        HttpClient? client = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var noCandidates = upgrades.Where(u => HasNoReportedCandidate(u.LatestVersion)).ToArray();
        var groups = upgrades.Where(u => !HasNoReportedCandidate(u.LatestVersion)).GroupBy(upgrade => (upgrade.Package, upgrade.LatestVersion),
            StringTupleComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<OutdatedPackageUpgrade, bool>();
        var failures = new List<PackageCompatibilityFailure>();
        if (groups.Count == 0) return new(result, failures, 0, upgrades.Count, 0) { NoReportedCandidates = noCandidates };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget ?? TimeSpan.FromSeconds(20));
        NuGetPackageFrameworkResolver resolver;
        try
        {
            resolver = await NuGetPackageFrameworkResolver.CreateAsync(commandOutput, timeout.Token, client);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            foreach (var group in groups)
                failures.Add(new(group.Key.Package, group.Key.Item2, group.Count(), ["sourceDiscoveryBudgetExceeded"]));
            return new(result, failures, groups.Count, upgrades.Count, watch.Elapsed.TotalMilliseconds) { NoReportedCandidates = noCandidates };
        }
        using var gate = new SemaphoreSlim(4);
        var tasks = groups.Select(async group =>
        {
            var reasons = new List<string>();
            var entered = false;
            PackageFrameworkSet? frameworks = null;
            try
            {
                await gate.WaitAsync(timeout.Token);
                entered = true;
                if (!Regex.IsMatch(group.Key.Item2, @"^[0-9]+(?:\.[0-9]+){0,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$"))
                    reasons.Add("latestVersionUnavailable");
                else
                    frameworks = await resolver.FindAsync(group.Key.Package, group.Key.Item2, timeout.Token, reasons.Add);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                reasons.Add(entered ? "packageBudgetExceeded" : "queueBudgetExceeded");
            }
            finally
            {
                if (entered) gate.Release();
            }
            return (Group: group, Frameworks: frameworks, Reasons: reasons);
        }).ToList();
        foreach (var assessment in await Task.WhenAll(tasks))
        {
            var missing = 0;
            foreach (var upgrade in assessment.Group)
            {
                bool? compatible = assessment.Frameworks == null ? null :
                    assessment.Frameworks.IsFrameworkAgnostic ? true :
                    IsCompatible(upgrade.TargetFramework ?? "", assessment.Frameworks.Frameworks);
                if (compatible.HasValue) result[upgrade] = compatible.Value;
                else missing++;
            }
            if (missing > 0)
                failures.Add(new(assessment.Group.Key.Package, assessment.Group.Key.Item2,
                    missing, assessment.Reasons.Count > 0 ? assessment.Reasons.Distinct().ToArray() : ["frameworkMetadataUnsupported"]));
        }
        return new(result, failures, groups.Count, upgrades.Count, watch.Elapsed.TotalMilliseconds) { NoReportedCandidates = noCandidates };
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return string.IsNullOrEmpty(text)
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Package, string Version)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();

        public bool Equals(
            (string Package, string Version) x,
            (string Package, string Version) y)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(x.Package, y.Package) &&
                   StringComparer.OrdinalIgnoreCase.Equals(x.Version, y.Version);
        }

        public int GetHashCode((string Package, string Version) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Package),
                obj.Version == null
                    ? 0
                    : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Version));
        }
    }
}

public sealed record PackageCompatibilityFailure(
    string Package, string LatestVersion, int AffectedObservations, IReadOnlyList<string> Reasons);

public sealed record PackageCompatibilityAssessment(
    IReadOnlyDictionary<OutdatedPackageUpgrade, bool> Results,
    IReadOnlyList<PackageCompatibilityFailure> Failures,
    int UniquePackageVersions,
    int TotalObservations,
    double ElapsedMilliseconds)
{
    public IReadOnlyList<OutdatedPackageUpgrade> NoReportedCandidates { get; init; } = [];
}
