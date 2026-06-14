using CodeMetrics.AI.Metrics;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Metrics;

public class MaintainabilityIndexTests
{
    [Fact]
    public void ZeroSourceLines_Returns100()
    {
        var mi = MaintainabilityIndexCalculator.Calculate(cc: 1, sourceLines: 0, halsteadVolume: 100);
        mi.Should().Be(100);
    }

    [Fact]
    public void SimpleMethod_ReturnsHighMI()
    {
        // CC=1, LOC=5, HV=50 => high MI (>70)
        var mi = MaintainabilityIndexCalculator.Calculate(cc: 1, sourceLines: 5, halsteadVolume: 50);
        mi.Should().BeGreaterThan(70);
    }

    [Fact]
    public void ComplexMethod_ReturnsLowMI()
    {
        // CC=30, LOC=200, HV=5000 => low MI (<50)
        var mi = MaintainabilityIndexCalculator.Calculate(cc: 30, sourceLines: 200, halsteadVolume: 5000);
        mi.Should().BeLessThan(50);
    }

    [Fact]
    public void Result_IsAlwaysClampedBetween0And100()
    {
        // Extreme values that might produce out-of-range result
        var miHigh = MaintainabilityIndexCalculator.Calculate(cc: 0, sourceLines: 1, halsteadVolume: 1);
        var miLow = MaintainabilityIndexCalculator.Calculate(cc: 1000, sourceLines: 100000, halsteadVolume: 1000000);

        miHigh.Should().BeInRange(0, 100);
        miLow.Should().BeInRange(0, 100);
    }
}
