using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

internal static class ArchitectureEvidence
{
    public static DimensionResult Create(IReadOnlyList<TypeMetrics> types,
        ArchitectureObservations observations, ArchitectureAssessment assessment)
    {
        var displayedHotspots = assessment.Hotspots.Take(10).ToList();
        var exclusions = SummarizeExclusions(types, observations.Scope);
        return new()
        {
            Status = "scored",
            Score = assessment.Decision.FinalScore,
            ScoringDecision = assessment.Decision,
            Findings = assessment.Findings,
            Basis = BuildBasis(assessment, observations.Cycles.Count, displayedHotspots.Count, exclusions),
            Extra =
            {
                ["architectureMetrics"] = MetricDetails(assessment),
                ["cycles"] = observations.Cycles.Select(c => string.Join(" → ", c) + " → " + c[0]).ToList(),
                ["hotspots"] = displayedHotspots.Select(h => new { h.Project, h.Type, h.Category, h.Message }).ToList<object>(),
                ["hotspotCount"] = assessment.Hotspots.Count,
                ["hotspotsTruncated"] = assessment.Hotspots.Count > displayedHotspots.Count,
                ["excludedPassiveDataCarriers"] = exclusions.DataCarriers,
                ["excludedDependencyInjectionExtensionTypes"] = exclusions.DependencyInjectionExtensions,
                ["excludedFrameworkCouplingArchetypeTypes"] = exclusions.FrameworkArchetypes,
                ["excludedApplicationCompositionRoots"] = exclusions.CompositionRoots,
                ["excludedCouplingReferencesByReason"] = exclusions.CouplingReferences,
                ["couplingProvenance"] = CouplingProvenance(types, observations.Scope),
                // Supplemental evidence never feeds back into the score.
                ["controllerActionCoupling"] = ControllerSummaries(observations.ControllerActions)
            }
        };
    }

    private static object MetricDetails(ArchitectureAssessment assessment) => new
    {
        policy = "population-severity-v1",
        formula = "component = 10 - min(6, 12 * hotspotRate) - min(4, 2 * max(0, worstThresholdRatio - 1)); final = round(min(components, graphLayeringCap), 1, awayFromZero)",
        components = assessment.Components,
        metricScore = assessment.MetricScore,
        graphLayeringCap = assessment.LayeringCap,
        graphLayeringReason = assessment.LayeringReason,
        finalScore = assessment.Decision.FinalScore
    };

    private static ExclusionSummary SummarizeExclusions(IReadOnlyList<TypeMetrics> types, ArchitectureTypeScope scope) => new(
        types.Count(metric => metric.IsDataCarrier), types.Count(scope.IsDependencyInjectionExtension),
        types.Count(scope.IsFrameworkCouplingArchetype), types.Count(scope.IsApplicationCompositionRoot),
        types.SelectMany(metric => metric.CouplingExclusions)
            .GroupBy(exclusion => exclusion.Key, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(exclusion => exclusion.Value.Count), StringComparer.Ordinal));

    private static string BuildBasis(ArchitectureAssessment assessment, int cycleCount, int displayedCount, ExclusionSummary exclusions)
    {
        var hotspotBasis = assessment.Hotspots.Count > displayedCount
            ? $"hotspots: {assessment.Hotspots.Count} (showing {displayedCount})"
            : $"hotspots: {assessment.Hotspots.Count}";
        return $"Findings: {assessment.Findings.Count} (errors: {assessment.ErrorCount}, warnings: {assessment.WarningCount}). " +
                    $"Cycles: {cycleCount}, {hotspotBasis}. " +
                    $"Excluded passive data carriers: {exclusions.DataCarriers}, " +
                    $"DI extension types: {exclusions.DependencyInjectionExtensions}, " +
                    $"framework coupling archetypes: {exclusions.FrameworkArchetypes}, " +
                    $"application composition roots: {exclusions.CompositionRoots}. " +
                    string.Join("; ", assessment.Components.Select(component =>
                        FormattableString.Invariant($"{component.Metric}: {component.HotspotCount}/{component.EligibleTypeCount} eligible types, score {component.Score:F2}"))) +
                    FormattableString.Invariant($". Final = min(metric score {assessment.MetricScore:F2}, graph/layering cap {assessment.LayeringCap:F1}), rounded to 1 decimal.");
    }

    private static List<object> ControllerSummaries(IReadOnlyList<ControllerActionObservation> observations)
    {
        return observations
            .GroupBy(observation => new
            {
                observation.Project,
                observation.Namespace,
                observation.Type
            })
            .Select(group => new
            {
                group.Key.Project,
                group.Key.Namespace,
                group.Key.Type,
                ConstructorDependencyCount = group
                    .SelectMany(observation => observation.ConstructorDependencyTypes)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                MaxActionTypeCoupling = group.Max(observation => observation.TypeCoupling),
                MaxStructuralActionCoupling = group.Max(observation => observation.StructuralTypeCoupling),
                MaxFromServicesParameters = group.Max(observation => observation.FromServicesParameters),
                Actions = group
                    .OrderBy(observation => observation.Method, StringComparer.Ordinal)
                    .ThenBy(observation => observation.Line)
                    .Select(observation => new
                    {
                        observation.Method,
                        observation.Line,
                        observation.TypeCoupling,
                        observation.StructuralTypeCoupling,
                        observation.FromServicesParameters
                    })
                    .ToList()
            })
            .OrderByDescending(summary => summary.MaxStructuralActionCoupling)
            .ThenByDescending(summary => summary.MaxActionTypeCoupling)
            .ThenBy(summary => summary.Project, StringComparer.Ordinal)
            .ThenBy(summary => summary.Namespace, StringComparer.Ordinal)
            .ThenBy(summary => summary.Type, StringComparer.Ordinal)
            .ToList<object>();
    }

    private static List<object> CouplingProvenance(IReadOnlyList<TypeMetrics> typeMetrics, ArchitectureTypeScope scope)
    {
        return typeMetrics
            .Where(metric =>
                metric.ClassCoupling >= ArchitectureScoring.LegacyRawCouplingThreshold(metric) ||
                ArchitectureScoring.IsCouplingHotspot(metric, scope))
            .OrderByDescending(ArchitectureScoring.ScoredCoupling)
            .ThenByDescending(metric => metric.ClassCoupling)
            .ThenBy(metric => metric.Project, StringComparer.Ordinal)
            .ThenBy(metric => metric.Namespace, StringComparer.Ordinal)
            .ThenBy(metric => metric.Type, StringComparer.Ordinal)
            .Select(metric => new
            {
                metric.Project,
                metric.Namespace,
                metric.Type,
                metric.ClassCoupling,
                CoupledTypes = metric.CoupledTypes,
                StructuralClassCoupling = ArchitectureScoring.ScoredCoupling(metric),
                StructuralCoupledTypes = metric.StructuralCoupledTypes,
                CouplingExclusions = metric.CouplingExclusions,
                WouldExceedRawThreshold = metric.ClassCoupling >= ArchitectureScoring.LegacyRawCouplingThreshold(metric)
            })
            .ToList<object>();
    }

    private sealed record ExclusionSummary(int DataCarriers, int DependencyInjectionExtensions,
        int FrameworkArchetypes, int CompositionRoots, Dictionary<string, int> CouplingReferences);
}
