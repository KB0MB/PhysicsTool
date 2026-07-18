using System.Buffers.Binary;

namespace HKCLTool;

// Native, read-only BPHCL document. Binary TAG0 parsing and cloth-domain
// decoding live in separate partial files to keep this entry point focused.
public sealed partial class NativeBphclDocument
{
    private NativeBphclDocument(byte[] bytes, NativeBphclHeader header, NativeBphclSection tagFile)
    {
        Bytes = bytes;
        Header = header;
        TagFile = tagFile;
    }

    public byte[] Bytes { get; }
    public NativeBphclHeader Header { get; }
    public NativeBphclSection TagFile { get; }
    public NativeBphclSection? DataSection => TagFile.Children.FirstOrDefault(x => x.Signature == "DATA");
    public NativeBphclSection? TypeSection => TagFile.Children.FirstOrDefault(x => x.Signature == "TYPE");
    public NativeBphclSection? IndexSection => TagFile.Children.FirstOrDefault(x => x.Signature == "INDX");
    public IReadOnlyList<NativeBphclItem> Items { get; private init; } = Array.Empty<NativeBphclItem>();
    public IReadOnlyList<NativeBphclPatch> InternalPatches { get; private init; } = Array.Empty<NativeBphclPatch>();
    // Havok type indexes are one-based. Entry zero stays blank deliberately.
    public IReadOnlyList<string> TypeNames { get; private init; } = Array.Empty<string>();
    public IReadOnlyList<NativeBphclTypeDefinition> TypeDefinitions { get; private init; } = Array.Empty<NativeBphclTypeDefinition>();
    public IReadOnlyList<NativeBphclNamedVariant> RootVariants { get; private set; } = Array.Empty<NativeBphclNamedVariant>();
    public IReadOnlyList<NativeBphclCloth> Cloths { get; private set; } = Array.Empty<NativeBphclCloth>();
    public IReadOnlyList<NativeBphclSkeleton> Skeletons { get; private set; } = Array.Empty<NativeBphclSkeleton>();
    public IReadOnlyList<NativeBphclCollider> Colliders { get; private set; } = Array.Empty<NativeBphclCollider>();
    internal NativeAampMetadata Aamp { get; private set; } = NativeAampMetadata.Read(Array.Empty<byte>(), 0, 0);
    public int CollidableCount { get; private set; }
    public IEnumerable<uint> RelocationOffsets => InternalPatches.SelectMany(x => x.Offsets);

    public static NativeBphclDocument Open(string path) => Parse(File.ReadAllBytes(path));

    public static NativeBphclDocument Parse(byte[] bytes)
    {
        if (bytes.Length < 0x30)
            throw new InvalidDataException("BPHCL is shorter than its Phive header.");
        if (!bytes.AsSpan(0, 6).SequenceEqual("Phive\0"u8))
            throw new InvalidDataException("Missing Phive header.");

        var header = new NativeBphclHeader(
            bytes[6], bytes[7],
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)),
            bytes[10], bytes[11],
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(32, 4)));

        if (header.FileType != 3)
            throw new InvalidDataException($"Phive file type {header.FileType} is not cloth.");
        if (header.TagFileOffset >= bytes.Length)
            throw new InvalidDataException("Phive TAG0 offset is outside the file.");

        var tagFile = NativeBphclTagFileReader.ReadTagFile(bytes, header);
        var document = new NativeBphclDocument(bytes, header, tagFile)
        {
            Items = NativeBphclTagFileReader.ReadItems(tagFile),
            InternalPatches = NativeBphclTagFileReader.ReadPatches(tagFile),
            TypeNames = NativeBphclTagFileReader.ReadTypeNames(tagFile),
            TypeDefinitions = NativeBphclTagFileReader.ReadTypeDefinitions(tagFile)
        };
        document.RootVariants = document.ReadRootVariants();
        document.ReadClothAndSkeletonLists();
        document.Aamp = NativeAampMetadata.Read(bytes, header.ParameterOffset, header.ParameterSize);
        return document;
    }

    public NativeBphclItem GetItem(int index)
    {
        if ((uint)index >= (uint)Items.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Items[index];
    }

    public string? GetTypeName(uint typeIndex) =>
        typeIndex > 0 && typeIndex < TypeNames.Count ? TypeNames[checked((int)typeIndex)] : null;

    public IEnumerable<NativeBphclNamedVariant> FindRootVariants(string className) =>
        RootVariants.Where(x => string.Equals(x.ClassName, className, StringComparison.Ordinal));

    public bool IsRelocationOffset(uint dataOffset) => RelocationOffsets.Contains(dataOffset);

    // Serialized pointer fields contain a 32-bit ITEM index. The game loader
    // applies PTCH fixups and expands that index into a runtime pointer.
    public bool TryGetReferencedItem(uint dataOffset, out int itemIndex)
    {
        itemIndex = -1;
        if (!IsRelocationOffset(dataOffset) || DataSection is null || dataOffset > DataSection.PayloadSize - 4)
            return false;

        var rawIndex = BinaryPrimitives.ReadUInt32LittleEndian(
            Bytes.AsSpan(DataSection.PayloadOffset + checked((int)dataOffset), 4));
        if (rawIndex >= Items.Count)
            return false;

        itemIndex = checked((int)rawIndex);
        return true;
    }
}
