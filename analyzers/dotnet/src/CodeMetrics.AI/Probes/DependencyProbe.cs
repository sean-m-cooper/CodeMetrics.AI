using System.Diagnostics;
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

    public static async Task<DimensionResult> AnalyzeAsync(string solutionPath, string solutionDir)
    {
        var vulnerable = await RunDotnetListAsync(solutionPath, "--vulnerable --include-transitive");
        var outdated = await RunDotnetListAsync(solutionPath, "--outdated");
        var deprecated = await RunDotnetListAsync(solutionPath, "--deprecated");
        DependencyCommandResult[] commands = [vulnerable, outdated, deprecated];

        bool anyCommandFailed = commands.Any(command => command.Failed);

        return AnalyzeOutput(
            vulnerable.StandardOutput,
            outdated.StandardOutput,
            deprecated.StandardOutput,
            solutionDir,
            anyCommandFailed,
            commands);
    }

    // ── Output processor (public for testability) ─────────────────────────────

    public static DimensionResult AnalyzeOutput(
        string vulnerableOutput,
        string outdatedOutput,
        string deprecatedOutput,
        string solutionDir,
        bool anyCommandFailed,
        IReadOnlyList<DependencyCommandResult>? commandResults = null)
    {
        if (anyCommandFailed)
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

        var findings = new List<Finding>();

        // ── Parse vulnerable output ───────────────────────────────────────────
        int vulnerableDirect = 0;
        int vulnerableTransitive = 0;
        bool inTransitiveSection = false;

        foreach (var line in SplitLines(vulnerableOutput))
        {
            var trimmed = line.Trim();

            if (trimmed.Equals("Transitive Package", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Transitive Package", StringComparison.OrdinalIgnoreCase) ||
                trimmed.IndexOf("Transitive", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !trimmed.StartsWith(">"))
            {
                inTransitiveSection = true;
            }
            else if (trimmed.IndexOf("Top-level Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     trimmed.IndexOf("Direct Package", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                inTransitiveSection = false;
            }

            if (trimmed.StartsWith(">"))
            {
                var packageName = ExtractPackageName(trimmed);
                if (inTransitiveSection)
                {
                    vulnerableTransitive++;
                    findings.Add(new Finding
                    {
                        Category = "vulnerableTransitiveDependency",
                        Severity = "warning",
                        Package = packageName,
                        Message = $"Transitive dependency '{packageName}' has a known vulnerability."
                    });
                }
                else
                {
                    vulnerableDirect++;
                    findings.Add(new Finding
                    {
                        Category = "vulnerableDirectDependency",
                        Severity = "error",
                        Package = packageName,
                        Message = $"Direct dependency '{packageName}' has a known vulnerability."
                    });
                }
            }
        }

        // ── Parse outdated output ─────────────────────────────────────────────
        // Aspire AppHost projects are local orchestration infrastructure. Keep their
        // packages visible to vulnerability/deprecation checks, but do not let routine
        // local-tooling upgrades reduce the production dependency score.
        var aspireProjects = FindAspireProjectNames(solutionDir);
        var (outdated, outdatedAspireExcluded) = CountOutdatedPackages(
            outdatedOutput, aspireProjects);

        // ── Parse deprecated output ───────────────────────────────────────────
        int deprecated = SplitLines(deprecatedOutput).Count(l => l.TrimStart().StartsWith(">"));

        if (deprecated > 0)
        {
            findings.Add(new Finding
            {
                Category = "deprecatedDependency",
                Severity = "warning",
                Message = $"{deprecated} deprecated package(s) found."
            });
        }

        // ── Static checks ─────────────────────────────────────────────────────
        var (unsupportedTFMs, unsupportedTFMList) = FindUnsupportedTargetFrameworks(solutionDir);
        bool cpmEnabled = FindCpm(solutionDir);
        int versionDrift = cpmEnabled ? 0 : FindVersionDrift(solutionDir);

        foreach (var tfm in unsupportedTFMList)
        {
            findings.Add(new Finding
            {
                Category = "unsupportedTargetFramework",
                Severity = "warning",
                Message = $"Project targets unsupported framework '{tfm}'."
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

        // ── Scoring ───────────────────────────────────────────────────────────
        double score;
        if (vulnerableDirect > 0)
            score = 0;
        else if (vulnerableTransitive > 0 || unsupportedTFMs > 1)
            score = 2;
        else if (deprecated > 0 || versionDrift > 2 || outdated > 10)
            score = 4;
        else if (outdated > 5 || unsupportedTFMs == 1)
            score = 6;
        else if (cpmEnabled && versionDrift == 0 && outdated == 0)
            score = 10;
        else
            score = 8;

        var basis = $"vulnerableDirect={vulnerableDirect}, vulnerableTransitive={vulnerableTransitive}, " +
                    $"outdated={outdated}, outdatedAspireExcluded={outdatedAspireExcluded}, " +
                    $"deprecated={deprecated}, unsupportedTFMs={unsupportedTFMs}, " +
                    $"versionDrift={versionDrift}, cpmEnabled={cpmEnabled}, anyCommandFailed={anyCommandFailed}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = score,
            Basis = basis,
            Findings = findings,
            Extra =
            {
                ["dependencyMetrics"] = new
                {
                    vulnerableDirect,
                    vulnerableTransitive,
                    outdated,
                    outdatedAspireExcluded,
                    aspireProjectsExcludedFromOutdated = aspireProjects
                        .OrderBy(project => project, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    deprecated,
                    unsupportedTFMs,
                    versionDrift,
                    cpmEnabled,
                    anyCommandFailed
                }
            }
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<DependencyCommandResult> RunDotnetListAsync(
        string solutionPath, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"list \"{solutionPath}\" package {args}")
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

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr, exited);

            return new DependencyCommandResult(
                args, await stdout, await stderr, process.ExitCode);
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

    private static (int Included, int AspireExcluded) CountOutdatedPackages(
        string output,
        IReadOnlySet<string> aspireProjects)
    {
        var included = 0;
        var aspireExcluded = 0;
        string? currentProject = null;

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
                continue;
            }

            if (!trimmed.StartsWith('>'))
                continue;

            if (currentProject != null && IsAspireProjectSection(currentProject, aspireProjects))
                aspireExcluded++;
            else
                included++;
        }

        return (included, aspireExcluded);
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
            catch
            {
                // Skip malformed csproj files; the command output remains scored.
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
            catch
            {
                // Skip malformed csproj
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
            catch
            {
                // Skip malformed csproj
            }
        }

        // Count packages with more than 1 distinct version
        return packageVersions.Count(kvp => kvp.Value.Count > 1);
    }
}
