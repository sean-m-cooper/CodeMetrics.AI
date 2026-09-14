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
            var document = ParseDocument(bytes);
            var classes = document.Descendants("class").ToList();
            if (classes.Count == 0)
                return new("aggregateUnverified", path, hash, Rate(document.Root!.Attribute("line-rate")?.Value),
                    Rate(document.Root.Attribute("branch-rate")?.Value), [], []);

            var coverage = new CoverageSourceAccumulator(document, productionFiles, root);
            foreach (var item in classes)
                coverage.Add(item);
            return coverage.ToReport(path, hash, Rate(document.Root!.Attribute("branch-rate")?.Value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        {
            return new("invalid", path, hash, null, null, [], [], ex.Message);
        }
    }

    private static XDocument ParseDocument(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 50_000_000 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "coverage")
            throw new XmlException("Expected a Cobertura coverage root.");
        return document;
    }

    private static double? Rate(string? value) => double.TryParse(value, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var rate) && rate is >= 0 and <= 1 ? rate : null;
}
