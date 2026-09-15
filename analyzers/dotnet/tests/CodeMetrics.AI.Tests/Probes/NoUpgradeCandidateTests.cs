using System.Text.Json;
using CodeMetrics.AI.Probes;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class NoUpgradeCandidateTests
{
    [Theory]
    [InlineData("4.1.2")]
    [InlineData("4.8.0-beta00018")]
    public async Task NoReportedCandidate_IsVisibleAndNotCompatibilityFailure(string resolved)
    {
        var output = Report(resolved, "Not found at the sources");
        var assessment = await PackageFrameworkCompatibility.AssessAsync(PackageFrameworkCompatibility.ParseOutdatedOutput(output), output, TestContext.Current.CancellationToken);
        assessment.Results.Should().BeEmpty();
        assessment.Failures.Should().BeEmpty();
        assessment.TotalObservations.Should().Be(1);
        assessment.NoReportedCandidates.Should().ContainSingle();
        var result = DependencyProbe.AnalyzeOutput(Empty, output, Empty, Path.GetTempPath(), false,
            frameworkCompatibility: assessment.Results, projectPaths: [], compatibilityAssessment: assessment);
        result.Status.Should().Be("scored");
        var finding = result.Findings.Single(f => f.Category == "outdatedDependency");
        finding.Severity.Should().Be("info");
        finding.Observations["scoreDisposition"].Should().Be("excludedNoReportedCandidate");
        finding.Observations["frameworkCompatibility"].Should().Be("notApplicable");
        result.ScoringDecision!.Inputs["outdatedIncluded"].Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("Not found at the sources due to an error")]
    public async Task MalformedCandidate_RemainsUnavailable(string latest)
    {
        var output = Report("1.0.0", latest);
        var result = await PackageFrameworkCompatibility.AssessAsync(PackageFrameworkCompatibility.ParseOutdatedOutput(output), output, TestContext.Current.CancellationToken);
        result.Failures.Should().ContainSingle();
        result.NoReportedCandidates.Should().BeEmpty();
    }

    [Fact]
    public void LegacyText_PreservesWholeNoCandidateSentinel()
    {
        PackageFrameworkCompatibility.ParseOutdatedOutput("Project 'App' has updates\n[net10.0]:\n> Pkg 1.0.0 1.0.0 Not found at the sources")
            .Should().ContainSingle().Which.LatestVersion.Should().Be(PackageFrameworkCompatibility.NoCandidateText);
    }

    [Fact]
    public async Task NoCandidate_DoesNotMaskUnknownCandidates()
    {
        var result = await PackageFrameworkCompatibility.AssessAsync(
            [new("App", "net10.0", "NoCandidate", PackageFrameworkCompatibility.NoCandidateText), new("App", "net10.0", "Broken", "bad")],
            Empty, TestContext.Current.CancellationToken);
        result.NoReportedCandidates.Should().ContainSingle();
        result.Failures.Should().ContainSingle().Which.Package.Should().Be("Broken");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoCandidate_DoesNotExcludeAdvisoriesOrDeprecation(bool vulnerable)
    {
        var report = JsonSerializer.Serialize(new
        {
            version = 1,
            projects = new[] { new { path = "App.csproj", frameworks = new[] {
            new { framework = "net10.0", topLevelPackages = new[] { new { id = "Pkg", resolvedVersion = "1.0.0",
                vulnerabilities = new[] { new { severity = "High", advisoryurl = "https://example.test/advisory" } }, deprecationReasons = new[] { "Legacy" } } } } } } }
        });
        var result = DependencyProbe.AnalyzeOutput(vulnerable ? report : Empty, Report("1.0.0", PackageFrameworkCompatibility.NoCandidateText),
            vulnerable ? Empty : report, Path.GetTempPath(), false, frameworkCompatibility: new Dictionary<OutdatedPackageUpgrade, bool>(), projectPaths: []);
        result.Status.Should().Be("scored");
        result.Score.Should().BeLessThan(10);
        result.Findings.Should().Contain(f => f.Category == (vulnerable ? "vulnerableDirectDependency" : "deprecatedDependency"));
    }

    private const string Empty = "{\"version\":1,\"projects\":[]}";
    private static string Report(string resolved, string latest) => JsonSerializer.Serialize(new
    {
        version = 1,
        projects = new[] { new { path = "App.csproj", frameworks = new[] { new { framework = "net10.0",
            topLevelPackages = new[] { new { id = "Pkg", resolvedVersion = resolved, latestVersion = latest } } } } } }
    });
}
