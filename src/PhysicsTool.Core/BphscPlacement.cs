using System.Buffers.Binary;
using System.Numerics;

namespace PhysicsTool.Core;

internal sealed partial class BphscDocument
{
    // Transform existing shape instances without rebuilding or discarding their geometry.
    // The SIMD tree retains its topology; every affected leaf and ancestor is refitted.
    internal BphscDocument MoveActors(IReadOnlyDictionary<ulong, Vector3> moves)
        => TransformActors(moves.ToDictionary(p => p.Key, p => Matrix4x4.CreateTranslation(p.Value)));

    internal BphscDocument TransformActors(IReadOnlyDictionary<ulong, Matrix4x4> transforms)
    {
        var result = Parse(_bytes);
        result.TransformActorsInPlace(transforms);
        return result;
    }

    private void TransformActorsInPlace(IReadOnlyDictionary<ulong, Matrix4x4> moves)
    {
        if (moves.Count == 0) return;
        if (ExternalMeshCount != 0 || Water.Count != 0)
            throw new NotSupportedException("This compound contains external meshes or water. Placement collision rebuilding is not supported for that layout yet.");
        var entries = BodyEntries().ToArray();
        var changes = new Dictionary<int, Dictionary<int, Matrix4x4>>();
        foreach (var link in ActorLinks)
        {
            if (!moves.TryGetValue(link.Hash, out var worldDelta) || worldDelta == Matrix4x4.Identity) continue;
            if (link.FirstInstance == uint.MaxValue) continue;
            if (link.EndInstance < link.FirstInstance || link.EndInstance >= U32(_image + 0x4014))
                throw new InvalidDataException("Invalid actor collision-instance range.");
            var body = Bodies.Single(b => b.Index == link.BodyIndex);
            var bodyWorld = Matrix4x4.CreateRotationX(body.Rotation.X) * Matrix4x4.CreateRotationY(body.Rotation.Y) * Matrix4x4.CreateRotationZ(body.Rotation.Z)
                * Matrix4x4.CreateTranslation(body.Position);
            if (!Matrix4x4.Invert(bodyWorld, out var inverse)) throw new InvalidDataException("Invalid collision body transform.");
            var delta = ShrineTransform.SameLinear(worldDelta, Matrix4x4.Identity)
                ? Matrix4x4.CreateTranslation(Vector3.TransformNormal(worldDelta.Translation, inverse))
                : bodyWorld * worldDelta * inverse;
            for (uint i = link.FirstInstance; i <= link.EndInstance; i++)
            {
                int at = checked(I32(28) + (int)i * 20);
                var slots = Enumerable.Range(0, entries.Length).Where(s => _bytes[entries[s] + 2] == link.BodyIndex && _bytes[entries[s] + 3] == _bytes[at + 5]).ToArray();
                if (slots.Length != 1) throw new InvalidDataException("Ambiguous actor collision body/layer reference.");
                int slot = slots[0], instance = U16(at + 10);
                if (!changes.TryGetValue(slot, out var instances)) changes[slot] = instances = new();
                if (!instances.TryAdd(instance, delta)) throw new InvalidDataException("Shared collision instance has more than one moved owner.");
            }
        }
        foreach (var (slot, instances) in changes) TransformCompound(slot, instances);
    }

