using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class ErrorHandlingPropagationTests
{
    private static DimensionResult Analyze(string body, string returnType = "object", bool async = false)
    {
        var code = $$"""
            using System;
            using System.Threading.Tasks;
            class Outcome {
                public Exception Error { get; set; }
                public Outcome() { }
                public Outcome(Exception error) { Error = error; }
                public static Outcome Failure(Exception error) => new Outcome(error);
            }
            class C {
                void Report(Exception error) { }
                void LogError(Exception error) { }
                {{(async ? "async Task<" + returnType + ">" : returnType)}} Run(
                    Action<Exception> onError, Func<Exception, Task> onErrorAsync, Exception other) {
                    try { throw new InvalidOperationException(); }
                    catch (Exception ex) { {{body}} }
                }
            }
            """;
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        return ErrorHandlingProbe.Analyze([("Library(net10.0)", compilation)]);
    }

    [Theory]
    [InlineData("return Outcome.Failure(ex);")]
    [InlineData("return new Outcome(ex);")]
    [InlineData("return new Outcome { Error = ex };")]
    [InlineData("return ex;")]
    [InlineData("return (false, ex);")]
    [InlineData("return Outcome.Failure((Exception)(ex!));")]
    [InlineData("var result = Outcome.Failure(ex); return result;")]
    [InlineData("var first = Outcome.Failure(ex); var second = Outcome.Failure(ex); first = new Outcome(); return second;")]
    [InlineData("var result = Outcome.Failure(ex); if (other != null) return result; result = new Outcome(); return result;")]
    [InlineData("onError(ex); return null;")]
    [InlineData("onError.Invoke(ex); return null;")]
    [InlineData("onError?.Invoke(ex); return null;")]
    public void ExceptionBearingReturnsAndInvokedCallbacks_AreHandlingPaths(string body)
    {
        var result = Analyze(body);
        result.Findings.Should().BeEmpty();
        result.Score.Should().Be(10);
        result.ScoringDecision!.Inputs["handlingRecognition"].Should().Be("documented-intent-error-output-v2");
    }

    [Fact]
    public void TargetTypedCreation_IsAHandlingPath()
    {
        Analyze("return new(ex);", "Outcome").Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("await onErrorAsync(ex); return null;")]
    [InlineData("await onErrorAsync(ex).ConfigureAwait(false); return null;")]
    [InlineData("return await Task.FromResult(Outcome.Failure(ex));")]
    public void AwaitedPropagation_IsAHandlingPath(string body)
    {
        Analyze(body, async: true).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("return null;")]
    [InlineData("Outcome.Failure(ex); return null;")]
    [InlineData("onError(other); return null;")]
    [InlineData("onError(new Exception()); return null;")]
    [InlineData("onErrorAsync(ex); return null;")]
    [InlineData("Action callback = () => onError(ex); return null;")]
    [InlineData("void Callback() { onError(ex); } return null;")]
    [InlineData("Func<object> callback = () => Outcome.Failure(ex); return null;")]
    [InlineData("object Callback() { return Outcome.Failure(ex); } return null;")]
    [InlineData("Action<Exception> callback = ex => onError(ex); return null;")]
    [InlineData("Report(ex); return null;")]
    [InlineData("ex = other; onError(ex); return null;")]
    [InlineData("ex = other; return null;")]
    [InlineData("Action LogError = () => onError(ex); return null;")]
    [InlineData("Action callback = () => LogError(ex); return null;")]
    [InlineData("void LogError() { Report(ex); } return null;")]
    [InlineData("void Callback() { throw ex; } return null;")]
    public void DiscardedOrDeferredHandling_DoesNotHideDefaultReturns(string body)
    {
        var result = Analyze(body);
        result.Findings.Should().Contain(finding => finding.Category == "broadCatchWithoutLoggingOrRethrow");
        result.Findings.Should().Contain(finding => finding.Category == "broadCatchReturnsDefault");
        result.Score.Should().BeLessThanOrEqualTo(4);
    }

    [Theory]
    [InlineData("return Outcome.Failure(other);")]
    [InlineData("return Outcome.Failure(new Exception());")]
    [InlineData("return ex.Message.Length;")]
    [InlineData("return ex == null ? new object() : new object();")]
    [InlineData("return nameof(ex);")]
    [InlineData("ex = other; return Outcome.Failure(ex);")]
    [InlineData("var result = Outcome.Failure(ex); result = new Outcome(); return result;")]
    [InlineData("var first = Outcome.Failure(ex); var second = Outcome.Failure(ex); first = new Outcome(); second = new Outcome(); return second;")]
    public void MerelyReferencingOrReplacingAnException_IsNotPropagation(string body)
    {
        Analyze(body).Findings.Should().Contain(finding => finding.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Theory]
    [InlineData("", "outcome", false)]
    [InlineData("", "outcome.WithContext()", false)]
    [InlineData("", "outcome.ToString()", true)]
    [InlineData("outcome = new Outcome(null);", "outcome", true)]
    [InlineData("Replace(ref outcome);", "outcome", true)]
    [InlineData("Reset(out outcome);", "outcome", true)]
    [InlineData("Action replace = () => outcome = new Outcome(null);", "outcome", true)]
    [InlineData("var unrelated = new Outcome(null); Replace(ref unrelated);", "outcome", false)]
    [InlineData("(outcome, _) = (new Outcome(null), 1);", "outcome", true)]
    public void StoredOutcomeReturnedAfterCleanup_RequiresAnUnchangedLocal(
        string cleanup, string returned, bool expectedFinding)
    {
        var code = $$"""
            using System;
            class Outcome {
                public Outcome(Exception error) { }
                public Outcome WithContext() => this;
            }
            class C {
                void Replace(ref Outcome value) { value = new Outcome(null); }
                void Reset(out Outcome value) { value = new Outcome(null); }
                object Run() {
                    Outcome outcome;
                    try { outcome = new Outcome(null); }
                    catch (Exception ex) { outcome = new(ex); }
                    {{cleanup}}
                    return {{returned}};
                }
            }
            """;
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        var result = ErrorHandlingProbe.Analyze([("Library(net10.0)", compilation)]);
        result.Findings.Any(finding => finding.Category == "broadCatchWithoutLoggingOrRethrow")
            .Should().Be(expectedFinding);
    }

    [Theory]
    [InlineData("onError?.Invoke(ex);", false)]
    [InlineData("Action callback = () => onError(ex);", true)]
    [InlineData("void Callback() { onError(ex); }", true)]
    [InlineData("onError(new Exception());", true)]
    public void MissingLoggerAdvisory_RespectsActualExceptionCallbacks(string body, bool expectedFinding)
    {
        var code = $$"""
            using System;
            class C {
                void Run(Action<Exception> onError) {
                    try { }
                    catch (ArgumentException ex) { {{body}} }
                    catch (InvalidOperationException ex) { {{body}} }
                }
            }
            """;
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        var result = ErrorHandlingProbe.Analyze([("Library(net10.0)", compilation)]);
        result.Findings.Any(finding => finding.Category == "missingLoggerForMultipleCatches")
            .Should().Be(expectedFinding);
    }
}
