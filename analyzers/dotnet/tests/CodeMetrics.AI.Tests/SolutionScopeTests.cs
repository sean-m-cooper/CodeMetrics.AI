using FluentAssertions;

namespace CodeMetrics.AI.Tests;

public class SolutionScopeTests
{
    [Theory]
    [InlineData("Library.Benchmarks(net8.0)", "Benchmark project")]
    [InlineData("Library.Specs (net10.0)", "Test project")]
    [InlineData("App.AppHost(net10.0)", "Aspire orchestration host")]
    public void FrameworkSuffixDoesNotHideProjectRole(string name, string reason)
    {
        ProjectFilter.ShouldSkip(name, out var actual).Should().BeTrue();
        actual.Should().Be(reason);
    }

    [Fact]
    public void TestSupportPathIsExcludedWithoutExcludingTestingLibrary()
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        ProjectFilter.ShouldSkip("Polly.TestUtils", Path.Combine(root, "test", "Polly.TestUtils.csproj"), root, out _).Should().BeTrue();
        ProjectFilter.ShouldSkip("Polly.Testing", Path.Combine(root, "src", "Polly.Testing.csproj"), root, out _).Should().BeFalse();
    }

    [Fact]
    public async Task DisabledSolutionProjects_AreAbsentFromPackageScope()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var entry = Path.Combine(root, "App.slnx");
        await File.WriteAllTextAsync(entry, """
            <Solution><Project Path="App.csproj"/><Project Path="Broken.csproj"><Build Project="false"/></Project></Solution>
            """, TestContext.Current.CancellationToken);
        try
        {
            var scope = await SolutionScope.ReadAsync(entry, "Release", "Any CPU", TestContext.Current.CancellationToken);
            scope.DisabledPaths.Should().Equal(Path.Combine(root, "Broken.csproj"));
            var filtered = await scope.WriteDependencySolutionAsync(root, TestContext.Current.CancellationToken);
            var reloaded = await SolutionScope.ReadAsync(filtered, "Release", "Any CPU", TestContext.Current.CancellationToken);
            reloaded.ProjectPaths.Should().Equal(Path.Combine(root, "App.csproj"));
        }
        finally { Directory.Delete(root, true); }
    }
}
