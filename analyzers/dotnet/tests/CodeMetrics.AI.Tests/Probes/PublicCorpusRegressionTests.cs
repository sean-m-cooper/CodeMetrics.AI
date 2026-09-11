using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public class PublicCorpusRegressionTests
{
    private static readonly string Root = Path.GetFullPath(Path.GetTempPath());

    private static Compilation Compile(string code) => RoslynTestHelper.CompileCodeAtPath(code, Path.Combine(Root, "Corpus.cs")).Compilation;

    [Theory]
    [InlineData("task.IsCompletedSuccessfully")]
    [InlineData("task.IsCompleted")]
    [InlineData("task.Status == TaskStatus.RanToCompletion")]
    [InlineData("task.IsCompletedSuccessfullyHelper()")]
    public void CompletedTaskGuards_DoNotReportBlocking(string guard)
    {
        var compilation = Compile($$"""
            using System.Threading.Tasks;
            static class Helpers {
                public static bool IsCompletedSuccessfullyHelper(this Task task) => task.Status == TaskStatus.RanToCompletion;
            }
            class Reader {
                public int Read(Task<int> task) { if ({{guard}}) { return task.Result; } return 0; }
            }
            """);
        var projects = new[] { ("Reader", compilation) };
        PerformanceAsyncProbe.Analyze(projects, Root).Findings.Should().NotContain(f => f.Category == "syncOverAsync");
        ErrorHandlingProbe.Analyze(projects, Root).Findings.Should().NotContain(f => f.Category == "syncBlockingCall");
    }

    [Theory]
    [InlineData("task.LooksCompleted()", "")]
    [InlineData("other.IsCompletedSuccessfully", "")]
    [InlineData("task.IsCompletedSuccessfully", "task = other;")]
    [InlineData("task.IsCompletedSuccessfully || other.IsCompletedSuccessfully", "")]
    public void UnprovenOrInvalidatedTaskGuards_StillReportBlocking(string guard, string prefix)
    {
        var compilation = Compile($$"""
            using System.Threading.Tasks;
            static class Helpers { public static bool LooksCompleted(this Task task) => true; }
            class Reader {
                public int Read(Task<int> task, Task<int> other) { if ({{guard}}) { {{prefix}} return task.Result; } return 0; }
            }
            """);
        PerformanceAsyncProbe.Analyze(new[] { ("Reader", compilation) }, Root).Findings.Should().Contain(f => f.Category == "syncOverAsync");
    }

    [Fact]
    public void CompletionGuardOutsideDeferredLambda_DoesNotProveCompletionAtExecution()
    {
        var compilation = Compile("""
            using System;
            using System.Threading.Tasks;
            class Reader {
                public Func<int> Read(Task<int> task, Task<int> other) {
                    if (task.IsCompletedSuccessfully) {
                        Func<int> read = () => task.Result;
                        task = other;
                        return read;
                    }
                    return () => 0;
                }
            }
            """);
        PerformanceAsyncProbe.Analyze(new[] { ("Reader", compilation) }, Root).Findings.Should().Contain(f => f.Category == "syncOverAsync");
    }

    [Fact]
    public void CircuitControllerAndExecutionContext_AreNotWebDataLayerRoles()
    {
        var compilation = Compile("""
            using System;
            namespace Polly {
                public class Context { }
                public class CircuitController { public CircuitController(Action<Context> reset) { } }
            }
            """);
        ArchitectureProbe.Analyze(new[] { ("Polly", compilation) }, [], Root, projectPaths: [])
            .Findings.Should().NotContain(f => f.Category == "controllerDataDependency");
        MetricsCollector.Collect("Polly", compilation, Root).Types.Should().OnlyContain(t => !t.IsWebController);
    }

    [Fact]
    public void SemanticWebController_StillReportsRepositoryButNotGenericContextCallback()
    {
        var compilation = Compile("""
            using System;
            namespace Microsoft.AspNetCore.Mvc { public class ControllerBase { } }
            namespace App {
                public interface IRepository<T> { }
                public class Context { }
                public class OrdersEndpoint : Microsoft.AspNetCore.Mvc.ControllerBase {
                    public OrdersEndpoint(IRepository<int> data, Action<Context> callback) { }
                }
            }
            """);
        ArchitectureProbe.Analyze(new[] { ("App", compilation) }, [], Root, projectPaths: [])
            .Findings.Count(f => f.Category == "controllerDataDependency").Should().Be(1);
        MetricsCollector.Collect("App", compilation, Root).Types.Single(t => t.Type == "OrdersEndpoint").IsWebController.Should().BeTrue();
    }

    [Fact]
    public void MultiTargetTests_AreMatchedAndCountedAsUniqueSites()
    {
        var production = Compile("public class Library { public int Get() => 1; }");
        var tests = Compile("""
            using System;
            namespace Xunit { public class FactAttribute : Attribute { public string Skip { get; set; } } }
            class LibraryTests {
                [Xunit.Fact(Skip="reason")] public void Works() { System.Diagnostics.Debug.Assert(true); }
            }
            """);
        var result = TestingProbe.Analyze(new[] { ("Library(net8.0)", production), ("Library(net10.0)", production),
            ("Library.Tests(net8.0)", tests), ("Library.Tests (net10.0)", tests) }, ["Library(net8.0)", "Library(net10.0)"], Root);
        result.Findings.Should().NotContain(f => f.Category == "uncoveredProject");
        result.Basis.Should().Contain("testProjects=1").And.Contain("testMethods=1").And.Contain("skipped=1");
        result.Score.Should().Be(8);
    }

    [Fact]
    public void ArchitectureGraph_IgnoresCyclesOutsideSelectedProjects()
    {
        var root = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var app = Path.Combine(root, "App.csproj");
            File.WriteAllText(app, "<Project/>");
            File.WriteAllText(Path.Combine(root, "A.csproj"), "<Project><ItemGroup><ProjectReference Include='B.csproj'/></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(root, "B.csproj"), "<Project><ItemGroup><ProjectReference Include='A.csproj'/></ItemGroup></Project>");
            ArchitectureProbe.Analyze([], [], root).Findings.Should().Contain(f => f.Category == "projectCycle");
            ArchitectureProbe.Analyze([], [], root, [app]).Findings.Should().NotContain(f => f.Category == "projectCycle");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void NestedSolution_UsesRepositoryDocumentation()
    {
        var root = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        Directory.CreateDirectory(Path.Combine(root, "Src"));
        File.WriteAllText(Path.Combine(root, "README.md"), string.Join('\n', Enumerable.Repeat("Documentation", 30)));
        try
        {
            DocumentationProbe.Analyze(Path.Combine(root, "Src"), []).Basis.Should().Contain("hasReadme=True");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DependencyStaticChecks_DoNotScanUnselectedBuildTools()
    {
        var root = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var selected = Path.Combine(root, "App.csproj");
        File.WriteAllText(selected, "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "Unselected.csproj"), "<Project><PropertyGroup><TargetFramework>netcoreapp2.0</TargetFramework></PropertyGroup></Project>");
        try
        {
            DependencyProbe.AnalyzeOutput("", "", "", root, false, projectPaths: [selected])
                .Findings.Should().NotContain(f => f.Category == "unsupportedTargetFramework");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DependencyFailure_IncludesErrorsWrittenToStdout()
    {
        var result = DependencyProbe.AnalyzeOutput("", "", "", Root, true,
            [new("--outdated", "error: SDK resolver cannot load System.Runtime", "", 1)]);
        result.Basis.Should().Contain("SDK resolver cannot load System.Runtime");
        result.Status.Should().Be("failed");
    }
}
