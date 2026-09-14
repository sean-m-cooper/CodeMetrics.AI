using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public class WaitBoundaryTests
{
    private static Compilation Compile(string source)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(
            "using System; using System.Threading; using System.Threading.Tasks;\n" + source,
            Path.Combine(Path.GetTempPath(), "WaitBoundary", "Source.cs"),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location));
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return compilation;
    }

    private static void AssertWaits(string source, int errors, int reviews, string? reason = null)
    {
        var compilation = Compile(source);
        var performance = PerformanceAsyncProbe.Analyze([("Fixture", compilation)]);
        var handling = ErrorHandlingProbe.Analyze([("Fixture", compilation)]);
        foreach (var (result, category) in new[] { (performance, "syncOverAsync"), (handling, "syncBlockingCall") })
        {
            var waits = result.Findings.Where(f => f.Category == category).ToArray();
            var scoredSeverity = category == "syncOverAsync" ? "error" : "warning";
            waits.Count(f => f.Severity == scoredSeverity).Should().Be(errors);
            waits.Count(f => f.Severity == "info").Should().Be(reviews);
            if (reason != null)
                waits.Where(f => f.Severity == "info").Should().OnlyContain(f =>
                    (string)f.Observations["classificationReason"]! == reason);
        }
    }

    [Theory]
    [InlineData("Task<int>", "task.IsCompletedSuccessfully ? task.Result : 0")]
    [InlineData("Task<int>", "!task.IsCompletedSuccessfully ? 0 : task.Result")]
    [InlineData("ValueTask<int>", "task.IsCompleted ? task.Result : 0")]
    [InlineData("ValueTask<int>", "!(task.IsCompleted) ? 0 : task.Result")]
    public void ConditionalCompletedBranchIsNonblocking(string type, string expression) =>
        AssertWaits($$"""class C { int M({{type}} task) => {{expression}}; }""", 0, 0);

    [Theory]
    [InlineData("task.IsCompletedSuccessfully ? task.Result : task.GetAwaiter().GetResult()")]
    [InlineData("task.IsCompletedSuccessfully ? (task = other).Result : 0")]
    [InlineData("other.IsCompletedSuccessfully ? task.Result : 0")]
    [InlineData("task.IsCompletedSuccessfully ? 0 : task.Result")]
    [InlineData("task.IsCompletedSuccessfully ? Use(Reset(ref task), task.Result) : 0")]
    public void IncompleteOrMutatedConditionalBranchStillBlocks(string expression) =>
        AssertWaits($$"""
            class C {
                int M(Task<int> task, Task<int> other) => {{expression}};
                int Reset(ref Task<int> task) { task = new TaskCompletionSource<int>().Task; return 0; }
                int Use(int a, int b) => a + b;
            }
            """, 1, 0);

    [Fact]
    public void ConditionalGuardDoesNotTransferToDeferredCallback() => AssertWaits("""
        class C { Func<int> M(Task<int> task) => task.IsCompleted ? () => task.Result : () => 0; }
        """, 1, 0);

    [Theory]
    [InlineData("return Task.CompletedTask;")]
    [InlineData("Console.WriteLine(1); return Task.CompletedTask;")]
    [InlineData("if (flag) return Task.CompletedTask; throw new Exception();")]
    [InlineData("Task Nested() => Task.Delay(1); if (flag) return Task.CompletedTask; return Task.CompletedTask;")]
    public void AllNormalHelperReturnsAreCompleted(string body) => AssertWaits($$"""
        sealed class C {
            public Task Work(bool flag) { {{body}} }
            void M() => Work(true).GetAwaiter().GetResult();
        }
        """, 0, 0);

    [Theory]
    [InlineData("if (flag) return Task.Delay(1); return Task.CompletedTask;")]
    [InlineData("return Forward();")]
    [InlineData("Func<Task> completed = () => Task.CompletedTask; return Task.Delay(1);")]
    public void IncompleteOrUnprovenHelperReturnsStillBlock(string body) => AssertWaits($$"""
        sealed class C {
            Task Forward() => Task.CompletedTask;
            public Task Work(bool flag) { {{body}} }
            void M() => Work(true).GetAwaiter().GetResult();
        }
        """, 1, 0);

    [Fact]
    public void FromResultFactoryAndResolvedHelperAreCompleted() => AssertWaits("""
        class C {
            static Task<int> Work() => Task.FromResult(1);
            int M() => Work().Result + Task.FromResult(2).Result;
        }
        """, 0, 0);

    [Fact]
    public void VirtualAndAsyncHelpersAreNotProvenByVisibleReturns() => AssertWaits("""
        class C {
            public virtual Task Work() => Task.CompletedTask;
            async Task<int> Other() { await Task.Delay(1); return 1; }
            void M() { Work().GetAwaiter().GetResult(); _ = Other().Result; }
        }
        class D : C { public override Task Work() => Task.Delay(1); }
        """, 2, 0);

    [Theory]
    [InlineData("public int Value => Load().Result;")]
    [InlineData("public int Value { get { return Load().Result; } }")]
    [InlineData("int IValue.Value => Load().Result;")]
    public void SynchronousInterfacePropertyIsReviewable(string property) => AssertWaits($$"""
        interface IValue { int Value { get; } }
        class C : IValue { Task<int> Load() => new TaskCompletionSource<int>().Task; {{property}} }
        """, 0, 1, "synchronousContract");

    [Theory]
    [InlineData("void IHook.Configure(Task task) { First(task); }")]
    [InlineData("public void Configure(Task task) { First(task); }")]
    public void PrivateHelperChainInheritsEveryCallersContract(string configure) => AssertWaits($$"""
        interface IHook { void Configure(Task task); }
        partial class C : IHook { {{configure}} private void First(Task task) => Second(task); }
        partial class C { private void Second(Task task) => task.Wait(); }
        """, 0, 1, "synchronousContractCallChain");

    [Theory]
    [InlineData("public Task Other(Task task) { Helper(task); return task; }")]
    [InlineData("public void Other(Task task) { Action later = () => Helper(task); }")]
    [InlineData("public Action<Task> Escape() => Helper;")]
    [InlineData("public void Other(Task task) { Helper(task); }")]
    [InlineData("public void Other(dynamic task) { Helper(task); }")]
    public void MixedCallersAndDelegateEscapesKeepHelpersActionable(string other) => AssertWaits($$"""
        interface IHook { void Configure(Task task); }
        class C : IHook {
            public void Configure(Task task) => Helper(task);
            private void Helper(Task task) => task.Wait();
            {{other}}
        }
        """, 1, 0);

    [Fact]
    public void PrivatePropertyChainRetainsSynchronousContract() => AssertWaits("""
        interface IValue { int Value { get; } }
        class C : IValue {
            Task<int> pending = new TaskCompletionSource<int>().Task;
            public int Value => Cached;
            private int Cached { get { Initialize(); return 1; } }
            private void Initialize() => pending.Wait();
        }
        """, 0, 1, "synchronousContractCallChain");

    [Fact]
    public void RecursiveHelpersDoNotAcquireUnprovenContext() => AssertWaits("""
        interface IHook { void Configure(Task task); }
        class C : IHook {
            public void Configure(Task task) => Helper(task);
            private void Helper(Task task) { task.Wait(); Helper(task); }
        }
        """, 1, 0);

    [Fact]
    public void CancellationLifecycleCallbackAndMethodGroupAreReviewable() => AssertWaits("""
        class C {
            Task task = new TaskCompletionSource<int>().Task;
            void Initialize(CancellationToken stopped) {
                stopped.Register(() => task.Wait());
                stopped.Register(Release);
            }
            private void Release() => task.Wait();
        }
        """, 0, 2);

    [Fact]
    public void ArbitraryAndAsyncCallbacksRemainActionable() => AssertWaits("""
        class C {
            void Configure(Action callback) => callback();
            void M(Task task, CancellationToken token) {
                Configure(() => task.Wait());
                Task.Run(() => task.Wait());
                token.Register(async () => { task.Wait(); await task; });
            }
        }
        """, 3, 0);

    [Fact]
    public void TaskReturningInterfacePropertyRemainsActionable() => AssertWaits("""
        interface IValue { Task Value { get; } }
        class C : IValue {
            Task pending = new TaskCompletionSource<int>().Task;
            public Task Value { get { pending.Wait(); return pending; } }
        }
        """, 1, 0);

    [Fact]
    public void ResolvedOptionsAndRedisFactoryCallbacksAreReviewable() => AssertWaits("""
        namespace Microsoft.Extensions.Options {
            class OptionsBuilder<T> {
                public OptionsBuilder<T> Configure<TService>(Action<T, TService> configureOptions) => this;
            }
        }
        namespace StackExchange.Redis { interface IDatabase { } }
        namespace Microsoft.AspNetCore.DataProtection.StackExchangeRedis {
            class RedisXmlRepository {
                public RedisXmlRepository(Func<global::StackExchange.Redis.IDatabase> databaseFactory, string key) { }
            }
        }
        class C {
            void M(Task task, Task<StackExchange.Redis.IDatabase> database) {
                new Microsoft.Extensions.Options.OptionsBuilder<object>()
                    .Configure<object>(configureOptions: (options, service) => task.Wait());
                _ = new Microsoft.AspNetCore.DataProtection.StackExchangeRedis.RedisXmlRepository(
                    key: "key", databaseFactory: () => database.Result);
            }
        }
        """, 0, 2, "synchronousCallbackContract");

    [Fact]
    public void CustomAwaitableContractsRemainActionable() => AssertWaits("""
        class Awaitable {
            public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter() => Task.CompletedTask.GetAwaiter();
        }
        interface IHook { Awaitable M(Task task); }
        class C : IHook { public Awaitable M(Task task) { task.Wait(); return new Awaitable(); } }
        """, 1, 0);

    [Fact]
    public void UncalledPrivateHelperIsNotPresumedSynchronous() => AssertWaits("""
        class C { private void Helper(Task task) => task.Wait(); }
        """, 1, 0);

    [Fact]
    public void UnrelatedOverloadsDoNotPollutePrivateCallGraph() => AssertWaits("""
        interface IHook { void Configure(Task task); }
        class C : IHook {
            public void Configure(Task task) => Helper(task);
            private void Helper(Task task) => task.Wait();
            private void Helper(int value) { }
            public Task Other() { Helper(1); return Task.CompletedTask; }
        }
        """, 0, 1, "synchronousContractCallChain");
}
