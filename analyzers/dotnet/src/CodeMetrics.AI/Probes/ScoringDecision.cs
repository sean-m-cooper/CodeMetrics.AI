namespace CodeMetrics.AI.Probes;

/// <summary>The executed scoring path, independent of presentation and per-finding deductions.</summary>
public sealed class ScoringDecision
{
    public int Version => 1;
    public required string Policy { get; init; }
    public required string Operation { get; init; }
    public required double FinalScore { get; init; }
    public Dictionary<string, object?> Inputs { get; init; } = [];
    public List<ScoringStep> Steps { get; init; } = [];
    public List<FindingScoreEffect> FindingEffects { get; set; } = [];

    public static ScoringDecision FirstMatch(string policy, Dictionary<string, object?> inputs, params ScoringStep[] rules)
    {
        var selected = Array.FindIndex(rules, rule => rule.Matched == true);
        if (selected < 0) throw new ArgumentException("A scoring ladder must include a matching default.", nameof(rules));
        for (var index = 0; index < rules.Length; index++)
            rules[index].Disposition = index == selected ? "selected" : rules[index].Matched == true ? "shadowed" : "notMatched";
        return new ScoringDecision { Policy = policy, Operation = "firstMatch", Inputs = inputs, Steps = [.. rules], FinalScore = rules[selected].Score };
    }

    public static ScoringDecision Minimum(string policy, int decimals, MidpointRounding rounding, params ScoringStep[] components)
    {
        var minimum = components.Min(component => component.Score);
        foreach (var component in components)
            component.Disposition = component.Score == minimum ? "selected" : "notLimiting";
        return new ScoringDecision
        {
            Policy = policy,
            Operation = "minimum",
            Steps = [.. components],
            Inputs = { ["roundingDecimals"] = decimals, ["roundingMode"] = rounding.ToString(), ["unroundedScore"] = minimum },
            FinalScore = Math.Round(minimum, decimals, rounding)
        };
    }

    public static ScoringDecision Mean(string policy, params ScoringStep[] components) => new()
    {
        Policy = policy,
        Operation = "mean",
        Steps = [.. components],
        Inputs = { ["roundingDecimals"] = 1, ["roundingMode"] = "ToEven", ["unroundedScore"] = components.Average(component => component.Score) },
        FinalScore = Math.Round(components.Average(component => component.Score), 1)
    };

    public static ScoringDecision Deductions(string policy, Dictionary<string, object?> inputs, params ScoringStep[] deductions)
    {
        foreach (var deduction in deductions)
            deduction.Disposition = deduction.Matched == true ? "applied" : "notMatched";
        var raw = 10 - deductions.Where(step => step.Matched == true).Sum(step => step.Score);
        inputs["startingScore"] = 10;
        inputs["unclampedScore"] = raw;
        inputs["minimumScore"] = 0;
        inputs["maximumScore"] = 10;
        return new ScoringDecision { Policy = policy, Operation = "deductions", Inputs = inputs, Steps = [.. deductions], FinalScore = Math.Clamp(raw, 0, 10) };
    }

    public static ScoringStep Threshold(string id, double measured, double[] thresholds, bool descending = false)
    {
        var rules = thresholds.Select((threshold, index) => ScoringStep.Rule(id + "/band" + index,
            $"measured {(descending ? ">=" : "<=")} threshold[{index}]",
            descending ? measured >= threshold : measured <= threshold, 10 - index * 2)).ToList();
        rules.Add(ScoringStep.Rule(id + "/otherwise", "otherwise", true, 0));
        var decision = FirstMatch(id, new() { ["measured"] = measured, ["thresholds"] = thresholds }, [.. rules]);
        return ScoringStep.Component(id, decision.FinalScore, decision: decision);
    }

    public void AttachFindings(IEnumerable<Finding> findings)
    {
        var paths = Flatten(Steps, true).ToList();
        FindingEffects = findings.Select(finding =>
        {
            var matched = paths.Where(path => path.Step.FindingCategories.Contains(finding.Category)).ToList();
            var excluded = finding.Observations.TryGetValue("scoreDisposition", out var disposition) &&
                disposition?.ToString()?.StartsWith("excluded", StringComparison.Ordinal) == true;
            return new FindingScoreEffect(finding.Fingerprint!, finding.Category,
                excluded ? "excluded" : matched.Any(path => path.Active) ? "policyInput" : "noAdditionalReduction",
                matched.Select(path => path.Step.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }).OrderBy(effect => effect.Fingerprint, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<(ScoringStep Step, bool Active)> Flatten(IEnumerable<ScoringStep> steps, bool parentActive)
    {
        foreach (var step in steps)
        {
            var active = parentActive && step.Disposition is "selected" or "contributing" or "applied";
            yield return (step, active);
            if (step.Decision != null)
                foreach (var nested in Flatten(step.Decision.Steps, active)) yield return nested;
        }
    }
}

public sealed class ScoringStep
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required double Score { get; init; }
    public string? Condition { get; init; }
    public bool? Matched { get; init; }
    public string Disposition { get; set; } = "contributing";
    public Dictionary<string, object?> Inputs { get; init; } = [];
    public string[] FindingCategories { get; init; } = [];
    public ScoringDecision? Decision { get; init; }

    public static ScoringStep Rule(string id, string condition, bool matched, double score, params string[] categories) =>
        new() { Id = id, Kind = "rule", Condition = condition, Matched = matched, Score = score, FindingCategories = categories };

    public static ScoringStep Component(string id, double score, string[]? categories = null,
        Dictionary<string, object?>? inputs = null, ScoringDecision? decision = null, string kind = "component") =>
        new() { Id = id, Kind = kind, Score = score, FindingCategories = categories ?? [], Inputs = inputs ?? [], Decision = decision };
}

public sealed record FindingScoreEffect(string Fingerprint, string Category, string Effect, string[] StepIds);
