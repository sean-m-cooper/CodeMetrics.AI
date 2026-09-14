using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public class ArchitectureLayeringTests
{
    [Fact]
    public void Analyze_MixedTypes_PreservesRulePriorityExclusionsAndFindingOrder()
    {
        const string code = """
            using Alias = Dependencies.UserRepository;
            namespace Dependencies
            {
                public class UserRepository { }
                public class SqlGateway { }
                public class ILoggerRepository { }
                public interface Gateway { }
                public abstract class AbstractRepository { }
            }
            namespace Microsoft.AspNetCore.Mvc
            {
                public class ControllerAttribute : System.Attribute { }
                public class NonControllerAttribute : System.Attribute { }
            }
            namespace Microsoft.Extensions.Hosting { public class HostContext { } }
            [Microsoft.AspNetCore.Mvc.Controller] public class EndpointBase { }
            public partial class OrderService : EndpointBase
            {
                public OrderService(Alias repository, Dependencies.SqlGateway gateway,
                    Dependencies.ILoggerRepository logger) { }
            }
            public class WorkerService
            {
                public WorkerService(Dependencies.Gateway contract, Dependencies.AbstractRepository abstraction,
                    Microsoft.Extensions.Hosting.HostContext host, Dependencies.SqlGateway gateway, Alias repository) { }
            }
            public class Unrelated(Dependencies.SqlGateway gateway, Alias repository) { }
            [Microsoft.AspNetCore.Mvc.NonController] public class ExcludedService : EndpointBase
            {
                public ExcludedService(Dependencies.SqlGateway gateway) { }
            }
            public partial class OrderService
            {
                public OrderService(Alias repository) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().HaveCount(5);
        result.Findings.Select(f => (f.Category, f.Type, f.Severity)).Should().Equal(
            ("controllerDataDependency", "OrderService", "error"),
            ("controllerDataDependency", "OrderService", "error"),
            ("concreteInfrastructureDependency", "WorkerService", "warning"),
            ("concreteInfrastructureDependency", "ExcludedService", "warning"),
            ("controllerDataDependency", "OrderService", "error"));
        result.Findings.Select(f => f.Line).Should().Equal(19, 20, 25, 30, 34);
        result.Findings[0].Message.Should().Be(
            "Controller 'OrderService' directly depends on data-layer type 'Alias'. Controllers should not depend on DbContext, Repository, or DAL types.");
        result.Findings[1].Message.Should().Be(
            "Controller 'OrderService' directly depends on data-layer type 'Dependencies.ILoggerRepository'. Controllers should not depend on DbContext, Repository, or DAL types.");
        result.Findings[2].Message.Should().Be(
            "Service 'WorkerService' depends on concrete infrastructure type 'Dependencies.SqlGateway'. Prefer depending on abstractions (interfaces).");
        result.Findings.Should().AllSatisfy(f =>
        {
            f.Project.Should().Be("Application");
            f.File.Should().Be(SourcePath);
        });
    }

    [Theory]
    [InlineData("class")]
    [InlineData("record")]
    [InlineData("record struct")]
    public void Analyze_PrimaryAndRegularConstructors_PreservesRegularBeforePrimaryOrder(string declarationKind)
    {
        var code = $$"""
            public class PrimaryRepository { }
            public class RegularRepository { }
            namespace Microsoft.AspNetCore.Mvc { public class ControllerAttribute : System.Attribute { } }
            [Microsoft.AspNetCore.Mvc.Controller]
            public {{declarationKind}} Endpoint(PrimaryRepository primary)
            {
                public Endpoint(RegularRepository regular) : this(new PrimaryRepository()) { }
            }
            public {{declarationKind}} WorkerService(PrimaryRepository primary)
            {
                public WorkerService(RegularRepository regular) : this(new PrimaryRepository()) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Select(f => (f.Category, f.Type, f.Line)).Should().Equal(
            ("controllerDataDependency", "Endpoint", (int?)7),
            ("controllerDataDependency", "Endpoint", (int?)5),
            ("concreteInfrastructureDependency", "WorkerService", (int?)11),
            ("concreteInfrastructureDependency", "WorkerService", (int?)9));
        result.Findings.Select(f => f.Message).Should().Equal(
            "Controller 'Endpoint' directly depends on data-layer type 'RegularRepository'. Controllers should not depend on DbContext, Repository, or DAL types.",
            "Controller 'Endpoint' directly depends on data-layer type 'PrimaryRepository'. Controllers should not depend on DbContext, Repository, or DAL types.",
            "Service 'WorkerService' depends on concrete infrastructure type 'RegularRepository'. Prefer depending on abstractions (interfaces).",
            "Service 'WorkerService' depends on concrete infrastructure type 'PrimaryRepository'. Prefer depending on abstractions (interfaces).");
    }

    private static readonly string SolutionDir = Path.Combine(Path.GetTempPath(), "architecture-layering-fixture");
    private static readonly string SourcePath = Path.Combine(SolutionDir, "Application.cs");

    private static DimensionResult Analyze(string code)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(code, SourcePath);
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return ArchitectureProbe.Analyze([("Application", compilation)], [], SolutionDir);
    }
}
