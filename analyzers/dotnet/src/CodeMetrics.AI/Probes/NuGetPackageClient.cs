using System.IO.Compression;
using System.Net.Http.Headers;

namespace CodeMetrics.AI.Probes;

internal static class NuGetPackageClient
{
    private const long MaximumPackageBytes = 50 * 1024 * 1024;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<PackageFrameworkSet?> FindFrameworksAsync(
        string package,
        string version,
        IReadOnlyList<Uri> packageBaseAddresses,
        CancellationToken cancellationToken)
    {
        // codemetrics-ignore: awaitedIoInsideLoop -- package sources are ordered fallbacks.
        foreach (var baseAddress in packageBaseAddresses)
        {
            try
            {
                var packageUri = CreatePackageUri(baseAddress, package, version);
                var frameworks = await DownloadAndInspectAsync(packageUri, cancellationToken);
                if (frameworks != null)
                    return frameworks;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                // Try the next configured source. If all fail, compatibility remains unknown.
                continue;
            }
        }

        return null;
    }

    private static async Task<PackageFrameworkSet?> DownloadAndInspectAsync(
        Uri packageUri,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
            packageUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode ||
            response.Content.Headers.ContentLength is > MaximumPackageBytes)
        {
            return null;
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var packageStream = new MemoryStream();
        await CopyWithLimitAsync(responseStream, packageStream, cancellationToken);
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: false);
        return PackageAssetInspector.InspectArchive(archive);
    }

    private static Uri CreatePackageUri(Uri baseAddress, string package, string version)
    {
        return new Uri(
            baseAddress,
            $"{Uri.EscapeDataString(package)}/{Uri.EscapeDataString(version)}/" +
            $"{Uri.EscapeDataString(package)}.{Uri.EscapeDataString(version)}.nupkg");
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaximumPackageBytes)
                throw new InvalidDataException("Package exceeded compatibility-inspection size limit.");

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CodeMetrics.AI", "1.0"));
        return client;
    }
}
