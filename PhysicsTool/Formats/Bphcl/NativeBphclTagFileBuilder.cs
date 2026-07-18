using System.Buffers.Binary;
using System.Text;

namespace HKCLTool;

// Rebuilds the small mutable part of a TAG0 container while retaining every
// untouched section byte-for-byte. Merge support will use this once it has
// appended DATA objects, ITEM entries, and relocation patch offsets.
internal static class NativeBphclTagFileBuilder
{
    public static byte[] Rebuild(
        NativeBphclDocument document,
        ReadOnlySpan<byte> dataPayload,
        IReadOnlyList<NativeBphclItem> items,
        IReadOnlyList<NativeBphclPatch> internalPatches,
        byte[]? replacementTypeSection = null,
        byte[]? replacementAamp = null)
    {
        var data = document.DataSection
            ?? throw new InvalidDataException("BPHCL TAG0 has no DATA section.");
        var index = document.IndexSection
            ?? throw new InvalidDataException("BPHCL TAG0 has no INDX section.");

        var dataBytes = BuildSection(data.Signature, data.ChunkKind, dataPayload);
        var indexBytes = BuildIndexSection(document, index, items, internalPatches);

        using var tagPayload = new MemoryStream();
        foreach (var section in document.TagFile.Children)
        {
            if (ReferenceEquals(section, data))
                tagPayload.Write(dataBytes);
            else if (ReferenceEquals(section, index))
                tagPayload.Write(indexBytes);
            else if (section.Signature == "TYPE" && replacementTypeSection != null)
                tagPayload.Write(replacementTypeSection);
            else
                WriteOriginalSection(tagPayload, document.Bytes, section);
        }

        var tagBytes = BuildSection(document.TagFile.Signature, document.TagFile.ChunkKind, tagPayload.ToArray());
        var oldTagEnd = checked(document.TagFile.Offset + document.TagFile.Size);
        var delta = checked(tagBytes.Length - document.TagFile.Size);

        using var file = new MemoryStream(document.Bytes.Length + delta);
        file.Write(document.Bytes, 0, document.TagFile.Offset);
        file.Write(tagBytes);
        file.Write(document.Bytes, oldTagEnd, document.Bytes.Length - oldTagEnd);

        var rebuilt = file.ToArray();
        UpdatePhiveHeader(document, rebuilt, tagBytes.Length, delta, oldTagEnd);
        if (replacementAamp != null)
        {
            var parameterOffset = BinaryPrimitives.ReadUInt32LittleEndian(rebuilt.AsSpan(16, 4));
            var originalAampSize = checked((int)document.Header.ParameterSize);
            if (parameterOffset > rebuilt.Length - originalAampSize)
                throw new InvalidDataException("Replacement BPHCL AAMP lies outside the rebuilt file.");

            var prefix = rebuilt.AsSpan(0, checked((int)parameterOffset)).ToArray();
            var suffixOffset = checked((int)parameterOffset + originalAampSize);
            var suffix = rebuilt.AsSpan(suffixOffset).ToArray();
            rebuilt = new byte[prefix.Length + replacementAamp.Length + suffix.Length];
            prefix.CopyTo(rebuilt, 0);
            replacementAamp.CopyTo(rebuilt, prefix.Length);
            suffix.CopyTo(rebuilt, prefix.Length + replacementAamp.Length);

            var aampDelta = replacementAamp.Length - originalAampSize;
            BinaryPrimitives.WriteUInt32LittleEndian(rebuilt.AsSpan(28, 4), checked((uint)replacementAamp.Length));
            var fileEndOffset = BinaryPrimitives.ReadUInt32LittleEndian(rebuilt.AsSpan(20, 4));
            if (fileEndOffset >= parameterOffset + originalAampSize)
                BinaryPrimitives.WriteUInt32LittleEndian(rebuilt.AsSpan(20, 4), checked((uint)((long)fileEndOffset + aampDelta)));
        }
        return rebuilt;
    }

    private static byte[] BuildIndexSection(
        NativeBphclDocument document,
        NativeBphclSection index,
        IReadOnlyList<NativeBphclItem> items,
        IReadOnlyList<NativeBphclPatch> internalPatches)
    {
        var item = index.Children.FirstOrDefault(x => x.Signature == "ITEM")
            ?? throw new InvalidDataException("BPHCL INDX has no ITEM section.");
        var patch = index.Children.FirstOrDefault(x => x.Signature == "PTCH")
            ?? throw new InvalidDataException("BPHCL INDX has no PTCH section.");

        using var payload = new MemoryStream();
        foreach (var section in index.Children)
        {
            if (ReferenceEquals(section, item))
                payload.Write(BuildItems(item.ChunkKind, items));
            else if (ReferenceEquals(section, patch))
                payload.Write(BuildPatches(document, patch, internalPatches));
            else
                WriteOriginalSection(payload, document.Bytes, section);
        }
        return BuildSection(index.Signature, index.ChunkKind, payload.ToArray());
    }

