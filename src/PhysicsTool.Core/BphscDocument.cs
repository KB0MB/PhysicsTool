using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using HKCLTool;
using ZstdSharp;

namespace PhysicsTool.Core;

// Fixed-size edits preserve opaque Havok shapes and every unrecognized byte.
internal sealed partial class BphscDocument
{
    internal const int HeaderSize = 780, ImageSize = 19120;
    private readonly byte[] _bytes;
    private readonly int _image, _links, _hashes, _indices;
    private readonly int _hashSize, _indexSize;
    internal string Name => Text(_image + 0x4a20, 128);
    internal IReadOnlyList<BphscBody> Bodies { get; private set; } = [];
    internal IReadOnlyList<BphscActorLink> ActorLinks { get; private set; } = [];
    internal IReadOnlyList<BphscWater> Water { get; private set; } = [];
    internal IReadOnlyList<BphscSection> Sections { get; private set; } = [];
    internal int CompoundCount { get; }
    internal int ExternalMeshCount { get; }
    internal byte[] Snapshot() => (byte[])_bytes.Clone();

    internal static bool IsPath(string path) => path.EndsWith(".bphsc", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bphsc.zs", StringComparison.OrdinalIgnoreCase);

    internal static BphscDocument Load(string path, IEnumerable<string>? roots = null)
    {
        return Parse(ReadResource(path, roots));
    }

    internal static byte[] ReadResource(string path, IEnumerable<string>? roots = null)
    {
        if (new FileInfo(path).Length > 256 * 1024 * 1024) throw new InvalidDataException("Resource exceeds the 256 MiB input limit.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == 0xfd2fb528 && Decompressor.GetDecompressedSize(bytes) > 512 * 1024 * 1024)
            throw new InvalidDataException("Resource exceeds the 512 MiB decoded limit or has an unknown decoded size.");
        return new TotkResourceCompression().Decode(bytes, (roots ?? []).Concat(TotkResourceCompression.Roots(path)));
    }

    internal static BphscDocument Parse(byte[] bytes) => new(bytes);

    private BphscDocument(byte[] bytes)
    {
        if (bytes.Length < HeaderSize || bytes.Length > 512 * 1024 * 1024 || !bytes.AsSpan(0, 6).SequenceEqual("Phive\0"u8))
            throw new InvalidDataException("Not a supported BPHSC file.");
        _bytes = (byte[])bytes.Clone();
        if (U16(6) != 2 || U16(8) != 0xfeff || U16(10) != 0x6002)
            throw new InvalidDataException("Unsupported BPHSC version or byte order; no data was changed.");
        _image = Region(12, 396); _links = Region(16, 400); _hashes = Region(20, 404); _indices = Region(24, 408);
        if (I32(396) != ImageSize) throw new InvalidDataException("Unsupported BPHSC scene image layout.");
        _hashSize = I32(404); _indexSize = I32(408);
        for (int i = 4; i < 8; i++) Region(12 + i * 4, 396 + i * 4);
        var linkCount = Count(_image + 0x4010, 65535);
        RequireSize(400, checked(linkCount * 32));
        RequireSize(404, checked(linkCount * 16));
        RequireSize(408, checked(linkCount * 4));
        RequireSize(412, checked(Count(_image + 0x4014, 1_000_000) * 20));
        RequireSize(416, checked(Count(_image + 0x4018, 1_000_000) * 16));
        RequireSize(420, checked(Count(_image + 0x401c, 1_000_000) * 8));
        var entities = Count(_image + 0x4008, 64);
        var sensors = Count(_image + 0x400c, 64);
        CompoundCount = checked(entities + sensors);
        if (CompoundCount > 64) throw new InvalidDataException("Too many BPHSC compound bodies.");
        var boxes = Count(_image + 0x4aa0, 1_000_000);
        var cylinders = Count(_image + 0x4aa4, 1_000_000);
        var falls = Count(_image + 0x4aa8, 1_000_000);
        ExternalMeshCount = Count(_image + 0x4aac, 80);
        var expectedSections = CompoundCount + (boxes > 0 ? 1 : 0) + (cylinders > 0 ? 1 : 0) + (falls > 0 ? 1 : 0)
            + (ExternalMeshCount > 0 ? ExternalMeshCount + 1 : 0);
        if (expectedSections > 85) throw new InvalidDataException("Too many BPHSC image sections.");
        var sections = new List<BphscSection>();
        for (int i = 0; i < 85; i++)
        {
            int offset = Region(56 + i * 4, 440 + i * 4), size = I32(440 + i * 4);
            if (i < expectedSections && size == 0) throw new InvalidDataException("Missing BPHSC image section.");
            if (size != 0) sections.Add(new(i, offset, size, i < CompoundCount ? "Compound collision" : "Scene data"));
            if (i < CompoundCount)
            {
                if (size < 8 || !_bytes.AsSpan(offset + 4, 4).SequenceEqual("TAG0"u8)) throw new InvalidDataException("Invalid compound Havok tag file.");
                int tagSize = checked((int)(BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(offset)) & 0x3fffffff));
                if (tagSize < 8 || tagSize > size) throw new InvalidDataException("Truncated compound Havok tag file.");
            }
        }
        Sections = sections;
        var bodyIds = new SortedSet<int>();
        for (int i = 0; i < entities; i++) bodyIds.Add(_bytes[_image + i * 4 + 2]);
        for (int i = 0; i < sensors; i++) bodyIds.Add(_bytes[_image + 0x2000 + i * 4 + 2]);
        // Some moving groups have actor links but no baked compound shape of their own.
        for (int i = 0; i < 64; i++) if (U64(_image + 0x4020 + i * 40 + 32) != 0) bodyIds.Add(i);
        var water = new List<BphscWater>();
        int slot = CompoundCount;
        foreach (var entry in new[] { ("Box", boxes, 72), ("Cylinder", cylinders, 88), ("Waterfall", falls, 88) })
        {
            if (entry.Item2 == 0) continue;
            int offset = I32(56 + slot * 4), size = I32(440 + slot * 4);
            if ((long)entry.Item2 * entry.Item3 > size) throw new InvalidDataException("Truncated BPHSC water section.");
            for (int i = 0; i < entry.Item2; i++)
            {
                int at = offset + i * entry.Item3, body = U16(at);
                if (body >= 64) throw new InvalidDataException("Invalid BPHSC water body index.");
                bodyIds.Add(body);
                water.Add(ReadWater(water.Count, entry.Item1, at));
            }
            slot++;
        }
        if (bodyIds.Any(i => i >= 64)) throw new InvalidDataException("Invalid BPHSC body-info index.");
        Bodies = bodyIds.Select(ReadBody).ToArray();
        Water = water;
        ActorLinks = Enumerable.Range(0, linkCount).Select(ReadLink).ToArray();
        ValidateNonOverlappingRegions();
    }

    private void ValidateNonOverlappingRegions()
    {
        var regions = new List<(int Offset, int Size)> { (0, HeaderSize) };
        for (int i = 0; i < 8; i++) if (I32(396 + i * 4) > 0) regions.Add((I32(12 + i * 4), I32(396 + i * 4)));
        // Terrain arrays carry 64-bit lengths inside TeraWater rather than header sizes.
        int water = I32(40);
        if (I32(424) < 80) throw new InvalidDataException("Truncated BPHSC terrain-water header.");
        for (int i = 0; i < 3; i++)
        {
            ulong size = U64(water + 32 + i * 8);
            if (size > int.MaxValue) throw new InvalidDataException("BPHSC terrain data is too large.");
            int at = I32(44 + i * 4);
            Bounds(at, (int)size);
            if (size > 0) regions.Add((at, (int)size));
        }
        regions.AddRange(Sections.Select(s => (s.Offset, s.Size)));
        regions.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        for (int i = 1; i < regions.Count; i++)
            if ((long)regions[i - 1].Offset + regions[i - 1].Size > regions[i].Offset)
                throw new InvalidDataException("Overlapping BPHSC sections are not supported for editing.");
    }

    internal void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => c < 32 || c > 126) || Encoding.ASCII.GetByteCount(name) > 127)
            throw new ArgumentException("Use a compound name of 1-127 printable ASCII characters.");
        _bytes.AsSpan(_image + 0x4a20, 128).Clear();
        Encoding.ASCII.GetBytes(name, _bytes.AsSpan(_image + 0x4a20, 127));
    }

    internal void SetBody(int index, Vector3 position, Vector3 rotation)
    {
        if (!Bodies.Any(b => b.Index == index)) throw new ArgumentOutOfRangeException(nameof(index));
        Finite(position); Finite(rotation);
        int at = _image + 0x4020 + index * 40;
        PutVector(at, position); PutVector(at + 12, rotation);
        Bodies = Bodies.Select(b => b.Index == index ? ReadBody(index) : b).ToArray();
    }

    internal void SetActorHash(int index, ulong hash)
    {
        if ((uint)index >= ActorLinks.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (hash == 0 || ActorLinks.Any(l => l.Index != index && l.Hash == hash))
            throw new ArgumentException("Actor hashes must be nonzero and unique.");
        if (ActorLinks[index].Hash == hash) return;
        // Validate the whole hash set before writing either the link or its open-addressed lookup.
        var changed = ActorLinks.Select(l => l.Index == index ? l with { Hash = hash } : l).ToArray();
        if (changed.Any(l => l.Hash == 0) || changed.Select(l => l.Hash).Distinct().Count() != changed.Length)
            throw new InvalidDataException("Existing actor hashes are not unique; hash editing is unavailable.");
        int capacity = checked(changed.Length * 2);
        var occupied = new bool[capacity];
        var hashes = new byte[_hashSize]; var indices = new byte[_indexSize];
        foreach (var link in changed)
        {
            int slot = (int)(link.Hash % (ulong)capacity);
            while (occupied[slot]) slot = (slot + 1) % capacity;
            occupied[slot] = true;
            BinaryPrimitives.WriteUInt64LittleEndian(hashes.AsSpan(slot * 8), link.Hash);
            BinaryPrimitives.WriteUInt16LittleEndian(indices.AsSpan(slot * 2), checked((ushort)link.Index));
        }
        BinaryPrimitives.WriteUInt64LittleEndian(_bytes.AsSpan(_links + index * 32 + 8), hash);
        hashes.CopyTo(_bytes, _hashes); indices.CopyTo(_bytes, _indices); ActorLinks = changed;
    }

    internal void SetGroup(int bodyIndex, ushort groupId, ulong guid)
    {
        var body = Bodies.Single(b => b.Index == bodyIndex);
        if (body.PlacementGroupId == 0 || body.GroupId == 0)
            throw new InvalidOperationException("Only existing moving groups can be reassigned. Creating a group needs collision rebaking.");
        if (groupId == 0 || guid == 0 || Bodies.Any(b => b.Index != bodyIndex && (b.GroupId == groupId || b.PlacementGroupId == guid)))
            throw new ArgumentException("Choose a nonzero GroupID and GroupGUID not used by another body.");
        var entries = BodyEntries().Where(at => _bytes[at + 2] == bodyIndex).ToArray();
        if (entries.Any(at => U16(at) != body.GroupId) || ActorLinks.Any(l => l.BodyIndex == bodyIndex && l.GroupId != body.GroupId))
            throw new InvalidDataException("This group's existing links disagree; group editing is unavailable.");
        foreach (int at in entries) BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(at), groupId);
        foreach (var link in ActorLinks.Where(l => l.BodyIndex == bodyIndex))
            BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(_links + link.Index * 32 + 20), groupId);
        BinaryPrimitives.WriteUInt64LittleEndian(_bytes.AsSpan(_image + 0x4020 + bodyIndex * 40 + 32), guid);
        Bodies = Bodies.Select(b => b.Index == bodyIndex ? ReadBody(bodyIndex) : b).ToArray();
        ActorLinks = Enumerable.Range(0, ActorLinks.Count).Select(ReadLink).ToArray();
    }

    private IEnumerable<int> BodyEntries()
    {
        for (int i = 0; i < I32(_image + 0x4008); i++) yield return _image + i * 4;
        for (int i = 0; i < I32(_image + 0x400c); i++) yield return _image + 0x2000 + i * 4;
    }

    private BphscActorLink ReadLink(int i)
    {
        int at = _links + i * 32;
        return new(i, U64(at + 8), U32(at), U32(at + 4), U16(at + 24), _bytes[at + 27] != 0, U32(at + 20));
    }

    internal void SetWater(int index, Vector3 position, Vector3 rotation, Vector3 size, float flowSpeed, uint fluidType)
    {
        if ((uint)index >= Water.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var water = Water[index];
        Finite(position); Finite(rotation); Finite(size);
        if (!float.IsFinite(flowSpeed)) throw new ArgumentException("Flow speed must be finite.");
        if (water.Kind != "Waterfall" && (size.X <= 0 || size.Y <= 0 || size.Z <= 0)) throw new ArgumentException("Water dimensions must be positive.");
        PutVector(water.Offset + 36, position); PutVector(water.Offset + 24, rotation);
        if (water.Kind != "Waterfall") { PutVector(water.Offset + 12, size); PutFloat(water.Offset + 4, flowSpeed); }
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(water.Offset + 48), fluidType);
        Water = Water.Select(w => w.Index == index ? ReadWater(index, w.Kind, w.Offset) : w).ToArray();
    }

    internal void SaveCopy(string path, string source)
    {
        if (!IsPath(path)) throw new ArgumentException("Choose .bphsc or .bphsc.zs.");
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(source), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Save to a separate file to preserve the original BPHSC.");
        _ = Parse(_bytes);
        byte[] output = _bytes;
        if (path.EndsWith(".zs", StringComparison.OrdinalIgnoreCase))
        {
            using var compressor = new Compressor(9);
            output = compressor.Wrap(_bytes).ToArray();
        }
        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".bphsc-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(temporary, output);
            var reopened = Load(temporary);
            if (!reopened._bytes.AsSpan().SequenceEqual(_bytes)) throw new InvalidDataException("BPHSC write verification failed.");
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal byte[] ExtractSection(int index)
    {
        var section = Sections.Single(s => s.Index == index);
        return _bytes.AsSpan(section.Offset, section.Size).ToArray();
    }

    private BphscBody ReadBody(int i)
    {
        int at = _image + 0x4020 + i * 40;
        ushort group = BodyEntries().Where(e => _bytes[e + 2] == i).Select(U16).FirstOrDefault();
        if (group == 0)
        {
            for (int link = 0; link < I32(_image + 0x4010); link++)
                if (U16(_links + link * 32 + 24) == i && U32(_links + link * 32 + 20) != 0)
                { group = checked((ushort)U32(_links + link * 32 + 20)); break; }
        }
        return new(i, Vector(at), Vector(at + 12), U64(at + 32), group);
    }
    private BphscWater ReadWater(int i, string kind, int at) => new(i, kind, at, U16(at), Vector(at + 36), Vector(at + 24), Vector(at + 12), Float(at + 4), U32(at + 48));
    private void RequireSize(int at, int needed) { if (I32(at) < needed) throw new InvalidDataException("Truncated BPHSC table."); }
    private int Count(int at, int max) { int n = I32(at); if (n < 0 || n > max) throw new InvalidDataException("Invalid BPHSC entry count."); return n; }
    private int Region(int offsetField, int sizeField) { int o = I32(offsetField), n = I32(sizeField); Bounds(o, n); return o; }
    private void Bounds(int at, int size) { if (at < 0 || size < 0 || (long)at + size > _bytes.Length) throw new InvalidDataException("BPHSC section lies outside the file."); }
    private ushort U16(int at) => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(at, 2));
    private uint U32(int at) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(at, 4));
    private ulong U64(int at) => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(at, 8));
    private int I32(int at) => checked((int)U32(at));
    private float Float(int at) => BinaryPrimitives.ReadSingleLittleEndian(_bytes.AsSpan(at, 4));
    private Vector3 Vector(int at) => new(Float(at), Float(at + 4), Float(at + 8));
    private void PutFloat(int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(_bytes.AsSpan(at, 4), value);
    private void PutVector(int at, Vector3 v) { PutFloat(at, v.X); PutFloat(at + 4, v.Y); PutFloat(at + 8, v.Z); }
    private string Text(int at, int size) { var span = _bytes.AsSpan(at, size); int end = span.IndexOf((byte)0); return Encoding.UTF8.GetString(end < 0 ? span : span[..end]); }
    private static void Finite(Vector3 v) { if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)) throw new ArgumentException("Coordinates must be finite."); }
}

internal sealed record BphscBody(int Index, Vector3 Position, Vector3 Rotation, ulong PlacementGroupId, ushort GroupId);
internal sealed record BphscActorLink(int Index, ulong Hash, uint FirstInstance, uint EndInstance, ushort BodyIndex, bool Valid, uint GroupId);
internal sealed record BphscWater(int Index, string Kind, int Offset, ushort BodyIndex, Vector3 Position, Vector3 Rotation, Vector3 Size, float FlowSpeed, uint FluidType);
internal sealed record BphscSection(int Index, int Offset, int Size, string Kind);
