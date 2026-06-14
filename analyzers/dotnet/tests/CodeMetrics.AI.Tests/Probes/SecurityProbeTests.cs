using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public class SecurityProbeTests
{
    private static DimensionResult Analyze(string code, int importedVulnerabilities = 0)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        var projects = new List<(string, Compilation)> { ("TestProject", compilation) };
        return SecurityProbe.Analyze(projects, importedVulnerabilities);
    }

    // ── 1. Hardcoded Secret — found ───────────────────────────────────────────

    [Fact]
    public void HardcodedSecret_VariableNameContainsApiKey_FindsHardcodedSecret()
    {
        const string code = """
            class C {
                string ApiKey = "abcdef1234567890";
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void HardcodedSecret_SeverityIsError()
    {
        const string code = """
            class C {
                string Password = "supersecretpassword123";
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "hardcodedSecret")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    [Fact]
    public void HardcodedSecret_VariableNameContainsToken_FindsHardcodedSecret()
    {
        const string code = """
            class C {
                string authToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void HardcodedSecret_VariableNameContainsSecret_FindsHardcodedSecret()
    {
        const string code = """
            class C {
                string clientSecret = "abcdefghijklmnop";
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void HardcodedSecret_VariableNameContainsConnectionString_FindsHardcodedSecret()
    {
        const string code = """
            class C {
                string connectionString = "Server=myserver;Database=mydb;User=sa;Password=pass1234";
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void HardcodedSecret_AssignmentExpression_FindsHardcodedSecret()
    {
        const string code = """
            class C {
                string _apiKey;
                void Init() {
                    _apiKey = "abcdef1234567890xyz";
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "hardcodedSecret");
    }

    // ── 2. Hardcoded Secret — negative cases ─────────────────────────────────

    [Fact]
    public void HardcodedSecret_ShortString_NotFound()
    {
        const string code = """
            class C {
                string ApiKey = "short";
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void HardcodedSecret_ContainsExample_NotFound()
    {
        const string code = """
            class C {
                string ApiKey = "example_api_key_placeholder";
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void HardcodedSecret_ContainsPlaceholder_NotFound()
    {
        const string code = """
            class C {
                string Password = "placeholder_password_here";
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void HardcodedSecret_ContainsLocalhost_NotFound()
    {
        const string code = """
            class C {
                string connectionString = "Server=localhost;Database=mydb;";
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void HardcodedSecret_UnrelatedVariableName_NotFound()
    {
        const string code = """
            class C {
                string userName = "abcdef1234567890xyz";
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "hardcodedSecret");
    }

    // ── 3. Raw SQL Interpolation — found ─────────────────────────────────────

    [Fact]
    public void RawSqlInterpolation_InterpolatedString_FindsRawSqlInterpolation()
    {
        const string code = """
            class Db {
                void ExecuteSql(string s) { }
            }
            class C {
                void M(string table) {
                    var db = new Db();
                    db.ExecuteSql($"SELECT * FROM {table}");
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "rawSqlInterpolation");
    }

    [Fact]
    public void RawSqlInterpolation_StringConcatenation_FindsRawSqlInterpolation()
    {
        const string code = """
            class Db {
                void ExecuteSql(string s) { }
            }
            class C {
                void M(string table) {
                    var db = new Db();
                    db.ExecuteSql("SELECT * FROM " + table);
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "rawSqlInterpolation");
    }

    [Fact]
    public void RawSqlInterpolation_SeverityIsError()
    {
        const string code = """
            class Db {
                void ExecuteSql(string s) { }
            }
            class C {
                void M(string table) {
                    var db = new Db();
                    db.ExecuteSql($"SELECT * FROM {table}");
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "rawSqlInterpolation")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    [Fact]
    public void RawSqlInterpolation_LiteralString_NotFound()
    {
        const string code = """
            class Db {
                void ExecuteSql(string s) { }
            }
            class C {
                void M() {
                    var db = new Db();
                    db.ExecuteSql("SELECT * FROM Users");
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "rawSqlInterpolation");
    }

    // ── 4. Unsafe Deserialization — found ─────────────────────────────────────

    [Fact]
    public void UnsafeDeserialization_BinaryFormatter_FindsUnsafeDeserialization()
    {
        const string code = """
            class BinaryFormatter { }
            class C {
                void M() {
                    var bf = new BinaryFormatter();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "unsafeDeserialization");
    }

    [Fact]
    public void UnsafeDeserialization_NetDataContractSerializer_FindsUnsafeDeserialization()
    {
        const string code = """
            class NetDataContractSerializer { }
            class C {
                void M() {
                    var s = new NetDataContractSerializer();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "unsafeDeserialization");
    }

    [Fact]
    public void UnsafeDeserialization_SeverityIsError()
    {
        const string code = """
            class BinaryFormatter { }
            class C {
                void M() {
                    var bf = new BinaryFormatter();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "unsafeDeserialization")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    [Fact]
    public void UnsafeDeserialization_SafeSerializer_NotFound()
    {
        const string code = """
            class JsonSerializer { }
            class C {
                void M() {
                    var s = new JsonSerializer();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "unsafeDeserialization");
    }

    // ── 5. AllowAnonymous — found ─────────────────────────────────────────────

    [Fact]
    public void AllowAnonymous_ClassWithAllowAnonymousAttribute_FindsAllowAnonymous()
    {
        const string code = """
            public class AllowAnonymousAttribute : System.Attribute { }
            public class AuthorizeAttribute : System.Attribute { }
            [AllowAnonymous]
            public class PublicController { }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "allowAnonymous");
    }

    [Fact]
    public void AllowAnonymous_SeverityIsWarning()
    {
        const string code = """
            public class AllowAnonymousAttribute : System.Attribute { }
            [AllowAnonymous]
            public class PublicController { }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "allowAnonymous")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    [Fact]
    public void AllowAnonymous_NoAttribute_NotFound()
    {
        const string code = """
            public class SomeController { }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "allowAnonymous");
    }

    // ── 6. Missing Authorization — found ─────────────────────────────────────

    [Fact]
    public void MissingAuthorization_ControllerWithoutAuthorize_InProjectThatUsesIt_Found()
    {
        const string code = """
            public class AllowAnonymousAttribute : System.Attribute { }
            public class AuthorizeAttribute : System.Attribute { }
            [Authorize] public class AuthController { }
            public class OpenController { }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "missingAuthorization");
    }

    [Fact]
    public void MissingAuthorization_SeverityIsWarning()
    {
        const string code = """
            public class AllowAnonymousAttribute : System.Attribute { }
            public class AuthorizeAttribute : System.Attribute { }
            [Authorize] public class AuthController { }
            public class OpenController { }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "missingAuthorization")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    [Fact]
    public void MissingAuthorization_ControllerWithAuthorize_NotFound()
    {
        const string code = """
            public class AuthorizeAttribute : System.Attribute { }
            [Authorize] public class SecureController { }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingAuthorization");
    }

    [Fact]
    public void MissingAuthorization_ControllerWithAllowAnonymous_NotFound()
    {
        const string code = """
            public class AllowAnonymousAttribute : System.Attribute { }
            public class AuthorizeAttribute : System.Attribute { }
            [Authorize] public class AuthController { }
            [AllowAnonymous] public class PublicController { }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f =>
            f.Category == "missingAuthorization" && f.Type == "PublicController");
    }

    [Fact]
    public void MissingAuthorization_PartialControllerWithAuthorizeOnAnotherDeclaration_NotFound()
    {
        const string code = """
            public class AllowAnonymousAttribute : System.Attribute { }
            public class AuthorizeAttribute : System.Attribute { }
            [Authorize] public class AuthController { }
            [Authorize] public partial class PublicController { }
            public partial class PublicController {
                public void Get() { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f =>
            f.Category == "missingAuthorization" && f.Type == "PublicController");
    }

    [Fact]
    public void MissingAuthorization_PartialControllerWithAllowAnonymousOnAnotherDeclaration_NotFound()
    {
        const string code = """
            public class AllowAnonymousAttribute : System.Attribute { }
            public class AuthorizeAttribute : System.Attribute { }
            [Authorize] public class AuthController { }
            [AllowAnonymous] public partial class PublicController { }
            public partial class PublicController {
                public void Get() { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f =>
            f.Category == "missingAuthorization" && f.Type == "PublicController");
    }

    [Fact]
    public void MissingAuthorization_ProjectDoesNotUseAuthorize_NotFound()
    {
        // Project uses no [Authorize] anywhere → rule does not apply
        const string code = """
            public class OpenController { }
            public class AnotherController { }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingAuthorization");
    }

    // ── 7. Clean code ─────────────────────────────────────────────────────────

    [Fact]
    public void CleanCode_NoSecurityIssues_ScoreIs10()
    {
        const string code = """
            class C {
                string Name { get; set; } = "Alice";
                void DoWork(int x) {
                    var result = x * 2;
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(10);
        result.Findings.Should().BeEmpty();
    }

    // ── 8. Scoring ────────────────────────────────────────────────────────────

    [Fact]
    public void Scoring_MoreThanTwoHardcodedSecrets_ScoreIs0()
    {
        const string code = """
            class C {
                string ApiKey1 = "abcdef1234567890";
                string ApiKey2 = "zyxwvutsrqponmlk";
                string ApiKey3 = "mnbvcxzasdfghjkl";
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(0);
    }

    [Fact]
    public void Scoring_SingleHardcodedSecret_ScoreIs2()
    {
        const string code = """
            class C {
                string ApiKey = "abcdef1234567890";
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(2);
    }

    [Fact]
    public void Scoring_RawSqlInterpolation_ScoreIs2()
    {
        const string code = """
            class Db {
                void ExecuteSql(string s) { }
            }
            class C {
                void M(string table) {
                    var db = new Db();
                    db.ExecuteSql($"SELECT * FROM {table}");
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(2);
    }

    [Fact]
    public void Scoring_AllowAnyOriginWithCredentials_ScoreIs0()
    {
        const string code = """
            class CorsOptions {
                public CorsOptions AllowAnyOrigin() { return this; }
                public CorsOptions AllowCredentials() { return this; }
            }
            class Startup {
                void Configure(CorsOptions options) {
                    options.AllowAnyOrigin().AllowCredentials();
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(0);
    }

    [Fact]
    public void Scoring_UnsafeDeserializationOnly_ScoreIs4()
    {
        const string code = """
            class BinaryFormatter { }
            class C {
                void M() {
                    var bf = new BinaryFormatter();
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(4);
    }

    [Fact]
    public void Scoring_MoreThanTwoWarnings_ScoreIs6()
    {
        // 3+ missingAuthorization findings → warnings > 2 → score 6
        const string code = """
            public class AuthorizeAttribute : System.Attribute { }
            public class AllowAnonymousAttribute : System.Attribute { }
            [Authorize] public class SecureController { }
            public class AlphaController { }
            public class BetaController { }
            public class GammaController { }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(6);
    }

    [Fact]
    public void Scoring_SingleWarning_ScoreIs8()
    {
        // A single [AllowAnonymous] → 1 warning → score 8
        const string code = """
            public class AllowAnonymousAttribute : System.Attribute { }
            [AllowAnonymous]
            public class PublicController { }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(8);
    }

    [Fact]
    public void Scoring_ImportedVulnerabilities_ScoreIs2()
    {
        // Clean code but importedVulnerabilities > 0 → score 2
        const string code = """
            class C {
                void M() { int x = 1; }
            }
            """;

        var result = Analyze(code, importedVulnerabilities: 3);

        result.Score.Should().Be(2);
    }

    // ── Metadata ──────────────────────────────────────────────────────────────

    [Fact]
    public void Finding_HasProjectName()
    {
        const string code = """
            class C {
                string ApiKey = "abcdef1234567890";
            }
            """;

        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        var projects = new List<(string, Compilation)> { ("MyProject", compilation) };
        var result = SecurityProbe.Analyze(projects);

        result.Findings.Should().AllSatisfy(f => f.Project.Should().Be("MyProject"));
    }

    [Fact]
    public void Finding_HasLineNumber()
    {
        const string code = """
            class C {
                string ApiKey = "abcdef1234567890";
            }
            """;

        var result = Analyze(code);

        var finding = result.Findings.First(f => f.Category == "hardcodedSecret");
        finding.Line.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void EmptyProjectList_ReturnsScore10()
    {
        var result = SecurityProbe.Analyze(new List<(string, Compilation)>());

        result.Status.Should().Be("scored");
        result.Score.Should().Be(10);
    }
}
