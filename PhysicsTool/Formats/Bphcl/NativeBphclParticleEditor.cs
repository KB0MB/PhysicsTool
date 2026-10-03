using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace HKCLTool;

// Append-only DATA allocations preserve unrecognized objects and existing ITEM identities.
// Particle indices are never renumbered, so authored constraints/skin bindings stay valid.
internal sealed partial class NativeBphclParticleEditor
{
    private readonly NativeBphclDocument _document;
    private readonly List<byte> _data;
    private readonly List<NativeBphclItem> _items;
    private readonly Dictionary<uint, HashSet<uint>> _patches;

    private NativeBphclParticleEditor(NativeBphclDocument document)
    {
        _document = document;
        var section = document.DataSection ?? throw new InvalidDataException("Missing BPHCL DATA.");
        _data = document.Bytes.AsSpan(section.PayloadOffset, section.PayloadSize).ToArray().ToList();
        _items = document.Items.ToList();
        _patches = document.InternalPatches.ToDictionary(p => p.TypeIndex, p => p.Offsets.ToHashSet());
    }

    public static NativeBphclDocument Add(NativeBphclDocument document, int clothIndex, ParticleEditRow? source)
    {
        if (!document.TypeNames.Contains("hclGatherAllVerticesOperator") &&
            document.Cloths[clothIndex].Operators.Any(o => o.ClassName == "hclCopyVerticesOperator"))
        {
            var types = NativeBphclTypeTable.AddEmbeddedTypes(document,
                new[] { NativeBphclReflectionCatalog.GetTypeIndex("hclGatherAllVerticesOperator") });
            var data = document.DataSection!;
            var items = document.Items.Select(item =>
            {
                var type = types.TargetToMerged[item.TypeIndex];
                return item with { TypeIndex = type, Flags = (item.Flags & 0xff000000u) | type };
            }).ToArray();
            document = NativeBphclDocument.Parse(NativeBphclTagFileBuilder.Rebuild(document,
                document.Bytes.AsSpan(data.PayloadOffset, data.PayloadSize), items,
                document.InternalPatches.Select(p => new NativeBphclPatch(types.TargetToMerged[p.TypeIndex], p.Offsets)).ToArray(),
                types.ReplacementSection));
        }
        var cloth = document.Cloths[clothIndex];
        var sim = cloth.SimCloths.FirstOrDefault() ?? throw new InvalidOperationException("This cloth has no particle data.");
        var count = sim.Particles.Count;
        if (count == 0 || count >= short.MaxValue)
            throw new InvalidOperationException("Adding particles requires a nonempty cloth with fewer than 32767 particles.");
        var sourceIndex = source is { Index: >= 0 } && source.Index < count ? source.Index : count - 1;
        var template = sim.Particles[sourceIndex];
        var row = source ?? new ParticleEditRow
        {
            X = template.Position.X, Y = template.Position.Y, Z = template.Position.Z, W = template.Position.W,
            Mass = template.Mass > 0 ? template.Mass : 0.1f, Radius = template.Radius,
            Friction = template.Friction, CollisionMask = sim.StaticCollisionMasks.ElementAtOrDefault(sourceIndex)
        };
        var edit = new NativeBphclParticleEditor(document);
        edit.Append(sim.DataOffset + 64, 16, sourceIndex, count);
        foreach (var pose in document.GetReferenceItemIndices(sim.DataOffset + 120))
            edit.Append(document.GetItem(pose).DataOffset + 32, 16, sourceIndex, count);
        if (document.GetReferenceItemIndices(sim.DataOffset + 120).Count == 0)
            throw new InvalidDataException("The cloth has no simulation pose to extend.");
        edit.AppendOptional(sim.DataOffset + 232, 4, sourceIndex, count);
        edit.AppendOptional(sim.DataOffset + 392, 1, sourceIndex, count);
        // New particles have no virtual faces or pinch constraints until explicitly authored.
        if (edit.Array(sim.DataOffset + 392) is { } pinch)
            edit._data[checked((int)pinch.DataOffset + count)] = 0;
        // These virtual-collision lookup tables are indexed by physical particle,
        // even though the virtual edge/triangle lists themselves do not grow.
        foreach (var member in new uint[] { 24, 240, 256, 272, 288 })
        {
            var field = sim.DataOffset + 432 + member;
            if (edit.Array(field) is not { Count: > 0 }) continue;
            edit.Append(field, 2, sourceIndex, count);
            var array = edit.Array(field)!;
            edit._data[checked((int)array.DataOffset + count * 2)] = 0xff;
            edit._data[checked((int)array.DataOffset + count * 2 + 1)] = 0xff;
        }

        var buffers = cloth.BufferDefinitions.Where(b => b.Type is 1 or 2 && b.SubType == sim.Index).ToArray();
        if (buffers.Length == 0)
            throw new InvalidDataException("No simulation buffers were found for this cloth.");
        foreach (var buffer in buffers)
        {
            if (buffer.VertexCount != count)
                throw new InvalidDataException("The simulation buffer has a nonstandard particle layout; it cannot be extended safely.");
            var writers = cloth.Operators.Where(o => o.OutputBufferIndex == buffer.Index).ToArray();
            if (writers.Length == 0 && (cloth.ObjectSpaceSkins.Count > 0 || cloth.BoneSpaceSkins.Count > 0))
                throw new InvalidDataException("The simulation buffer has no supported initialization operator.");
            foreach (var op in writers)
                edit.ExtendInitializer(op, count, sourceIndex);
            edit.UInt(buffer.DataOffset + 48, checked((uint)count + 1));
        }
        var added = edit.Finish();
        row = new ParticleEditRow
        {
            Index = count, Fixed = row.Fixed, X = row.X, Y = row.Y, Z = row.Z, W = row.W,
            Mass = row.Mass, Radius = row.Radius, Friction = row.Friction, CollisionMask = row.CollisionMask
        };
        return Update(added, clothIndex, new[] { row });
    }

