using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Tests.Metrics;

public class LinesOfCodeTests
{
    private static (int Source, int Executable) Count(string methodBody)
    {
        var code = $"public class C {{\n  public void M() {{\n{methodBody}\n  }}\n}}";
        var tree = RoslynTestHelper.ParseCode(code);
        var method = RoslynTestHelper.FindFirstNode<MethodDeclarationSyntax>(tree);
        return (LinesOfCodeCounter.CountSourceLines(method), LinesOfCodeCounter.CountExecutableLines(method));
    }

    [Fact]
    public void EmptyMethod_ZeroSourceAndExecutable()
    {
        var (src, exec) = Count("");
        src.Should().Be(0);
        exec.Should().Be(0);
    }

    [Fact]
    public void SingleStatement()
    {
        var (src, exec) = Count("    int x = 1;");
        src.Should().Be(1);
        exec.Should().Be(1);
    }

    [Fact]
    public void CommentsAreExcluded()
    {
        var (src, _) = Count("    // this is a comment\n    int x = 1;");
        src.Should().Be(1);
    }

    [Fact]
    public void BlockCommentsAreExcluded()
    {
        var (src, _) = Count("    /* comment */\n    int x = 1;");
        src.Should().Be(1);
    }

    [Fact]
    public void BraceOnlyLinesExcluded()
    {
        var (src, _) = Count("    if (true)\n    {\n      int x = 1;\n    }");
        src.Should().Be(2); // if line + int x line
    }

    [Fact]
    public void BlankLinesExcluded()
    {
        var (src, _) = Count("    int x = 1;\n\n    int y = 2;");
        src.Should().Be(2);
    }

    [Fact]
    public void ExecutableLines_ExcludeBlockSyntax()
    {
        var (_, exec) = Count("    if (true) { int x = 1; int y = 2; }");
        exec.Should().Be(3); // if statement + 2 declarations
    }
}
