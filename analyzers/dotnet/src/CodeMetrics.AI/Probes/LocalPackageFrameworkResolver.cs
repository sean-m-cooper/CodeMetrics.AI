using System.IO.Compression;

namespace CodeMetrics.AI.Probes;

internal static class LocalPackageFrameworkResolver
{
    public static PackageFrameworkSet? Find(
        string package,
        string version,
        IReadOnlyList<string> localSources)
    {
        foreach (var source in localSources)
        {
            var nupkg = Path.Combine(source, $"{package}.{version}.nupkg");
            var frameworks = TryInspectArchive(nupkg);
            if (frameworks != null)
                return frameworks;
        }

        var packageDirectories = FindPackageDirectories(package, version, localSources);
        foreach (var directory in packageDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var frameworks = TryInspectDirectory(directory);
            if (frameworks != null)
                return frameworks;
        }

        return null;
    }

    private static IReadOnlyList<string> FindPackageDirectories(
        string package,
        string version,
        IReadOnlyList<string> localSources)
    {
        var packageDirectories = new List<string>();
        AddGlobalPackageDirectory(packageDirectories, package, version);
        AddLocalPackageDirectories(packageDirectories, package, version, localSources);
        return packageDirectories;
    }

    private static void AddGlobalPackageDirectory(
        ICollection<string> packageDirectories,
        string package,
        string version)
    {
        var globalPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(globalPackages))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
                globalPackages = Path.Combine(userProfile, ".nuget", "packages");
        }

        if (!string.IsNullOrWhiteSpace(globalPackages))
        {
            packageDirectories.Add(Path.Combine(
                globalPackages,
                package.ToLowerInvariant(),
                version));
        }
    }

    private static void AddLocalPackageDirectories(
        ICollection<string> packageDirectories,
        string package,
        string version,
        IEnumerable<string> localSources)
    {
        foreach (var source in localSources)
        {
            packageDirectories.Add(Path.Combine(source, package, version));
            packageDirectories.Add(Path.Combine(source, package.ToLowerInvariant(), version));
        }
    }

    private static PackageFrameworkSet? TryInspectArchive(string nupkg)
    {
        if (!File.Exists(nupkg))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(nupkg);
            return PackageAssetInspector.InspectArchive(archive);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return null;
        }
    }

    private static PackageFrameworkSet? TryInspectDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        try
        {
            var nestedNupkg = Directory.EnumerateFiles(
                    directory,
                    "*.nupkg",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            return nestedNupkg != null
                ? TryInspectArchive(nestedNupkg)
                : PackageAssetInspector.InspectDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
