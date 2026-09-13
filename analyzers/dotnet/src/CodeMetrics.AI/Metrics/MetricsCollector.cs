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

        var declarations = new Dictionary<INamedTypeSymbol, List<TypePart>>(SymbolEqualityComparer.Default);
        foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                    continue;
                if (!declarations.TryGetValue(symbol, out var parts))
                    declarations[symbol] = parts = [];
                parts.Add(new TypePart(declaration, model));
            }
        }

        foreach (var (symbol, parts) in declarations.OrderBy(pair => pair.Key.ToDisplayString(), StringComparer.Ordinal))
            CollectType(projectName, symbol, parts, types, members);

        return (types, members);
    }

    private static void CollectType(
        string projectName,
        INamedTypeSymbol typeSymbol,
        List<TypePart> parts,
        List<TypeMetrics> types,
        List<MemberMetrics> members)
    {
        parts = parts.OrderBy(part => part.Declaration.SyntaxTree.FilePath, StringComparer.Ordinal)
            .ThenBy(part => part.Declaration.SpanStart).ToList();
        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        var typeName = typeSymbol.Name;
        var typeId = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var memberMetrics = MemberMetricsCollector.Collect(
            parts.Select(part => (part.Declaration, part.Model)),
            projectName,
            namespaceName,
            typeName,
            typeId);
        members.AddRange(memberMetrics);
        types.Add(BuildTypeMetrics(
            parts,
            typeSymbol,
            projectName,
            namespaceName,
            typeName,
            typeId,
            memberMetrics));
    }

    private static TypeMetrics BuildTypeMetrics(
        IReadOnlyList<TypePart> parts, INamedTypeSymbol typeSymbol,
        string project, string ns, string type,
        string typeId, List<MemberMetrics> memberMetrics)
    {
        var aggregate = CalculateAggregates(memberMetrics);
        var couplings = parts.Select(part => ClassCouplingCalculator.Analyze(part.Declaration, part.Model)).ToList();
        var raw = Union(couplings.SelectMany(coupling => coupling.RawTypes));
        var structural = Union(couplings.SelectMany(coupling => coupling.StructuralTypes));
        var exclusions = couplings.SelectMany(coupling => coupling.ExcludedTypes)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => Union(group.SelectMany(pair => pair.Value).Except(structural, StringComparer.Ordinal)),
                StringComparer.Ordinal);
        var files = Union(parts.Select(part => part.Declaration.SyntaxTree.FilePath));
        return new TypeMetrics
        {
            IsWebController = WebTypeClassifier.IsController(typeSymbol),
            Project = project,
            Namespace = ns,
            Type = type,
            TypeId = typeId,
            FilePath = files[0],
            SourceFiles = files,
            CyclomaticComplexity = aggregate.CyclomaticComplexity,
            MaintainabilityIndex = aggregate.MaintainabilityIndex,
            DepthOfInheritance = DepthOfInheritanceCalculator.Calculate(typeSymbol),
            ClassCoupling = raw.Count,
            CoupledTypes = raw,
            StructuralClassCoupling = structural.Count,
            StructuralCoupledTypes = structural,
            CouplingExclusions = exclusions.Where(pair => pair.Value.Count > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            LinesOfSource = parts.Sum(part => LinesOfCodeCounter.CountSourceLines(part.Declaration)),
            LinesOfExecutable = parts.Sum(part => LinesOfCodeCounter.CountExecutableLines(part.Declaration)),
            MemberCount = aggregate.MemberCount,
            MaxMemberCyclomaticComplexity = aggregate.MaxMemberCyclomaticComplexity,
            DecompositionRatio = aggregate.DecompositionRatio,
            IsDataCarrier = DataCarrierClassifier.IsPassiveDataCarrier(typeSymbol),
            ExecutableMetrics = ExecutableFunctionCollector.Collect(parts.Select(part => (part.Declaration, part.Model))),
        };
    }

    private static IReadOnlyList<string> Union(IEnumerable<string> values) =>
        values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList();

    private sealed record TypePart(TypeDeclarationSyntax Declaration, SemanticModel Model);

    private static TypeMetricAggregates CalculateAggregates(
        IReadOnlyList<MemberMetrics> memberMetrics)
    {
        var bodiedMembers = memberMetrics.Where(member => member.HasBody).ToList();
        var complexity = 1 + bodiedMembers.Sum(member => member.CyclomaticComplexity);
        var maxMemberComplexity = bodiedMembers.Count > 0
            ? bodiedMembers.Max(member => member.CyclomaticComplexity)
            : 0;
        var maintainabilityIndex = memberMetrics.Count > 0
            ? (int)Math.Round(memberMetrics.Average(member => (double)member.MaintainabilityIndex))
            : 100;
        var decompositionRatio = memberMetrics.Count > 0
            ? Math.Round((double)complexity / memberMetrics.Count, 4)
            : 0;
        return new TypeMetricAggregates(
            complexity,
            maintainabilityIndex,
            memberMetrics.Count,
            maxMemberComplexity,
            decompositionRatio);
    }

    private sealed record TypeMetricAggregates(
        int CyclomaticComplexity,
        int MaintainabilityIndex,
        int MemberCount,
        int MaxMemberCyclomaticComplexity,
        double DecompositionRatio);

}
