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
