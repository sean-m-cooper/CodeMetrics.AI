using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public class DeclaredAsyncBoundaryTests
{
    private static Compilation Compile(string code)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(
            "using System; using System.Threading.Tasks;\n" + code,
            Path.Combine(Path.GetTempPath(), "DeclaredAsyncBoundary", "Source.cs"),
            MetadataReference.CreateFromFile(typeof(IServiceProvider).Assembly.Location));
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return compilation;
    }

    private static void AssertWait(Compilation compilation, bool review, string reason)
    {
        var performance = PerformanceAsyncProbe.Analyze([("Fixture", compilation)]);
        var errors = ErrorHandlingProbe.Analyze([("Fixture", compilation)]);
        foreach (var (dimension, category) in new[] { (performance, "syncOverAsync"), (errors, "syncBlockingCall") })
        {
            var finding = dimension.Findings.Should().ContainSingle(f => f.Category == category).Subject;
            finding.Severity.Should().Be(review ? "info" : category == "syncOverAsync" ? "error" : "warning");
            if (review || category == "syncOverAsync")
            {
                finding.Observations["classificationReason"].Should().Be(review ? reason : "observedHazard");
                finding.Observations["scoreDisposition"].Should().Be(review ? "excludedReviewLead" : "scored");
            }
        }
        performance.Score.Should().Be(review ? 10 : 2);
        errors.Score.Should().Be(review ? 10 : 4);
    }

    [Theory]
    [InlineData("Task<int>", "return task.Result;")]
    [InlineData("Task<int>", "return task.GetAwaiter().GetResult();")]
    [InlineData("Task<int>", "return task.IsCompletedSuccessfully ? task.Result : task.GetAwaiter().GetResult();")]
    [InlineData("Task<int>", "task.Wait(); return 0;")]
    [InlineData("ValueTask<int>", "return task.Result;")]
    [InlineData("ValueTask<int>", "return task.GetAwaiter().GetResult();")]
    public void AdjacentDeclarationExplainsTheSameTask(string type, string body)
    {
        AssertWait(Compile($$"""
            class C { int M({{type}} source) {
                // Preloading permits synchronous retrieval of these settings.
                var task = source;
                {{body}}
            } }
            """), true, "documentedTaskLocalChoice");
    }

    [Theory]
    [InlineData("return other.Result;")]
    [InlineData("task = other; return task.Result;")]
    [InlineData("Reset(ref task, other); return task.Result;")]
    [InlineData("return (task = other).Result;")]
    [InlineData("return Reset(ref task, other) ? task.Result : 0;")]
    [InlineData("return Hidden() ? task.Result : 0; bool Hidden() { task = other; return true; }")]
    [InlineData("task.Wait(Hidden()); return 0; int Hidden() { task = other; return 10; }")]
    [InlineData("Func<int> later = () => task.Result; return later();")]
    [InlineData("int Later() => task.Result; return Later();")]
    [InlineData("if (flag) { return task.Result; } return 0;")]
    [InlineData("Console.WriteLine(flag); return task.Result;")]
    public void IntentDoesNotTransferAcrossWritesCallsScopesOrReceivers(string body)
    {
        AssertWait(Compile($$"""
            class C {
                static bool Reset(ref Task<int> value, Task<int> other) { value = other; return true; }
                int M(Task<int> source, Task<int> other, bool flag) {
                    // Preloading permits synchronous retrieval of these settings.
                    var task = source;
                    {{body}}
                }
            }
            """), false, "");
    }

    [Theory]
    [InlineData("// TODO remove this synchronous wait")]
    [InlineData("// Configure telemetry here")]
    [InlineData("// task.Wait();")]
    public void DeclarationRequiresExplanatoryProse(string comment)
    {
        AssertWait(Compile($$"""
            class C { int M(Task<int> source) {
                {{comment}}
                var task = source;
                return task.Result;
            } }
            """), false, "");
    }

    private const string CatalogType = """
        namespace OrchardCore.Scripting {
            public class GlobalMethod {
                public Func<IServiceProvider, Delegate> Method { get; set; }
                public Func<IServiceProvider, Delegate> AsyncMethod { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData("(Func<int>)(() => source.GetAwaiter().GetResult())")]
    [InlineData("(Func<int>)(() => { return source.Result; })")]
    [InlineData("(Action)(() => source.Wait())")]
    public void CatalogedSynchronousScriptDelegateIsAReviewLead(string callback)
    {
        AssertWait(Compile(CatalogType + $$"""
            class C { object M(Task<int> source) => new OrchardCore.Scripting.GlobalMethod {
                Method = services => {{callback}}
            }; }
            """), true, "synchronousScriptingContract");
    }

    [Theory]
    [InlineData("AsyncMethod", "services => (Func<int>)(() => source.Result)")]
    [InlineData("Method", "services => (Func<Task<int>>)(() => Task.FromResult(source.Result))")]
    [InlineData("Method", "services => (Func<Task<int>>)(async () => { var result = source.Result; await Task.Yield(); return result; })")]
    [InlineData("Method", "services => (Func<Func<int>>)(() => () => source.Result)")]
    [InlineData("Method", "services => { Func<int> callback = () => source.Result; return callback; }")]
    public void DifferentPropertyAsyncDelegateOrDeferredWorkDoesNotQualify(string property, string factory)
    {
        AssertWait(Compile(CatalogType + $$"""
            class C { object M(Task<int> source) => new OrchardCore.Scripting.GlobalMethod {
                {{property}} = {{factory}}
            }; }
            """), false, "");
    }

    [Fact]
    public void SimilarNamedApiAndPairedAsyncPropertyDoNotQualify()
    {
        AssertWait(Compile(CatalogType.Replace("OrchardCore.Scripting", "Application") + """
            class C { object M(Task<int> source) => new Application.GlobalMethod {
                Method = services => (Func<int>)(() => source.Result),
                AsyncMethod = services => (Func<Task<int>>)(() => source)
            }; }
            """), false, "");
    }
}
