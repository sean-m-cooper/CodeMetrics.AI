using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodeMetrics.AI.Probes;

public static class DependencyProbe
{
    public sealed record DependencyCommandResult(
        string Arguments,
        string StandardOutput,
        string StandardError,
        int? ExitCode,
        string? ExceptionType = null,
        string? ExceptionMessage = null)
    {
        public bool Failed => ExitCode != 0 || ExceptionType != null;
    }

    public static async Task<DimensionResult> AnalyzeAsync(
        string solutionPath,
        string solutionDir,
        CancellationToken cancellationToken = default)
    {
        var vulnerable = await RunDotnetListAsync(
            solutionPath, "--vulnerable --include-transitive", cancellationToken);
        var outdated = await RunDotnetListAsync(
            solutionPath, "--outdated", cancellationToken);
        var deprecated = await RunDotnetListAsync(
            solutionPath, "--deprecated", cancellationToken);
        DependencyCommandResult[] commands = [vulnerable, outdated, deprecated];

        bool anyCommandFailed = commands.Any(command => command.Failed);
        IReadOnlyDictionary<OutdatedPackageUpgrade, bool>? frameworkCompatibility = null;
        if (!outdated.Failed)
        {
            var aspireProjects = FindAspireProjectNames(solutionDir);
            var assessableUpgrades = PackageFrameworkCompatibility
                .ParseOutdatedOutput(outdated.StandardOutput)
                .Where(upgrade =>
                    upgrade.Project == null ||
                    !IsAspireProjectSection(upgrade.Project, aspireProjects))
                .ToList();
            frameworkCompatibility = await PackageFrameworkCompatibility.AssessAsync(
                assessableUpgrades,
                outdated.StandardOutput,
                cancellationToken);
        }

        return AnalyzeOutput(
            vulnerable.StandardOutput,
            outdated.StandardOutput,
            deprecated.StandardOutput,
            solutionDir,
            anyCommandFailed,
            commands,
            frameworkCompatibility);
    }

    // ── Output processor (public for testability) ─────────────────────────────

    public static DimensionResult AnalyzeOutput(
        string vulnerableOutput,
        string outdatedOutput,
        string deprecatedOutput,
        string solutionDir,
        bool anyCommandFailed,
        IReadOnlyList<DependencyCommandResult>? commandResults = null,
        IReadOnlyDictionary<OutdatedPackageUpgrade, bool>? frameworkCompatibility = null)
    {
        if (anyCommandFailed)
            return CreateFailureResult(commandResults);

        // Runtime commands always request JSON. Keep text support for callers replaying old reports.
        try
        {
            foreach (var output in new[] { vulnerableOutput, outdatedOutput, deprecatedOutput }.Where(PackageReport.IsJson))
                PackageReport.Parse(output);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return CreateFailureResult([new DependencyCommandResult("parse package report", "", "", null,
                nameof(JsonException), exception.Message)]);
        }

        var findings = new List<Finding>();
        var (vulnerableDirect, vulnerableTransitive) = AnalyzeVulnerabilities(
            vulnerableOutput,
            findings);
        var aspireProjects = FindAspireProjectNames(solutionDir);
        var outdatedCounts = CountOutdatedPackages(
            outdatedOutput,
            aspireProjects,
            frameworkCompatibility);
        AddOutdatedFindings(outdatedOutput, aspireProjects, frameworkCompatibility, findings);
        var deprecated = CountDeprecatedPackages(deprecatedOutput, findings);
        var (unsupportedTFMs, unsupportedTFMList) = FindUnsupportedTargetFrameworks(solutionDir);
        var cpmEnabled = FindCpm(solutionDir);
        var versionDrift = cpmEnabled ? 0 : FindVersionDrift(solutionDir);
        AddStaticFindings(findings, unsupportedTFMList, versionDrift, cpmEnabled);

        var metrics = new DependencyMetrics(
            vulnerableDirect,
            vulnerableTransitive,
            outdatedCounts,
            aspireProjects,
            deprecated,
            unsupportedTFMs,
            versionDrift,
            cpmEnabled);
        return CreateSuccessResult(metrics, findings);
    }

    private static DimensionResult CreateFailureResult(
        IReadOnlyList<DependencyCommandResult>? commandResults)
    {
        var failedCommands = commandResults?.Where(command => command.Failed).ToList() ?? [];
        var diagnostic = failedCommands.Count == 0
            ? "One or more dotnet list package commands failed; command diagnostics were unavailable."
            : string.Join("; ", failedCommands.Select(FormatFailure));

        return new DimensionResult
        {
            Status = "failed",
            Basis = $"Dependency probe failed. {diagnostic}",
            Findings =
            [
                new Finding
                {
                    Category = "dependencyProbeFailure",
                    Severity = "error",
                    Confidence = "high",
                    Message = diagnostic
                }
            ],
            Extra =
            {
                ["dependencyMetrics"] = new { anyCommandFailed = true },
                ["dependencyCommands"] = BuildCommandDiagnostics(commandResults)
            }
        };
    }

