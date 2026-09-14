using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

internal sealed record ArchitectureObservations(
    IReadOnlyList<List<string>> Cycles,
    IReadOnlyList<Finding> Findings,
    ArchitectureTypeScope Scope,
    IReadOnlyList<ControllerActionObservation> ControllerActions);

internal sealed record ControllerActionObservation(
        string Project,
        string Namespace,
        string Type,
        string Method,
        int Line,
        int TypeCoupling,
        int StructuralTypeCoupling,
        int FromServicesParameters,
        IReadOnlyList<string> ConstructorDependencyTypes);

internal sealed record ArchitectureTypeScope(
    IReadOnlySet<string> DependencyInjectionExtensionTypes,
    IReadOnlySet<string> FrameworkCouplingArchetypeTypes,
    IReadOnlySet<string> ApplicationProjects)
{
    public static string Key(string project, string namespaceName, string typeName) =>
        $"{project}\0{namespaceName}\0{typeName}";

    public bool IsDependencyInjectionExtension(TypeMetrics metric) =>
        DependencyInjectionExtensionTypes.Contains(Key(metric.Project, metric.Namespace, metric.Type));

    public bool IsFrameworkCouplingArchetype(TypeMetrics metric) =>
        FrameworkCouplingArchetypeTypes.Contains(Key(metric.Project, metric.Namespace, metric.Type));

    public bool IsApplicationCompositionRoot(TypeMetrics metric) =>
        ApplicationProjects.Contains(metric.Project) &&
        (metric.Type is "Program" or "Startup" ||
            metric.FilePath.EndsWith("Program.cs", StringComparison.OrdinalIgnoreCase) ||
            metric.FilePath.EndsWith("Startup.cs", StringComparison.OrdinalIgnoreCase));

    public bool IsMetricEligible(TypeMetrics metric) => !metric.IsDataCarrier && !IsDependencyInjectionExtension(metric);

    public bool IsCouplingEligible(TypeMetrics metric) =>
        IsMetricEligible(metric) && !IsFrameworkCouplingArchetype(metric) && !IsApplicationCompositionRoot(metric);
}

internal sealed record ArchitectureAssessment(
    ScoringDecision Decision,
    MetricPopulationScore[] Components,
    double MetricScore,
    double LayeringCap,
    string LayeringReason,
    int ErrorCount,
    int WarningCount,
    List<Finding> Findings,
    List<Finding> Hotspots);

internal sealed record MetricPopulationScore(
        string Metric, int EligibleTypeCount, int HotspotCount, double HotspotRate,
        double WorstThresholdRatio, double PopulationPenalty, double SeverityPenalty, double Score);
