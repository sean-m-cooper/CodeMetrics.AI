using System.Text;
using System.Text.Json;
using CodeMetrics.AI.Output;

namespace CodeMetrics.AI.Rules;

public static class RuleCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly Lazy<RuleCatalogDocument> Catalog = new(() =>
    {
        using var stream = typeof(RuleCatalog).Assembly.GetManifestResourceStream("CodeMetrics.AI.Rules.rules.json")
            ?? throw new InvalidOperationException("The packaged rule catalog is missing.");
        return JsonSerializer.Deserialize<RuleCatalogDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The packaged rule catalog is invalid.");
    });

    public static RuleCatalogDocument Document => Catalog.Value;

    public static RuleDefinition? Find(string identity) => Document.Rules.FirstOrDefault(rule =>
        rule.Code.Equals(identity, StringComparison.OrdinalIgnoreCase) ||
        rule.RuleId.Equals(identity, StringComparison.OrdinalIgnoreCase));

    public static string Render(string format, string? code = null)
    {
        var rules = code == null ? Document.Rules : [Find(code) ?? throw new ArgumentException($"Unknown rule code: {code}")];
        if (format == "json")
            return JsonSerializer.Serialize(new
            {
                Document.SchemaVersion,
                Document.CatalogVersion,
                tool = new ToolInfo(),
                Document.Ecosystem,
                rules
            }, JsonOptions) + Environment.NewLine;

        if (format == "text")
        {
            var overview = string.Join(Environment.NewLine, rules.Select(rule =>
                $"{rule.Code}  {rule.Title} [{rule.Dimension}; {rule.Kind}; annotations: {(rule.Annotation.Supported ? "supported" : "unsupported")}]")) + Environment.NewLine;
            return code == null ? overview : overview + rules[0].RuleId + Environment.NewLine +
                rules[0].Description + Environment.NewLine + rules[0].Annotation.ScoringEffect + Environment.NewLine +
                (rules[0].Annotation.Supported ? $"Scopes: {string.Join(", ", rules[0].Annotation.Scopes)}. Rationale required.{Environment.NewLine}{rules[0].Annotation.Example}{Environment.NewLine}" : "");
        }

        if (format != "markdown")
            throw new ArgumentException("Supported rule formats: text, json, markdown.");

        var markdown = new StringBuilder("# CodeMetrics.AI rule catalog\n\n");
        markdown.AppendLine("Generated from the catalog shipped in the analyzer package. Codes are permanent aliases; existing rule IDs remain stable. Number ranges identify dimensions (1000 architecture through 9000 maintainability).");
        markdown.AppendLine("\nUse `code-metrics rules --format json` for the installed package version and machine-readable definitions, or `code-metrics rules --code CMAI5001` to look up a code. Metric entries describe aggregate components; they are not emitted per-site diagnostics.");
        markdown.AppendLine("\nOnly entries marked as supporting annotations accept CMAI comments. Place a directive immediately before the indicated syntax or its containing member. CMAI directives require a reason after ` -- ` or `—`; its content is accepted without evaluating the developer's business decision. Existing category directives retain their legacy behavior. Each code addresses only its own rule; one source site can participate in multiple rules. A declaration in evidence is not proof it matched an occurrence.");
        foreach (var rule in rules)
        {
            markdown.AppendLine($"\n<a id=\"{rule.Code.ToLowerInvariant()}\"></a>\n\n## {rule.Code}: {rule.Title}\n");
            markdown.AppendLine($"- Identity: `{rule.RuleId}`\n- Dimension: `{rule.Dimension}`\n- Kind: `{rule.Kind}`\n");
            markdown.AppendLine(rule.Description);
            markdown.AppendLine($"\n{rule.Annotation.ScoringEffect}");
            if (rule.Annotation.Supported)
                markdown.AppendLine($"\nSupported scopes: {string.Join(", ", rule.Annotation.Scopes)}. Rationale required.\n\n```csharp\n{rule.Annotation.Example}\n```");
        }
        return markdown.ToString().Replace("\r\n", "\n");
    }
}

public sealed record RuleCatalogDocument(int SchemaVersion, int CatalogVersion, string Ecosystem, RuleDefinition[] Rules);
public sealed record RuleDefinition(string Code, string RuleId, string Dimension, string Category, string Kind,
    string Title, string Description, string Documentation, RuleAnnotation Annotation);
public sealed record RuleAnnotation(bool Supported, string[] Scopes, bool RationaleRequired, string? Example, string ScoringEffect);
