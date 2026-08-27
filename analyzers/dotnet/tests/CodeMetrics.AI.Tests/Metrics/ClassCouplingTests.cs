using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Tests.Metrics;

public class ClassCouplingTests
{
    private static int Coupling(string classCode)
    {
        var (tree, model, _) = RoslynTestHelper.CompileCode(classCode);
        var typeDecl = RoslynTestHelper.FindFirstNode<ClassDeclarationSyntax>(tree);
        return ClassCouplingCalculator.Calculate(typeDecl, model);
    }

    [Fact]
    public void NoCoupledTypes_Returns0()
    {
        Coupling("public class C { public int X { get; set; } }").Should().Be(0);
    }

    [Fact]
    public void PrimitivesExcluded()
    {
        Coupling("public class C { public string S { get; set; } public int I { get; set; } }")
            .Should().Be(0);
    }

    [Fact]
    public void SelfReferenceExcluded()
    {
        Coupling("public class C { public C Other { get; set; } }").Should().Be(0);
    }

    [Fact]
    public void SingleExternalType()
    {
        var code = @"
            public class Dep { }
            public class C { public Dep D { get; set; } }
        ";
        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var types = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree).ToList();
        var classC = types.First(t => t.Identifier.Text == "C");
        ClassCouplingCalculator.Calculate(classC, model).Should().Be(1);
    }

    [Fact]
    public void BaseClassCounted()
    {
        var code = @"
            public class Base { }
            public class C : Base { }
        ";
        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var types = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree).ToList();
        var classC = types.First(t => t.Identifier.Text == "C");
        ClassCouplingCalculator.Calculate(classC, model).Should().Be(1);
    }

    [Fact]
    public void GenericTypeArgsCounted()
    {
        var code = @"
            public class Dep { }
            public class C { public System.Collections.Generic.List<Dep> Items { get; set; } }
        ";
        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var types = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree).ToList();
        var classC = types.First(t => t.Identifier.Text == "C");
        var coupling = ClassCouplingCalculator.Calculate(classC, model);
        coupling.Should().BeGreaterThanOrEqualTo(2); // List<T> + Dep
    }

    [Fact]
    public void FromServicesMethodParameter_IsExcludedFromCoupling()
    {
        const string code = """
            namespace Microsoft.AspNetCore.Mvc { public sealed class FromServicesAttribute : System.Attribute { } }
            public class BigInjectedService { public void Run() { } }
            public class MyController {
                public void Get([Microsoft.AspNetCore.Mvc.FromServices] BigInjectedService svc) { svc.Run(); }
            }
            """;

        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var controller = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
            .Single(c => c.Identifier.Text == "MyController");

        ClassCouplingCalculator.Calculate(controller, model).Should().Be(0);
        ClassCouplingCalculator.CalculateTypes(controller, model).Should().BeEmpty();
    }

    [Fact]
    public void CalculateAction_IncludesFromServicesAndActionTypes()
    {
        const string code = """
            namespace Microsoft.AspNetCore.Mvc { public sealed class FromServicesAttribute : System.Attribute { } }
            public class InjectedService { }
            public class Response { }
            public class MyController {
                public Response Get(
                    [Microsoft.AspNetCore.Mvc.FromServices] InjectedService service) => new Response();
            }
            """;

        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var action = RoslynTestHelper.FindAllNodes<MethodDeclarationSyntax>(tree)
            .Single(method => method.Identifier.Text == "Get");
        var controller = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
            .Single(type => type.Identifier.Text == "MyController");

        ClassCouplingCalculator.CalculateAction(action, model).Should().Be(2);
        var classTypes = ClassCouplingCalculator.CalculateTypes(controller, model);
        classTypes.Should().ContainSingle().Which.Should().Be("Response");
        ClassCouplingCalculator.Calculate(controller, model).Should().Be(classTypes.Count);
    }

    [Fact]
    public void Analyze_ControllerSeparatesBehavioralDependenciesFromTransportNoise()
    {
        const string code = """
            namespace Microsoft.AspNetCore.Mvc {
                public interface IActionResult { }
                public class Controller {
                    protected IActionResult BadRequest(object value) => null!;
                }
                public sealed class HttpPostAttribute : System.Attribute { }
                public sealed class FromServicesAttribute : System.Attribute { }
            }
            public interface IServiceA { void Run(); }
            public interface ILogger { }
            public interface IActionService { void Run(); }
            public sealed class RequestDto { public System.Guid Id { get; set; } }
            public static class StaticHelper { public static void Run() { } }

            public sealed class OrdersController : Microsoft.AspNetCore.Mvc.Controller {
                private readonly IServiceA service;
                public OrdersController(IServiceA service, ILogger logger) {
                    this.service = service;
                }

                [Microsoft.AspNetCore.Mvc.HttpPost]
                public Microsoft.AspNetCore.Mvc.IActionResult Save(
                    RequestDto request,
                    System.Threading.CancellationToken cancellationToken,
                    [Microsoft.AspNetCore.Mvc.FromServices] IActionService actionService) {
                    service.Run();
                    actionService.Run();
                    StaticHelper.Run();
                    return BadRequest(new { success = false, request.Id });
                }
            }
            """;

        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var controller = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
            .Single(type => type.Identifier.Text == "OrdersController");

        var analysis = ClassCouplingCalculator.Analyze(controller, model);

        analysis.StructuralTypes.Should().BeEquivalentTo(
            "IActionService", "ILogger", "IServiceA", "StaticHelper");
        analysis.RawTypes.Count.Should().BeGreaterThan(analysis.StructuralTypes.Count);
        analysis.ExcludedTypes.Should().ContainKey("attribute");
        analysis.ExcludedTypes.Should().ContainKey("compilerGenerated");
        analysis.ExcludedTypes.Should().ContainKey("frameworkPresentation");
        analysis.ExcludedTypes.Should().ContainKey("passiveDataCarrier");
        analysis.ExcludedTypes.Should().ContainKey("valueOrContainer");
    }

    [Fact]
    public void Analyze_GenericContainerIsNoiseButBehavioralTypeArgumentIsStructural()
    {
        const string code = """
            public interface IStrategy { void Run(); }
            public sealed class Coordinator {
                private readonly System.Collections.Generic.IReadOnlyList<IStrategy> strategies;
            }
            """;

        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var coordinator = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
            .Single(type => type.Identifier.Text == "Coordinator");

        var analysis = ClassCouplingCalculator.Analyze(coordinator, model);

        analysis.StructuralTypes.Should().Equal("IStrategy");
        analysis.ExcludedTypes["valueOrContainer"]
            .Should().Contain("System.Collections.Generic.IReadOnlyList<T>");
    }

    [Fact]
    public void Analyze_MethodPayloadOnlyBecomesStructuralWhenBehaviorIsUsed()
    {
        const string code = """
            public sealed class BehavioralRequest { public void Validate() { } }
            public sealed class Handler {
                public void PassThrough(BehavioralRequest request) { }
                public void Validate(BehavioralRequest request) { request.Validate(); }
            }
            """;

        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var handler = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
            .Single(type => type.Identifier.Text == "Handler");

        var analysis = ClassCouplingCalculator.Analyze(handler, model);

        analysis.StructuralTypes.Should().ContainSingle().Which.Should().Be("BehavioralRequest");
    }

    [Fact]
    public void Analyze_ReducedExtensionMethodCountsReceiverWithoutProviderInflation()
    {
        const string code = """
            public interface IService { }
            public static class ServiceExtensions {
                public static void Run(this IService service) { }
            }
            public sealed class Coordinator {
                private readonly IService service;
                public Coordinator(IService service) { this.service = service; }
                public void Run() { service.Run(); }
            }
            """;

        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var coordinator = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
            .Single(type => type.Identifier.Text == "Coordinator");

        var analysis = ClassCouplingCalculator.Analyze(coordinator, model);

        analysis.StructuralTypes.Should().Contain("IService");
        analysis.StructuralTypes.Should().NotContain("ServiceExtensions");
    }

    [Fact]
    public void CalculateStructuralAction_ExcludesPayloadAndResultTypes()
    {
        const string code = """
            namespace Microsoft.AspNetCore.Mvc {
                public interface IActionResult { }
                public sealed class FromServicesAttribute : System.Attribute { }
            }
            public sealed class RequestDto { public int Id { get; set; } }
            public interface IActionService { void Run(); }
            public sealed class Controller {
                public Microsoft.AspNetCore.Mvc.IActionResult Save(
                    RequestDto request,
                    [Microsoft.AspNetCore.Mvc.FromServices] IActionService service) {
                    service.Run();
                    return null!;
                }
            }
            """;

        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var action = RoslynTestHelper.FindAllNodes<MethodDeclarationSyntax>(tree)
            .Single(method => method.Identifier.Text == "Save");

        ClassCouplingCalculator.CalculateStructuralAction(action, model).Should().Be(1);
        ClassCouplingCalculator.CalculateAction(action, model).Should().BeGreaterThan(1);
    }
}
