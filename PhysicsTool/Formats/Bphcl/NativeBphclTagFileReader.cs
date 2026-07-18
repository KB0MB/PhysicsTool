using System.Buffers.Binary;
using System.Text;

namespace HKCLTool;

// Reads the shared TAG0 container. This layer deliberately knows nothing
// about cloth semantics; it only exposes sections, ITEM entries, fixups, and
// reflected Havok type names.
internal static class NativeBphclTagFileReader
{
    public static NativeBphclSection ReadTagFile(byte[] bytes, NativeBphclHeader header)
    {
        var tagFile = ReadSection(bytes, checked((int)header.TagFileOffset), bytes.Length, "TAG0");
        tagFile.Attach(bytes);
        return tagFile;
    }

    public static IReadOnlyList<NativeBphclItem> ReadItems(NativeBphclSection tagFile)
    {
        var itemSection = Descendants(tagFile).FirstOrDefault(x => x.Signature == "ITEM");
        if (itemSection is null)
            return Array.Empty<NativeBphclItem>();
        if (itemSection.PayloadSize % 12 != 0)
            throw new InvalidDataException("BPHCL ITEM data is not a multiple of 12 bytes.");

        var result = new List<NativeBphclItem>(itemSection.PayloadSize / 12);
        var bytes = tagFile.OwnerBytes();
        for (var offset = itemSection.PayloadOffset; offset < itemSection.Offset + itemSection.Size; offset += 12)
        {
            var flags = ReadUInt32LittleEndian(bytes, offset);
            result.Add(new NativeBphclItem(
                flags,
                flags & 0x00ff_ffff,
                ReadUInt32LittleEndian(bytes, offset + 4),
                ReadUInt32LittleEndian(bytes, offset + 8)));
        }
        return result;
    }

    public static IReadOnlyList<NativeBphclPatch> ReadPatches(NativeBphclSection tagFile)
    {
        var ptch = Descendants(tagFile).FirstOrDefault(x => x.Signature == "PTCH");
        if (ptch is null)
            return Array.Empty<NativeBphclPatch>();

        var bytes = tagFile.OwnerBytes();
        var result = new List<NativeBphclPatch>();
        var cursor = ptch.PayloadOffset;
        var end = ptch.Offset + ptch.Size;
        while (cursor + 4 <= end)
        {
            var typeIndex = ReadUInt32LittleEndian(bytes, cursor);
            if (typeIndex == 0)
                break;
            if (cursor + 8 > end)
                throw new InvalidDataException("Truncated BPHCL PTCH entry.");
            var count = ReadUInt32LittleEndian(bytes, cursor + 4);
            var byteCount = checked((int)count * 4);
            if (cursor + 8 > end - byteCount)
                throw new InvalidDataException("BPHCL PTCH entry exceeds its section.");
            var offsets = new uint[checked((int)count)];
            for (var i = 0; i < offsets.Length; i++)
                offsets[i] = ReadUInt32LittleEndian(bytes, cursor + 8 + i * 4);
            result.Add(new NativeBphclPatch(typeIndex, offsets));
            cursor += 8 + byteCount;
        }
        return result;
    }

    public static IReadOnlyList<string> ReadTypeNames(NativeBphclSection tagFile)
    {
        var typeStringSection = Descendants(tagFile)
            .FirstOrDefault(x => x.Signature is "TST1" or "TSTR");
        var typeNameSection = Descendants(tagFile)
            .FirstOrDefault(x => x.Signature is "TNAM" or "TNA1");
        if (typeStringSection is null || typeNameSection is null)
            return Array.Empty<string>();

        var strings = ReadNullTerminatedStrings(typeStringSection);
        var bytes = tagFile.OwnerBytes();
        var cursor = typeNameSection.PayloadOffset;
        var end = typeNameSection.Offset + typeNameSection.Size;
        var typeCount = checked((int)ReadVarUInt(bytes, ref cursor, end));
        var result = new List<string>(typeCount) { string.Empty };

        for (var typeIndex = 1; typeIndex < typeCount; typeIndex++)
        {
            var stringIndex = checked((int)ReadVarUInt(bytes, ref cursor, end));
            var templateCount = checked((int)ReadVarUInt(bytes, ref cursor, end));
            result.Add(stringIndex >= 0 && stringIndex < strings.Count ? strings[stringIndex] : $"<invalid type string {stringIndex}>");
            for (var template = 0; template < templateCount; template++)
            {
                _ = ReadVarUInt(bytes, ref cursor, end);
                _ = ReadVarUInt(bytes, ref cursor, end);
            }
        }
        return result;
    }