    private static (int Direct, int Transitive) AnalyzeVulnerabilities(
        string output,
        ICollection<Finding> findings)
    {
        if (PackageReport.IsJson(output))
        {
            var packages = PackageReport.Parse(output);
            foreach (var package in packages)
            {
                findings.Add(new Finding
                {
                    Category = package.Transitive ? "vulnerableTransitiveDependency" : "vulnerableDirectDependency",
                    Severity = package.Transitive ? "warning" : "error",
                    Confidence = "high",
                    Project = package.Project,
                    Package = package.Package,
                    Message = $"{(package.Transitive ? "Transitive" : "Direct")} dependency '{package.Package}' {package.ResolvedVersion} ({package.TargetFramework}) has known vulnerabilities; see advisory details.",
                    Observations = package.Observations()
                });
            }
            return (packages.Count(package => !package.Transitive), packages.Count(package => package.Transitive));
        }

        var direct = 0;
        var transitive = 0;
        var inTransitiveSection = false;

        foreach (var line in SplitLines(output))
        {
            var trimmed = line.Trim();
            if (IsTransitiveHeader(trimmed))
                inTransitiveSection = true;
            else if (IsDirectHeader(trimmed))
                inTransitiveSection = false;

            if (!trimmed.StartsWith('>'))
                continue;

            var packageName = ExtractPackageName(trimmed);
            if (inTransitiveSection)
            {
                transitive++;
                findings.Add(CreateVulnerabilityFinding(packageName, isTransitive: true));
            }
            else
            {
                direct++;
                findings.Add(CreateVulnerabilityFinding(packageName, isTransitive: false));
            }
        }

        return (direct, transitive);
    }

