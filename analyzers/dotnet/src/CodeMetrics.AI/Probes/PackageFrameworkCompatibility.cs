using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodeMetrics.AI.Probes;

public sealed record OutdatedPackageUpgrade(
    string? Project,
    string? TargetFramework,
    string Package,
    string LatestVersion);

public static class PackageFrameworkCompatibility
{
    private const long MaximumPackageBytes = 50 * 1024 * 1024;

    private sealed record PackageFrameworkSet(
        bool IsFrameworkAgnostic,
        IReadOnlyList<string> Frameworks);

    private sealed record ParsedFramework(
        FrameworkFamily Family,
        Version Version,
        string? Platform = null,
        Version? PlatformVersion = null);

    private enum FrameworkFamily
    {
        ModernDotNet,
        NetCoreApp,
        NetStandard,
        NetFramework,
        Any
    }

    private static readonly HttpClient HttpClient = CreateHttpClient();

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
        var packageFrameworkValues = packageTargetFrameworks.ToList();
        var projectFramework = ParseFramework(projectTargetFramework);
        if (projectFramework == null)
            return null;

        var parsedPackageFrameworks = packageFrameworkValues
            .Select(value => (Value: value, Framework: ParseFramework(value)))
            .ToList();
        var packageFrameworks = parsedPackageFrameworks
            .Where(item => item.Framework != null)
            .Select(item => item.Framework!)
            .Distinct()
            .ToList();

        if (packageFrameworks.Count == 0)
            return packageFrameworkValues.Count == 0 ? true : null;

        if (packageFrameworks.Any(packageFramework =>
                IsFrameworkCompatible(projectFramework, packageFramework)))
        {
            return true;
        }

