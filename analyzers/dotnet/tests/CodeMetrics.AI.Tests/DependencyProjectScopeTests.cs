using CodeMetrics.AI.Output;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests;

public sealed class DependencyProjectScopeTests
{
    [Fact]
    public void CollidingDisplayNames_DoNotTransferTestScopeToAnotherProject()
    {
        using var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), "ScopeFixture");
        var solution = workspace.CurrentSolution;
        foreach (var directory in new[] { "tests", "hosts" })
            solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
                "Shared", "Shared", LanguageNames.CSharp, filePath: Path.Combine(root, directory, "Shared.csproj")));
        var projects = solution.Projects.ToArray();
        var selection = new SolutionProjectSelection(2, projects, projects,
            [new SkippedProjectInfo { Name = "Shared", Reason = "Test project" },
             new SkippedProjectInfo { Name = "Shared", Reason = "Aspire orchestration host" }], []);
        DependencyProjectScope.Create(selection, root).Values.Should().OnlyContain(scope => scope == "unknown");
    }

    [Theory]
    [InlineData("src/App.csproj", "Test project (semantic attributes)", false, "test")]
    [InlineData("benchmarks/Tests.Performance.csproj", "Test project", false, "benchmark")]
    [InlineData("src/App.Benchmarks.csproj", "Benchmark project", false, "benchmark")]
    [InlineData("src/App.Hosting.csproj", "Aspire / generic hosting", false, "unknown")]
    [InlineData("samples/App.csproj", "Sample / demo code", false, "unknown")]
    [InlineData("src/App.csproj", "", true, "production")]
    [InlineData("tests/App.csproj", "Test project", true, "production")]
    public void ScopeUsesLoadedSelection_ProductionVariantWins(string relativePath, string reason, bool production, string expected)
    {
        using var workspace = new AdhocWorkspace();
        var root = Path.GetFullPath("ScopeFixture");
        var path = Path.GetFullPath(relativePath, root);
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "App (net8.0)", "App", LanguageNames.CSharp, filePath: path));
        solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(),
            "App (net10.0)", "App", LanguageNames.CSharp, filePath: path));
        var projects = solution.Projects.ToArray();
        var selection = new SolutionProjectSelection(2, projects, projects,
            projects.Select(p => new SkippedProjectInfo { Name = p.Name, Reason = reason }).ToList(),
            production ? [projects[1].Id] : []);
        var scopes = DependencyProjectScope.Create(selection, root);
        scopes.Should().ContainSingle();
        scopes[path].Should().Be(expected);
    }
}
