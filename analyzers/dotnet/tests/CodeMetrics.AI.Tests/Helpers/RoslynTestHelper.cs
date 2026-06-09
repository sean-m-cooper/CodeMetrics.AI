using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Helpers;

public static class RoslynTestHelper
{
    public static SyntaxTree ParseCode(string code)
    {
        return CSharpSyntaxTree.ParseText(code);
    }

    public static (SyntaxTree Tree, SemanticModel Model, Compilation Compilation) CompileCode(
        string code, params MetadataReference[] additionalRefs)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        refs.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
        refs.AddRange(additionalRefs);

        var compilation = CSharpCompilation.Create("TestAssembly",
            syntaxTrees: [tree],
            references: refs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (tree, compilation.GetSemanticModel(tree), compilation);
    }

    public static T FindFirstNode<T>(SyntaxTree tree) where T : SyntaxNode
    {
        return tree.GetRoot().DescendantNodes().OfType<T>().First();
    }

    public static IEnumerable<T> FindAllNodes<T>(SyntaxTree tree) where T : SyntaxNode
    {
        return tree.GetRoot().DescendantNodes().OfType<T>();
    }
}