    public static NativeBphclDocument MoveWithBones(NativeBphclDocument document, int clothIndex, IReadOnlyList<ParticleEditRow> rows)
    {
        var edit = new NativeBphclParticleEditor(document);
        var simulation = document.Cloths[clothIndex].SimCloths[0];
        var poses = document.GetReferenceItemIndices(simulation.DataOffset + 120);
        if (poses.Count == 0) throw new InvalidDataException("Cloth has no simulation poses.");
        foreach (var row in rows)
        {
            var original = simulation.Particles[row.Index].Position;
            var delta = new System.Numerics.Vector3(row.X - original.X, row.Y - original.Y, row.Z - original.Z);
            if (!float.IsFinite(delta.LengthSquared())) throw new ArgumentException("Particle movement must be finite.");
            foreach (var pose in poses)
            {
                var positions = edit.Array(document.GetItem(pose).DataOffset + 32);
                if (positions == null || positions.Count != simulation.Particles.Count)
                    throw new InvalidDataException("Cloth pose particle count is inconsistent.");
                var offset = positions.DataOffset + checked((uint)row.Index * 16);
                edit.Float(offset, edit.Single(offset) + delta.X);
                edit.Float(offset + 4, edit.Single(offset + 4) + delta.Y);
                edit.Float(offset + 8, edit.Single(offset + 8) + delta.Z);
            }
        }
        return edit.Finish();
    }

