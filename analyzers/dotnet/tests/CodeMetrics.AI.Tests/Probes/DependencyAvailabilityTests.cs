using System.Net;
using System.Text.Json;
using CodeMetrics.AI.Probes;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class DependencyAvailabilityTests : IDisposable
{
    private const string Empty = "{\"version\":1,\"projects\":[],\"sources\":[]}";
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public DependencyAvailabilityTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private static string Report(string package, string details) => $$"""
        {"version":1,"projects":[{"path":"App.csproj","frameworks":[{"framework":"net10.0",
        "topLevelPackages":[{"id":"{{package}}","resolvedVersion":"1.0.0",{{details}}}]}]}]}
        """;

    [Fact]
    public async Task MissingSources_AreAssessmentFailuresNotUpgradePenalties()
    {
        var report = Report("Absent." + Guid.NewGuid().ToString("N"), "\"latestVersion\":\"2.0.0\"");
        var assessment = await PackageFrameworkCompatibility.AssessAsync(PackageFrameworkCompatibility.ParseOutdatedOutput(report), Empty, TestContext.Current.CancellationToken);
        assessment.Failures.Should().ContainSingle().Which.Reasons.Should().Contain("noPackageBaseAddress");
        var result = DependencyProbe.AnalyzeOutput(Empty, report, Empty, directory, false,
            frameworkCompatibility: assessment.Results, compatibilityAssessment: assessment);
        result.Status.Should().Be("failed");
        result.Score.Should().BeNull();
        result.ScoringDecision.Should().BeNull();
        var finding = result.Findings.Single(f => f.Category == "outdatedDependency");
        finding.Severity.Should().Be("info");
        finding.Observations["scoreDisposition"].Should().Be("unavailable");
        JsonSerializer.SerializeToElement(result.Extra["dependencyCompatibility"])
            .GetProperty("status").GetString().Should().Be("failed");
        result.Extra["vulnerabilityAssessmentAvailable"].Should().Be(true);
    }

    [Fact]
    public async Task RepeatedPackageLookup_ReportsUniqueAndAffectedCounts()
    {
        var package = "Absent." + Guid.NewGuid().ToString("N");
        var upgrades = Enumerable.Range(0, 20).Select(i =>
            new OutdatedPackageUpgrade($"App{i}.csproj", "net10.0", package, "2.0.0")).ToArray();
        var result = await PackageFrameworkCompatibility.AssessAsync(upgrades, Empty, TestContext.Current.CancellationToken);
        result.UniquePackageVersions.Should().Be(1);
        result.TotalObservations.Should().Be(20);
        result.Failures.Should().ContainSingle().Which.AffectedObservations.Should().Be(20);
    }

    [Fact]
    public async Task LocalAssets_RemainAssessableWithoutRemoteSources()
    {
        var package = "local." + Guid.NewGuid().ToString("N");
        var assets = Path.Combine(directory, package, "2.0.0", "lib", "net10.0");
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "Library.dll"), "asset fixture");
        var report = JsonSerializer.Serialize(new { version = 1, projects = Array.Empty<object>(), sources = new[] { directory } });
        var known = new OutdatedPackageUpgrade("App", "net10.0", package, "2.0.0");
        var unknown = new OutdatedPackageUpgrade("App", "net10.0", "absent." + package, "2.0.0");
        var result = await PackageFrameworkCompatibility.AssessAsync([known, unknown], report, TestContext.Current.CancellationToken);
        result.Results[known].Should().BeTrue();
        result.Failures.Should().ContainSingle().Which.Package.Should().Be(unknown.Package);
    }

    [Theory]
    [InlineData("invalid version")]
    [InlineData("")]
    public async Task UnavailableLatestVersion_IsExplicit(string version)
    {
        var result = await PackageFrameworkCompatibility.AssessAsync([new("App", "net10.0", "Missing", version)], Empty, TestContext.Current.CancellationToken);
        result.Failures.Should().ContainSingle().Which.Reasons.Should().Contain("latestVersionUnavailable");
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => PackageFrameworkCompatibility.AssessAsync([], Empty, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "sourceIndex:HttpRequestException")]
    [InlineData(HttpStatusCode.OK, "sourceIndexMissingPackageBaseAddress")]
    public async Task SourceDiscovery_PreservesFailureReason(HttpStatusCode status, string reason)
    {
        using var client = new HttpClient(new Handler(() => new(status) { Content = new StringContent("{}") }));
        var reasons = new List<string>();
        var addresses = await NuGetServiceIndexClient.FindPackageBaseAddressesAsync([new Uri("https://feed.invalid/v3/index.json")], TestContext.Current.CancellationToken, reasons.Add, client);
        addresses.Should().BeEmpty();
        reasons.Should().Contain(reason);
    }

    [Fact]
    public async Task SourceDiscovery_RetainsSuccessfulFallback()
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(() => ++calls == 1
            ? new(HttpStatusCode.ServiceUnavailable)
            : new(HttpStatusCode.OK) { Content = new StringContent("{\"resources\":[{\"@id\":\"https://feed.invalid/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}") }));
        var reasons = new List<string>();
        var addresses = await NuGetServiceIndexClient.FindPackageBaseAddressesAsync([new("https://feed.invalid/one"), new("https://feed.invalid/two")], TestContext.Current.CancellationToken, reasons.Add, client);
        addresses.Should().ContainSingle();
        reasons.Should().Contain("sourceIndex:HttpRequestException");
    }

    [Fact]
    public async Task PackageDownload_RetainsHttpStatus()
    {
        using var client = new HttpClient(new Handler(() => new(HttpStatusCode.NotFound)));
        var reasons = new List<string>();
        var result = await NuGetPackageClient.FindFrameworksAsync("missing", "1.0.0", [new Uri("https://feed.invalid/packages/")], TestContext.Current.CancellationToken, reasons.Add, client);
        result.Should().BeNull();
        reasons.Should().Contain("packageHttpStatus:404");
    }

    [Fact]
    public void FailedOutdatedCommand_RetainsVerifiedVulnerabilityAndDeprecation()
    {
        var vulnerable = Report("Risky", "\"vulnerabilities\":[{\"severity\":\"High\",\"advisoryurl\":\"https://example.test/advisory\"}]");
        var deprecated = Report("Old", "\"deprecationReasons\":[\"Legacy\"]");
        var result = DependencyProbe.AnalyzeOutput(vulnerable, "failed", deprecated, directory, true,
            [new("--vulnerable --include-transitive", vulnerable, "", 0),
             new("--outdated", "failed", "restore failed", 1), new("--deprecated", deprecated, "", 0)]);
        result.Status.Should().Be("failed");
        result.Score.Should().BeNull();
        result.Findings.Should().Contain(f => f.Package == "Risky");
        result.Findings.Should().Contain(f => f.Package == "Old");
        result.Extra["vulnerabilityAssessmentAvailable"].Should().Be(true);
    }

    [Fact]
    public void FailedVulnerabilityCommand_CannotImportItsPartialFindings()
    {
        var partial = Report("Risky", "\"vulnerabilities\":[{\"severity\":\"High\"}]");
        var result = DependencyProbe.AnalyzeOutput(partial, Empty, Empty, directory, true,
            [new("--vulnerable", partial, "restore failed", 1), new("--outdated", Empty, "", 0)]);
        result.Extra["vulnerabilityAssessmentAvailable"].Should().Be(false);
        result.Findings.Should().NotContain(f => f.Package == "Risky");
    }

    [Fact]
    public void MissingVulnerabilityAssessment_WithholdsSecurityScoreButPreservesStaticFindings()
    {
        var tree = CSharpSyntaxTree.ParseText("class App { string password = \"this-is-a-real-credential-value\"; }", path: Path.Combine(directory, "App.cs"), cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create("App", [tree]);
        var result = SecurityProbe.Analyze([("App", compilation)], vulnerabilityAssessmentAvailable: false);
        result.Status.Should().Be("failed");
        result.Score.Should().BeNull();
        result.ScoringDecision.Should().BeNull();
        result.Findings.Should().Contain(f => f.Category == "hardcodedSecret");
    }

    [Theory]
    [InlineData(true, "sourceDiscoveryBudgetExceeded")]
    [InlineData(false, "queueBudgetExceeded")]
    public async Task BudgetFailure_IsExplicitAndKeepsCallerCancellationDistinct(bool blockIndex, string expectedReason)
    {
        using var client = new HttpClient(new DelayedHandler(blockIndex));
        var upgrades = Enumerable.Range(0, 20).Select(i =>
            new OutdatedPackageUpgrade("App", "net10.0", "absent." + Guid.NewGuid().ToString("N"), "2.0.0")).ToArray();
        var result = await PackageFrameworkCompatibility.AssessAsync(upgrades, "{\"version\":1,\"projects\":[],\"sources\":[\"https://feed.invalid/index.json\"]}", cancellationToken: TestContext.Current.CancellationToken, budget: TimeSpan.FromMilliseconds(100), client: client);
        result.Results.Should().BeEmpty();
        var reasons = result.Failures.SelectMany(f => f.Reasons).ToArray();
        if (blockIndex)
            reasons.Should().OnlyContain(reason => reason == expectedReason);
        else
        {
            // Cancellation can release an occupied semaphore slot before a queued wait's
            // cancellation callback runs. Either observed stage is valid; no partial success is.
            reasons.Should().OnlyContain(reason => reason == expectedReason || reason == "packageBudgetExceeded");
            reasons.Should().Contain("packageBudgetExceeded");
        }
        result.Failures.Sum(f => f.AffectedObservations).Should().Be(20);
    }

    private sealed class DelayedHandler(bool blockIndex) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!blockIndex && request.RequestUri!.AbsolutePath == "/index.json")
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"resources\":[{\"@id\":\"https://feed.invalid/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}") };
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Cancellation should end the request.");
        }
    }

    private sealed class Handler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond());
    }
}
