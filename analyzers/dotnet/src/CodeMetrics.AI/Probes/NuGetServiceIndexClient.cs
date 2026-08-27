using System.Net.Http.Headers;
using System.Text.Json;

namespace CodeMetrics.AI.Probes;

internal static class NuGetServiceIndexClient
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static async Task<IReadOnlyList<Uri>> FindPackageBaseAddressesAsync(
        IReadOnlyList<Uri> httpSources,
        CancellationToken cancellationToken)
    {
        var result = new List<Uri>();
        // codemetrics-ignore: awaitedIoInsideLoop -- service indexes are ordered NuGet sources.
        foreach (var source in httpSources)
        {
            try
            {
                var addresses = await ReadPackageBaseAddressesAsync(source, cancellationToken);
                result.AddRange(addresses);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or JsonException or TaskCanceledException)
            {
                // Other configured sources may still provide the package.
                continue;
            }
        }

        return result.Distinct().ToList();
    }

    private static async Task<IReadOnlyList<Uri>> ReadPackageBaseAddressesAsync(
        Uri source,
        CancellationToken cancellationToken)
    {
        using var stream = await HttpClient.GetStreamAsync(source, cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("resources", out var resources))
            return [];

        return resources.EnumerateArray()
            .Select(TryReadPackageBaseAddress)
            .Where(address => address != null)
            .Cast<Uri>()
            .ToList();
    }

    private static Uri? TryReadPackageBaseAddress(JsonElement resource)
    {
        if (!resource.TryGetProperty("@id", out var id) ||
            !resource.TryGetProperty("@type", out var type) ||
            !ContainsResourceType(type, "PackageBaseAddress/3.0.0") ||
            !Uri.TryCreate(id.GetString(), UriKind.Absolute, out var address))
        {
            return null;
        }

        return address.AbsoluteUri.EndsWith('/')
            ? address
            : new Uri(address.AbsoluteUri + "/");
    }

    private static bool ContainsResourceType(JsonElement value, string expected)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Array => value.EnumerateArray().Any(item =>
                item.GetString()?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true),
            _ => false
        };
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CodeMetrics.AI", "1.0"));
        return client;
    }
}
