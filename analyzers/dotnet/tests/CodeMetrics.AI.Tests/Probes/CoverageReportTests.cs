using CodeMetrics.AI.Probes;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public class CoverageReportTests
{
    [Fact]
    public void MissingBranchRate_RemainsUnknown_AndIncludesContentHash()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "<coverage line-rate='0.8' />");
            var report = CoverageReport.Read(path, [], Path.GetTempPath());
            report.LineRate.Should().Be(0.8);
            report.BranchRate.Should().BeNull();
            report.Sha256.Should().HaveLength(64);
            report.Status.Should().Be("aggregateUnverified");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MatchesProductionFiles_AndDoesNotUseUnrelatedAggregateRates()
    {
        var path = Path.GetTempFileName();
        var root = Path.GetTempPath();
        try
        {
            File.WriteAllText(path, """
                <coverage line-rate="1" branch-rate="1"><packages><package><classes>
                <class filename="Production.cs"><lines><line number="1" hits="1"/><line number="2" hits="0"/></lines></class>
                <class filename="Other.cs"><lines><line number="1" hits="10"/></lines></class>
                </classes></package></packages></coverage>
                """);
            var report = CoverageReport.Read(path, [Path.Combine(root, "Production.cs")], root);
            report.LineRate.Should().Be(0.5);
            report.BranchRate.Should().BeNull();
            report.MatchedFiles.Should().ContainSingle().Which.Should().Be("Production.cs");
            report.UnmatchedFiles.Should().ContainSingle().Which.Should().Be("Other.cs");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExplicitMissingCoverage_FailsInsteadOfInventingAScore()
    {
        var result = TestingProbe.Analyze([], [], Path.GetTempPath(), Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml"));
        result.Status.Should().Be("failed");
        result.Score.Should().BeNull();
    }
}
