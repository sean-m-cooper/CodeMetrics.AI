using System.Text.Json;
using CodeMetrics.AI.Output;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Rules;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Tests;

public class RuleCatalogTests
{
    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "shared", "scorecard-schema")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "analyzers", "dotnet", "src", "CodeMetrics.AI");
    }

    [Fact]
    public void CatalogCoversEveryEmittedCategoryAndAllNineDimensions()
    {
        var rules = RuleCatalog.Document.Rules;
        rules.Select(rule => rule.Code).Should().OnlyHaveUniqueItems();
        rules.Select(rule => rule.RuleId).Should().OnlyHaveUniqueItems();
        rules.Select(rule => rule.Dimension).Distinct().Should().HaveCount(9);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SourceRoot(), "Probes"), "*.cs"))
        {
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file), cancellationToken: TestContext.Current.CancellationToken)
                .GetRoot(TestContext.Current.CancellationToken);
            var categories = root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .Where(assignment => assignment.Left.ToString() == "Category")
                .SelectMany(assignment => assignment.Right.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>())
                .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                .Select(literal => literal.Token.ValueText);
            foreach (var category in categories)
                rules.Should().Contain(rule => rule.Kind == "finding" && rule.Category == category, $"{file} emits {category}");
        }
    }

    [Theory]
    [InlineData("CMAI1001", "dotnet/architecture/highCoupling")]
    [InlineData("CMAI2001", "dotnet/codeQuality/decomposition")]
    [InlineData("CMAI5001", "dotnet/errorHandling/emptyCatch")]
    [InlineData("CMAI5005", "dotnet/errorHandling/syncBlockingCall")]
    [InlineData("CMAI8001", "dotnet/performanceAsync/syncOverAsync")]
    [InlineData("CMAI8006", "dotnet/performanceAsync/awaitedIoInsideLoop")]
    public void CodesRetainTheirPublicIdentity(string code, string identity)
    {
        RuleCatalog.Find(code)!.RuleId.Should().Be(identity);
        RuleCatalog.Find(identity)!.Code.Should().Be(code);
        RuleCatalog.Find(code.ToLowerInvariant())!.Code.Should().Be(code);
    }

    [Fact]
    public void JsonReportsTheActualAnalyzerVersionAndSupportedCapabilities()
    {
        var catalog = JsonDocument.Parse(RuleCatalog.Render("json")).RootElement;
        catalog.GetProperty("tool").GetProperty("version").GetString().Should().Be(new ToolInfo().Version);
        catalog.GetProperty("rules").GetArrayLength().Should().Be(44);
        RuleCatalog.Document.Rules.Where(rule => rule.Annotation.Supported).Select(rule => rule.Code)
            .Should().BeEquivalentTo("CMAI5001", "CMAI5005", "CMAI8001", "CMAI8006");
        RuleCatalog.Find("CMAI1001")!.Annotation.Example.Should().BeNull();
        RuleCatalog.Document.Rules.Where(rule => rule.Annotation.Supported).Should().OnlyContain(rule =>
            rule.Annotation.RationaleRequired && rule.Annotation.Scopes.Length > 0 && rule.Annotation.Example != null);
    }

    [Fact]
    public void PackagedMarkdownMatchesTheCatalog()
    {
        File.ReadAllText(Path.Combine(SourceRoot(), "Rules", "rules.md")).Replace("\r\n", "\n")
            .Should().Be(RuleCatalog.Render("markdown"));
        var single = JsonDocument.Parse(RuleCatalog.Render("json", "CMAI5001")).RootElement;
        single.GetProperty("rules").GetArrayLength().Should().Be(1);
        var unknown = () => RuleCatalog.Render("json", "CMAI9999");
        unknown.Should().Throw<ArgumentException>();
        var invalidFormat = () => RuleCatalog.Render("yaml");
        invalidFormat.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("CMAI5001 -- Required fallback", true)]
    [InlineData("cmai5001 — Required fallback", true)]
    [InlineData("CMAI5001", false)]
    [InlineData("CMAI5001 -- ", false)]
    [InlineData("CMAI1001 -- Different concern", false)]
    [InlineData("CMAI9999 -- Unknown code", false)]
    [InlineData("emptyCatch", true)]
    public void EmptyCatchAnnotationsRequireTheMatchingCodeAndRationale(string directive, bool suppressed)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode($$"""
            class C { void Run() { try { }
            // codemetrics-ignore: {{directive}}
            catch { }
            } }
            """);
        var result = ErrorHandlingProbe.Analyze([("Test", compilation)]);
        result.Findings.Any(finding => finding.Category == "emptyCatch").Should().Be(!suppressed);
    }

    [Theory]
    [InlineData("CMAI5005", false, true)]
    [InlineData("CMAI8001", true, false)]
    [InlineData("CMAI5005, CMAI8001", false, false)]
    public void SharedSyntaxKeepsDimensionSpecificSuppression(string codes, bool errorFinding, bool performanceFinding)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode($$"""
            using System.Threading.Tasks;
            class C { void Run(Task task) {
                // codemetrics-ignore: {{codes}} -- Synchronous host contract
                task.Wait();
            } }
            """);
        ErrorHandlingProbe.Analyze([("Test", compilation)]).Findings
            .Any(finding => finding.Category == "syncBlockingCall").Should().Be(errorFinding);
        PerformanceAsyncProbe.Analyze([("Test", compilation)]).Findings
            .Any(finding => finding.Category == "syncOverAsync").Should().Be(performanceFinding);
    }

    [Fact]
    public void MethodAnnotationDoesNotSuppressAnotherMember()
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode("""
            class C {
                // codemetrics-ignore: CMAI5001 -- Required fallback
                void First() { try {} catch {} }
                void Second() { try {} catch {} }
            }
            """);
        ErrorHandlingProbe.Analyze([("Test", compilation)]).Findings.Count(finding => finding.Category == "emptyCatch")
            .Should().Be(1);
    }

    [Fact]
    public void SequentialIoCodeIsRecognizedAtItsLoopScope()
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode("""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            class Client { public Task<string> GetAsync(string url) => Task.FromResult(""); }
            class C { async Task Run(IEnumerable<string> urls) {
                var client = new Client();
                // codemetrics-ignore: CMAI8006 -- Shared state requires sequential execution
                foreach (var url in urls) { await client.GetAsync(url); }
            } }
            """);
        PerformanceAsyncProbe.Analyze([("Test", compilation)]).Findings
            .Should().NotContain(finding => finding.Category == "awaitedIoInsideLoop");
        var original = compilation.SyntaxTrees.Single().ToString();
        var (_, _, withoutAnnotation) = RoslynTestHelper.CompileCode(original.Replace("CMAI8006", "CMAI5001"));
        PerformanceAsyncProbe.Analyze([("Test", withoutAnnotation)]).Findings
            .Should().Contain(finding => finding.Category == "awaitedIoInsideLoop");
    }
}
