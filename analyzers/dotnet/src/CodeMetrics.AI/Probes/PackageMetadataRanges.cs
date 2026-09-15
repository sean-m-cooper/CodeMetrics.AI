using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CodeMetrics.AI.Probes;

internal sealed class PackageMetadataRanges(HttpClient client, Uri uri, long length, string etag)
{
    internal static string? ReadStrongTag(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("ETag", out var values)) return null;
        var tags = values.ToArray();
        if (tags.Length != 1) return null;
        var tag = tags[0];
        if (EntityTagHeaderValue.TryParse(tag, out var parsed))
            return parsed.IsWeak || parsed.Tag == "*" ? null : tag;
        // Older Azure Blob responses use an unquoted hexadecimal opaque validator.
        return System.Text.RegularExpressions.Regex.IsMatch(tag, @"\A0x[0-9A-Fa-f]{1,32}\z") ? tag : null;
    }

    private const int MaximumManifestBytes = 1024 * 1024;

    internal async Task<PackageFrameworkSet> InspectAsync(CancellationToken cancellationToken)
    {
        var tail = await ReadAsync(Math.Max(0, length - 65557), (int)Math.Min(length, 65557), cancellationToken);
        var location = PackageZipDirectory.Locate(tail, length);
        var directory = await ReadAsync(location.Offset, location.Length, cancellationToken);
        var entries = PackageZipDirectory.Read(directory, location);
        var manifests = entries.Where(e => e.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
            !e.Name.Contains('/') && !e.Name.Contains('\\')).ToArray();
        if (manifests.Length != 1) throw new InvalidDataException("Expected one package manifest.");
        var manifest = await ReadManifestAsync(manifests[0], location.Offset, cancellationToken);
        return PackageAssetInspector.Inspect(entries.Select(e => e.Name), manifest);
    }

    private async Task<string> ReadManifestAsync(PackageZipDirectory.Entry entry, long directoryOffset, CancellationToken cancellationToken)
    {
        if (entry.Compressed > MaximumManifestBytes || entry.Expanded > MaximumManifestBytes ||
            (entry.Flags & 1) != 0 || entry.Method is not (0 or 8) || (long)entry.Offset + 30 > directoryOffset)
            throw new InvalidDataException("Unsupported or oversized package manifest.");
        var header = await ReadAsync(entry.Offset, 30, cancellationToken);
        if (PackageZipDirectory.U32(header, 0) != 0x04034b50 || PackageZipDirectory.U16(header, 6) != entry.Flags ||
            PackageZipDirectory.U16(header, 8) != entry.Method)
            throw new InvalidDataException("Invalid manifest local header.");
        var nameLength = PackageZipDirectory.U16(header, 26);
        var prefixLength = nameLength + PackageZipDirectory.U16(header, 28);
        var dataOffset = (long)entry.Offset + 30;
        if (dataOffset + prefixLength + entry.Compressed > directoryOffset)
            throw new InvalidDataException("Manifest overlaps ZIP directory.");
        var data = await ReadAsync(dataOffset, checked(prefixLength + (int)entry.Compressed), cancellationToken);
        if (Encoding.UTF8.GetString(data, 0, nameLength) != entry.Name)
            throw new InvalidDataException("Manifest name mismatch.");
        using var source = new MemoryStream(data, prefixLength, (int)entry.Compressed);
        using Stream expanded = entry.Method == 8 ? new DeflateStream(source, CompressionMode.Decompress) : source;
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await expanded.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaximumManifestBytes) throw new InvalidDataException("Manifest expansion limit.");
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray();
        if (bytes.Length != entry.Expanded || Crc32(bytes) != entry.Crc)
            throw new InvalidDataException("Manifest integrity mismatch.");
        using var text = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var xml = await text.ReadToEndAsync(cancellationToken);
        using var reader = System.Xml.XmlReader.Create(new StringReader(xml), new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumManifestBytes
        });
        while (reader.Read()) cancellationToken.ThrowIfCancellationRequested();
        return xml;
    }

    private async Task<byte[]> ReadAsync(long start, int count, CancellationToken cancellationToken)
    {
        if (count <= 0 || count > PackageZipDirectory.MaximumDirectoryBytes || start < 0 || start > length - count)
            throw new InvalidDataException("Package metadata range exceeds bounds.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(start, start + count - 1);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent || range?.Unit != "bytes" || range.From != start ||
            range.To != start + count - 1 || range.Length != length || ReadStrongTag(response) != etag ||
            response.Content.Headers.ContentEncoding.Count != 0 ||
            (response.Content.Headers.ContentLength is { } reported && reported != count))
            throw new InvalidDataException("Package server did not honor a consistent metadata range.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        if (await stream.ReadAsync(new byte[1], cancellationToken) != 0)
            throw new InvalidDataException("Package range exceeded requested length.");
        return bytes;
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
        }
        return ~crc;
    }
}