    private static byte[] BuildItems(byte chunkKind, IReadOnlyList<NativeBphclItem> items)
    {
        var payload = new byte[checked(items.Count * 12)];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var offset = index * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset, 4), item.Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 4, 4), item.DataOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 8, 4), item.Count);
        }
        return BuildSection("ITEM", chunkKind, payload);
    }

    private static byte[] BuildPatches(
        NativeBphclDocument document,
        NativeBphclSection original,
        IReadOnlyList<NativeBphclPatch> internalPatches)
    {
        using var payload = new MemoryStream();
        foreach (var patch in internalPatches)
        {
            WriteUInt32(payload, patch.TypeIndex);
            WriteUInt32(payload, checked((uint)patch.Offsets.Count));
            foreach (var offset in patch.Offsets)
                WriteUInt32(payload, offset);
        }

        // The zero type index separates internal and external relocation
        // records. We do not yet author external records, but they must stay
        // untouched while rebuilding a BPHCL that already contains them.
        var externalPatchData = ReadExternalPatchTail(document.Bytes, original);
        if (externalPatchData.HasTerminator)
            WriteUInt32(payload, 0);
        payload.Write(externalPatchData.Tail);
        return BuildSection("PTCH", original.ChunkKind, payload.ToArray());
    }

    private static (bool HasTerminator, byte[] Tail) ReadExternalPatchTail(byte[] bytes, NativeBphclSection section)
    {
        var cursor = section.PayloadOffset;
        var end = checked(section.Offset + section.Size);
        while (cursor + 4 <= end)
        {
            var typeIndex = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor, 4));
            cursor += 4;
            if (typeIndex == 0)
                return (true, bytes.AsSpan(cursor, end - cursor).ToArray());

            if (cursor + 4 > end)
                throw new InvalidDataException("Truncated BPHCL PTCH count.");
            var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor, 4));
            cursor += 4;
            var byteCount = checked((int)count * 4);
            if (cursor > end - byteCount)
                throw new InvalidDataException("BPHCL PTCH entry exceeds its section.");
            cursor += byteCount;
        }

        // Retail samples can end directly after the last internal group. The
        // zero terminator described by older format notes is optional here.
        if (cursor == end)
            return (false, Array.Empty<byte>());
        throw new InvalidDataException("BPHCL PTCH ends in a truncated patch group.");
    }

    private static byte[] BuildSection(string signature, byte chunkKind, ReadOnlySpan<byte> payload)
    {
        if (signature.Length != 4)
            throw new ArgumentException("BPHCL section signatures are four ASCII characters.", nameof(signature));
        var size = checked(payload.Length + 8);
        if (size > 0x3fff_ffff)
            throw new InvalidDataException("BPHCL section exceeds the 30-bit size limit.");

        var bytes = new byte[size];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), ((uint)chunkKind << 30) | (uint)size);
        Encoding.ASCII.GetBytes(signature, bytes.AsSpan(4, 4));
        payload.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private static void WriteOriginalSection(Stream destination, byte[] source, NativeBphclSection section) =>
        destination.Write(source, section.Offset, section.Size);

    private static void WriteUInt32(Stream destination, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        destination.Write(bytes);
    }

    private static void UpdatePhiveHeader(
        NativeBphclDocument document,
        byte[] bytes,
        int tagSize,
        int delta,
        int oldTagEnd)
    {
        // The Phive wrapper's tagfile_size includes the alignment padding
        // between TAG0 and the following AAMP region. The TAG0 section size
        // itself does not, so retain that existing gap and apply only delta.
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(24, 4),
            checked((uint)((long)document.Header.TagFileSize + delta)));

        // A changed TAG0 shifts the AAMP and file-end regions that follow it.
        // Their contents are retained exactly, so only their wrapper offsets
        // need adjustment.
        UpdateOffsetIfAfter(bytes, 16, delta, oldTagEnd);
        UpdateOffsetIfAfter(bytes, 20, delta, oldTagEnd);
    }

    private static void UpdateOffsetIfAfter(byte[] bytes, int headerOffset, int delta, int oldTagEnd)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(headerOffset, 4));
        if (value >= oldTagEnd)
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(headerOffset, 4),
                checked((uint)((long)value + delta)));
    }
}
