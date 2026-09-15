using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetrics.AI.Tests;

public sealed class BenchmarkProjectTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("benchmark-scope-").FullName;
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData("Quartz.Benchmark", true)]
    [InlineData("Quartz.Benchmarks (net10.0)", true)]
    [InlineData("benchmark", true)]
    [InlineData("BenchmarkEngine", false)]
    [InlineData("BenchmarkDotNet", false)]
    [InlineData("App.Benchmarking", false)]
    public void NamesMatchBoundedConventions(string name, bool excluded) =>
        ProjectFilter.ShouldSkip(name, out _).Should().Be(excluded);

    [Theory]
    [InlineData("benchmark", null, true)]
    [InlineData("benchmarks", null, true)]
    [InlineData("bench", null, true)]
    [InlineData("src", "true", true)]
    [InlineData("benchmark", "false", false)]
    [InlineData("src", "false", false)]
    public void ExplicitMarkerAndDirectoryScope(string directory, string? marker, bool excluded)
    {
        var path = Path.Combine(root, directory, "Runner.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"<Project><PropertyGroup><IsBenchmarkProject>{marker}</IsBenchmarkProject></PropertyGroup></Project>");
        ProjectFilter.ShouldSkip("Runner", path, root, out _).Should().Be(excluded);
    }

    [Theory]
    [InlineData(true, false, null, false)]
    [InlineData(true, false, "false", true)]
    [InlineData(false, false, null, true)]
    [InlineData(true, true, null, true)]
    public async Task SemanticBenchmarkMarker_ExcludesOnlyExecutableAndHonorsOptOut(bool executable, bool lookalike, string? marker, bool analyzed)
    {
        using var workspace = new AdhocWorkspace();
        var path = Path.Combine(root, "Runner.csproj");
        File.WriteAllText(path, $"<Project><PropertyGroup><IsBenchmarkProject>{marker}</IsBenchmarkProject></PropertyGroup></Project>");
        var (_, _, referenceCompilation) = RoslynTestHelper.CompileCode("class C {}");
        var id = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(id, VersionStamp.Create(), "Runner", "Runner", LanguageNames.CSharp,
            filePath: path, compilationOptions: new CSharpCompilationOptions(executable ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: referenceCompilation.References));
        var ns = lookalike ? "Unrelated" : "BenchmarkDotNet.Attributes";
        var code = $$"""
            namespace {{ns}} { public class BenchmarkAttribute : System.Attribute {} }
            class Program { static void Main() {} [{{ns}}.Benchmark] public int Measure() => 1; }
            """;
        solution = solution.AddDocument(DocumentId.CreateNewId(id), "Source.cs", SourceText.From(code), filePath: Path.Combine(root, "Source.cs"));
        var context = await SolutionCompilationLoader.LoadAsync(solution, root, TestContext.Current.CancellationToken);
        context.AnalyzedProjectNames.Contains("Runner").Should().Be(analyzed);
        context.DependencyProjectScopes[path].Should().Be(analyzed ? "production" : "benchmark");
        context.ScopedProjectPaths.Should().Contain(path);
        if (!analyzed) context.TypeMetrics.Should().BeEmpty();
    }

    [Fact]
    public void ExplicitFalseOverridesName_AndConditionalMarkerIsNotAssumed()
    {
        var path = Path.Combine(root, "Runner.csproj");
        File.WriteAllText(path, "<Project><PropertyGroup><IsBenchmarkProject>false</IsBenchmarkProject></PropertyGroup></Project>");
        ProjectFilter.ShouldSkip("Runner.Benchmark", path, root, out _).Should().BeFalse();
        File.WriteAllText(path, "<Project><PropertyGroup Condition=\"'$(Configuration)' == 'Benchmark'\"><IsBenchmarkProject>true</IsBenchmarkProject></PropertyGroup></Project>");
        ProjectFilter.ShouldSkip("Runner", path, root, out _).Should().BeFalse();
    }
}
