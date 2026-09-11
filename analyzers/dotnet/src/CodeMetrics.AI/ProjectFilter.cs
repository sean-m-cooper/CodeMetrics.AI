using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI;

public sealed record SkippedProject(string Name, string Reason);

public static class ProjectFilter
{
    private static readonly (Func<string, bool> Match, string Reason)[] Rules =
    [
        (n => n.Contains("Tests", StringComparison.OrdinalIgnoreCase), "Test project"),
        (n => n.EndsWith(".Specs", StringComparison.OrdinalIgnoreCase), "Test project"),
        (n => n.EndsWith(".AppHost", StringComparison.OrdinalIgnoreCase), "Aspire orchestration host"),
        (n => n.EndsWith(".ServiceDefaults", StringComparison.OrdinalIgnoreCase), "Aspire service defaults"),
        (n => n.EndsWith(".Hosting", StringComparison.OrdinalIgnoreCase), "Aspire / generic hosting"),
        (n => n.EndsWith(".Benchmarks", StringComparison.OrdinalIgnoreCase), "Benchmark project"),
        (n => n.EndsWith(".Samples", StringComparison.OrdinalIgnoreCase), "Sample / demo code"),
        (n => n.EndsWith(".Demo", StringComparison.OrdinalIgnoreCase), "Demo code"),
        (n => n.EndsWith(".Playground", StringComparison.OrdinalIgnoreCase), "Playground / spike project"),
        (n => n.Equals("Snippets", StringComparison.OrdinalIgnoreCase), "Sample / demo code"),
    ];

    public static bool ShouldSkip(string projectName, out string reason)
    {
        projectName = NormalizeName(projectName);
        foreach (var (match, r) in Rules)
        {
            if (match(projectName))
            {
                reason = r;
                return true;
            }
        }
        reason = "";
        return false;
    }

    public static string NormalizeName(string name) => Regex.Replace(name, @"\s*\((?:net|\.NET)[^()]*\)$", "", RegexOptions.IgnoreCase);

    public static bool ShouldSkip(string projectName, string? projectPath, string root, out string reason)
    {
        if (ShouldSkip(projectName, out reason)) return true;
        if (projectPath == null) return false;
        if (File.Exists(projectPath))
        {
            var document = XDocument.Load(projectPath);
            if (document.Descendants().Any(e => e.Attribute("Condition") == null && e.Parent?.Attribute("Condition") == null &&
                    ((e.Name.LocalName == "IsTestProject" && e.Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)) ||
                     (e.Name.LocalName == "ProjectType" && e.Value.Trim().Equals("Test", StringComparison.OrdinalIgnoreCase)))))
            { reason = "Test project"; return true; }
        }
        var segments = Path.GetRelativePath(root, projectPath).Replace('\\', '/').Split('/');
        if (segments.Contains("..")) return false;
        if (segments.Any(s => s.Equals("test", StringComparison.OrdinalIgnoreCase) || s.Equals("tests", StringComparison.OrdinalIgnoreCase)))
        { reason = "Test support / fixture"; return true; }
        if (segments.Any(s => s.Equals("bench", StringComparison.OrdinalIgnoreCase) || s.Equals("benchmarks", StringComparison.OrdinalIgnoreCase)))
        { reason = "Benchmark project"; return true; }
        if (segments.Any(s => s.Equals("samples", StringComparison.OrdinalIgnoreCase) || s.Equals("snippets", StringComparison.OrdinalIgnoreCase)))
        { reason = "Sample / demo code"; return true; }
        return false;
    }

    internal static bool HasTestMethods(Compilation compilation, string root) =>
        SourceFileFilter.AnalyzableTrees(compilation, root).Any(tree =>
            tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Any(method =>
                compilation.GetSemanticModel(tree).GetDeclaredSymbol(method)?.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() is "Xunit.FactAttribute" or "Xunit.TheoryAttribute" or
                        "NUnit.Framework.TestAttribute" or "NUnit.Framework.TestCaseAttribute" or
                        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute" or
                        "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute") == true));
}
