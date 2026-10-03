using System.Buffers.Binary;
using System.Text;

namespace HKCLTool;

internal sealed class BotwSarcArchive
{
    private readonly byte[] _data;
    private string? _indexedPath;

    private BotwSarcArchive(byte[] data, bool littleEndian, IReadOnlyList<BotwSarcEntry> entries)
    {
        _data = data;
        LittleEndian = littleEndian;
        Entries = entries;
    }

    public bool LittleEndian { get; }
    public IReadOnlyList<BotwSarcEntry> Entries { get; }

    public static BotwSarcArchive Open(string path) => Parse(File.ReadAllBytes(path));

    public static BotwSarcArchive OpenIndexed(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[16];
        stream.ReadExactly(header);
        if (header.StartsWith("Yaz0"u8)) return Open(path);
        if (!header.StartsWith("SARC"u8)) throw new InvalidDataException("The file is not a SARC archive.");
        var dataOffset = header[6] == 0xff && header[7] == 0xfe
            ? BinaryPrimitives.ReadUInt32LittleEndian(header[12..]) : BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
        if (dataOffset < 32 || dataOffset > stream.Length || dataOffset > 16 * 1024 * 1024)
            throw new InvalidDataException("The SARC index size is invalid.");
        var index = new byte[dataOffset];
        header.CopyTo(index); stream.ReadExactly(index.AsSpan(header.Length));
        var archive = ParseIndex(index, stream.Length);
        archive._indexedPath = Path.GetFullPath(path);
        return archive;
    }

    public static BotwSarcArchive Parse(ReadOnlySpan<byte> source)
    {
        var data = source.StartsWith("Yaz0"u8) ? BotwYaz0.Decode(source) : source.ToArray();
        return ParseIndex(data, data.Length);
    }

    private static BotwSarcArchive ParseIndex(byte[] data, long fileLength)
    {
        if (data.Length < 0x20 || !data.AsSpan(0, 4).SequenceEqual("SARC"u8))
            throw new InvalidDataException("The file is not a SARC archive.");

        var littleEndian = data[6] == 0xff && data[7] == 0xfe;
        if (!littleEndian && !(data[6] == 0xfe && data[7] == 0xff))
            throw new InvalidDataException("The SARC byte-order marker is invalid.");

        ushort U16(int offset) => littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
        uint U32(int offset) => littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

        var dataOffset = checked((int)U32(0x0c));
        var sfatOffset = U16(4);
        if (sfatOffset + 12 > data.Length || !data.AsSpan(sfatOffset, 4).SequenceEqual("SFAT"u8))
            throw new InvalidDataException("The SARC SFAT section is missing.");

        var nodeCount = U16(sfatOffset + 6);
        var nodesOffset = sfatOffset + U16(sfatOffset + 4);
        var sfntOffset = nodesOffset + nodeCount * 16;
        if (sfntOffset + 8 > data.Length || !data.AsSpan(sfntOffset, 4).SequenceEqual("SFNT"u8))
            throw new InvalidDataException("The SARC SFNT section is missing.");
        var namesOffset = sfntOffset + U16(sfntOffset + 4);

        var entries = new List<BotwSarcEntry>(nodeCount);
        for (var index = 0; index < nodeCount; index++)
        {
            var nodeOffset = nodesOffset + index * 16;
            if (nodeOffset + 16 > data.Length)
                throw new InvalidDataException("A SARC node extends beyond the archive.");
            var attributes = U32(nodeOffset + 4);
            var start = checked(dataOffset + (int)U32(nodeOffset + 8));
            var end = checked(dataOffset + (int)U32(nodeOffset + 12));
            if (start < dataOffset || end < start || end > fileLength)
                throw new InvalidDataException("A SARC entry has an invalid data range.");

            var name = $"entry_{index}";
            if ((attributes >> 24) != 0)
            {
                var nameOffset = checked(namesOffset + (int)(attributes & 0x00ffffff) * 4);
                if (nameOffset < namesOffset || nameOffset >= dataOffset)
                    throw new InvalidDataException("A SARC entry has an invalid name offset.");
                var terminator = Array.IndexOf(data, (byte)0, nameOffset, dataOffset - nameOffset);
                if (terminator < 0)
                    throw new InvalidDataException("A SARC entry name is not terminated.");
                name = Encoding.UTF8.GetString(data, nameOffset, terminator - nameOffset);
            }
            entries.Add(new BotwSarcEntry(name, start, end - start));
        }

        return new BotwSarcArchive(data, littleEndian, entries);
    }

    public ReadOnlyMemory<byte> Read(BotwSarcEntry entry)
    {
        if (_indexedPath == null) return new(_data, entry.Offset, entry.Length);
        using var stream = File.OpenRead(_indexedPath);
        stream.Position = entry.Offset;
        var bytes = new byte[entry.Length]; stream.ReadExactly(bytes);
        return bytes;
    }
}

internal sealed record BotwSarcEntry(string Name, int Offset, int Length);
