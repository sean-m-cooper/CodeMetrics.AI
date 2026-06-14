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
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                    continue;

                var root = tree.GetRoot();
                var filePath = tree.FilePath;

                AnalyzeTestMethods(root, filePath, projectName, findings,
                    ref testMethodCount, ref skippedTests, ref placeholderTests, ref assertionCount);
            }
        }

        double assertionDensity = testMethodCount > 0
            ? (double)assertionCount / testMethodCount
            : 0.0;

        // Determine uncovered production projects
        var testProjectNames = testProjects.Select(p => p.Name).ToList();
        var uncoveredProjects = FindUncoveredProductionProjects(analyzedProjectNames, testProjectNames);

        foreach (var uncovered in uncoveredProjects)
        {
            findings.Add(new Finding
            {
                Category = "uncoveredProject",
                Severity = "warning",
                Project = uncovered,
                Message = $"Production project '{uncovered}' has no matching test project."
            });
        }

        // Check for coverage file
        bool coverageFileFound = false;
        if (!string.IsNullOrEmpty(solutionDir))
        {
            var coveragePath = Path.Combine(solutionDir, ".scorecard", "coverage.cobertura.xml");
            coverageFileFound = File.Exists(coveragePath);
        }

        // Scoring (first match wins)
        double score;
        if (testProjects.Count == 0 || testMethodCount == 0)
            score = 0;
        else if (assertionDensity == 0.0 || placeholderTests >= testMethodCount)
            score = 2;
        else if (placeholderTests > 0 || skippedTests > 2)
            score = 4;
        else if (uncoveredProjects.Count > 0 || assertionDensity < 1.0)
            score = 6;
        else if (skippedTests > 0)
            score = 8;
        else
            score = 10;

        var basis = $"testProjects={testProjects.Count}, testMethods={testMethodCount}, " +
                    $"skipped={skippedTests}, placeholders={placeholderTests}, " +
                    $"assertions={assertionCount}, assertionDensity={assertionDensity:F2}, " +
                    $"uncoveredProjects={uncoveredProjects.Count}, coverageFile={coverageFileFound}.";

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
                    coverageFileFound
                }
            }
        };
    }

    // ── Test project detection ────────────────────────────────────────────────

    private static bool IsTestProject(string projectName, Compilation compilation, string solutionDir)
    {
        // Name-based detection
        if (projectName.Contains("Test", StringComparison.OrdinalIgnoreCase))
            return true;

        // Source-based detection: any method with a test attribute
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                continue;

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