    private static bool IsTransitiveHeader(string line)
    {
        return !line.StartsWith('>') &&
               line.Contains("Transitive", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirectHeader(string line)
    {
        return line.Contains("Top-level Package", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Direct Package", StringComparison.OrdinalIgnoreCase);
    }

    private static Finding CreateVulnerabilityFinding(string packageName, bool isTransitive)
    {
        return new Finding
        {
            Category = isTransitive
                ? "vulnerableTransitiveDependency"
                : "vulnerableDirectDependency",
            Severity = isTransitive ? "warning" : "error",
            Package = packageName,
            Message = isTransitive
                ? $"Transitive dependency '{packageName}' has a known vulnerability."
                : $"Direct dependency '{packageName}' has a known vulnerability."
        };
    }

    private static int CountDeprecatedPackages(
        string output,
        ICollection<Finding> findings)
    {
        if (PackageReport.IsJson(output))
        {
            var packages = PackageReport.Parse(output);
            foreach (var package in packages)
            {
                findings.Add(new Finding
                {
                    Category = "deprecatedDependency",
                    Severity = "warning",
                    Confidence = "high",
                    Project = package.Project,
                    Package = package.Package,
                    Message = $"Package '{package.Package}' {package.ResolvedVersion} ({package.TargetFramework}) is deprecated; review the reported reasons and alternative package when provided.",
                    Observations = package.Observations()
                });
            }
            return packages.Count;
        }

        var packagesFromText = SplitLines(output).Where(line => line.TrimStart().StartsWith('>')).Select(ExtractPackageName).ToList();
        foreach (var package in packagesFromText)
        {
            findings.Add(new Finding
            {
                Category = "deprecatedDependency",
                Severity = "warning",
                Package = package,
                Message = $"Package '{package}' is deprecated (legacy text report)."
            });
        }

        return packagesFromText.Count;
    }

    private static void AddOutdatedFindings(string output, IReadOnlySet<string> aspireProjects,
        IReadOnlyDictionary<OutdatedPackageUpgrade, bool>? compatibility, ICollection<Finding> findings)
    {
        if (!PackageReport.IsJson(output)) return;
        foreach (var package in PackageReport.Parse(output))
        {
            var aspire = IsAspireProjectSection(package.Project, aspireProjects);
            var assessed = compatibility != null && compatibility.TryGetValue(package.Upgrade, out _);
            var incompatible = assessed && !compatibility![package.Upgrade];
            var disposition = aspire ? "excludedAspire" : incompatible ? "excludedFrameworkIncompatible" : "included";
            var observations = package.Observations();
            observations["scoreDisposition"] = disposition;
            observations["frameworkCompatibility"] = aspire || !assessed ? "unknown" : incompatible ? "incompatible" : "compatible";
            findings.Add(new Finding
            {
                Category = "outdatedDependency",
                Severity = disposition == "included" ? "warning" : "info",
                Confidence = "high",
                Project = package.Project,
                Package = package.Package,
                Message = $"Package '{package.Package}' {package.ResolvedVersion} ({package.TargetFramework}) has latest version {package.LatestVersion}. " +
                    (aspire ? "Excluded from outdated scoring by Aspire policy." : incompatible ? "Latest version is incompatible with this target framework; excluded from scoring." :
                        assessed ? "Latest version has compatible framework assets; review breaking changes before upgrading." : "Framework compatibility is unknown; review before upgrading."),
                Observations = observations
            });
        }
    }

    private static void AddStaticFindings(
        ICollection<Finding> findings,
        IEnumerable<string> unsupportedFrameworks,
        int versionDrift,
        bool cpmEnabled)
    {
        foreach (var framework in unsupportedFrameworks)
        {
            findings.Add(new Finding
            {
                Category = "unsupportedTargetFramework",
                Severity = "warning",
                Message = $"Project targets unsupported framework '{framework}'."
            });
        }

        if (versionDrift > 0)
        {
            findings.Add(new Finding
            {
                Category = "versionDrift",
                Severity = "warning",
                Message = $"{versionDrift} package(s) have inconsistent versions across projects."
            });
        }

        if (!cpmEnabled)
        {
            findings.Add(new Finding
            {
                Category = "noCentralPackageManagement",
                Severity = "info",
                Message = "Directory.Packages.props not found — central package management (CPM) is not enabled."
            });
        }
    }

    private static DimensionResult CreateSuccessResult(
        DependencyMetrics metrics,
        List<Finding> findings)
    {
        var outdated = metrics.OutdatedCounts.Included;
        var frameworkIncompatible = metrics.OutdatedCounts.FrameworkIncompatible;
        var compatibilityUnknown = metrics.OutdatedCounts.CompatibilityUnknown;
        var basis = $"vulnerableDirect={metrics.VulnerableDirect}, " +
                    $"vulnerableTransitive={metrics.VulnerableTransitive}, " +
                    $"outdated={outdated}, outdatedAspireExcluded={metrics.OutdatedCounts.AspireExcluded}, " +
                    $"outdatedFrameworkIncompatibleExcluded={frameworkIncompatible.Count}, " +
                    $"outdatedFrameworkCompatibilityUnknown={compatibilityUnknown.Count}, " +
                    $"deprecated={metrics.Deprecated}, unsupportedTFMs={metrics.UnsupportedTfms}, " +
                    $"versionDrift={metrics.VersionDrift}, cpmEnabled={metrics.CpmEnabled}, " +
                    "anyCommandFailed=False.";

        return new DimensionResult
        {
            Status = "scored",
            Score = CalculateScore(metrics),
            Basis = basis,
            Findings = findings,
            Extra =
            {
                ["dependencyMetrics"] = new
                {
                    countUnit = "package occurrences per project and target framework; not unique package IDs",
                    vulnerableDirect = metrics.VulnerableDirect,
                    vulnerableTransitive = metrics.VulnerableTransitive,
                    outdated,
                    outdatedAspireExcluded = metrics.OutdatedCounts.AspireExcluded,
                    outdatedFrameworkIncompatibleExcluded = frameworkIncompatible.Count,
                    outdatedFrameworkCompatibilityUnknown = compatibilityUnknown.Count,
                    frameworkIncompatibleUpgradesExcluded = CreateUpgradeEvidence(frameworkIncompatible),
                    frameworkCompatibilityUnknown = CreateUpgradeEvidence(compatibilityUnknown),
                    aspireProjectsExcludedFromOutdated = metrics.AspireProjects
                        .OrderBy(project => project, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    deprecated = metrics.Deprecated,
                    unsupportedTFMs = metrics.UnsupportedTfms,
                    versionDrift = metrics.VersionDrift,
                    cpmEnabled = metrics.CpmEnabled,
                    anyCommandFailed = false
                }
            }
        };
    }

    private static object[] CreateUpgradeEvidence(IEnumerable<OutdatedPackageUpgrade> upgrades)
    {
        return upgrades
            .Select(upgrade => (object)new
            {
                upgrade.Project,
                upgrade.TargetFramework,
                upgrade.Package,
                upgrade.LatestVersion
            })
            .ToArray();
    }

    private static double CalculateScore(DependencyMetrics metrics)
    {
        if (metrics.VulnerableDirect > 0)
            return 0;
        if (metrics.VulnerableTransitive > 0 || metrics.UnsupportedTfms > 1)
            return 2;
        if (metrics.Deprecated > 0 ||
            metrics.VersionDrift > 2 ||
            metrics.OutdatedCounts.Included > 10)
        {
            return 4;
        }

        if (metrics.OutdatedCounts.Included > 5 || metrics.UnsupportedTfms == 1)
            return 6;
        return metrics.CpmEnabled &&
               metrics.VersionDrift == 0 &&
               metrics.OutdatedCounts.Included == 0
            ? 10
            : 8;
    }

    private sealed record DependencyMetrics(
        int VulnerableDirect,
        int VulnerableTransitive,
        OutdatedPackageCounts OutdatedCounts,
        IReadOnlySet<string> AspireProjects,
        int Deprecated,
        int UnsupportedTfms,
        int VersionDrift,
        bool CpmEnabled);

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<DependencyCommandResult> RunDotnetListAsync(
        string solutionPath, string args, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"list \"{solutionPath}\" package {args} --format json --output-version 1")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new DependencyCommandResult(
                    args, string.Empty, string.Empty, null,
                    nameof(InvalidOperationException), "dotnet process could not be started.");
            }

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            var exited = process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr, exited);

            var output = await stdout;
            if (process.ExitCode == 0)
                PackageReport.Parse(output); // Never turn missing or malformed output into a clean score.
            return new DependencyCommandResult(
                args, output, await stderr, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new DependencyCommandResult(
                args, string.Empty, string.Empty, null,
                ex.GetType().Name, ex.Message);
        }
    }

    private static string FormatFailure(DependencyCommandResult command)
    {
        var outcome = command.ExitCode is { } exitCode
            ? $"exit code {exitCode}"
            : command.ExceptionType ?? "unknown failure";
        var detail = FirstDiagnosticLine(command.StandardError)
                     ?? FirstDiagnosticLine(command.ExceptionMessage);
        return detail == null
            ? $"dotnet list package {command.Arguments}: {outcome}"
            : $"dotnet list package {command.Arguments}: {outcome}: {detail}";
    }

    private static object[] BuildCommandDiagnostics(
        IReadOnlyList<DependencyCommandResult>? commandResults)
    {
        return commandResults?
            .Select(command => (object)new
            {
                arguments = command.Arguments,
                command.ExitCode,
                failed = command.Failed,
                stderr = FirstDiagnosticLine(command.StandardError),
                command.ExceptionType,
                exception = FirstDiagnosticLine(command.ExceptionMessage)
            })
            .ToArray() ?? [];
    }

    private static string? FirstDiagnosticLine(string? value)
    {
        var line = value?
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(candidate => candidate.Trim())
            .FirstOrDefault(candidate => candidate.Length > 0);
        if (line == null)
            return null;

        const int maxLength = 300;
        return line.Length <= maxLength ? line : line[..maxLength] + "…";
    }

    private static string ExtractPackageName(string line)
    {
        // Lines look like:  > PackageName   1.0.0   1.0.0   ...
        var parts = line.TrimStart('>', ' ').Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : line.Trim();
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private sealed record OutdatedPackageCounts(
        int Included,
        int AspireExcluded,
        IReadOnlyList<OutdatedPackageUpgrade> FrameworkIncompatible,
        IReadOnlyList<OutdatedPackageUpgrade> CompatibilityUnknown);

    private static OutdatedPackageCounts CountOutdatedPackages(
        string output,
        IReadOnlySet<string> aspireProjects,
        IReadOnlyDictionary<OutdatedPackageUpgrade, bool>? frameworkCompatibility)
    {
        var included = 0;
        var aspireExcluded = 0;
        var frameworkIncompatible = new List<OutdatedPackageUpgrade>();
        var compatibilityUnknown = new List<OutdatedPackageUpgrade>();

        foreach (var upgrade in PackageFrameworkCompatibility.ParseOutdatedOutput(output))
        {
            if (upgrade.Project != null &&
                IsAspireProjectSection(upgrade.Project, aspireProjects))
            {
                aspireExcluded++;
                continue;
            }

            if (frameworkCompatibility == null)
            {
                included++;
                continue;
            }

            if (!frameworkCompatibility.TryGetValue(upgrade, out var compatible))
            {
                included++;
                compatibilityUnknown.Add(upgrade);
                continue;
            }

            if (compatible)
                included++;
            else
                frameworkIncompatible.Add(upgrade);
        }

        return new OutdatedPackageCounts(
            included,
            aspireExcluded,
            frameworkIncompatible,
            compatibilityUnknown);
    }

    private static bool IsAspireProjectSection(
        string projectSection,
        IReadOnlySet<string> aspireProjects)
    {
        return aspireProjects.Contains(projectSection) ||
               aspireProjects.Contains(Path.GetFileNameWithoutExtension(projectSection));
    }

    private static HashSet<string> FindAspireProjectNames(string solutionDir)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(solutionDir) || !Directory.Exists(solutionDir))
            return result;

        foreach (var csproj in Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories))
        {
            try
            {
                var document = XDocument.Load(csproj);
                if (!IsAspireAppHost(document))
                    continue;

                result.Add(Path.GetFileNameWithoutExtension(csproj));
                var assemblyName = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")
                    ?.Value.Trim();
                if (!string.IsNullOrEmpty(assemblyName))
                    result.Add(assemblyName);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Skip malformed csproj files; the command output remains scored.
                continue;
            }
        }

