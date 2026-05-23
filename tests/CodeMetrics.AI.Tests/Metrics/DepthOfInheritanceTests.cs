using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Tests.Metrics;

public class DepthOfInheritanceTests
{
    private static int Doi(string code, string typeName)
    {
        var (tree, model, _) = RoslynTestHelper.CompileCode(code);
        var classDecl = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
            .FirstOrDefault(t => t.Identifier.Text == typeName);
        if (classDecl != null)
        {
            var symbol = model.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
            return DepthOfInheritanceCalculator.Calculate(symbol!);
        }
        var ifaceDecl = RoslynTestHelper.FindAllNodes<InterfaceDeclarationSyntax>(tree)
            .First(t => t.Identifier.Text == typeName);
        var ifaceSymbol = model.GetDeclaredSymbol(ifaceDecl) as INamedTypeSymbol;
        return DepthOfInheritanceCalculator.Calculate(ifaceSymbol!);
    }

    [Fact]
    public void PlainClass_InheritsObject_Returns1()
    {
        // A plain class implicitly inherits System.Object, so depth = 1
        var code = "public class MyClass { }";
        Doi(code, "MyClass").Should().Be(1);
    }

    [Fact]
    public void ClassInheritingAnotherClass_Returns2()
    {
        var code = @"
            public class Base { }
            public class Derived : Base { }
        ";
        Doi(code, "Derived").Should().Be(2);
    }

    [Fact]
    public void Interface_Returns1()
    {
        var code = "public interface IMyInterface { }";
        Doi(code, "IMyInterface").Should().Be(1);
    }

    [Fact]
    public void TwoLevelInheritanceChain_Returns3()
    {
        // C : B : A, where A implicitly inherits object
        // C -> B -> A -> object = depth 3
        var code = @"
            public class A { }
            public class B : A { }
            public class C : B { }
        ";
        Doi(code, "C").Should().Be(3);
    }
}
