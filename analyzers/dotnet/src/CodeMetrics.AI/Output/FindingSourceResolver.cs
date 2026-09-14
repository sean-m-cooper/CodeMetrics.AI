using CodeMetrics.AI.Probes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Output;

internal sealed class FindingSourceResolver(
    IReadOnlyDictionary<string, SyntaxTree> trees, string root, string repositoryRoot)
{
    public string Resolve(Finding finding)
    {
        if (finding.File is not { Length: > 0 } file)
            return "";

        var fullPath = Path.GetFullPath(file, root);
        var anchor = trees.TryGetValue(fullPath, out var tree) ? ResolveNode(finding, tree) : "";
        finding.File = Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');
        return anchor;
    }

    private static string ResolveNode(Finding finding, SyntaxTree tree)
    {
        var node = FindNode(finding, tree);
        if (node == null)
            return "";

        finding.Member = MemberName(node);
        finding.Line ??= tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
        var statement = node.AncestorsAndSelf().FirstOrDefault(candidate => candidate is StatementSyntax or CatchClauseSyntax);
        return statement == null ? "" : string.Join(" ", statement.DescendantTokens().Select(token => token.Text));
    }

    private static SyntaxNode? FindNode(Finding finding, SyntaxTree tree)
    {
        var syntaxRoot = tree.GetRoot();
        if (finding.Line is > 0 && finding.Line <= tree.GetText().Lines.Count)
            return syntaxRoot.FindToken(tree.GetText().Lines[finding.Line.Value - 1].Start).Parent;
        return finding.Type == null ? null : syntaxRoot.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(type => type.Identifier.Text == finding.Type);
    }

    private static string? MemberName(SyntaxNode node)
    {
        var member = node.AncestorsAndSelf().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text + method.ParameterList.WithoutTrivia().ToString(),
            ConstructorDeclarationSyntax constructor => constructor.Identifier.Text + constructor.ParameterList.WithoutTrivia().ToString(),
            null => null,
            _ => member.Kind().ToString()
        };
    }
}
