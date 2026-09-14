using System.Text.Json;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class ErrorHandlingSourcePopulationTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "ErrorHandlingPopulation");
    private const string EmptyCatch = "class C { void Run() { try {} catch {} } }";

    private static Compilation Compile(string code, string file = "Source.cs", string? symbol = null)
    {
        var (_, _, template) = RoslynTestHelper.CompileCode("");
        var tree = CSharpSyntaxTree.ParseText(code,
            new CSharpParseOptions(preprocessorSymbols: symbol == null ? [] : [symbol]),
            path: file == "" ? "" : Path.Combine(Root, file));
        var compilation = template.RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        return compilation;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void RepeatingSourceAcrossFrameworks_DoesNotEscalateToSystemic(int frameworks)
    {
        var compilation = Compile(EmptyCatch);
        var projects = Enumerable.Range(0, frameworks).Select(index => ($"Library(net{index}.0)", compilation)).ToList();
        var result = ErrorHandlingProbe.Analyze(projects, Root);

        result.Score.Should().Be(0);
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Observations["observationCount"].Should().Be(frameworks);
        JsonSerializer.SerializeToElement(finding.Observations["affectedProjects"]).GetArrayLength().Should().Be(frameworks);
        result.ScoringDecision!.Inputs["emptyCatches"].Should().Be(1);
        result.ScoringDecision.Inputs["projectFrameworkObservations"].Should().Be(frameworks);
    }

    [Fact]
    public void FiveDistinctCatchesOnOneLine_StillCountAsSystemic()
    {
        var compilation = Compile("class C { void Run() { " + string.Concat(Enumerable.Repeat("try {} catch {} ", 5)) + "} }");
        var result = ErrorHandlingProbe.Analyze([("Library(net8.0)", compilation), ("Library(net10.0)", compilation)], Root);

        result.Score.Should().Be(0);
        result.Findings.Where(finding => finding.Category == "emptyCatch").Should().HaveCount(5);
        result.Findings.Where(finding => finding.Category == "emptyCatch")
            .Select(finding => finding.Observations["sourceSpanStart"]).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SeparateConditionalBranches_RemainDistinctSourceFindings()
    {
        const string code = """
            class C { void Run() {
            #if OLD
                try {} catch {}
            #else
                try {} catch {}
            #endif
            } }
            """;
        var result = ErrorHandlingProbe.Analyze([
            ("Library(net8.0)", Compile(code, symbol: "OLD")),
            ("Library(net10.0)", Compile(code))], Root);

        result.Findings.Should().HaveCount(2);
        result.Findings.Should().OnlyContain(finding => (int)finding.Observations["observationCount"]! == 1);
        result.ScoringDecision!.Inputs["emptyCatches"].Should().Be(2);
    }

    [Fact]
    public void FindingPresentInOnlyOneFramework_ListsOnlyThatFramework()
    {
        const string code = """
            class C { void Run() { try {} catch {
            #if SAFE
                throw;
            #endif
            } } }
            """;
        var result = ErrorHandlingProbe.Analyze([
            ("Library(net8.0)", Compile(code, symbol: "SAFE")),
            ("Library(net10.0)", Compile(code))], Root);

        var finding = result.Findings.Should().ContainSingle().Subject;
        JsonSerializer.SerializeToElement(finding.Observations["affectedProjects"])
            .EnumerateArray().Select(item => item.GetString()).Should().Equal("Library(net10.0)");
        result.ScoringDecision!.Inputs["catchPopulation"].Should().Be(1);
        result.ScoringDecision.Inputs["affectedCatches"].Should().Be(1);
        result.ScoringDecision.Inputs["weightedAffectedCatches"].Should().Be(1m);
        result.Score.Should().Be(0);
    }

    [Fact]
    public void IdenticalCodeInDifferentFiles_IsNotDeduplicated()
    {
        var result = ErrorHandlingProbe.Analyze([
            ("A", Compile(EmptyCatch, "A/Source.cs")),
            ("B", Compile(EmptyCatch, "B/Source.cs"))], Root);
        result.Findings.Should().HaveCount(2);
    }

    [Fact]
    public void SharedAuthoredFileAcrossProjects_IsCountedOnce()
    {
        var result = ErrorHandlingProbe.Analyze([
            ("FirstProject", Compile(EmptyCatch)),
            ("SecondProject", Compile(EmptyCatch))], Root);
        result.Findings.Should().ContainSingle().Subject.Observations["observationCount"].Should().Be(2);
    }

    [Fact]
    public void AdvisoryWarnings_AreAlsoScoredBySourceRatherThanFrameworkCount()
    {
        var compilation = Compile("class C { void Run() { try {} catch { var ignored = 1; } } }");
        var result = ErrorHandlingProbe.Analyze(Enumerable.Range(0, 5).Select(index => ($"App(net{index}.0)", compilation)).ToList(), Root);
        result.Score.Should().Be(5);
        result.Findings.Should().ContainSingle().Subject.Category.Should().Be("broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void DifferentRulesAtSameCatch_RemainSeparate()
    {
        var compilation = Compile("class C { int Run() { try { return 1; } catch { return 0; } } }");
        var result = ErrorHandlingProbe.Analyze([("App(net8.0)", compilation), ("App(net10.0)", compilation)], Root);
        result.Score.Should().Be(2.5);
        result.Findings.Select(finding => finding.Category).Should().BeEquivalentTo(
            "broadCatchWithoutLoggingOrRethrow", "broadCatchReturnsDefault");
    }

    [Fact]
    public void ProjectEnumerationOrder_DoesNotChangeFindingsOrProvenance()
    {
        var projects = new List<(string, Compilation)> { ("Z(net10.0)", Compile(EmptyCatch)), ("A(net8.0)", Compile(EmptyCatch)) };
        var forward = ErrorHandlingProbe.Analyze(projects, Root);
        projects.Reverse();
        var reverse = ErrorHandlingProbe.Analyze(projects, Root);
        JsonSerializer.Serialize(reverse).Should().Be(JsonSerializer.Serialize(forward));
    }

    [Fact]
    public void MissingFileIdentity_DoesNotMergePotentiallyUnrelatedSources()
    {
        var compilation = Compile(EmptyCatch, file: "");
        var result = ErrorHandlingProbe.Analyze([("First", compilation), ("Second", compilation)], Root);
        result.Findings.Should().HaveCount(2);
    }
}