    // TYPE stores reflection data that describes each Havok class. This lets
    // us validate specialised layouts before writing a hand-coded decoder.
    public static IReadOnlyList<NativeBphclTypeDefinition> ReadTypeDefinitions(NativeBphclSection tagFile)
    {
        var fieldStringSection = Descendants(tagFile).FirstOrDefault(x => x.Signature is "FST1" or "FSTR");
        var bodySection = Descendants(tagFile).FirstOrDefault(x => x.Signature is "TBDY" or "TBOD");
        if (fieldStringSection is null || bodySection is null)
            return Array.Empty<NativeBphclTypeDefinition>();

        var fieldNames = ReadNullTerminatedStrings(fieldStringSection);
        var bytes = tagFile.OwnerBytes();
        var cursor = bodySection.PayloadOffset;
        var end = bodySection.Offset + bodySection.Size;
        var definitions = new List<NativeBphclTypeDefinition>();

        while (cursor < end)
        {
            var typeIndex = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            if (typeIndex == 0)
            {
                definitions.Add(new NativeBphclTypeDefinition(0, 0, 0, null, null, Array.Empty<NativeBphclTypeMember>()));
                continue;
            }

            var parentTypeIndex = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            var flags = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            if ((flags & 0x01) != 0)
                _ = ReadVarUInt(bytes, ref cursor, end); // format
            if ((flags & 0x02) != 0)
                _ = ReadVarUInt(bytes, ref cursor, end); // subtype
            if ((flags & 0x04) != 0)
                _ = ReadVarUInt(bytes, ref cursor, end); // version
            uint? size = null;
            uint? alignment = null;
            if ((flags & 0x08) != 0)
            {
                size = checked((uint)ReadVarUInt(bytes, ref cursor, end));
                alignment = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            }
            if ((flags & 0x10) != 0)
                _ = ReadVarUInt(bytes, ref cursor, end); // unknown flags

            var members = new List<NativeBphclTypeMember>();
            if ((flags & 0x20) != 0)
            {
                var encodedCount = ReadVarUInt(bytes, ref cursor, end);
                var memberCount = checked((int)(encodedCount & 0xffff));
                for (var index = 0; index < memberCount; index++)
                {
                    var nameIndex = checked((int)ReadVarUInt(bytes, ref cursor, end));
                    var memberFlags = checked((uint)ReadVarUInt(bytes, ref cursor, end));
                    if ((memberFlags & 0x80) != 0)
                    {
                        if (cursor >= end)
                            throw new InvalidDataException("Truncated BPHCL reflected member padding.");
                        cursor++;
                    }
                    var offset = checked((uint)ReadVarUInt(bytes, ref cursor, end));
                    var memberTypeIndex = checked((uint)ReadVarUInt(bytes, ref cursor, end));
                    members.Add(new NativeBphclTypeMember(
                        nameIndex >= 0 && nameIndex < fieldNames.Count ? fieldNames[nameIndex] : $"<invalid field {nameIndex}>",
                        offset,
                        memberTypeIndex,
                        memberFlags));
                }
            }
            if ((flags & 0x40) != 0)
            {
                var interfaceCount = checked((int)ReadVarUInt(bytes, ref cursor, end));
                for (var index = 0; index < interfaceCount; index++)
                {
                    _ = ReadVarUInt(bytes, ref cursor, end);
                    _ = ReadVarUInt(bytes, ref cursor, end);
                }
            }
            if ((flags & 0x80) != 0)
                _ = ReadVarUInt(bytes, ref cursor, end); // attribute string

            definitions.Add(new NativeBphclTypeDefinition(typeIndex, parentTypeIndex, flags, size, alignment, members));
        }
        return definitions;
    }

