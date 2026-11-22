namespace HKCLTool;

// References are kept in bind-world space. Native copy/gather transforms through
// source-world and inverse destination-world, so this representation preserves
// the same positions without baking either scratch buffer's local frame.
internal sealed class OotReferenceBuffers
{
    private readonly Dictionary<int, NativeBphclBufferDefinition> _definitions;
    private readonly Dictionary<int, int> _redirects = new();
    private readonly Dictionary<int, PositionStorage?> _positions = new();
    internal Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> Attachments { get; } = new();

    private sealed class PositionStorage(uint count)
    {
        internal uint Count { get; } = count;
        internal Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> Vertices { get; } = new();
    }

    internal OotReferenceBuffers(IEnumerable<NativeBphclBufferDefinition> definitions,
        IEnumerable<NativeBphclClothStateBufferAccess>? accesses = null,
        IReadOnlyDictionary<int, bool>? shadowPositions = null)
    {
        _definitions = definitions.ToDictionary(b => b.Index);
        foreach (var definition in _definitions.Values)
        {
            bool ownsPositions = definition.ClassName == "hclScratchBufferDefinition";
            if (definition.ClassName == "hclShadowBufferDefinition" &&
                (shadowPositions == null || !shadowPositions.TryGetValue(definition.Index, out ownsPositions)))
                throw new InvalidDataException("Missing OoT shadow position allocation flag.");
            _positions[definition.Index] = ownsPositions && definition.VertexCount > 0 ? new(definition.VertexCount) : null;
        }
        foreach (var route in accesses ?? [])
        {
            if (route.BufferIndex > int.MaxValue || route.ShadowBufferIndex > int.MaxValue ||
                !_definitions.TryGetValue((int)route.BufferIndex, out var source) ||
                !_definitions.TryGetValue((int)route.ShadowBufferIndex, out var shadow))
                throw new InvalidDataException("Invalid OoT shadow-buffer index.");
            if (source.Index == shadow.Index) continue;
            if (source.ClassName is not ("hclScratchBufferDefinition" or "hclShadowBufferDefinition") ||
                shadow.ClassName is not ("hclScratchBufferDefinition" or "hclShadowBufferDefinition"))
                throw new NotSupportedException("OoT shadow routing requires decoded scratch position inputs.");
            _redirects[source.Index] = shadow.Index;
            // Setup copies physical components in access order, not through
            // redirects. Borrowing snapshots the pointer; later allocation on
            // the source does not retroactively fill an earlier empty shadow.
            _positions[shadow.Index] ??= _positions[source.Index];
        }
    }

    private PositionStorage Storage(int index)
    {
        // Consumers read +0x110 exactly once, even for chains and cycles.
        int physical = _redirects.GetValueOrDefault(index, index);
        if (!_positions.TryGetValue(physical, out var storage) || storage == null)
            throw new NotSupportedException($"OoT reference buffer {index} has no supported position storage.");
        return storage;
    }

    private Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> Buffer(int index) => Storage(index).Vertices;

    private void Range(int buffer, int start, int count)
    {
        uint capacity = VertexCount(buffer);
        if (start < 0 || count < 0 || (ulong)start + (ulong)count > capacity)
            throw new InvalidDataException($"OoT reference route exceeds buffer {buffer}.");
    }

    internal uint VertexCount(int buffer)
    {
        return Storage(buffer).Count;
    }

    internal void Skin(int buffer, IReadOnlyDictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> vertices)
    {
        var destination = Buffer(buffer);
        foreach (var pair in vertices)
        {
            Range(buffer, pair.Key, 1);
            destination[pair.Key] = HkclService.ResolveOotPinchReference(vertices, pair.Key);
        }
    }

    internal IReadOnlyList<ParticlePreviewBoneWeight> Resolve(int buffer, int vertex)
    {
        Range(buffer, vertex, 1);
        return HkclService.ResolveOotPinchReference(Buffer(buffer), vertex);
    }

    internal void Copy(int input, int output, int startIn, int startOut, int count)
    {
        Range(input, startIn, count);
        Range(output, startOut, count);
        // The native loop is forward, including overlapping aliased ranges.
        var destination = Buffer(output);
        for (int i = 0; i < count; i++)
            destination[startOut + i] = Resolve(input, startIn + i);
    }

    internal void Gather(int input, int output, IReadOnlyList<short> map)
    {
        Range(output, 0, map.Count);
        Buffer(input);
        var destination = Buffer(output);
        for (int vertex = 0; vertex < map.Count; vertex++)
            if (map[vertex] >= 0) destination[vertex] = Resolve(input, map[vertex]);
    }

    internal void Move(int buffer, IEnumerable<NativeBphclVertexParticlePair> pairs)
    {
        foreach (var pair in pairs) Attachments[pair.ParticleIndex] = Resolve(buffer, pair.VertexIndex);
    }
}
