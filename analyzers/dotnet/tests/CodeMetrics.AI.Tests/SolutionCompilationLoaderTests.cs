using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

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
    public async Task MissingCompilations_PreserveDiagnosticAndSkippedOrderBeforeEmptyPopulation()
    {
        using var fixture = new SolutionFixture();
        var production = fixture.Add("Core", "public class Core { }");
        var tests = fixture.Add("Core.Tests", "public class Tests { }");
        var selection = SolutionProjectSelection.Create(fixture.Solution, fixture.Root, null, null);
        var context = new SolutionAnalysisContext(2, [], selection.SkippedProjects, [], [], [], [], []);

        await SolutionCompilationDiagnostics.AppendAsync(context,
            [(fixture.Solution.GetProject(production)!, null), (fixture.Solution.GetProject(tests)!, null)],
            selection, TestContext.Current.CancellationToken);

        context.Diagnostics.Select(d => d.Kind).Should().Equal("compilationUnavailable", "compilationUnavailable", "emptyPopulation");
        context.SkippedProjects.Select(p => (p.Name, p.Reason)).Should().Equal(
            ("Core.Tests", "Test project"), ("Core", "Compilation unavailable"));
        context.HasErrors.Should().BeTrue();
    }

    [Theory]
    [InlineData("Core")]
    [InlineData("Core.Tests")]
    public async Task ProjectSuppressor_HonorsPromotedCompilerWarnings(string name)
    {
        using var fixture = new SolutionFixture();
        if (name.EndsWith("Tests")) fixture.Add("Core", "public class Core { }");
        var id = fixture.Add(name, "public class Example { public string Name { get; set; } }");
        fixture.ConfigureSuppressor(id);
        var raw = await fixture.Solution.GetProject(id)!.GetCompilationAsync(TestContext.Current.CancellationToken);
        raw!.GetDiagnostics(TestContext.Current.CancellationToken).Should().Contain(d => d.Id == "CS8618" && d.IsWarningAsError);

        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root, TestContext.Current.CancellationToken);
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task Suppressor_DoesNotHideGenuineErrorsOrOtherPromotedWarnings()
    {
        using var fixture = new SolutionFixture();
        var id = fixture.Add("Core", "#warning Keep this warning\npublic class Example { public string Name { get; set; } public Missing Value; }");
        fixture.ConfigureSuppressor(id);
        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root, TestContext.Current.CancellationToken);
        result.Diagnostics.Should().Contain(d => d.Message.Contains("CS0246"));
        result.Diagnostics.Should().Contain(d => d.Message.Contains("CS1030"));
        result.Diagnostics.Should().NotContain(d => d.Message.Contains("CS8618"));
        result.HasErrors.Should().BeTrue();
    }

    [Fact]
    public async Task DisabledSuppressor_LeavesPromotedWarningBlocking()
    {
        using var fixture = new SolutionFixture();
        var id = fixture.Add("Core", "public class Example { public string Name { get; set; } }");
        fixture.ConfigureSuppressor(id, disabled: true);
        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root, TestContext.Current.CancellationToken);
        result.Diagnostics.Should().Contain(d => d.Message.Contains("CS8618"));
        result.HasErrors.Should().BeTrue();
    }

    [Fact]
    public async Task SuppressorFailure_MakesAssessmentUnavailable()
    {
        using var fixture = new SolutionFixture();
        var id = fixture.Add("Core", "public class Example { public string Name { get; set; } }");
        fixture.ConfigureSuppressor(id, throws: true);
        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root, TestContext.Current.CancellationToken);
        result.Diagnostics.Should().Contain(d => d.Kind == "compilationUnavailable" && d.Message.Contains("Diagnostic suppression failed"));
        result.HasErrors.Should().BeTrue();
    }

    [Fact]
    public async Task CrossProjectAsyncCall_DiagnosticsMatchMetadataBuild_WithoutChangingMetricReferences()
    {
        using var fixture = new SolutionFixture();
        var reference = fixture.Add("Referenced", "public class Helper { public static async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Yield(); } }");
        var entry = fixture.Add("Entry", "public class Entry { public void Run() { Helper.Run(); } }", reference);
        fixture.ConfigureSuppressor(entry);
        var raw = (await fixture.Solution.GetProject(entry)!.GetCompilationAsync(TestContext.Current.CancellationToken))!;
        raw.GetDiagnostics(TestContext.Current.CancellationToken).Should().Contain(d => d.Id == "CS4014" && d.IsWarningAsError);
        var metadataCompilation = new CompilationReferenceMetadata().Create(raw, TestContext.Current.CancellationToken);
        metadataCompilation.GetDiagnostics(TestContext.Current.CancellationToken).Should().NotContain(d => d.Id == "CS4014");

        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root, TestContext.Current.CancellationToken);
        result.Diagnostics.Should().BeEmpty();
        result.AllProjectCompilations.Single(p => p.Name == "Entry").Compilation.References
            .Should().Contain(r => r is CompilationReference);
    }

    [Fact]
    public async Task AsyncCallerAndSameProjectCalls_KeepUnawaitedCallDiagnostics()
    {
        using var fixture = new SolutionFixture();
        var reference = fixture.Add("Referenced", "public class Helper { public static async System.Threading.Tasks.Task Run() { await System.Threading.Tasks.Task.Yield(); } }");
        var entry = fixture.Add("Entry", """
            public class Entry {
                public async System.Threading.Tasks.Task Run() { Helper.Run(); await System.Threading.Tasks.Task.Yield(); }
                public void CallLocal() { Local(); }
                public async System.Threading.Tasks.Task Local() { await System.Threading.Tasks.Task.Yield(); }
            }
            """, reference);
        fixture.ConfigureSuppressor(entry);
        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root, TestContext.Current.CancellationToken);
        result.Diagnostics.Count(d => d.Message.Contains("CS4014")).Should().Be(2);
        result.HasErrors.Should().BeTrue();
    }

    [Fact]
    public async Task ReferenceEmissionFailure_RemainsBlocking()
    {
        using var fixture = new SolutionFixture();
        var reference = fixture.Add("Referenced", "public class Helper { public MissingType Value; }");
        var entry = fixture.Add("Entry", "public class Entry { public string Name { get; set; } }", reference);
        fixture.ConfigureSuppressor(entry);
        var result = await SolutionCompilationLoader.LoadAsync(fixture.Solution, fixture.Root, TestContext.Current.CancellationToken);
        result.Diagnostics.Should().Contain(d => d.Kind == "compilationUnavailable" && d.Message.Contains("reference metadata"));
        result.HasErrors.Should().BeTrue();
    }

