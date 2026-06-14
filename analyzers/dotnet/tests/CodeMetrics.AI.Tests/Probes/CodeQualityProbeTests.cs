using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using FluentAssertions;
using System.Text.Json;

namespace CodeMetrics.AI.Tests.Probes;

public class CodeQualityProbeTests
{
    private static TypeMetrics MakeType(
        string type = "T",
        int memberCount = 5,
        double decompositionRatio = 1.0,
        int maxMemberCC = 3,
        int mi = 80,
        int classCc = 5,
        int coupling = 2,
        int loc = 50) =>
        new()
        {
            Project = "Proj",
            Namespace = "NS",
            Type = type,
            FilePath = "file.cs",
            MemberCount = memberCount,
            DecompositionRatio = decompositionRatio,
            MaxMemberCyclomaticComplexity = maxMemberCC,
            MaintainabilityIndex = mi,
            CyclomaticComplexity = classCc,
            ClassCoupling = coupling,
            LinesOfSource = loc
        };

    [Fact]
    public void EmptyInput_ReturnsScore10()
    {
        var result = CodeQualityProbe.Analyze([]);

        result.Status.Should().Be("scored");
        result.Score.Should().Be(10);
        result.Basis.Should().Be("No types with members.");
    }

    [Fact]
    public void AllTypesWithZeroMembers_ReturnsScore10()
    {
        var types = new List<TypeMetrics>
        {
            MakeType("A", memberCount: 0),
            MakeType("B", memberCount: 0),
        };

        var result = CodeQualityProbe.Analyze(types);

        result.Status.Should().Be("scored");
        result.Score.Should().Be(10);
        result.Basis.Should().Be("No types with members.");
    }

    [Fact]
    public void AllLowDecompAndLowCC_ReturnsScore10()
    {
        // 100 types with low ratio and low CC → all signals score 10
        var types = Enumerable.Range(1, 100)
            .Select(i => MakeType($"T{i}", memberCount: 5, decompositionRatio: 1.0, maxMemberCC: 2))
            .ToList();

        var result = CodeQualityProbe.Analyze(types);

        result.Status.Should().Be("scored");
        result.Score.Should().Be(10.0);
    }

    [Fact]
    public void TwoPercentOver4DecompRatio_PopOver4Score8()
    {
        // 100 types, 2 with ratio > 4 → 2% → threshold [1, 3, 6, 10, 15] → score 8
        var types = Enumerable.Range(1, 98)
            .Select(i => MakeType($"T{i}", memberCount: 5, decompositionRatio: 1.0, maxMemberCC: 2))
            .Concat(new[]
            {
                MakeType("HighA", memberCount: 5, decompositionRatio: 5.0, maxMemberCC: 2),
                MakeType("HighB", memberCount: 5, decompositionRatio: 6.0, maxMemberCC: 2)
            })
            .ToList();

        var result = CodeQualityProbe.Analyze(types);

        // 2% is > 1 (t10) but <= 3 (t8) → score 8 for popOver4
        result.Score.Should().BeGreaterThan(0);
        result.Status.Should().Be("scored");

        // Verify the metrics extra contains the decomposition info
        result.Extra.Should().ContainKey("metrics");
        var metricsEl = (JsonElement)result.Extra["metrics"]!;
        var decompEl = metricsEl.GetProperty("decomposition");
        decompEl.GetProperty("populationPercentOver4Score").GetInt32().Should().Be(8);
    }

    [Fact]
    public void HighDecompAndHighCC_ProducesLowerScore()
    {
        // 20% of types have ratio > 4 and high CC
        var types = Enumerable.Range(1, 80)
            .Select(i => MakeType($"Low{i}", memberCount: 5, decompositionRatio: 1.0, maxMemberCC: 2))
            .Concat(Enumerable.Range(1, 20)
                .Select(i => MakeType($"High{i}", memberCount: 5, decompositionRatio: 8.0, maxMemberCC: 20)))
            .ToList();

        var result = CodeQualityProbe.Analyze(types);

        result.Score.Should().BeLessThan(10.0);
    }

