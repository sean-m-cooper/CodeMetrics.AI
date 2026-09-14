using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Tests.Probes;

/// <summary>
/// Labeled counterexamples from the Polly factory review. These preserve observations,
/// not a claim that every coupling hotspot is a design defect. No factory exemption is calibrated.
/// </summary>
public sealed class FactoryCouplingReviewTests
{
    private static readonly string Dependencies = string.Join("\n", Enumerable.Range(0, 12)
        .Select(index => $"public class Dependency{index} {{ public int Work() => 1; }}"));

    private static (TypeMetrics Type, Dictionary<string, int> Methods, DimensionResult Architecture) Measure(string body)
    {
        var root = Path.GetTempPath();
        var (_, model, compilation) = RoslynTestHelper.CompileCodeAtPath(
            Dependencies + "\npublic class ExampleFactory {\n" + body + "\n}", Path.Combine(root, "FactoryReview.cs"));
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        var (types, _) = MetricsCollector.Collect("Review", compilation, root);
        var methods = model.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => model.GetDeclaredSymbol(method)!.ContainingType.Name == "ExampleFactory")
            .ToDictionary(method => method.Identifier.ValueText,
                method => ClassCouplingCalculator.CalculateStructuralAction(method, model));
        var architecture = ArchitectureProbe.Analyze([("Review", compilation)], types, root, []);
        return (types.Single(type => type.Type == "ExampleFactory"), methods, architecture);
    }

    [Fact]
    public void IndependentConstruction_HasBroadTypeCouplingButNarrowMethods()
    {
        // Label: independent construction surface; breadth alone does not demonstrate orchestration.
        var result = Measure(string.Join("\n", Enumerable.Range(0, 12).Select(index =>
            $"public static Dependency{index} Create{index}() => new Dependency{index}();")));

        result.Type.StructuralClassCoupling.Should().Be(12);
        result.Methods.Values.Should().HaveCount(12).And.OnlyContain(count => count == 1);
        result.Type.MaxMemberCyclomaticComplexity.Should().Be(1);
        AssertStillReported(result.Architecture);
    }

    [Fact]
    public void Orchestration_HasTheSameTypeBreadthConcentratedInOneMethod()
    {
        // Label: one operation coordinates all dependencies, even though the class is named Factory.
        var result = Measure("public static int Execute() => " + string.Join(" + ",
            Enumerable.Range(0, 12).Select(index => $"new Dependency{index}().Work()")) + ";");

        result.Type.StructuralClassCoupling.Should().Be(12);
        result.Methods["Execute"].Should().Be(12);
        AssertStillReported(result.Architecture);
    }

    [Fact]
    public void FactoryWithDecisionLogic_DoesNotBecomeExemptBecauseItReturnsAProduct()
    {
        // Label: construction mixed with a decision based on multiple collaborating services.
        var result = Measure("public static Dependency0 Create() { if (" + string.Join(" + ",
            Enumerable.Range(1, 11).Select(index => $"new Dependency{index}().Work()")) +
            " > 5) return new Dependency0(); throw new System.InvalidOperationException(); }");

        result.Type.StructuralClassCoupling.Should().Be(12);
        result.Methods["Create"].Should().Be(12);
        result.Type.MaxMemberCyclomaticComplexity.Should().Be(2);
        AssertStillReported(result.Architecture);
    }

    [Fact]
    public void DelegatingToLocalHelpers_CanHideOrchestrationFromDirectMethodCoupling()
    {
        // Label: same operation as the direct orchestration case, merely extracted into helpers.
        // Its low per-method maximum must not be used as proof of independent factory behavior.
        var helpers = string.Join("\n", Enumerable.Range(0, 12).Select(index =>
            $"private static int Step{index}() => new Dependency{index}().Work();"));
        var result = Measure(helpers + "\npublic static int Execute() => " + string.Join(" + ",
            Enumerable.Range(0, 12).Select(index => $"Step{index}()")) + ";");

        result.Type.StructuralClassCoupling.Should().Be(12);
        result.Methods["Execute"].Should().Be(0);
        result.Methods.Values.Max().Should().Be(1);
        AssertStillReported(result.Architecture);
    }

    private static void AssertStillReported(DimensionResult result) =>
        result.Findings.Should().Contain(finding => finding.Category == "highCoupling" && finding.Type == "ExampleFactory");
}
