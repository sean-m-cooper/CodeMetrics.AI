using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Probes;

public class TestingProbeTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    /// <summary>
    /// Build a (name, Compilation) tuple from raw C# source. The name drives
    /// whether the probe considers it a test project (contains "Test").
    /// </summary>
    private static (string Name, Compilation Compilation) Project(string name, string code)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        // Re-create under the desired assembly name so diagnostics carry it
        var tree = CSharpSyntaxTree.ParseText(code);
        var refs = compilation.References;
        var comp = CSharpCompilation.Create(name,
            syntaxTrees: [tree],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return (name, comp);
    }

    // ── Test attribute preamble ───────────────────────────────────────────────
    // We define the attribute types inline in test code so Roslyn can parse
    // the attribute syntax even without real framework references.

    private const string AttributePreamble = @"
public class FactAttribute : System.Attribute {
    public string? Skip { get; set; }
}
public class TheoryAttribute : System.Attribute { }
public class TestAttribute : System.Attribute { }
public class TestCaseAttribute : System.Attribute { }
public class TestMethodAttribute : System.Attribute { }
public class DataTestMethodAttribute : System.Attribute { }
public class Assert {
    public static void True(bool v) { }
    public static void Equal(object a, object b) { }
    public static void NotNull(object o) { }
}
";

    // ── 1. No test projects → score 0 ─────────────────────────────────────────

    [Fact]
    public void NoTestProjects_ScoreIsZero()
    {
        var prodCode = @"public class MyService { public void Do() { } }";
        var prodProject = Project("MyApp.Core", prodCode);

        var allProjects = new List<(string, Compilation)> { prodProject };
        var analyzedNames = new List<string> { "MyApp.Core" };

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Score.Should().Be(0);
        result.Status.Should().Be("scored");
    }

    // ── 2. Good assertion density → score 10 ─────────────────────────────────

    [Fact]
    public void GoodAssertionDensity_ScoreIsTen()
    {
        var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne() { Assert.True(true); }
    [Fact] public void TestTwo() { Assert.Equal(1, 1); }
    [Fact] public void TestThree() { Assert.NotNull(new object()); }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var prodProject = Project("MyApp.Core", @"public class MyService { }");

        var allProjects = new List<(string, Compilation)> { prodProject, testProject };
        var analyzedNames = new List<string> { "MyApp.Core" };

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Score.Should().Be(10);
    }

    // ── 3. Placeholder test (empty body) → detected ───────────────────────────

    [Fact]
    public void PlaceholderTest_EmptyBody_IsDetected()
    {
        var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne() { Assert.True(true); }
    [Fact] public void TestPlaceholder() { }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var allProjects = new List<(string, Compilation)> { testProject };
        var analyzedNames = new List<string>();

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Findings.Should().Contain(f => f.Category == "placeholderTest");
    }

    [Fact]
    public void PlaceholderTest_NotImplementedException_IsDetected()
    {
        var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne() { Assert.True(true); }
    [Fact] public void TestNotDone() { throw new System.NotImplementedException(); }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var allProjects = new List<(string, Compilation)> { testProject };
        var analyzedNames = new List<string>();

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Findings.Should().Contain(f => f.Category == "placeholderTest");
    }

    // ── 4. Skipped test with Skip named argument → detected ───────────────────

    [Fact]
    public void SkippedTest_SkipNamedArg_IsDetected()
    {
        var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne()  { Assert.True(true); }
    [Fact] public void TestTwo()  { Assert.True(true); }
    [Fact] public void TestThree() { Assert.True(true); }
    [Fact(Skip = ""reason"")] public void SkippedTest() { Assert.True(true); }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var allProjects = new List<(string, Compilation)> { testProject };
        var analyzedNames = new List<string>();

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        // Should be 1 skipped test, verify extra data captures it
        var extra = result.Extra["testMetrics"];
        extra.Should().NotBeNull();

        // Score 8: no placeholders, only 1 skip (<= 2), covered, density >= 1
        result.Score.Should().Be(8);
    }

    // ── 5. Uncovered production project → finding present ────────────────────

    [Fact]
    public void UncoveredProductionProject_FindingPresent()
    {
        var testCode = AttributePreamble + @"
public class CoreTests {
    [Fact] public void TestOne() { Assert.True(true); }
}
";
        // Test project only covers MyApp.Core; MyApp.Api has no matching test project
        var testProject = Project("MyApp.Core.Tests", testCode);
        var prodCore    = Project("MyApp.Core", @"public class CoreService { }");
        var prodApi     = Project("MyApp.Api",  @"public class ApiController { }");

        var allProjects = new List<(string, Compilation)> { prodCore, prodApi, testProject };
        var analyzedNames = new List<string> { "MyApp.Core", "MyApp.Api" };

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Findings.Should().Contain(f =>
            f.Category == "uncoveredProject" && f.Project == "MyApp.Api");
    }

    [Fact]
    public void UncoveredProductionProject_ScoreReducedToSixOrLower()
    {
        var testCode = AttributePreamble + @"
public class CoreTests {
    [Fact] public void TestOne() { Assert.True(true); Assert.Equal(1, 1); }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var prodCore    = Project("MyApp.Core", @"public class CoreService { }");
        var prodApi     = Project("MyApp.Api",  @"public class ApiController { }");

        var allProjects = new List<(string, Compilation)> { prodCore, prodApi, testProject };
        var analyzedNames = new List<string> { "MyApp.Core", "MyApp.Api" };

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Score.Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public void MultiTargetFrameworkProductionNames_MatchingBaseTestProject_AreCovered()
    {
        var testCode = AttributePreamble + @"
public class CoreTests {
    [Fact] public void TestOne() { Assert.True(true); }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var prodProject = Project("MyApp.Core", @"public class CoreService { }");

        var allProjects = new List<(string, Compilation)> { prodProject, testProject };
        var analyzedNames = new List<string> { "MyApp.Core (net9.0)", "MyApp.Core (net10.0)" };

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Findings.Should().NotContain(f =>
            f.Category == "uncoveredProject" &&
            (f.Project == "MyApp.Core (net9.0)" || f.Project == "MyApp.Core (net10.0)"));
    }

    // ── 6. Zero assertions → assertion density 0 → score 2 ──────────────────

    [Fact]
    public void ZeroAssertions_AssertionDensityZero_ScoreIsTwo()
    {
        var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne() { int x = 1 + 1; }
    [Fact] public void TestTwo() { var s = ""hello""; }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var allProjects = new List<(string, Compilation)> { testProject };
        var analyzedNames = new List<string>();

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Score.Should().Be(2);
    }

    // ── 7. Extra data is populated ────────────────────────────────────────────

    [Fact]
    public void ExtraData_TestMetricsKey_IsPresent()
    {
        var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne() { Assert.True(true); }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var allProjects = new List<(string, Compilation)> { testProject };
        var analyzedNames = new List<string>();

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Extra.Should().ContainKey("testMetrics");
        result.Extra["testMetrics"].Should().NotBeNull();
    }

    // ── 8. Test project detected by attribute (not by name) ──────────────────

    [Fact]
    public void ProjectWithTestAttributes_NotTestByName_IsDetectedAsTestProject()
    {
        // Project name does NOT contain "Test" but has [Fact] methods
        var testCode = AttributePreamble + @"
public class Specs {
    [Fact] public void ItWorks() { Assert.True(true); }
}
";
        // Name deliberately avoids "Test"
        var specProject = Project("MyApp.Specs", testCode);
        var allProjects = new List<(string, Compilation)> { specProject };
        var analyzedNames = new List<string>();

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        // Has test methods → should not be score 0
        result.Score.Should().BeGreaterThan(0);
    }

    // ── 9. Coverage file found ────────────────────────────────────────────────

    [Fact]
    public void CoverageFile_WhenPresent_ReportedInExtra()
    {
        var tempDir = TempDir();
        Directory.CreateDirectory(tempDir);
        var scorecardDir = Path.Combine(tempDir, ".scorecard");
        Directory.CreateDirectory(scorecardDir);
        var coveragePath = Path.Combine(scorecardDir, "coverage.cobertura.xml");
        File.WriteAllText(coveragePath, "<coverage />");

        try
        {
            var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne() { Assert.True(true); }
}
";
            var testProject = Project("MyApp.Core.Tests", testCode);
            var allProjects = new List<(string, Compilation)> { testProject };
            var analyzedNames = new List<string>();

            var result = TestingProbe.Analyze(allProjects, analyzedNames, tempDir);

            result.Extra["testMetrics"].Should().NotBeNull();
            // CoverageFileFound is recorded inside the anonymous object;
            // verify via the basis string which includes it
            result.Basis.Should().Contain("coverageFile=True");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 9a. Coverage rate drives the score ───────────────────────────────────

    /// <summary>
    /// Runs the probe against a solution dir holding a Cobertura report with the given
    /// overall rates. The sources are otherwise clean — real assertions, no placeholders,
    /// no skips — so the resulting score is attributable to coverage alone.
    /// </summary>
    private static DimensionResult AnalyzeWithCoverage(
        string? lineRate,
        string branchRate = "0.5",
        IReadOnlyList<string>? analyzedNames = null)
    {
        var tempDir = TempDir();
        Directory.CreateDirectory(Path.Combine(tempDir, ".scorecard"));

        var rootAttributes = lineRate == null
            ? $@"branch-rate=""{branchRate}"""
            : $@"line-rate=""{lineRate}"" branch-rate=""{branchRate}""";

        File.WriteAllText(
            Path.Combine(tempDir, ".scorecard", "coverage.cobertura.xml"),
            $"<coverage {rootAttributes} />");

        try
        {
            var testCode = AttributePreamble + @"
public class CoreTests {
    [Fact] public void TestOne() { Assert.True(true); Assert.Equal(1, 1); }
    [Fact] public void TestTwo() { Assert.NotNull(new object()); Assert.True(true); }
}
";
            var testProject = Project("MyApp.Core.Tests", testCode);
            var prodCore = Project("MyApp.Core", @"public class CoreService { }");

            var allProjects = new List<(string, Compilation)> { prodCore, testProject };

            return TestingProbe.Analyze(
                allProjects,
                analyzedNames ?? new List<string> { "MyApp.Core" },
                tempDir);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("0.95", 10)]
    [InlineData("0.80", 10)]
    [InlineData("0.65", 8)]
    [InlineData("0.60", 8)]
    [InlineData("0.45", 6)]
    [InlineData("0.40", 6)]
    [InlineData("0.25", 4)]
    [InlineData("0.20", 4)]
    [InlineData("0.05", 2)]
    [InlineData("0.0", 2)]
    public void CoverageLineRate_CapsTheScore(string lineRate, double expected)
    {
        AnalyzeWithCoverage(lineRate).Score.Should().Be(expected);
    }

    [Fact]
    public void CoverageLineRate_IsReportedInBasis()
    {
        var result = AnalyzeWithCoverage("0.382", branchRate: "0.2356");

        result.Basis.Should().Contain("lineRate=38.2%");
        result.Basis.Should().Contain("branchRate=23.6%");
    }

    [Fact]
    public void NoCoverageReport_BasisReportsRateAsUnavailable()
    {
        var testCode = AttributePreamble + @"
public class CoreTests {
    [Fact] public void TestOne() { Assert.True(true); Assert.Equal(1, 1); }
}
";
        var allProjects = new List<(string, Compilation)> { Project("MyApp.Core.Tests", testCode) };

        var result = TestingProbe.Analyze(allProjects, new List<string>(), TempDir());

        result.Basis.Should().Contain("lineRate=n/a");
    }

    [Fact]
    public void MeasuredCoverage_SupersedesTheUncoveredProjectNameHeuristic()
    {
        // The consumer layout that exposed the bug: one shared test project covers several
        // production projects, so the name match fails while real coverage is high. The
        // measured rate must win, and the heuristic must not hold the score at 6.
        var result = AnalyzeWithCoverage(
            "0.85",
            analyzedNames: new List<string> { "MyApp.Core", "MyApp.Api", "MyApp.Infrastructure" });

        result.Score.Should().Be(10);
        result.Findings.Should().Contain(f => f.Category == "uncoveredProject");
        result.Findings.Where(f => f.Category == "uncoveredProject")
            .Should().AllSatisfy(f => f.Severity.Should().Be("info"));
    }

    [Fact]
    public void WithoutCoverage_UncoveredProjectHeuristicStillGatesTheScore()
    {
        // No report, so the name match remains the only signal available and keeps its bite.
        var testCode = AttributePreamble + @"
public class CoreTests {
    [Fact] public void TestOne() { Assert.True(true); Assert.Equal(1, 1); }
}
";
        var allProjects = new List<(string, Compilation)>
        {
            Project("MyApp.Core", @"public class CoreService { }"),
            Project("MyApp.Api", @"public class ApiController { }"),
            Project("MyApp.Core.Tests", testCode)
        };

        var result = TestingProbe.Analyze(
            allProjects, new List<string> { "MyApp.Core", "MyApp.Api" }, TempDir());

        result.Score.Should().Be(6);
        result.Findings.Where(f => f.Category == "uncoveredProject")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    [Fact]
    public void MalformedCoverageReport_FallsBackToUnknownRatherThanScoringZero()
    {
        // A report with no usable line-rate must not be read as 0% coverage.
        var result = AnalyzeWithCoverage(lineRate: null);

        result.Basis.Should().Contain("coverageFile=True");
        result.Basis.Should().Contain("lineRate=n/a");
        result.Score.Should().Be(10);
    }

    [Fact]
    public void CoverageCeiling_DoesNotRaiseAScoreEarnedDownByOtherSignals()
    {
        // 100% line coverage cannot paper over placeholder tests: the ceiling only caps.
        var tempDir = TempDir();
        Directory.CreateDirectory(Path.Combine(tempDir, ".scorecard"));
        File.WriteAllText(
            Path.Combine(tempDir, ".scorecard", "coverage.cobertura.xml"),
            @"<coverage line-rate=""1.0"" branch-rate=""1.0"" />");

        try
        {
            var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne() { Assert.True(true); }
    [Fact] public void TestTwo() { }
}
";
            var allProjects = new List<(string, Compilation)> { Project("MyApp.Core.Tests", testCode) };

            var result = TestingProbe.Analyze(allProjects, new List<string>(), tempDir);

            result.Score.Should().Be(4);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 10. Status is "scored" ────────────────────────────────────────────────

    [Fact]
    public void Result_StatusIsScored()
    {
        var allProjects = new List<(string, Compilation)>();
        var result = TestingProbe.Analyze(allProjects, new List<string>(), TempDir());

        result.Status.Should().Be("scored");
    }

    // ── 11. More than 2 skipped → score 4 ────────────────────────────────────

    [Fact]
    public void MoreThanTwoSkippedTests_ScoreIsFour()
    {
        var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne()   { Assert.True(true); }
    [Fact] public void TestTwo()   { Assert.True(true); }
    [Fact] public void TestThree() { Assert.True(true); }
    [Fact(Skip = ""a"")] public void Skip1() { Assert.True(true); }
    [Fact(Skip = ""b"")] public void Skip2() { Assert.True(true); }
    [Fact(Skip = ""c"")] public void Skip3() { Assert.True(true); }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var allProjects = new List<(string, Compilation)> { testProject };
        var analyzedNames = new List<string>();

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Score.Should().Be(4);
    }

    // ── 12. Placeholders >= testMethods → score 2 ────────────────────────────

    [Fact]
    public void AllTestsArePlaceholders_ScoreIsTwo()
    {
        var testCode = AttributePreamble + @"
public class MyTests {
    [Fact] public void TestOne() { }
    [Fact] public void TestTwo() { }
}
";
        var testProject = Project("MyApp.Core.Tests", testCode);
        var allProjects = new List<(string, Compilation)> { testProject };
        var analyzedNames = new List<string>();

        var result = TestingProbe.Analyze(allProjects, analyzedNames, TempDir());

        result.Score.Should().Be(2);
    }
}
