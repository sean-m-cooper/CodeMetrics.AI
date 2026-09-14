using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public class CatchContextTests
{
    private static DimensionResult Analyze(string code, int frameworks = 1)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath("using System; " + code, Path.Combine(Path.GetTempPath(), "CodeMetricsContext", "CatchContext.cs"));
        compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        return ErrorHandlingProbe.Analyze(Enumerable.Range(0, frameworks).Select(i => ($"P{i}", compilation)).ToList());
    }

    [Theory]
    [InlineData("// Best-effort cleanup; stale files are collected later.", false)]
    [InlineData("/* The upload may already be completed or aborted. */", false)]
    [InlineData("// TODO: implement handling", true)]
    [InlineData("// logger.LogError(ex, \"failure\");", true)]
    [InlineData("//", true)]
    public void LocalRationaleIsAcceptedWithoutJudgingBusinessDecision(string comment, bool found)
    {
        var result = Analyze("class C { void M() { try {} catch (Exception ex) { " + comment + "\n } } }");
        result.Findings.Any(f => f.Category == "emptyCatch").Should().Be(found);
    }

    [Fact]
    public void RationaleDoesNotExcuseThrowEx()
    {
        Analyze("class C { void M() { try {} catch (Exception ex) { /* Cleanup is best effort. */ throw ex; } } }")
            .Findings.Should().ContainSingle(f => f.Category == "throwEx");
    }

    [Fact]
    public void PrecedingCancellationRethrowDoesNotExplainAnEmptyCatch()
    {
        Analyze("class C { void M() { try {} catch (OperationCanceledException) { throw; } catch {} } }")
            .Findings.Should().ContainSingle(f => f.Category == "emptyCatch");
    }

    [Fact]
    public void DeferredCommentDoesNotExplainEnclosingCatch()
    {
        Analyze("class C { object M() { try { return new object(); } catch { Action a = () => { /* Best-effort cleanup. */ }; return null; } } }")
            .Findings.Should().Contain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void TrailingCatchCommentExplainsIntent()
    {
        Analyze("class C { void M() { try {} catch {} // Best-effort cleanup.\n } }")
            .Findings.Should().NotContain(f => f.Category == "emptyCatch");
    }

    [Fact]
    public void CommentInTryDoesNotExplainAnEmptyCatch()
    {
        Analyze("class C { void M() { try { /* Best-effort cleanup. */ } catch {} } }")
            .Findings.Should().ContainSingle(f => f.Category == "emptyCatch");
    }

    [Theory]
    [InlineData("error = \"Invalid configuration.\"; return null;", false)]
    [InlineData("error = \"Unexpected error: \" + ex.Message; return null;", false)]
    [InlineData("error = null; return null;", true)]
    [InlineData("error = \"Invalid.\"; error = null; return null;", true)]
    [InlineData("Action a = () => errorCallback(\"Invalid.\"); return null;", true)]
    public void ErrorOutputMustBeAnActiveDiagnostic(string body, bool found)
    {
        var result = Analyze("class C { object M(out string error, Action<string> errorCallback) { error = null; try { return new object(); } catch (Exception ex) { " + body + " } } }");
        result.Findings.Any(f => f.Category == "broadCatchReturnsDefault").Should().Be(found);
    }

    [Fact]
    public void DiagnosticCollectionIsAnErrorOutput()
    {
        Analyze("""
            class C { bool M(out string[] messages) { messages = []; try { return true; }
                catch (Exception ex) { messages = ["Unexpected error: " + ex.Message]; return false; } } }
            """).Findings.Should().NotContain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Theory]
    [InlineData(2, 2, 0)]
    [InlineData(2, 100, 9)]
    [InlineData(20, 100, 8)]
    [InlineData(50, 100, 5)]
    [InlineData(100, 100, 0)]
    [InlineData(0, 100, 10)]
    public void EmptyCatchImpactDependsOnSourcePopulation(int bad, int total, double score)
    {
        var methods = Enumerable.Range(0, total).Select(i => $"void M{i}() {{ try {{}} catch {{ {(i < bad ? "" : "throw;")} }} }}");
        var result = Analyze("class C { " + string.Join(" ", methods) + " }", frameworks: 3);
        result.Score.Should().Be(score);
        result.ScoringDecision!.Inputs["catchPopulation"].Should().Be(total);
        result.ScoringDecision.Inputs["affectedCatches"].Should().Be(bad);
        result.ScoringDecision.Inputs["catchProjectFrameworkObservations"].Should().Be(total * 3);
    }

    [Fact]
    public void DefaultAndBroadWarningScoreTheSameCatchOnce()
    {
        var result = Analyze("class C { object M() { try { return new object(); } catch { return null; } } }");
        result.Findings.Should().HaveCount(2);
        result.ScoringDecision!.Inputs["affectedCatches"].Should().Be(1);
        result.ScoringDecision.Inputs["weightedAffectedCatches"].Should().Be(0.75m);
        result.Score.Should().Be(2.5);
    }
}