    public static NativeBphclDocument Update(NativeBphclDocument document, int clothIndex, IReadOnlyList<ParticleEditRow> rows)
    {
        if (rows.Count == 0) return document;
        var edit = new NativeBphclParticleEditor(document);
        var cloth = document.Cloths[clothIndex];
        var sim = cloth.SimCloths[0];
        var fixedIndices = sim.Particles.Where(p => p.Fixed).Select(p => p.Index).ToHashSet();
        var fixedChanged = false;
        foreach (var row in rows)
        {
            var original = sim.Particles.ElementAtOrDefault(row.Index)
                ?? throw new ArgumentOutOfRangeException(nameof(rows), "Particle index is outside the cloth.");
            var mass = row.Fixed ? 0 : row.Mass;
            if (!row.Fixed && mass == 0 && original.Fixed) mass = 0.1f;
            if (!float.IsFinite(mass) || mass < 0 || (!row.Fixed && (mass == 0 || !float.IsFinite(1 / mass))) ||
                !float.IsFinite(row.Radius) || row.Radius < 0 || !float.IsFinite(row.Friction) ||
                !float.IsFinite(row.X) || !float.IsFinite(row.Y) || !float.IsFinite(row.Z) || !float.IsFinite(row.W))
                throw new ArgumentException("Particle position and properties must be finite; a free particle needs positive mass and radius cannot be negative.");
            edit.Float(original.PhysicsDataOffset, mass);
            edit.Float(original.PhysicsDataOffset + 4, row.Fixed ? 0 : 1 / mass);
            edit.Float(original.PhysicsDataOffset + 8, row.Radius);
            edit.Float(original.PhysicsDataOffset + 12, row.Friction);
            // All authored poses must move by the same delta, not only the first pose.
            var delta = new System.Numerics.Vector4(row.X, row.Y, row.Z, row.W) - original.Position;
            foreach (var poseIndex in document.GetReferenceItemIndices(sim.DataOffset + 120))
            {
                var positions = edit.Array(document.GetItem(poseIndex).DataOffset + 32);
                if (positions is null || positions.Count != sim.Particles.Count)
                    throw new InvalidDataException("A simulation pose does not match the particle count.");
                var offset = positions.DataOffset + checked((uint)row.Index * 16);
                edit.Float(offset, edit.Single(offset) + delta.X);
                edit.Float(offset + 4, edit.Single(offset + 4) + delta.Y);
                edit.Float(offset + 8, edit.Single(offset + 8) + delta.Z);
                edit.Float(offset + 12, edit.Single(offset + 12) + delta.W);
            }
            var masks = edit.Array(sim.DataOffset + 232);
            if (masks is { Count: > 0 } && masks.Count != sim.Particles.Count)
                throw new InvalidDataException("The collision-mask array does not match the particle count.");
            if (masks is { Count: > 0 })
                edit.UInt(masks.DataOffset + checked((uint)row.Index * 4), row.CollisionMask);
            else if (row.CollisionMask != 0)
                throw new InvalidDataException("This cloth has no authored collision-mask array.");
            if (row.Fixed != original.Fixed)
            {
                fixedChanged = true;
                if (row.Fixed) fixedIndices.Add(row.Index); else fixedIndices.Remove(row.Index);
            }
        }
        if (fixedChanged)
        {
            edit.ReplaceArray(sim.DataOffset + 80, fixedIndices.Order().SelectMany(i => BitConverter.GetBytes(checked((ushort)i))).ToArray(),
                fixedIndices.Count, edit.Type("hkUint16"), edit.MemberType(sim.ItemIndex, "fixedParticles"));
            foreach (var op in cloth.Operators.Where(o => o.ClassName == "hclMoveParticlesOperator" && o.SimulationClothIndex == sim.Index))
            {
                var pairs = op.VertexParticlePairs.Where(p => fixedIndices.Contains(p.ParticleIndex)).ToList();
                foreach (var index in fixedIndices.Except(pairs.Select(p => (int)p.ParticleIndex)))
                {
                    var vertex = FindSourceVertex(cloth, sim.Index, index, op.ReferenceBufferIndex);
                    pairs.Add(new NativeBphclVertexParticlePair(checked((ushort)vertex), checked((ushort)index)));
                }
                edit.ReplaceArray(op.DataOffset + 72,
                    pairs.OrderBy(p => p.ParticleIndex).SelectMany(p => BitConverter.GetBytes(p.VertexIndex).Concat(BitConverter.GetBytes(p.ParticleIndex))).ToArray(),
                    pairs.Count, edit.Type("hclMoveParticlesOperator::VertexParticlePair"), edit.MemberType(op.ItemIndex, "vertexParticlePairs"));
            }
            if (fixedIndices.Count > 0 && (cloth.ObjectSpaceSkins.Count > 0 || cloth.BoneSpaceSkins.Count > 0) &&
                !cloth.Operators.Any(o => o.ClassName == "hclMoveParticlesOperator" && o.SimulationClothIndex == sim.Index))
                throw new InvalidDataException("This cloth has no fixed-particle binding operator.");
        }
        edit.UpdateStatistics(sim, fixedIndices);
        return edit.Finish();
    }

    internal static bool SupportsLinkEditing(NativeBphclConstraintSet set) =>
        !set.IsAntiPinch && set.ClassName is "hclStandardLinkConstraintSet" or "hclStretchLinkConstraintSet";

    internal static bool SupportsLinkRemoval(NativeBphclConstraintSet set) =>
        SupportsLinkEditing(set) || (!set.IsAntiPinch && set.ClassName == "hclBendLinkConstraintSet");

    internal static bool ExecutesSet(NativeBphclSimulateConfig config, int index) =>
        config.ConstraintExecution.Count == 0 || config.ConstraintExecution.Contains(index);

