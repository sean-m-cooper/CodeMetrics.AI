using System.Text.Json;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Probes;

public class PerformanceSourceFindingsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "AsyncPopulation");
    private static readonly string FilePath = Path.Combine(Root, "Source.cs");

    private static Compilation Compile(string source, string? path = null, params string[] symbols)
    {
        source = "using System.Threading;\n" + source.Replace("System.Threading.Thread", "Thread", StringComparison.Ordinal);
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(source, path ?? FilePath);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(preprocessorSymbols: symbols), path ?? FilePath);
        compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        return compilation;
    }

    private static DimensionResult Analyze(Compilation compilation, int frameworks) => PerformanceAsyncProbe.Analyze(
        Enumerable.Range(0, frameworks).Select(index => ($"Project(net{index})", compilation)).ToArray(), Root);

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void OneWarningDoesNotBecomeManyWarningsAcrossFrameworks(int frameworks)
    {
        var result = Analyze(Compile("class C { void M() { System.Threading.Thread.Sleep(1); } }"), frameworks);
        result.Score.Should().Be(8);
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Observations["observationCount"].Should().Be(frameworks);
        finding.Observations["affectedProjects"].Should().BeOfType<string[]>().Subject.Should().HaveCount(frameworks);
        result.ScoringDecision!.Policy.Should().Be("dotnet/performanceAsync/source-findings-v1");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void OneErrorDoesNotBecomeSystemicAcrossFrameworks(int frameworks)
    {
        var result = Analyze(Compile("class C { void M(System.Threading.Tasks.Task task) { task.Wait(); } }"), frameworks);
        result.Score.Should().Be(2);
        result.Findings.Should().ContainSingle().Subject.Observations["observationCount"].Should().Be(frameworks);
    }

    [Fact]
    public void FiveOperationsOnOneLineRemainFiveErrors()
    {
        var result = Analyze(Compile("class C { void M(System.Threading.Tasks.Task task) { task.Wait(); task.Wait(); task.Wait(); task.Wait(); task.Wait(); } }"), 5);
        result.Score.Should().Be(0);
        result.Findings.Should().HaveCount(5);
        result.Findings.Select(finding => finding.Observations["sourceSpanStart"]).Should().OnlyHaveUniqueItems();
        result.Findings.Should().AllSatisfy(finding => finding.Observations["observationCount"].Should().Be(5));
    }

    [Fact]
    public void DifferentFilesRemainDifferentSites()
    {
        const string source = "class C { void M() { System.Threading.Thread.Sleep(1); } }";
        var result = PerformanceAsyncProbe.Analyze(
            [("One", Compile(source)), ("Two", Compile(source, Path.Combine(Root, "Other.cs")))], Root);
        result.Findings.Should().HaveCount(2);
        result.Score.Should().Be(6);
    }

    [Fact]
    public void ConditionalBranchesKeepTheirExactSpans()
    {
        const string source = """
            class C { void M() {
            #if FIRST
                System.Threading.Thread.Sleep(1);
            #else
                System.Threading.Thread.Sleep(2);
            #endif
            } }
            """;
        var result = PerformanceAsyncProbe.Analyze(
            [("First", Compile(source, symbols: ["FIRST"])), ("Second", Compile(source))], Root);
        result.Findings.Should().HaveCount(2);
        result.Findings.Select(finding => finding.Observations["sourceSpanStart"]).Should().OnlyHaveUniqueItems();
        result.Score.Should().Be(6);
    }

    [Fact]
    public void SeverityVariantsCountOnceAtWorstSeverityAndRetainDetails()
    {
        const string source = """
            class B { public virtual void M(System.Threading.Tasks.Task task) {} }
            class C : B {
            #if OVERRIDE
                public override
            #else
                public new
            #endif
                void M(System.Threading.Tasks.Task task) { task.Wait(); }
            }
            """;
        var projects = new (string, Compilation)[]
        {
            ("Override", Compile(source, symbols: ["OVERRIDE"])), ("Ordinary", Compile(source))
        };
        var forward = PerformanceAsyncProbe.Analyze(projects, Root);
        var reverse = PerformanceAsyncProbe.Analyze(projects.Reverse().ToArray(), Root);
        var finding = forward.Findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be("error");
        finding.Observations["observationCount"].Should().Be(2);
        var variants = JsonSerializer.SerializeToElement(finding.Observations["projectFrameworkObservations"]);
        variants.EnumerateArray().Select(item => item.GetProperty("severity").GetString())
            .Should().BeEquivalentTo("info", "error");
        JsonSerializer.Serialize(forward).Should().Be(JsonSerializer.Serialize(reverse));
        forward.Score.Should().Be(2);
    }

    [Fact]
    public void FindingPresentInOnlyOneVariantIsRetained()
    {
        const string source = """
            class C { void M() {
            #if FIRST
                Thread.Sleep(1);
            #endif
            } }
            """;
        var result = PerformanceAsyncProbe.Analyze(
            [("First", Compile(source, symbols: ["FIRST"])), ("Second", Compile(source))], Root);
        result.Score.Should().Be(8);
        result.Findings.Should().ContainSingle().Subject.Observations["observationCount"].Should().Be(1);
    }

    [Fact]
    public void PhysicalPathCasingFollowsTheOperatingSystem()
    {
        var findings = new[] { FilePath, FilePath.ToUpperInvariant() }.Select(file => new Finding
        {
            Category = "threadSleep",
            Severity = "warning",
            File = file,
            Message = "Thread sleeps.",
            Observations = new() { ["sourceSpanStart"] = 10, ["sourceSpanLength"] = 5 }
        });
        PerformanceSourceFindings.Collapse(findings, Root).Should().HaveCount(OperatingSystem.IsWindows() ? 1 : 2);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("Source.cs", false)]
    public void UnknownIdentityIsNotMerged(string file, bool hasSpan)
    {
        var findings = Enumerable.Range(0, 2).Select(_ => new Finding
        {
            Category = "threadSleep",
            Severity = "warning",
            File = file,
            Message = "Thread sleeps.",
            Observations = hasSpan
                ? new() { ["sourceSpanStart"] = 1, ["sourceSpanLength"] = 5 }
                : new()
        });
        PerformanceSourceFindings.Collapse(findings, Root).Should().HaveCount(2);
    }

    [Fact]
    public void LinkedPathsNormalizeWithoutUsingDisplayLineIdentity()
    {
        var findings = new[] { FilePath, Path.Combine(Root, "sub", "..", "Source.cs") }
            .Select(file => new Finding
            {
                Category = "threadSleep",
                Severity = "warning",
                File = file,
                Message = "Thread sleeps.",
                Observations = new() { ["sourceSpanStart"] = 10, ["sourceSpanLength"] = 5 }
            });
        PerformanceSourceFindings.Collapse(findings, Root).Should().ContainSingle()
            .Subject.Observations["observationCount"].Should().Be(2);
    }

    [Theory]
    [InlineData("missingCancellationToken", "public Task ExecuteAsync() => GetAsync(); Task GetAsync() => Task.CompletedTask;")]
    [InlineData("saveChangesInsideLoop", "void SaveChanges() {} void M(int[] items) { foreach (var item in items) this.SaveChanges(); }")]
    [InlineData("materializationBeforeQueryShape", "void M(int[] items) { var result = items.ToList().Where(item => item > 0); }")]
    [InlineData("awaitedIoInsideLoop", "Task GetAsync() => Task.CompletedTask; async Task M(int[] items) { foreach (var item in items) await GetAsync(); }")]
    [InlineData("unboundedWhenAll", "Task Work(int item) => Task.CompletedTask; Task M(int[] items) => Task.WhenAll(items.Select(item => Work(item)));")]
    [InlineData("sharedStateMutationInFanOut", "class State { public int Value; } Task Work(State state) { state.Value = 1; return Task.CompletedTask; } Task M(int[] items, State state) => Task.WhenAll(items.Select(item => Work(state)));")]
    public void EveryAsyncRuleRecordsIdentityIncludingMultipleRulesAtOneSpan(string category, string body)
    {
        var compilation = Compile("using System.Linq; using System.Threading.Tasks; class C { " + body + " }");
        var once = Analyze(compilation, 1);
        var repeated = Analyze(compilation, 5);
        repeated.Score.Should().Be(once.Score);
        repeated.Findings.Should().HaveCount(once.Findings.Count);
        repeated.Findings.Should().ContainSingle(finding => finding.Category == category)
            .Subject.Observations["observationCount"].Should().Be(5);
        repeated.Findings.Should().AllSatisfy(finding =>
        {
            finding.Observations.Should().ContainKeys("sourceSpanStart", "sourceSpanLength");
            finding.Observations["observationCount"].Should().Be(5);
        });
    }
}
