using System.Text.Json;
using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class ArchitecturePopulationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public ArchitecturePopulationTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private static TypeMetrics Metric(int index, int coupling = 0, bool carrier = false) => new()
    {
        Project = "App",
        Namespace = "App",
        Type = "Type" + index,
        FilePath = $"Type{index}.cs",
        StructuralClassCoupling = coupling,
        IsDataCarrier = carrier
    };

    private DimensionResult Analyze(IEnumerable<TypeMetrics> metrics) => ArchitectureProbe.Analyze([], metrics.ToList(), directory);

    [Theory]
    [InlineData(1, 9, 10)]
    [InlineData(1, 10, 9.9)]
    [InlineData(1, 11, 9.7)]
    [InlineData(50, 10, 4)]
    [InlineData(100, 10, 4)]
    [InlineData(1, 30, 5.9)]
    [InlineData(100, 30, 0)]
    public void Coupling_AccountsForPrevalenceAndSeverity(int hotspots, int coupling, double score)
    {
        Analyze(Enumerable.Range(0, 100).Select(index => Metric(index, index < hotspots ? coupling : 0)))
            .Score.Should().Be(score);
    }

    [Fact]
    public void CycleCapRetainsCompleteMetricCensusAndStableDisplaySample()
    {
        File.WriteAllText(Path.Combine(directory, "A.csproj"),
            "<Project><ItemGroup><ProjectReference Include=\"B.csproj\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(directory, "B.csproj"),
            "<Project><ItemGroup><ProjectReference Include=\"A.csproj\" /></ItemGroup></Project>");
        var metrics = Enumerable.Range(0, 12).Select(index => new TypeMetrics
        {
            Project = "App",
            Namespace = "App",
            Type = $"Type{index:D2}",
            FilePath = $"Type{index:D2}.cs",
            StructuralClassCoupling = 11,
            CyclomaticComplexity = 80,
            DecompositionRatio = 8,
            LinesOfSource = 500
        }).ToArray();

        var result = Analyze(metrics);
        result.Score.Should().Be(0);
        result.Findings.Should().HaveCount(37);
        result.Findings[0].Category.Should().Be("projectCycle");
        result.Extra["hotspotCount"].Should().Be(36);
        result.Extra["hotspotsTruncated"].Should().Be(true);
        result.Basis.Should().Contain("Findings: 37 (errors: 1, warnings: 36)")
            .And.Contain("hotspots: 36 (showing 10)");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var sample = JsonSerializer.SerializeToElement(result.Extra["hotspots"], options).EnumerateArray().ToArray();
        sample.Select(item => item.GetProperty("type").GetString())
            .Should().Equal(Enumerable.Range(0, 10).Select(index => $"Type{index:D2}"));
        sample.Should().OnlyContain(item => item.GetProperty("category").GetString() == "highCyclomaticComplexity");
        var details = JsonSerializer.SerializeToElement(result.Extra["architectureMetrics"], options);
        details.GetProperty("graphLayeringReason").GetString().Should().Be("projectCycle");
        details.GetProperty("metricScore").GetDouble().Should().BeApproximately(3.8, 0.00001);
        result.ScoringDecision!.Steps.Single(step => step.Id == "graphLayeringCap").Disposition.Should().Be("selected");
    }

    [Fact]
    public void ReplicatingPopulation_PreservesScore()
    {
        var small = Analyze(Enumerable.Range(0, 10).Select(index => Metric(index, index == 0 ? 15 : 0)));
        var large = Analyze(Enumerable.Range(0, 100).Select(index => Metric(index, index % 10 == 0 ? 15 : 0)));
        small.Score.Should().Be(7.8);
        large.Score.Should().Be(small.Score);
    }

    [Fact]
    public void ExcludedCarriers_DoNotDilutePopulation_AndEvidenceExplainsArithmetic()
    {
        var metrics = new[] { Metric(0, 15) }.Concat(Enumerable.Range(1, 99).Select(index => Metric(index, carrier: true)));
        var result = Analyze(metrics);
        result.Score.Should().Be(3);
        var evidence = JsonSerializer.SerializeToElement(result.Extra["architectureMetrics"], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var coupling = evidence.GetProperty("components")[0];
        coupling.GetProperty("eligibleTypeCount").GetInt32().Should().Be(1);
        coupling.GetProperty("hotspotCount").GetInt32().Should().Be(1);
        coupling.GetProperty("populationPenalty").GetDouble().Should().Be(6);
        coupling.GetProperty("severityPenalty").GetDouble().Should().Be(1);
    }

    [Fact]
    public void OverlappingMetricHotspots_UseWorstComponentWithoutAddingDuplicatePenalties()
    {
        var metrics = Enumerable.Range(0, 100).Select(index => Metric(index, index == 0 ? 10 : 0)).ToArray();
        metrics[0].LinesOfSource = 500;
        metrics[0].CyclomaticComplexity = 80;
        metrics[0].DecompositionRatio = 8;
        var result = Analyze(metrics);
        result.Findings.Should().HaveCount(3);
        result.Score.Should().Be(9.9);
    }

    [Fact]
    public void Complexity_RequiresBothAbsoluteAndDensityThresholds()
    {
        var metric = Metric(0);
        metric.CyclomaticComplexity = 800;
        metric.DecompositionRatio = 7.9;
        Analyze([metric]).Score.Should().Be(10);
        metric.DecompositionRatio = 8;
        Analyze([metric]).Score.Should().Be(4);
    }

    [Fact]
    public void Controller_UsesItsOwnThreshold()
    {
        var controller = new TypeMetrics { Project = "App", Namespace = "App", Type = "HomeController", FilePath = "HomeController.cs", StructuralClassCoupling = 8, IsWebController = true };
        Analyze(new[] { controller }.Concat(Enumerable.Range(1, 99).Select(index => Metric(index)))).Score.Should().Be(9.9);
    }
}
