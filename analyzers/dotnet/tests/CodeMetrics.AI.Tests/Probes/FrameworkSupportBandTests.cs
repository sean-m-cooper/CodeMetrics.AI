using CodeMetrics.AI.Probes;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public class FrameworkSupportBandTests
{
    [Theory]
    [InlineData("net45", "netstandard1.1", true)]
    [InlineData("net45", "netstandard1.2", false)]
    [InlineData("net451", "netstandard1.2", true)]
    [InlineData("net451", "netstandard1.3", false)]
    [InlineData("net46", "netstandard1.3", true)]
    [InlineData("net46", "netstandard1.4", false)]
    [InlineData("net461", "netstandard2.0", true)]
    [InlineData("net461", "netstandard2.1", false)]
    [InlineData("netcoreapp1.1", "netstandard1.0", false)]
    [InlineData("netcoreapp2.0", "netstandard2.0", true)]
    [InlineData("netcoreapp2.0", "netstandard2.1", false)]
    [InlineData("netcoreapp3.0", "netstandard2.1", true)]
    [InlineData("net5.0", "netstandard2.1", true)]
    [InlineData("net5.0", "netstandard2.2", false)]
    [InlineData("net10.0", "netcoreapp3.1", true)]
    [InlineData("net10.0", "netcoreapp3.2", false)]
    [InlineData("netstandard2.1", "netstandard2.0", true)]
    [InlineData("netstandard2.0", "netstandard2.1", false)]
    [InlineData("net10.0-windows10.0", "net10.0-windows7.0", true)]
    [InlineData("net10.0-windows7.0", "net10.0-windows10.0", false)]
    [InlineData("net10.0-windows", "net10.0-windows7.0", false)]
    [InlineData("net10.0", "net10.0-windows", false)]
    [InlineData("net10.0-windows", "net10.0", true)]
    public void Compatibility_RetainsSupportBoundaries(string project, string asset, bool expected)
    {
        PackageFrameworkCompatibility.IsCompatible(project, [asset]).Should().Be(expected);
    }

    [Fact]
    public void Compatibility_UnknownAssetsRetainThreeValuedResults()
    {
        PackageFrameworkCompatibility.IsCompatible("net10.0", []).Should().BeTrue();
        PackageFrameworkCompatibility.IsCompatible("unknown", []).Should().BeNull();
        PackageFrameworkCompatibility.IsCompatible("net10.0", ["unknown"]).Should().BeNull();
        PackageFrameworkCompatibility.IsCompatible("net10.0", ["net11.0", "unknown"]).Should().BeNull();
        PackageFrameworkCompatibility.IsCompatible("net10.0", ["unknown", "net10.0", "net10.0"]).Should().BeTrue();
        PackageFrameworkCompatibility.IsCompatible("net10.0", ["net11.0", "net11.0"]).Should().BeFalse();
    }
}
