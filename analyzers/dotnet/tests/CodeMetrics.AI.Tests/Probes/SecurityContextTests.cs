using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public class SecurityContextTests
{
    private static DimensionResult Analyze(string code)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(code, Path.Combine(Path.GetTempPath(), "CodeMetricsContext", "Context.cs"));
        compilation.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Should().BeEmpty();
        return SecurityProbe.Analyze([("Context", compilation)]);
    }

    [Theory]
    [InlineData("ForgotPasswordConfirmationPath", "ForgotPasswordConfirmation")]
    [InlineData("ResetPasswordConfirmationPath", "ResetPasswordConfirmation")]
    [InlineData("DatabaseConnectionString", "DatabaseConnectionString")]
    [InlineData("TokenProtector", "OrchardCore.UserStore.Token")]
    [InlineData("AuthenticatorKeyTokenName", "AuthenticatorKey")]
    [InlineData("ResetPassword", "OrchardCore.Users.ResetPassword")]
    [InlineData("PasswordAuthentication", "password-authentication")]
    [InlineData("PasswordRecovery", "password-recovery")]
    [InlineData("_changePasswordConfirmationUrl", "ChangePasswordConfirmation")]
    public void DescriptiveIdentifier_IsNotACredential(string name, string value)
    {
        Analyze($$"""class C { string {{name}} = "{{value}}"; void M() { {{name}} = "{{value}}"; } }""")
            .Findings.Should().NotContain(f => f.Category == "hardcodedSecret");
    }

    [Theory]
    [InlineData("PasswordPath", "r4ndomCredentialValue0123")]
    [InlineData("TokenName", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9")]
    [InlineData("DatabaseConnectionString", "Server=db;Password=realcredential")]
    [InlineData("TokenProtector", "abcdefghijklmnop")]
    public void IdentifierSuffix_DoesNotHideCredentials(string name, string value)
    {
        Analyze($$"""class C { string {{name}} = "{{value}}"; }""")
            .Findings.Should().ContainSingle(f => f.Category == "hardcodedSecret");
    }

    private const string CorsTypes = """
        class Builder {
            public Builder AllowAnyOrigin() => this;
            public Builder AllowCredentials() => this;
        }
        class Options { public void AddPolicy(string name, System.Action<Builder> configure) {} }
        class Policy { public bool AllowAnyOrigin; public bool AllowCredentials; }
        """;

    [Theory]
    [InlineData("b.AllowAnyOrigin().AllowCredentials();", true)]
    [InlineData("b.AllowAnyOrigin(); b.AllowCredentials();", true)]
    [InlineData("b.AllowAnyOrigin(); c.AllowCredentials();", false)]
    [InlineData("if (flag) b.AllowAnyOrigin(); else b.AllowCredentials();", false)]
    public void Cors_TracksBuilderAndBranches(string body, bool found)
    {
        var result = Analyze(CorsTypes + "class C { void M(Builder b, Builder c, bool flag) { " + body + " } }");
        result.Findings.Any(f => f.Category == "allowAnyOriginWithCredentials").Should().Be(found);
    }

    [Theory]
    [InlineData("if (p.AllowCredentials && p.AllowAnyOrigin) continue;", false)]
    [InlineData("if (p.AllowCredentials && p.AllowAnyOrigin) { System.Console.WriteLine(1); continue; }", false)]
    [InlineData("if (p.AllowCredentials && p.AllowAnyOrigin) { System.Console.WriteLine(1); }", true)]
    [InlineData("if (p.AllowCredentials && p.AllowAnyOrigin) continue; p.AllowAnyOrigin = true;", true)]
    [InlineData("if (p.AllowCredentials && other.AllowAnyOrigin) continue;", true)]
    [InlineData("", true)]
    public void Cors_OnlyAnApplicableRejectingGuardRemovesFinding(string guard, bool found)
    {
        var result = Analyze(CorsTypes + $$"""
            class C { void M(Options options, Policy[] policies, Policy other) {
                foreach (var p in policies) {
                    {{guard}}
                    options.AddPolicy("one", b => {
                        if (p.AllowAnyOrigin) b.AllowAnyOrigin();
                        if (p.AllowCredentials) b.AllowCredentials();
                    });
                }
            } }
            """);
        result.Findings.Any(f => f.Category == "allowAnyOriginWithCredentials").Should().Be(found);
    }

    [Fact]
    public void SeparatePolicies_DoNotCombineConfiguration()
    {
        Analyze(CorsTypes + """
            class C { void M(Options o) {
                o.AddPolicy("public", b => b.AllowAnyOrigin());
                o.AddPolicy("private", b => b.AllowCredentials());
            } }
            """).Findings.Should().NotContain(f => f.Category == "allowAnyOriginWithCredentials");
    }
}
