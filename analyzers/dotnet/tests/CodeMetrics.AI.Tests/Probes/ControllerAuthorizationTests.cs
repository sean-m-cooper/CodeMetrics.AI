using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Probes;

public class ControllerAuthorizationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialControllers_PreserveFirstDeclarationOrderAndEligibleActionCounts(bool reverseParts)
    {
        var firstPath = Path.Combine(Path.GetTempPath(), "First.cs");
        var secondPath = Path.Combine(Path.GetTempPath(), "Second.cs");
        var first = CSharpSyntaxTree.ParseText("""
            using Gate = AuthorizeAttribute;
            public partial class MixedController {
                [Gate] public void PrivateEndpoint() { }
                public void Open() { }
                [NonAction] public void Utility() { }
                public static void StaticUtility() { }
                private void PrivateUtility() { }
                public int Value => 0;
            }
            public class EmptyController { }
            public class ExplicitController {
                [Gate] public void PrivateEndpoint() { }
                [AllowAnonymous] public void PublicEndpoint() { }
            }
            public class InheritedController : SecureBase { public void Open() { } }
            """, path: firstPath, cancellationToken: TestContext.Current.CancellationToken);
        var second = CSharpSyntaxTree.ParseText("""
            public partial class MixedController { public void AnotherOpen() { } }
            public class OtherController { public void Open() { } }
            """, path: secondPath, cancellationToken: TestContext.Current.CancellationToken);
        var (_, _, compilation) = RoslynTestHelper.CompileCode("""
            public class AuthorizeAttribute : System.Attribute { }
            public class AllowAnonymousAttribute : System.Attribute { }
            public class NonActionAttribute : System.Attribute { }
            [Authorize] public class SecureBase { }
            """);
        var definitions = compilation.SyntaxTrees.Single();
        compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(
            reverseParts ? [second, first, definitions] : [first, second, definitions]);
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var result = SecurityProbe.Analyze([("Web", compilation)]);
        var findings = result.Findings.Where(f => f.Category == "missingAuthorization").ToArray();

        findings.Select(f => f.Type).Should().Equal(reverseParts
            ? ["MixedController", "OtherController", "EmptyController"]
            : ["MixedController", "EmptyController", "OtherController"]);
        findings[0].File.Should().Be(reverseParts ? secondPath : firstPath);
        findings[0].Line.Should().Be(reverseParts ? 1 : 2);
        findings[0].Message.Should().Be(
            "Controller 'MixedController' has no explicit class-level " +
            "[Authorize]/[AllowAnonymous] intent and 2 public action(s) " +
            "also lack an explicit authorization attribute. Global filters or a fallback policy " +
            "may still protect the endpoint; verify the effective policy.");
        findings.Single(f => f.Type == "EmptyController").Message.Should().Contain("0 public action(s)");
        findings.Should().AllSatisfy(f =>
        {
            f.Severity.Should().Be("warning");
            f.Confidence.Should().Be("medium");
            f.Project.Should().Be("Web");
        });
        result.Score.Should().Be(6);
    }

    [Theory]
    [InlineData("Authorization.cs", 1)]
    [InlineData("Authorization.g.cs", 0)]
    public void ProjectGate_UsesIncludedMembersEvenWhenTheyFollowControllerDeclarations(string gatePath, int expected)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath("""
            public class OpenController { public void Get() { } }
            public class AuthorizeAttribute : System.Attribute { }
            """, Path.Combine(Path.GetTempPath(), "Controller.cs"));
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            public class Support { [Authorize] public void Operation() { } }
            """, path: Path.Combine(Path.GetTempPath(), gatePath), cancellationToken: TestContext.Current.CancellationToken));
        compilation.GetDiagnostics(TestContext.Current.CancellationToken).Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var result = SecurityProbe.Analyze([("Web", compilation)]);

        result.Findings.Count(f => f.Category == "missingAuthorization").Should().Be(expected);
        result.Score.Should().Be(expected == 0 ? 10 : 8);
    }
}
