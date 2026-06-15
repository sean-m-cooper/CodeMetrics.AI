using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal sealed record TestProjectSet(
    List<(string Name, Compilation Compilation)> TestProjects,
    List<(string Name, Compilation Compilation)> ProductionProjects);

internal sealed record TestMetrics(
    int TestMethodCount,
    int SkippedTests,
    int PlaceholderTests,
    int AssertionCount);

internal static class TestProjectClassifier
{
    public static TestProjectSet Classify(
        IReadOnlyList<(string Name, Compilation Compilation)> projects,
        string solutionDir)
    {
        var testProjects = new List<(string Name, Compilation Compilation)>();
        var productionProjects = new List<(string Name, Compilation Compilation)>();

        foreach (var project in projects)
        {
            if (IsTestProject(project.Name, project.Compilation, solutionDir))
                testProjects.Add(project);
            else
                productionProjects.Add(project);
        }

        return new TestProjectSet(testProjects, productionProjects);
    }

    private static bool IsTestProject(string projectName, Compilation compilation, string solutionDir)
    {
        return projectName.Contains("Test", StringComparison.OrdinalIgnoreCase)
            || compilation.SyntaxTrees
                .Where(tree => SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                .Select(tree => tree.GetRoot())
                .Any(ContainsTestMethod);
    }

    private static bool ContainsTestMethod(SyntaxNode root)
    {
        return root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Any(TestAttributeInspector.HasTestAttribute);
    }
}

internal static class TestMetricsCollector
{
    public static TestMetrics Collect(
        IReadOnlyList<(string Name, Compilation Compilation)> testProjects,
        string solutionDir,
        List<Finding> findings)
    {
        var metrics = new MutableTestMetrics();

        foreach (var (projectName, compilation) in testProjects)
            CollectProject(projectName, compilation, solutionDir, findings, metrics);

        return metrics.ToSnapshot();
    }

    private static void CollectProject(
        string projectName,
        Compilation compilation,
        string solutionDir,
        List<Finding> findings,
        MutableTestMetrics metrics)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                continue;

            CollectFile(tree.GetRoot(), tree.FilePath, projectName, findings, metrics);
        }
    }

    private static void CollectFile(
        SyntaxNode root,
        string filePath,
        string projectName,
        List<Finding> findings,
        MutableTestMetrics metrics)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!TestAttributeInspector.HasTestAttribute(method))
                continue;

            metrics.TestMethodCount++;
            metrics.SkippedTests += TestAttributeInspector.HasSkipOrIgnoreArgument(method) ? 1 : 0;
            metrics.AssertionCount += TestMethodInspector.CountAssertions(method);

            if (TestMethodInspector.IsPlaceholderTest(method))
                AddPlaceholderFinding(method, filePath, projectName, findings, metrics);
        }
    }

    private static void AddPlaceholderFinding(
        MethodDeclarationSyntax method,
        string filePath,
        string projectName,
        List<Finding> findings,
        MutableTestMetrics metrics)
    {
        metrics.PlaceholderTests++;
        findings.Add(new Finding
        {
            Category = "placeholderTest",
            Severity = "warning",
            File = filePath,
            Line = SyntaxLocation.GetLine(method),
            Project = projectName,
            Type = SyntaxLocation.GetContainingTypeName(method),
            Message = $"Test method '{method.Identifier.Text}' appears to be a placeholder."
        });
    }

    private sealed class MutableTestMetrics
    {
        public int TestMethodCount { get; set; }
        public int SkippedTests { get; set; }
        public int PlaceholderTests { get; set; }
        public int AssertionCount { get; set; }

        public TestMetrics ToSnapshot()
        {
            return new TestMetrics(
                TestMethodCount,
                SkippedTests,
                PlaceholderTests,
                AssertionCount);
        }
    }
}

internal static class TestCoverageInspector
{
    public static List<string> FindUncoveredProductionProjects(
        IReadOnlyList<string> analyzedProjectNames,
        IReadOnlyList<string> testProjectNames)
    {
        return analyzedProjectNames
            .Where(productionProject => !IsCovered(productionProject, testProjectNames))
            .ToList();
    }

