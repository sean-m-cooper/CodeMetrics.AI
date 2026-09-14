using System.Security.Cryptography;
using System.Text;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Output;

internal static class EvidenceEnricher
{
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string RepositoryRoot(string analysisRoot)
    {
        for (var directory = new DirectoryInfo(analysisRoot); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
        return analysisRoot;
    }

    public static void Enrich(Dictionary<string, object> dimensions, SolutionAnalysisContext context, string root)
    {
        var repositoryRoot = RepositoryRoot(root);
        var trees = context.AllProjectCompilations.SelectMany(project => project.Compilation.SyntaxTrees)
            .Where(tree => !string.IsNullOrEmpty(tree.FilePath))
            .GroupBy(tree => Path.GetFullPath(tree.FilePath)).ToDictionary(group => group.Key, group => group.First());
        var sourceResolver = new FindingSourceResolver(trees, root, repositoryRoot);
        foreach (var (dimension, value) in dimensions)
        {
            var result = (DimensionResult)value;
            AttachRuleCatalog(result, dimension);
            var occurrences = new Dictionary<string, int>();
            foreach (var finding in result.Findings.OrderBy(finding => finding.File, StringComparer.Ordinal).ThenBy(finding => finding.Line))
            {
                finding.RuleId = $"dotnet/{dimension}/{finding.Category}";
                if (RuleCatalog.Find(finding.RuleId) is { } rule)
                    finding.Observations["diagnosticCode"] = rule.Code;
                NormalizePackageProject(finding, dimension, repositoryRoot);
                var anchor = sourceResolver.Resolve(finding);
                AssignConfidence(finding);
                AssignIdentity(finding, dimension, anchor, occurrences);
            }
            result.Findings.Sort((left, right) => StringComparer.Ordinal.Compare(left.Fingerprint, right.Fingerprint));
            AttachScoring(result, dimension);
        }
    }

    private static void AttachRuleCatalog(DimensionResult result, string dimension)
    {
        result.Extra["ruleCatalog"] = new
        {
            version = RuleCatalog.Document.CatalogVersion,
            toolVersion = new ToolInfo().Version,
            command = "code-metrics rules --format json",
            rules = RuleCatalog.Document.Rules.Where(rule => rule.Dimension == dimension)
                .Select(rule => new { rule.Code, rule.RuleId, rule.Kind }).ToArray()
        };
    }

    private static void NormalizePackageProject(Finding finding, string dimension, string repositoryRoot)
    {
        if (dimension == "dependencyManagement" && finding.Package != null && finding.Project is { } packageProject && Path.IsPathFullyQualified(packageProject))
            finding.Project = Path.GetRelativePath(repositoryRoot, packageProject).Replace('\\', '/');
    }

    private static void AssignConfidence(Finding finding)
    {
        if (finding.Category is "emptyCatch" or "throwEx" or "syncOverAsync" or "syncBlockingCall" or "highCoupling" or "largeClass" or "highCyclomaticComplexity" or "projectCycle")
            finding.Confidence = "high";
        if (finding.Category is "uncoveredProject" or "missingAuthorization" or "missingLoggerForMultipleCatches")
            finding.Confidence = "low";
        finding.Observations.TryAdd("confidenceBasis", finding.Confidence == "high" ? "Direct syntax, semantic, or metric observation" : "Heuristic; review surrounding context");
    }

    private static void AssignIdentity(Finding finding, string dimension, string anchor, Dictionary<string, int> occurrences)
    {
        var identity = string.Join("|", finding.RuleId, finding.File, finding.Project, finding.Type, finding.Member, finding.Package, anchor);
        if (dimension == "dependencyManagement" && finding.Package != null && finding.Observations.TryGetValue("targetFramework", out var framework))
            identity += "|" + framework;
        occurrences.TryGetValue(identity, out var occurrence);
        occurrences[identity] = occurrence + 1;
        finding.Fingerprint = Hash(identity + "|" + occurrence);
    }

    private static void AttachScoring(DimensionResult result, string dimension)
    {
        if (result.Status == "scored" && result.ScoringDecision is { } decision)
        {
            if (decision.FinalScore != result.Score)
                throw new InvalidOperationException($"Scoring decision differs from the {dimension} score.");
            decision.AttachFindings(result.Findings);
            result.Extra["scoringDecision"] = decision;
        }
        if (result.Status == "scored")
            result.Extra["scoring"] = new
            {
                algorithm = "dimension-policy",
                finalScore = result.Score,
                aggregateScoreLoss = 10 - result.Score,
                basis = result.Basis,
                contributionMode = "aggregate; findings are not independent deductions",
                rules = result.Findings.GroupBy(finding => finding.RuleId).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new { ruleId = group.Key, count = group.Count() }).ToArray()
            };
    }

    public static List<object> FindSuppressionDeclarations(SolutionAnalysisContext context, string root)
    {
        var results = new List<object>();
        var repositoryRoot = RepositoryRoot(root);
        foreach (var tree in context.AnalyzedProjectCompilations.SelectMany(project => SourceFileFilter.AnalyzableTrees(project.Compilation, root))
            .DistinctBy(tree => tree.FilePath).OrderBy(tree => tree.FilePath, StringComparer.Ordinal))
        {
            foreach (var trivia in tree.GetRoot().DescendantTrivia())
            {
                var parts = ParseSuppressionComment(trivia);
                if (parts == null) continue;
                results.Add(new
                {
                    file = Path.GetRelativePath(repositoryRoot, tree.FilePath).Replace('\\', '/'),
                    line = tree.GetLineSpan(trivia.Span).StartLinePosition.Line + 1,
                    categories = parts[0].Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries),
                    reason = parts.Length > 1 ? parts[1] : null,
                    status = "declared"
                });
            }
        }
        return results;
    }

    private static string[]? ParseSuppressionComment(SyntaxTrivia trivia)
    {
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            return null;
        var text = trivia.ToString();
        var marker = text.IndexOf("codemetrics-ignore:", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 && !text.Contains("amp-metrics: sync-required", StringComparison.OrdinalIgnoreCase))
            return null;
        var declaration = marker < 0 ? "sync-required" : text[(marker + "codemetrics-ignore:".Length)..].Trim().TrimEnd('*', '/');
        return declaration.Split(["—", " -- "], 2, StringSplitOptions.TrimEntries);
    }
}
