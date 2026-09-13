using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class ErrorHandlingStandardErrorTests
{
    private static DimensionResult Analyze(string statements, string declarations = "")
    {
        var code = $$"""
            using System;
            using System.IO;
            using Terminal = System.Console;
            using static System.Console;
            {{declarations}}
            class Application {
                int Run() {
                    try { throw new Exception(); }
                    catch (Exception ex) { {{statements}} return 0; }
                }
                void Cleanup() {
                    try { throw new IOException(); }
                    catch (IOException ex) { {{statements}} }
                    catch (UnauthorizedAccessException ex) { {{statements}} }
                }
            }
            """;
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
        return ErrorHandlingProbe.Analyze([("ConsoleApplication", compilation)]);
    }

    [Theory]
    [InlineData("Console.Error.WriteLine(ex.Message);")]
    [InlineData("global::System.Console.Error.WriteLine($\"Analysis failed: {ex.Message}\");")]
    [InlineData("Terminal.Error.Write(\"Cleanup failed: {0}\", ex.Message);")]
    [InlineData("Error.WriteLine(ex);")]
    [InlineData("((TextWriter)Console.Error).WriteLine(ex.Message);")]
    public void StandardErrorReporting_HandlesBroadAndMultipleNarrowCatches(string statements)
    {
        var result = Analyze(statements);

        result.Findings.Should().BeEmpty();
        result.Score.Should().Be(10);
        result.ScoringDecision!.Inputs["stderrReportingRecognition"]
            .Should().Be("system-console-error-v1");
    }

    [Theory]
    [InlineData("Action report = () => Console.Error.WriteLine(ex.Message);")]
    [InlineData("void Report() { Console.Error.WriteLine(ex.Message); }")]
    [InlineData("Console.Error.WriteLine();")]
    [InlineData("Console.Error.Flush();")]
    [InlineData("Console.Out.WriteLine(ex.Message);")]
    [InlineData("new StringWriter().WriteLine(ex.Message);")]
    public void DeferredOrUnrelatedWrites_DoNotEstablishStandardErrorReporting(string statements)
    {
        AssertUnrecognized(Analyze(statements));
    }

    [Fact]
    public void LookalikeConsoleError_DoesNotHideUnhandledCatches()
    {
        AssertUnrecognized(Analyze("Console.Error.WriteLine(ex.Message);", """
            static class Console {
                public static TextWriter Error => TextWriter.Null;
            }
            """));
    }

    private static void AssertUnrecognized(DimensionResult result)
    {
        result.Findings.Should().Contain(finding => finding.Category == "broadCatchWithoutLoggingOrRethrow");
        result.Findings.Should().Contain(finding => finding.Category == "broadCatchReturnsDefault");
        result.Findings.Should().Contain(finding => finding.Category == "missingLoggerForMultipleCatches");
    }
}
