using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public class PerformanceAsyncProbeTests
{
    private static readonly string TasksRef =
        Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Threading.Tasks.dll");

    private static DimensionResult Analyze(string code, bool addTasksRef = false)
    {
        MetadataReference[] extras = addTasksRef && File.Exists(TasksRef)
            ? [MetadataReference.CreateFromFile(TasksRef)]
            : [];

        var (_, _, compilation) = RoslynTestHelper.CompileCode(code, extras);
        var projects = new List<(string, Compilation)> { ("TestProject", compilation) };
        return PerformanceAsyncProbe.Analyze(projects);
    }

    // ── 1. syncOverAsync ─────────────────────────────────────────────────────

    [Fact]
    public void DotResult_FindsSyncOverAsync()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    var v = Task.FromResult(1).Result;
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "syncOverAsync");
    }

    [Fact]
    public void DotResult_SeverityIsError()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    var v = Task.FromResult(1).Result;
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Where(f => f.Category == "syncOverAsync")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    [Fact]
    public void DotWait_FindsSyncOverAsync()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.Delay(100).Wait();
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "syncOverAsync");
    }

    [Fact]
    public void GetAwaiterGetResult_FindsSyncOverAsync()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.FromResult(1).GetAwaiter().GetResult();
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "syncOverAsync");
    }

    [Fact]
    public void SyncRequiredComment_DowngradesSyncOverAsyncToInfo()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                // amp-metrics: sync-required
                void M() {
                    var value = Task.FromResult(1).Result;
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Where(f => f.Category == "syncOverAsync")
            .Should().ContainSingle()
            .Which.Severity.Should().Be("info");
    }

    [Fact]
    public void OverrideWithGetAwaiterGetResult_DowngradesSyncOverAsyncToInfo()
    {
        const string code = """
            using System.Threading.Tasks;
            abstract class Base {
                public abstract string M();
            }
            class C : Base {
                public override string M() {
                    return Task.FromResult("value").GetAwaiter().GetResult();
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Where(f => f.Category == "syncOverAsync")
            .Should().ContainSingle()
            .Which.Severity.Should().Be("info");
    }

    [Fact]
    public void ExplicitInterfaceImplementationWithGetAwaiterGetResult_DowngradesSyncOverAsyncToInfo()
    {
        const string code = """
            using System.Threading.Tasks;
            interface IFoo {
                string M();
            }
            class C : IFoo {
                string IFoo.M() {
                    return Task.FromResult("value").GetAwaiter().GetResult();
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Where(f => f.Category == "syncOverAsync")
            .Should().ContainSingle()
            .Which.Severity.Should().Be("info");
    }

    // ── 2. threadSleep ───────────────────────────────────────────────────────

    [Fact]
    public void ThreadSleep_FindsThreadSleepFinding()
    {
        const string code = """
            using System.Threading;
            public class Svc {
                public void Bad() { Thread.Sleep(1000); }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().ContainSingle(f => f.Category == "threadSleep");
    }

    [Fact]
    public void ThreadSleep_SeverityIsWarning()
    {
        const string code = """
            using System.Threading;
            class C {
                void M() { Thread.Sleep(500); }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "threadSleep")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    // ── 3. saveChangesInsideLoop ─────────────────────────────────────────────

    [Fact]
    public void SaveChangesInForeach_FindsSaveChangesInsideLoop()
    {
        const string code = """
            using System.Collections.Generic;
            class FakeContext {
                public void SaveChanges() { }
            }
            class C {
                void M(FakeContext ctx, IEnumerable<int> items) {
                    foreach (var item in items) {
                        ctx.SaveChanges();
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "saveChangesInsideLoop");
    }

    [Fact]
    public void SaveChangesInFor_FindsSaveChangesInsideLoop()
    {
        const string code = """
            class FakeContext {
                public void SaveChanges() { }
            }
            class C {
                void M(FakeContext ctx) {
                    for (int i = 0; i < 10; i++) {
                        ctx.SaveChanges();
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "saveChangesInsideLoop");
    }

    [Fact]
    public void SaveChangesInWhile_FindsSaveChangesInsideLoop()
    {
        const string code = """
            class FakeContext {
                public void SaveChanges() { }
            }
            class C {
                void M(FakeContext ctx, bool cond) {
                    while (cond) {
                        ctx.SaveChanges();
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "saveChangesInsideLoop");
    }

    [Fact]
    public void SaveChangesOutsideLoop_DoesNotFindSaveChangesInsideLoop()
    {
        const string code = """
            class FakeContext {
                public void SaveChanges() { }
            }
            class C {
                void M(FakeContext ctx) {
                    ctx.SaveChanges();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "saveChangesInsideLoop");
    }

    [Fact]
    public void SaveChangesInsideLoop_SeverityIsError()
    {
        const string code = """
            class FakeContext {
                public void SaveChanges() { }
            }
            class C {
                void M(FakeContext ctx, bool cond) {
                    while (cond) {
                        ctx.SaveChanges();
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "saveChangesInsideLoop")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    // ── 4. missingCancellationToken ──────────────────────────────────────────

    [Fact]
    public void AsyncMethodWithIoNoCancellationToken_FindsMissingCancellationToken()
    {
        const string code = """
            using System.Threading.Tasks;
            using System.Net.Http;
            class C {
                public async Task DoWorkAsync() {
                    var client = new HttpClient();
                    var response = await client.GetAsync("http://example.com");
                }
            }
            """;

        // Need System.Net.Http reference for HttpClient
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var httpRef = Path.Combine(runtimeDir, "System.Net.Http.dll");
        MetadataReference[] extras = File.Exists(httpRef)
            ? [MetadataReference.CreateFromFile(httpRef)]
            : [];
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code, extras);
        var projects = new List<(string, Compilation)> { ("TestProject", compilation) };
        var result = PerformanceAsyncProbe.Analyze(projects);

        result.Findings.Should().Contain(f => f.Category == "missingCancellationToken");
    }

    [Fact]
    public void AsyncMethodWithIoAndCancellationToken_DoesNotFindMissingCancellationToken()
    {
        const string code = """
            using System.Threading;
            using System.Threading.Tasks;
            class FakeRepo {
                public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("");
            }
            class C {
                public async Task DoWorkAsync(CancellationToken cancellationToken) {
                    var repo = new FakeRepo();
                    var result = await repo.GetAsync(cancellationToken);
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "missingCancellationToken");
    }

    [Fact]
    public void MissingCancellationToken_SeverityIsWarning()
    {
        const string code = """
            using System.Threading.Tasks;
            class FakeRepo {
                public Task<string> GetAsync() => Task.FromResult("");
            }
            class C {
                public async Task DoWorkAsync() {
                    var repo = new FakeRepo();
                    var result = await repo.GetAsync();
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Where(f => f.Category == "missingCancellationToken")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    [Fact]
    public void MiddlewareInvokeAsyncWithHttpContext_DoesNotFindMissingCancellationToken()
    {
        const string code = """
            using System.Threading.Tasks;
            namespace Microsoft.AspNetCore.Http { public class HttpContext { } }
            class FakeClient {
                public Task<string> GetAsync(string url) => Task.FromResult("");
            }
            class MyMiddleware {
                public async Task InvokeAsync(Microsoft.AspNetCore.Http.HttpContext context) {
                    var client = new FakeClient();
                    var result = await client.GetAsync("http://example.com");
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "missingCancellationToken");
    }

    // ── 5. materializationBeforeQueryShape ───────────────────────────────────

    [Fact]
    public void ToListThenWhere_FindsMaterializationBeforeQueryShape()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            class C {
                void M(IEnumerable<int> items) {
                    var result = items.ToList().Where(x => x > 0);
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "materializationBeforeQueryShape");
    }

    [Fact]
    public void ToListThenOrderBy_FindsMaterializationBeforeQueryShape()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            class C {
                void M(IEnumerable<int> items) {
                    var result = items.ToList().OrderBy(x => x);
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "materializationBeforeQueryShape");
    }

    [Fact]
    public void ToListThenSkip_FindsMaterializationBeforeQueryShape()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            class C {
                void M(IEnumerable<int> items) {
                    var result = items.ToList().Skip(5);
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "materializationBeforeQueryShape");
    }

    [Fact]
    public void WhereThenToList_DoesNotFindMaterializationBeforeQueryShape()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            class C {
                void M(IEnumerable<int> items) {
                    var result = items.Where(x => x > 0).ToList();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "materializationBeforeQueryShape");
    }

    [Fact]
    public void MaterializationBeforeQueryShape_SeverityIsWarning()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            class C {
                void M(IEnumerable<int> items) {
                    var result = items.ToList().Where(x => x > 0);
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "materializationBeforeQueryShape")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    // ── 6. awaitedIoInsideLoop ───────────────────────────────────────────────

    [Fact]
    public void AwaitGetAsyncInsideForeach_FindsAwaitedIoInsideLoop()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class FakeClient {
                public Task<string> GetAsync(string url) => Task.FromResult("");
            }
            class C {
                public async Task M(IEnumerable<string> urls) {
                    var client = new FakeClient();
                    foreach (var url in urls) {
                        var result = await client.GetAsync(url);
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void AwaitReadAsyncInsideWhile_FindsAwaitedIoInsideLoop()
    {
        const string code = """
            using System.Threading.Tasks;
            class FakeStream {
                public Task<int> ReadAsync(byte[] buf) => Task.FromResult(0);
            }
            class C {
                public async Task M(FakeStream stream, bool cond) {
                    while (cond) {
                        var buf = new byte[128];
                        await stream.ReadAsync(buf);
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void AwaitedIoInsideLoop_SeverityIsWarning()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class FakeClient {
                public Task<string> GetAsync(string url) => Task.FromResult("");
            }
            class C {
                public async Task M(IEnumerable<string> urls) {
                    var client = new FakeClient();
                    foreach (var url in urls) {
                        var result = await client.GetAsync(url);
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Where(f => f.Category == "awaitedIoInsideLoop")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    [Fact]
    public void SyncRequiredComment_SuppressesAwaitedIoInsideLoop()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class FakeClient {
                public Task<string> GetAsync(string url) => Task.FromResult("");
            }
            class C {
                // amp-metrics: sync-required
                public async Task M(IEnumerable<string> urls) {
                    var client = new FakeClient();
                    foreach (var url in urls) {
                        var result = await client.GetAsync(url);
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void CursorPaginationLoop_DoesNotFindAwaitedIoInsideLoop()
    {
        const string code = """
            using System.Threading.Tasks;
            class Request {
                public string? PageToken { get; set; }
            }
            class Response {
                public string? NextPageToken { get; set; }
            }
            class FakeClient {
                public Task<Response> GetAsync(Request request) => Task.FromResult(new Response());
            }
            class C {
                public async Task M(Request request) {
                    var client = new FakeClient();
                    do {
                        var response = await client.GetAsync(request);
                        request.PageToken = response.NextPageToken;
                    } while (request.PageToken != null);
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "awaitedIoInsideLoop");
    }

    // ── 7. unboundedWhenAll ──────────────────────────────────────────────────

    [Fact]
    public void WhenAllWithUnboundedSequence_FindsUnboundedWhenAll()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class C {
                public async Task M(IEnumerable<Task> tasks) {
                    await Task.WhenAll(tasks);
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "unboundedWhenAll");
    }

    [Fact]
    public void WhenAllWithToArray_DoesNotFindUnboundedWhenAll()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            class C {
                public async Task M(IEnumerable<Task> tasks) {
                    await Task.WhenAll(tasks.ToArray());
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "unboundedWhenAll");
    }

    [Fact]
    public void WhenAllWithToList_DoesNotFindUnboundedWhenAll()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            class C {
                public async Task M(IEnumerable<Task> tasks) {
                    await Task.WhenAll(tasks.ToList());
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "unboundedWhenAll");
    }

    [Fact]
    public void WhenAllWithMultipleInlineArgs_DoesNotFindUnboundedWhenAll()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                public async Task M() {
                    var t1 = Task.CompletedTask;
                    var t2 = Task.CompletedTask;
                    await Task.WhenAll(t1, t2);
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "unboundedWhenAll");
    }

    [Fact]
    public void UnboundedWhenAll_SeverityIsInfo()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class C {
                public async Task M(IEnumerable<Task> tasks) {
                    await Task.WhenAll(tasks);
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Where(f => f.Category == "unboundedWhenAll")
            .Should().AllSatisfy(f => f.Severity.Should().Be("info"));
    }

    // ── Clean code ───────────────────────────────────────────────────────────

    [Fact]
    public void CleanCode_ReturnsScore10()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                public async Task<int> DoWorkAsync(System.Threading.CancellationToken ct) {
                    await Task.Delay(0, ct);
                    return 42;
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Score.Should().Be(10);
        result.Findings.Where(f => f.Severity == "error" || f.Severity == "warning")
            .Should().BeEmpty();
    }

    // ── Scoring ──────────────────────────────────────────────────────────────

    [Fact]
    public void SyncOverAsyncPresent_ScoreIs2()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    var v = Task.FromResult(1).Result;
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Score.Should().Be(2);
    }

    [Fact]
    public void SaveChangesInsideLoopPresent_ScoreIs2()
    {
        const string code = """
            class FakeContext {
                public void SaveChanges() { }
            }
            class C {
                void M(FakeContext ctx, bool cond) {
                    while (cond) {
                        ctx.SaveChanges();
                    }
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(2);
    }

    [Fact]
    public void FiveOrMoreErrors_ScoreIs0()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M1() { var v = Task.FromResult(1).Result; }
                void M2() { var v = Task.FromResult(1).Result; }
                void M3() { var v = Task.FromResult(1).Result; }
                void M4() { var v = Task.FromResult(1).Result; }
                void M5() { var v = Task.FromResult(1).Result; }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Score.Should().Be(0);
    }

    [Fact]
    public void OnlyThreadSleepWarning_ScoreIs6()
    {
        const string code = """
            using System.Threading;
            class C {
                void M() { Thread.Sleep(100); }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(6);
    }

    [Fact]
    public void MoreThanThreeWarnings_ScoreIs4()
    {
        const string code = """
            using System.Threading;
            class C {
                void M1() { Thread.Sleep(100); }
                void M2() { Thread.Sleep(200); }
                void M3() { Thread.Sleep(300); }
                void M4() { Thread.Sleep(400); }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(4);
    }

    // ── Metadata ─────────────────────────────────────────────────────────────

    [Fact]
    public void Finding_HasProjectName()
    {
        const string code = """
            using System.Threading;
            class C {
                void M() { Thread.Sleep(100); }
            }
            """;

        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        var projects = new List<(string, Compilation)> { ("MyPerformanceProject", compilation) };
        var result = PerformanceAsyncProbe.Analyze(projects);

        result.Findings.Should().AllSatisfy(f => f.Project.Should().Be("MyPerformanceProject"));
    }

    [Fact]
    public void Finding_HasLineNumber()
    {
        const string code = """
            using System.Threading;
            class C {
                void M() {
                    Thread.Sleep(100);
                }
            }
            """;

        var result = Analyze(code);

        var finding = result.Findings.First(f => f.Category == "threadSleep");
        finding.Line.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void EmptyProjectList_ReturnsScore10()
    {
        var result = PerformanceAsyncProbe.Analyze(new List<(string, Compilation)>());

        result.Status.Should().Be("scored");
        result.Score.Should().Be(10);
    }
}
