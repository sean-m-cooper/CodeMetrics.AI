using System.Text.Json;
using CodeMetrics.AI.Probes;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class DependencyPopulationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dependency-population-" + Guid.NewGuid());
    public DependencyPopulationTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);
    private const string Empty = "{\"version\":1,\"projects\":[]}";

    private static string Report(int projects, int frameworks, bool differentVersions = false) => JsonSerializer.Serialize(new
    {
        version = 1,
        projects = Enumerable.Range(0, projects).Select(p => new
        {
            path = $"Project{p}.csproj",
            frameworks = Enumerable.Range(0, frameworks).Select(t => new
            {
                framework = $"net{8 + t}.0",
                topLevelPackages = new[] { new { id = "Package", resolvedVersion = differentVersions ? $"{t + 1}.0.0" : "1.0.0", latestVersion = "10.0.0" } }
            })
        })
    });

    [Fact]
    public void OutdatedThreshold_UsesPackageVersions_NotProjectFrameworkRows()
    {
        var result = DependencyProbe.AnalyzeOutput(Empty, Report(3, 4), Empty, directory, false);
        result.Score.Should().Be(8);
        result.Basis.Should().Contain("outdated=1,");
        result.Findings.Count(f => f.Category == "outdatedDependency").Should().Be(12);
    }

    [Fact]
    public void DifferentResolvedVersions_CountSeparately_WhileRetainingAdvisories()
    {
        var result = DependencyProbe.AnalyzeOutput(Report(2, 3, true), Empty, Report(2, 3, true), directory, false);
        result.Basis.Should().Contain("vulnerableDirect=3,").And.Contain("deprecated=3,");
        result.Findings.Count(f => f.Category == "vulnerableDirectDependency").Should().Be(6);
    }

    [Fact]
    public void MixedFrameworkCompatibility_ScoresCompatibleVersionOnce_AndKeepsUnknownBlocking()
    {
        var report = Report(1, 3);
        var upgrades = PackageFrameworkCompatibility.ParseOutdatedOutput(report);
        var compatibility = new Dictionary<OutdatedPackageUpgrade, bool> { [upgrades[0]] = true, [upgrades[1]] = false };
        var result = DependencyProbe.AnalyzeOutput(Empty, report, Empty, directory, false, frameworkCompatibility: compatibility);
        result.Status.Should().Be("failed");
        result.Score.Should().BeNull();
        result.Basis.Should().Contain("outdated=1,").And.Contain("outdatedFrameworkIncompatibleExcluded=1,")
            .And.Contain("outdatedFrameworkCompatibilityUnknown=1,");
        result.Findings.Count(f => f.Category == "outdatedDependency").Should().Be(3);
    }

    [Fact]
    public void ScopeSeparatesSecurityImports_ButDevelopmentPackagesStillAffectDependencyManagement()
    {
        var result = DependencyProbe.AnalyzeOutput(Report(2, 3), Empty, Empty, directory, false);
        var scopes = new Dictionary<string, string>(SolutionScope.PathComparer)
        {
            [Path.Combine(directory, "Project0.csproj")] = "test",
            [Path.Combine(directory, "Project1.csproj")] = "benchmark"
        };
        DependencyFindingPopulation.Attach(result, directory, scopes);
        result.Score.Should().Be(0);
        var vulnerabilities = result.Findings.Where(DependencyFindingPopulation.IsVulnerability).ToArray();
        vulnerabilities.Should().HaveCount(6);
        vulnerabilities.Should().OnlyContain(f => !DependencyFindingPopulation.IsSecurityInput(f));
        DependencyFindingPopulation.Count(vulnerabilities.Where(DependencyFindingPopulation.IsSecurityInput)).Should().Be(0);
        vulnerabilities.Select(f => f.Observations["dependencyScope"]).Distinct().Should().BeEquivalentTo(["test", "benchmark"]);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("unknown")]
    [InlineData("unrecognized")]
    public void ProductionOrUnknownObservation_KeepsMixedScopeVulnerabilityInSecurity(string scope)
    {
        var result = DependencyProbe.AnalyzeOutput(Report(2, 3), Empty, Empty, directory, false);
        var scopes = new Dictionary<string, string>(SolutionScope.PathComparer)
        {
            [Path.Combine(directory, "Project0.csproj")] = "test",
            [Path.Combine(directory, "Project1.csproj")] = scope
        };
        DependencyFindingPopulation.Attach(result, directory, scopes);
        DependencyFindingPopulation.Count(result.Findings.Where(DependencyFindingPopulation.IsSecurityInput)).Should().Be(1);
    }

    [Fact]
    public void UnmappedProject_AndMissingPackageIdentity_AreConservative()
    {
        var result = DependencyProbe.AnalyzeOutput(Report(1, 2), Empty, Empty, directory, false);
        DependencyFindingPopulation.Count(result.Findings.Where(DependencyFindingPopulation.IsSecurityInput)).Should().Be(1);
        var unknowns = new[] { new Finding { Category = "vulnerableDirectDependency", Package = "Unknown", Severity = "error", Message = "Unknown version" },
            new Finding { Category = "vulnerableDirectDependency", Package = "Unknown", Severity = "error", Message = "Unknown version" } };
        DependencyFindingPopulation.Count(unknowns).Should().Be(2);
    }
}
