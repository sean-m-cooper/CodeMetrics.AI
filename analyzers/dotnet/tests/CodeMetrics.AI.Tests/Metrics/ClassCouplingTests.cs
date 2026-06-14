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
            public class BigInjectedService { }
            public class MyController {
                public void Get([Microsoft.AspNetCore.Mvc.FromServices] BigInjectedService svc) { }
            }
            """;

        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var controller = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
            .Single(c => c.Identifier.Text == "MyController");

        ClassCouplingCalculator.Calculate(controller, model).Should().Be(0);
    }
}
