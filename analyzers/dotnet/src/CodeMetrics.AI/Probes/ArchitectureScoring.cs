using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

internal static class ArchitectureScoring
{
    private const int StructuralCouplingThreshold = 10;
    private const int ControllerStructuralCouplingThreshold = 8;
    private const double HighComplexityDensityThreshold = 8;
    private const int LegacyRawCouplingThresholdValue = 30;
    private const int LegacyControllerRawCouplingThreshold = 50;

    public static ArchitectureAssessment Evaluate(
        IReadOnlyList<TypeMetrics> typeMetrics, ArchitectureObservations observations)
    {
        var hotspots = FindMetricHotspots(typeMetrics, observations.Scope);
        var findings = observations.Findings.Concat(hotspots).ToList();
        var errors = findings.Count(finding => finding.Severity == "error");
        var warnings = findings.Count(finding => finding.Severity == "warning");
        var advisoryWarnings = warnings - hotspots.Count;
        var layering = ScoreLayering(observations.Cycles.Count, errors, advisoryWarnings);
        var components = ScoreComponents(typeMetrics, observations.Scope);
        return new(CreateDecision(components, layering), components, components.Min(component => component.Score), layering.FinalScore,
            LayeringReason(observations.Cycles.Count, errors, advisoryWarnings),
            errors, warnings, findings, hotspots);
    }

    private static ScoringDecision ScoreLayering(int cycleCount, int errorCount, int warningCount)
    {
        return ScoringDecision.FirstMatch("dotnet/architecture/layering/v1", new()
        {
            ["cycles"] = cycleCount,
            ["errors"] = errorCount,
            ["advisoryWarnings"] = warningCount
        },
        ScoringStep.Rule("projectCycles", "cycles > 0", cycleCount > 0, 0, "projectCycle"),
        ScoringStep.Rule("layeringErrors", "errors > 0", errorCount > 0, 2, "controllerDataDependency"),
        ScoringStep.Rule("manyLayeringWarnings", "advisoryWarnings > 2", warningCount > 2, 4, "concreteInfrastructureDependency"),
        ScoringStep.Rule("severalLayeringWarnings", "advisoryWarnings > 1", warningCount > 1, 6, "concreteInfrastructureDependency"),
        ScoringStep.Rule("layeringWarning", "advisoryWarnings > 0", warningCount > 0, 8, "concreteInfrastructureDependency"),
        ScoringStep.Rule("noLayeringCap", "otherwise", true, 10));
    }

    private static string LayeringReason(int cycles, int errors, int warnings) =>
        cycles > 0 ? "projectCycle" : errors > 0 ? "layeringError" : warnings > 0 ? "layeringWarnings" : "none";

    private static MetricPopulationScore[] ScoreComponents(IReadOnlyList<TypeMetrics> typeMetrics, ArchitectureTypeScope scope)
    {
        var eligibleTypes = typeMetrics.Where(scope.IsMetricEligible).ToList();
        return
        [
            ScoreMetricPopulation("coupling", eligibleTypes.Where(scope.IsCouplingEligible),
                metric => (double)ScoredCoupling(metric) / CouplingThreshold(metric)),
            ScoreMetricPopulation("complexity", eligibleTypes,
                metric => Math.Min(metric.CyclomaticComplexity / 80d, metric.DecompositionRatio / HighComplexityDensityThreshold)),
            ScoreMetricPopulation("size", eligibleTypes, metric => metric.LinesOfSource / 500d)
        ];
    }

    private static ScoringDecision CreateDecision(MetricPopulationScore[] components, ScoringDecision layeringDecision)
    {
        var metricSteps = components.Select(component => ScoringStep.Component(component.Metric, component.Score,
            [component.Metric switch { "coupling" => "highCoupling", "complexity" => "highCyclomaticComplexity", _ => "largeClass" }],
            new()
            {
                ["eligibleTypeCount"] = component.EligibleTypeCount,
                ["hotspotCount"] = component.HotspotCount,
                ["hotspotRate"] = component.HotspotRate,
                ["worstThresholdRatio"] = component.WorstThresholdRatio,
                ["populationPenalty"] = component.PopulationPenalty,
                ["severityPenalty"] = component.SeverityPenalty,
                ["formula"] = "10 - min(6, 12 * hotspotRate) - min(4, 2 * max(0, worstThresholdRatio - 1))"
            })).ToList();
        metricSteps.Add(ScoringStep.Component("graphLayeringCap", layeringDecision.FinalScore, decision: layeringDecision, kind: "cap"));
        return ScoringDecision.Minimum("dotnet/architecture/population-severity-v1", 1, MidpointRounding.AwayFromZero, [.. metricSteps]);
    }

