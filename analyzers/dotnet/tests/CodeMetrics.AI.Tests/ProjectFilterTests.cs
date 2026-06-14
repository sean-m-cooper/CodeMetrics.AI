using FluentAssertions;

namespace CodeMetrics.AI.Tests;

public class ProjectFilterTests
{
    [Fact]
    public void ShouldSkip_TestProject_ReturnsTrue()
    {
        var result = ProjectFilter.ShouldSkip("MyApp.Tests", out var reason);

        result.Should().BeTrue();
        reason.Should().Be("Test project");
    }

    [Fact]
    public void ShouldSkip_AppHost_ReturnsTrue()
    {
        var result = ProjectFilter.ShouldSkip("MyApp.AppHost", out var reason);

        result.Should().BeTrue();
        reason.Should().Be("Aspire orchestration host");
    }

    [Fact]
    public void ShouldSkip_CoreProject_ReturnsFalse()
    {
        var result = ProjectFilter.ShouldSkip("MyApp.Core", out var reason);

        result.Should().BeFalse();
        reason.Should().BeEmpty();
    }

    [Fact]
    public void ShouldSkip_Benchmarks_ReturnsTrue()
    {
        var result = ProjectFilter.ShouldSkip("MyApp.Benchmarks", out var reason);

        result.Should().BeTrue();
        reason.Should().Be("Benchmark project");
    }

    [Fact]
    public void SourceFileFilter_InSolutionRoot_ReturnsTrue()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodeMetricsSourceRoot"));
        var file = Path.Combine(root, "src", "App", "Service.cs");

        SourceFileFilter.ShouldAnalyze(file, root).Should().BeTrue();
    }

    [Fact]
    public void SourceFileFilter_OutsideSolutionRoot_ReturnsFalse()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodeMetricsSourceRoot"));
        var file = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "OtherRoot", "Service.cs"));

        SourceFileFilter.ShouldAnalyze(file, root).Should().BeFalse();
    }

    [Fact]
    public void SourceFileFilter_NuGetPackagePath_ReturnsFalse()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodeMetricsSourceRoot"));
        var file = Path.Combine(root, ".nuget", "packages", "some.package", "contentFiles", "Service.cs");

        SourceFileFilter.ShouldAnalyze(file, root).Should().BeFalse();
    }
}
