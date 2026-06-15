using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

internal static class TypeMetricsBuilder
{
    public static TypeMetrics Build(
        TypeDeclarationSyntax typeDecl,
        INamedTypeSymbol typeSymbol,
        SemanticModel model,
        string project,
        string ns,
        string type,
        string filePath,
        List<MemberMetrics> memberMetrics)
    {
        var bodiedMembers = memberMetrics.Where(m => m.HasBody).ToList();
        var typeComplexity = 1 + bodiedMembers.Sum(m => m.CyclomaticComplexity);
        var memberCount = memberMetrics.Count;

        return new TypeMetrics
        {
            Project = project,
            Namespace = ns,
            Type = type,
            FilePath = filePath,
            CyclomaticComplexity = typeComplexity,
            MaintainabilityIndex = AverageMaintainabilityIndex(memberMetrics),
            DepthOfInheritance = DepthOfInheritanceCalculator.Calculate(typeSymbol),
            ClassCoupling = ClassCouplingCalculator.Calculate(typeDecl, model),
            LinesOfSource = LinesOfCodeCounter.CountSourceLines(typeDecl),
            LinesOfExecutable = LinesOfCodeCounter.CountExecutableLines(typeDecl),
            MemberCount = memberCount,
            MaxMemberCyclomaticComplexity = MaxCyclomaticComplexity(bodiedMembers),
            DecompositionRatio = DecompositionRatio(typeComplexity, memberCount),
        };
    }

    private static int AverageMaintainabilityIndex(List<MemberMetrics> memberMetrics)
    {
        return memberMetrics.Count > 0
            ? (int)Math.Round(memberMetrics.Average(m => (double)m.MaintainabilityIndex))
            : 100;
    }

    private static int MaxCyclomaticComplexity(List<MemberMetrics> bodiedMembers)
    {
        return bodiedMembers.Count > 0
            ? bodiedMembers.Max(m => m.CyclomaticComplexity)
            : 0;
    }

    private static double DecompositionRatio(int typeComplexity, int memberCount)
    {
        return memberCount > 0
            ? Math.Round((double)typeComplexity / memberCount, 4)
            : 0;
    }
}
