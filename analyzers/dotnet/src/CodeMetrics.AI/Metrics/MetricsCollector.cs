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

        foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            CollectTree(projectName, compilation, tree, types, members);

        return (types, members);
    }

    private static void CollectTree(
        string projectName,
        Compilation compilation,
        SyntaxTree tree,
        List<TypeMetrics> types,
        List<MemberMetrics> members)
    {
        var semanticModel = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            CollectType(projectName, tree.FilePath, typeDeclaration, semanticModel, types, members);
    }

    private static void CollectType(
        string projectName,
        string filePath,
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel,
        List<TypeMetrics> types,
        List<MemberMetrics> members)
    {
        if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol typeSymbol)
            return;

        var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        var typeName = typeSymbol.Name;
        var memberMetrics = CollectMemberMetrics(
            typeDeclaration,
            semanticModel,
            projectName,
            namespaceName,
            typeName);
        members.AddRange(memberMetrics);
        types.Add(BuildTypeMetrics(
            typeDeclaration,
            typeSymbol,
            semanticModel,
            projectName,
            namespaceName,
            typeName,
            filePath,
            memberMetrics));
    }

    private static List<MemberMetrics> CollectMemberMetrics(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        string project,
        string ns,
        string type)
    {
        var result = new List<MemberMetrics>();

        foreach (var member in typeDecl.Members)
        {
            var metrics = BuildMemberMetrics(member, semanticModel, project, ns, type);
            if (metrics != null)
                result.Add(metrics);
        }

        return result;
    }

    private static MemberMetrics? BuildMemberMetrics(
        MemberDeclarationSyntax member,
        SemanticModel semanticModel,
        string project,
        string ns,
        string type)
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
        TypeDeclarationSyntax typeDecl, INamedTypeSymbol typeSymbol,
        SemanticModel model, string project, string ns, string type,
        string filePath, List<MemberMetrics> memberMetrics)
    {
        var aggregate = CalculateAggregates(memberMetrics);
        var coupling = ClassCouplingCalculator.Analyze(typeDecl, model);
        return new TypeMetrics
        {
            Project = project,
            Namespace = ns,
            Type = type,
            FilePath = filePath,
            CyclomaticComplexity = aggregate.CyclomaticComplexity,
            MaintainabilityIndex = aggregate.MaintainabilityIndex,
            DepthOfInheritance = DepthOfInheritanceCalculator.Calculate(typeSymbol),
            ClassCoupling = coupling.RawTypes.Count,
            CoupledTypes = coupling.RawTypes,
            StructuralClassCoupling = coupling.StructuralTypes.Count,
            StructuralCoupledTypes = coupling.StructuralTypes,
            CouplingExclusions = coupling.ExcludedTypes,
            LinesOfSource = LinesOfCodeCounter.CountSourceLines(typeDecl),
            LinesOfExecutable = LinesOfCodeCounter.CountExecutableLines(typeDecl),
            MemberCount = aggregate.MemberCount,
            MaxMemberCyclomaticComplexity = aggregate.MaxMemberCyclomaticComplexity,
            DecompositionRatio = aggregate.DecompositionRatio,
            IsDataCarrier = DataCarrierClassifier.IsPassiveDataCarrier(typeSymbol),
        };
    }

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
