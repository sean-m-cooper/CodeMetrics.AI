using System.Text.RegularExpressions;

namespace CodeMetrics.AI.Probes;

public sealed record OutdatedPackageUpgrade(
    string? Project,
    string? TargetFramework,
    string Package,
    string LatestVersion);

public static class PackageFrameworkCompatibility
{
    public static IReadOnlyList<OutdatedPackageUpgrade> ParseOutdatedOutput(string output)
    {
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
                columns[^1]));
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

    internal static async Task<IReadOnlyDictionary<OutdatedPackageUpgrade, bool>> AssessAsync(
        IReadOnlyList<OutdatedPackageUpgrade> upgrades,
        string commandOutput,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<OutdatedPackageUpgrade, bool>();
        var assessable = upgrades
            .Where(upgrade =>
                !string.IsNullOrWhiteSpace(upgrade.TargetFramework) &&
                !string.IsNullOrWhiteSpace(upgrade.LatestVersion))
            .GroupBy(
                upgrade => (upgrade.Package, upgrade.LatestVersion),
                StringTupleComparer.OrdinalIgnoreCase)
            .ToList();

        if (assessable.Count == 0)
            return result;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var resolver = await NuGetPackageFrameworkResolver.CreateAsync(
            commandOutput,
            timeout.Token);
        using var gate = new SemaphoreSlim(4);
        var tasks = assessable
            .Select(group => AssessGroupAsync(group, resolver, gate, timeout.Token, cancellationToken))
            .ToList();

        foreach (var assessment in await Task.WhenAll(tasks))
            AddAssessmentResults(result, assessment.Group, assessment.Frameworks);

        return result;
    }

    private static async Task<(
        IGrouping<(string Package, string? LatestVersion), OutdatedPackageUpgrade> Group,
        PackageFrameworkSet? Frameworks)> AssessGroupAsync(
        IGrouping<(string Package, string? LatestVersion), OutdatedPackageUpgrade> group,
        NuGetPackageFrameworkResolver resolver,
        SemaphoreSlim gate,
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        var entered = false;
        try
        {
            await gate.WaitAsync(timeoutToken);
            entered = true;
            var frameworks = await resolver.FindAsync(
                group.Key.Package,
                group.Key.LatestVersion!,
                timeoutToken);
            return (group, frameworks);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            return (group, null);
        }
        finally
        {
            if (entered)
                gate.Release();
        }
    }

    private static void AddAssessmentResults(
        IDictionary<OutdatedPackageUpgrade, bool> result,
        IEnumerable<OutdatedPackageUpgrade> upgrades,
        PackageFrameworkSet? frameworks)
    {
        if (frameworks == null)
            return;

        foreach (var upgrade in upgrades)
        {
            var compatible = frameworks.IsFrameworkAgnostic
                ? true
                : IsCompatible(upgrade.TargetFramework!, frameworks.Frameworks);
            if (compatible.HasValue)
                result[upgrade] = compatible.Value;
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return string.IsNullOrEmpty(text)
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed class StringTupleComparer : IEqualityComparer<(string Package, string? Version)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();

        public bool Equals(
            (string Package, string? Version) x,
            (string Package, string? Version) y)
        {
            return StringComparer.OrdinalIgnoreCase.Equals(x.Package, y.Package) &&
                   StringComparer.OrdinalIgnoreCase.Equals(x.Version, y.Version);
        }

        public int GetHashCode((string Package, string? Version) obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Package),
                obj.Version == null
                    ? 0
                    : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Version));
        }
    }
}
