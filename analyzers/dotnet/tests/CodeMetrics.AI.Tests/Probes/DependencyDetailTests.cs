using System.Text.Json;
using CodeMetrics.AI.Probes;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class DependencyDetailTests : IDisposable
{
    private const string Empty = "{\"version\":1,\"projects\":[]}";
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DependencyDetailTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Directory.Packages.props"), "<Project />");
    }
    public void Dispose() => Directory.Delete(directory, true);

    private static string Report(string rows, string framework = "net9.0", bool transitive = false) => $$"""
        {"version":1,"sources":["https://api.nuget.org/v3/index.json"],"projects":[
          {"path":"src/App.csproj","frameworks":[{"framework":"{{framework}}","{{(transitive ? "transitivePackages" : "topLevelPackages")}}":[{{rows}}]}]}
        ]}
        """;

    [Fact]
    public void DeprecatedPackages_ExposeVersionsReasonsAndAlternativePerOccurrence()
    {
        var report = Report("""
            {"id":"Old.One","requestedVersion":"[1.0.0,2.0.0)","resolvedVersion":"1.2.0","deprecationReasons":["Legacy","CriticalBugs"],"alternativePackage":{"id":"New.One","versionRange":"[3.0.0,)"}},
            {"id":"Old.Two","requestedVersion":"2.0.0","resolvedVersion":"2.0.0","deprecationReasons":["Other"]}
            """);
        var result = DependencyProbe.AnalyzeOutput(Empty, Empty, report, directory, false);
        result.Score.Should().Be(4);
        result.Findings.Should().HaveCount(2);
        var first = result.Findings.Single(finding => finding.Package == "Old.One");
        first.Project.Should().Be("src/App.csproj");
        first.Observations["targetFramework"].Should().Be("net9.0");
        first.Observations["resolvedVersion"].Should().Be("1.2.0");
        ((JsonElement)first.Observations["deprecationReasons"]!).GetArrayLength().Should().Be(2);
        ((JsonElement)first.Observations["alternativePackage"]!).GetProperty("id").GetString().Should().Be("New.One");
        result.Findings.Single(finding => finding.Package == "Old.Two").Observations["alternativePackage"].Should().BeNull();
    }

    [Fact]
    public void Vulnerabilities_PreserveMultipleAdvisoriesWithoutInflatingPackageCount()
    {
        var report = Report("""
            {"id":"Risky","resolvedVersion":"1.0.0","vulnerabilities":[
              {"severity":"High","advisoryurl":"https://example.test/advisory/1"},
              {"severity":"Moderate","advisoryurl":"https://example.test/advisory/2"}]}
            """, transitive: true);
        var result = DependencyProbe.AnalyzeOutput(report, Empty, Empty, directory, false);
        result.Score.Should().Be(2);
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Category.Should().Be("vulnerableTransitiveDependency");
        ((JsonElement)finding.Observations["vulnerabilities"]!).GetArrayLength().Should().Be(2);
        result.Basis.Should().Contain("vulnerableTransitive=1");
    }

    [Fact]
    public void OutdatedPackages_DistinguishCompatibleIncompatibleAndUnknownCandidates()
    {
        var report = Report("""
            {"id":"Compatible","requestedVersion":"1.0.0","resolvedVersion":"1.0.0","latestVersion":"2.0.0"},
            {"id":"Incompatible","requestedVersion":"9.0.0","resolvedVersion":"9.0.0","latestVersion":"10.0.0"},
            {"id":"Unknown","requestedVersion":"1.0.0","resolvedVersion":"1.0.0","latestVersion":"2.0.0"}
            """);
        var upgrades = PackageFrameworkCompatibility.ParseOutdatedOutput(report);
        var compatibility = new Dictionary<OutdatedPackageUpgrade, bool> { [upgrades[0]] = true, [upgrades[1]] = false };
        var result = DependencyProbe.AnalyzeOutput(Empty, report, Empty, directory, false, frameworkCompatibility: compatibility);
        result.Score.Should().Be(8);
        result.Basis.Should().Contain("outdated=2,").And.Contain("outdatedFrameworkIncompatibleExcluded=1");
        result.Findings.Single(f => f.Package == "Compatible").Observations["frameworkCompatibility"].Should().Be("compatible");
        var excluded = result.Findings.Single(f => f.Package == "Incompatible");
        excluded.Severity.Should().Be("info");
        excluded.Observations["scoreDisposition"].Should().Be("excludedFrameworkIncompatible");
        result.Findings.Single(f => f.Package == "Unknown").Observations["frameworkCompatibility"].Should().Be("unknown");
    }

    [Fact]
    public void SamePackageAcrossFrameworks_RemainsTwoOccurrences()
    {
        var report = Report("""{"id":"Old","resolvedVersion":"1.0.0","deprecationReasons":["Legacy"]}""");
        using var document = JsonDocument.Parse(report);
        var project = document.RootElement.GetProperty("projects")[0];
        var combined = "{\"version\":1,\"projects\":[" + project.GetRawText() + "," + project.GetRawText().Replace("net9.0", "net10.0") + "]}";
        var result = DependencyProbe.AnalyzeOutput(Empty, Empty, combined, directory, false);
        result.Findings.Should().HaveCount(2);
        result.Findings.Select(f => f.Observations["targetFramework"]).Should().BeEquivalentTo(["net9.0", "net10.0"]);
        result.Basis.Should().Contain("deprecated=2");
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"version\":2,\"projects\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"projects\":[{\"path\":\"App.csproj\",\"frameworks\":false}]}")]
    public void InvalidJsonReport_FailsWithoutRestoringScore(string report)
    {
        var result = DependencyProbe.AnalyzeOutput(Empty, report, Empty, directory, false);
        result.Status.Should().Be("failed");
        result.Score.Should().BeNull();
    }

    [Fact]
    public void UpgradeKeys_PreserveProjectFrameworkAndCandidateVersion()
    {
        var upgrades = PackageFrameworkCompatibility.ParseOutdatedOutput(Report("""{"id":"Package","resolvedVersion":"1.0.0","latestVersion":"2.0.0"}"""));
        upgrades.Should().ContainSingle().Which.Should().Be(new OutdatedPackageUpgrade("src/App.csproj", "net9.0", "Package", "2.0.0"));
    }
}
