using System.Buffers.Binary;
using System.Text;

namespace HKCLTool;

// Small, bounded reader for the AAMP block embedded after a BPHCL TAG0.
// It intentionally exposes only the named cloth entries we need to keep in
// sync with hclClothContainer; a general AAMP writer comes later.
internal sealed class NativeAampMetadata
{
    private const uint ClothMeshListHash = 1_571_872_146;
    private const uint NameParameterHash = 4_262_580_536;
    private const byte String32 = 7;
    private const byte String64 = 8;
    private const byte String256 = 15;
    private const byte StringReference = 20;

    private NativeAampMetadata(
        int offset,
        int size,
        IReadOnlyList<NativeAampNamedObject> clothEntries,
        NativeAampObjectList? clothObjectList,
        string? diagnostic)
    {
        Offset = offset;
        Size = size;
        ClothEntries = clothEntries;
        ClothObjectList = clothObjectList;
        Diagnostic = diagnostic;
    }

    public int Offset { get; }
    public int Size { get; }
    public IReadOnlyList<NativeAampNamedObject> ClothEntries { get; }
    public string? Diagnostic { get; }
    public bool IsReadable => Diagnostic is null;
    private NativeAampObjectList? ClothObjectList { get; }

    public static NativeAampMetadata Read(byte[] bytes, uint parameterOffset, uint parameterSize)
    {
        if (parameterSize == 0)
            return new NativeAampMetadata(0, 0, Array.Empty<NativeAampNamedObject>(), null, "No embedded AAMP block.");

        try
        {
            var offset = checked((int)parameterOffset);
            var size = checked((int)parameterSize);
            var end = checked(offset + size);
            if (offset < 0 || size < 0 || end > bytes.Length || size < 0x30)
                throw new InvalidDataException("Embedded AAMP range lies outside the BPHCL file.");
            if (!bytes.AsSpan(offset, 4).SequenceEqual("AAMP"u8))
                throw new InvalidDataException("Embedded parameter block does not begin with AAMP.");

            var declaredSize = ReadUInt32(bytes, offset + 0x0c, offset, end);
            if (declaredSize == 0 || declaredSize > size)
                throw new InvalidDataException("Embedded AAMP declares an invalid file size.");

            var rootRelativeOffset = ReadUInt32(bytes, offset + 0x14, offset, end);
            var rootOffset = checked(offset + 0x30 + (int)rootRelativeOffset);
            EnsureRange(rootOffset, 12, offset, end);

            var clothEntries = new List<NativeAampNamedObject>();
            var visitedLists = new HashSet<int>();
            var clothObjectList = ReadList(bytes, offset, end, rootOffset, visitedLists, clothEntries);
            return new NativeAampMetadata(offset, size, clothEntries, clothObjectList, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or ArgumentOutOfRangeException)
        {
            return new NativeAampMetadata(
                checked((int)parameterOffset),
                checked((int)parameterSize),
                Array.Empty<NativeAampNamedObject>(),
                null,
                ex.Message);
        }
    }

    // Remove an AAMP list entry without changing the order of the remaining
    // entries. BPHCL's cloth and skeleton arrays preserve their order, and
    // the loader-facing MeshList needs to stay aligned with them.
    public byte[] RemoveClothEntry(byte[] sourceBytes, string clothName)
    {
        if (!IsReadable || ClothObjectList is null)
            throw new InvalidDataException($"Cannot update BPHCL AAMP metadata: {Diagnostic ?? "cloth_mesh_list is missing"}.");

        var matches = ClothEntries
            .Where(entry => string.Equals(entry.Name, clothName, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
            return sourceBytes.AsSpan(Offset, Size).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException($"BPHCL AAMP has {matches.Length} cloth_mesh_list entries named '{clothName}'.");
        if (ClothObjectList.Count <= 0)
            throw new InvalidDataException("BPHCL AAMP cloth_mesh_list has no object entries.");

        var result = sourceBytes.AsSpan(Offset, Size).ToArray();
        var removed = matches[0];
        var lastObjectOffset = checked(ClothObjectList.ObjectArrayOffset + (ClothObjectList.Count - 1) * 8);
        if (removed.DataOffset != lastObjectOffset)
        {
            // Each object header is eight bytes. Moving a header back one
            // slot means its relative parameter address grows by two words.
            for (var sourceOffset = removed.DataOffset + 8; sourceOffset <= lastObjectOffset; sourceOffset += 8)
            {
                var hash = ReadUInt32(sourceBytes, sourceOffset, Offset, Offset + Size);
                var flags = ReadUInt32(sourceBytes, sourceOffset + 4, Offset, Offset + Size);
                var originalOffsetWords = flags & 0xffff;
                if (originalOffsetWords > 0xfffd)
                    throw new InvalidDataException("BPHCL AAMP object parameter offset cannot be represented after deletion.");

                var destinationOffset = sourceOffset - 8;
                WriteUInt32(result, destinationOffset - Offset, hash);
                WriteUInt32(result, destinationOffset - Offset + 4,
                    (flags & 0xffff_0000u) | (originalOffsetWords + 2));
            }
        }

        var listFlags = ReadUInt32(sourceBytes, ClothObjectList.FlagsOffset, Offset, Offset + Size);
        WriteUInt32(result, ClothObjectList.FlagsOffset - Offset,
            (listFlags & 0x0000_ffffu) | checked(((uint)ClothObjectList.Count - 1u) << 16));
        return result;
    }

    private static NativeAampObjectList? ReadList(
        byte[] bytes,
        int archiveStart,
        int archiveEnd,
        int listOffset,
        ISet<int> visitedLists,
        ICollection<NativeAampNamedObject> clothEntries)
    {
        if (!visitedLists.Add(listOffset))
            return null;

        EnsureRange(listOffset, 12, archiveStart, archiveEnd);
        var listHash = ReadUInt32(bytes, listOffset, archiveStart, archiveEnd);
        var childLists = ReadFlags(bytes, listOffset + 4, archiveStart, archiveEnd);
        var childObjects = ReadFlags(bytes, listOffset + 8, archiveStart, archiveEnd);

        NativeAampObjectList? nestedClothList = null;
        var childListOffset = checked(listOffset + childLists.OffsetWords * 4);
        EnsureRange(childListOffset, checked(childLists.Count * 12), archiveStart, archiveEnd);
        for (var index = 0; index < childLists.Count; index++)
            nestedClothList ??= ReadList(
                bytes,
                archiveStart,
                archiveEnd,
                childListOffset + index * 12,
                visitedLists,
                clothEntries);

        if (listHash != ClothMeshListHash)
            return nestedClothList;

        var childObjectOffset = checked(listOffset + childObjects.OffsetWords * 4);
        EnsureRange(childObjectOffset, checked(childObjects.Count * 8), archiveStart, archiveEnd);
        for (var index = 0; index < childObjects.Count; index++)
        {
            var objectOffset = childObjectOffset + index * 8;
            var name = ReadObjectName(bytes, archiveStart, archiveEnd, objectOffset);
            if (!string.IsNullOrWhiteSpace(name))
                clothEntries.Add(new NativeAampNamedObject(index, objectOffset, name));
        }

        return new NativeAampObjectList(listOffset + 8, childObjectOffset, childObjects.Count);
    }

    private static string? ReadObjectName(byte[] bytes, int archiveStart, int archiveEnd, int objectOffset)
    {
        EnsureRange(objectOffset, 8, archiveStart, archiveEnd);
        var parameters = ReadFlags(bytes, objectOffset + 4, archiveStart, archiveEnd);
        var parameterOffset = checked(objectOffset + parameters.OffsetWords * 4);
        EnsureRange(parameterOffset, checked(parameters.Count * 8), archiveStart, archiveEnd);

        for (var index = 0; index < parameters.Count; index++)
        {
            var entryOffset = parameterOffset + index * 8;
            if (ReadUInt32(bytes, entryOffset, archiveStart, archiveEnd) != NameParameterHash)
                continue;

            var rawFlags = ReadUInt32(bytes, entryOffset + 4, archiveStart, archiveEnd);
            // AAMP parameter flags store the data offset in the low 24 bits
            // and the parameter type in the high byte.
            var type = unchecked((byte)(rawFlags >> 24));
            if (type is not (String32 or String64 or String256 or StringReference))
                return null;

            var valueOffset = checked(entryOffset + (int)(rawFlags & 0x00ff_ffff) * 4);
            var maximumLength = type switch
            {
                String32 => 32,
                String64 => 64,
                String256 => 256,
                _ => archiveEnd - valueOffset
            };
            return ReadNullTerminatedString(bytes, valueOffset, maximumLength, archiveStart, archiveEnd);
        }

        return null;
    }

    private static (int OffsetWords, int Count) ReadFlags(byte[] bytes, int offset, int start, int end)
    {
        var value = ReadUInt32(bytes, offset, start, end);
        return ((int)(value & 0xffff), (int)(value >> 16));
    }

    private static uint ReadUInt32(byte[] bytes, int offset, int start, int end)
    {
        EnsureRange(offset, 4, start, end);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    private static string ReadNullTerminatedString(
        byte[] bytes,
        int offset,
        int maximumLength,
        int start,
        int end)
    {
        EnsureRange(offset, 1, start, end);
        var length = 0;
        while (length < maximumLength && offset + length < end && bytes[offset + length] != 0)
            length++;
        return Encoding.UTF8.GetString(bytes, offset, length);
    }

    private static void EnsureRange(int offset, int length, int start, int end)
    {
        if (length < 0 || offset < start || offset > end - length)
            throw new InvalidDataException("Embedded AAMP contains an out-of-range list or object pointer.");
    }
}

internal sealed record NativeAampNamedObject(int Index, int DataOffset, string Name);
internal sealed record NativeAampObjectList(int FlagsOffset, int ObjectArrayOffset, int Count);
