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

    private static readonly string ChannelsRef =
        Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Threading.Channels.dll");

    private static DimensionResult Analyze(
        string code, bool addTasksRef = false, bool addChannelsRef = false)
    {
        var extras = new List<MetadataReference>();

        if (addTasksRef && File.Exists(TasksRef))
            extras.Add(MetadataReference.CreateFromFile(TasksRef));

        if (addChannelsRef && File.Exists(ChannelsRef))
            extras.Add(MetadataReference.CreateFromFile(ChannelsRef));

        var (_, _, compilation) = RoslynTestHelper.CompileCode(code, extras.ToArray());
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

    [Fact]
    public void DomainResultProperty_DoesNotFindSyncOverAsync()
    {
        const string code = """
            class AdjudicationResult { }
            class AdjudicationRound {
                public AdjudicationResult? Result { get; set; }
            }
            class C {
                void M(AdjudicationRound round) {
                    var v = round.Result;
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "syncOverAsync");
    }

    [Fact]
    public void DomainWaitAndGetAwaiter_DoesNotFindSyncOverAsync()
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

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "syncOverAsync");
    }

    [Fact]
    public void ConfigureAwaitGetAwaiterGetResult_FindsSyncOverAsync()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.FromResult(1).ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "syncOverAsync");
    }

    [Fact]
    public void ValueTaskResult_FindsSyncOverAsync()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M(ValueTask<int> vt) {
                    var v = vt.Result;
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "syncOverAsync");
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

    [Fact]
    public void ConventionalMiddlewareWithPerRequestService_DoesNotFindMissingCancellationToken()
    {
        const string code = """
            using System.Threading.Tasks;
            using Context = Microsoft.AspNetCore.Http.HttpContext;
            namespace Microsoft.AspNetCore.Http { public class HttpContext { } }
            class FakeClient {
                public Task<string> GetAsync(string url) => Task.FromResult("");
            }
            class MyMiddleware {
                public async Task InvokeAsync(Context context, FakeClient client) {
                    var result = await client.GetAsync("http://example.com");
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "missingCancellationToken");
    }

    [Fact]
    public void IMiddlewareEntryPoint_DoesNotFindMissingCancellationToken()
    {
        const string code = """
            using System.Threading.Tasks;
            namespace Microsoft.AspNetCore.Http {
                public class HttpContext { }
                public delegate Task RequestDelegate(HttpContext context);
                public interface IMiddleware {
                    Task InvokeAsync(HttpContext context, RequestDelegate next);
                }
            }
            class FakeClient {
                public Task<string> GetAsync(string url) => Task.FromResult("");
            }
            class RequestHandler : Microsoft.AspNetCore.Http.IMiddleware {
                public async Task InvokeAsync(
                    Microsoft.AspNetCore.Http.HttpContext context,
                    Microsoft.AspNetCore.Http.RequestDelegate next) {
                    var client = new FakeClient();
                    var result = await client.GetAsync("http://example.com");
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "missingCancellationToken");
    }

    [Fact]
    public void NonMiddlewareInvokeAsyncWithHttpContext_FindsMissingCancellationToken()
    {
        const string code = """
            using System.Threading.Tasks;
            namespace Microsoft.AspNetCore.Http { public class HttpContext { } }
            class FakeClient {
                public Task<string> GetAsync(string url) => Task.FromResult("");
            }
            class RequestHandler {
                public async Task InvokeAsync(Microsoft.AspNetCore.Http.HttpContext context) {
                    var client = new FakeClient();
                    var result = await client.GetAsync("http://example.com");
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().ContainSingle(f => f.Category == "missingCancellationToken");
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

    // ── 6a. Sequential-by-necessity loops ────────────────────────────────────
    // Each suppressed shape is paired with a control proving the same recogniser
    // does not swallow a genuine N+1.

    [Fact]
    public void CursorLoop_TokenDerivedThroughSeveralHops_DoesNotFindAwaitedIoInsideLoop()
    {
        // The real shape: the next URL is built from the previous response, and the
        // continuation token reaches the loop condition only after three assignments.
        const string code = """
            using System.Threading.Tasks;
            class Page { public string? NextPageToken { get; set; } }
            class Content { public Task<string> ReadAsStringAsync() => Task.FromResult(""); }
            class Response { public Content Content { get; } = new Content(); }
            class FakeClient { public Task<Response> GetAsync(string url) => Task.FromResult(new Response()); }
            class C {
                public async Task M(FakeClient client) {
                    string? nextPageToken = null;
                    do {
                        var url = BuildUrl(nextPageToken);
                        var response = await client.GetAsync(url);
                        var json = await response.Content.ReadAsStringAsync();
                        var page = Deserialize(json);
                        nextPageToken = page?.NextPageToken;
                    } while (!string.IsNullOrEmpty(nextPageToken));
                }
                private static string BuildUrl(string? token) => "";
                private static Page? Deserialize(string json) => null;
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void CursorLoop_ConditionVariableAssignedStraightFromAwait_DoesNotFindAwaitedIoInsideLoop()
    {
        const string code = """
            using System.Threading.Tasks;
            class FakeClient { public Task<string> GetStringAsync(string url) => Task.FromResult(""); }
            class C {
                public async Task M(FakeClient client, string firstPage) {
                    var currentJson = firstPage;
                    var pagesWalked = 0;
                    while (pagesWalked < 10 && !string.IsNullOrEmpty(currentJson)) {
                        var nextUrl = NextUrl(currentJson);
                        pagesWalked++;
                        if (string.IsNullOrEmpty(nextUrl)) break;
                        currentJson = await client.GetStringAsync(nextUrl);
                    }
                }
                private static string NextUrl(string json) => "";
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void CursorLoop_SuppressesEveryAwaitInTheSameIteration()
    {
        // Once the loop is cursor-driven every await in that body is on the critical
        // path to the next cursor, so none of them is batchable.
        const string code = """
            using System.Threading.Tasks;
            class Response { public string? NextToken { get; set; } }
            class FakeClient {
                public Task<Response> GetAsync(string url) => Task.FromResult(new Response());
                public Task WriteAsync(Response r) => Task.CompletedTask;
            }
            class C {
                public async Task M(FakeClient client) {
                    string? token = null;
                    do {
                        var response = await client.GetAsync(token ?? "");
                        await client.WriteAsync(response);
                        token = response.NextToken;
                    } while (token != null);
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void NestedLoopInsideCursorLoop_StillFindsAwaitedIoInsideLoop()
    {
        // The outer loop is cursor-driven, but the inner foreach over that page's items
        // is a textbook N+1. Judging each await against its innermost loop keeps it visible.
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class Response {
                public string? NextToken { get; set; }
                public List<string> Items { get; } = new List<string>();
            }
            class FakeClient {
                public Task<Response> GetAsync(string url) => Task.FromResult(new Response());
                public Task SendAsync(string item) => Task.CompletedTask;
            }
            class C {
                public async Task M(FakeClient client) {
                    string? token = null;
                    do {
                        var response = await client.GetAsync(token ?? "");
                        foreach (var item in response.Items) {
                            await client.SendAsync(item);
                        }
                        token = response.NextToken;
                    } while (token != null);
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().ContainSingle(f => f.Category == "awaitedIoInsideLoop")
            .Which.Message.Should().Contain("SendAsync");
    }

    [Fact]
    public void WhileConditionNotDerivedFromAwait_StillFindsAwaitedIoInsideLoop()
    {
        // The condition variable is recomputed from a local queue, not from the response,
        // so batching remains a legitimate suggestion.
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class FakeClient { public Task<string> GetAsync(string id) => Task.FromResult(""); }
            class C {
                public async Task M(FakeClient client, Queue<string> pending) {
                    var hasMore = pending.Count > 0;
                    while (hasMore) {
                        var id = pending.Dequeue();
                        var result = await client.GetAsync(id);
                        hasMore = pending.Count > 0;
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void BoundedFallbackSequence_DoesNotFindAwaitedIoInsideLoop()
    {
        // Try https, fall back to http, stop at the first that answers.
        const string code = """
            using System.Threading.Tasks;
            class FakeClient { public Task<bool> SendAsync(string url) => Task.FromResult(true); }
            class C {
                public async Task M(FakeClient client, string host) {
                    foreach (var url in new[] { "https://" + host, "http://" + host }) {
                        var ok = await client.SendAsync(url);
                        if (ok) return;
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void InlineLiteralLoopWithoutEarlyExit_StillFindsAwaitedIoInsideLoop()
    {
        // Same short inline literal, but every element is always visited — these two
        // calls are independent and Task.WhenAll really would halve the latency.
        const string code = """
            using System.Threading.Tasks;
            class FakeClient { public Task<bool> SendAsync(string url) => Task.FromResult(true); }
            class C {
                public async Task M(FakeClient client, string host) {
                    foreach (var url in new[] { "https://" + host, "http://" + host }) {
                        var ok = await client.SendAsync(url);
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void ChannelWriterWriteAsyncInsideLoop_DoesNotFindAwaitedIoInsideLoop()
    {
        // The await IS the back-pressure; Task.WhenAll would defeat the channel bound.
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Channels;
            using System.Threading.Tasks;
            class C {
                public async Task M(ChannelWriter<int> writer, IEnumerable<int> items) {
                    foreach (var item in items) {
                        await writer.WriteAsync(item);
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true, addChannelsRef: true);

        result.Findings.Should().NotContain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void BackpressureWrapperInterfaceInsideLoop_DoesNotFindAwaitedIoInsideLoop()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Channels;
            using System.Threading.Tasks;
            interface IWorkQueue { ValueTask WriteAsync(int item); }
            sealed class WorkQueue : IWorkQueue {
                private readonly Channel<int> channel = Channel.CreateBounded<int>(4);
                public ValueTask WriteAsync(int item) => channel.Writer.WriteAsync(item);
            }
            class C {
                public async Task M(IWorkQueue queue, IEnumerable<int> items) {
                    foreach (var item in items) await queue.WriteAsync(item);
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true, addChannelsRef: true);

        result.Findings.Should().NotContain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void OrderedPipelineStagesSharingContext_DoNotFindAwaitedIoInsideLoop()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            interface IPipelineStage { Task ExecuteAsync(Context context); }
            sealed class Context { }
            class Pipeline {
                private readonly IReadOnlyList<IPipelineStage> stages;
                public Pipeline(IReadOnlyList<IPipelineStage> stages) { this.stages = stages; }
                public async Task RunAsync(Context context) {
                    for (var i = 0; i < stages.Count; i++)
                        await ExecuteStageWithTimingAsync(stages[i], context);
                }
                private static Task ExecuteStageWithTimingAsync(IPipelineStage stage, Context context) =>
                    stage.ExecuteAsync(context);
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void UserTypeNamedLikeAChannel_StillFindsAwaitedIoInsideLoop()
    {
        // Back-pressure is recognised from the resolved framework type, never from the
        // name, so a hand-rolled wrapper is not silently exempted.
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class ChannelWriter<T> { public Task WriteAsync(T item) => Task.CompletedTask; }
            class C {
                public async Task M(ChannelWriter<int> writer, IEnumerable<int> items) {
                    foreach (var item in items) {
                        await writer.WriteAsync(item);
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "awaitedIoInsideLoop");
    }

    [Fact]
    public void IndependentAwaitsOverACollection_StillFindsAwaitedIoInsideLoop()
    {
        // The headline true positive the rule exists for: N independent lookups with no
        // inter-iteration dependency. None of the new recognisers may touch it.
        const string code = """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class FakeClient { public Task<string> GetAsync(string id) => Task.FromResult(""); }
            class C {
                public async Task M(FakeClient client, IEnumerable<string> ids) {
                    var results = new List<string>();
                    foreach (var id in ids) {
                        results.Add(await client.GetAsync(id));
                    }
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "awaitedIoInsideLoop");
    }

    // ── 7. unboundedWhenAll ──────────────────────────────────────────────────

    [Fact]
    public void WhenAllWithExistingTaskCollection_DoesNotClaimItCreatesUnboundedConcurrency()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            class C {
                public async Task M(IEnumerable<Task> tasks) {
                    await Task.WhenAll(tasks);
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "unboundedWhenAll");
    }

    [Fact]
    public void WhenAllWithInputSizedProjection_FindsPotentiallyUnboundedWhenAll()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            class C {
                private Task SendAsync(int item) => Task.CompletedTask;
                public async Task M(IEnumerable<int> items) {
                    await Task.WhenAll(items.Select(SendAsync));
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().ContainSingle(f => f.Category == "unboundedWhenAll")
            .Which.Confidence.Should().Be("medium");
    }

    [Fact]
    public void MaterializingInputSizedProjection_DoesNotHideUnboundedFanOut()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            class C {
                private Task SendAsync(int item) => Task.CompletedTask;
                public async Task M(IEnumerable<int> items) {
                    await Task.WhenAll(items.Select(SendAsync).ToArray());
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().Contain(f => f.Category == "unboundedWhenAll");
    }

    [Fact]
    public void ConstructorMaterializedStrategySet_DoesNotFindUnboundedWhenAll()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            interface IEnricher { Task RunAsync(); }
            class C {
                private readonly IReadOnlyList<IEnricher> enrichers;
                public C(IEnumerable<IEnricher> enrichers) { this.enrichers = enrichers.ToList(); }
                public Task M() => Task.WhenAll(enrichers.Select(enricher => enricher.RunAsync()));
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
            using System.Linq;
            using System.Threading.Tasks;
            class C {
                private Task SendAsync(int item) => Task.CompletedTask;
                public async Task M(IEnumerable<int> items) {
                    await Task.WhenAll(items.Select(SendAsync));
                }
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Where(f => f.Category == "unboundedWhenAll")
            .Should().AllSatisfy(f => f.Severity.Should().Be("info"));
    }

    [Fact]
    public void FanOutImplementationMutatesCapturedRequest_FindsSharedStateMutation()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            sealed class Request { public List<int> Values { get; } = new(); }
            interface IEnricher { Task EnrichAsync(Request request); }
            sealed class Enricher : IEnricher {
                public Task EnrichAsync(Request request) {
                    request.Values.Add(1);
                    return Task.CompletedTask;
                }
            }
            class Orchestrator {
                public Task RunAsync(IEnumerable<IEnricher> enrichers, Request request) =>
                    Task.WhenAll(enrichers.Select(enricher => enricher.EnrichAsync(request)));
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().ContainSingle(f => f.Category == "sharedStateMutationInFanOut")
            .Which.Severity.Should().Be("error");
    }

    [Fact]
    public void FanOutReturnsResultsWithoutMutatingRequest_DoesNotFindSharedStateMutation()
    {
        const string code = """
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            sealed class Request { }
            sealed class Detail { }
            interface IEnricher { Task<Detail> EnrichAsync(Request request); }
            sealed class Enricher : IEnricher {
                public Task<Detail> EnrichAsync(Request request) => Task.FromResult(new Detail());
            }
            class Orchestrator {
                public Task<Detail[]> RunAsync(IEnumerable<IEnricher> enrichers, Request request) =>
                    Task.WhenAll(enrichers.Select(enricher => enricher.EnrichAsync(request)));
            }
            """;

        var result = Analyze(code, addTasksRef: true);

        result.Findings.Should().NotContain(f => f.Category == "sharedStateMutationInFanOut");
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
    public void OnlyThreadSleepWarning_ScoreIs8()
    {
        const string code = """
            using System.Threading;
            class C {
                void M() { Thread.Sleep(100); }
            }
            """;

        var result = Analyze(code);

        result.Findings.Count(f => f.Severity == "warning").Should().Be(1);
        result.Score.Should().Be(8);
    }

    [Fact]
    public void TwoWarnings_ScoreIs6()
    {
        const string code = """
            using System.Threading;
            class C {
                void M1() { Thread.Sleep(100); }
                void M2() { Thread.Sleep(200); }
            }
            """;

        var result = Analyze(code);

        result.Findings.Count(f => f.Severity == "warning").Should().Be(2);
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