        return parsedPackageFrameworks.Any(item => item.Framework == null) ? null : false;
    }

    public static bool? IsPackageCompatible(
        string projectTargetFramework,
        IEnumerable<string> packageAssetPaths,
        string? nuspecXml = null)
    {
        var frameworks = InspectFiles(
            packageAssetPaths.Select(path => path.Replace('\\', '/')),
            () => string.IsNullOrWhiteSpace(nuspecXml) ? null : XDocument.Parse(nuspecXml));
        return frameworks.IsFrameworkAgnostic
            ? true
            : IsCompatible(projectTargetFramework, frameworks.Frameworks);
    }

    internal static async Task<IReadOnlyDictionary<OutdatedPackageUpgrade, bool>> AssessAsync(
        IReadOnlyList<OutdatedPackageUpgrade> upgrades,
        string commandOutput)
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

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var packageBaseAddresses = await FindPackageBaseAddressesAsync(commandOutput, timeout.Token);
        var localSources = FindLocalSources(commandOutput);
        var gate = new SemaphoreSlim(4);

        var tasks = assessable.Select(async group =>
        {
            var entered = false;
            try
            {
                await gate.WaitAsync(timeout.Token);
                entered = true;
                var frameworkSet = await FindPackageFrameworksAsync(
                    group.Key.Package,
                    group.Key.Item2,
                    packageBaseAddresses,
                    localSources,
                    timeout.Token);
                return (Group: group, Frameworks: frameworkSet);
            }
            catch (OperationCanceledException)
            {
                return (Group: group, Frameworks: (PackageFrameworkSet?)null);
            }
            finally
            {
                if (entered)
                    gate.Release();
            }
        }).ToList();

        foreach (var assessment in await Task.WhenAll(tasks))
        {
            if (assessment.Frameworks == null)
                continue;

            foreach (var upgrade in assessment.Group)
            {
                var compatible = assessment.Frameworks.IsFrameworkAgnostic
                    ? true
                    : IsCompatible(
                        upgrade.TargetFramework!,
                        assessment.Frameworks.Frameworks);
                if (compatible.HasValue)
                    result[upgrade] = compatible.Value;
            }
        }

        return result;
    }

    private static async Task<PackageFrameworkSet?> FindPackageFrameworksAsync(
        string package,
        string version,
        IReadOnlyList<Uri> packageBaseAddresses,
        IReadOnlyList<string> localSources,
        CancellationToken cancellationToken)
    {
        var local = FindLocalPackage(package, version, localSources);
        if (local != null)
            return local;

        var normalizedPackage = package.ToLowerInvariant();
        var normalizedVersion = NormalizeVersion(version);
        foreach (var baseAddress in packageBaseAddresses)
        {
            try
            {
                var packageUri = new Uri(
                    baseAddress,
                    $"{Uri.EscapeDataString(normalizedPackage)}/" +
                    $"{Uri.EscapeDataString(normalizedVersion)}/" +
                    $"{Uri.EscapeDataString(normalizedPackage)}.{Uri.EscapeDataString(normalizedVersion)}.nupkg");
                using var response = await HttpClient.GetAsync(
                    packageUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode ||
                    response.Content.Headers.ContentLength is > MaximumPackageBytes)
                {
                    continue;
                }

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var packageStream = new MemoryStream();
                await CopyWithLimitAsync(responseStream, packageStream, cancellationToken);
                packageStream.Position = 0;
                using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
                return InspectArchive(archive);
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                // Try the next configured source. If all fail, compatibility remains unknown.
            }
        }

        return null;
    }

    private static PackageFrameworkSet? FindLocalPackage(
        string package,
        string version,
        IReadOnlyList<string> localSources)
    {
        var normalizedVersion = NormalizeVersion(version);
        var packageDirectories = new List<string>();
        var globalPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(globalPackages))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
                globalPackages = Path.Combine(userProfile, ".nuget", "packages");
        }

        if (!string.IsNullOrWhiteSpace(globalPackages))
        {
            packageDirectories.Add(Path.Combine(
                globalPackages,
                package.ToLowerInvariant(),
                normalizedVersion));
        }

        foreach (var source in localSources)
        {
            packageDirectories.Add(Path.Combine(source, package, normalizedVersion));
            packageDirectories.Add(Path.Combine(source, package.ToLowerInvariant(), normalizedVersion));

            var nupkg = Path.Combine(source, $"{package}.{normalizedVersion}.nupkg");
            if (File.Exists(nupkg))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(nupkg);
                    return InspectArchive(archive);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    // Continue looking in other local/global package locations.
                }
            }
        }

        foreach (var directory in packageDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
                continue;

            try
            {
                var nestedNupkg = Directory.EnumerateFiles(
                        directory,
                        "*.nupkg",
                        SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();
                if (nestedNupkg != null)
                {
                    using var archive = ZipFile.OpenRead(nestedNupkg);
                    return InspectArchive(archive);
                }

                return InspectFiles(
                    Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                        .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/')),
                    () => Directory.EnumerateFiles(directory, "*.nuspec", SearchOption.TopDirectoryOnly)
                        .Select(XDocument.Load)
                        .FirstOrDefault());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Continue to configured package sources.
            }
        }

        return null;
    }

    private static PackageFrameworkSet InspectArchive(ZipArchive archive)
    {
        return InspectFiles(
            archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')),
            () =>
            {
                var nuspec = archive.Entries.FirstOrDefault(entry =>
                    entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                if (nuspec == null)
                    return null;

                using var stream = nuspec.Open();
                return XDocument.Load(stream);
            });
    }

    private static PackageFrameworkSet InspectFiles(
        IEnumerable<string> fileNames,
        Func<XDocument?> loadNuspec)
    {
        var files = fileNames.ToList();
        var compileFrameworks = FindAssetFrameworks(files, "ref", "lib");
        compileFrameworks.UnionWith(FindRuntimeLibFrameworks(files));
        if (compileFrameworks.Count > 0)
            return new PackageFrameworkSet(false, compileFrameworks.Order(StringComparer.Ordinal).ToList());

        var nuspec = loadNuspec();
        if (HasFrameworkAgnosticDependencies(nuspec))
            return new PackageFrameworkSet(true, []);

        var dependencyFrameworks = FindDependencyFrameworks(nuspec);
        if (dependencyFrameworks.Count > 0)
            return new PackageFrameworkSet(false, dependencyFrameworks.Order(StringComparer.Ordinal).ToList());

        var buildFrameworks = FindAssetFrameworks(
            files,
            "build",
            "buildTransitive",
            "buildMultiTargeting",
            "tools");
        return buildFrameworks.Count > 0
            ? new PackageFrameworkSet(false, buildFrameworks.Order(StringComparer.Ordinal).ToList())
            : new PackageFrameworkSet(true, []);
    }

    private static HashSet<string> FindAssetFrameworks(
        IEnumerable<string> files,
        params string[] roots)
    {
        var rootSet = roots.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var segments = file.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3 && rootSet.Contains(segments[0]))
                result.Add(segments[1]);
        }

        return result;
    }

    private static HashSet<string> FindRuntimeLibFrameworks(IEnumerable<string> files)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var segments = file.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 5 &&
                segments[0].Equals("runtimes", StringComparison.OrdinalIgnoreCase) &&
                segments[2].Equals("lib", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(segments[3]);
            }
        }

        return result;
    }

    private static HashSet<string> FindDependencyFrameworks(XDocument? nuspec)
    {
        if (nuspec == null)
            return [];

        return nuspec.Descendants()
            .Where(element => element.Name.LocalName == "group")
            .Select(element => element.Attribute("targetFramework")?.Value?.Trim())
            .Where(framework => !string.IsNullOrWhiteSpace(framework))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasFrameworkAgnosticDependencies(XDocument? nuspec)
    {
        if (nuspec == null)
            return false;

        var dependencies = nuspec.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "dependencies");
        if (dependencies == null)
            return false;

        var groups = dependencies.Elements()
            .Where(element => element.Name.LocalName == "group")
            .ToList();
        return groups.Count == 0 || groups.Any(group =>
            string.IsNullOrWhiteSpace(group.Attribute("targetFramework")?.Value));
    }

    private static async Task<IReadOnlyList<Uri>> FindPackageBaseAddressesAsync(
        string output,
        CancellationToken cancellationToken)
    {
        var result = new List<Uri>();
        foreach (var source in FindHttpSources(output))
        {
            try
            {
                using var stream = await HttpClient.GetStreamAsync(source, cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("resources", out var resources))
                    continue;

                foreach (var resource in resources.EnumerateArray())
                {
                    if (!resource.TryGetProperty("@id", out var id) ||
                        !resource.TryGetProperty("@type", out var type) ||
                        !ContainsResourceType(type, "PackageBaseAddress/3.0.0") ||
                        !Uri.TryCreate(id.GetString(), UriKind.Absolute, out var address))
                    {
                        continue;
                    }

                    result.Add(address.AbsoluteUri.EndsWith('/')
                        ? address
                        : new Uri(address.AbsoluteUri + "/"));
                }
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                // Other configured sources may still provide the package.
            }
        }

        return result.Distinct().ToList();
    }

    private static bool ContainsResourceType(JsonElement value, string expected)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Array => value.EnumerateArray().Any(item =>
                item.GetString()?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true),
            _ => false
        };
    }

    private static IReadOnlyList<Uri> FindHttpSources(string output)
    {
        return SplitLines(output)
            .Select(line => line.Trim())
            .Select(line => Uri.TryCreate(line, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri?.Scheme is "http" or "https")
            .Cast<Uri>()
            .Distinct()
            .ToList();
    }

    private static IReadOnlyList<string> FindLocalSources(string output)
    {
        return SplitLines(output)
            .Select(line => line.Trim())
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ParsedFramework? ParseFramework(string value)
    {
        var normalized = value.Trim();
        if (normalized.Equals("any", StringComparison.OrdinalIgnoreCase))
            return new ParsedFramework(FrameworkFamily.Any, new Version(0, 0));

        var canonical = Regex.Match(
            normalized,
            "^\\.(?<family>NETCoreApp|NETStandard|NETFramework),?Version=v?(?<version>\\d+(?:\\.\\d+){1,3})$",
            RegexOptions.IgnoreCase);
        if (canonical.Success && Version.TryParse(canonical.Groups["version"].Value, out var canonicalVersion))
        {
            var family = canonical.Groups["family"].Value.ToUpperInvariant() switch
            {
                "NETCOREAPP" => canonicalVersion.Major >= 5
                    ? FrameworkFamily.ModernDotNet
                    : FrameworkFamily.NetCoreApp,
                "NETSTANDARD" => FrameworkFamily.NetStandard,
                "NETFRAMEWORK" => FrameworkFamily.NetFramework,
                _ => throw new InvalidOperationException("Unknown framework family.")
            };
            return new ParsedFramework(family, canonicalVersion);
        }

        var longAlias = Regex.Match(
            normalized,
            "^\\.?(?<family>NETCoreApp|NETStandard|NETFramework)(?<version>\\d+(?:\\.\\d+){1,3})$",
            RegexOptions.IgnoreCase);
        if (longAlias.Success && Version.TryParse(longAlias.Groups["version"].Value, out var aliasVersion))
        {
            var family = longAlias.Groups["family"].Value.ToUpperInvariant() switch
            {
                "NETCOREAPP" => aliasVersion.Major >= 5
                    ? FrameworkFamily.ModernDotNet
                    : FrameworkFamily.NetCoreApp,
                "NETSTANDARD" => FrameworkFamily.NetStandard,
                "NETFRAMEWORK" => FrameworkFamily.NetFramework,
                _ => throw new InvalidOperationException("Unknown framework family.")
            };
            return new ParsedFramework(family, aliasVersion);
        }

        var standardOrCore = Regex.Match(
            normalized,
            "^(?<family>netstandard|netcoreapp)(?<version>\\d+(?:\\.\\d+){1,3})$",
            RegexOptions.IgnoreCase);
        if (standardOrCore.Success &&
            Version.TryParse(standardOrCore.Groups["version"].Value, out var standardOrCoreVersion))
        {
            return new ParsedFramework(
                standardOrCore.Groups["family"].Value.Equals(
                    "netstandard", StringComparison.OrdinalIgnoreCase)
                    ? FrameworkFamily.NetStandard
                    : FrameworkFamily.NetCoreApp,
                standardOrCoreVersion);
        }

        var modern = Regex.Match(
            normalized,
            "^net(?<version>\\d+\\.\\d+)(?:-(?<platform>[a-z]+)(?<platformVersion>\\d+(?:\\.\\d+){0,3})?)?$",
            RegexOptions.IgnoreCase);
        if (modern.Success &&
            Version.TryParse(modern.Groups["version"].Value, out var modernVersion) &&
            modernVersion.Major >= 5)
        {
            var platformVersion = modern.Groups["platformVersion"].Success &&
                                  Version.TryParse(
                                      modern.Groups["platformVersion"].Value,
                                      out var parsedPlatformVersion)
                ? parsedPlatformVersion
                : null;
            return new ParsedFramework(
                FrameworkFamily.ModernDotNet,
                modernVersion,
                modern.Groups["platform"].Success
                    ? modern.Groups["platform"].Value.ToLowerInvariant()
                    : null,
                platformVersion);
        }

        var netFramework = Regex.Match(normalized, "^net(?<version>\\d{2,4})$", RegexOptions.IgnoreCase);
        if (netFramework.Success)
        {
            var digits = netFramework.Groups["version"].Value;
            var versionComponents = digits.Select(character => character - '0').ToArray();
            return versionComponents.Length switch
            {
                2 => new ParsedFramework(
                    FrameworkFamily.NetFramework,
                    new Version(versionComponents[0], versionComponents[1])),
                3 => new ParsedFramework(
                    FrameworkFamily.NetFramework,
                    new Version(versionComponents[0], versionComponents[1], versionComponents[2])),
                4 => new ParsedFramework(
                    FrameworkFamily.NetFramework,
                    new Version(
                        versionComponents[0],
                        versionComponents[1],
                        versionComponents[2] * 10 + versionComponents[3])),
                _ => null
            };
        }

        return null;
    }

    private static bool IsFrameworkCompatible(
        ParsedFramework project,
        ParsedFramework package)
    {
        if (package.Family == FrameworkFamily.Any)
            return true;

        if (!IsPlatformCompatible(project, package))
            return false;

        if (project.Family == package.Family)
            return project.Version >= package.Version;

        if (package.Family == FrameworkFamily.NetStandard)
        {
            return project.Family switch
            {
                FrameworkFamily.ModernDotNet => package.Version <= new Version(2, 1),
                FrameworkFamily.NetCoreApp when project.Version >= new Version(3, 0) =>
                    package.Version <= new Version(2, 1),
                FrameworkFamily.NetCoreApp when project.Version >= new Version(2, 0) =>
                    package.Version <= new Version(2, 0),
                FrameworkFamily.NetFramework when project.Version >= new Version(4, 6, 1) =>
                    package.Version <= new Version(2, 0),
                FrameworkFamily.NetFramework when project.Version >= new Version(4, 6) =>
                    package.Version <= new Version(1, 3),
                FrameworkFamily.NetFramework when project.Version >= new Version(4, 5, 1) =>
                    package.Version <= new Version(1, 2),
                FrameworkFamily.NetFramework when project.Version >= new Version(4, 5) =>
                    package.Version <= new Version(1, 1),
                _ => false
            };
        }

        return project.Family == FrameworkFamily.ModernDotNet &&
               package.Family == FrameworkFamily.NetCoreApp &&
               package.Version <= new Version(3, 1);
    }

    private static bool IsPlatformCompatible(
        ParsedFramework project,
        ParsedFramework package)
    {
        if (package.Platform == null)
            return true;
        if (project.Platform == null ||
            !project.Platform.Equals(package.Platform, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return package.PlatformVersion == null ||
               project.PlatformVersion != null && project.PlatformVersion >= package.PlatformVersion;
    }

    private static string NormalizeVersion(string version)
    {
        var metadataIndex = version.IndexOf('+');
        return (metadataIndex >= 0 ? version[..metadataIndex] : version).ToLowerInvariant();
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaximumPackageBytes)
                throw new InvalidDataException("Package exceeded compatibility-inspection size limit.");

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return string.IsNullOrEmpty(text)
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CodeMetrics.AI", "1.0"));
        return client;
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
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Version));
        }
    }
}