    private static List<Finding> FindMetricHotspots(
        IReadOnlyList<TypeMetrics> typeMetrics,
        ArchitectureTypeScope scope)
    {
        var hotspots = new List<(Finding Finding, int Cc, int Coupling, int Loc)>();

        foreach (var tm in typeMetrics)
        {
            if (!scope.IsMetricEligible(tm))
                continue;

            if (tm.CyclomaticComplexity >= 80 &&
                tm.DecompositionRatio >= HighComplexityDensityThreshold)
            {
                hotspots.Add((new Finding
                {
                    Category = "highCyclomaticComplexity",
                    Observations = { ["measured"] = tm.CyclomaticComplexity, ["threshold"] = 80, ["density"] = tm.DecompositionRatio, ["densityThreshold"] = HighComplexityDensityThreshold },
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has cyclomatic complexity of {tm.CyclomaticComplexity} " +
                              $"and complexity density {tm.DecompositionRatio:F1} " +
                              $"(thresholds: 80 and {HighComplexityDensityThreshold:F1})."
                }, tm.CyclomaticComplexity, 0, 0));
            }

            var couplingThreshold = CouplingThreshold(tm);
            var scoredCoupling = ScoredCoupling(tm);
            if (IsCouplingHotspot(tm, scope))
            {
                hotspots.Add((new Finding
                {
                    Category = "highCoupling",
                    Observations = { ["measured"] = scoredCoupling, ["threshold"] = couplingThreshold, ["rawCoupling"] = tm.ClassCoupling, ["provenance"] = "architecture.couplingProvenance" },
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has structural coupling of {scoredCoupling} " +
                              $"(raw class coupling: {tm.ClassCoupling}, threshold: {couplingThreshold})."
                }, 0, scoredCoupling, 0));
            }

            if (tm.LinesOfSource >= 500)
            {
                hotspots.Add((new Finding
                {
                    Category = "largeClass",
                    Observations = { ["measured"] = tm.LinesOfSource, ["threshold"] = 500, ["metric"] = "linesOfSource" },
                    Severity = "warning",
                    File = tm.FilePath,
                    Project = tm.Project,
                    Type = tm.Type,
                    Message = $"Type '{tm.Type}' has {tm.LinesOfSource} lines of source (threshold: 500)."
                }, 0, 0, tm.LinesOfSource));
            }
        }

        // Preserve the complete census. Presentation limits belong to the output sample,
        // not to the population used by basis, findings, or scoring.
        return hotspots
            .OrderByDescending(candidate => candidate.Cc)
            .ThenByDescending(candidate => candidate.Coupling)
            .ThenByDescending(candidate => candidate.Loc)
            .ThenBy(candidate => candidate.Finding.Project, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Finding.Type, StringComparer.Ordinal)
            .Select(candidate => candidate.Finding)
            .ToList();
    }

    internal static bool IsCouplingHotspot(TypeMetrics metric, ArchitectureTypeScope scope) =>
        scope.IsCouplingEligible(metric) && ScoredCoupling(metric) >= CouplingThreshold(metric);

    private static MetricPopulationScore ScoreMetricPopulation(
        string metric, IEnumerable<TypeMetrics> population, Func<TypeMetrics, double> thresholdRatio)
    {
        var ratios = population.Select(thresholdRatio).ToArray();
        var count = ratios.Count(ratio => ratio >= 1);
        var rate = ratios.Length == 0 ? 0 : (double)count / ratios.Length;
        var worst = ratios.DefaultIfEmpty(0).Max();
        var populationPenalty = Math.Min(6, 12 * rate);
        var severityPenalty = Math.Min(4, 2 * Math.Max(0, worst - 1));
        return new MetricPopulationScore(metric, ratios.Length, count, rate, worst,
            populationPenalty, severityPenalty, 10 - populationPenalty - severityPenalty);
    }

    private static int CouplingThreshold(TypeMetrics metric)
    {
        if (!metric.StructuralClassCoupling.HasValue)
        {
            return metric.IsWebController
                ? LegacyControllerRawCouplingThreshold
                : LegacyRawCouplingThresholdValue;
        }

        return metric.IsWebController
            ? ControllerStructuralCouplingThreshold
            : StructuralCouplingThreshold;
    }

    internal static int LegacyRawCouplingThreshold(TypeMetrics metric)
    {
        return metric.IsWebController
            ? LegacyControllerRawCouplingThreshold
            : LegacyRawCouplingThresholdValue;
    }

    internal static int ScoredCoupling(TypeMetrics metric)
    {
        return metric.StructuralClassCoupling ?? metric.ClassCoupling;
    }
}
