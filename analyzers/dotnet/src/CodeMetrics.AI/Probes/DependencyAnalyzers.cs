using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CodeMetrics.AI.Probes;

internal sealed record DotnetListResult(string VulnerableOutput, string OutdatedOutput, string DeprecatedOutput, bool AnyCommandFailed);

internal sealed record DependencyMetrics(
    int VulnerableDirect,
    int VulnerableTransitive,
    int Outdated,
    int Deprecated,
    int UnsupportedTFMs,
    int VersionDrift,
    bool CpmEnabled,
    bool AnyCommandFailed);

internal static class DotnetPackageListRunner
{
    public static async Task<DotnetListResult> RunAsync(
        string solutionPath,
        CancellationToken cancellationToken)
    {
        var vulnerable = await RunDotnetListAsync(
            solutionPath,
            "--vulnerable --include-transitive",
            cancellationToken);
        var outdated = await RunDotnetListAsync(
            solutionPath,
            "--outdated",
            cancellationToken);
        var deprecated = await RunDotnetListAsync(
            solutionPath,
            "--deprecated",
            cancellationToken);

        return new DotnetListResult(
            vulnerable.Output,
            outdated.Output,
            deprecated.Output,
            vulnerable.Failed || outdated.Failed || deprecated.Failed);
    }

    private static async Task<(string Output, bool Failed)> RunDotnetListAsync(
        string solutionPath,
        string args,
        CancellationToken cancellationToken)
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

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return (output, process.ExitCode != 0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"dotnet list failed for '{solutionPath}' with args '{args}': {ex.Message}");
            return (string.Empty, true);
        }
    }
}

internal static class DependencyOutputAnalyzer
{
    public static (DependencyMetrics Metrics, List<Finding> Findings) Analyze(
        string vulnerableOutput,
        string outdatedOutput,
        string deprecatedOutput,
        string solutionDir,
        bool anyCommandFailed)
    {
        var findings = new List<Finding>();
        var vulnerabilityMetrics = VulnerabilityOutputParser.Parse(vulnerableOutput, findings);
        var outdated = DependencyOutputLines.CountPackageRows(outdatedOutput);
        var deprecated = DependencyOutputLines.CountPackageRows(deprecatedOutput);

        AddDeprecatedFinding(deprecated, findings);

        var staticMetrics = DependencyStaticChecks.Analyze(solutionDir, findings);
        var metrics = new DependencyMetrics(
            vulnerabilityMetrics.Direct,
            vulnerabilityMetrics.Transitive,
            outdated,
            deprecated,
            staticMetrics.UnsupportedTFMs,
            staticMetrics.VersionDrift,
            staticMetrics.CpmEnabled,
            anyCommandFailed);

        return (metrics, findings);
    }

    private static void AddDeprecatedFinding(int deprecated, List<Finding> findings)
    {
        if (deprecated <= 0)
            return;

        findings.Add(new Finding
        {
            Category = "deprecatedDependency",
            Severity = "warning",
            Message = $"{deprecated} deprecated package(s) found."
        });
    }
}

internal static class VulnerabilityOutputParser
{
    public static (int Direct, int Transitive) Parse(string output, List<Finding> findings)
    {
        var direct = 0;
        var transitive = 0;
        var inTransitiveSection = false;

        foreach (var line in DependencyOutputLines.Split(output))
        {
            var trimmed = line.Trim();
            inTransitiveSection = SectionState.Update(trimmed, inTransitiveSection);

            if (!trimmed.StartsWith(">"))
                continue;

            var packageName = DependencyOutputLines.ExtractPackageName(trimmed);
            if (inTransitiveSection)
                AddTransitiveFinding(packageName, findings, ref transitive);
            else
                AddDirectFinding(packageName, findings, ref direct);
        }

        return (direct, transitive);
    }

    private static void AddDirectFinding(string packageName, List<Finding> findings, ref int direct)
    {
        direct++;
        findings.Add(new Finding
        {
            Category = "vulnerableDirectDependency",
            Severity = "error",
            Package = packageName,
            Message = $"Direct dependency '{packageName}' has a known vulnerability."
        });
    }

