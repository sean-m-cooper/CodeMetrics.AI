using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Probes;

public static class MaintainabilityProbe
{
    public static DimensionResult Analyze(IReadOnlyList<TypeMetrics> types)
    {
        // Explicit legacy inputs cannot establish executable-function MI.
        var legacyInputs = types.Count(type => type.ExecutableMetrics == null);
        if (legacyInputs > 0)
        {
            var legacy = LegacyMaintainabilityProbe.Analyze(types);
            legacy.Extra["measurementPolicy"] = "legacy-type-member-mi-v1";
            legacy.Extra["legacyInputTypes"] = legacyInputs;
            if (legacy.ScoringDecision != null) legacy.ScoringDecision.Inputs["legacyInputTypes"] = legacyInputs;
            return legacy;
        }
        return FunctionMaintainabilityProbe.Analyze(types);
    }
}