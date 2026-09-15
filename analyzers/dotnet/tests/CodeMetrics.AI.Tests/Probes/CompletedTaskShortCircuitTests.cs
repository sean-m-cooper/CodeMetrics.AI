using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class CompletedTaskShortCircuitTests
{
    [Theory]
    [InlineData("task.IsCompleted && task.Result")]
    [InlineData("task.IsCompletedSuccessfully && task.Result")]
    [InlineData("!task.IsCompleted || task.Result")]
    [InlineData("(!(task.IsCompletedSuccessfully)) || task.Result")]
    [InlineData("task.IsCompleted && (flag || task.Result)")]
    [InlineData("flag && task.IsCompleted && task.Result")]
    [InlineData("(task.IsCompleted || task.IsCompletedSuccessfully) && task.Result")]
    [InlineData("task.Status == TaskStatus.RanToCompletion && task.Result")]
    [InlineData("task.IsCompleted && task.GetAwaiter().GetResult()")]
    public void ProvenCompletion_ExcludesBothBlockingFindings(string expression) => Check(expression, false);

    [Theory]
    [InlineData("task.IsCompleted || task.Result")]
    [InlineData("!task.IsCompleted && task.Result")]
    [InlineData("other.IsCompleted && task.Result")]
    [InlineData("task.Result && task.IsCompleted")]
    [InlineData("task.IsCompleted & task.Result")]
    [InlineData("!task.IsCompleted | task.Result")]
    [InlineData("(task.IsCompleted || flag) && task.Result")]
    [InlineData("task.IsCompleted && Reset(ref task) && task.Result")]
    [InlineData("task.IsCompleted && ((task = other) != null && task.Result)")]
    [InlineData("task.IsCompleted && (((task, other) = (other, task)).Item1 != null && task.Result)")]
    [InlineData("(task.IsCompleted && Reset(ref task)) || task.Result")]
    [InlineData("task.IsCompleted && new Func<bool>(() => task.Result)()")]
    [InlineData("task.IsCompleted && (flag || Reset(ref task)) && task.Result")]
    public void UnprovenOrInvalidatedCompletion_RetainsBothFindings(string expression) => Check(expression, true);

    [Fact]
    public void ValueTaskCompletion_IsAlsoRecognized() => Check("task.IsCompleted && task.Result", false, "ValueTask<bool>");

    [Fact]
    public void SameNamedProperty_DoesNotEstablishTaskCompletion() => Check("pretend.IsCompleted && task.Result", true);

    private static void Check(string expression, bool expectFinding, string taskType = "Task<bool>")
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath($$"""
            using System; using System.Threading.Tasks;
            class C {
                async Task<bool> Read({{taskType}} task, {{taskType}} other, bool flag, Pretend pretend) => {{expression}};
                static bool Reset(ref Task<bool> task) { task = new TaskCompletionSource<bool>().Task; return true; }
            }
            class Pretend { public bool IsCompleted => true; }
            """, Path.Combine(Path.GetTempPath(), "ShortCircuitSource.cs"));
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
        PerformanceAsyncProbe.Analyze([("App", compilation)]).Findings.Any(f => f.Category == "syncOverAsync")
            .Should().Be(expectFinding);
        ErrorHandlingProbe.Analyze([("App", compilation)]).Findings.Any(f => f.Category == "syncBlockingCall")
            .Should().Be(expectFinding);
    }
}
