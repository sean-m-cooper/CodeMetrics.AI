using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Tests;

public class SourceRegressionTests
{
    [Fact]
    public void CsvWriter_DoesNotAwaitInsideLoops()
    {
        var source = ReadSource("Output", "CsvWriter.cs");
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();

        var loopAwaitExpressions = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(IsLoop)
            .SelectMany(loop => loop.DescendantNodes().OfType<AwaitExpressionSyntax>())
            .ToList();

        loopAwaitExpressions.Should().BeEmpty(
            "CSV rows should be buffered and written once at process end");
    }

    [Fact]
    public void SolutionAnalyzer_RunAsync_AcceptsCancellationToken()
    {
        var source = ReadSource("SolutionAnalyzer.cs");
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();

        var runAsync = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.Text == "RunAsync");

        var parameterTypes = runAsync.ParameterList.Parameters
            .Select(parameter => parameter.Type?.ToString())
            .ToList();

        parameterTypes.Should().Contain("CancellationToken");
    }

    [Fact]
    public void Program_PassesCliCancellationTokenToSolutionAnalyzer()
    {
        var source = ReadSource("Program.cs");

        source.Should().Contain(
            "await analyzer.RunAsync(options, cancellationToken)",
            "the System.CommandLine cancellation token should flow into analysis");
    }

    private static bool IsLoop(StatementSyntax statement)
    {
        return statement is ForStatementSyntax
            or ForEachStatementSyntax
            or ForEachVariableStatementSyntax
            or WhileStatementSyntax
            or DoStatementSyntax;
    }

    private static string ReadSource(params string[] pathParts)
    {
        var root = FindAnalyzerRoot();
        return File.ReadAllText(Path.Combine([root, "src", "CodeMetrics.AI", .. pathParts]));
    }

    private static string FindAnalyzerRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (directory != null)
        {
            var candidate = Path.Combine(directory, "src", "CodeMetrics.AI");
            if (Directory.Exists(candidate))
                return directory;

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate analyzers/dotnet root.");
    }
}
