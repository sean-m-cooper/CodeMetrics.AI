using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class AnonymousIntentTests
{
    [Theory]
    [InlineData("Microsoft.AspNetCore.Authorization")]
    [InlineData("System.Web.Mvc")]
    [InlineData("System.Web.Http")]
    public void AnonymousAlias_IsInformationalAndDoesNotLowerScore(string ns)
    {
        var result = Analyze($$"""
            using Anonymous = {{ns}}.AllowAnonymousAttribute;
            namespace {{ns}} { public class AllowAnonymousAttribute : System.Attribute {} }
            [Anonymous] class A {} [Anonymous] class B {}
            class C { [Anonymous] public void Endpoint() {} }
            """);
        result.Score.Should().Be(10);
        result.Findings.Should().HaveCount(3).And.OnlyContain(f => f.Severity == "info" && f.Category == "allowAnonymous");
        result.Findings.Should().AllSatisfy(f => f.Observations["scoreDisposition"].Should().Be("excludedExplicitIntent"));
    }

    [Fact]
    public void MetadataContract_CustomAttribute_ExpressesIntent()
    {
        var result = Analyze("""
            namespace Microsoft.AspNetCore.Authorization { public interface IAllowAnonymous {} }
            class AnonymousAttribute : System.Attribute, Microsoft.AspNetCore.Authorization.IAllowAnonymous {}
            class AuthorizeAttribute : System.Attribute {}
            [Authorize] class SecureController {}
            class PublicController { [Anonymous] public void Endpoint() {} }
            """);
        result.Score.Should().Be(10);
        result.Findings.Should().ContainSingle(f => f.Category == "allowAnonymous");
    }

    [Fact]
    public void UnrelatedAnonymousAttributes_DoNotSuppressMissingIntent()
    {
        var result = Analyze("""
            class AnonymousAttribute : System.Attribute {}
            class AllowAnonymousAttribute : System.Attribute {}
            class AuthorizeAttribute : System.Attribute {}
            [Authorize] class SecureController {}
            class PublicController { [Anonymous, AllowAnonymous] public void Endpoint() {} }
            """);
        result.Findings.Should().NotContain(f => f.Category == "allowAnonymous");
        result.Findings.Should().ContainSingle(f => f.Category == "missingAuthorization");
        result.Score.Should().Be(8);
    }

    [Fact]
    public void ExplicitIntentDoesNotExemptSecrets()
    {
        const string code = """
            namespace Microsoft.AspNetCore.Authorization { public class AllowAnonymousAttribute : System.Attribute {} }
            [Microsoft.AspNetCore.Authorization.AllowAnonymous] class C { string Secret = "abcdefghijklmnop"; }
            """;
        var result = Analyze(code);
        result.Score.Should().Be(2);
        result.Findings.Should().Contain(f => f.Category == "hardcodedSecret" && f.Severity == "error");
    }

    private static DimensionResult Analyze(string code)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(code, Path.Combine(Path.GetTempPath(), "Anonymous.cs"));
        return SecurityProbe.Analyze([("App", compilation)]);
    }
}
