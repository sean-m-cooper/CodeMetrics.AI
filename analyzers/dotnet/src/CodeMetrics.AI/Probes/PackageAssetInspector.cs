using System.IO.Compression;
using System.Xml.Linq;

namespace CodeMetrics.AI.Probes;

internal sealed record PackageFrameworkSet(
    bool IsFrameworkAgnostic,
    IReadOnlyList<string> Frameworks);

internal static class PackageAssetInspector
{
    public static PackageFrameworkSet Inspect(
        IEnumerable<string> packageAssetPaths,
        string? nuspecXml = null)
    {
        return InspectFiles(
            packageAssetPaths.Select(path => path.Replace('\\', '/')),
            () => string.IsNullOrWhiteSpace(nuspecXml) ? null : XDocument.Parse(nuspecXml));
    }

    public static PackageFrameworkSet InspectArchive(ZipArchive archive)
    {
        return InspectFiles(
            archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')),
            () => LoadNuspec(archive));
    }

    public static PackageFrameworkSet InspectDirectory(string directory)
    {
        return InspectFiles(
            Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/')),
            () => Directory.EnumerateFiles(directory, "*.nuspec", SearchOption.TopDirectoryOnly)
                .Select(XDocument.Load)
                .FirstOrDefault());
    }

    private static PackageFrameworkSet InspectFiles(
        IEnumerable<string> fileNames,
        Func<XDocument?> loadNuspec)
    {
        var files = fileNames.ToList();
        var compileFrameworks = FindAssetFrameworks(files, "ref", "lib");
        compileFrameworks.UnionWith(FindRuntimeLibFrameworks(files));
        if (compileFrameworks.Count > 0)
            return CreateFrameworkSpecificSet(compileFrameworks);

        var nuspec = loadNuspec();
        if (HasFrameworkAgnosticDependencies(nuspec))
            return new PackageFrameworkSet(true, []);

        var dependencyFrameworks = FindDependencyFrameworks(nuspec);
        if (dependencyFrameworks.Count > 0)
            return CreateFrameworkSpecificSet(dependencyFrameworks);

        var buildFrameworks = FindAssetFrameworks(
            files,
            "build",
            "buildTransitive",
            "buildMultiTargeting",
            "tools");
        return buildFrameworks.Count > 0
            ? CreateFrameworkSpecificSet(buildFrameworks)
            : new PackageFrameworkSet(true, []);
    }

    private static PackageFrameworkSet CreateFrameworkSpecificSet(IEnumerable<string> frameworks)
    {
        return new PackageFrameworkSet(
            false,
            frameworks.Order(StringComparer.Ordinal).ToList());
    }

    private static XDocument? LoadNuspec(ZipArchive archive)
    {
        var nuspec = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspec == null)
            return null;

        using var stream = nuspec.Open();
        return XDocument.Load(stream);
    }

    private static HashSet<string> FindAssetFrameworks(
        IEnumerable<string> files,
        params string[] roots)
    {
        var rootSet = roots.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var segments = file.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 3 && rootSet.Contains(segments[0]))
                result.Add(segments[1]);
        }

        return result;
    }

    private static HashSet<string> FindRuntimeLibFrameworks(IEnumerable<string> files)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var segments = file.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 5 &&
                segments[0].Equals("runtimes", StringComparison.OrdinalIgnoreCase) &&
                segments[2].Equals("lib", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(segments[3]);
            }
        }

        return result;
    }

    private static HashSet<string> FindDependencyFrameworks(XDocument? nuspec)
    {
        if (nuspec == null)
            return [];

        return nuspec.Descendants()
            .Where(element => element.Name.LocalName == "group")
            .Select(element => element.Attribute("targetFramework")?.Value?.Trim())
            .Where(framework => !string.IsNullOrWhiteSpace(framework))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasFrameworkAgnosticDependencies(XDocument? nuspec)
    {
        if (nuspec == null)
            return false;

        var dependencies = nuspec.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "dependencies");
        if (dependencies == null)
            return false;

        var groups = dependencies.Elements()
            .Where(element => element.Name.LocalName == "group")
            .ToList();
        return groups.Count == 0 || groups.Any(group =>
            string.IsNullOrWhiteSpace(group.Attribute("targetFramework")?.Value));
    }
}
