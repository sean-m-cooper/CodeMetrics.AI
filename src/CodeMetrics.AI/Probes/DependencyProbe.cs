using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodeMetrics.AI.Probes;

public static class DependencyProbe
{
    public static async Task<DimensionResult> AnalyzeAsync(string solutionPath, string solutionDir)
    {
        var (vulnerableOutput, vulnerableFailed)  = await RunDotnetListAsync(solutionPath, "--vulnerable --include-transitive");
        var (outdatedOutput,   outdatedFailed)    = await RunDotnetListAsync(solutionPath, "--outdated");
        var (deprecatedOutput, deprecatedFailed)  = await RunDotnetListAsync(solutionPath, "--deprecated");

        bool anyCommandFailed = vulnerableFailed || outdatedFailed || deprecatedFailed;

        return AnalyzeOutput(vulnerableOutput, outdatedOutput, deprecatedOutput, solutionDir, anyCommandFailed);
    }

    // ── Output processor (public for testability) ─────────────────────────────

    public static DimensionResult AnalyzeOutput(
        string vulnerableOutput,
        string outdatedOutput,
        string deprecatedOutput,
        string solutionDir,
        bool anyCommandFailed)
    {
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
        int outdated = SplitLines(outdatedOutput).Count(l => l.TrimStart().StartsWith(">"));

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
        if (anyCommandFailed || vulnerableDirect > 0)
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
                    $"outdated={outdated}, deprecated={deprecated}, unsupportedTFMs={unsupportedTFMs}, " +
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

    private static async Task<(string Output, bool Failed)> RunDotnetListAsync(
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
                return (string.Empty, true);

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (output, process.ExitCode != 0);
        }
        catch
        {
            return (string.Empty, true);
        }
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
