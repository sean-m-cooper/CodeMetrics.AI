using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using CodeMetrics.AI.Probes;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Probes;

public sealed class PackageMetadataRangeTests
{
    [Theory]
    [InlineData(true, CompressionLevel.Optimal)]
    [InlineData(false, CompressionLevel.Optimal)]
    [InlineData(false, CompressionLevel.NoCompression)]
    public async Task LargePackage_UsesBoundedMetadataAndManifest(bool compileAsset, CompressionLevel compression)
    {
        var handler = new SparsePackage(compileAsset, compression);
        using var client = new HttpClient(handler);
        var result = await Find(client);
        result.Should().NotBeNull();
        result!.Frameworks.Should().Contain("net10.0");
        handler.Transferred.Should().BeLessThan(100_000);
        handler.RangeRequests.Should().Be(4);
    }

    [Theory]
    [InlineData("noValidator")]
    [InlineData("weakValidator")]
    [InlineData("ignoredRange")]
    [InlineData("wrongRange")]
    [InlineData("changedValidator")]
    [InlineData("truncated")]
    [InlineData("encoded")]
    [InlineData("zip64")]
    [InlineData("directorySize")]
    [InlineData("manifestSize")]
    [InlineData("corruptManifest")]
    [InlineData("malformedXml")]
    [InlineData("dtd")]
    public async Task UntrustedOrUnsupportedMetadata_RemainsUnavailable(string fault)
    {
        var handler = new SparsePackage(false, CompressionLevel.Optimal, fault);
        using var client = new HttpClient(handler);
        (await Find(client)).Should().BeNull();
        handler.Transferred.Should().BeLessThan(100_000);
    }

    [Fact]
    public async Task LegacyAzureTag_PreservesConditionalRangeIdentity()
    {
        using var client = new HttpClient(new SparsePackage(false, CompressionLevel.Optimal, "legacyEtag"));
        (await Find(client)).Should().NotBeNull();
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(new SparsePackage(false, CompressionLevel.Optimal, cancel: cancellation));
        var action = () => NuGetPackageClient.FindFrameworksAsync("pkg", "1.0.0", [new("https://feed.invalid/")], cancellation.Token, client: client);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static Task<PackageFrameworkSet?> Find(HttpClient client) => NuGetPackageClient.FindFrameworksAsync(
        "pkg", "1.0.0", [new("https://feed.invalid/")], TestContext.Current.CancellationToken, client: client);

    private sealed class SparsePackage : HttpMessageHandler
    {
        private readonly byte[] bytes;
        private readonly int originalDirectory;
        private readonly long directory = 100 * 1024 * 1024;
        private readonly long length;
        private readonly string? fault;
        private readonly CancellationTokenSource? cancel;
        public long Transferred { get; private set; }
        public int RangeRequests { get; private set; }

        public SparsePackage(bool compileAsset, CompressionLevel compression, string? fault = null, CancellationTokenSource? cancel = null)
        {
            this.fault = fault;
            this.cancel = cancel;
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                if (compileAsset) zip.CreateEntry("lib/net10.0/library.dll");
                using var writer = new StreamWriter(zip.CreateEntry("pkg.nuspec", compression).Open());
                writer.Write(fault == "malformedXml" ? "<broken" : fault == "dtd" ? "<!DOCTYPE package [<!ENTITY x \"expanded\">]><package>&x;</package>" : "<package><metadata><dependencies><group targetFramework=\"net10.0\" /></dependencies></metadata></package>");
            }
            bytes = memory.ToArray();
            originalDirectory = (int)PackageZipDirectory.U32(bytes, bytes.Length - 6);
            length = directory + bytes.Length - originalDirectory;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 6), (uint)directory);
            if (fault == "zip64") BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 12), ushort.MaxValue);
            if (fault == "directorySize") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 10), 9 * 1024 * 1024);
            if (fault == "manifestSize") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(originalDirectory + 24), 2 * 1024 * 1024);
            if (fault == "corruptManifest") bytes[originalDirectory + 16] ^= 1;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var range = request.Headers.Range?.Ranges.Single();
            var response = new HttpResponseMessage(range == null || fault == "ignoredRange" ? HttpStatusCode.OK : HttpStatusCode.PartialContent);
            response.Headers.ETag = new EntityTagHeaderValue("\"package\"", fault == "weakValidator");
            if (fault == "legacyEtag")
            {
                response.Headers.ETag = null;
                response.Headers.TryAddWithoutValidation("ETag", "0x8DEF7C048963F67");
            }
            if (fault == "noValidator") response.Headers.ETag = null;
            if (range == null)
            {
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentLength = length;
                return Task.FromResult(response);
            }
            RangeRequests++;
            request.Headers.GetValues("If-Match").Single().Should().Be(fault == "legacyEtag" ? "0x8DEF7C048963F67" : "\"package\"");
            cancel?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            var start = range.From!.Value;
            var count = checked((int)(range.To!.Value - start + 1));
            var data = new byte[fault == "truncated" ? count - 1 : count];
            for (var i = 0; i < data.Length; i++)
            {
                var index = start + i < originalDirectory ? start + i : start + i >= directory ? originalDirectory + start + i - directory : -1;
                if (index >= 0 && index < bytes.Length) data[i] = bytes[index];
            }
            Transferred += data.Length;
            response.Content = new ByteArrayContent(data);
            response.Content.Headers.ContentLength = count;
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(fault == "wrongRange" ? start + 1 : start, range.To.Value, length);
            if (fault == "changedValidator") response.Headers.ETag = new EntityTagHeaderValue("\"changed\"");
            if (fault == "encoded") response.Content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(response);
        }
    }
}
