using System.Xml.Linq;

namespace CodeMetrics.AI.Probes;

// One report's matched source sites, with duplicate line hits merged across classes.
internal sealed class CoverageSourceAccumulator
{
    private readonly string root;
    private readonly HashSet<string> known;
    private readonly string[] sources;
    private readonly HashSet<string> matched;
    private readonly HashSet<string> unmatched = new(StringComparer.Ordinal);
    private readonly Dictionary<(string File, int Line), bool> lines = new();

    public CoverageSourceAccumulator(XDocument document, IEnumerable<string> productionFiles, string root)
    {
        this.root = root;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        known = productionFiles.Select(file => Path.GetFullPath(file, root)).ToHashSet(comparer);
        sources = document.Descendants("source").Select(source => source.Value).Append(root).ToArray();
        matched = new(comparer);
    }

    public void Add(XElement item)
    {
        var filename = item.Attribute("filename")?.Value;
        if (string.IsNullOrWhiteSpace(filename)) return;
        var resolved = Resolve(filename);
        if (resolved == null)
        {
            unmatched.Add(filename);
            return;
        }
        matched.Add(Path.GetRelativePath(root, resolved).Replace('\\', '/'));
        AddLines(item, resolved);
    }

    private string? Resolve(string filename)
    {
        var normalized = filename.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var candidates = sources.Select(source => Path.GetFullPath(normalized, Path.GetFullPath(source, root)));
        return candidates.FirstOrDefault(known.Contains);
    }

    private void AddLines(XElement item, string resolved)
    {
        foreach (var line in item.Element("lines")?.Elements("line") ?? [])
        {
            if (!TryReadLine(line, out var number, out var hits)) continue;
            var key = (resolved, number);
            lines[key] = lines.GetValueOrDefault(key) || hits > 0;
        }
    }

    private static bool TryReadLine(XElement line, out int number, out int hits)
    {
        hits = 0;
        return int.TryParse(line.Attribute("number")?.Value, out number) && number >= 1 &&
               int.TryParse(line.Attribute("hits")?.Value, out hits) && hits >= 0;
    }

    public CoverageReport ToReport(string path, string hash, double? branchRate)
    {
        // Aggregate branch rates cannot be attributed to the matched subset.
        return new(lines.Count == 0 ? "unmatched" : "matched", path, hash,
            lines.Count == 0 ? null : (double)lines.Values.Count(hit => hit) / lines.Count,
            unmatched.Count == 0 ? branchRate : null,
            matched.Order(StringComparer.Ordinal).ToArray(), unmatched.Order(StringComparer.Ordinal).ToArray());
    }
}
