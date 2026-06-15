using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

internal static class MemberMetricsCollector
{
    public static List<MemberMetrics> Collect(
        TypeDeclarationSyntax typeDecl, string project, string ns, string type)
    {
        var result = new List<MemberMetrics>();

        foreach (var member in typeDecl.Members)
        {
            var memberName = MemberSyntaxInfo.GetName(member);
            if (member is TypeDeclarationSyntax || memberName == null)
                continue;

            result.Add(BuildMemberMetrics(member, memberName, project, ns, type));
        }

        return result;
    }

    private static MemberMetrics BuildMemberMetrics(
        MemberDeclarationSyntax member, string memberName, string project, string ns, string type)
    {
        var hasBody = MemberSyntaxInfo.HasBody(member);
        var metrics = hasBody
            ? ComputeBodiedMemberMetrics(member)
            : MemberBodyMetrics.Empty;

        return new MemberMetrics
        {
            Project = project,
            Namespace = ns,
            Type = type,
            Member = memberName,
            CyclomaticComplexity = metrics.CyclomaticComplexity,
            LinesOfSource = metrics.LinesOfSource,
            LinesOfExecutable = metrics.LinesOfExecutable,
            MaintainabilityIndex = metrics.MaintainabilityIndex,
            HasBody = hasBody,
        };
    }

    private static MemberBodyMetrics ComputeBodiedMemberMetrics(MemberDeclarationSyntax member)
    {
        var ccWalker = new CyclomaticComplexityWalker();
        ccWalker.Visit(member);

        var sourceLines = LinesOfCodeCounter.CountSourceLines(member);
        var executableLines = LinesOfCodeCounter.CountExecutableLines(member);
        var halsteadVolume = HalsteadCalculator.ComputeVolume(member);

        return new MemberBodyMetrics(
            ccWalker.Complexity,
            sourceLines,
            executableLines,
            MaintainabilityIndexCalculator.Calculate(ccWalker.Complexity, sourceLines, halsteadVolume));
    }

    private sealed record MemberBodyMetrics(
        int CyclomaticComplexity,
        int LinesOfSource,
        int LinesOfExecutable,
        int MaintainabilityIndex)
    {
        public static MemberBodyMetrics Empty { get; } = new(1, 0, 0, 100);
    }
}
