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
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? projectPaths = null,
        SolutionScope? scope = null)
    {
        string? temporaryDirectory = null;
        var packageEntryPoint = solutionPath;
        try
        {
            if (scope?.DisabledPaths.Count > 0)
            {
                temporaryDirectory = Path.Combine(Path.GetTempPath(), "codemetrics-packages-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryDirectory);
                packageEntryPoint = await scope.WriteDependencySolutionAsync(temporaryDirectory, cancellationToken);
            }
            var vulnerable = await RunDotnetListAsync(
                packageEntryPoint, "--vulnerable --include-transitive", cancellationToken, solutionDir);
            var outdated = await RunDotnetListAsync(
                packageEntryPoint, "--outdated", cancellationToken, solutionDir);
            var deprecated = await RunDotnetListAsync(
                packageEntryPoint, "--deprecated", cancellationToken, solutionDir);
            DependencyCommandResult[] commands = [vulnerable, outdated, deprecated];

            bool anyCommandFailed = commands.Any(command => command.Failed);
            PackageCompatibilityAssessment? compatibilityAssessment = null;
            if (!outdated.Failed)
            {
                var aspireProjects = FindAspireProjectNames(solutionDir, projectPaths);
                var assessableUpgrades = PackageFrameworkCompatibility
                    .ParseOutdatedOutput(outdated.StandardOutput)
                    .Where(upgrade =>
                        upgrade.Project == null ||
                        !IsAspireProjectSection(upgrade.Project, aspireProjects))
                    .ToList();
                compatibilityAssessment = await PackageFrameworkCompatibility.AssessAsync(
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
                compatibilityAssessment?.Results,
                projectPaths,
                compatibilityAssessment);
        }
        finally
        {
            if (temporaryDirectory != null) Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    // ── Output processor (public for testability) ─────────────────────────────

    public static DimensionResult AnalyzeOutput(
        string vulnerableOutput,
        string outdatedOutput,
        string deprecatedOutput,
        string solutionDir,
        bool anyCommandFailed,
        IReadOnlyList<DependencyCommandResult>? commandResults = null,
        IReadOnlyDictionary<OutdatedPackageUpgrade, bool>? frameworkCompatibility = null,
        IReadOnlyList<string>? projectPaths = null,
        PackageCompatibilityAssessment? compatibilityAssessment = null)
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
        var aspireProjects = FindAspireProjectNames(solutionDir, projectPaths);
        var outdatedCounts = CountOutdatedPackages(
            outdatedOutput,
            aspireProjects,
            frameworkCompatibility);
        AddOutdatedFindings(outdatedOutput, aspireProjects, frameworkCompatibility, findings);
        var deprecated = CountDeprecatedPackages(deprecatedOutput, findings);
        if (PackageReport.IsJson(vulnerableOutput))
        {
            vulnerableDirect = DependencyFindingPopulation.Count(findings.Where(f => f.Category == "vulnerableDirectDependency"));
            vulnerableTransitive = DependencyFindingPopulation.Count(findings.Where(f => f.Category == "vulnerableTransitiveDependency"));
        }
        if (PackageReport.IsJson(deprecatedOutput))
            deprecated = DependencyFindingPopulation.Count(findings.Where(f => f.Category == "deprecatedDependency"));
        if (PackageReport.IsJson(outdatedOutput))
            outdatedCounts = outdatedCounts with
            {
                Included = DependencyFindingPopulation.Count(findings.Where(f => f.Category == "outdatedDependency" &&
                    f.Observations.GetValueOrDefault("scoreDisposition") as string == "included"))
            };
        var (unsupportedTFMs, unsupportedTFMList) = FindUnsupportedTargetFrameworks(solutionDir, projectPaths);
        var cpmEnabled = FindCpm(solutionDir);
        var versionDrift = cpmEnabled ? 0 : FindVersionDrift(solutionDir, projectPaths);
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
        var result = CreateSuccessResult(metrics, findings);
        DependencyFindingPopulation.Attach(result, solutionDir, null);
        result.Extra["vulnerabilityAssessmentAvailable"] = true;
        if (compatibilityAssessment != null)
            result.Extra["dependencyCompatibility"] = new
            {
                status = compatibilityAssessment.Failures.Count == 0 ? "complete" : "failed",
                compatibilityAssessment.UniquePackageVersions,
                compatibilityAssessment.TotalObservations,
                knownObservations = compatibilityAssessment.Results.Count,
                notApplicableObservations = compatibilityAssessment.NoReportedCandidates.Count,
                compatibilityAssessment.NoReportedCandidates,
                compatibilityAssessment.ElapsedMilliseconds,
                compatibilityAssessment.Failures
            };
        if (outdatedCounts.CompatibilityUnknown.Count == 0) return result;
        return new DimensionResult
        {
            Status = "failed",
            Basis = "Dependency compatibility assessment unavailable; no dependency score is assigned. " + result.Basis,
            Findings = findings,
            Extra = result.Extra
        };
    }

    private static DimensionResult CreateFailureResult(
        IReadOnlyList<DependencyCommandResult>? commandResults)
    {
        var failedCommands = commandResults?.Where(command => command.Failed).ToList() ?? [];
        var diagnostic = failedCommands.Count == 0
            ? "One or more dotnet list package commands failed; command diagnostics were unavailable."
            : string.Join("; ", failedCommands.Select(FormatFailure));

        var verifiedFindings = new List<Finding>();
        var vulnerabilityAvailable = false;
        foreach (var command in commandResults?.Where(command => !command.Failed) ?? [])
        {
            try
            {
                PackageReport.Parse(command.StandardOutput);
                if (command.Arguments.Contains("--vulnerable", StringComparison.Ordinal))
                {
                    AnalyzeVulnerabilities(command.StandardOutput, verifiedFindings);
                    vulnerabilityAvailable = true;
                }
                else if (command.Arguments.Contains("--deprecated", StringComparison.Ordinal))
                    CountDeprecatedPackages(command.StandardOutput, verifiedFindings);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                // Malformed successful reports cannot contribute verified findings.
            }
        }
        var failure = new DimensionResult
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
                ["vulnerabilityAssessmentAvailable"] = vulnerabilityAvailable,
                ["dependencyMetrics"] = new { anyCommandFailed = true },
                ["dependencyCommands"] = BuildCommandDiagnostics(commandResults)
            }
        };
        failure.Findings.AddRange(verifiedFindings);
        return failure;
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
            var assessment = AssessOutdatedPackage(package.Upgrade, aspireProjects, compatibility);
            findings.Add(CreateOutdatedFinding(package, assessment));
        }
    }

    private enum OutdatedAssessment { NoCandidate, Aspire, NotAssessed, Unknown, Compatible, Incompatible }

    private static OutdatedAssessment AssessOutdatedPackage(OutdatedPackageUpgrade upgrade,
        IReadOnlySet<string> aspireProjects, IReadOnlyDictionary<OutdatedPackageUpgrade, bool>? compatibility)
    {
        if (upgrade.Project != null && IsAspireProjectSection(upgrade.Project, aspireProjects))
            return OutdatedAssessment.Aspire;
        if (PackageFrameworkCompatibility.HasNoReportedCandidate(upgrade.LatestVersion))
            return OutdatedAssessment.NoCandidate;
        if (compatibility == null)
            return OutdatedAssessment.NotAssessed;
        if (!compatibility.TryGetValue(upgrade, out var compatible))
            return OutdatedAssessment.Unknown;
        return compatible ? OutdatedAssessment.Compatible : OutdatedAssessment.Incompatible;
    }

    private static Finding CreateOutdatedFinding(PackageReport.PackageRow package, OutdatedAssessment assessment)
    {
        var (disposition, compatibility, explanation) = assessment switch
        {
            OutdatedAssessment.NoCandidate => ("excludedNoReportedCandidate", "notApplicable",
                "NuGet reported no upgrade candidate under the stable-only query. This does not establish that the package is current, supported or safe; prerelease and unlisted packages may require review."),
            OutdatedAssessment.Aspire => ("excludedAspire", "unknown", "Excluded from outdated scoring by Aspire policy."),
            OutdatedAssessment.Incompatible => ("excludedFrameworkIncompatible", "incompatible",
                "Latest version is incompatible with this target framework; excluded from scoring."),
            OutdatedAssessment.Compatible => ("included", "compatible",
                "Latest version has compatible framework assets; review breaking changes before upgrading."),
            OutdatedAssessment.Unknown => ("unavailable", "unknown",
                "Framework compatibility could not be assessed; this is missing evidence, not a confirmed upgrade issue."),
            _ => ("included", "unknown", "Framework compatibility was not assessed in this historical report.")
        };
        var observations = package.Observations();
        observations["scoreDisposition"] = disposition;
        observations["frameworkCompatibility"] = compatibility;
        if (assessment == OutdatedAssessment.NoCandidate) observations["candidateSelection"] = "noReportedStableCandidate";
        return new Finding
        {
            Category = "outdatedDependency",
            Severity = disposition == "included" ? "warning" : "info",
            Confidence = "high",
            Project = package.Project,
            Package = package.Package,
            Message = assessment == OutdatedAssessment.NoCandidate
                ? $"Package '{package.Package}' {package.ResolvedVersion} ({package.TargetFramework}): " + explanation
                : $"Package '{package.Package}' {package.ResolvedVersion} ({package.TargetFramework}) has latest version {package.LatestVersion}. " + explanation,
            Observations = observations
        };
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
        var decision = CalculateDecision(metrics);
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
            Score = decision.FinalScore,
            ScoringDecision = decision,
            Basis = basis,
            Findings = findings,
            Extra =
            {
                ["dependencyMetrics"] = new
                {
                    countUnit = DependencyFindingPopulation.CountingUnit,
                    exclusionCountUnit = "project/target-framework candidate observations",
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

    private static ScoringDecision CalculateDecision(DependencyMetrics metrics)
    {
        return ScoringDecision.FirstMatch("dotnet/dependencyManagement/package-versions-v2", new()
        {
            ["countingUnit"] = DependencyFindingPopulation.CountingUnit,
            ["vulnerableDirect"] = metrics.VulnerableDirect,
            ["vulnerableTransitive"] = metrics.VulnerableTransitive,
            ["deprecated"] = metrics.Deprecated,
            ["versionDrift"] = metrics.VersionDrift,
            ["outdatedIncluded"] = metrics.OutdatedCounts.Included,
            ["unsupportedTfms"] = metrics.UnsupportedTfms,
            ["cpmEnabled"] = metrics.CpmEnabled
        },
        ScoringStep.Rule("directVulnerability", "vulnerableDirect > 0", metrics.VulnerableDirect > 0, 0, "vulnerableDirectDependency"),
        ScoringStep.Rule("transitiveVulnerability", "vulnerableTransitive > 0", metrics.VulnerableTransitive > 0, 2, "vulnerableTransitiveDependency"),
        ScoringStep.Rule("multipleUnsupportedFrameworks", "unsupportedTfms > 1", metrics.UnsupportedTfms > 1, 2, "unsupportedTargetFramework"),
        ScoringStep.Rule("deprecatedPackage", "deprecated > 0", metrics.Deprecated > 0, 4, "deprecatedDependency"),
        ScoringStep.Rule("versionDrift", "versionDrift > 2", metrics.VersionDrift > 2, 4, "versionDrift"),
        ScoringStep.Rule("manyOutdated", "outdatedIncluded > 10", metrics.OutdatedCounts.Included > 10, 4, "outdatedDependency"),
        ScoringStep.Rule("severalOutdated", "outdatedIncluded > 5", metrics.OutdatedCounts.Included > 5, 6, "outdatedDependency"),
        ScoringStep.Rule("unsupportedFramework", "unsupportedTfms == 1", metrics.UnsupportedTfms == 1, 6, "unsupportedTargetFramework"),
        ScoringStep.Rule("cleanManagedDependencies", "cpmEnabled && versionDrift == 0 && outdatedIncluded == 0", metrics.CpmEnabled && metrics.VersionDrift == 0 && metrics.OutdatedCounts.Included == 0, 10),
        ScoringStep.Rule("remainingMaintenance", "otherwise", true, 8, "outdatedDependency", "versionDrift", "noCentralPackageManagement"));
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
        string solutionPath, string args, CancellationToken cancellationToken, string workingDirectory)
    {
        try
        {
            var command = await DependencyCommandRunner.RunAsync(
                solutionPath, args, cancellationToken, workingDirectory);
            return ValidateCommandResult(command);
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

    internal static DependencyCommandResult ValidateCommandResult(DependencyCommandResult command)
    {
        if (command.ExitCode == 0)
            PackageReport.Parse(command.StandardOutput); // Missing or malformed output cannot restore a clean score.
        return command;
    }

    private static string FormatFailure(DependencyCommandResult command)
    {
        var outcome = command.ExitCode is { } exitCode
            ? $"exit code {exitCode}"
            : command.ExceptionType ?? "unknown failure";
        var detail = FirstDiagnosticLine(command.StandardError)
                     ?? (command.Failed ? FirstDiagnosticLine(command.StandardOutput) : null)
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
                stdout = command.Failed ? FirstDiagnosticLine(command.StandardOutput) : null,
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
            switch (AssessOutdatedPackage(upgrade, aspireProjects, frameworkCompatibility))
            {
                case OutdatedAssessment.NoCandidate:
                    break;
                case OutdatedAssessment.Aspire:
                    aspireExcluded++;
                    break;
                case OutdatedAssessment.Incompatible:
                    frameworkIncompatible.Add(upgrade);
                    break;
                case OutdatedAssessment.Unknown:
                    compatibilityUnknown.Add(upgrade);
                    break;
                default:
                    // No assessment retains the legacy count without adding an
                    // unknown-upgrade diagnostic; an explicitly missing entry does.
                    included++;
                    break;
            }
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

    private static HashSet<string> FindAspireProjectNames(string solutionDir, IReadOnlyList<string>? projectPaths = null)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(solutionDir) || !Directory.Exists(solutionDir))
            return result;

        foreach (var csproj in projectPaths ?? Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories))
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
    private static (int Count, List<string> Tfms) FindUnsupportedTargetFrameworks(string solutionDir, IReadOnlyList<string>? projectPaths)
    {
        if (string.IsNullOrEmpty(solutionDir) || !Directory.Exists(solutionDir))
            return (0, []);

        var csprojFiles = projectPaths ?? Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
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

        var distinct = unsupported.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(tfm => tfm, StringComparer.Ordinal).ToList();
        return (distinct.Count, distinct);
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

    private static int FindVersionDrift(string solutionDir, IReadOnlyList<string>? projectPaths)
    {
        if (string.IsNullOrEmpty(solutionDir) || !Directory.Exists(solutionDir))
            return 0;

        var csprojFiles = projectPaths ?? Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
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
