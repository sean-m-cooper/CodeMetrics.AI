namespace CodeMetrics.AI.Probes;

internal sealed class NuGetPackageFrameworkResolver(
    IReadOnlyList<Uri> packageBaseAddresses,
    IReadOnlyList<string> localSources)
{
    public static async Task<NuGetPackageFrameworkResolver> CreateAsync(
        string commandOutput,
        CancellationToken cancellationToken)
    {
        var packageSources = PackageSourceParser.Parse(commandOutput);
        var packageBaseAddresses = await NuGetServiceIndexClient.FindPackageBaseAddressesAsync(
            packageSources.HttpSources,
            cancellationToken);
        return new NuGetPackageFrameworkResolver(
            packageBaseAddresses,
            packageSources.LocalSources);
    }

    public async Task<PackageFrameworkSet?> FindAsync(
        string package,
        string version,
        CancellationToken cancellationToken)
    {
        var normalizedPackage = package.ToLowerInvariant();
        var normalizedVersion = NormalizeVersion(version);
        var local = LocalPackageFrameworkResolver.Find(
            normalizedPackage,
            normalizedVersion,
            localSources);
        return local ?? await NuGetPackageClient.FindFrameworksAsync(
            normalizedPackage,
            normalizedVersion,
            packageBaseAddresses,
            cancellationToken);
    }

    private static string NormalizeVersion(string version)
    {
        var metadataIndex = version.IndexOf('+');
        return (metadataIndex >= 0 ? version[..metadataIndex] : version).ToLowerInvariant();
    }
}

internal sealed record PackageSources(
    IReadOnlyList<Uri> HttpSources,
    IReadOnlyList<string> LocalSources);

internal static class PackageSourceParser
{
    public static PackageSources Parse(string commandOutput)
    {
        var sourceLines = SplitLines(commandOutput)
            .Select(line => line.Trim())
            .ToList();
        var httpSources = sourceLines
            .Select(line => Uri.TryCreate(line, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri?.Scheme is "http" or "https")
            .Cast<Uri>()
            .Distinct()
            .ToList();
        var localSources = sourceLines
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PackageSources(httpSources, localSources);
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return string.IsNullOrEmpty(text)
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }
}
