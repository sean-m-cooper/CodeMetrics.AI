using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using System.Text.Json;

namespace CodeMetrics.AI.Tests.Metrics;

public sealed class ExecutableFunctionMetricsTests
{
    private static (List<TypeMetrics> Types, List<MemberMetrics> Members) Collect(string source)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(source,
            Path.Combine(Path.GetTempPath(), "CodeMetricsProduction", "Widget.cs"));
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        return MetricsCollector.Collect("Sample(net10.0)", compilation);
    }

    [Fact]
    public void NamedLocalHelperAndPrivateMethod_HaveIdenticalScoringMeasurements()
    {
        var local = Collect("""
            class C { int Run(int x) {
                return x > 0 ? Helper(x) : 0;
                int Helper(int value) => value > 1 ? 1 : 2;
            } }
            """);
        var extracted = Collect("""
            class C {
                int Run(int x) => x > 0 ? Helper(x) : 0;
                private int Helper(int value) => value > 1 ? 1 : 2;
            }
            """);
        var a = local.Types.Single().ExecutableMetrics!;
        var b = extracted.Types.Single().ExecutableMetrics!;
        a.Functions.Select(f => f.OwnCyclomaticComplexity).Should().Equal(2, 2);
        a.DecompositionRatio.Should().Be(b.DecompositionRatio);
        a.MaxComplexity.Should().Be(b.MaxComplexity);
        CodeQualityProbe.Analyze(local.Types).Score.Should().Be(CodeQualityProbe.Analyze(extracted.Types).Score);

        // Raw compatibility: local decisions still aggregate into the enclosing
        // CSV member; scoring measurements do not replace these existing values.
        local.Members.Should().ContainSingle().Which.CyclomaticComplexity.Should().Be(3);
        local.Types.Single().MemberCount.Should().Be(1);
        local.Types.Single().CyclomaticComplexity.Should().Be(4);
        local.Types.Single().DecompositionRatio.Should().Be(4);
    }

    [Fact]
    public void FieldsAndAutomaticProperties_CannotDiluteDecomposition()
    {
        const string behavior = "int A(int x) => x > 0 ? 1 : 2; int B(int x) => x < 0 ? 3 : 4;";
        var before = Collect("class C { " + behavior + " }");
        var after = Collect("class C { " + behavior + " int field; int other = 42; public int P { get; set; } }");
        before.Types.Single().ExecutableMetrics!.DecompositionRatio.Should()
            .Be(after.Types.Single().ExecutableMetrics!.DecompositionRatio);
        after.Types.Single().ExecutableMetrics!.FunctionCount.Should().Be(2);
        CodeQualityProbe.Analyze(before.Types).Score.Should().Be(CodeQualityProbe.Analyze(after.Types).Score);
        before.Types.Single().MemberCount.Should().BeLessThan(after.Types.Single().MemberCount);
    }

    [Fact]
    public void NestedCallbacksAndLocalFunctions_OwnEveryDecisionExactlyOnce()
    {
        var result = Collect("""
            using System;
            class C { int Run(int x) {
                Func<int, int> callback = y => y > 0 ? Local(y) : 0;
                return x > 0 ? callback(x) : 0;
                int Local(int value) {
                    Func<int> inner = delegate { return value > 2 ? 1 : 2; };
                    return value > 1 ? inner() : 0;
                }
            } }
            """);
        var measured = result.Types.Single().ExecutableMetrics!;
        measured.Functions.Select(f => f.OwnCyclomaticComplexity).Should().OnlyContain(cc => cc == 2);
        measured.FunctionCount.Should().Be(4);
        measured.DecisionCount.Should().Be(4);
        result.Members.Single().CyclomaticComplexity.Should().Be(5);
        measured.Functions.Should().Contain(f => f.Kind == "localFunction");
        measured.Functions.Count(f => f.Kind == "callback").Should().Be(2);
    }

    [Fact]
    public void BranchFreeCallbacks_DoNotIncreaseDecompositionDenominator()
    {
        var result = Collect("""
            using System;
            class C {
                Func<int> field = () => 1;
                int A(int x) { Func<int> callback = () => 2; return x > 0 ? callback() : 0; }
                int B(int x) => x > 0 ? 1 : 2;
            }
            """);
        var metrics = result.Types.Single().ExecutableMetrics!;
        metrics.FunctionCount.Should().Be(4);
        metrics.DecompositionFunctionCount.Should().Be(2);
        metrics.DecompositionRatio.Should().Be(2.5);
    }

    [Fact]
    public void AccessorsAndInitializers_PreserveTheirOwnAndNestedDecisions()
    {
        var result = Collect("""
            using System;
            class C {
                static bool flag;
                int field = flag ? 1 : 0;
                Func<int> factory = () => flag ? 1 : 0;
                int P { get { return flag ? 1 : 0; } set { if (value > 0) flag = true; } }
                int Q => flag ? 1 : 0;
            }
            """);
        var metrics = result.Types.Single().ExecutableMetrics!;
        metrics.FunctionCount.Should().Be(5);
        metrics.DecisionCount.Should().Be(5);
        metrics.Functions.Count(f => f.Kind == "accessor").Should().Be(2);
        metrics.Functions.Count(f => f.Kind == "initializer").Should().Be(1);
    }

    [Fact]
    public void PartialSignaturesAndNestedTypes_DoNotDuplicateExecutableFunctions()
    {
        var result = Collect("""
            partial class C { partial void A(int x); int B(int x) => x > 0 ? 1 : 0; }
            partial class C {
                partial void A(int x) { if (x > 0) return; }
                class Nested { int D(int x) => x > 1 ? 1 : 0; }
            }
            """);
        result.Types.Single(t => t.Type == "C").ExecutableMetrics!.FunctionCount.Should().Be(2);
        result.Types.Single(t => t.Type == "Nested").ExecutableMetrics!.FunctionCount.Should().Be(1);
    }

    [Fact]
    public void ComplexCallback_RemainsAFunctionHotspotAfterItsParentIsSeparated()
    {
        var branches = string.Join(" ", Enumerable.Range(1, 20).Select(value => $"if (x == {value}) return {value};"));
        var result = Collect("using System; class C { int Run(int x) { Func<int> callback = () => { "
            + branches + " return 0; }; return callback(); } }");
        var metrics = result.Types.Single().ExecutableMetrics!;
        metrics.Functions.Single(f => f.Kind == "method").OwnCyclomaticComplexity.Should().Be(1);
        metrics.Functions.Single(f => f.Kind == "callback").OwnCyclomaticComplexity.Should().Be(21);
        metrics.MaxComplexity.Should().Be(21);
        metrics.DecisionCount.Should().Be(20);
        var dimension = CodeQualityProbe.Analyze(result.Types);
        var details = (JsonElement)dimension.Extra["componentDetails"]!;
        details.GetProperty("methodComplexity").GetProperty("topOffenders")[0]
            .GetProperty("ownCc").GetInt32().Should().Be(21);
    }

    [Fact]
    public void ScoreEvidence_DistinguishesExecutableMeasurementsFromRawMetrics()
    {
        var collected = Collect("class C { int F; int A(int x) => x > 0 ? 1 : 0; int B() => 2; }");
        var result = CodeQualityProbe.Analyze(collected.Types);
        result.ScoringDecision!.Inputs["legacyInputTypes"].Should().Be(0);
        result.ScoringDecision.Policy.Should().Be("dotnet/codeQuality/method-population-v3");
        var details = (JsonElement)result.Extra["componentDetails"]!;
        var type = details.GetProperty("decomposition").GetProperty("topOffenders")[0];
        type.GetProperty("memberCount").GetInt32().Should().Be(3);
        type.GetProperty("decompositionFunctionCount").GetInt32().Should().Be(2);
        type.GetProperty("decompositionComplexity").GetInt32().Should().Be(4);
        type.GetProperty("decompositionRatio").GetDouble().Should().Be(2);
        type.GetProperty("rawDecompositionRatio").GetDouble().Should().Be(1.3333);
    }
}
