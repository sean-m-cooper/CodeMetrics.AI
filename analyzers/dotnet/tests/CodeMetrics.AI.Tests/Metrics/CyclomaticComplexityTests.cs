// tests/CodeMetrics.AI.Tests/Metrics/CyclomaticComplexityTests.cs
using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Tests.Metrics;

public class CyclomaticComplexityTests
{
    private static int ComputeCC(string methodBody)
    {
        var code = $"public class C {{ public void M() {{ {methodBody} }} }}";
        var tree = RoslynTestHelper.ParseCode(code);
        var method = RoslynTestHelper.FindFirstNode<MethodDeclarationSyntax>(tree);
        var walker = new CyclomaticComplexityWalker();
        walker.Visit(method);
        return walker.Complexity;
    }

    [Fact] public void EmptyMethod_Returns1() => ComputeCC("").Should().Be(1);
    [Fact] public void SingleIf_Returns2() => ComputeCC("if (true) { }").Should().Be(2);
    [Fact] public void IfElse_Returns2() => ComputeCC("if (true) { } else { }").Should().Be(2);
    [Fact] public void IfElseIf_Returns3() => ComputeCC("if (true) { } else if (false) { }").Should().Be(3);
    [Fact] public void WhileLoop_Returns2() => ComputeCC("while (true) { }").Should().Be(2);
    [Fact] public void DoWhile_Returns2() => ComputeCC("do { } while (true);").Should().Be(2);
    [Fact] public void ForLoop_Returns2() => ComputeCC("for (int i = 0; i < 10; i++) { }").Should().Be(2);
    [Fact] public void ForEach_Returns2() => ComputeCC("foreach (var x in new int[0]) { }").Should().Be(2);
    [Fact] public void SwitchWithThreeCases_Returns4() => ComputeCC("int x = 1; switch(x) { case 1: break; case 2: break; case 3: break; }").Should().Be(4);
    [Fact] public void CatchClause_Returns2() => ComputeCC("try { } catch (System.Exception) { }").Should().Be(2);
    [Fact] public void Ternary_Returns2() => ComputeCC("var x = true ? 1 : 2;").Should().Be(2);
    [Fact] public void LogicalAnd_Returns2() => ComputeCC("var x = true && false;").Should().Be(2);
    [Fact] public void LogicalOr_Returns2() => ComputeCC("var x = true || false;").Should().Be(2);
    [Fact] public void NullCoalescing_Returns2() => ComputeCC("string? s = null; var x = s ?? \"\";").Should().Be(2);
    [Fact] public void NullCoalescingAssignment_Returns2() => ComputeCC("string? s = null; s ??= \"\";").Should().Be(2);
    [Fact] public void SwitchExpression_ExcludesDiscard() => ComputeCC("int x = 1; var y = x switch { 1 => \"a\", 2 => \"b\", _ => \"c\" };").Should().Be(3);
    [Fact] public void Combined_IfAndOrTernary_Returns5() => ComputeCC("if (true && false) { var x = true ? 1 : 2; } else if (true) { }").Should().Be(5);
}
