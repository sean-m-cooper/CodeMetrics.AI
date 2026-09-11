using CodeMetrics.AI.Probes;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class ScoringDecisionTests
{
    [Fact]
    public void DeprecationSelectsFourWithoutAddingOutdatedDeductions()
    {
        var directory = Directory.CreateTempSubdirectory("decision-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Directory.Packages.props"), "<Project/>");
            const string empty = "{\"version\":1,\"projects\":[]}";
            static string Report(string rows) => $$"""
                {"version":1,"projects":[{"path":"App.csproj","frameworks":[{"framework":"net10.0","topLevelPackages":[{{rows}}]}]}]}
                """;
            var deprecated = Report("""{"id":"Legacy.Tests","resolvedVersion":"2.0.0","deprecationReasons":["Legacy"]}""");
            var outdated = Report("""{"id":"Web.OpenApi","resolvedVersion":"9.0.0","latestVersion":"10.0.0"},{"id":"Data.Client","resolvedVersion":"1.0.0","latestVersion":"2.0.0"}""");
            var result = DependencyProbe.AnalyzeOutput(empty, outdated, deprecated, directory, false);
            var decision = result.ScoringDecision!;
            decision.FinalScore.Should().Be(4).And.Be(result.Score);
            decision.Steps.Single(step => step.Disposition == "selected").Id.Should().Be("deprecatedPackage");
            decision.Steps.Single(step => step.Id == "remainingMaintenance").Disposition.Should().Be("shadowed");
            for (var i = 0; i < result.Findings.Count; i++) result.Findings[i].Fingerprint = i.ToString("x64");
            decision.AttachFindings(result.Findings);
            decision.FindingEffects.Where(effect => effect.Category == "deprecatedDependency").Should().OnlyContain(effect => effect.Effect == "policyInput");
            decision.FindingEffects.Where(effect => effect.Category == "outdatedDependency").Should().HaveCount(2)
                .And.OnlyContain(effect => effect.Effect == "noAdditionalReduction");
            var excluded = result.Findings.First(finding => finding.Category == "outdatedDependency");
            excluded.Observations["scoreDisposition"] = "excludedFrameworkIncompatible";
            decision.AttachFindings(result.Findings);
            decision.FindingEffects.Single(effect => effect.Fingerprint == excluded.Fingerprint).Effect.Should().Be("excluded");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(4, 6, "selected", "notLimiting")]
    [InlineData(6, 4, "notLimiting", "selected")]
    [InlineData(4, 4, "selected", "selected")]
    public void MinimumPreservesBindingCapsAndAllTies(double component, double cap, string componentStatus, string capStatus)
    {
        var decision = ScoringDecision.Minimum("test", 1, MidpointRounding.ToEven,
            ScoringStep.Component("signals", component), ScoringStep.Component("coverage", cap, kind: "cap"));
        decision.FinalScore.Should().Be(Math.Min(component, cap));
        decision.Steps[0].Disposition.Should().Be(componentStatus);
        decision.Steps[1].Disposition.Should().Be(capStatus);
    }

    [Fact]
    public void NonbindingParentDoesNotAttributeNestedFindingToFinalScore()
    {
        var nested = ScoringDecision.FirstMatch("nested", [], ScoringStep.Rule("warning", "warnings > 0", true, 8, "warning"));
        var decision = ScoringDecision.Minimum("test", 1, MidpointRounding.ToEven,
            ScoringStep.Component("signals", 4), ScoringStep.Component("graph", 8, decision: nested, kind: "cap"));
        decision.AttachFindings([new Finding { Category = "warning", Severity = "warning", Message = "Review", Fingerprint = new string('a', 64) }]);
        decision.FindingEffects.Single().Effect.Should().Be("noAdditionalReduction");
        decision.FindingEffects.Single().StepIds.Should().Equal("warning");
    }

    [Fact]
    public void NestedMeansPreserveIntermediateRounding()
    {
        var inner = ScoringDecision.Mean("inner", ScoringStep.Component("one", 10), ScoringStep.Component("two", 8), ScoringStep.Component("three", 4));
        var outer = ScoringDecision.Mean("outer", ScoringStep.Component("inner", inner.FinalScore, decision: inner), ScoringStep.Component("other", 9.3));
        inner.FinalScore.Should().Be(7.3);
        outer.FinalScore.Should().Be(8.3);
    }
}
