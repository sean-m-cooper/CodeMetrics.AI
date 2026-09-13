using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

// Raw CSV member measurements retain their historical counting/body rules.
// Executable-function scoring is collected independently at the type level.
internal static class MemberMetricsCollector
{
    internal static List<MemberMetrics> Collect(
        IEnumerable<(TypeDeclarationSyntax Declaration, SemanticModel Model)> parts,
        string project,
        string ns,
        string type,
        string typeId)
    {
        var result = new List<MemberMetrics>();
        var partialMembers = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);

        foreach (var part in parts)
            foreach (var member in part.Declaration.Members)
            {
                if (member is TypeDeclarationSyntax)
                    continue;
                var symbol = GetMemberSymbol(member, part.Model);
                var metrics = BuildMemberMetrics(member, symbol, project, ns, type, typeId);
                if (metrics == null)
                    continue;

                // A partial member's signature and implementation describe one member.
                // Only consider declarations in authored, included trees: never pull in generated bodies.
                var definition = symbol switch
                {
                    IMethodSymbol method => method.PartialDefinitionPart ?? method,
                    IPropertySymbol property => (ISymbol?)property.PartialDefinitionPart ?? property,
                    _ => null
                };
                if (definition != null && partialMembers.TryGetValue(definition, out var index))
                {
                    if (metrics.HasBody)
                        result[index] = metrics;
                    continue;
                }
                if (definition != null)
                    partialMembers[definition] = result.Count;
                result.Add(metrics);
            }

        return result;
    }

    private static MemberMetrics? BuildMemberMetrics(
        MemberDeclarationSyntax member,
        ISymbol? symbol,
        string project,
        string ns,
        string type,
        string typeId)
    {
        var memberName = GetMemberName(symbol);
        if (memberName == null)
            return null;

        var hasBody = HasMethodBody(member);
        var (complexity, sourceLines, executableLines, maintainabilityIndex) = hasBody
            ? CalculateBodyMetrics(member)
            : (1, 0, 0, 100);
        return new MemberMetrics
        {
            Project = project,
            Namespace = ns,
            Type = type,
            TypeId = typeId,
            Member = memberName,
            CyclomaticComplexity = complexity,
            LinesOfSource = sourceLines,
            LinesOfExecutable = executableLines,
            MaintainabilityIndex = maintainabilityIndex,
            HasBody = hasBody,
        };
    }

    private static (int Complexity, int SourceLines, int ExecutableLines, int MaintainabilityIndex)
        CalculateBodyMetrics(MemberDeclarationSyntax member)
    {
        var complexityWalker = new CyclomaticComplexityWalker();
        complexityWalker.Visit(member);
        var sourceLines = LinesOfCodeCounter.CountSourceLines(member);
        return (
            complexityWalker.Complexity,
            sourceLines,
            LinesOfCodeCounter.CountExecutableLines(member),
            MaintainabilityIndexCalculator.Calculate(
                complexityWalker.Complexity,
                sourceLines,
                HalsteadCalculator.ComputeVolume(member)));
    }

    private static ISymbol? GetMemberSymbol(
        MemberDeclarationSyntax member,
        SemanticModel semanticModel)
    {
        var variable = member switch
        {
            FieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault(),
            EventFieldDeclarationSyntax field => field.Declaration.Variables.FirstOrDefault(),
            _ => null
        };
        if (variable != null)
            return semanticModel.GetDeclaredSymbol(variable);

        return semanticModel.GetDeclaredSymbol(member);
    }

    private static string? GetMemberName(ISymbol? symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { } owner } => owner.Name,
            IMethodSymbol { MethodKind: MethodKind.Destructor, ContainingType: { } owner } => $"~{owner.Name}",
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method => method.ToDisplayString(),
            IMethodSymbol { MethodKind: MethodKind.Conversion } method => method.ToDisplayString(),
            IPropertySymbol { IsIndexer: true } => "this[]",
            { } named => named.Name,
            _ => null
        };
    }

    private static bool HasMethodBody(MemberDeclarationSyntax member)
    {
        if (member is FieldDeclarationSyntax or EventFieldDeclarationSyntax)
            return false;

        return member.DescendantNodes(node => node is not TypeDeclarationSyntax)
            .Any(node => node is BlockSyntax or ArrowExpressionClauseSyntax);
    }
}
