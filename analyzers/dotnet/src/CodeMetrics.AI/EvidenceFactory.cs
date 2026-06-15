using CodeMetrics.AI.Output;

namespace CodeMetrics.AI;

internal static class EvidenceFactory
{
    public static EvidenceModel Create(
        string solutionPath,
        string solutionDir,
        CliOptions options,
        ProjectSelection selection,
        MetricsAnalysisResult metrics,
        Dictionary<string, object> dimensions)
    {
        return new EvidenceModel
        {
            Subject = new SubjectInfo
            {
                Root = solutionDir,
                EntryPoint = Path.GetFullPath(solutionPath),
                Name = Path.GetFileNameWithoutExtension(solutionPath),
                Variant = options.Configuration
            },
            Filters = new FilterInfo
            {
                TotalUnits = selection.AllProjects.Count,
                AnalyzedUnits = selection.AnalyzedProjects.Count,
                Skipped = selection.Skipped,
            },
            Population = new PopulationInfo
            {
                Types = metrics.TypeMetrics.Count,
                Members = metrics.MemberMetrics.Count,
            },
            Dimensions = dimensions,
        };
    }
}
