using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal sealed class CatchPopulation(string? solutionDir)
{
    private readonly Dictionary<(string File, int Start, int Length), Site> sites = [];
    private int observations;
    private sealed record Site(decimal Weight, bool Documented);

    public void Add(CatchObservation observation, IReadOnlyList<CatchIssue> issues)
    {
        observations++;
        var clause = observation.Clause;
        var path = clause.SyntaxTree.FilePath;
        // Unknown physical locations cannot establish cross-compilation identity.
        path = string.IsNullOrWhiteSpace(path) ? $"unknown:{observations}" :
            Path.GetFullPath(path, Path.GetFullPath(solutionDir ?? "."));
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        var key = (path, clause.SpanStart, clause.Span.Length);
        var weight = issues.Select(issue => Weight(issue.Kind)).DefaultIfEmpty(0m).Max();
        var documented = CatchIntentRecognition.HasExplanation(clause);
        if (sites.TryGetValue(key, out var prior))
            sites[key] = new(Math.Max(prior.Weight, weight), prior.Documented && documented);
        else
            sites.Add(key, new(weight, documented));
    }

    private static decimal Weight(CatchIssueKind kind) => kind switch
    {
        CatchIssueKind.Empty or CatchIssueKind.ThrowCaught => 1m,
        CatchIssueKind.BroadDefault => 0.75m,
        CatchIssueKind.UnhandledBroad => 0.5m,
        _ => 0m
    };

    public ScoringDecision Score(List<Finding> findings, int findingObservations)
    {
        var affected = sites.Values.Count(site => site.Weight > 0);
        var weighted = sites.Values.Sum(site => site.Weight);
        var rate = sites.Count == 0 ? 0m : weighted / sites.Count;
        var critical = sites.Values.Any(site => site.Weight == 1m);
        var syncBlock = findings.Any(finding => finding.Category == "syncBlockingCall" && finding.Severity != "info");
        string[] catchCategories = ["emptyCatch", "throwEx", "broadCatchReturnsDefault", "broadCatchWithoutLoggingOrRethrow"];
        var decision = ScoringDecision.Minimum("dotnet/errorHandling/catch-population-v2", 1, MidpointRounding.AwayFromZero,
            ScoringStep.Component("catchPopulation", (double)(10m - 10m * rate), catchCategories),
            ScoringStep.Component("criticalCatchCap", critical ? 9 : 10, ["emptyCatch", "throwEx"], kind: "cap"),
            ScoringStep.Component("syncBlockingCap", syncBlock ? 4 : 10, ["syncBlockingCall"], kind: "cap"));
        var inputs = decision.Inputs;
        inputs["countingUnit"] = "distinctSourceCatch";
        inputs["handlingRecognition"] = "documented-intent-error-output-v2";
        inputs["stderrReportingRecognition"] = "system-console-error-v1";
        inputs["sourceFindings"] = findings.Count;
        inputs["projectFrameworkObservations"] = findingObservations;
        inputs["catchProjectFrameworkObservations"] = observations;
        inputs["catchPopulation"] = sites.Count;
        inputs["affectedCatches"] = affected;
        inputs["documentedCatches"] = sites.Values.Count(site => site.Documented);
        inputs["weightedAffectedCatches"] = weighted;
        inputs["weightedAffectedRate"] = rate;
        inputs["weights"] = new { emptyCatch = 1, throwEx = 1, broadDefault = 0.75, unhandledBroad = 0.5 };
        inputs["formula"] = "10 - 10 * weightedAffectedCatches / catchPopulation; zero population => 10";
        inputs["emptyCatches"] = findings.Count(f => f.Category == "emptyCatch");
        inputs["throwExes"] = findings.Count(f => f.Category == "throwEx");
        inputs["broadDefaults"] = findings.Count(f => f.Category == "broadCatchReturnsDefault");
        inputs["hasSyncBlock"] = syncBlock;
        inputs["syncBlockingClassification"] = "context-classification-v3";
        foreach (var finding in findings.Where(f => f.Category is "missingLoggerForMultipleCatches" or "consoleWriteLine"))
            finding.Observations["scoreDisposition"] = "excludedAdvisoryContext";
        return decision;
    }
}