    internal static bool ContainsLink(NativeBphclConstraintSet set, int a, int b) =>
        set.Links.Any(link => (link.ParticleA == a && link.ParticleB == b) ||
            (set.ClassName != "hclStretchLinkConstraintSet" && link.ParticleA == b && link.ParticleB == a));

    public static NativeBphclDocument Link(NativeBphclDocument document, int clothIndex, IReadOnlyList<int> indices)
    {
        if (indices.Count != 2 || indices[0] == indices[1])
            throw new InvalidOperationException("BPHCL linking supports exactly two different particles. Creating virtual triangles is not supported yet.");
        var cloth = document.Cloths[clothIndex];
        var sim = cloth.SimCloths[0];
        if (indices.Any(i => i < 0 || i >= sim.Particles.Count))
            throw new ArgumentOutOfRangeException(nameof(indices));
        var sets = sim.ConstraintSets.Where(SupportsLinkEditing).ToArray();
        var set = sets.OrderBy(s => s.ClassName == "hclStandardLinkConstraintSet" ? 0 : 1)
            .FirstOrDefault(s => cloth.Operators.Where(o => o.SimulationClothIndex == sim.Index)
                .SelectMany(o => o.SimulateConfigs).Any(c => ExecutesSet(c, s.Index)))
            ?? throw new InvalidOperationException("This cloth has no active standard/stretch link set to extend.");
        if (ContainsLink(set, indices[0], indices[1]))
            return document;
        var length = System.Numerics.Vector3.Distance(
            new(sim.Particles[indices[0]].Position.X, sim.Particles[indices[0]].Position.Y, sim.Particles[indices[0]].Position.Z),
            new(sim.Particles[indices[1]].Position.X, sim.Particles[indices[1]].Position.Y, sim.Particles[indices[1]].Position.Z));
        if (!float.IsFinite(length) || length <= 0.000001f)
            throw new InvalidOperationException("Move the particles apart before linking them.");
        var edit = new NativeBphclParticleEditor(document);
        var type = edit.Type(set.ClassName + "::Link");
        edit.ValidateLinkLayout(type);
        var array = edit.Array(set.DataOffset + 40);
        var count = checked((int)(array?.Count ?? 0));
        var bytes = new byte[checked((count + 1) * 12)];
        if (array is not null)
            CollectionsMarshal.AsSpan(edit._data).Slice((int)array.DataOffset, count * 12).CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(count * 12), checked((ushort)indices[0]));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(count * 12 + 2), checked((ushort)indices[1]));
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(count * 12 + 4), length);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(count * 12 + 8), 1.0f);
        edit.ReplaceArray(set.DataOffset + 40, bytes, count + 1, type, edit.MemberType(set.ItemIndex, "links"));
        return edit.Finish();
    }

    public static NativeBphclDocument RemoveLink(NativeBphclDocument document, int clothIndex, ParticleRelationshipRow row)
    {
        var set = document.Cloths[clothIndex].SimCloths[0].ConstraintSets.ElementAtOrDefault(row.ConstraintSetIndex);
        if (set is null || !SupportsLinkRemoval(set) || row.LinkIndex < 0 || row.LinkIndex >= set.Links.Count)
            throw new InvalidOperationException("Only individual standard/stretch/bend links can currently be removed from BPHCL.");
        var edit = new NativeBphclParticleEditor(document);
        var type = edit.Type(set.ClassName + "::Link");
        var stride = set.ClassName == "hclBendLinkConstraintSet" ? edit.ValidateBendLinkLayout(type) : 12;
        if (stride == 12) edit.ValidateLinkLayout(type);
        var array = edit.Array(set.DataOffset + 40)!;
        var count = checked((int)array.Count);
        if (count != set.Links.Count || array.TypeIndex != type)
            throw new InvalidDataException("The native link array does not match its constraint definition.");
        var original = CollectionsMarshal.AsSpan(edit._data).Slice((int)array.DataOffset, count * stride);
        var bytes = new byte[(count - 1) * stride];
        original[..(row.LinkIndex * stride)].CopyTo(bytes);
        original[((row.LinkIndex + 1) * stride)..].CopyTo(bytes.AsSpan(row.LinkIndex * stride));
        edit.ReplaceArray(set.DataOffset + 40, bytes, count - 1, type, edit.MemberType(set.ItemIndex, "links"));
        return edit.Finish();
    }

    private void ValidateLinkLayout(uint type)
    {
        var layout = _document.TypeDefinitions.Single(t => t.TypeIndex == type);
        if (layout.Size != 12 || !new[] { ("particleA", 0u), ("particleB", 2u), ("restLength", 4u), ("stiffness", 8u) }
            .All(expected => layout.Members.Any(m => (m.Name == expected.Item1 || m.Name == "m_" + expected.Item1) && m.Offset == expected.Item2)))
            throw new InvalidDataException("The link record has an unsupported native layout.");
    }

    private int ValidateBendLinkLayout(uint type)
    {
        var layout = _document.TypeDefinitions.Single(t => t.TypeIndex == type);
        if (layout.Size != 20 || !new[] { ("particleA", 0u), ("particleB", 2u),
            ("bendMinLength", 4u), ("stretchMaxLength", 8u), ("bendStiffness", 12u), ("stretchStiffness", 16u) }
            .All(expected => layout.Members.Any(m => (m.Name == expected.Item1 || m.Name == "m_" + expected.Item1) && m.Offset == expected.Item2)))
            throw new InvalidDataException("The bend-link record has an unsupported native layout.");
        return 20;
    }

    private static int FindSourceVertex(NativeBphclCloth cloth, int simulation, int particle, int? inputBuffer)
    {
        var outputs = cloth.BufferDefinitions.Where(b => b.Type == 1 && b.SubType == simulation).Select(b => b.Index).ToHashSet();
        var op = cloth.Operators.FirstOrDefault(o => o.InputBufferIndex == inputBuffer && o.OutputBufferIndex is int output && outputs.Contains(output));
        return op?.ClassName switch
        {
            "hclGatherAllVerticesOperator" when particle < op.VertexInputFromVertexOutput.Count && op.VertexInputFromVertexOutput[particle] >= 0
                => op.VertexInputFromVertexOutput[particle],
            "hclCopyVerticesOperator" when particle >= op.StartVertexOut.GetValueOrDefault() &&
                particle < op.StartVertexOut.GetValueOrDefault() + op.VertexCount.GetValueOrDefault()
                => particle - op.StartVertexOut.GetValueOrDefault() + op.StartVertexIn.GetValueOrDefault(),
            _ => throw new InvalidDataException("No unambiguous skinned reference vertex was found for this fixed particle.")
        };
    }

    private void ExtendInitializer(NativeBphclOperatorLayout op, int count, int source)
    {
        if (op.ClassName == "hclGatherAllVerticesOperator")
        {
            Append(op.DataOffset + 72, 2, source, count);
            return;
        }
        if (op.ClassName != "hclCopyVerticesOperator" || op.StartVertexOut != 0 || op.VertexCount != count)
            throw new InvalidDataException($"Particle creation cannot safely extend the {op.ClassName} buffer initializer.");
        var inputStart = op.StartVertexIn.GetValueOrDefault();
        if (inputStart + count > short.MaxValue)
            throw new InvalidDataException("Reference vertex indices exceed Havok's signed 16-bit gather range.");
        var type = Type("hclGatherAllVerticesOperator");
        var definition = _document.TypeDefinitions.Single(t => t.TypeIndex == type);
        if (definition.Size != 104)
            throw new InvalidDataException("Unsupported gather-operator layout.");
        var old = _items[op.ItemIndex];
        var bytes = new byte[104];
        CollectionsMarshal.AsSpan(_data).Slice(checked((int)old.DataOffset), 72).CopyTo(bytes);
        bytes[96] = _data[checked((int)old.DataOffset + 92)];
        var offset = Allocate(bytes);
        foreach (var (patchType, offsets) in _patches.ToArray())
            foreach (var patch in offsets.Where(p => p >= old.DataOffset && p < old.DataOffset + 72).ToArray())
                offsets.Add(offset + patch - old.DataOffset);
        _items[op.ItemIndex] = old with { DataOffset = offset, TypeIndex = type, Flags = (old.Flags & 0xff000000u) | type };
        UInt(offset + 88, checked((uint)op.InputBufferIndex!.Value));
        UInt(offset + 92, checked((uint)op.OutputBufferIndex!.Value));
        var mapping = Enumerable.Range(inputStart, count).Append(inputStart + source)
            .SelectMany(i => BitConverter.GetBytes(checked((short)i))).ToArray();
        ReplaceArray(offset + 72, mapping, count + 1, Type("hkInt16"),
            definition.Members.Single(m => m.Name is "vertexInputFromVertexOutput" or "m_vertexInputFromVertexOutput").TypeIndex);
    }

    private void UpdateStatistics(NativeBphclSimCloth sim, HashSet<int> fixedIndices)
    {
        var particles = Array(sim.DataOffset + 64)!;
        var masks = Array(sim.DataOffset + 232);
        float mass = 0, radius = 0;
        uint landscape = 0;
        for (uint i = 0; i < particles.Count; i++)
        {
            mass += Single(particles.DataOffset + i * 16);
            radius = Math.Max(radius, Single(particles.DataOffset + i * 16 + 8));
            if (!fixedIndices.Contains((int)i) && masks is { Count: > 0 } && (Read(masks.DataOffset + i * 4) & 0x80000000u) != 0) landscape++;
        }
        if (!float.IsFinite(mass))
            throw new ArgumentException("The total particle mass exceeds the supported range.");
        Float(sim.DataOffset + 264, mass);
        Float(sim.DataOffset + 224, radius);
        UInt(sim.DataOffset + 344, landscape);
    }

    private NativeBphclItem? Array(uint field)
    {
        var index = Read(field);
        return index == 0 ? null : _items[checked((int)index)];
    }

    private void AppendOptional(uint field, int stride, int source, int expected)
    {
        if (Array(field) is { Count: > 0 }) Append(field, stride, source, expected);
    }

    private void Append(uint field, int stride, int source, int expected)
    {
        var array = Array(field);
        if (array is null || array.Count != expected)
            throw new InvalidDataException("A BPHCL particle-related array has an unexpected length.");
        var bytes = new byte[checked((expected + 1) * stride)];
        CollectionsMarshal.AsSpan(_data).Slice(checked((int)array.DataOffset), expected * stride).CopyTo(bytes);
        bytes.AsSpan(source * stride, stride).CopyTo(bytes.AsSpan(expected * stride));
        ReplaceArray(field, bytes, expected + 1, array.TypeIndex, _patches.Single(p => p.Value.Contains(field)).Key);
    }

    private void ReplaceArray(uint field, byte[] bytes, int count, uint elementType, uint arrayType)
    {
        if (count == 0)
        {
            // A null array has no ITEM target and therefore no pointer fixup.
            UInt(field, 0); UInt(field + 4, 0); UInt(field + 8, 0);
            UInt(field + 12, Read(field + 12) & 0xc0000000u);
            foreach (var offsets in _patches.Values) offsets.Remove(field);
            return;
        }
        var offset = Allocate(bytes);
        var index = _items.Count;
        _items.Add(new NativeBphclItem(0x20000000u | elementType, elementType, offset, checked((uint)count)));
        UInt(field, count == 0 ? 0 : checked((uint)index));
        UInt(field + 4, 0);
        UInt(field + 8, checked((uint)count));
        UInt(field + 12, (Read(field + 12) & 0xc0000000u) | checked((uint)count));
        if (!_patches.TryGetValue(arrayType, out var patches)) _patches[arrayType] = patches = new HashSet<uint>();
        patches.Add(field);
    }

    private uint Allocate(byte[] bytes)
    {
        while (_data.Count % 16 != 0) _data.Add(0);
        var offset = checked((uint)_data.Count);
        _data.AddRange(bytes);
        return offset;
    }
    private uint Type(string name) => _document.TypeDefinitions.FirstOrDefault(t => _document.GetTypeName(t.TypeIndex) == name)?.TypeIndex
        ?? throw new InvalidDataException($"This file does not contain the required {name} type metadata.");
    private uint MemberType(int item, string name) => _document.TypeDefinitions.Single(t => t.TypeIndex == _items[item].TypeIndex)
        .Members.Single(m => m.Name == name || m.Name == "m_" + name).TypeIndex;
    private uint Read(uint offset) => BinaryPrimitives.ReadUInt32LittleEndian(CollectionsMarshal.AsSpan(_data).Slice(checked((int)offset), 4));
    private float Single(uint offset) => BitConverter.UInt32BitsToSingle(Read(offset));
    private void UInt(uint offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(_data).Slice(checked((int)offset), 4), value);
    private void Float(uint offset, float value) => UInt(offset, BitConverter.SingleToUInt32Bits(value));
    private NativeBphclDocument Finish() => NativeBphclDocument.Parse(NativeBphclTagFileBuilder.Rebuild(_document,
        CollectionsMarshal.AsSpan(_data), _items, _patches.Select(p => new NativeBphclPatch(p.Key, p.Value.Order().ToArray())).ToArray()));
}
