using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class CompletedTaskSwitchTests
{
    [Theory]
    [InlineData("switch (task.Status) { case TaskStatus.RanToCompletion: return task.Result; default: return 0; }")]
    [InlineData("switch (task.Status) { case TaskStatus.Faulted: case TaskStatus.Canceled: return task.Result; default: return 0; }")]
    [InlineData("return task.Status switch { TaskStatus.RanToCompletion => task.Result, _ => 0 };")]
    [InlineData("return task.Status switch { TaskStatus.RanToCompletion or TaskStatus.Faulted => task.Result, _ => 0 };")]
    public void TerminalStateBranch_ProvesNonblockingAccess(string body) => Analyze(body).Findings.Should().NotContain(f => f.Category == "syncOverAsync");

    [Theory]
    [InlineData("switch (other.Status) { case TaskStatus.RanToCompletion: return task.Result; default: return 0; }")]
    [InlineData("switch (task.Status) { case TaskStatus.RanToCompletion: case TaskStatus.Running: return task.Result; default: return 0; }")]
    [InlineData("switch (task.Status) { case TaskStatus.RanToCompletion: task = other; return task.Result; default: return 0; }")]
    [InlineData("switch (task.Status) { case TaskStatus.RanToCompletion: (task, other) = (other, task); return task.Result; default: return 0; }")]
    [InlineData("switch (task.Status) { case TaskStatus.RanToCompletion: Reset(ref task); return task.Result; default: return 0; }")]
    [InlineData("switch (task.Status) { case TaskStatus.Running: goto case TaskStatus.RanToCompletion; case TaskStatus.RanToCompletion: return task.Result; default: return 0; }")]
    [InlineData("switch (task.Status) { case TaskStatus.RanToCompletion: Func<int> later = () => task.Result; return later(); default: return 0; }")]
    [InlineData("return task.Status switch { TaskStatus.RanToCompletion or TaskStatus.Running => task.Result, _ => 0 };")]
    [InlineData("return task.Status switch { TaskStatus.RanToCompletion when Reset(ref task) => task.Result, _ => 0 };")]
    [InlineData("return task.Status switch { TaskStatus.Running => 0, _ => task.Result };")]
    [InlineData("return task.Status switch { TaskStatus.RanToCompletion when Reset(ref task) && false => 0, TaskStatus.RanToCompletion => task.Result, _ => 0 };")]
    [InlineData("switch (task.Status) { case TaskStatus.RanToCompletion when Reset(ref task) && false: return 0; case TaskStatus.RanToCompletion: return task.Result; default: return 0; }")]
    public void UnprovenOrInvalidatedBranch_RetainsBlockingFinding(string body) => Analyze(body).Findings.Should().Contain(f => f.Category == "syncOverAsync");

    private static DimensionResult Analyze(string body)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath($$"""
            using System; using System.Threading.Tasks;
            class C {
                async Task<int> Read(Task<int> task, Task<int> other) { {{body}} }
                static bool Reset(ref Task<int> task) { task = new TaskCompletionSource<int>().Task; return true; }
            }
            """, Path.Combine(Path.GetTempPath(), "SwitchSource.cs"));
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        return PerformanceAsyncProbe.Analyze([("App", compilation)]);
    }
}
