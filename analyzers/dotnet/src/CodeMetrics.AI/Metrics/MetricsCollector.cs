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
            var filePath = tree.FilePath;

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
                if (typeSymbol == null) continue;

                var namespaceName = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
                var typeName = typeSymbol.Name;

                var memberMetricsList = CollectMemberMetrics(
                    typeDecl, projectName, namespaceName, typeName);
                members.AddRange(memberMetricsList);

                var typeMetric = BuildTypeMetrics(
                    typeDecl, typeSymbol, semanticModel,
                    projectName, namespaceName, typeName, filePath,
                    memberMetricsList);
                types.Add(typeMetric);
            }
        }

        return (types, members);
    }

    private static List<MemberMetrics> CollectMemberMetrics(
        TypeDeclarationSyntax typeDecl, string project, string ns, string type)
    {
        var result = new List<MemberMetrics>();

        foreach (var member in typeDecl.Members)
        {
            if (member is TypeDeclarationSyntax) continue; // skip nested types

            var memberName = GetMemberName(member);
            if (memberName == null) continue;

            bool hasBody = HasMethodBody(member);

            int cc = 1;
            int srcLines = 0;
            int execLines = 0;
            int mi = 100;

            if (hasBody)
            {
                var ccWalker = new CyclomaticComplexityWalker();
                ccWalker.Visit(member);
                cc = ccWalker.Complexity;

                srcLines = LinesOfCodeCounter.CountSourceLines(member);
                execLines = LinesOfCodeCounter.CountExecutableLines(member);

                var hv = HalsteadCalculator.ComputeVolume(member);
                mi = MaintainabilityIndexCalculator.Calculate(cc, srcLines, hv);
            }

            result.Add(new MemberMetrics
            {
                Project = project,
                Namespace = ns,
                Type = type,
                Member = memberName,
                CyclomaticComplexity = cc,
                LinesOfSource = srcLines,
                LinesOfExecutable = execLines,
                MaintainabilityIndex = mi,
                HasBody = hasBody,
            });
        }

        return result;
    }

    private static TypeMetrics BuildTypeMetrics(
        TypeDeclarationSyntax typeDecl, INamedTypeSymbol typeSymbol,
        SemanticModel model, string project, string ns, string type,
        string filePath, List<MemberMetrics> memberMetrics)
    {
        var bodiedMembers = memberMetrics.Where(m => m.HasBody).ToList();

        int typeCC = 1 + bodiedMembers.Sum(m => m.CyclomaticComplexity);
        int memberCount = memberMetrics.Count;
        int maxMemberCC = bodiedMembers.Count > 0
            ? bodiedMembers.Max(m => m.CyclomaticComplexity) : 0;

        int avgMI = memberMetrics.Count > 0
            ? (int)Math.Round(memberMetrics.Average(m => (double)m.MaintainabilityIndex))
            : 100;

        int coupling = ClassCouplingCalculator.Calculate(typeDecl, model);
        int doi = DepthOfInheritanceCalculator.Calculate(typeSymbol);
        int srcLines = LinesOfCodeCounter.CountSourceLines(typeDecl);
        int execLines = LinesOfCodeCounter.CountExecutableLines(typeDecl);

        double decomp = memberCount > 0
            ? Math.Round((double)typeCC / memberCount, 4) : 0;

        return new TypeMetrics
        {
            Project = project,
            Namespace = ns,
            Type = type,
            FilePath = filePath,
            CyclomaticComplexity = typeCC,
            MaintainabilityIndex = avgMI,
            DepthOfInheritance = doi,
            ClassCoupling = coupling,
            LinesOfSource = srcLines,
            LinesOfExecutable = execLines,
            MemberCount = memberCount,
            MaxMemberCyclomaticComplexity = maxMemberCC,
            DecompositionRatio = decomp,
        };
    }

    private static string? GetMemberName(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        PropertyDeclarationSyntax p => p.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        EventFieldDeclarationSyntax ef => ef.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
        OperatorDeclarationSyntax o => $"operator {o.OperatorToken.Text}",
        ConversionOperatorDeclarationSyntax co => $"operator {co.Type}",
        IndexerDeclarationSyntax => "this[]",
        DestructorDeclarationSyntax d => $"~{d.Identifier.Text}",
        _ => null
    };

    private static bool HasMethodBody(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Body != null || m.ExpressionBody != null,
        ConstructorDeclarationSyntax c => c.Body != null || c.ExpressionBody != null,
        PropertyDeclarationSyntax p => p.AccessorList?.Accessors.Any(a => a.Body != null || a.ExpressionBody != null) == true
                                       || p.ExpressionBody != null,
        IndexerDeclarationSyntax i => i.AccessorList?.Accessors.Any(a => a.Body != null || a.ExpressionBody != null) == true
                                      || i.ExpressionBody != null,
        OperatorDeclarationSyntax o => o.Body != null || o.ExpressionBody != null,
        ConversionOperatorDeclarationSyntax co => co.Body != null || co.ExpressionBody != null,
        DestructorDeclarationSyntax d => d.Body != null || d.ExpressionBody != null,
        _ => false
    };
}