    [Fact]
    public void TopOffenders_ContainsAtMost5()
    {
        var types = Enumerable.Range(1, 10)
            .Select(i => MakeType($"T{i}", memberCount: 5, decompositionRatio: i * 2.0, maxMemberCC: i))
            .ToList();

        var result = CodeQualityProbe.Analyze(types);

        result.Extra.Should().ContainKey("topOffenders");
        var offenders = (JsonElement)result.Extra["topOffenders"]!;
        offenders.GetArrayLength().Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public void TopOffenders_SortedByDecompRatioDesc()
    {
        var types = Enumerable.Range(1, 10)
            .Select(i => MakeType($"T{i}", memberCount: 5, decompositionRatio: i * 1.0, maxMemberCC: 2))
            .ToList();

        var result = CodeQualityProbe.Analyze(types);

        var offenders = (JsonElement)result.Extra["topOffenders"]!;
        // First offender should have highest decompositionRatio
        var firstRatio = offenders[0].GetProperty("decompositionRatio").GetDouble();
        firstRatio.Should().Be(10.0); // T10 has ratio 10
    }

    [Fact]
    public void SingleMemberType_IsExcludedFromDecompositionMetricsAndOffenders()
    {
        var types = new List<TypeMetrics>
        {
            MakeType("SingleBigMethod", memberCount: 1, decompositionRatio: 20.0, maxMemberCC: 2),
            MakeType("CleanA", memberCount: 4, decompositionRatio: 1.0, maxMemberCC: 2),
            MakeType("CleanB", memberCount: 4, decompositionRatio: 1.0, maxMemberCC: 2)
        };

        var result = CodeQualityProbe.Analyze(types);

        var metrics = (JsonElement)result.Extra["metrics"]!;
        var decomposition = metrics.GetProperty("decomposition");
        decomposition.GetProperty("populationPercentOver4").GetDouble().Should().Be(0);
        decomposition.GetProperty("extremePercentOver15").GetDouble().Should().Be(0);

        var offenders = (JsonElement)result.Extra["topOffenders"]!;
        offenders.EnumerateArray()
            .Select(o => o.GetProperty("type").GetString())
            .Should().NotContain("SingleBigMethod");
    }

    [Fact]
    public void Score_HasMetricsAndTopOffendersInExtra()
    {
        var types = new List<TypeMetrics> { MakeType("TypeA", memberCount: 3) };

        var result = CodeQualityProbe.Analyze(types);

        result.Extra.Should().ContainKey("metrics");
        result.Extra.Should().ContainKey("topOffenders");
    }

    [Fact]
    public void ScoreThreshold_ValuesAtBoundaries()
    {
        // value exactly at t10 boundary → 10
        CodeQualityProbe.ScoreThreshold(1.0, [1, 3, 6, 10, 15]).Should().Be(10);
        // value just above t10 but at t8 → 8
        CodeQualityProbe.ScoreThreshold(1.1, [1, 3, 6, 10, 15]).Should().Be(8);
        // value exactly at t8 → 8
        CodeQualityProbe.ScoreThreshold(3.0, [1, 3, 6, 10, 15]).Should().Be(8);
        // value exactly at t2 → 2
        CodeQualityProbe.ScoreThreshold(15.0, [1, 3, 6, 10, 15]).Should().Be(2);
        // value above t2 → 0
        CodeQualityProbe.ScoreThreshold(20.0, [1, 3, 6, 10, 15]).Should().Be(0);
    }

    [Fact]
    public void Percentile_P90_WithKnownValues()
    {
        // sorted: 1,2,3,4,5,6,7,8,9,10 → p90 = index 8.1 → 9 + 0.1*(10-9) = 9.1
        var values = Enumerable.Range(1, 10).Select(i => (double)i).ToList();
        var p90 = CodeQualityProbe.Percentile(values, 90);
        p90.Should().BeApproximately(9.1, 0.001);
    }

    [Fact]
    public void Percentile_SingleValue_ReturnsThatValue()
    {
        var values = new List<double> { 42.0 };
        CodeQualityProbe.Percentile(values, 90).Should().Be(42.0);
    }

    [Fact]
    public void Percentile_EmptyList_ReturnsZero()
    {
        var values = new List<double>();
        CodeQualityProbe.Percentile(values, 90).Should().Be(0.0);
    }

    [Fact]
    public void ExtremeHighCC_ProducesLowScore()
    {
        // Types with extreme CC (> 30 for many)
        var types = Enumerable.Range(1, 50)
            .Select(i => MakeType($"Low{i}", memberCount: 5, decompositionRatio: 1.0, maxMemberCC: 2))
            .Concat(Enumerable.Range(1, 50)
                .Select(i => MakeType($"High{i}", memberCount: 5, decompositionRatio: 1.0, maxMemberCC: 35)))
            .ToList();

        var result = CodeQualityProbe.Analyze(types);

        // 50% have CC > 15 → score 0 for that signal
        // 50% have CC > 30 → extreme score 0
        result.Score.Should().BeLessThan(6.0);
    }
}
