using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace CodeMetrics.AI.Probes;

public sealed record CoverageReport(
    string Status, string Path, string? Sha256, double? LineRate, double? BranchRate,
    IReadOnlyList<string> MatchedFiles, IReadOnlyList<string> UnmatchedFiles, string? Error = null)
{
    public static CoverageReport Read(string path, IEnumerable<string> productionFiles, string root)
    {
        string? hash = null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 50_000_000 });
            var document = XDocument.Load(reader);
            if (document.Root?.Name.LocalName != "coverage") throw new XmlException("Expected a Cobertura coverage root.");
            var classes = document.Descendants("class").ToList();
            if (classes.Count == 0)
                return new("aggregateUnverified", path, hash, Rate(document.Root.Attribute("line-rate")?.Value),
                    Rate(document.Root.Attribute("branch-rate")?.Value), [], []);

            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var known = productionFiles.Select(file => System.IO.Path.GetFullPath(file, root)).ToHashSet(comparer);
            var sources = document.Descendants("source").Select(source => source.Value).Append(root).ToArray();
            var matched = new HashSet<string>(comparer);
            var unmatched = new HashSet<string>(StringComparer.Ordinal);
            var lines = new Dictionary<(string File, int Line), bool>();
            foreach (var item in classes)
            {
                var filename = item.Attribute("filename")?.Value;
                if (string.IsNullOrWhiteSpace(filename)) continue;
                var normalized = filename.Replace('\\', System.IO.Path.DirectorySeparatorChar).Replace('/', System.IO.Path.DirectorySeparatorChar);
                var candidates = sources.Select(source => System.IO.Path.GetFullPath(normalized, System.IO.Path.GetFullPath(source, root)));
                var resolved = candidates.FirstOrDefault(known.Contains);
                if (resolved == null) { unmatched.Add(filename); continue; }
                matched.Add(System.IO.Path.GetRelativePath(root, resolved).Replace('\\', '/'));
                foreach (var line in item.Element("lines")?.Elements("line") ?? [])
                {
                    if (!int.TryParse(line.Attribute("number")?.Value, out var number) || number < 1 ||
                        !int.TryParse(line.Attribute("hits")?.Value, out var hits) || hits < 0) continue;
                    var key = (resolved, number);
                    lines[key] = lines.GetValueOrDefault(key) || hits > 0;
                }
            }
            // Aggregate branch rates cannot be attributed to the matched subset.
            return new(lines.Count == 0 ? "unmatched" : "matched", path, hash,
                lines.Count == 0 ? null : (double)lines.Values.Count(hit => hit) / lines.Count,
                unmatched.Count == 0 ? Rate(document.Root.Attribute("branch-rate")?.Value) : null,
                matched.Order(StringComparer.Ordinal).ToArray(), unmatched.Order(StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        {
            return new("invalid", path, hash, null, null, [], [], ex.Message);
        }
    }

    private static double? Rate(string? value) => double.TryParse(value, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var rate) && rate is >= 0 and <= 1 ? rate : null;
}
