using System.Buffers.Binary;
using System.Text;

namespace CodeMetrics.AI.Probes;

// Classic ZIP only. ZIP64, split archives and oversized directories stay unavailable.
internal static class PackageZipDirectory
{
    internal const int MaximumDirectoryBytes = 8 * 1024 * 1024;
    internal sealed record Location(long Offset, int Length, int Entries);
    internal sealed record Entry(string Name, uint Offset, uint Compressed, uint Expanded, ushort Method, ushort Flags, uint Crc);

    internal static Location Locate(byte[] tail, long totalLength)
    {
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (U32(tail, i) != 0x06054b50 || i + 22 + U16(tail, i + 20) != tail.Length) continue;
            var entries = U16(tail, i + 10);
            var length = U32(tail, i + 12);
            var offset = U32(tail, i + 16);
            if (U16(tail, i + 4) != 0 || U16(tail, i + 6) != 0 || U16(tail, i + 8) != entries ||
                entries is 0 or ushort.MaxValue || length > MaximumDirectoryBytes || offset == uint.MaxValue ||
                (long)offset + length != totalLength - tail.Length + i)
                throw new InvalidDataException("Unsupported package ZIP directory.");
            return new(offset, checked((int)length), entries);
        }
        throw new InvalidDataException("Package ZIP end record missing.");
    }

    internal static IReadOnlyList<Entry> Read(byte[] bytes, Location location)
    {
        var entries = new List<Entry>();
        var position = 0;
        while (position < bytes.Length)
        {
            if (bytes.Length - position < 46 || U32(bytes, position) != 0x02014b50)
                throw new InvalidDataException("Invalid ZIP directory entry.");
            var nameLength = U16(bytes, position + 28);
            var size = 46 + nameLength + U16(bytes, position + 30) + U16(bytes, position + 32);
            if (size > bytes.Length - position || U16(bytes, position + 34) != 0 || entries.Count >= location.Entries)
                throw new InvalidDataException("Invalid ZIP directory bounds.");
            var entry = new Entry(Encoding.UTF8.GetString(bytes, position + 46, nameLength), U32(bytes, position + 42),
                U32(bytes, position + 20), U32(bytes, position + 24), U16(bytes, position + 10), U16(bytes, position + 8), U32(bytes, position + 16));
            if (entry.Offset >= location.Offset || entry.Compressed == uint.MaxValue || entry.Expanded == uint.MaxValue)
                throw new InvalidDataException("Unsupported ZIP64 entry or offset.");
            entries.Add(entry);
            position += size;
        }
        if (entries.Count != location.Entries) throw new InvalidDataException("Incomplete ZIP directory.");
        return entries;
    }

    internal static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    internal static uint U32(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
}
