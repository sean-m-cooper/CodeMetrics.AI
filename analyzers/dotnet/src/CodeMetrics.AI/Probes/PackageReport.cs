using System.Text.Json;

namespace CodeMetrics.AI.Probes;

/// <summary>NuGet's version 1 machine-readable package report. Rows are project/TFM occurrences.</summary>
internal static class PackageReport
{
    internal sealed record PackageRow(string Project, string TargetFramework, string Package,
        string? RequestedVersion, string ResolvedVersion, string? LatestVersion, bool Transitive,
        JsonElement Details)
    {
        public OutdatedPackageUpgrade Upgrade => new(Project, TargetFramework, Package, LatestVersion ?? "");

        public Dictionary<string, object?> Observations() => new()
        {
            ["targetFramework"] = TargetFramework,
            ["requestedVersion"] = RequestedVersion,
            ["resolvedVersion"] = ResolvedVersion,
            ["dependencyKind"] = Transitive ? "transitive" : "direct",
            ["latestVersion"] = LatestVersion,
            ["deprecationReasons"] = Optional(Details, "deprecationReasons"),
            ["alternativePackage"] = Optional(Details, "alternativePackage"),
            ["vulnerabilities"] = Optional(Details, "vulnerabilities")
        };
    }

    internal static bool IsJson(string output) => output.TrimStart().StartsWith('{');

    internal static JsonDocument ParseDocument(string output)
    {
        var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1 ||
            !root.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            throw new JsonException("Expected a version 1 NuGet package report with a projects array.");
        }
        return document;
    }

    internal static IReadOnlyList<PackageRow> Parse(string output)
    {
        using var document = ParseDocument(output);
        var rows = new List<PackageRow>();
        RejectErrors(document.RootElement);
        foreach (var project in document.RootElement.GetProperty("projects").EnumerateArray())
        {
            RejectErrors(project);
            var path = RequiredString(project, "path");
            // NuGet omits frameworks when a project has no applicable package references.
            foreach (var framework in Array(project, "frameworks"))
            {
                RejectErrors(framework);
                var tfm = RequiredString(framework, "framework");
                foreach (var (property, transitive) in new[] { ("topLevelPackages", false), ("transitivePackages", true) })
                {
                    foreach (var package in Array(framework, property))
                    {
                        rows.Add(new PackageRow(path, tfm, RequiredString(package, "id"),
                            String(package, "requestedVersion"), RequiredString(package, "resolvedVersion"),
                            String(package, "latestVersion"), transitive, package.Clone()));
                    }
                }
            }
        }
        return rows.OrderBy(row => row.Project, StringComparer.Ordinal)
            .ThenBy(row => row.TargetFramework, StringComparer.Ordinal)
            .ThenBy(row => row.Package, StringComparer.Ordinal)
            .ThenBy(row => row.Transitive).ToArray();
    }

    private static IEnumerable<JsonElement> Array(JsonElement parent, string property)
    {
        if (!parent.TryGetProperty(property, out var value)) return [];
        if (value.ValueKind != JsonValueKind.Array) throw new JsonException($"Expected array '{property}'.");
        return value.EnumerateArray();
    }

    private static void RejectErrors(JsonElement element)
    {
        foreach (var log in Array(element, "logs"))
            if (string.Equals(String(log, "level"), "error", StringComparison.OrdinalIgnoreCase))
                throw new JsonException("NuGet reported an error in its package report; dependency results are incomplete.");
    }

    private static string RequiredString(JsonElement parent, string property) =>
        String(parent, property) is { Length: > 0 } value ? value : throw new JsonException($"Missing package report field '{property}'.");

    private static string? String(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static object? Optional(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var value) ? value.Clone() : null;
}
