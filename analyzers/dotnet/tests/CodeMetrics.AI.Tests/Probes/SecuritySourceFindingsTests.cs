using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class SecuritySourceFindingsTests
{
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "SharedSecurity.cs");

    [Theory]
    [InlineData("class C { string Secret = \"abcdefghijklmnop\"; }", "hardcodedSecret", 2)]
    [InlineData("namespace Microsoft.AspNetCore.Authorization { public class AllowAnonymousAttribute : System.Attribute {} } [Microsoft.AspNetCore.Authorization.AllowAnonymous] class C {}", "allowAnonymous", 10)]
    [InlineData("class C { void M() { ExecuteSql($\"select {1}\"); } }", "rawSqlInterpolation", 2)]
    [InlineData("class C { object M() => new BinaryFormatter(); }", "unsafeDeserialization", 4)]
    public void RepeatedSourceAcrossProjectsAndFrameworks_CountsOnce(string code, string category, int score)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(code, FilePath);
        var result = SecurityProbe.Analyze([("App (net8.0)", compilation), ("App (net10.0)", compilation), ("StrongName", compilation)]);
        result.Score.Should().Be(score);
        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Category.Should().Be(category);
        finding.Observations["observationCount"].Should().Be(3);
        finding.Observations["affectedProjects"].Should().BeEquivalentTo(new[] { "App (net8.0)", "App (net10.0)", "StrongName" });
    }

    [Fact]
    public void DistinctSitesOnSameLine_AndSeparateFiles_StillCount()
    {
        const string code = "class C { string SecretA = \"abcdefghijklmnop\"; string SecretB = \"abcdefghijklmnop\"; }";
        var (_, _, first) = RoslynTestHelper.CompileCodeAtPath(code, FilePath);
        var (_, _, second) = RoslynTestHelper.CompileCodeAtPath(code, Path.Combine(Path.GetTempPath(), "OtherSecurity.cs"));
        var result = SecurityProbe.Analyze([("A", first), ("B", second), ("C", first)]);
        result.Findings.Should().HaveCount(4);
        result.Score.Should().Be(0);
    }

    [Fact]
    public void MissingPhysicalIdentity_IsNotMerged()
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode("class C { string Secret = \"abcdefghijklmnop\"; }");
        SecurityProbe.Analyze([("A", compilation), ("B", compilation)]).Findings.Should().HaveCount(2);
    }
}