    private void TransformCompound(int slot, Dictionary<int, Matrix4x4> moves)
    {
        var section = Sections.Single(s => s.Index == slot);
        int end = checked(section.Offset + section.Size), data = -1;
        for (int at = section.Offset + 8; at < end;)
        {
            Bounds(at, 8);
            int size = checked((int)(BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(at)) & 0x3fffffff));
            if (size < 8 || (long)at + size > end) throw new InvalidDataException("Invalid collision tag chunk.");
            if (_bytes.AsSpan(at + 4, 4).SequenceEqual("DATA"u8)) { data = at + 8; end = at + size; break; }
            at += size;
        }
        if (data < 0 || data + 272 > end || _bytes[data + 256] != 2)
            throw new NotSupportedException("Only SIMD-tree compound collision can currently be rebuilt.");
        (int Start, int Count) Array(int field, int stride)
        {
            long start = field + BinaryPrimitives.ReadInt64LittleEndian(_bytes.AsSpan(field));
            int count = I32(field + 8);
            if (start < data || start + (long)count * stride > end) throw new InvalidDataException("Invalid collision relative array.");
            return (checked((int)start), count);
        }
        var shapes = Array(data + 72, 64); var nodes = Array(data + 232, 128);
        if (nodes.Count < 2) throw new InvalidDataException("Missing collision tree root.");
        foreach (var (index, delta) in moves)
        {
            if ((uint)index >= shapes.Count) throw new InvalidDataException("Invalid collision instance index.");
            int at = shapes.Start + index * 64;
            if (ShrineTransform.SameLinear(delta, Matrix4x4.Identity))
            {
                var translated = Vector(at + 16) + delta.Translation;
                Finite(translated); PutVector(at + 16, translated);
                _bytes[at + 28] = (byte)((_bytes[at + 28] & ~2) | (translated != Vector3.Zero ? 2 : 0));
                continue;
            }
            var q = new Quaternion(Float(at), Float(at + 4), Float(at + 8), Float(at + 12));
            if (!float.IsFinite(q.LengthSquared()) || q.LengthSquared() < 1e-10f) throw new InvalidDataException("Invalid collision instance rotation.");
            var matrix = Matrix4x4.CreateScale(Vector(at + 32)) * Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q))
                * Matrix4x4.CreateTranslation(Vector(at + 16));
            ShrineTransform.Decompose(matrix * delta, out var scale, out var rotation, out var position);
            PutFloat(at, rotation.X); PutFloat(at + 4, rotation.Y); PutFloat(at + 8, rotation.Z); PutFloat(at + 12, rotation.W);
            PutVector(at + 16, position); PutVector(at + 32, scale);
            // Instance transform bits: translation=2, rotation=4, scale=8.
            // Preserve the shape/instance flags outside these cached transform bits.
            _bytes[at + 28] = (byte)((_bytes[at + 28] & ~14) | (position != Vector3.Zero ? 2 : 0)
                | (rotation.X != 0 || rotation.Y != 0 || rotation.Z != 0 ? 4 : 0) | (scale != Vector3.One ? 8 : 0));
        }
        var visited = new HashSet<int>(); var moved = new HashSet<int>();
        (Vector3 Min, Vector3 Max) Refit(int index, int depth)
        {
            if (index <= 0 || index >= nodes.Count || depth > 128 || !visited.Add(index)) throw new InvalidDataException("Invalid collision tree topology.");
            int node = nodes.Start + index * 128;
            bool leaf = _bytes[node + 112] != 0;
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            for (int lane = 0; lane < 4; lane++)
            {
                uint child = U32(node + 96 + lane * 4);
                if (child == (leaf ? uint.MaxValue : 0)) continue;
                Vector3 lo = new(Float(node + lane * 4), Float(node + 32 + lane * 4), Float(node + 64 + lane * 4));
                Vector3 hi = new(Float(node + 16 + lane * 4), Float(node + 48 + lane * 4), Float(node + 80 + lane * 4));
                if (leaf)
                {
                    if (child >= shapes.Count) throw new InvalidDataException("Invalid collision tree leaf.");
                    if (moves.TryGetValue((int)child, out var delta)) { (lo, hi) = ShrineTransform.Bounds(lo, hi, delta); moved.Add((int)child); }
                }
                else (lo, hi) = Refit(checked((int)child), depth + 1);
                Finite(lo); Finite(hi);
                if (lo.X > hi.X || lo.Y > hi.Y || lo.Z > hi.Z) throw new InvalidDataException("Invalid collision bounds.");
                for (int axis = 0; axis < 3; axis++) { PutFloat(node + axis * 32 + lane * 4, lo[axis]); PutFloat(node + axis * 32 + 16 + lane * 4, hi[axis]); }
                min = Vector3.Min(min, lo); max = Vector3.Max(max, hi);
            }
            return (min, max);
        }
        var bounds = Refit(1, 0);
        if (moved.Count != moves.Count) throw new InvalidDataException("A moved shape is missing from the collision tree.");
        PutVector(data + 112, bounds.Min); PutVector(data + 128, bounds.Max);
        PutFloat(data + 144, Vector3.Max(Vector3.Abs(bounds.Min), Vector3.Abs(bounds.Max)).Length());
    }
}