    private static void AddTransitiveFinding(string packageName, List<Finding> findings, ref int transitive)
    {
        transitive++;
        findings.Add(new Finding
        {
            Category = "vulnerableTransitiveDependency",
            Severity = "warning",
            Package = packageName,
            Message = $"Transitive dependency '{packageName}' has a known vulnerability."
        });
    }
}

internal static class SectionState
{
    public static bool Update(string trimmedLine, bool currentTransitiveState)
    {
        if (IsTransitiveHeader(trimmedLine))
            return true;
        if (IsDirectHeader(trimmedLine))
            return false;
        return currentTransitiveState;
    }

    private static bool IsTransitiveHeader(string line)
    {
        return line.Equals("Transitive Package", StringComparison.OrdinalIgnoreCase) ||
               line.StartsWith("Transitive Package", StringComparison.OrdinalIgnoreCase) ||
               line.IndexOf("Transitive", StringComparison.OrdinalIgnoreCase) >= 0 &&
               !line.StartsWith(">");
    }

    private static bool IsDirectHeader(string line)
    {
        return line.IndexOf("Top-level Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
               line.IndexOf("Direct Package", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal static class DependencyOutputLines
{
    public static int CountPackageRows(string text)
    {
        return Split(text).Count(line => line.TrimStart().StartsWith(">"));
    }

    public static IEnumerable<string> Split(string text)
    {
        return string.IsNullOrEmpty(text)
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    public static string ExtractPackageName(string line)
    {
        var parts = line.TrimStart('>', ' ').Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : line.Trim();
    }
}

internal sealed record DependencyStaticMetrics(int UnsupportedTFMs, int VersionDrift, bool CpmEnabled);

internal static class DependencyStaticChecks
{
    public static DependencyStaticMetrics Analyze(string solutionDir, List<Finding> findings)
    {
        var unsupported = TargetFrameworkScanner.FindUnsupportedTargetFrameworks(solutionDir);
        foreach (var tfm in unsupported)
            AddUnsupportedFrameworkFinding(tfm, findings);

        var cpmEnabled = CentralPackageManagementFinder.Find(solutionDir);
        var versionDrift = cpmEnabled ? 0 : VersionDriftScanner.Count(solutionDir);

        AddVersionDriftFinding(versionDrift, findings);
        AddCpmFinding(cpmEnabled, findings);

        return new DependencyStaticMetrics(unsupported.Count, versionDrift, cpmEnabled);
    }

    private static void AddUnsupportedFrameworkFinding(string tfm, List<Finding> findings)
    {
        findings.Add(new Finding
        {
            Category = "unsupportedTargetFramework",
            Severity = "warning",
            Message = $"Project targets unsupported framework '{tfm}'."
        });
    }

    private static void AddVersionDriftFinding(int versionDrift, List<Finding> findings)
    {
        if (versionDrift <= 0)
            return;

        findings.Add(new Finding
        {
            Category = "versionDrift",
            Severity = "warning",
            Message = $"{versionDrift} package(s) have inconsistent versions across projects."
        });
    }

    private static void AddCpmFinding(bool cpmEnabled, List<Finding> findings)
    {
        if (cpmEnabled)
            return;

        findings.Add(new Finding
        {
            Category = "noCentralPackageManagement",
            Severity = "info",
            Message = "Directory.Packages.props not found - central package management (CPM) is not enabled."
        });
    }
}

internal static class TargetFrameworkScanner
{
    public static List<string> FindUnsupportedTargetFrameworks(string solutionDir)
    {
        var unsupported = new List<string>();
        if (string.IsNullOrEmpty(solutionDir) || !Directory.Exists(solutionDir))
            return unsupported;

        foreach (var csproj in Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories))
            AddUnsupportedFrameworks(csproj, unsupported);

        return unsupported;
    }

    private static void AddUnsupportedFrameworks(string csproj, List<string> unsupported)
    {
        try
        {
            foreach (var framework in ProjectFileReader.ReadTargetFrameworks(csproj))
            {
                if (TargetFrameworkSupport.IsUnsupported(framework))
                    unsupported.Add(framework);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Skipping dependency scan for '{csproj}': {ex.Message}");
        }
    }
}

internal static class TargetFrameworkSupport
{
    public static bool IsUnsupported(string tfm)
    {
        if (tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            return true;

        var match = Regex.Match(tfm, @"^net(\d+)\.0$", RegexOptions.IgnoreCase);
        return match.Success
            && int.TryParse(match.Groups[1].Value, out var version)
            && version < 8;
    }
}

internal static class CentralPackageManagementFinder
{
    public static bool Find(string solutionDir)
    {
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
}

internal static class VersionDriftScanner
{
    public static int Count(string solutionDir)
    {
        if (string.IsNullOrEmpty(solutionDir) || !Directory.Exists(solutionDir))
            return 0;

        var packageVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var csproj in Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories))
            AddPackageVersions(csproj, packageVersions);

        return packageVersions.Count(kvp => kvp.Value.Count > 1);
    }

    private static void AddPackageVersions(
        string csproj,
        Dictionary<string, HashSet<string>> packageVersions)
    {
        try
        {
            foreach (var reference in ProjectFileReader.ReadPackageReferences(csproj))
                AddPackageVersion(reference, packageVersions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Skipping dependency scan for '{csproj}': {ex.Message}");
        }
    }

    private static void AddPackageVersion(
        PackageReferenceInfo reference,
        Dictionary<string, HashSet<string>> packageVersions)
    {
        if (!packageVersions.TryGetValue(reference.Name, out var versions))
        {
            versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            packageVersions[reference.Name] = versions;
        }

        versions.Add(reference.Version);
    }
}

internal sealed record PackageReferenceInfo(string Name, string Version);

internal static class ProjectFileReader
{
    public static IEnumerable<string> ReadTargetFrameworks(string csproj)
    {
        var doc = XDocument.Load(csproj);
        return doc.Descendants()
            .Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .SelectMany(e => e.Value.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(tfm => tfm.Trim());
    }

    public static IEnumerable<PackageReferenceInfo> ReadPackageReferences(string csproj)
    {
        var doc = XDocument.Load(csproj);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(ReadPackageReference)
            .Where(reference => reference != null)!;
    }

    private static PackageReferenceInfo? ReadPackageReference(XElement element)
    {
        var name = element.Attribute("Include")?.Value;
        var version = element.Attribute("Version")?.Value
                      ?? element.Element(element.Name.Namespace + "Version")?.Value;

        return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(version)
            ? null
            : new PackageReferenceInfo(name, version);
    }
}

internal static class DependencyResultBuilder
{
    public static DimensionResult Build(DependencyMetrics metrics, List<Finding> findings)
    {
        return new DimensionResult
        {
            Status = "scored",
            Score = Score(metrics),
            Basis = Basis(metrics),
            Findings = findings,
            Extra =
            {
                ["dependencyMetrics"] = new
                {
                    vulnerableDirect = metrics.VulnerableDirect,
                    vulnerableTransitive = metrics.VulnerableTransitive,
                    outdated = metrics.Outdated,
                    deprecated = metrics.Deprecated,
                    unsupportedTFMs = metrics.UnsupportedTFMs,
                    versionDrift = metrics.VersionDrift,
                    cpmEnabled = metrics.CpmEnabled,
                    anyCommandFailed = metrics.AnyCommandFailed
                }
            }
        };
    }

    private static double Score(DependencyMetrics metrics)
    {
        if (metrics.AnyCommandFailed || metrics.VulnerableDirect > 0)
            return 0;
        if (metrics.VulnerableTransitive > 0 || metrics.UnsupportedTFMs > 1)
            return 2;
        if (metrics.Deprecated > 0 || metrics.VersionDrift > 2 || metrics.Outdated > 10)
            return 4;
        if (metrics.Outdated > 5 || metrics.UnsupportedTFMs == 1)
            return 6;
        if (metrics.CpmEnabled && metrics.VersionDrift == 0 && metrics.Outdated == 0)
            return 10;
        return 8;
    }

    private static string Basis(DependencyMetrics metrics)
    {
        return $"vulnerableDirect={metrics.VulnerableDirect}, vulnerableTransitive={metrics.VulnerableTransitive}, " +
               $"outdated={metrics.Outdated}, deprecated={metrics.Deprecated}, unsupportedTFMs={metrics.UnsupportedTFMs}, " +
               $"versionDrift={metrics.VersionDrift}, cpmEnabled={metrics.CpmEnabled}, anyCommandFailed={metrics.AnyCommandFailed}.";
    }
}
