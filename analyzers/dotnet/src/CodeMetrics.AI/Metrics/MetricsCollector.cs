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
        var memberMetrics = CollectMemberMetrics(
            parts,
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

    private static List<MemberMetrics> CollectMemberMetrics(
        IReadOnlyList<TypePart> parts,
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
                var metrics = BuildMemberMetrics(member, part.Model, project, ns, type, typeId);
                if (metrics == null)
                    continue;

                // A partial member's signature and implementation describe one member.
                // Only consider declarations in authored, included trees: never pull in generated bodies.
                var definition = part.Model.GetDeclaredSymbol(member) switch
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
        SemanticModel semanticModel,
        string project,
        string ns,
        string type,
        string typeId)
    {
        if (member is TypeDeclarationSyntax)
            return null;

        var memberName = GetMemberName(member, semanticModel);
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
            ExecutableMetrics = new ExecutableTypeMetrics(parts
                .SelectMany(part => ExecutableFunctionCollector.Collect(part.Declaration, part.Model)).ToArray())
            {
                MaintainabilityFunctions = parts.SelectMany(part =>
                    ExecutableFunctionCollector.Collect(part.Declaration, part.Model, forMaintainability: true)).ToArray()
            },
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

    private static string? GetMemberName(
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
            return semanticModel.GetDeclaredSymbol(variable)?.Name;

        return semanticModel.GetDeclaredSymbol(member) switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { } owner } => owner.Name,
            IMethodSymbol { MethodKind: MethodKind.Destructor, ContainingType: { } owner } => $"~{owner.Name}",
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } method => method.ToDisplayString(),
            IMethodSymbol { MethodKind: MethodKind.Conversion } method => method.ToDisplayString(),
            IPropertySymbol { IsIndexer: true } => "this[]",
            { } symbol => symbol.Name,
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
