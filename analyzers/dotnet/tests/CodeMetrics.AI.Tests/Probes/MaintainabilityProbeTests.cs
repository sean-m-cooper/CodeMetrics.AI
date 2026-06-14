using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using FluentAssertions;
using System.Text.Json;

namespace CodeMetrics.AI.Tests.Probes;

public class MaintainabilityProbeTests
{
    private static TypeMetrics MakeType(
        string type = "T",
        int mi = 85,
        int memberCount = 5,
        int classCc = 3,
        int coupling = 2,
        int loc = 50) =>
        new()
        {
            Project = "Proj",
            Namespace = "NS",
            Type = type,
            FilePath = "file.cs",
            MaintainabilityIndex = mi,
            MemberCount = memberCount,
            CyclomaticComplexity = classCc,
            ClassCoupling = coupling,
            LinesOfSource = loc
        };

    [Fact]
    public void EmptyInput_ReturnsScore10()
    {
        var result = MaintainabilityProbe.Analyze([]);

        result.Status.Should().Be("scored");
        result.Score.Should().Be(10);
        result.Basis.Should().Be("No types found.");
    }

    [Fact]
    public void AllTypesWithHighMI_ReturnsScore10()
    {
        // All types with MI > 80 → all signals score 10
        var types = Enumerable.Range(1, 20)
            .Select(i => MakeType($"T{i}", mi: 85 + (i % 10)))
            .ToList();

        var result = MaintainabilityProbe.Analyze(types);

        result.Status.Should().Be("scored");
        result.Score.Should().Be(10.0);
    }

    [Fact]
    public void ManyLowMIValues_ProducesLowerScore()
    {
        // 30% below 60, 10% below 40 → lower signals
        var types = Enumerable.Range(1, 70)
            .Select(i => MakeType($"High{i}", mi: 80))
            .Concat(Enumerable.Range(1, 20)
                .Select(i => MakeType($"Mid{i}", mi: 50)))  // MI 50 < 60
            .Concat(Enumerable.Range(1, 10)
                .Select(i => MakeType($"Low{i}", mi: 30)))   // MI 30 < 40
            .ToList();

        var result = MaintainabilityProbe.Analyze(types);

        result.Score.Should().BeLessThan(10.0);
    }

    [Fact]
    public void PopBelow60_CorrectScore()
    {
        // 2% below 60 → threshold [1, 3, 6, 10, 15] → score 8
        var types = Enumerable.Range(1, 98)
            .Select(i => MakeType($"T{i}", mi: 80))
            .Concat(new[]
            {
                MakeType("LowA", mi: 55),
                MakeType("LowB", mi: 50)
            })
            .ToList();

        var result = MaintainabilityProbe.Analyze(types);

        result.Extra.Should().ContainKey("metrics");
        var metricsEl = (JsonElement)result.Extra["metrics"]!;
        var miEl = metricsEl.GetProperty("maintainabilityIndex");
        miEl.GetProperty("populationPercentBelow60Score").GetInt32().Should().Be(8);
    }

    [Fact]
    public void TopOffenders_SortedByMIAsc()
    {
        var types = new List<TypeMetrics>
        {
            MakeType("TypeC", mi: 75),
            MakeType("TypeA", mi: 30),
            MakeType("TypeB", mi: 55),
            MakeType("TypeD", mi: 85),
            MakeType("TypeE", mi: 45),
            MakeType("TypeF", mi: 20),
        };

        var result = MaintainabilityProbe.Analyze(types);

        var offenders = (JsonElement)result.Extra["topOffenders"]!;
        // First offender should have lowest MI
        var firstMI = offenders[0].GetProperty("mi").GetInt32();
        firstMI.Should().Be(20); // TypeF has MI 20
    }