        return result;
    }

    private static bool IsAspireAppHost(XDocument document)
    {
        var rootSdk = document.Root?.Attribute("Sdk")?.Value;
        if (rootSdk?.Contains("Aspire.AppHost.Sdk", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        if (document.Descendants().Any(element =>
                element.Name.LocalName == "Sdk" &&
                (element.Attribute("Name")?.Value ?? element.Value)
                .Contains("Aspire.AppHost.Sdk", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return document.Descendants().Any(element =>
            element.Name.LocalName == "PackageReference" &&
            element.Attribute("Include")?.Value.Equals(
                "Aspire.Hosting.AppHost", StringComparison.OrdinalIgnoreCase) == true);
    }

    // Returns (count, list of TFM strings) for unsupported target frameworks
    private static (int Count, List<string> Tfms) FindUnsupportedTargetFrameworks(string solutionDir)
    {
        if (string.IsNullOrEmpty(solutionDir) || !Directory.Exists(solutionDir))
            return (0, []);

        var csprojFiles = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
        var unsupported = new List<string>();

        foreach (var csproj in csprojFiles)
        {
            try
            {
                var doc = XDocument.Load(csproj);
                var tfmElements = doc.Descendants()
                    .Where(e => e.Name.LocalName == "TargetFramework" ||
                                e.Name.LocalName == "TargetFrameworks");

                foreach (var el in tfmElements)
                {
                    var value = el.Value.Trim();
                    // Handle multi-target: net6.0;net8.0
                    foreach (var tfm in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var t = tfm.Trim();
                        if (IsUnsupportedFramework(t))
                            unsupported.Add(t);
                    }
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Skip malformed csproj
                continue;
            }
        }

        return (unsupported.Count, unsupported);
    }

    private static bool IsUnsupportedFramework(string tfm)
    {
        // netcoreapp* is always unsupported
        if (tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            return true;

        // net<N>.0 where N < 8 (e.g. net6.0, net7.0)
        var match = Regex.Match(tfm, @"^net(\d+)\.0$", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out int version))
            return version < 8;

        return false;
    }

    private static bool FindCpm(string solutionDir)
    {
        if (string.IsNullOrEmpty(solutionDir))
            return false;

        // Walk up the directory tree looking for Directory.Packages.props
        var dir = solutionDir;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Directory.Packages.props")))
                return true;

            var parent = Directory.GetParent(dir)?.FullName;
            if (parent == dir || parent == null)
                break;
            dir = parent;
        }

        return false;
    }

    private static int FindVersionDrift(string solutionDir)
    {
        if (string.IsNullOrEmpty(solutionDir) || !Directory.Exists(solutionDir))
            return 0;

        var csprojFiles = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
        // package name → set of distinct versions
        var packageVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var csproj in csprojFiles)
        {
            try
            {
                var doc = XDocument.Load(csproj);
                var refs = doc.Descendants()
                    .Where(e => e.Name.LocalName == "PackageReference");

                foreach (var r in refs)
                {
                    var name = r.Attribute("Include")?.Value;
                    var version = r.Attribute("Version")?.Value
                                  ?? r.Element(r.Name.Namespace + "Version")?.Value;

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version))
                        continue;

                    if (!packageVersions.TryGetValue(name, out var versions))
                    {
                        versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        packageVersions[name] = versions;
                    }
                    versions.Add(version);
                }
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // Skip malformed csproj
                continue;
            }
        }

        // Count packages with more than 1 distinct version
        return packageVersions.Count(kvp => kvp.Value.Count > 1);
    }
}
