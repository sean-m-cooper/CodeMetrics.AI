namespace CodeMetrics.AI.Output;

internal static class EvidenceFactory
{
    public static EvidenceModel Create(
        SolutionAnalysisContext context,
        string solutionPath,
        string solutionDir,
        string configuration,
        Dictionary<string, object> dimensions)
    {
        return new EvidenceModel
        {
            Subject = CreateSubject(solutionPath, solutionDir, configuration),
            Filters = CreateFilters(context),
            Population = CreatePopulation(context),
            Dimensions = dimensions
        };
    }

    private static SubjectInfo CreateSubject(
        string solutionPath,
        string solutionDir,
        string configuration)
    {
        return new SubjectInfo
        {
            Root = solutionDir,
            EntryPoint = solutionPath,
            Name = Path.GetFileNameWithoutExtension(solutionPath),
            Variant = configuration
        };
    }

    private static FilterInfo CreateFilters(SolutionAnalysisContext context)
    {
        return new FilterInfo
        {
            TotalUnits = context.TotalProjectCount,
            AnalyzedUnits = context.AnalyzedProjectNames.Count,
            Skipped = context.SkippedProjects.ToList()
        };
    }

    private static PopulationInfo CreatePopulation(SolutionAnalysisContext context)
    {
        return new PopulationInfo
        {
            Types = context.TypeMetrics.Count,
            Members = context.MemberMetrics.Count
        };
    }
}