    public static bool CoverageFileExists(string solutionDir)
    {
        return !string.IsNullOrEmpty(solutionDir)
            && File.Exists(Path.Combine(solutionDir, ".scorecard", "coverage.cobertura.xml"));
    }

    private static bool IsCovered(string productionProjectName, IReadOnlyList<string> testProjectNames)
    {
        return testProjectNames.Any(testProject =>
            DoesTestProjectCover(testProject, productionProjectName));
    }

    private static bool DoesTestProjectCover(string testProjectName, string productionProjectName)
    {
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
        var suffixes = new[] { ".Tests", ".Test", "Tests", "Test" };
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name[..^suffix.Length];
        }

        return name;
    }
}

internal static class TestAttributeInspector
{
    private static readonly HashSet<string> TestAttributeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fact", "Theory", "Test", "TestCase", "TestMethod", "DataTestMethod"
    };

    public static bool HasTestAttribute(MethodDeclarationSyntax method)
    {
        return method.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Select(GetAttributeSimpleName)
            .Any(TestAttributeNames.Contains);
    }

    public static bool HasSkipOrIgnoreArgument(MethodDeclarationSyntax method)
    {
        return method.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Where(IsTestAttribute)
            .Any(HasSkipOrIgnoreArgument);
    }

    private static bool IsTestAttribute(AttributeSyntax attribute)
    {
        return TestAttributeNames.Contains(GetAttributeSimpleName(attribute));
    }

    private static bool HasSkipOrIgnoreArgument(AttributeSyntax attribute)
    {
        return attribute.ArgumentList?.Arguments
            .Select(GetArgumentName)
            .Any(IsSkipOrIgnore) == true;
    }

    private static string? GetArgumentName(AttributeArgumentSyntax argument)
    {
        return argument.NameEquals?.Name.Identifier.Text
            ?? argument.NameColon?.Name.Identifier.Text;
    }

    private static bool IsSkipOrIgnore(string? argumentName)
    {
        return argumentName?.Equals("Skip", StringComparison.OrdinalIgnoreCase) == true
            || argumentName?.Equals("Ignore", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string GetAttributeSimpleName(AttributeSyntax attr)
    {
        var name = attr.Name.ToString();
        if (name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
            name = name[..^"Attribute".Length];

        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }
}

internal static class TestMethodInspector
{
    public static bool IsPlaceholderTest(MethodDeclarationSyntax method)
    {
        return HasPlaceholderName(method)
            || HasEmptyBody(method)
            || ThrowsNotImplemented(method);
    }

    public static int CountAssertions(MethodDeclarationSyntax method)
    {
        return method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Count(IsAssertionCall);
    }

    private static bool HasPlaceholderName(MethodDeclarationSyntax method)
    {
        var methodName = method.Identifier.Text;
        return methodName.Contains("todo", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasEmptyBody(MethodDeclarationSyntax method)
    {
        return method.Body?.Statements.Count == 0;
    }

    private static bool ThrowsNotImplemented(MethodDeclarationSyntax method)
    {
        return method.ExpressionBody?.Expression is ThrowExpressionSyntax throwExpr
            ? IsNotImplementedException(throwExpr.Expression)
            : method.Body?.Statements is [ThrowStatementSyntax throwStmt]
                && IsNotImplementedException(throwStmt.Expression);
    }

    private static bool IsNotImplementedException(ExpressionSyntax? expression)
    {
        if (expression is not ObjectCreationExpressionSyntax objCreation)
            return false;

        var typeName = objCreation.Type.ToString();
        return typeName == "NotImplementedException" ||
               typeName == "System.NotImplementedException";
    }

    private static bool IsAssertionCall(InvocationExpressionSyntax invocation)
    {
        var text = invocation.Expression.ToString();
        return text.IndexOf("Assert", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("Should", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("Verify", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("Expect", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

internal static class SyntaxLocation
{
    public static string? GetContainingTypeName(SyntaxNode node)
    {
        return node.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.Text;
    }

    public static int GetLine(SyntaxNode node)
    {
        return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }
}
