using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.RegularExpressions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class SecretRegexTests
{
    private const string Pattern = "(?<![\\p{L}\\p{N}_])\\{=([\\p{L}\\p{N}_]+)\\}";

    [Theory]
    [InlineData("new Regex(LiteralTokensPattern)")]
    [InlineData("new Regex(pattern: LiteralTokensPattern)")]
    [InlineData("Regex.IsMatch(input: text, pattern: LiteralTokensPattern)")]
    [InlineData("new Regex(C.LiteralTokensPattern)")]
    public void PrivateConstant_OnlyConsumedAsRegexPattern_IsNotCredential(string use)
    {
        var result = Analyze($$"""
            using System.Text.RegularExpressions;
            class C {
                private const string LiteralTokensPattern = @"{{Pattern}}";
                object Parse(string text) => {{use}};
            }
            """);
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void GeneratedRegexAttribute_InOtherPartialFile_IsRecognized()
    {
        var (_, _, compilation) = Compile($$"""
            partial class C { private const string LiteralTokensPattern = @"{{Pattern}}"; }
            """);
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            using System.Text.RegularExpressions;
            partial class C {
                [GeneratedRegex(pattern: LiteralTokensPattern)]
                private static partial Regex Parse();
            }
            """, path: Path.Combine(Path.GetTempPath(), "RegexConsumer.cs"), cancellationToken: TestContext.Current.CancellationToken));
        SecurityProbe.Analyze([("App", compilation)]).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("private", "new Regex(text).IsMatch(LiteralTokensPattern)")]
    [InlineData("private", "Regex.Replace(text, text, LiteralTokensPattern)")]
    [InlineData("private", "LiteralTokensPattern")]
    [InlineData("private", "MissingApi(LiteralTokensPattern)")]
    [InlineData("public", "new Regex(LiteralTokensPattern)")]
    public void NonPatternUse_OrPublicConstant_RemainsFinding(string visibility, string use)
    {
        Analyze($$"""
            using System.Text.RegularExpressions;
            public class C {
                {{visibility}} const string LiteralTokensPattern = @"{{Pattern}}";
                object Parse(string text) => {{use}};
            }
            """).Findings.Should().ContainSingle(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void MixedPatternAndCredentialUse_IsNotExempted()
    {
        Analyze($$"""
            using System.Text.RegularExpressions;
            class C {
                private const string SecretPattern = @"{{Pattern}}";
                Regex Parse() => new Regex(SecretPattern);
                string Credential() => SecretPattern;
            }
            """).Findings.Should().ContainSingle(f => f.Category == "hardcodedSecret");
    }

    [Fact]
    public void LookalikeRegexType_AndUnusedConstant_AreNotExempted()
    {
        Analyze($$"""
            class Regex { public Regex(string pattern) {} }
            class C {
                private const string SecretPattern = @"{{Pattern}}";
                private const string UnusedSecretPattern = @"{{Pattern}}";
                Regex Parse() => new Regex(SecretPattern);
            }
            """).Findings.Should().HaveCount(2);
    }

    private static (SyntaxTree, SemanticModel, Compilation) Compile(string code) =>
        RoslynTestHelper.CompileCodeAtPath(code, Path.Combine(Path.GetTempPath(), "RegexSource.cs"),
            MetadataReference.CreateFromFile(typeof(Regex).Assembly.Location));

    private static DimensionResult Analyze(string code) => SecurityProbe.Analyze([("App", Compile(code).Item3)]);
}
