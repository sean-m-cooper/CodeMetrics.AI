using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class MethodComplexityPopulationTests
{
    private static TypeMetrics Type(string project, int[] complexities, string file = "source.cs", int offset = 0) => new()
    {
        Project = project,
        Namespace = "Calibration",
        Type = "Functions",
        FilePath = file,
        ExecutableMetrics = new ExecutableTypeMetrics(complexities.Select((cc, index) =>
            new ExecutableFunctionMetrics("F" + (offset + index), "method", file, 1, cc)
            {
                SourceSpanStart = (offset + index) * 20,
                SourceSpanLength = 19
            }).ToArray())
    };

    private static ScoringDecision Decision(params TypeMetrics[] types) =>
        CodeQualityProbe.Analyze(types).ScoringDecision!.Steps.Single(step => step.Id == "complexity").Decision!;

    [Theory]
    [InlineData(3, 100, 10)]
    [InlineData(5, 100, 8)]
    [InlineData(10, 1, 8.4)]
    [InlineData(10, 10, 8.2)]
    [InlineData(10, 100, 6)]
    [InlineData(20, 1, 7.6)]
    [InlineData(21, 1, 7.5)]
    [InlineData(21, 10, 7.2)]
    [InlineData(21, 20, 6.8)]
    [InlineData(40, 1, 6)]
    public void AcceptedPopulationsMatchProductExamples(int cc, int count, double expected)
    {
        var values = Enumerable.Repeat(cc, count).Concat(Enumerable.Repeat(3, 100 - count)).ToArray();
        var decision = Decision(Type("App", values));
        decision.FinalScore.Should().Be(expected);
        decision.Inputs["eligibleFunctions"].Should().Be(100);
        decision.Inputs["remainingFunctions"].Should().Be(99);
        decision.Policy.Should().Be("dotnet/codeQuality/complexity/source-functions-40-60-v1");
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(3, 10)]
    [InlineData(4, 9)]
    [InlineData(5, 8)]
    [InlineData(6, 7.6)]
    [InlineData(10, 6)]
    [InlineData(11, 5.8)]
    [InlineData(15, 5)]
    [InlineData(20, 4)]
    [InlineData(21, 3.8)]
    [InlineData(30, 2)]
    [InlineData(40, 0)]
    [InlineData(100, 0)]
    public void SingleFunctionUsesItsIndividualAssessment(int cc, double expected)
    {
        var decision = Decision(Type("App", [cc]));
        decision.FinalScore.Should().Be(expected);
        decision.Inputs["singleFunctionScope"].Should().Be(true);
        decision.Inputs["remainingMeanScore"].Should().BeNull();
        decision.Steps.Single(step => step.Id == "remainingPopulation").Disposition.Should().Be("notMatched");
    }

    [Fact]
    public void DecimalMeanIsRoundedOnceAndRecordedAsEquivalentDeductions()
    {
        var decision = Decision(Type("App", [5, 3, 3, 3, 4]));
        decision.FinalScore.Should().Be(9.1);
        decision.Inputs["unroundedScore"].Should().Be(9.05m);
        decision.Inputs["remainingMeanScore"].Should().Be(9.75m);
        decision.Inputs["roundingMode"].Should().Be("AwayFromZero");
        decision.Operation.Should().Be("deductions");
        decision.Steps.Sum(step => (decimal)step.Score).Should().Be(.95m);
    }

    [Fact]
    public void ExactlyOneWorstFunctionIsRemovedEvenWhenSeveralTie()
    {
        var decision = Decision(Type("App", [21, 21, 3]));
        decision.Inputs["remainingFunctions"].Should().Be(2);
        decision.Inputs["remainingMeanScore"].Should().Be(6.9m);
        decision.FinalScore.Should().Be(5.7);
    }

    [Fact]
    public void SeverityAndAdditionalHotspotsNeverImproveTheScore()
    {
        var previous = 10.0;
        for (var cc = 1; cc <= 100; cc++)
        {
            var score = Decision(Type("App", [cc, 3, 3])).FinalScore;
            score.Should().BeLessThanOrEqualTo(previous);
            if (cc >= 21) score.Should().BeLessThan(8);
            if (cc is >= 6 and <= 10) score.Should().BeGreaterThanOrEqualTo(8);
            previous = score;
        }
        decimal previousRaw = 10;
        for (var count = 1; count <= 100; count++)
        {
            var values = Enumerable.Repeat(21, count).Concat(Enumerable.Repeat(3, 100 - count)).ToArray();
            var raw = (decimal)Decision(Type("App", values)).Inputs["unroundedScore"]!;
            raw.Should().BeLessThan(previousRaw);
            previousRaw = raw;
        }
    }

    [Fact]
    public void SimpleMethodPaddingPreservesTheSevereCeiling()
    {
        var before = Decision(Type("App", [21, 3]));
        var after = Decision(Type("App", [21, .. Enumerable.Repeat(3, 1000)]));
        after.Inputs["aggregateCeiling"].Should().Be(before.Inputs["aggregateCeiling"]);
        after.FinalScore.Should().Be(before.FinalScore).And.BeLessThan(8);
    }

    [Fact]
    public void TypeGroupingDoesNotChangeTheFunctionPopulationScore()
    {
        var together = Decision(Type("App", [21, 5, 3]));
        var separated = Decision(Type("App", [21]), Type("App", [5, 3], offset: 1));
        separated.FinalScore.Should().Be(together.FinalScore);
        separated.Inputs["eligibleFunctions"].Should().Be(3);
    }

    [Fact]
    public void RepeatedSourceAcrossProjectsAndFrameworksCountsOnceWithWorstVariantCc()
    {
        var original = Type("App(net9.0)", [3, 21]);
        var repeated = Type("App(net10.0)", [3, 21]);
        Decision(original, repeated).FinalScore.Should().Be(Decision(original).FinalScore);
        var changed = Type("Linked(net10.0)", [3, 30]);
        var result = CodeQualityProbe.Analyze([original, repeated, changed]);
        var decision = result.ScoringDecision!.Steps.Single(step => step.Id == "complexity").Decision!;
        decision.Inputs["eligibleFunctions"].Should().Be(2);
        decision.Inputs["functionObservations"].Should().Be(6);
        decision.Inputs["repeatedObservations"].Should().Be(4);
        decision.Inputs["variantComplexityDifferences"].Should().Be(1);
        decision.FinalScore.Should().Be(6.8);
        var details = ((JsonElement)result.Extra["componentDetails"]!).GetProperty("methodComplexity");
        details.GetProperty("worstFunction").GetProperty("affectedProjects").GetArrayLength().Should().Be(3);
        details.GetProperty("worstFunction").GetProperty("ownCc").GetInt32().Should().Be(30);
        Decision(changed, repeated, original).Inputs.Should().BeEquivalentTo(decision.Inputs);
    }

    [Fact]
    public void DifferentFilesAndMissingSourceIdentitiesRemainDistinct()
    {
        Decision(Type("A", [21], "a.cs"), Type("B", [21], "b.cs")).Inputs["eligibleFunctions"].Should().Be(2);
        var missing = new TypeMetrics
        {
            Project = "Unknown",
            Namespace = "Calibration",
            Type = "Functions",
            FilePath = "",
            ExecutableMetrics = new ExecutableTypeMetrics([
                new ExecutableFunctionMetrics("F", "method", "", 1, 21),
                new ExecutableFunctionMetrics("F", "method", "", 1, 21)])
        };
        var decision = Decision(missing);
        decision.Inputs["eligibleFunctions"].Should().Be(2);
        decision.Inputs["unidentifiedObservations"].Should().Be(2);
    }

    [Fact]
    public void CollectorSpansKeepCallbacksOnTheSameLineDistinct()
    {
        const string source = "using System; class C { void M() { Func<int,int> a = x => x > 1 ? 1 : 2, b = x => x > 2 ? 3 : 4; } }";
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(source, Path.Combine(Path.GetTempPath(), "Production", "Functions.cs"));
        var collected = MetricsCollector.Collect("App(net10.0)", compilation);
        var functions = collected.Types.Single().ExecutableMetrics!.Functions;
        functions.Should().HaveCount(3);
        functions.Select(function => function.SourceSpanStart).Should().OnlyHaveUniqueItems();
        Decision([.. collected.Types, .. collected.Types]).Inputs["eligibleFunctions"].Should().Be(3);
    }

    [Fact]
    public void ConditionalBodiesCountOnceAndDistinctConditionalDeclarationsRemainSeparate()
    {
        const string source = """
            class C {
              int M(int x) {
            #if FEATURE
                if (x > 0) return 1;
            #endif
                return 0;
              }
            #if FEATURE
              int N() => 1;
            #else
              int N() => 2;
            #endif
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), "Production", "Conditional.cs");
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(source, path);
        var featureTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithPreprocessorSymbols("FEATURE"), path,
            cancellationToken: TestContext.Current.CancellationToken);
        var first = MetricsCollector.Collect("App(base)", compilation);
        var second = MetricsCollector.Collect("App(feature)", compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(featureTree));
        var decision = Decision([.. first.Types, .. second.Types]);
        decision.Inputs["eligibleFunctions"].Should().Be(3);
        decision.Inputs["functionObservations"].Should().Be(4);
        decision.Inputs["variantComplexityDifferences"].Should().Be(1);
        decision.Inputs["worstOwnCc"].Should().Be(2);
    }

    [Fact]
    public void ConditionalAttributesAndReturnTypesDoNotDuplicateTheAuthoredMethod()
    {
        const string source = """
            class C {
            #if FEATURE
              [System.Obsolete]
              long
            #else
              int
            #endif
              M(int x) => x > 0 ? 1 : 0;
            }
            """;
        var path = Path.Combine(Path.GetTempPath(), "Production", "Attributes.cs");
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(source, path);
        var featureTree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithPreprocessorSymbols("FEATURE"), path,
            cancellationToken: TestContext.Current.CancellationToken);
        var first = MetricsCollector.Collect("App(base)", compilation);
        var second = MetricsCollector.Collect("App(feature)", compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(featureTree));
        var decision = Decision([.. first.Types, .. second.Types]);
        decision.Inputs["eligibleFunctions"].Should().Be(1);
        decision.Inputs["functionObservations"].Should().Be(2);
        decision.Inputs["worstOwnCc"].Should().Be(2);
        first.Types.Single().ExecutableMetrics!.Functions.Single().SourceSpanStart.Should().Be(source.IndexOf("M(int", StringComparison.Ordinal));
    }

    [Fact]
    public void MixedLegacyInputIsExplicitAndDoesNotInventAFunctionPopulation()
    {
        var legacy = new TypeMetrics
        {
            Project = "Old",
            Namespace = "Calibration",
            Type = "Legacy",
            FilePath = "legacy.cs",
            MemberCount = 2,
            MaxMemberCyclomaticComplexity = 21
        };
        var result = CodeQualityProbe.Analyze([Type("New", [3, 21]), legacy]);
        result.Extra["complexityMeasurementPolicy"].Should().Be("legacy-type-maxima-v1");
        result.ScoringDecision!.Inputs["legacyInputTypes"].Should().Be(1);
        var metrics = (JsonElement)result.Extra["metrics"]!;
        metrics.TryGetProperty("methodComplexity", out _).Should().BeFalse();
        metrics.TryGetProperty("maxMemberCyclomaticComplexity", out _).Should().BeTrue();
    }
}
