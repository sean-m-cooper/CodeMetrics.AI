using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public class ErrorHandlingProbeTests
{
    private static DimensionResult Analyze(string code)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        var projects = new List<(string, Compilation)> { ("TestProject", compilation) };
        return ErrorHandlingProbe.Analyze(projects);
    }

    // ── 1. emptyCatch ─────────────────────────────────────────────────────────

    [Fact]
    public void EmptyCatch_FindsEmptyCatchFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "emptyCatch");
    }

    [Fact]
    public void EmptyCatch_SeverityIsError()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "emptyCatch")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    // ── 2. throwEx ────────────────────────────────────────────────────────────

    [Fact]
    public void ThrowEx_FindsThrowExFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) { throw ex; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "throwEx");
    }

    [Fact]
    public void ThrowEx_SeverityIsError()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) { throw ex; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "throwEx")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    [Fact]
    public void BareRethrow_DoesNotFindThrowEx()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { throw; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "throwEx");
    }

    // ── 3. broadCatchWithoutLoggingOrRethrow ──────────────────────────────────

    [Fact]
    public void BroadCatchWithoutLogging_FindsBroadCatchFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { var x = 1; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void BroadCatchWithLogging_DoesNotFindBroadCatchFinding()
    {
        const string code = """
            using System;
            class Logger { public void LogError(string msg) { } }
            class C {
                Logger _log = new Logger();
                void M() {
                    try { }
                    catch (Exception ex) { _log.LogError(ex.Message); }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void BroadCatchWithRethrow_DoesNotFindBroadCatchFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { throw; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void BroadCatchWithWhenFilter_DoesNotFindBroadCatchFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) when (ex.Message != null) { var x = 1; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    // ── 4. broadCatchReturnsDefault ───────────────────────────────────────────

    [Fact]
    public void BroadCatchReturnsNull_FindsBroadCatchReturnsDefault()
    {
        const string code = """
            using System;
            class C {
                object M() {
                    try { return new object(); }
                    catch (Exception) { return null; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void BroadCatchReturnsFalse_FindsBroadCatchReturnsDefault()
    {
        const string code = """
            using System;
            class C {
                bool M() {
                    try { return true; }
                    catch (Exception) { return false; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void BroadCatchReturnsZero_FindsBroadCatchReturnsDefault()
    {
        const string code = """
            using System;
            class C {
                int M() {
                    try { return 1; }
                    catch (Exception) { return 0; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void BroadCatchLogsAndReturnsNull_DoesNotFindBroadCatchReturnsDefault()
    {
        // GooglePlacesClient.ResolveAnchorAsync and S3CategoryStore.GetExistingHashAsync:
        // log the caught exception at Warning, then return the documented fallback.
        const string code = """
            using System;
            class Logger { public void LogWarning(Exception ex, string msg) { } }
            class C {
                Logger _logger = new Logger();
                object M() {
                    try { return new object(); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) {
                        _logger.LogWarning(ex, "resolution failed");
                        return null;
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchReturnsDefault");
        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void BroadCatchLogsAndReturnsFalse_DoesNotFindBroadCatchReturnsDefault()
    {
        // SqsEmailService.SendScorecardEmailAsync: logs at Error, returns false.
        const string code = """
            using System;
            class Logger { public void LogError(Exception ex, string msg) { } }
            class C {
                Logger _logger = new Logger();
                bool M() {
                    try { return true; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) {
                        _logger.LogError(ex, "email-queue-failed");
                        return false;
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void BroadCatchLogsAndReturnsStringEmpty_DoesNotFindBroadCatchReturnsDefault()
    {
        // AgenticScorecardService.SafeApiCall: logs at Warning, returns string.Empty.
        const string code = """
            using System;
            class Logger { public void LogWarning(Exception ex, string msg) { } }
            class C {
                Logger _logger = new Logger();
                string M() {
                    try { return "ok"; }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) {
                        _logger.LogWarning(ex, "endpoint call failed");
                        return string.Empty;
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void BroadCatchLogsInsideNestedBlock_DoesNotFindBroadCatchReturnsDefault()
    {
        const string code = """
            using System;
            class Logger { public void LogWarning(Exception ex, string msg) { } }
            class C {
                Logger _logger = new Logger();
                object M(bool verbose) {
                    try { return new object(); }
                    catch (Exception ex) {
                        if (verbose) {
                            _logger.LogWarning(ex, "failed");
                        }
                        return null;
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchReturnsDefault");
        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void BroadCatchLogsThroughHelper_DoesNotFindBroadCatchReturnsDefault()
    {
        const string code = """
            using System;
            static class LogExtensions { public static void LogFailure(this object o, Exception ex) { } }
            class C {
                object M() {
                    try { return new object(); }
                    catch (Exception ex) {
                        this.LogFailure(ex);
                        return null;
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void BroadCatchWrapsAndThrows_DoesNotFindBroadCatchReportedAsSwallowing()
    {
        const string code = """
            using System;
            class C {
                object M() {
                    try { return new object(); }
                    catch (Exception ex) { throw new InvalidOperationException("wrapped", ex); }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
        result.Findings.Should().NotContain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void PrecedingCancellationRethrow_DoesNotFindBroadCatchWithoutLoggingOrRethrow()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { var x = 1; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void PrecedingCancellationCatchWithoutRethrow_StillFindsBroadCatch()
    {
        // The cancellation clause swallows too, so it guarantees nothing.
        const string code = """
            using System;
            class C {
                object M() {
                    try { return new object(); }
                    catch (OperationCanceledException) { return null; }
                    catch (Exception) { return null; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void SilentBroadCatchReturningNull_StillFindsBroadCatchReturnsDefault()
    {
        // True-positive control: no logging, no rethrow, no cancellation clause.
        const string code = """
            using System;
            class C {
                object M() {
                    try { return new object(); }
                    catch (Exception) { return null; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchReturnsDefault");
        result.Findings.Should().Contain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void BroadCatchReturnsDefault_SeverityIsError()
    {
        const string code = """
            using System;
            class C {
                int M() {
                    try { return 1; }
                    catch (Exception) { return 0; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "broadCatchReturnsDefault")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    // ── 5. syncBlockingCall ───────────────────────────────────────────────────

    [Fact]
    public void DotResult_FindsSyncBlockingCall()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    var t = Task.CompletedTask;
                    var _ = Task.FromResult(1).Result;
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "syncBlockingCall");
    }

    [Fact]
    public void DotWait_FindsSyncBlockingCall()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.Delay(100).Wait();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "syncBlockingCall");
    }

    [Fact]
    public void GetAwaiterGetResult_FindsSyncBlockingCall()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.FromResult(1).GetAwaiter().GetResult();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "syncBlockingCall");
    }

    [Fact]
    public void SyncBlockingCall_SeverityIsWarning()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.Delay(100).Wait();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "syncBlockingCall")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    [Fact]
    public void DomainResultProperty_DoesNotFindSyncBlockingCall()
    {
        const string code = """
            class AdjudicationResult { }
            class AdjudicationRound {
                public AdjudicationResult? Result { get; set; }
            }
            class C {
                void M(AdjudicationRound round) {
                    var _ = round.Result;
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "syncBlockingCall");
    }

    [Fact]
    public void DomainWaitAndGetAwaiter_DoesNotFindSyncBlockingCall()
    {
        const string code = """
            class DomainAwaiter {
                public string GetResult() => "ready";
            }
            class DomainOutcome {
                public string Result => "ready";
                public void Wait() { }
                public DomainAwaiter GetAwaiter() => new DomainAwaiter();
            }
            class C {
                string M(DomainOutcome outcome) {
                    outcome.Wait();
                    return outcome.Result + outcome.GetAwaiter().GetResult();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "syncBlockingCall");
    }

    [Fact]
    public void ConfigureAwaitGetAwaiterGetResult_FindsSyncBlockingCall()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.FromResult(1).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "syncBlockingCall");
    }

    [Fact]
    public void ValueTaskResult_FindsSyncBlockingCall()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M(ValueTask<int> vt) {
                    var _ = vt.Result;
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "syncBlockingCall");
    }

    // ── 6. consoleWriteLine ───────────────────────────────────────────────────

    [Fact]
    public void ConsoleWriteLine_FindsConsoleWriteLineFinding()
    {
        const string code = """
            class C {
                void M() {
                    Console.WriteLine("hello");
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "consoleWriteLine");
    }

    [Fact]
    public void ConsoleWriteLine_SeverityIsInfo()
    {
        const string code = """
            class C {
                void M() {
                    Console.WriteLine("hello");
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "consoleWriteLine")
            .Should().AllSatisfy(f => f.Severity.Should().Be("info"));
    }

    // ── 7. missingLoggerForMultipleCatches ────────────────────────────────────

    [Fact]
    public void MultipleCatchesNoLogger_FindsMissingLoggerFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void MultipleCatchesWithILoggerField_DoesNotFindMissingLogger()
    {
        const string code = """
            using System;
            interface ILogger { }
            class C {
                private ILogger _logger;
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void StaticClassWithILoggerMethodParameters_DoesNotFindMissingLogger()
    {
        const string code = """
            using System;
            interface ILogger { void LogDebug(Exception ex, string message); }
            static class Reader {
                internal static int A(ILogger logger) {
                    try { return 1; }
                    catch (Exception ex) { logger.LogDebug(ex, "a"); return 0; }
                }
                internal static int B(ILogger logger) {
                    try { return 1; }
                    catch (Exception ex) { logger.LogDebug(ex, "b"); return 0; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void MultipleCatchesWithGenericILoggerMethodParameter_DoesNotFindMissingLogger()
    {
        const string code = """
            using System;
            interface ILogger<T> { void LogDebug(Exception ex, string message); }
            class C {
                void M(ILogger<C> logger) {
                    try { }
                    catch (ArgumentNullException ex) { logger.LogDebug(ex, "null"); }
                    catch (InvalidOperationException ex) { logger.LogDebug(ex, "invalid"); }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void MultipleCatchesWithILoggerPrimaryConstructor_DoesNotFindMissingLogger()
    {
        const string code = """
            using System;
            interface ILogger<T> { }
            public sealed class C(ILogger<C> logger) {
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void MultipleCatchesWithILoggerRecordParameter_DoesNotFindMissingLogger()
    {
        const string code = """
            using System;
            interface ILogger<T> { }
            public record C(ILogger<C> Logger) {
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void MultipleCatchesWithILoggerStructPrimaryConstructor_DoesNotFindMissingLogger()
    {
        const string code = """
            using System;
            interface ILogger<T> { }
            public struct C(ILogger<int> logger) {
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void MultipleCatchesWithPrimaryConstructorButNoLogger_FindsMissingLogger()
    {
        const string code = """
            using System;
            interface IProvider { }
            public sealed class C(IProvider provider) {
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void SingleCatchNoLogger_DoesNotFindMissingLogger()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) { var x = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    // ── Generated source exclusion ────────────────────────────────────────────

    private const string MultipleCatchesWithoutLogger = """
        using System;
        class JsonNodeExtensions {
            void M() {
                try { }
                catch (ArgumentNullException ex) { var x = ex.Message; }
                catch (InvalidOperationException ex) { var y = ex.Message; }
            }
        }
        """;

    private static DimensionResult AnalyzeAtPath(string code, string path, string solutionDir)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(code, path);
        var projects = new List<(string, Compilation)> { ("TestProject", compilation) };
        return ErrorHandlingProbe.Analyze(projects, solutionDir);
    }

    [Fact]
    public void GeneratedFileName_DoesNotFindMissingLogger()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodeMetricsGeneratedRoot"));
        var file = Path.Combine(root, "obj", "Debug", "net10.0", "OpenApiXmlCommentSupport.generated.cs");

        var result = AnalyzeAtPath(MultipleCatchesWithoutLogger, file, root);

        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void AutoGeneratedHeader_DoesNotFindMissingLogger()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodeMetricsGeneratedRoot"));
        var file = Path.Combine(root, "src", "App", "Extensions.cs");
        var code = "// <auto-generated/>\n" + MultipleCatchesWithoutLogger;

        var result = AnalyzeAtPath(code, file, root);

        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void AuthoredFileWithSameContent_FindsMissingLogger()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodeMetricsGeneratedRoot"));
        var file = Path.Combine(root, "src", "App", "Extensions.cs");

        var result = AnalyzeAtPath(MultipleCatchesWithoutLogger, file, root);

        result.Findings.Should().Contain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    // ── No findings — clean code ──────────────────────────────────────────────

    [Fact]
    public void NoFindings_ReturnsScore10()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    // No try/catch, no Console.WriteLine, no blocking calls
                    int x = 1 + 1;
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(10);
        result.Findings.Where(f => f.Severity == "error" || f.Severity == "warning")
            .Should().BeEmpty();
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyCatchPresent_ScoreIs2()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(2);
    }

    [Fact]
    public void ThrowExPresent_ScoreIs2()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) { throw ex; }
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(2);
    }

    [Fact]
    public void FiveOrMoreEmptyCatches_ScoreIs0()
    {
        const string code = """
            using System;
            class C {
                void M1() { try { } catch (Exception) { } }
                void M2() { try { } catch (Exception) { } }
                void M3() { try { } catch (Exception) { } }
                void M4() { try { } catch (Exception) { } }
                void M5() { try { } catch (Exception) { } }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(0);
    }

    [Fact]
    public void OnlySyncBlockingCall_ScoreIs4()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.Delay(100).Wait();
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(4);
    }

    [Fact]
    public void BroadCatchReturnsDefault_ScoreIs4()
    {
        const string code = """
            using System;
            class C {
                object M() {
                    try { return new object(); }
                    catch (Exception) { return null; }
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(4);
    }

    [Fact]
    public void MoreThanThreeWarnings_ScoreIs6()
    {
        // Four silent broad catches, one per type so missingLoggerForMultipleCatches
        // (which needs two catches in one type) does not add a fifth warning.
        const string code = """
            using System;
            class C1 { void M() { try { } catch (Exception) { var x = 1; } } }
            class C2 { void M() { try { } catch (Exception) { var x = 2; } } }
            class C3 { void M() { try { } catch (Exception) { var x = 3; } } }
            class C4 { void M() { try { } catch (Exception) { var x = 4; } } }
            """;

        var result = Analyze(code);

        result.Findings.Count(f => f.Severity == "warning").Should().Be(4);
        result.Findings.Should().NotContain(f => f.Severity == "error");
        result.Score.Should().Be(6);
    }

    [Fact]
    public void OneToThreeWarnings_ScoreIs8()
    {
        // A single silent broad catch — the "minor gaps, no systemic issues" rung.
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { var x = 1; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Count(f => f.Severity == "warning").Should().Be(1);
        result.Findings.Should().NotContain(f => f.Severity == "error");
        result.Score.Should().Be(8);
    }

    [Fact]
    public void ThreeWarnings_ScoreIs8()
    {
        const string code = """
            using System;
            class C1 { void M() { try { } catch (Exception) { var x = 1; } } }
            class C2 { void M() { try { } catch (Exception) { var x = 2; } } }
            class C3 { void M() { try { } catch (Exception) { var x = 3; } } }
            """;

        var result = Analyze(code);

        result.Findings.Count(f => f.Severity == "warning").Should().Be(3);
        result.Score.Should().Be(8);
    }

    [Fact]
    public void OnlyWarning_ScoreIs8()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        // missingLoggerForMultipleCatches is the only warning → score 8
        result.Score.Should().Be(8);
    }

    [Fact]
    public void EveryRungIsReachable_NoLadderGaps()
    {
        // Guards against a rung being unreachable, which is what left 8 missing and
        // the trailing 'errors != 0 || warnings != 0' branch dead.
        const string severe = """
            using System;
            class C {
                void M1() { try { } catch (Exception) { } }
                void M2() { try { } catch (Exception) { } }
                void M3() { try { } catch (Exception) { } }
                void M4() { try { } catch (Exception) { } }
                void M5() { try { } catch (Exception) { } }
            }
            """;
        const string errorRung = """
            using System;
            class C { void M() { try { } catch (Exception) { } } }
            """;
        const string structural = """
            using System;
            class C {
                object M() {
                    try { return new object(); }
                    catch (Exception) { return null; }
                }
            }
            """;
        const string noisy = """
            using System;
            class C1 { void M() { try { } catch (Exception) { var x = 1; } } }
            class C2 { void M() { try { } catch (Exception) { var x = 2; } } }
            class C3 { void M() { try { } catch (Exception) { var x = 3; } } }
            class C4 { void M() { try { } catch (Exception) { var x = 4; } } }
            """;
        const string minor = """
            using System;
            class C { void M() { try { } catch (Exception) { var x = 1; } } }
            """;
        const string clean = """
            class C { void M() { int x = 1 + 1; } }
            """;

        Analyze(severe).Score.Should().Be(0);
        Analyze(errorRung).Score.Should().Be(2);
        Analyze(structural).Score.Should().Be(4);
        Analyze(noisy).Score.Should().Be(6);
        Analyze(minor).Score.Should().Be(8);
        Analyze(clean).Score.Should().Be(10);
    }

    // ── Metadata ──────────────────────────────────────────────────────────────

    [Fact]
    public void Finding_HasProjectName()
    {
        const string code = """
            using System;
            class C {
                void M() { try { } catch (Exception) { } }
            }
            """;

        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        var projects = new List<(string, Compilation)> { ("MyProject", compilation) };
        var result = ErrorHandlingProbe.Analyze(projects);

        result.Findings.Should().AllSatisfy(f => f.Project.Should().Be("MyProject"));
    }

    [Fact]
    public void Finding_HasLineNumber()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        var finding = result.Findings.First(f => f.Category == "emptyCatch");
        finding.Line.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Finding_HasTypeName()
    {
        const string code = """
            using System;
            class MyClass {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        var finding = result.Findings.First(f => f.Category == "emptyCatch");
        finding.Type.Should().Be("MyClass");
    }

    [Fact]
    public void EmptyProjectList_ReturnsScore10()
    {
        var result = ErrorHandlingProbe.Analyze(new List<(string, Compilation)>());

        result.Status.Should().Be("scored");
        result.Score.Should().Be(10);
    }
}
