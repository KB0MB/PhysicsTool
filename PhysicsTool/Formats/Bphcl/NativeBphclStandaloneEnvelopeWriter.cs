using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace HKCLTool;

// Builds the Phive/TAG0 envelope without borrowing a BPHCL file at runtime.
// The graph serializer will provide real DATA, TYPE, ITEM, PTCH, and AAMP
// payloads in later steps; this layer owns only their native container layout.
public static class NativeBphclStandaloneEnvelopeWriter
{
    private static readonly byte[] SdkVersion = "20220100"u8.ToArray();
    private const int HeaderSize = 0x30;

    public static byte[] CreateEmptyForValidation()
    {
        // hkRootLevelContainer begins with hkArray<m_namedVariants>. A zero
        // pointer and zero count describe an empty root without requiring an
        // ITEM reference or PTCH relocation.
        var rootData = new byte[16];
        return Create(
            rootData,
            CreateMinimalTypeSection(),
            Array.Empty<NativeBphclItem>(),
            Array.Empty<NativeBphclPatch>());
    }

    public static byte[] Create(
        ReadOnlySpan<byte> dataPayload,
        byte[] typeSection,
        IReadOnlyList<NativeBphclItem> items,
        IReadOnlyList<NativeBphclPatch> patches,
        byte[]? aampPayload = null)
    {
        if (typeSection is null)
            throw new ArgumentNullException(nameof(typeSection));
        if (typeSection.Length < 8 || !typeSection.AsSpan(4, 4).SequenceEqual("TYPE"u8))
            throw new ArgumentException("A standalone BPHCL TYPE section must include its TYPE header.", nameof(typeSection));
        if (aampPayload is { Length: > 0 } && !aampPayload.AsSpan(0, Math.Min(4, aampPayload.Length)).SequenceEqual("AAMP"u8))
            throw new ArgumentException("Standalone BPHCL AAMP data must begin with AAMP.", nameof(aampPayload));

        var tagPayload = new List<byte>();
        tagPayload.AddRange(BuildSection("SDKV", SdkVersion, chunkKind: 1));
        tagPayload.AddRange(BuildSection("DATA", dataPayload, chunkKind: 1));
        tagPayload.AddRange(typeSection);
        tagPayload.AddRange(BuildIndexSection(items, patches));
        var tag = BuildSection("TAG0", CollectionsMarshal.AsSpan(tagPayload), chunkKind: 0);

        var aamp = aampPayload ?? Array.Empty<byte>();
        var parameterOffset = Align(HeaderSize + tag.Length, 8);
        var bytes = new byte[checked(parameterOffset + aamp.Length)];
        WritePhiveHeader(bytes, tag.Length, parameterOffset, aamp.Length);
        tag.CopyTo(bytes, HeaderSize);
        if (aamp.Length > 0)
            aamp.CopyTo(bytes, parameterOffset);
        return bytes;
    }

    private static byte[] CreateMinimalTypeSection()
    {
        // The empty reflected table is only for the parser gate. Real cloth
        // output will supply the generated hcl/hka type catalog instead.
        var payload = new List<byte>();
        payload.AddRange(BuildSection("TST1", ReadOnlySpan<byte>.Empty));
        payload.AddRange(BuildSection("FST1", ReadOnlySpan<byte>.Empty));
        payload.AddRange(BuildSection("TNA1", new byte[] { 1 }));
        payload.AddRange(BuildSection("TBDY", ReadOnlySpan<byte>.Empty));
        payload.AddRange(BuildSection("THSH", new byte[] { 0 }));
        payload.AddRange(BuildSection("TPTR", ReadOnlySpan<byte>.Empty));
        return BuildSection("TYPE", CollectionsMarshal.AsSpan(payload), chunkKind: 0);
    }

    private static byte[] BuildIndexSection(
        IReadOnlyList<NativeBphclItem> items,
        IReadOnlyList<NativeBphclPatch> patches)
    {
        var itemPayload = new byte[checked(items.Count * 12)];
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var offset = index * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(itemPayload.AsSpan(offset, 4), item.Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(itemPayload.AsSpan(offset + 4, 4), item.DataOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(itemPayload.AsSpan(offset + 8, 4), item.Count);
        }

        var patchPayload = new List<byte>();
        foreach (var patch in patches)
        {
            WriteUInt32(patchPayload, patch.TypeIndex);
            WriteUInt32(patchPayload, checked((uint)patch.Offsets.Count));
            foreach (var offset in patch.Offsets)
                WriteUInt32(patchPayload, offset);
        }
        // The zero marker separates internal relocations from optional
        // external patches. Standalone output has no external patches yet.
        WriteUInt32(patchPayload, 0);

        var indexPayload = new List<byte>();
        indexPayload.AddRange(BuildSection("ITEM", itemPayload, chunkKind: 1));
        indexPayload.AddRange(BuildSection("PTCH", CollectionsMarshal.AsSpan(patchPayload), chunkKind: 1));
        return BuildSection("INDX", CollectionsMarshal.AsSpan(indexPayload), chunkKind: 0);
    }

    private static byte[] BuildSection(string signature, ReadOnlySpan<byte> payload, uint chunkKind = 1)
    {
        if (signature.Length != 4)
            throw new ArgumentException("BPHCL section signatures must have four ASCII characters.", nameof(signature));
        var size = checked(payload.Length + 8);
        if (size > 0x3fff_ffff)
            throw new InvalidDataException("Standalone BPHCL section exceeds the 30-bit size limit.");

        var bytes = new byte[size];
        if (chunkKind > 3)
            throw new ArgumentOutOfRangeException(nameof(chunkKind));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), (chunkKind << 30) | (uint)size);
        Encoding.ASCII.GetBytes(signature, bytes.AsSpan(4, 4));
        payload.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private static void WritePhiveHeader(byte[] bytes, int tagSize, int parameterOffset, int parameterSize)
    {
        "Phive\0"u8.CopyTo(bytes);
        bytes[6] = 1;
        bytes[7] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), 0xfeff);
        bytes[10] = 3; // Cloth Phive.
        bytes[11] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), checked((uint)parameterOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20, 4), checked((uint)bytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24, 4), checked((uint)(parameterOffset - HeaderSize)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), checked((uint)parameterSize));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32, 4), 0);
        _ = tagSize;
    }

    private static int Align(int value, int alignment) => (value + alignment - 1) & -alignment;

    private static void WriteUInt32(List<byte> destination, uint value)
    {
        destination.Add((byte)value);
        destination.Add((byte)(value >> 8));
        destination.Add((byte)(value >> 16));
        destination.Add((byte)(value >> 24));
    }
}