    [Fact]
    public void TopOffenders_ContainsAtMost5()
    {
        var types = Enumerable.Range(1, 20)
            .Select(i => MakeType($"T{i}", mi: i * 4))
            .ToList();

        var result = MaintainabilityProbe.Analyze(types);

        var offenders = (JsonElement)result.Extra["topOffenders"]!;
        offenders.GetArrayLength().Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public void TopOffenders_ExcludesProgramAndStartup()
    {
        var program = MakeType("Program", mi: 5);
        program = new TypeMetrics
        {
            Project = program.Project,
            Namespace = program.Namespace,
            Type = program.Type,
            FilePath = "Program.cs",
            MaintainabilityIndex = program.MaintainabilityIndex,
            MemberCount = program.MemberCount,
            CyclomaticComplexity = program.CyclomaticComplexity,
            ClassCoupling = program.ClassCoupling,
            LinesOfSource = program.LinesOfSource
        };

        var startup = MakeType("Startup", mi: 10);
        startup = new TypeMetrics
        {
            Project = startup.Project,
            Namespace = startup.Namespace,
            Type = startup.Type,
            FilePath = "Startup.cs",
            MaintainabilityIndex = startup.MaintainabilityIndex,
            MemberCount = startup.MemberCount,
            CyclomaticComplexity = startup.CyclomaticComplexity,
            ClassCoupling = startup.ClassCoupling,
            LinesOfSource = startup.LinesOfSource
        };

        var types = new List<TypeMetrics>
        {
            program,
            startup,
            MakeType("RealHotspot", mi: 20),
            MakeType("Healthy", mi: 90)
        };

        var result = MaintainabilityProbe.Analyze(types);

        var offenders = (JsonElement)result.Extra["topOffenders"]!;
        var offenderTypes = offenders.EnumerateArray()
            .Select(o => o.GetProperty("type").GetString())
            .ToList();

        offenderTypes.Should().NotContain("Program");
        offenderTypes.Should().NotContain("Startup");
        offenderTypes.Should().Contain("RealHotspot");
    }

    [Fact]
    public void Score_HasMetricsAndTopOffendersInExtra()
    {
        var types = new List<TypeMetrics> { MakeType("TypeA", mi: 75) };

        var result = MaintainabilityProbe.Analyze(types);

        result.Extra.Should().ContainKey("metrics");
        result.Extra.Should().ContainKey("topOffenders");
    }

    [Fact]
    public void SingleType_WithMI90_Score10()
    {
        var types = new List<TypeMetrics> { MakeType("OnlyType", mi: 90) };

        var result = MaintainabilityProbe.Analyze(types);

        result.Score.Should().Be(10.0);
    }

    [Fact]
    public void P10MI_BelowLowestThreshold_Score0()
    {
        // All types with MI 40 → p10 = 40, thresholds [75, 70, 65, 58, 52], 40 < 52 → 0
        var types = Enumerable.Range(1, 50)
            .Select(i => MakeType($"T{i}", mi: 40))
            .ToList();

        var result = MaintainabilityProbe.Analyze(types);

        result.Extra.Should().ContainKey("metrics");
        var metricsEl = (JsonElement)result.Extra["metrics"]!;
        var p10Score = metricsEl.GetProperty("maintainabilityIndex").GetProperty("p10Score").GetInt32();
        p10Score.Should().Be(0);
    }

    [Fact]
    public void P10MI_AboveTopThreshold_Score10()
    {
        // All types with MI 90 → p10 = 90 ≥ 75 → score 10
        var types = Enumerable.Range(1, 50)
            .Select(i => MakeType($"T{i}", mi: 90))
            .ToList();

        var result = MaintainabilityProbe.Analyze(types);

        var metricsEl = (JsonElement)result.Extra["metrics"]!;
        var p10Score = metricsEl.GetProperty("maintainabilityIndex").GetProperty("p10Score").GetInt32();
        p10Score.Should().Be(10);
    }

    [Fact]
    public void ExtremeBelow40_CorrectScoreWhenZero()
    {
        // No types below 40 → 0% → threshold [0.2, 0.5, 1.2, 2.5, 4.0] → 0% ≤ 0.2 → score 10
        var types = Enumerable.Range(1, 50)
            .Select(i => MakeType($"T{i}", mi: 80))
            .ToList();

        var result = MaintainabilityProbe.Analyze(types);

        var metricsEl = (JsonElement)result.Extra["metrics"]!;
        var extremeScore = metricsEl.GetProperty("maintainabilityIndex").GetProperty("extremePercentBelow40Score").GetInt32();
        extremeScore.Should().Be(10);
    }

    [Fact]
    public void OffenderFields_ContainExpectedProperties()
    {
        var types = new List<TypeMetrics>
        {
            new()
            {
                Project = "MyProj",
                Namespace = "MyNS",
                Type = "MyType",
                FilePath = "file.cs",
                MaintainabilityIndex = 45,
                MemberCount = 8,
                CyclomaticComplexity = 12,
                ClassCoupling = 5,
                LinesOfSource = 200
            }
        };

        var result = MaintainabilityProbe.Analyze(types);

        var offenders = (JsonElement)result.Extra["topOffenders"]!;
        var first = offenders[0];
        first.GetProperty("project").GetString().Should().Be("MyProj");
        first.GetProperty("namespace").GetString().Should().Be("MyNS");
        first.GetProperty("type").GetString().Should().Be("MyType");
        first.GetProperty("mi").GetInt32().Should().Be(45);
        first.GetProperty("classCc").GetInt32().Should().Be(12);
        first.GetProperty("memberCount").GetInt32().Should().Be(8);
        first.GetProperty("coupling").GetInt32().Should().Be(5);
        first.GetProperty("loc").GetInt32().Should().Be(200);
    }
}