#pragma warning disable RS1001 // Instantiated through AnalyzerImageReference, not shipped as a discoverable compiler extension.
    private sealed class InitializationSuppressor(bool throws) : DiagnosticSuppressor
    {
        private static readonly SuppressionDescriptor Descriptor = new("TESTSUP001", "CS8618", "Initialized by the test fixture.");
        public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [Descriptor];
        public override void ReportSuppressions(SuppressionAnalysisContext context)
        {
            if (throws) throw new InvalidOperationException("Broken suppressor");
            foreach (var diagnostic in context.ReportedDiagnostics.Where(d => d.Id == "CS8618"))
                context.ReportSuppression(Suppression.Create(Descriptor, diagnostic));
        }
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

#pragma warning restore RS1001

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

        internal void ConfigureSuppressor(ProjectId id, bool disabled = false, bool throws = false)
        {
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                generalDiagnosticOption: ReportDiagnostic.Error, nullableContextOptions: NullableContextOptions.Enable);
            if (disabled) options = options.WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic> { ["TESTSUP001"] = ReportDiagnostic.Suppress });
            Solution = Solution.WithProjectCompilationOptions(id, options)
                .AddAnalyzerReference(id, new AnalyzerImageReference([new InitializationSuppressor(throws)]));
        }

        internal SolutionScope Scope(ProjectId[] included, ProjectId[] disabled) => new(
            Paths(included).ToHashSet(SolutionScope.PathComparer), Paths(disabled).ToHashSet(SolutionScope.PathComparer));

        public void Dispose() => workspace.Dispose();
    }
}
