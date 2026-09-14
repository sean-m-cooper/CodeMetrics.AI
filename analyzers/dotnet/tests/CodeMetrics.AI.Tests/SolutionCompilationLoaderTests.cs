using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetrics.AI.Tests;

public class SolutionCompilationLoaderTests
{
    [Fact]
    public async Task Load_PreservesProductionTestingAndBuildScopePopulations()
    {
        using var fixture = new SolutionFixture();
        var production = fixture.Add("Core", "public class Core { public int Get() => 1; }");
        var tests = fixture.Add("Core.Tests", "public class NamedTests { }");
        var semanticTests = fixture.Add("BehaviorChecks", """
            namespace Xunit { public class FactAttribute : System.Attribute { } }
            public class Checks { [Xunit.Fact] public void Verify() { } }
            """);
        var disabled = fixture.Add("Disabled", "invalid source");
        var outside = fixture.Add("Outside", "invalid source");
        var host = fixture.Add("App.AppHost", "invalid source");
        var benchmark = fixture.Add("Core.Benchmarks", "invalid source");
        var scope = fixture.Scope([production, tests, semanticTests, disabled, host, benchmark], [disabled]);

        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root,
            TestContext.Current.CancellationToken, scope: scope);

        result.TotalProjectCount.Should().Be(7);
        result.AnalyzedProjectNames.Should().Equal("Core");
        result.AllProjectCompilations.Select(p => p.Name).Should().Equal("Core", "Core.Tests", "BehaviorChecks");
        result.TypeMetrics.Should().ContainSingle(t => t.Type == "Core");
        result.TypeMetrics.Should().OnlyContain(t => t.Project == "Core");
        result.ScopedProjectPaths.Should().Equal(fixture.Paths(production, tests, semanticTests, host, benchmark));
        result.SkippedProjects.Select(p => (p.Name, p.Reason)).Should().Equal(
            ("Core.Tests", "Test project"),
            ("Disabled", "Excluded by solution build configuration"),
            ("Outside", "Reference outside selected solution"),
            ("App.AppHost", "Aspire orchestration host"),
            ("Core.Benchmarks", "Benchmark project"),
            ("BehaviorChecks", "Test project (semantic attributes)"));
        result.Diagnostics.Should().BeEmpty();
        result.HasErrors.Should().BeFalse();
    }

    [Fact]
    public async Task ProjectEntryPoint_KeepsReferenceResolutionWithoutScoringTheReference()
    {
        using var fixture = new SolutionFixture();
        var reference = fixture.Add("Referenced", "public class Helper { }");
        var entry = fixture.Add("Entry", "public class Entry { public Helper Get() => new Helper(); }", reference);

        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root,
            TestContext.Current.CancellationToken, entry, fixture.Scope([entry], []));

        result.TotalProjectCount.Should().Be(1);
        result.AnalyzedProjectNames.Should().Equal("Entry");
        result.AllProjectCompilations.Should().ContainSingle();
        result.TypeMetrics.Should().ContainSingle(t => t.Type == "Entry");
        result.Diagnostics.Should().BeEmpty();
        result.SkippedProjects.Should().BeEmpty();
        result.ScopedProjectPaths.Should().Equal(fixture.Paths(entry));
    }

    [Fact]
    public async Task TestCompilationErrors_RemainBlockingAndCappedAtTwenty()
    {
        using var fixture = new SolutionFixture();
        fixture.Add("Core", "public class Core { }");
        fixture.Add("Core.Tests", "public class Broken { " +
            string.Join(" ", Enumerable.Range(0, 25).Select(i => $"Missing{i} field{i};")) + " }");

        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root, TestContext.Current.CancellationToken);

        result.AnalyzedProjectNames.Should().Equal("Core");
        result.Diagnostics.Should().HaveCount(20);
        result.Diagnostics.Should().OnlyContain(d => d.Kind == "compilationError" && d.Project == "Core.Tests");
        result.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void MissingCompilations_PreserveDiagnosticAndSkippedOrderBeforeEmptyPopulation()
    {
        using var fixture = new SolutionFixture();
        var production = fixture.Add("Core", "public class Core { }");
        var tests = fixture.Add("Core.Tests", "public class Tests { }");
        var selection = SolutionProjectSelection.Create(fixture.Solution, fixture.Root, null, null);
        var context = new SolutionAnalysisContext(2, [], selection.SkippedProjects, [], [], [], [], []);

        SolutionCompilationDiagnostics.Append(context,
            [(fixture.Solution.GetProject(production)!, null), (fixture.Solution.GetProject(tests)!, null)],
            selection, TestContext.Current.CancellationToken);

        context.Diagnostics.Select(d => d.Kind).Should().Equal("compilationUnavailable", "compilationUnavailable", "emptyPopulation");
        context.SkippedProjects.Select(p => (p.Name, p.Reason)).Should().Equal(
            ("Core.Tests", "Test project"), ("Core", "Compilation unavailable"));
        context.HasErrors.Should().BeTrue();
    }

    [Fact]
    public async Task Load_PropagatesCallerCancellation()
    {
        using var fixture = new SolutionFixture();
        fixture.Add("Core", "public class Core { }");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root, cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class SolutionFixture : IDisposable
    {
        private readonly AdhocWorkspace workspace = new();
        private readonly IEnumerable<MetadataReference> references = RoslynTestHelper.CompileCode("").Compilation.References;
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "codemetrics-loader-" + Guid.NewGuid().ToString("N"));
        internal Solution Solution { get; private set; }

        internal SolutionFixture() => Solution = workspace.CurrentSolution;

        internal ProjectId Add(string name, string source, params ProjectId[] projectReferences)
        {
            var id = ProjectId.CreateNewId();
            Solution = Solution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), name, name, LanguageNames.CSharp,
                filePath: Path.Combine(Root, name + ".csproj"),
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                metadataReferences: references, projectReferences: projectReferences.Select(p => new ProjectReference(p))));
            Solution = Solution.AddDocument(DocumentId.CreateNewId(id), name + ".cs", SourceText.From(source),
                filePath: Path.Combine(Root, name + ".cs"));
            return id;
        }

        internal string[] Paths(params ProjectId[] ids) => ids.Select(id => Solution.GetProject(id)!.FilePath!).ToArray();

        internal SolutionScope Scope(ProjectId[] included, ProjectId[] disabled) => new(
            Paths(included).ToHashSet(SolutionScope.PathComparer), Paths(disabled).ToHashSet(SolutionScope.PathComparer));

        public void Dispose() => workspace.Dispose();
    }
}
