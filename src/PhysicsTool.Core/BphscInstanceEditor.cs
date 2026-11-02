using System.Buffers.Binary;
using System.Numerics;

namespace PhysicsTool.Core;

// Replaces the compound's instance array and SIMD tree while retaining the geometry
// tail byte-for-byte. Every geometry object moves by the same offset, preserving
// its internal relative pointers; instance pointers and ITEM offsets are relocated.
internal static class BphscInstanceEditor
{
    internal readonly record struct Instance(int Source, ushort ReferenceTag);
    private readonly record struct Box(Vector3 Min, Vector3 Max);

    internal static byte[] Rebuild(byte[] source, IReadOnlyList<Instance> instances)
    {
        int Size(int at) => checked((int)(BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(at)) & 0x3fffffff));
        int dataChunk = -1, itemChunk = -1, tagSize = Size(0);
        for (int at = 8; at < tagSize; at += Size(at))
        {
            if (Size(at) < 8 || at + Size(at) > tagSize) throw new InvalidDataException("Invalid compound chunk.");
            if (source.AsSpan(at + 4, 4).SequenceEqual("DATA"u8)) dataChunk = at;
            if (source.AsSpan(at + 4, 4).SequenceEqual("INDX"u8))
                for (int child = at + 8; child < at + Size(at); child += Size(child))
                {
                    if (Size(child) < 8 || child + Size(child) > at + Size(at) || !source.AsSpan(child + 4, 4).SequenceEqual("ITEM"u8))
                        throw new NotSupportedException("This collision tagfile has unsupported relocation records.");
                    itemChunk = child;
                }
        }
        if (dataChunk < 0 || itemChunk < 0) throw new InvalidDataException("Missing compound data or item table.");
        int data = dataChunk + 8, dataEnd = dataChunk + Size(dataChunk);
        int oldCount = I32(source, data + 80), oldNodes = I32(source, data + 240);
        int shapes = checked(data + 72 + (int)I64(source, data + 72));
        int nodes = checked(data + 232 + (int)I64(source, data + 232));
        int tail = checked(nodes + oldNodes * 128);
        if (shapes != data + 272 || nodes != shapes + oldCount * 64 || tail > dataEnd || oldNodes < 2 ||
            source[data + 256] != 2 || I32(source, data + 88) != -1 || I32(source, data + 92) != oldCount ||
            source.AsSpan(data + 96, 16).IndexOfAnyExcept((byte)0) >= 0 || source.AsSpan(data + 156, 76).IndexOfAnyExcept((byte)0) >= 0)
            throw new NotSupportedException("This compound instance layout cannot be resized safely.");
        if (instances.Count > 65535) throw new NotSupportedException("A collision body cannot exceed 65,535 instances.");
        var boxes = new Dictionary<int, Box>();
        var visited = new HashSet<int>();
        void ReadTree(int index)
        {
            if (index <= 0 || index >= oldNodes || !visited.Add(index)) throw new InvalidDataException("Invalid collision tree.");
            int at = nodes + index * 128; bool leaf = source[at + 112] != 0;
            for (int lane = 0; lane < 4; lane++)
            {
                int child = I32(source, at + 96 + lane * 4);
                if (child == (leaf ? -1 : 0)) continue;
                if (!leaf) { ReadTree(child); continue; }
                if ((uint)child >= oldCount || !boxes.TryAdd(child, ReadBox(source, at, lane))) throw new InvalidDataException("Invalid or shared collision leaf.");
            }
        }
        ReadTree(1);
        if (boxes.Count != oldCount) throw new NotSupportedException("Collision contains unused instance slots.");
        var bounds = instances.Select(i => boxes.TryGetValue(i.Source, out var box) ? box : throw new InvalidDataException("Invalid source collision instance.")).ToArray();
        var tree = new List<byte[]> { EmptyNode(false) };
        Box Union(IEnumerable<int> ids) => new(ids.Select(i => bounds[i].Min).Aggregate(Vector3.Min), ids.Select(i => bounds[i].Max).Aggregate(Vector3.Max));
        int Build(int[] ids)
        {
            bool leaf = ids.Length <= 4; int index = tree.Count;
            var node = EmptyNode(leaf); tree.Add(node);
            if (ids.Length == 0) return index;
            if (leaf)
                for (int lane = 0; lane < ids.Length; lane++) { PutBox(node, lane, bounds[ids[lane]]); Put32(node, 96 + lane * 4, ids[lane]); }
            else
            {
                var all = Union(ids); var size = all.Max - all.Min;
                int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
                var sorted = ids.OrderBy(i => (bounds[i].Min[axis] + bounds[i].Max[axis]) * .5f).ToArray();
                for (int lane = 0; lane < 4; lane++)
                {
                    var group = sorted[(lane * ids.Length / 4)..((lane + 1) * ids.Length / 4)];
                    PutBox(node, lane, Union(group)); Put32(node, 96 + lane * 4, Build(group));
                }
            }
            return index;
        }
        Build(Enumerable.Range(0, instances.Count).ToArray());
        int newNodes = shapes + instances.Count * 64, newTail = newNodes + tree.Count * 128, shift = newTail - tail;
        var result = new byte[checked(source.Length + shift)];
        source.AsSpan(0, shapes).CopyTo(result);
        source.AsSpan(tail).CopyTo(result.AsSpan(newTail));
        long estimate = 0;
        int compoundBits = instances.Count == 0 ? 0 : BitOperations.Log2((uint)instances.Count) + 1;
        for (int i = 0; i < instances.Count; i++)
        {
            int old = shapes + instances[i].Source * 64, at = shapes + i * 64;
            source.AsSpan(old, 64).CopyTo(result.AsSpan(at));
            long shape = old + 48 + I64(source, old + 48);
            if (shape < tail || shape + 64 > dataEnd) throw new NotSupportedException("Collision geometry is not in the preserved geometry block.");
            Put64(result, at + 48, shape + shift - (at + 48));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at + 44), instances[i].ReferenceTag);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(at + 46), ushort.MaxValue);
            int bits = source[checked((int)shape + 28)];
            if (bits > 30 || bits + compoundBits > 32) throw new NotSupportedException("Collision shape-key range is too large.");
            estimate += 1L << bits;
        }
        for (int i = 0; i < tree.Count; i++) tree[i].CopyTo(result, newNodes + i * 128);
        Put32(result, data + 80, instances.Count); Put32(result, data + 92, instances.Count);
        Put64(result, data + 232, newNodes - (data + 232)); Put32(result, data + 240, tree.Count);
        result[data + 28] = (byte)compoundBits;
        Put32(result, data + 152, checked((int)estimate));
        var overall = instances.Count == 0 ? new Box(Vector3.Zero, Vector3.Zero) : Union(Enumerable.Range(0, instances.Count));
        PutVector(result, data + 112, overall.Min); PutVector(result, data + 128, overall.Max);
        PutFloat(result, data + 144, Vector3.Max(Vector3.Abs(overall.Min), Vector3.Abs(overall.Max)).Length());
        int newItems = itemChunk + shift; bool shapeItem = false, nodeItem = false;
        for (int at = newItems + 8; at < newItems + Size(itemChunk); at += 12)
        {
            int offset = I32(result, at + 4), count = I32(result, at + 8);
            if (offset == shapes - data && count == oldCount)
            {
                if (shapeItem || count != oldCount) throw new InvalidDataException("Invalid instance ITEM.");
                shapeItem = true; Put32(result, at + 8, instances.Count);
            }
            else if (offset == nodes - data)
            {
                if (nodeItem || count != oldNodes) throw new InvalidDataException("Invalid tree ITEM.");
                nodeItem = true; Put32(result, at + 4, newNodes - data); Put32(result, at + 8, tree.Count);
            }
            else if (offset >= tail - data) Put32(result, at + 4, checked(offset + shift));
            else if (offset != 0) throw new NotSupportedException("Unexpected item inside the collision instance block.");
        }
        if (!shapeItem || !nodeItem) throw new InvalidDataException("Missing collision array ITEM.");
        void ChunkSize(int at, int size) => BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at),
            (BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(at)) & 0xc0000000) | checked((uint)size));
        ChunkSize(0, tagSize + shift); ChunkSize(dataChunk, Size(dataChunk) + shift);
        return result;
    }

    private static byte[] EmptyNode(bool leaf)
    {
        var b = new byte[128]; b[112] = leaf ? (byte)1 : (byte)0;
        for (int lane = 0; lane < 4; lane++) { PutBox(b, lane, new(new(float.MaxValue), new(float.MinValue))); Put32(b, 96 + lane * 4, leaf ? -1 : 0); }
        return b;
    }
    private static Box ReadBox(byte[] b, int at, int lane) => new(
        new(F(b, at + lane * 4), F(b, at + 32 + lane * 4), F(b, at + 64 + lane * 4)),
        new(F(b, at + 16 + lane * 4), F(b, at + 48 + lane * 4), F(b, at + 80 + lane * 4)));
    private static void PutBox(byte[] b, int lane, Box box)
    { for (int axis = 0; axis < 3; axis++) { PutFloat(b, axis * 32 + lane * 4, box.Min[axis]); PutFloat(b, axis * 32 + 16 + lane * 4, box.Max[axis]); } }
    internal static int I32(byte[] b, int at) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    internal static long I64(byte[] b, int at) => BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(at));
    private static float F(byte[] b, int at) => BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at));
    internal static void Put32(byte[] b, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), value);
    internal static void Put64(byte[] b, int at, long value) => BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(at), value);
    private static void PutFloat(byte[] b, int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(at), value);
    private static void PutVector(byte[] b, int at, Vector3 v) { for (int i = 0; i < 3; i++) PutFloat(b, at + i * 4, v[i]); }
}
