using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Metrics;

public class HalsteadTests
{
    private static double ComputeVolume(string methodBody)
    {
        var code = $"public class C {{ public void M() {{ {methodBody} }} }}";
        var tree = RoslynTestHelper.ParseCode(code);
        return HalsteadCalculator.ComputeVolume(tree.GetRoot());
    }

    [Fact]
    public void EmptyMethod_ReturnsNonNegative()
    {
        var volume = ComputeVolume("");
        volume.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void SimpleAssignment_ReturnsPositiveVolume()
    {
        var volume = ComputeVolume("int x = 1;");
        volume.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComplexCode_ReturnsHigherVolumeThanSimpleCode()
    {
        var simpleVolume = ComputeVolume("int x = 1;");
        var complexVolume = ComputeVolume(
            "int x = 1; int y = 2; int z = x + y; if (z > 0) { z = z * 2; } else { z = -z; }");
        complexVolume.Should().BeGreaterThan(simpleVolume);
    }
}
