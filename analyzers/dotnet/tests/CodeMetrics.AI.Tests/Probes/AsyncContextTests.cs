using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public class AsyncContextTests
{
    private static Compilation Compile(string code)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(
            "using System; using System.Threading; using System.Threading.Tasks;\n" + code,
            Path.Combine(Path.GetTempPath(), "AsyncContext", "Source.cs"));
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return compilation;
    }

    private static DimensionResult Analyze(string code) => PerformanceAsyncProbe.Analyze([("Fixture", Compile(code))]);

    [Theory]
    [InlineData("Task<int>", "IsCompleted")]
    [InlineData("Task<int>", "IsCompletedSuccessfully")]
    [InlineData("ValueTask<int>", "IsCompleted")]
    [InlineData("ValueTask<int>", "IsCompletedSuccessfully")]
    public void CompletedFastPathsDoNotBlock(string taskType, string property)
    {
        var code = $$"""
            class C {
                int Positive({{taskType}} task) { if (task.{{property}}) return task.Result; return 0; }
                int Negative({{taskType}} task) { if (!(task.{{property}})) { return 0; } return task.Result; }
            }
            """;
        var compilation = Compile(code);
        PerformanceAsyncProbe.Analyze([("Fixture", compilation)]).Findings.Should().NotContain(f => f.Category == "syncOverAsync");
        ErrorHandlingProbe.Analyze([("Fixture", compilation)]).Findings.Should().NotContain(f => f.Category == "syncBlockingCall");
    }

    [Theory]
    [InlineData("if (!task.IsCompleted) { if (flag) return 0; }")]
    [InlineData("if (!other.IsCompleted) return 0;")]
    [InlineData("if (!task.IsCompleted) return 0; task = other;")]
    [InlineData("if (!task.IsCompleted) return 0; Reset(ref task);")]
    public void InconclusiveOrInvalidatedGuardsRemainActionable(string guard)
    {
        var result = Analyze($$"""
            class C {
                void Reset(ref Task<int> value) { value = Task.FromResult(1); }
                int M(Task<int> task, Task<int> other, bool flag) { {{guard}} return task.Result; }
            }
            """);
        result.Findings.Should().ContainSingle(f => f.Category == "syncOverAsync" && f.Severity == "error");
    }

    [Fact]
    public void OuterCompletionAndContractsDoNotExemptDeferredWork()
    {
        var result = Analyze("""
            interface IHook { void M(Task<int> task); }
            class C : IHook {
                public void M(Task<int> task) {
                    if (task.IsCompleted) { Func<int> later = () => task.Result; }
                    int Later() => task.Result;
                }
            }
            """);
        result.Findings.Count(f => f.Category == "syncOverAsync" && f.Severity == "error").Should().Be(2);
    }

    [Theory]
    [InlineData("public", "")]
    [InlineData("", "IHook.")]
    public void SynchronousContractIsAReviewLeadInBothDimensions(string visibility, string qualifier)
    {
        var code = $$"""
            interface IHook { void Configure(Task task); }
            class C : IHook { {{visibility}} void {{qualifier}}Configure(Task task) { task.Wait(); } }
            """;
        var compilation = Compile(code);
        var result = PerformanceAsyncProbe.Analyze([("Fixture", compilation)]);
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be("info");
        finding.Observations["classificationReason"].Should().Be("synchronousContract");
        result.Score.Should().Be(10);
        var errorHandling = ErrorHandlingProbe.Analyze([("Fixture", compilation)]);
        errorHandling.Score.Should().Be(10);
        errorHandling.Findings.Should().ContainSingle(f => f.Category == "syncBlockingCall" && f.Severity == "info");
    }

    [Theory]
    [InlineData("interface IHook { Task M(Task task); }", "IHook", "public")]
    [InlineData("abstract class Hook { public abstract Task M(Task task); }", "Hook", "public override")]
    public void AsyncReturningContractsDoNotExcuseBlocking(string contract, string parent, string modifiers)
    {
        Analyze($$"""
            {{contract}}
            class C : {{parent}} { {{modifiers}} Task M(Task task) { task.Wait(); return task; } }
            """).Findings.Should().ContainSingle(f => f.Category == "syncOverAsync" && f.Severity == "error");
    }

    [Theory]
    [InlineData("// Synchronous execution is required because cancellation must remain available.", "info")]
    [InlineData("// TODO remove this synchronous wait", "error")]
    [InlineData("// task.Wait();", "error")]
    [InlineData("// Configure telemetry here", "error")]
    public void LocalExplanationIsAcceptedWithoutJudgingTheDecision(string comment, string severity)
    {
        Analyze($$"""
            class C { void M(Task task) {
                {{comment}}
                task.Wait();
            } }
            """).Findings.Should().ContainSingle(f => f.Category == "syncOverAsync" && f.Severity == severity);
    }

    [Fact]
    public void ExplanationDoesNotTransferToSiblingOrNestedFunction()
    {
        var result = Analyze("""
            class C { void M(Task task, Task other) {
                // Synchronous waiting is intentional because this boundary cannot await.
                task.Wait();
                other.Wait();
                // Synchronous waiting is intentional because this boundary cannot await.
                Action action = () => other.Wait();
            } }
            """);
        result.Findings.Count(f => f.Severity == "error").Should().Be(2);
        result.Findings.Count(f => f.Severity == "info").Should().Be(1);
    }

    [Theory]
    [InlineData("CancellationToken", "public", "context", false)]
    [InlineData("CancellationToken", "public", "context.Token", false)]
    [InlineData("CancellationToken", "private", "context", true)]
    [InlineData("int", "public", "context", true)]
    [InlineData("CancellationToken", "public", "new Context()", true)]
    public void CancellationContextRequiresAnAccessibleTokenAndUse(string type, string access, string argument, bool expected)
    {
        var result = Analyze($$"""
            class Context { {{access}} {{type}} Token { get; set; } }
            class C {
                Task ExecuteAsync(object value) => Task.CompletedTask;
                public Task RunAsync(Context context) => ExecuteAsync({{argument}});
            }
            """);
        result.Findings.Any(f => f.Category == "missingCancellationToken").Should().Be(expected);
        result.Score.Should().Be(10);
    }

    [Theory]
    [InlineData("using var session = new Session();", "iterationScopedPersistenceLifetime")]
    [InlineData("", "transactionAndBatchingContextRequired")]
    public void LoopPersistenceRetainsItsLifetimeContext(string declaration, string reason)
    {
        var parameter = declaration.Length == 0 ? "Session session" : "";
        var result = Analyze($$"""
            class Session : IDisposable {
                public void SaveChanges() { }
                public void Dispose() { }
            }
            class C { void M({{parameter}}) {
                for (var i = 0; i < 100; i++) { {{declaration}} session.SaveChanges(); }
            } }
            """);
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be("info");
        finding.Observations["classificationReason"].Should().Be(reason);
        finding.Observations["scoreDisposition"].Should().Be("excludedReviewLead");
        result.Score.Should().Be(10);
    }

    [Fact]
    public void LoggingAContextDoesNotEstablishCancellationPropagation()
    {
        Analyze("""
            class Context { public CancellationToken Token { get; set; } }
            class C {
                public async Task RunAsync(Context context) { Console.WriteLine(context); await GetAsync(); }
                Task GetAsync() => Task.CompletedTask;
            }
            """).Findings.Should().ContainSingle(f => f.Category == "missingCancellationToken");
    }

    [Fact]
    public void SleepWithLocalExplanationRemainsVisibleWithoutPenalty()
    {
        var result = Analyze("""
            class C { void M() {
                // Synchronous delay is intentional because this caller owns its worker thread.
                Thread.Sleep(1);
            } }
            """);
        result.Findings.Should().ContainSingle(f => f.Category == "threadSleep" && f.Severity == "info");
        result.Score.Should().Be(10);
    }

    [Fact]
    public void ReviewLeadIsExcludedEvenWhenSameCategorySelectsTheScore()
    {
        var result = Analyze("""
            interface IHook { void Configure(Task task); }
            class C : IHook {
                public void Configure(Task task) { task.Wait(); }
                void Other(Task task) { task.Wait(); }
            }
            """);
        result.Score.Should().Be(2);
        result.ScoringDecision!.AttachFindings(result.Findings);
        result.ScoringDecision.FindingEffects.Should().Contain(effect => effect.Effect == "excluded");
        result.ScoringDecision.FindingEffects.Should().Contain(effect => effect.Effect == "policyInput");
    }

    [Theory]
    [InlineData("task.Wait();", 1)]
    [InlineData("task.Wait(CancellationToken.None);", 1)]
    [InlineData("task.Wait(10);", 2)]
    [InlineData("try { task.Wait(); } catch { throw; }", 1)]
    [InlineData("try { task.Wait(); } catch { }", 2)]
    [InlineData("try { task.Wait(); } catch (AggregateException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }", 1)]
    [InlineData("try { task.Wait(); } finally { task = other; }", 2)]
    public void SuccessfulWaitPreventsCountingItsResultAsAnotherBlock(string wait, int expected)
    {
        var result = Analyze($$"""
            class C { int M(Task<int> task, Task<int> other) { {{wait}} return task.Result; } }
            """);
        result.Findings.Count(f => f.Category == "syncOverAsync").Should().Be(expected);
    }
}
