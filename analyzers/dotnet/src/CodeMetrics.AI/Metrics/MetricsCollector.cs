using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

public static class MetricsCollector
{
    public static (List<TypeMetrics> Types, List<MemberMetrics> Members) Collect(
        string projectName, Compilation compilation, string? solutionDir = null)
    {
        var types = new List<TypeMetrics>();
        var members = new List<MemberMetrics>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            if (solutionDir != null && !SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                continue;

            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (typeSymbol == null)
                    continue;

                var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
                var typeName = typeSymbol.Name;
                var memberMetrics = MemberMetricsCollector.Collect(typeDecl, projectName, namespaceName, typeName);

                members.AddRange(memberMetrics);
                types.Add(TypeMetricsBuilder.Build(
                    typeDecl,
                    typeSymbol,
                    semanticModel,
                    projectName,
                    namespaceName,
                    typeName,
                    tree.FilePath,
                    memberMetrics));
            }
        }

        return (types, members);
    }
}
