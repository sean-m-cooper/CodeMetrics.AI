using System.Text.Json;
using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class FunctionMaintainabilityTests
{
    private static TypeMetrics Functions(decimal[] values, string project = "App", string file = "source.cs") => new()
    {
        Project = project,
        Type = "Functions",
        Namespace = "Tests",
        FilePath = file,
        ExecutableMetrics = new(values.Select((mi, index) => new ExecutableFunctionMetrics("F" + index, "method", file, 1, 1)
        { SourceSpanStart = index * 20, Maintainability = new(1, 5, mi) }).ToArray())
    };

    private static DimensionResult Analyze(params decimal[] mi) => MaintainabilityProbe.Analyze([Functions(mi)]);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 9.8)]
    [InlineData(10, 8.4)]
    [InlineData(20, 6.8)]
    [InlineData(50, 5)]
    public void AcceptedHundredFunctionExamples(int weak, double expected)
    {
        var values = Enumerable.Repeat(52m, weak).Concat(Enumerable.Repeat(75m, 100 - weak)).ToArray();
        var result = Analyze(values);
        result.Score.Should().Be(expected);
        result.ScoringDecision!.Inputs["weakestFunctions"].Should().Be(20);
        result.ScoringDecision.Inputs["remainingFunctions"].Should().Be(80);
        result.ScoringDecision.Inputs["distributionStatisticsDisposition"].Should().Be("diagnosticOnly");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(40, 0)]
    [InlineData(46, 1)]
    [InlineData(52, 2)]
    [InlineData(55, 3)]
    [InlineData(58, 4)]
    [InlineData(65, 6)]
    [InlineData(70, 8)]
    [InlineData(72.5, 9)]
    [InlineData(75, 10)]
    [InlineData(100, 10)]
    public void SingleFunctionUsesInterpolatedLadder(double mi, double score)
    {
        var result = Analyze((decimal)mi);
        result.Score.Should().Be(score);
        result.ScoringDecision!.Inputs["singleFunctionScope"].Should().Be(true);
        result.ScoringDecision.Inputs["remainingMeanScore"].Should().BeNull();
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(100, 20)]
    public void GroupsAreDisjointAndWeightsSumToOne(int count, int weakest)
    {
        var result = Analyze(Enumerable.Repeat(70m, count).ToArray());
        result.Score.Should().Be(8);
        result.ScoringDecision!.Inputs["weakestFunctions"].Should().Be(weakest);
        var contributions = JsonSerializer.SerializeToElement(result.Extra["functionContributions"]).EnumerateArray().ToArray();
        contributions.Should().HaveCount(count);
        contributions.Select(item => item.GetProperty("sourceSpanStart").GetInt32()).Distinct().Should().HaveCount(count);
        contributions.Count(item => item.GetProperty("group").GetString() == "weakest").Should().Be(weakest);
        contributions.Sum(item => item.GetProperty("weightNumerator").GetDecimal() / item.GetProperty("weightDenominator").GetInt32())
            .Should().BeApproximately(1m, .000000000000000000000001m);
    }

    [Fact]
    public void DecimalTieRoundsUpOnce()
    {
        Analyze(70m, 70.625m).Score.Should().Be(8.2);
    }

    [Fact]
    public void WorseningAnyFunctionCannotImproveScoreIncludingAcrossGroupBoundary()
    {
        var values = new[] { 42m, 52m, 58m, 65m, 70m, 75m };
        decimal before = (decimal)Analyze(values).ScoringDecision!.Inputs["unroundedScore"]!;
        for (int index = 0; index < values.Length; index++)
        {
            var changed = values.ToArray();
            changed[index] -= 12;
            ((decimal)Analyze(changed).ScoringDecision!.Inputs["unroundedScore"]!).Should().BeLessThanOrEqualTo(before);
        }
    }

    [Fact]
    public void RepeatedFrameworkObservationsCountOnceAtLowestMi()
    {
        var result = MaintainabilityProbe.Analyze([Functions([75, 70], "net9"), Functions([52, 70], "net10")]);
        var decision = result.ScoringDecision!;
        decision.Inputs["eligibleFunctions"].Should().Be(2);
        decision.Inputs["repeatedObservations"].Should().Be(2);
        decision.Inputs["variantMiDifferences"].Should().Be(1);
        result.Score.Should().Be(5.6);
        MaintainabilityProbe.Analyze([Functions([52, 70])]).Score.Should().Be(result.Score);
    }

    [Fact]
    public void MissingIdentityRemainsSeparateAndMissingMeasurementFails()
    {
        var result = MaintainabilityProbe.Analyze([Functions([70], file: ""), Functions([70], file: "")]);
        result.ScoringDecision!.Inputs["eligibleFunctions"].Should().Be(2);
        result.ScoringDecision.Inputs["unidentifiedObservations"].Should().Be(2);
        var missing = new TypeMetrics
        {
            Project = "App",
            Type = "Missing",
            Namespace = "Tests",
            FilePath = "source.cs",
            ExecutableMetrics = new([new("Missing", "method", "source.cs", 1, 1)])
        };
        MaintainabilityProbe.Analyze([missing]).Status.Should().Be("failed");
        MaintainabilityProbe.Analyze([missing]).Score.Should().BeNull();
    }

    [Fact]
    public void NoFunctionsIsUnmeasured()
    {
        MaintainabilityProbe.Analyze([]).Status.Should().Be("skipped");
        Analyze().Score.Should().BeNull();
    }

    private static IReadOnlyList<TypeMetrics> Collect(string code)
    {
        var root = Path.GetTempPath();
        var compilation = RoslynTestHelper.CompileCodeAtPath(code, Path.Combine(root, "OwnMi.cs")).Compilation;
        return MetricsCollector.Collect("App", compilation, root).Types;
    }

    private static ExecutableFunctionMetrics[] Measured(string code) => Collect(code)
        .SelectMany(type => type.ExecutableMetrics!.MaintainabilityFunctions).ToArray();

    [Fact]
    public void EnumAndStorageDeclarationsGiveNoCredit()
    {
        const string method = "public int F(int x) { if (x > 0) return x; return 0; }";
        var plain = MaintainabilityProbe.Analyze(Collect("class C { " + method + " }"));
        var nested = MaintainabilityProbe.Analyze(Collect("class C { enum E { A, B } int field; const int Value = 8; " + method + " }"));
        var outside = MaintainabilityProbe.Analyze(Collect("enum E { A, B } class C { " + method + " }"));
        plain.Score.Should().Be(nested.Score).And.Be(outside.Score);
        nested.ScoringDecision!.Inputs["eligibleFunctions"].Should().Be(1);
        MaintainabilityProbe.Analyze(Collect("enum E { A, B } class C { int value; public int P { get; set; } }")).Score.Should().BeNull();
    }

    [Fact]
    public void MovingFunctionsBetweenTypesDoesNotChangeContributions()
    {
        const string first = "public int F(int x) { if (x > 0) return x; return 0; }";
        const string second = "public int G() => 4;";
        var together = Measured("class C { " + first + second + " }");
        var split = Measured("class C { " + first + " } class D { " + second + " }");
        together.Select(f => f.Maintainability).Should().Equal(split.Select(f => f.Maintainability));
    }

    [Theory]
    [InlineData("int Local(int x) { return x; }")]
    [InlineData("System.Func<int, int> callback = x => { return x; };")]
    public void NestedBodyDoesNotInflateEnclosingInputs(string nested)
    {
        var simple = Measured("class C { int Outer() { " + nested + " return 1; } }");
        var complex = Measured("class C { int Outer() { " + nested.Replace("return x;", "if (x > 1) {\n return x + 3;\n }\n return x * 2;") + " return 1; } }");
        var outerBefore = simple.Single(f => f.Name.Contains("Outer("));
        var outerAfter = complex.Single(f => f.Name.Contains("Outer("));
        outerBefore.Maintainability.Should().Be(outerAfter.Maintainability);
        outerBefore.OwnCyclomaticComplexity.Should().Be(outerAfter.OwnCyclomaticComplexity);
        simple.Should().HaveCount(2);
        complex.Should().HaveCount(2);
    }

    [Fact]
    public void RuntimeInitializersCountWithoutConstantOrLambdaWrapperCredit()
    {
        var functions = Measured("class C { int constant = 5; object value = new object(); System.Func<int> callback = () => 3; }");
        functions.Count(f => f.Kind == "initializer").Should().Be(1);
        functions.Count(f => f.Kind == "callback").Should().Be(1);
        functions.Should().HaveCount(2);
    }

    [Fact]
    public void CommentsAndSignaturesDoNotChangeOwnedBodyMeasurements()
    {
        var plain = Measured("class C { int F() { return 2; } }").Single();
        var documented = Measured("class C { public int LongerName() { // explanation\n\n return 2; /* reason */ } }").Single();
        plain.Maintainability.Should().Be(documented.Maintainability);
        plain.Maintainability!.SourceLines.Should().Be(1);
    }

    [Fact]
    public void ConstructorInitializerIsOwnedAndCompositionRootHasNoBonus()
    {
        const string code = "class C { public C(int value) {} public C(bool yes) : this(yes ? 1 : 2) {} }";
        var functions = Measured(code);
        var delegating = functions.Single(f => f.OwnCyclomaticComplexity == 2);
        delegating.Maintainability!.SourceLines.Should().Be(1);
        delegating.Maintainability.HalsteadVolume.Should().BeGreaterThan(0);
        var normal = MaintainabilityProbe.Analyze(Collect(code));
        var entry = MaintainabilityProbe.Analyze(Collect(code.Replace("C", "Program")));
        normal.Score.Should().Be(entry.Score);
        entry.ScoringDecision!.Inputs["entryPointMiAdjustment"].Should().Be(0);
    }
}