    private static NativeBphclSection ReadSection(byte[] bytes, int offset, int limit, string? expectedSignature = null)
    {
        if (offset < 0 || offset > limit - 8)
            throw new InvalidDataException("Truncated BPHCL section header.");

        var sizeWord = ReadUInt32BigEndian(bytes, offset);
        var chunkKind = (byte)(sizeWord >> 30);
        var size = checked((int)(sizeWord & 0x3fff_ffff));
        if (size < 8 || offset > limit - size)
            throw new InvalidDataException($"Invalid BPHCL section size at 0x{offset:X}.");

        var signature = Encoding.ASCII.GetString(bytes, offset + 4, 4);
        if (expectedSignature is not null && signature != expectedSignature)
            throw new InvalidDataException($"Expected {expectedSignature} at 0x{offset:X}, found {signature}.");

        var section = new NativeBphclSection(signature, chunkKind, offset, size, offset + 8, size - 8);
        if (signature is "TAG0" or "TYPE" or "INDX")
            PopulateChildren(bytes, section);
        return section;
    }

    private static void PopulateChildren(byte[] bytes, NativeBphclSection parent)
    {
        var cursor = parent.PayloadOffset;
        var end = checked(parent.Offset + parent.Size);
        while (cursor < end)
        {
            var child = ReadSection(bytes, cursor, end);
            parent.Children.Add(child);
            cursor = checked(cursor + child.Size);
        }
        if (cursor != end)
            throw new InvalidDataException($"BPHCL {parent.Signature} children do not end on a section boundary.");
    }

    private static List<string> ReadNullTerminatedStrings(NativeBphclSection section)
    {
        var bytes = section.OwnerBytes();
        var strings = new List<string>();
        var start = section.PayloadOffset;
        var end = section.Offset + section.Size;
        for (var cursor = start; cursor < end; cursor++)
        {
            if (bytes[cursor] != 0)
                continue;
            strings.Add(Encoding.UTF8.GetString(bytes, start, cursor - start));
            start = cursor + 1;
        }
        if (start < end)
            strings.Add(Encoding.UTF8.GetString(bytes, start, end - start));
        return strings;
    }

    private static ulong ReadVarUInt(byte[] bytes, ref int cursor, int end)
    {
        if (cursor >= end)
            throw new InvalidDataException("Truncated BPHCL VarUInt.");

        var first = bytes[cursor++];
        if ((first & 0x80) == 0)
            return first;

        var marker = first >> 3;
        var extraBytes = marker switch
        {
            >= 0x10 and <= 0x17 => 1,
            >= 0x18 and <= 0x1b => 2,
            0x1c => 3,
            0x1d => 4,
            0x1e => 7,
            _ => throw new InvalidDataException($"Unsupported BPHCL VarUInt marker 0x{marker:X}.")
        };
        if (cursor > end - extraBytes)
            throw new InvalidDataException("Truncated BPHCL VarUInt payload.");

        var value = (ulong)(first & (marker switch
        {
            >= 0x10 and <= 0x17 => 0x3f,
            >= 0x18 and <= 0x1b => 0x1f,
            _ => 0x07
        }));
        for (var i = 0; i < extraBytes; i++)
            value = (value << 8) | bytes[cursor++];
        return value;
    }

    private static IEnumerable<NativeBphclSection> Descendants(NativeBphclSection section)
    {
        foreach (var child in section.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static uint ReadUInt32LittleEndian(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static uint ReadUInt32BigEndian(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
}
