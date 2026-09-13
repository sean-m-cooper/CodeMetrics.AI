using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Output;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Metrics;

public class PartialTypeMetricsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "PartialTypePopulation");

    private static (List<TypeMetrics> Types, List<MemberMetrics> Members) Collect(
        params (string File, string Code)[] sources)
    {
        var (_, _, template) = RoslynTestHelper.CompileCode("");
        var compilation = template.RemoveAllSyntaxTrees().AddSyntaxTrees(sources.Select(source =>
            CSharpSyntaxTree.ParseText(source.Code, path: Path.Combine(Root, source.File))));
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        return MetricsCollector.Collect("App(net10.0)", compilation, Root);
    }

    [Fact]
    public void SplittingType_PreservesComplexityDecompositionMaintainabilityAndCoupling()
    {
        const string first = """
            private System.IDisposable resource;
            public Widget(System.IDisposable resource) { this.resource = resource; }
            public int First(bool yes) { if (yes) return 1; return 0; }
            """;
        const string second = """
            public System.IDisposable GetResource() { return resource; }
            public int Second(bool yes) { resource.Dispose(); if (yes) return 2; return 0; }
            """;
        var whole = Collect(("Widget.cs", "class Widget {\n" + first + second + "\n}"));
        var split = Collect(
            ("Widget.B.cs", "partial class Widget {\n" + second + "\n}"),
            ("Widget.A.cs", "partial class Widget {\n" + first + "\n}"));

        var actual = split.Types.Should().ContainSingle().Subject;
        actual.Should().BeEquivalentTo(whole.Types.Single(), options => options
            .Excluding(type => type.FilePath).Excluding(type => type.SourceFiles)
            .Excluding(member => member.Path.EndsWith(".File") || member.Path.EndsWith(".Line") || member.Path.EndsWith(".SourceSpanStart"))
            .Excluding(type => type.LinesOfSource)); // Physical declaration headers still occupy source lines.
        split.Members.Should().BeEquivalentTo(whole.Members);
        actual.StructuralCoupledTypes.Should().Equal("System.IDisposable");
        actual.CouplingExclusions.Values.SelectMany(types => types).Should().NotContain("System.IDisposable");
        actual.SourceFiles.Select(Path.GetFileName).Should().Equal("Widget.A.cs", "Widget.B.cs");
        CodeQualityProbe.Analyze(split.Types).Score.Should().Be(CodeQualityProbe.Analyze(whole.Types).Score);
        MaintainabilityProbe.Analyze(split.Types).Score.Should().Be(MaintainabilityProbe.Analyze(whole.Types).Score);
    }

    [Fact]
    public void ReorderingTrees_DoesNotChangeMetricsOrRepresentativeFile()
    {
        var sources = new[]
        {
            ("B.cs", "partial class Widget { public void B() {} }"),
            ("A.cs", "partial class Widget { public void A() {} }")
        };
        var forward = Collect(sources);
        var reverse = Collect(sources.Reverse().ToArray());
        reverse.Should().BeEquivalentTo(forward, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingComplexMethodsToPartialDeclaration_DoesNotCreateDecompositionHotspot(bool separateFiles)
    {
        var simple = string.Join("\n", Enumerable.Range(0, 99).Select(index => $"public void M{index}() {{}}"));
        var branches = string.Join("\n", Enumerable.Range(0, 29).Select(index => $"if (x == {index}) x++;"));
        var complex = "public void Read(int x) {\n" + branches + "\n}\npublic void Start() {}";
        var whole = Collect(("Widget.cs", "class Widget {\n" + simple + complex + "\n}"));
        var partA = "partial class Widget {\n" + simple + "\n}";
        var partB = "partial class Widget {\n" + complex + "\n}";
        var split = separateFiles
            ? Collect(("Widget.cs", partA), ("Widget.Async.cs", partB))
            : Collect(("Widget.cs", partA + "\n" + partB));
        split.Types.Should().ContainSingle();
        split.Types.Single().DecompositionRatio.Should().Be(whole.Types.Single().DecompositionRatio);
        split.Types.Single().MaxMemberCyclomaticComplexity.Should().Be(30);
        CodeQualityProbe.Analyze(split.Types).Score.Should().Be(CodeQualityProbe.Analyze(whole.Types).Score);
    }

    [Fact]
    public void PartialMethodsAndProperties_CountImplementationOnce()
    {
        var result = Collect(
            ("A.cs", "partial class Widget { public partial int Run(bool yes); public partial int Value { get; } }"),
            ("B.cs", "partial class Widget { public partial int Run(bool yes) { if (yes) return 1; return 0; } public partial int Value => 1; }"));
        var type = result.Types.Should().ContainSingle().Subject;
        type.MemberCount.Should().Be(2);
        type.CyclomaticComplexity.Should().Be(4);
        type.MaxMemberCyclomaticComplexity.Should().Be(2);
        result.Members.Should().HaveCount(2).And.OnlyContain(member => member.HasBody);
    }

    [Fact]
    public void GeneratedPartialBody_IsNotImportedThroughTheSymbol()
    {
        var result = Collect(
            ("Widget.cs", "partial class Widget { partial void Generated(); public void Run() {} }"),
            ("Widget.g.cs", "// <auto-generated/>\npartial class Widget { partial void Generated() { if (true) Run(); } }")
        );
        var type = result.Types.Should().ContainSingle().Subject;
        type.SourceFiles.Select(Path.GetFileName).Should().Equal("Widget.cs");
        type.CyclomaticComplexity.Should().Be(2);
        result.Members.Single(member => member.Member == "Generated").HasBody.Should().BeFalse();
    }

    [Fact]
    public async Task Csv_PartialsAppearOnceAndSameNamedNestedAndGenericTypesKeepTheirMembers()
    {
        var result = Collect(
            ("A.cs", """
                namespace Demo;
                partial class Widget { public void A() {} }
                class Widget<T> { public void Generic() {} }
                class Left { public class Widget { public void LeftOnly() {} } }
                class Right { public class Widget { public void RightOnly() {} } }
                """),
            ("B.cs", "namespace Demo; partial class Widget { public void B() {} }"));
        result.Types.Where(type => type.Type == "Widget").Should().HaveCount(4);
        result.Types.Select(type => type.TypeId).Should().OnlyHaveUniqueItems();
        var path = Path.GetTempFileName();
        try
        {
            await CsvWriter.WriteAsync(path, result.Types, result.Members, TestContext.Current.CancellationToken);
            var lines = await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken);
            lines.Where(line => line.StartsWith("Type,")).Should().HaveCount(result.Types.Count);
            lines.Where(line => line.StartsWith("Member,")).Should().HaveCount(5);
            foreach (var name in new[] { "A", "B", "Generic", "LeftOnly", "RightOnly" })
                lines.Count(line => line.Contains($",Widget,{name},")).Should().Be(1);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
