namespace HKCLTool;

// A TAG0 section retains its exact file range so read and write code can use
// the original layout when the native serializer is introduced.
public sealed class NativeBphclSection
{
    private byte[]? _ownerBytes;

    public NativeBphclSection(string signature, byte chunkKind, int offset, int size, int payloadOffset, int payloadSize)
    {
        Signature = signature;
        ChunkKind = chunkKind;
        Offset = offset;
        Size = size;
        PayloadOffset = payloadOffset;
        PayloadSize = payloadSize;
    }

    public string Signature { get; }
    public byte ChunkKind { get; }
    public int Offset { get; }
    public int Size { get; }
    public int PayloadOffset { get; }
    public int PayloadSize { get; }
    public List<NativeBphclSection> Children { get; } = new();

    internal byte[] OwnerBytes() => _ownerBytes ?? throw new InvalidOperationException("Section is not attached to a BPHCL document.");

    internal void Attach(byte[] bytes)
    {
        _ownerBytes = bytes;
        foreach (var child in Children)
            child.Attach(bytes);
    }
}
