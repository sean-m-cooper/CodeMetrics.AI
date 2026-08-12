using System.Globalization;
using System.Xml.Linq;
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
        string solutionDir)
    {
        var findings = new List<Finding>();

        // Identify test projects
        var testProjects = new List<(string Name, Compilation Compilation)>();
        var nonTestProjects = new List<(string Name, Compilation Compilation)>();

        foreach (var project in allProjects)
        {
            if (IsTestProject(project.Name, project.Compilation, solutionDir))
                testProjects.Add(project);
            else
                nonTestProjects.Add(project);
        }

        // Collect metrics across test projects
        int testMethodCount = 0;
        int skippedTests = 0;
        int placeholderTests = 0;
        int assertionCount = 0;

        foreach (var (projectName, compilation) in testProjects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                var root = tree.GetRoot();
                var filePath = tree.FilePath;

                AnalyzeTestMethods(root, filePath, projectName, findings,
                    ref testMethodCount, ref skippedTests, ref placeholderTests, ref assertionCount);
            }
        }

        double assertionDensity = testMethodCount > 0
            ? (double)assertionCount / testMethodCount
            : 0.0;

        // Coverage report. The file's existence only says a tool ran, so it is recorded
        // but never scored; the line rate inside it is the actual quality signal.
        var coveragePath = string.IsNullOrEmpty(solutionDir)
            ? null
            : Path.Combine(solutionDir, ".scorecard", "coverage.cobertura.xml");
        bool coverageFileFound = coveragePath != null && File.Exists(coveragePath);
        var coverage = coverageFileFound ? ReadCoverageRates(coveragePath!) : null;

        // Determine uncovered production projects
        var testProjectNames = testProjects.Select(p => p.Name).ToList();
        var uncoveredProjects = FindUncoveredProductionProjects(analyzedProjectNames, testProjectNames);

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
                Severity = coverage != null ? "info" : "warning",
                Project = uncovered,
                Message = coverage != null
                    ? $"Production project '{uncovered}' has no matching test project by name. " +
                      "Measured coverage is available and takes precedence over this heuristic."
                    : $"Production project '{uncovered}' has no matching test project."
            });
        }

        // Scoring (first match wins)
        double score;
        if (testProjects.Count == 0 || testMethodCount == 0)
            score = 0;
        else if (assertionDensity == 0.0 || placeholderTests >= testMethodCount)
            score = 2;
        else if (placeholderTests > 0 || skippedTests > 2)
            score = 4;
        else if ((coverage == null && uncoveredProjects.Count > 0) || assertionDensity < 1.0)
            score = 6;
        else if (skippedTests > 0)
            score = 8;
        else
            score = 10;

        // A measured line rate caps the dimension. Applied as a ceiling rather than as
        // extra rungs so the signals above still pull the score down on their own, and so
        // solutions with no coverage report keep the previous behaviour exactly.
        if (coverage != null)
            score = Math.Min(score, CoverageCeiling(coverage.LineRate));

        var coverageBasis = coverage != null
            ? $"lineRate={coverage.LineRate * 100:F1}%, branchRate={coverage.BranchRate * 100:F1}%"
            : "lineRate=n/a";

        var basis = $"testProjects={testProjects.Count}, testMethods={testMethodCount}, " +
                    $"skipped={skippedTests}, placeholders={placeholderTests}, " +
                    $"assertions={assertionCount}, assertionDensity={assertionDensity:F2}, " +
                    $"uncoveredProjects={uncoveredProjects.Count}, coverageFile={coverageFileFound}, " +
                    $"{coverageBasis}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = score,
            Basis = basis,
            Findings = findings,
            Extra =
            {
                ["testMetrics"] = new
                {
                    testProjects = testProjects.Count,
                    productionProjects = analyzedProjectNames.Count,
                    testMethods = testMethodCount,
                    skippedTests,
                    placeholderTests,
                    assertions = assertionCount,
                    assertionDensity,
                    uncoveredProjects,
                    coverageFileFound,
                    lineRate = coverage?.LineRate,
                    branchRate = coverage?.BranchRate
                }
            }
        };
    }

    // ── Coverage report ───────────────────────────────────────────────────────

    private sealed record CoverageRates(double LineRate, double BranchRate);

    /// <summary>
    /// Reads the overall <c>line-rate</c> and <c>branch-rate</c> from a Cobertura report's
    /// root element. Returns null when the report is unreadable, malformed, or carries no
    /// usable rate, so the caller falls back to treating coverage as unknown rather than
    /// inventing a number.
    /// </summary>
    private static CoverageRates? ReadCoverageRates(string coveragePath)
    {
        try
        {
            var root = XDocument.Load(coveragePath).Root;
            if (root == null)
                return null;

            var lineRate = ParseRate(root.Attribute("line-rate")?.Value);
            if (lineRate == null)
                return null;

            return new CoverageRates(
                lineRate.Value,
                ParseRate(root.Attribute("branch-rate")?.Value) ?? 0.0);
        }
        catch (Exception ex) when (ex is IOException
                                      or UnauthorizedAccessException
                                      or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static double? ParseRate(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
               && rate is >= 0.0 and <= 1.0
            ? rate
            : null;
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
        var methodName = method.Identifier.Text;

        // Name contains todo or placeholder
        if (methodName.Contains("todo", StringComparison.OrdinalIgnoreCase) ||
            methodName.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
            return true;

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
