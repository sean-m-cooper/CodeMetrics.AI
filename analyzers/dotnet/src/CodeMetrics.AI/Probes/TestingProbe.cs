using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

public static class TestingProbe
{
    private static readonly HashSet<string> TestAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fact", "Theory", "Test", "TestCase", "TestMethod", "DataTestMethod"
    };

    public static DimensionResult Analyze(
        IReadOnlyList<(string Name, Compilation Compilation)> allProjects,
        IReadOnlyList<string> analyzedProjectNames,
        string solutionDir,
        string? coveragePath = null)
    {
        var findings = new List<Finding>();
        var testProjects = FindTestProjects(allProjects, solutionDir);
        var testMetrics = CollectTestMetrics(testProjects, solutionDir, findings);
        var selectedCoveragePath = coveragePath ?? Path.Combine(solutionDir, ".scorecard", "coverage.cobertura.xml");
        var coverageFileFound = File.Exists(selectedCoveragePath);
        var productionFiles = allProjects.Where(project => analyzedProjectNames.Contains(project.Name))
            .SelectMany(project => SourceFileFilter.AnalyzableTrees(project.Compilation, solutionDir))
            .Select(tree => tree.FilePath).Where(path => !string.IsNullOrEmpty(path)).ToArray();
        var report = coverageFileFound ? CoverageReport.Read(selectedCoveragePath, productionFiles, solutionDir) : null;
        var coverage = report?.LineRate is double rate ? new CoverageRates(rate, report.BranchRate) : null;
        var testProjectNames = testProjects.Select(p => p.Name).ToList();
        var uncoveredProjects = FindUncoveredProductionProjects(analyzedProjectNames, testProjectNames);
        AddUncoveredProjectFindings(findings, uncoveredProjects, coverage != null);
        var decision = CalculateDecision(testProjects.Count, testMetrics, uncoveredProjects, coverage);
        var result = CreateResult(
            decision,
            testProjects.Count,
            analyzedProjectNames.Count,
            testMetrics,
            uncoveredProjects,
            coverageFileFound,
            coverage,
            findings);
        result.Extra["coverageMode"] = coveragePath == null ? "auto" : "explicit";
        result.Extra["coverage"] = report ?? (object)new { status = "missing", path = selectedCoveragePath };
        if (coveragePath != null && coverage == null)
            return new DimensionResult { Status = "failed", Basis = "The requested coverage report is missing, invalid, or does not match production files.", Findings = findings, Extra = result.Extra };
        return result;
    }

    private static List<(string Name, Compilation Compilation)> FindTestProjects(
        IEnumerable<(string Name, Compilation Compilation)> projects,
        string solutionDir)
    {
        return projects
            .Where(project => IsTestProject(project.Name, project.Compilation, solutionDir))
            .ToList();
    }

    private static TestMetrics CollectTestMetrics(
        IEnumerable<(string Name, Compilation Compilation)> testProjects,
        string solutionDir,
        List<Finding> findings)
    {
        var testMethodCount = 0;
        var skippedTests = 0;
        var placeholderTests = 0;
        var assertionCount = 0;

        foreach (var (projectName, compilation) in testProjects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                AnalyzeTestMethods(
                    tree.GetRoot(),
                    tree.FilePath,
                    projectName,
                    findings,
                    ref testMethodCount,
                    ref skippedTests,
                    ref placeholderTests,
                    ref assertionCount);
            }
        }

        return new TestMetrics(testMethodCount, skippedTests, placeholderTests, assertionCount);
    }

    private static void AddUncoveredProjectFindings(
        ICollection<Finding> findings,
        IEnumerable<string> uncoveredProjects,
        bool hasMeasuredCoverage)
    {
        foreach (var uncovered in uncoveredProjects)
        {
            // This is a name-matching guess: it flags a production project with no
            // '*.Tests'-named counterpart, which is wrong for the common layout where one
            // shared test project covers several production projects. When a measured
            // coverage report is available it supersedes the guess, so the finding drops
            // to advisory and stops gating the score below.
            findings.Add(new Finding
            {
                Category = "uncoveredProject",
                Severity = hasMeasuredCoverage ? "info" : "warning",
                Project = uncovered,
                Message = hasMeasuredCoverage
                    ? $"Production project '{uncovered}' has no matching test project by name. " +
                      "Measured coverage is available and takes precedence over this heuristic."
                    : $"Production project '{uncovered}' has no matching test project."
            });
        }
    }

    private static ScoringDecision CalculateDecision(
        int testProjectCount, TestMetrics metrics, IReadOnlyCollection<string> uncoveredProjects, CoverageRates? coverage)
    {
        var signals = ScoringDecision.FirstMatch("dotnet/testing/signals/v1", new()
        {
            ["testProjectCount"] = testProjectCount,
            ["testMethods"] = metrics.TestMethods,
            ["assertionDensity"] = metrics.AssertionDensity,
            ["placeholderTests"] = metrics.PlaceholderTests,
            ["skippedTests"] = metrics.SkippedTests,
            ["uncoveredProjectCount"] = uncoveredProjects.Count,
            ["coverageAvailable"] = coverage != null
        },
        ScoringStep.Rule("noTests", "testProjectCount == 0 || testMethods == 0", testProjectCount == 0 || metrics.TestMethods == 0, 0, "uncoveredProject"),
        ScoringStep.Rule("noMeaningfulAssertions", "assertionDensity == 0 || placeholderTests >= testMethods", metrics.AssertionDensity == 0 || metrics.PlaceholderTests >= metrics.TestMethods, 2, "placeholderTest"),
        ScoringStep.Rule("placeholdersOrManySkipped", "placeholderTests > 0 || skippedTests > 2", metrics.PlaceholderTests > 0 || metrics.SkippedTests > 2, 4, "placeholderTest"),
        ScoringStep.Rule("missingProjectsOrLowDensity", "(!coverageAvailable && uncoveredProjectCount > 0) || assertionDensity < 1", (coverage == null && uncoveredProjects.Count > 0) || metrics.AssertionDensity < 1, 6, "uncoveredProject"),
        ScoringStep.Rule("skippedTests", "skippedTests > 0", metrics.SkippedTests > 0, 8),
        ScoringStep.Rule("testSignalsSatisfied", "otherwise", true, 10));
        if (coverage == null) return signals;
        return ScoringDecision.Minimum("dotnet/testing/v1", 1, MidpointRounding.ToEven,
            ScoringStep.Component("testSignals", signals.FinalScore, decision: signals),
            ScoringStep.Component("lineCoverage", CoverageCeiling(coverage.LineRate), inputs: new()
            {
                ["lineRate"] = coverage.LineRate,
                ["thresholdsAtLeast"] = new[] { .8, .6, .4, .2 },
                ["scores"] = new[] { 10, 8, 6, 4, 2 }
            }, kind: "cap"));
    }

    private static DimensionResult CreateResult(
        ScoringDecision decision,
        int testProjectCount,
        int productionProjectCount,
        TestMetrics metrics,
        IReadOnlyList<string> uncoveredProjects,
        bool coverageFileFound,
        CoverageRates? coverage,
        List<Finding> findings)
    {
        var coverageBasis = coverage != null
            ? $"lineRate={coverage.LineRate * 100:F1}%, branchRate={(coverage.BranchRate.HasValue ? (coverage.BranchRate.Value * 100).ToString("F1") + "%" : "n/a")}"
            : "lineRate=n/a";
        var basis = $"testProjects={testProjectCount}, testMethods={metrics.TestMethods}, " +
                    $"skipped={metrics.SkippedTests}, placeholders={metrics.PlaceholderTests}, " +
                    $"assertions={metrics.Assertions}, assertionDensity={metrics.AssertionDensity:F2}, " +
                    $"uncoveredProjects={uncoveredProjects.Count}, coverageFile={coverageFileFound}, " +
                    $"{coverageBasis}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = decision.FinalScore,
            ScoringDecision = decision,
            Basis = basis,
            Findings = findings,
            Extra =
            {
                ["testMetrics"] = new
                {
                    testProjects = testProjectCount,
                    productionProjects = productionProjectCount,
                    testMethods = metrics.TestMethods,
                    skippedTests = metrics.SkippedTests,
                    placeholderTests = metrics.PlaceholderTests,
                    assertions = metrics.Assertions,
                    assertionDensity = metrics.AssertionDensity,
                    uncoveredProjects,
                    coverageFileFound,
                    lineRate = coverage?.LineRate,
                    branchRate = coverage?.BranchRate
                }
            }
        };
    }

    // ── Coverage report ───────────────────────────────────────────────────────

    private sealed record CoverageRates(double LineRate, double? BranchRate);

    private sealed record TestMetrics(
        int TestMethods,
        int SkippedTests,
        int PlaceholderTests,
        int Assertions)
    {
        public double AssertionDensity => TestMethods > 0
            ? (double)Assertions / TestMethods
            : 0.0;
    }

    /// <summary>
    /// Highest score a measured line rate permits, on conventional gates: 80% is a commonly
    /// required threshold, 60% acceptable, 40% weak, 20% token, below that effectively
    /// untested regardless of how many test methods exist.
    /// </summary>
    private static double CoverageCeiling(double lineRate)
    {
        if (lineRate >= 0.80) return 10;
        if (lineRate >= 0.60) return 8;
        if (lineRate >= 0.40) return 6;
        if (lineRate >= 0.20) return 4;
        return 2;
    }

    // ── Test project detection ────────────────────────────────────────────────

    private static bool IsTestProject(string projectName, Compilation compilation, string solutionDir)
    {
        // Name-based detection
        if (projectName.Contains("Test", StringComparison.OrdinalIgnoreCase))
            return true;

        // Source-based detection: any method with a test attribute
        foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
        {
            var root = tree.GetRoot();
            var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                if (HasTestAttribute(method))
                    return true;
            }
        }

        return false;
    }

    // ── Per-file analysis ─────────────────────────────────────────────────────

    private static void AnalyzeTestMethods(
        SyntaxNode root,
        string filePath,
        string projectName,
        List<Finding> findings,
        ref int testMethodCount,
        ref int skippedTests,
        ref int placeholderTests,
        ref int assertionCount)
    {
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methods)
        {
            if (!HasTestAttribute(method))
                continue;

            testMethodCount++;

            // Skipped test: Skip or Ignore named argument in any test attribute
            if (HasSkipOrIgnoreArgument(method))
                skippedTests++;

            // Placeholder test: empty body, NotImplementedException throw, or name contains todo/placeholder
            if (IsPlaceholderTest(method))
            {
                placeholderTests++;
                findings.Add(new Finding
                {
                    Category = "placeholderTest",
                    Severity = "warning",
                    File = filePath,
                    Line = GetLine(method),
                    Project = projectName,
                    Type = GetContainingTypeName(method),
                    Message = $"Test method '{method.Identifier.Text}' appears to be a placeholder."
                });
            }

            // Count assertions within this method
            assertionCount += CountAssertions(method);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool HasTestAttribute(MethodDeclarationSyntax method)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = GetAttributeSimpleName(attr);
                if (TestAttributeNames.Contains(name))
                    return true;
            }
        }
        return false;
    }

    private static bool HasSkipOrIgnoreArgument(MethodDeclarationSyntax method)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = GetAttributeSimpleName(attr);
                if (!TestAttributeNames.Contains(name))
                    continue;

                if (attr.ArgumentList == null)
                    continue;

                foreach (var arg in attr.ArgumentList.Arguments)
                {
                    var argName = arg.NameEquals?.Name.Identifier.Text
                                  ?? arg.NameColon?.Name.Identifier.Text;

                    if (argName != null &&
                        (argName.Equals("Skip", StringComparison.OrdinalIgnoreCase) ||
                         argName.Equals("Ignore", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static bool IsPlaceholderTest(MethodDeclarationSyntax method)
    {
        var body = method.Body;
        if (body == null)
        {
            // Expression-bodied member: check for throw new NotImplementedException()
            if (method.ExpressionBody?.Expression is ThrowExpressionSyntax throwExpr)
                return IsNotImplementedException(throwExpr.Expression);
            return false;
        }

        // Empty body
        if (body.Statements.Count == 0)
            return true;

        // Body consists solely of a throw new NotImplementedException()
        if (body.Statements.Count == 1 &&
            body.Statements[0] is ThrowStatementSyntax throwStmt)
        {
            return IsNotImplementedException(throwStmt.Expression);
        }

        return false;
    }

    private static bool IsNotImplementedException(ExpressionSyntax? expression)
    {
        if (expression is ObjectCreationExpressionSyntax objCreation)
        {
            var typeName = objCreation.Type.ToString();
            return typeName == "NotImplementedException" ||
                   typeName == "System.NotImplementedException";
        }
        return false;
    }

    private static int CountAssertions(MethodDeclarationSyntax method)
    {
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
        int count = 0;
        foreach (var inv in invocations)
        {
            var text = inv.Expression.ToString();
            if (text.IndexOf("Assert", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Should", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Verify", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Expect", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                count++;
            }
        }
        return count;
    }

    private static List<string> FindUncoveredProductionProjects(
        IReadOnlyList<string> analyzedProjectNames,
        IReadOnlyList<string> testProjectNames)
    {
        var uncovered = new List<string>();

        foreach (var productionProject in analyzedProjectNames)
        {
            bool covered = testProjectNames.Any(testProject =>
                DoesTestProjectCover(testProject, productionProject));

            if (!covered)
                uncovered.Add(productionProject);
        }

        return uncovered;
    }

    private static bool DoesTestProjectCover(string testProjectName, string productionProjectName)
    {
        // "MyApp.Core.Tests" covers "MyApp.Core"
        // Strategy: strip common test suffixes/prefixes and compare
        productionProjectName = StripTargetFrameworkSuffix(productionProjectName);
        var strippedTest = StripTestSuffix(testProjectName);
        return strippedTest.Equals(productionProjectName, StringComparison.OrdinalIgnoreCase) ||
               testProjectName.StartsWith(productionProjectName, StringComparison.OrdinalIgnoreCase);
    }

    private static string StripTargetFrameworkSuffix(string name)
    {
        var index = name.LastIndexOf(" (", StringComparison.Ordinal);
        return index > 0 && name.EndsWith(")", StringComparison.Ordinal)
            ? name[..index]
            : name;
    }

    private static string StripTestSuffix(string name)
    {
        // Remove trailing ".Tests", ".Test", "Tests", "Test"
        var suffixes = new[] { ".Tests", ".Test", "Tests", "Test" };
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }
        return name;
    }

    private static string GetAttributeSimpleName(AttributeSyntax attr)
    {
        var name = attr.Name.ToString();
        // Strip "Attribute" suffix if present
        if (name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
            name = name[..^"Attribute".Length];
        // Take last segment after dot
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private static string? GetContainingTypeName(SyntaxNode node)
    {
        return node.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.Text;
    }

    private static int GetLine(SyntaxNode node)
    {
        return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }
}
