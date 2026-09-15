using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI;

internal static class BenchmarkProjectRecognition
{
    private static readonly string[] Directories = ["bench", "benchmark", "benchmarks"];

    public static bool IsName(string name) => name.Equals("Benchmark", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Benchmarks", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".Benchmark", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".Benchmarks", StringComparison.OrdinalIgnoreCase);

    public static bool IsDirectory(string segment) =>
        Directories.Contains(segment, StringComparer.OrdinalIgnoreCase);

    public static bool? Declaration(string? path)
    {
        if (path == null || !File.Exists(path)) return null;
        var values = XDocument.Load(path).Descendants().Where(e => e.Name.LocalName == "IsBenchmarkProject" &&
            !e.AncestorsAndSelf().Any(parent => parent.Attribute("Condition") != null))
            .Select(e => bool.TryParse(e.Value.Trim(), out var value) ? (bool?)value : null).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    public static bool HasBenchmarkMethods(Compilation compilation, string root) =>
        compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication &&
        SourceFileFilter.AnalyzableTrees(compilation, root).Any(tree =>
            tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Any(method =>
                compilation.GetSemanticModel(tree).GetDeclaredSymbol(method)?.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "BenchmarkDotNet.Attributes.BenchmarkAttribute") == true));
}
