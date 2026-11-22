using System.Buffers.Binary;
using System.Numerics;

namespace PhysicsTool.Core;

// cave017 layout references: TotKCave (BladesawStudios) and Starlight's
// CrbinFile declarations. All offsets/counts are checked before accessing data.
internal sealed class TotkCaveDocument
{
    internal readonly record struct Node(ushort X, ushort Y, ushort Z, ushort Lod, int Stream, int Streams, int Material, Vector3 Min, Vector3 Max);
    internal readonly record struct Stream(int FirstIndex, int Triangles, int Page);
    internal readonly record struct Material(Vector3 U, Vector3 V, int Layer, float Scale);
    internal uint Id { get; }
    internal Vector3 Origin { get; }
    internal Vector3 Minimum { get; }
    internal Vector3 Maximum { get; }
    internal float SideLength { get; }
    internal int Subdivisions { get; }
    internal Node[] Nodes { get; }
    internal Stream[] Streams { get; }
    internal Material[] Materials { get; }
    internal IReadOnlyDictionary<int, int> PageSizes { get; }

    internal TotkCaveDocument(byte[] data)
    {
        if (data.Length < 0x1dc || data.Length > 16 * 1024 * 1024 || !data.AsSpan(0, 8).SequenceEqual("cave017\0"u8))
            throw new InvalidDataException("Invalid cave017 index.");
        int U(int offset) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)));
        int S(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        float F(int offset)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
            if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite cave coordinate.");
            return value;
        }
        Vector3 V(int offset) => new(F(offset), F(offset + 4), F(offset + 8));
        (int Offset, int Count) Table(int offset, int stride)
        {
            int start = U(offset), count = U(offset + 4);
            if (count > 1000000 || start > data.Length || (long)count * stride > data.Length - start)
                throw new InvalidDataException("Cave table exceeds its index file.");
            return (start, count);
        }
        Id = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x10));
        Origin = V(0xb8); SideLength = F(0xc4); Subdivisions = U(0xc8);
        Minimum = V(0xcc); Maximum = V(0xd8);
        if (SideLength <= 0 || SideLength > 10000 || Subdivisions > 16 || Minimum.X > Maximum.X || Minimum.Y > Maximum.Y || Minimum.Z > Maximum.Z)
            throw new InvalidDataException("Invalid cave dimensions.");
        var nodes = Table(0x68, 8); var bounds = Table(0x80, 24); var indices = Table(0x88, 16);
        var streams = Table(0x98, 12); var materials = Table(0xa0, 32); var pages = Table(0xa8, 8);
        if (bounds.Count != nodes.Count || indices.Count != nodes.Count) throw new InvalidDataException("Cave node tables disagree.");
        var sizes = new Dictionary<int, int>();
        for (int i = 0; i < pages.Count; i++)
        {
            int p = pages.Offset + i * 8, size = U(p);
            if (size <= 0 || size > 64 * 1024 * 1024 || !sizes.TryAdd(U(p + 4), size)) throw new InvalidDataException("Invalid cave page entry.");
        }
        PageSizes = sizes;
        Streams = new Stream[streams.Count];
        for (int i = 0; i < streams.Count; i++)
        {
            int p = streams.Offset + i * 12, first = U(p), triangles = U(p + 4), page = S(p + 10);
            if (!sizes.TryGetValue(page, out int size) || triangles > 1000000 || (long)first * 2 + (long)triangles * 6 > size)
                throw new InvalidDataException("Invalid cave index stream.");
            Streams[i] = new(first, triangles, page);
        }
        Materials = new Material[materials.Count];
        for (int i = 0; i < materials.Count; i++)
        {
            int p = materials.Offset + i * 32; float layer = F(p + 12), scale = F(p + 28);
            if (layer < 0 || layer > 4095 || layer != MathF.Truncate(layer) || Math.Abs(scale) > 10000)
                throw new InvalidDataException("Invalid cave material projection.");
            Materials[i] = new(V(p), V(p + 16), (int)layer, scale);
        }
        Nodes = new Node[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            int p = nodes.Offset + i * 8, q = indices.Offset + i * 16, b = bounds.Offset + i * 24;
            int first = U(q), count = data[q + 6], material = S(q + 4), lod = S(p + 6);
            var min = V(b); var max = V(b + 12);
            if ((long)first + count > Streams.Length || material >= Materials.Length || lod > Subdivisions ||
                min.X > max.X || min.Y > max.Y || min.Z > max.Z) throw new InvalidDataException("Invalid cave node.");
            Nodes[i] = new((ushort)S(p), (ushort)S(p + 2), (ushort)S(p + 4), (ushort)lod, first, count, material, min, max);
        }
    }
}
