using CodeMetrics.AI.Probes;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public class DependencyProbeTests
{
    // ── Helper: build empty temp directory ────────────────────────────────────

    private static string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteCsproj(string dir, string filename, string content)
    {
        File.WriteAllText(Path.Combine(dir, filename), content);
    }

    private static string SimpleCsproj(string tfm = "net10.0", string packageName = "Newtonsoft.Json", string version = "13.0.3") =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>{tfm}</TargetFramework></PropertyGroup>
          <ItemGroup><PackageReference Include="{packageName}" Version="{version}" /></ItemGroup>
        </Project>
        """;

    // ── Empty vulnerable/outdated/deprecated outputs ──────────────────────────

    private const string EmptyVulnerableOutput = """
        The following sources were used:
           https://api.nuget.org/v3/index.json

        The given project(s) have no vulnerable packages.
        """;

    private const string EmptyOutdatedOutput = """
        The following sources were used:
           https://api.nuget.org/v3/index.json

        The given project(s) have no outdated packages.
        """;

    private const string EmptyDeprecatedOutput = """
        The following sources were used:
           https://api.nuget.org/v3/index.json

        The given project(s) have no deprecated packages.
        """;

    // ── 1. No issues, no CPM ──────────────────────────────────────────────────

    [Fact]
    public void NoIssues_NoCpm_ScoreIs8()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(8);
            result.Status.Should().Be("scored");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 2. Direct vulnerability → score 0 ────────────────────────────────────

    [Fact]
    public void DirectVulnerability_ScoreIs0()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());

            const string vulnerableOutput = """
                The following sources were used:
                   https://api.nuget.org/v3/index.json

                Project `MyApp` has the following vulnerable packages

                   [net10.0]:
                   Top-level Package                  Requested   Resolved   Severity   Advisory URL
                   > Newtonsoft.Json                  12.0.3      12.0.3     High       https://example.com
                """;

            var result = DependencyProbe.AnalyzeOutput(
                vulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(0);
            result.Findings.Should().Contain(f => f.Category == "vulnerableDirectDependency");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 3. Transitive vulnerability → score 2 ────────────────────────────────

    [Fact]
    public void TransitiveVulnerability_ScoreIs2()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());

            const string vulnerableOutput = """
                The following sources were used:
                   https://api.nuget.org/v3/index.json

                Project `MyApp` has the following vulnerable packages

                   [net10.0]:
                   Transitive Package                  Requested   Resolved   Severity   Advisory URL
                   > SomeTransitivePackage             1.0.0       1.0.0      Medium     https://example.com
                """;

            var result = DependencyProbe.AnalyzeOutput(
                vulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(2);
            result.Findings.Should().Contain(f => f.Category == "vulnerableTransitiveDependency");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 4. Outdated packages > 10 → score 4 ──────────────────────────────────

    [Fact]
    public void OutdatedPackagesMoreThan10_ScoreIs4()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());

            // Build output with 11 outdated entries
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("The following sources were used:");
            lines.AppendLine("   https://api.nuget.org/v3/index.json");
            lines.AppendLine();
            lines.AppendLine("Project `MyApp` has outdated packages");
            lines.AppendLine("   [net10.0]:");
            for (int i = 0; i < 11; i++)
                lines.AppendLine($"   > Package{i}   1.0.{i}   2.0.0");

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, lines.ToString(), EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(4);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 5. Deprecated packages → score 4 ─────────────────────────────────────

    [Fact]
    public void DeprecatedPackages_ScoreIs4()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());

            const string deprecatedOutput = """
                The following sources were used:
                   https://api.nuget.org/v3/index.json

                Project `MyApp` has the following deprecated packages

                   [net10.0]:
                   Top-level Package                  Requested   Resolved   Reason
                   > OldPackage                       1.0.0       1.0.0      Legacy
                """;

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, deprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(4);
            result.Findings.Should().Contain(f => f.Category == "deprecatedDependency");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 6. Clean with CPM → score 10 ─────────────────────────────────────────

    [Fact]
    public void CleanWithCpm_ScoreIs10()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());
            File.WriteAllText(Path.Combine(dir, "Directory.Packages.props"),
                """
                <Project>
                  <ItemGroup>
                    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
                  </ItemGroup>
                </Project>
                """);

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(10);
            result.Findings.Should().NotContain(f => f.Category == "noCentralPackageManagement");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 7. Version drift > 2 → score 4 ───────────────────────────────────────

    [Fact]
    public void VersionDriftMoreThan2_ScoreIs4()
    {
        var dir = TempDir();
        try
        {
            // Create 4 projects each using different versions of 3 packages
            // to produce > 2 drifted packages
            var pkgs = new[] { ("PkgA", "1.0.0"), ("PkgB", "2.0.0"), ("PkgC", "3.0.0") };
            for (int i = 0; i < 3; i++)
            {
                // Each project has a different version suffix for all 3 packages
                var csproj = $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                      <ItemGroup>
                        <PackageReference Include="PkgA" Version="1.0.{i}" />
                        <PackageReference Include="PkgB" Version="2.0.{i}" />
                        <PackageReference Include="PkgC" Version="3.0.{i}" />
                      </ItemGroup>
                    </Project>
                    """;
                WriteCsproj(dir, $"Project{i}.csproj", csproj);
            }

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(4);
            result.Findings.Should().Contain(f => f.Category == "versionDrift");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 8. anyCommandFailed → score 0 ────────────────────────────────────────

    [Fact]
    public void AnyCommandFailed_ScoreIs0()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());

            var result = DependencyProbe.AnalyzeOutput(
                string.Empty, string.Empty, string.Empty,
                dir, anyCommandFailed: true);

            result.Score.Should().Be(0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 9. Unsupported TFM (1) → score 6 ─────────────────────────────────────

    [Fact]
    public void SingleUnsupportedTfm_ScoreIs6()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj(tfm: "net6.0"));

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(6);
            result.Findings.Should().Contain(f => f.Category == "unsupportedTargetFramework");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 10. More than 1 unsupported TFM → score 2 ────────────────────────────

    [Fact]
    public void MultipleUnsupportedTfms_ScoreIs2()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App1.csproj", SimpleCsproj(tfm: "net6.0"));
            WriteCsproj(dir, "App2.csproj", SimpleCsproj(tfm: "netcoreapp3.1"));

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(2);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 11. Outdated > 5 but <= 10 → score 6 ─────────────────────────────────

    [Fact]
    public void OutdatedPackagesBetween6And10_ScoreIs6()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());

            var lines = new System.Text.StringBuilder();
            lines.AppendLine("The following sources were used:");
            lines.AppendLine("   https://api.nuget.org/v3/index.json");
            lines.AppendLine();
            lines.AppendLine("Project `MyApp` has outdated packages");
            lines.AppendLine("   [net10.0]:");
            for (int i = 0; i < 7; i++)
                lines.AppendLine($"   > Package{i}   1.0.{i}   2.0.0");

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, lines.ToString(), EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Score.Should().Be(6);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 12. Extra data populated ──────────────────────────────────────────────

    [Fact]
    public void ExtraData_ContainsDependencyMetrics()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Extra.Should().ContainKey("dependencyMetrics");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 13. Version drift == 0 with CPM in parent dir ────────────────────────

    [Fact]
    public void CpmInParentDir_IsDetected()
    {
        var parentDir = TempDir();
        var childDir = Path.Combine(parentDir, "src");
        Directory.CreateDirectory(childDir);
        try
        {
            WriteCsproj(childDir, "App.csproj", SimpleCsproj());
            File.WriteAllText(Path.Combine(parentDir, "Directory.Packages.props"),
                "<Project><ItemGroup></ItemGroup></Project>");

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                childDir, anyCommandFailed: false);

            result.Findings.Should().NotContain(f => f.Category == "noCentralPackageManagement");
        }
        finally
        {
            Directory.Delete(parentDir, true);
        }
    }

    // ── 14. No noCPM finding when CPM is enabled ─────────────────────────────

    [Fact]
    public void CpmEnabled_NoCpmFindingAbsent()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());
            File.WriteAllText(Path.Combine(dir, "Directory.Packages.props"), "<Project />");

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Findings.Should().NotContain(f => f.Category == "noCentralPackageManagement");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 15. netcoreapp TFM counts as unsupported ─────────────────────────────

    [Fact]
    public void NetCoreAppTfm_CountsAsUnsupported()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj(tfm: "netcoreapp3.1"));

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Findings.Should().Contain(f =>
                f.Category == "unsupportedTargetFramework" &&
                f.Message != null && f.Message.Contains("netcoreapp3.1"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 16. net8.0 and above are supported ───────────────────────────────────

    [Fact]
    public void Net8AndAbove_IsSupported()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj(tfm: "net8.0"));

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Findings.Should().NotContain(f => f.Category == "unsupportedTargetFramework");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── 17. Version drift == 0 when only 1 project ───────────────────────────

    [Fact]
    public void SingleProject_NoVersionDrift()
    {
        var dir = TempDir();
        try
        {
            WriteCsproj(dir, "App.csproj", SimpleCsproj());

            var result = DependencyProbe.AnalyzeOutput(
                EmptyVulnerableOutput, EmptyOutdatedOutput, EmptyDeprecatedOutput,
                dir, anyCommandFailed: false);

            result.Findings.Should().NotContain(f => f.Category == "versionDrift");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
