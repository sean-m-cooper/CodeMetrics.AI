using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Tests.Metrics;

public class CouplingTraversalTests
{
    private static CouplingAnalysis Analyze(string code)
    {
        var (tree, model, compilation) = RoslynTestHelper.CompileCode(code,
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location));
        compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        var declaration = RoslynTestHelper.FindAllNodes<ClassDeclarationSyntax>(tree)
            .Single(type => type.Identifier.Text == "Subject");
        return ClassCouplingCalculator.Analyze(declaration, model);
    }

    [Fact]
    public void ArraysAndRepeatedGenericDefinitionsRetainDistinctArgumentsButExcludeSelfAndPrimitives()
    {
        var result = Analyze("""
            interface IFirst { void Run(); }
            interface ISecond { void Run(); }
            class Worker<T> { public void Run() {} }
            class Subject<T> {
                Worker<IFirst>[,][] first;
                Worker<ISecond> second;
                Subject<T>[] self;
                Nested nested;
                T parameter;
                dynamic dynamicValue;
                int number;
                class Nested {}
            }
            """);

        result.RawTypes.Should().Equal("IFirst", "ISecond", "Worker<T>");
        result.StructuralTypes.Should().Equal("IFirst", "ISecond", "Worker<T>");
        result.ExcludedTypes.Should().BeEmpty();
    }

    [Fact]
    public void PassiveGenericCarrierStopsStructuralExpansionButKeepsRawArgumentProvenance()
    {
        var result = Analyze("""
            interface IHidden { void Run(); }
            interface IUsed { void Run(); }
            class Payload<T> { public T Value { get; set; } }
            class Worker<T> { public void Run() {} }
            class Subject {
                Payload<IHidden> payload;
                Worker<IUsed> worker;
            }
            """);

        result.RawTypes.Should().Equal("IHidden", "IUsed", "Payload<T>", "Worker<T>");
        result.StructuralTypes.Should().Equal("IUsed", "Worker<T>");
        result.ExcludedTypes.Values.SelectMany(types => types).Should().BeEquivalentTo("IHidden", "Payload<T>");
    }

    [Fact]
    public void RawTraversalPreservesDeclarationMetadataAndInvokedMethodSignatureTypes()
    {
        var result = Analyze("""
            class ClassTagAttribute : System.Attribute {}
            class MemberTagAttribute : System.Attribute {}
            class Parent {}
            class Result {}
            class Argument {}
            static class Factory { public static Result Make(Argument argument = null) => null; }
            [ClassTag]
            class Subject : Parent {
                [MemberTag] public void Run() { Factory.Make(); }
            }
            """);

        result.RawTypes.Should().Equal("Argument", "ClassTagAttribute", "Factory", "MemberTagAttribute", "Parent", "Result");
    }
}
