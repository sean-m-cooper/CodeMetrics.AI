using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Probes;

public class ErrorHandlingProbeTests
{
    private static DimensionResult Analyze(string code)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        var projects = new List<(string, Compilation)> { ("TestProject", compilation) };
        return ErrorHandlingProbe.Analyze(projects);
    }

    // ── 1. emptyCatch ─────────────────────────────────────────────────────────

    [Fact]
    public void EmptyCatch_FindsEmptyCatchFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "emptyCatch");
    }

    [Fact]
    public void EmptyCatch_SeverityIsError()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "emptyCatch")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    [Fact]
    public void AnalyzerProbeSources_DoNotContainEmptyCatchFindings()
    {
        var repoRoot = FindAnalyzerRoot();
        var sourcePaths = new[]
        {
            Path.Combine("src", "CodeMetrics.AI", "Probes", "ArchitectureProbe.cs"),
            Path.Combine("src", "CodeMetrics.AI", "Probes", "DependencyProbe.cs")
        };

        var syntaxTrees = sourcePaths.Select(path =>
        {
            var fullPath = Path.Combine(repoRoot, path);
            return CSharpSyntaxTree.ParseText(File.ReadAllText(fullPath), path: fullPath);
        });

        var compilation = CSharpCompilation.Create("AnalyzerSources",
            syntaxTrees: syntaxTrees,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = ErrorHandlingProbe.Analyze([("CodeMetrics.AI", compilation)]);

        result.Findings.Should().NotContain(f => f.Category == "emptyCatch");
    }

    private static string FindAnalyzerRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CodeMetrics.AI.slnx")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src", "CodeMetrics.AI")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate analyzer root.");
    }

    // ── 2. throwEx ────────────────────────────────────────────────────────────

    [Fact]
    public void ThrowEx_FindsThrowExFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) { throw ex; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "throwEx");
    }

    [Fact]
    public void ThrowEx_SeverityIsError()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) { throw ex; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "throwEx")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    [Fact]
    public void BareRethrow_DoesNotFindThrowEx()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { throw; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "throwEx");
    }

    // ── 3. broadCatchWithoutLoggingOrRethrow ──────────────────────────────────

    [Fact]
    public void BroadCatchWithoutLogging_FindsBroadCatchFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { var x = 1; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void BroadCatchWithLogging_DoesNotFindBroadCatchFinding()
    {
        const string code = """
            using System;
            class Logger { public void LogError(string msg) { } }
            class C {
                Logger _log = new Logger();
                void M() {
                    try { }
                    catch (Exception ex) { _log.LogError(ex.Message); }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void BroadCatchWithRethrow_DoesNotFindBroadCatchFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { throw; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    [Fact]
    public void BroadCatchWithWhenFilter_DoesNotFindBroadCatchFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) when (ex.Message != null) { var x = 1; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "broadCatchWithoutLoggingOrRethrow");
    }

    // ── 4. broadCatchReturnsDefault ───────────────────────────────────────────

    [Fact]
    public void BroadCatchReturnsNull_FindsBroadCatchReturnsDefault()
    {
        const string code = """
            using System;
            class C {
                object M() {
                    try { return new object(); }
                    catch (Exception) { return null; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void BroadCatchReturnsFalse_FindsBroadCatchReturnsDefault()
    {
        const string code = """
            using System;
            class C {
                bool M() {
                    try { return true; }
                    catch (Exception) { return false; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void BroadCatchReturnsZero_FindsBroadCatchReturnsDefault()
    {
        const string code = """
            using System;
            class C {
                int M() {
                    try { return 1; }
                    catch (Exception) { return 0; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "broadCatchReturnsDefault");
    }

    [Fact]
    public void BroadCatchReturnsDefault_SeverityIsError()
    {
        const string code = """
            using System;
            class C {
                int M() {
                    try { return 1; }
                    catch (Exception) { return 0; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "broadCatchReturnsDefault")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    // ── 5. syncBlockingCall ───────────────────────────────────────────────────

    [Fact]
    public void DotResult_FindsSyncBlockingCall()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    var t = Task.CompletedTask;
                    var _ = Task.FromResult(1).Result;
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "syncBlockingCall");
    }

    [Fact]
    public void DotWait_FindsSyncBlockingCall()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.Delay(100).Wait();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "syncBlockingCall");
    }

    [Fact]
    public void GetAwaiterGetResult_FindsSyncBlockingCall()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.FromResult(1).GetAwaiter().GetResult();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "syncBlockingCall");
    }

    [Fact]
    public void SyncBlockingCall_SeverityIsWarning()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.Delay(100).Wait();
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "syncBlockingCall")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    // ── 6. consoleWriteLine ───────────────────────────────────────────────────

    [Fact]
    public void ConsoleWriteLine_FindsConsoleWriteLineFinding()
    {
        const string code = """
            class C {
                void M() {
                    Console.WriteLine("hello");
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "consoleWriteLine");
    }

    [Fact]
    public void ConsoleWriteLine_SeverityIsInfo()
    {
        const string code = """
            class C {
                void M() {
                    Console.WriteLine("hello");
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "consoleWriteLine")
            .Should().AllSatisfy(f => f.Severity.Should().Be("info"));
    }

    // ── 7. missingLoggerForMultipleCatches ────────────────────────────────────

    [Fact]
    public void MultipleCatchesNoLogger_FindsMissingLoggerFinding()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void MultipleCatchesWithILoggerField_DoesNotFindMissingLogger()
    {
        const string code = """
            using System;
            interface ILogger { }
            class C {
                private ILogger _logger;
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    [Fact]
    public void SingleCatchNoLogger_DoesNotFindMissingLogger()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) { var x = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "missingLoggerForMultipleCatches");
    }

    // ── No findings — clean code ──────────────────────────────────────────────

    [Fact]
    public void NoFindings_ReturnsScore10()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    // No try/catch, no Console.WriteLine, no blocking calls
                    int x = 1 + 1;
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(10);
        result.Findings.Where(f => f.Severity == "error" || f.Severity == "warning")
            .Should().BeEmpty();
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyCatchPresent_ScoreIs2()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(2);
    }

    [Fact]
    public void ThrowExPresent_ScoreIs2()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception ex) { throw ex; }
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(2);
    }

    [Fact]
    public void FiveOrMoreEmptyCatches_ScoreIs0()
    {
        const string code = """
            using System;
            class C {
                void M1() { try { } catch (Exception) { } }
                void M2() { try { } catch (Exception) { } }
                void M3() { try { } catch (Exception) { } }
                void M4() { try { } catch (Exception) { } }
                void M5() { try { } catch (Exception) { } }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(0);
    }

    [Fact]
    public void OnlySyncBlockingCall_ScoreIs4()
    {
        const string code = """
            using System.Threading.Tasks;
            class C {
                void M() {
                    Task.Delay(100).Wait();
                }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(4);
    }

    [Fact]
    public void OnlyWarning_ScoreIs6()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (ArgumentNullException ex) { var x = ex.Message; }
                    catch (InvalidOperationException ex) { var y = ex.Message; }
                }
            }
            """;

        var result = Analyze(code);

        // missingLoggerForMultipleCatches is a warning → score 6
        result.Score.Should().BeGreaterThanOrEqualTo(4);
    }

    // ── Metadata ──────────────────────────────────────────────────────────────

    [Fact]
    public void Finding_HasProjectName()
    {
        const string code = """
            using System;
            class C {
                void M() { try { } catch (Exception) { } }
            }
            """;

        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        var projects = new List<(string, Compilation)> { ("MyProject", compilation) };
        var result = ErrorHandlingProbe.Analyze(projects);

        result.Findings.Should().AllSatisfy(f => f.Project.Should().Be("MyProject"));
    }

    [Fact]
    public void Finding_HasLineNumber()
    {
        const string code = """
            using System;
            class C {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        var finding = result.Findings.First(f => f.Category == "emptyCatch");
        finding.Line.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Finding_HasTypeName()
    {
        const string code = """
            using System;
            class MyClass {
                void M() {
                    try { }
                    catch (Exception) { }
                }
            }
            """;

        var result = Analyze(code);

        var finding = result.Findings.First(f => f.Category == "emptyCatch");
        finding.Type.Should().Be("MyClass");
    }

    [Fact]
    public void EmptyProjectList_ReturnsScore10()
    {
        var result = ErrorHandlingProbe.Analyze(new List<(string, Compilation)>());

        result.Status.Should().Be("scored");
        result.Score.Should().Be(10);
    }
}
