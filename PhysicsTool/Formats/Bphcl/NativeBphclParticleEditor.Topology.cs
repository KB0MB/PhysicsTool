using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace HKCLTool;

internal sealed partial class NativeBphclParticleEditor
{
    public static NativeBphclDocument AddTriangle(NativeBphclDocument document, int clothIndex, IReadOnlyList<int> indices)
    {
        var cloth = document.Cloths[clothIndex]; var sim = cloth.SimCloths.Single();
        if (indices.Count != 3 || indices.Distinct().Count() != 3 || indices.Any(i => i < 0 || i >= sim.Particles.Count))
            throw new ArgumentException("A triangle requires three different particles.");
        var p = indices.Select(i => sim.Particles[i].Position).Select(v => new System.Numerics.Vector3(v.X, v.Y, v.Z)).ToArray();
        var area = System.Numerics.Vector3.Cross(p[1] - p[0], p[2] - p[0]).LengthSquared();
        if (!float.IsFinite(area) || area < 1e-12f) throw new InvalidOperationException("The three particles form a collapsed triangle. Move them apart first.");
        if (sim.TriangleIndices.Chunk(3).Any(t => t.Select(i => (int)i).Order().SequenceEqual(indices.Order())))
            return document;
        var edit = new NativeBphclParticleEditor(document);
        var triangles = sim.TriangleIndices.Select(i => (int)i).Concat(indices).SelectMany(i => BitConverter.GetBytes(checked((ushort)i))).ToArray();
        edit.ReplaceArray(sim.DataOffset + 352, triangles, triangles.Length / 2, edit.Type("hkUint16"), edit.MemberType(sim.ItemIndex, "triangleIndices"));
        if (edit.Array(sim.DataOffset + 368) is { Count: > 0 } flips)
        {
            if (flips.Count != sim.TriangleCount) throw new InvalidDataException("Triangle flip count mismatch.");
            var bytes = edit.ArrayBytes(sim.DataOffset + 368, 1).Append((byte)0).ToArray();
            edit.ReplaceArray(sim.DataOffset + 368, bytes, bytes.Length, flips.TypeIndex, edit.MemberType(sim.ItemIndex, "triangleFlips"));
        }
        foreach (var buffer in cloth.BufferDefinitions.Where(b => b.Type is 1 or 2 && b.SubType == sim.Index))
            edit.UInt(buffer.DataOffset + 52, checked((uint)sim.TriangleCount + 1));
        var updated = edit.Finish();
        for (var i = 0; i < 3; i++)
        {
            var a = indices[i]; var b = indices[(i + 1) % 3];
            var active = updated.Cloths[clothIndex].SimCloths[0].ConstraintSets.Where(s => !s.IsAntiPinch && s.ClassName == "hclStandardLinkConstraintSet" &&
                updated.Cloths[clothIndex].Operators.SelectMany(o => o.SimulateConfigs).Any(c => ExecutesSet(c, s.Index)));
            if (active.Any(s => ContainsLink(s, a, b))) continue;
            var mass = sim.Particles[a].InverseMass + sim.Particles[b].InverseMass;
            updated = AddConstraint(updated, clothIndex, new[] { a, b }, new ClothLinkOptions("hclStandardLinkConstraintSet",
                System.Numerics.Vector3.Distance(p[i], p[(i + 1) % 3]), mass > 0 ? 1 / mass : 0));
        }
        return updated;
    }

    public static NativeBphclDocument DeleteCollider(NativeBphclDocument document, int clothIndex, int colliderIndex)
    {
        var collider = document.Colliders.ElementAtOrDefault(colliderIndex) ?? throw new ArgumentOutOfRangeException(nameof(colliderIndex));
        var cloth = document.Cloths[clothIndex];
        var edit = new NativeBphclParticleEditor(document);
        foreach (var sim in cloth.SimCloths)
        {
            var slots = sim.CollidableItemIndices.Select((item, index) => (item, index))
                .Where(p => p.item == collider.ItemIndex).Select(p => p.index).ToHashSet();
            if (slots.Count == 0) continue;
            if (sim.CollidableItemIndices.Count > 31) throw new InvalidOperationException("Collider deletion cannot remap this extended collision-mask layout.");
            var field = sim.DataOffset + 208;
            var storage = document.GetReferenceArray(field);
            edit.Rewrite(field, 8, (r, i) => slots.Contains(i) ? null : r);
            if (edit.Array(field) is { } refs)
                for (uint i = 0; i < refs.Count; i++) edit._patches[storage.EntryPatchTypeIndex].Add(refs.DataOffset + i * 8);
            edit.Rewrite(sim.DataOffset + 408, 8, (r, i) => slots.Contains(i) ? null : r);
            if (sim.CollidableTransformMap.TransformSetIndex >= 0)
            {
                edit.Rewrite(sim.DataOffset + 176, 4, (r, i) => slots.Contains(i) ? null : r);
                edit.Rewrite(sim.DataOffset + 192, 64, (r, i) => slots.Contains(i) ? null : r);
            }
            edit.Rewrite(sim.DataOffset + 232, 4, (r, _) =>
            {
                var mask = BinaryPrimitives.ReadUInt32LittleEndian(r);
                foreach (var slot in slots.OrderDescending())
                {
                    var lower = mask & ((1u << slot) - 1);
                    var upper = ((mask & 0x7fffffffu) >> (slot + 1)) << slot;
                    mask = lower | upper | (mask & 0x80000000u);
                }
                BinaryPrimitives.WriteUInt32LittleEndian(r, mask);
                return r;
            });
            foreach (var op in cloth.Operators.Where(o => o.ClassName == "hclSimulateOperator" && o.SimulationClothIndex == sim.Index))
            {
                var configs = edit.Array(op.DataOffset + 80);
                if (configs == null) continue;
                for (uint i = 0; i < configs.Count; i++)
                    edit.Rewrite(configs.DataOffset + i * 48 + 24, 1, (r, n) => slots.Contains(n) ? null : r);
            }
        }
        // Outer collider objects may be shared by other cloths or registrations; retain ownership there.
        return edit.Finish();
    }

    internal static bool CanRemoveRelationship(NativeBphclConstraintSet set, ParticleRelationshipRow row) =>
        !set.IsAntiPinch && ((row.LinkIndex >= 0 && row.LinkIndex < set.Links.Count &&
            (SupportsLinkRemoval(set) || set.ClassName == "hclBendStiffnessConstraintSet")) ||
            (set.ClassName == "hclLocalRangeConstraintSet" && row.LocalConstraintIndex >= 0 &&
                row.LocalConstraintIndex < set.LocalConstraints.Count + set.LocalStiffnessConstraints.Count));

    public static NativeBphclDocument RemoveRelationship(NativeBphclDocument document, int clothIndex, ParticleRelationshipRow row)
    {
        var set = document.Cloths[clothIndex].SimCloths[0].ConstraintSets.ElementAtOrDefault(row.ConstraintSetIndex);
        if (set == null || !CanRemoveRelationship(set, row)) throw new InvalidOperationException("This relationship cannot be removed safely.");
        var edit = new NativeBphclParticleEditor(document);
        var field = set.DataOffset + 40;
        var index = row.LinkIndex;
        if (row.LocalConstraintIndex >= 0)
        {
            index = row.LocalConstraintIndex;
            if (index >= set.LocalConstraints.Count) { field += 16; index -= set.LocalConstraints.Count; }
        }
        var array = edit.Array(field)!;
        var size = document.TypeDefinitions.Single(t => t.TypeIndex == array.TypeIndex).Size
            ?? throw new InvalidDataException("Constraint record size is unknown.");
        edit.Rewrite(field, checked((int)size), (r, i) => i == index ? null : r);
        return edit.Finish();
    }

    public static NativeBphclDocument AddConstraint(NativeBphclDocument document, int clothIndex,
        IReadOnlyList<int> particles, ClothLinkOptions options)
    {
        var kind = ClothConstraintCatalog.Kinds.SingleOrDefault(k => k.ClassName == options.ClassName && k.CanCreate)
            ?? throw new ArgumentException("Unsupported constraint creation type.");
        document = EnsureTypes(document, kind.ClassName);
        var cloth = document.Cloths[clothIndex];
        var sim = cloth.SimCloths.Single();
        if (particles.Count != 2 || particles.Distinct().Count() != 2 || particles.Any(p => p < 0 || p >= sim.Particles.Count))
            throw new ArgumentException("Select two different particles.");
        var edit = new NativeBphclParticleEditor(document);
        var set = sim.ConstraintSets.FirstOrDefault(s => !s.IsAntiPinch && s.ClassName == kind.ClassName &&
            cloth.Operators.SelectMany(o => o.SimulationClothIndex == sim.Index ? o.SimulateConfigs : System.Array.Empty<NativeBphclSimulateConfig>())
                .Any(c => ExecutesSet(c, s.Index)));
        uint offset; int itemIndex;
        if (set == null)
        {
            var definition = document.TypeDefinitions.Single(t => t.TypeIndex == edit.Type(kind.ClassName));
            offset = edit.Allocate(new byte[checked((int)definition.Size!.Value)]);
            itemIndex = edit._items.Count;
            edit._items.Add(new NativeBphclItem(0x10000000u | definition.TypeIndex, definition.TypeIndex, offset, 1));
            // TotK stores the solver kind in the reflected class, not hclConstraintSet.type.
            edit.UInt(offset + 32, checked(sim.ConstraintSets.Select(s => s.ConstraintId).DefaultIfEmpty(0u).Max() + 1));
            var staticField = sim.DataOffset + 136;
            var arrayType = edit.MemberType(sim.ItemIndex, "staticConstraintSets");
            var entryType = NativeBphclTypeTable.GetArrayElementType(document, arrayType);
            var oldReferences = document.GetReferenceItemIndices(staticField);
            var refs = oldReferences.Append(itemIndex).SelectMany(i => BitConverter.GetBytes((ulong)i)).ToArray();
            edit.ReplaceArray(staticField, refs, oldReferences.Count + 1, entryType, arrayType);
            var newStorage = edit.Array(staticField)!;
            if (!edit._patches.TryGetValue(entryType, out var patches))
                edit._patches[entryType] = patches = new HashSet<uint>();
            for (uint i = 0; i < newStorage.Count; i++) patches.Add(newStorage.DataOffset + i * 8);
            foreach (var op in cloth.Operators.Where(o => o.ClassName == "hclSimulateOperator" && o.SimulationClothIndex == sim.Index))
            {
                var configs = edit.Array(op.DataOffset + 80);
                if (configs == null) continue;
                for (uint i = 0; i < configs.Count; i++)
                {
                    var field = configs.DataOffset + i * 48 + 8;
                    if (edit.Array(field) is not { Count: > 0 } schedule) continue;
                    var values = edit.ArrayBytes(field, 4).Chunk(4).Select(b => BitConverter.ToInt32(b)).ToList();
                    var collision = values.IndexOf(-1);
                    values.Insert(collision < 0 ? values.Count : collision, oldReferences.Count);
                    edit.ReplaceArray(field, values.SelectMany(BitConverter.GetBytes).ToArray(), values.Count,
                        schedule.TypeIndex, edit._patches.Single(p => p.Value.Contains(field)).Key);
                }
            }
        }
        else
        {
            if (ContainsLink(set, particles[0], particles[1])) throw new InvalidOperationException("This constraint already exists in the active set.");
            offset = set.DataOffset; itemIndex = set.ItemIndex;
        }
        var linkType = edit.Type(kind.ClassName + "::Link");
        var stride = kind.ClassName == "hclBendLinkConstraintSet" ? edit.ValidateBendLinkLayout(linkType) : 12;
        if (stride == 12) edit.ValidateLinkLayout(linkType);
        var previous = edit.ArrayBytes(offset + 40, stride);
        var bytes = new byte[previous.Length + stride]; previous.CopyTo(bytes, 0);
        U16(bytes, previous.Length, particles[0]); U16(bytes, previous.Length + 2, particles[1]);
        void FloatAt(int position, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(previous.Length + position), value);
        FloatAt(4, stride == 20 ? options.MinimumLength : options.Length);
        FloatAt(8, stride == 20 ? options.Length : options.Stiffness);
        if (stride == 20) { FloatAt(12, options.Stiffness); FloatAt(16, options.StretchStiffness); }
        edit.ReplaceArray(offset + 40, bytes, bytes.Length / stride, linkType, edit.MemberType(itemIndex, "links"));
        return edit.Finish();
    }

    private static NativeBphclDocument EnsureTypes(NativeBphclDocument document, params string[] names)
    {
        var missing = names.Where(n => !document.TypeNames.Contains(n)).ToArray();
        if (missing.Length == 0) return document;
        var types = NativeBphclTypeTable.AddEmbeddedTypes(document,
            missing.Select(n => NativeBphclReflectionCatalog.GetTypeIndex(n)).ToArray());
        var section = document.DataSection!;
        return NativeBphclDocument.Parse(NativeBphclTagFileBuilder.Rebuild(document,
            document.Bytes.AsSpan(section.PayloadOffset, section.PayloadSize),
            document.Items.Select(item => item with { TypeIndex = types.TargetToMerged[item.TypeIndex],
                Flags = (item.Flags & 0xff000000u) | types.TargetToMerged[item.TypeIndex] }).ToArray(),
            document.InternalPatches.Select(p => new NativeBphclPatch(types.TargetToMerged[p.TypeIndex], p.Offsets)).ToArray(),
            types.ReplacementSection));
    }

    private NativeBphclTypeMember Member(uint type, string name)
    {
        while (type != 0)
        {
            var definition = _document.TypeDefinitions.Single(t => t.TypeIndex == type);
            var member = definition.Members.FirstOrDefault(m => m.Name == name || m.Name == "m_" + name);
            if (member != null) return member;
            type = definition.ParentTypeIndex;
        }
        throw new InvalidDataException($"Required member {name} is absent from the native type metadata.");
    }

    private byte[] ArrayBytes(uint field, int stride)
    {
        var array = Array(field);
        return array == null ? System.Array.Empty<byte>() : CollectionsMarshal.AsSpan(_data)
            .Slice(checked((int)array.DataOffset), checked((int)array.Count * stride)).ToArray();
    }

    private void Rewrite(uint field, int stride, Func<byte[], int, byte[]?> transform, int elementsPerRecord = 1)
    {
        var array = Array(field);
        if (array == null || array.Count == 0) return;
        if (array.Count % elementsPerRecord != 0) throw new InvalidDataException("Native array has an incomplete record.");
        var bytes = ArrayBytes(field, stride / elementsPerRecord);
        var result = new List<byte>();
        for (var i = 0; i < array.Count / elementsPerRecord; i++)
        {
            var record = transform(bytes.AsSpan(i * stride, stride).ToArray(), i);
            if (record != null) result.AddRange(record);
        }
        ReplaceArray(field, result.ToArray(), result.Count / stride * elementsPerRecord, array.TypeIndex,
            _patches.Single(p => p.Value.Contains(field)).Key);
    }

    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    private static void U16(byte[] data, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), checked((ushort)value));

    public static NativeBphclDocument Delete(NativeBphclDocument document, int clothIndex, IReadOnlyCollection<int> deleted)
    {
        document = EnsureTypes(document, "hclGatherAllVerticesOperator");
        var cloth = document.Cloths[clothIndex];
        if (cloth.SimCloths.Count != 1) throw new InvalidOperationException("Particle deletion requires one simulation per cloth.");
        var sim = cloth.SimCloths[0];
        var removed = deleted.ToHashSet();
        if (removed.Count == 0) return document;
        if (removed.Any(i => i < 0 || i >= sim.Particles.Count)) throw new ArgumentOutOfRangeException(nameof(deleted));
        if (removed.Count == sim.Particles.Count) throw new InvalidOperationException("Keep at least one particle, or remove the whole cloth instead.");
        var edit = new NativeBphclParticleEditor(document);
        var map = Enumerable.Range(0, sim.Particles.Count).Select(i => removed.Contains(i) ? -1 : i - removed.Count(r => r < i)).ToArray();
        var kept = sim.Particles.Count - removed.Count;
        // Packed virtual-contact structures need their own topology compiler. Never retain stale indices.
        var virtualType = document.TypeDefinitions.Single(t => document.GetTypeName(t.TypeIndex) == "hclVirtualCollisionPointsData");
        if (virtualType.Members.Where(m => document.GetTypeName(m.TypeIndex) == "hkArray")
            .Any(m => edit.Array(sim.DataOffset + 432 + m.Offset) is { Count: > 0 }))
            throw new InvalidOperationException("This cloth contains packed virtual-collision data. Particle deletion is not yet safe for this layout; no changes were made.");
        var simBuffers = cloth.BufferDefinitions.Where(b => b.Type is 1 or 2 && b.SubType == sim.Index).ToArray();
        var simBufferIds = simBuffers.Select(b => b.Index).ToHashSet();
        var allowed = new[] { "hclObjectSpaceSkinPOperator", "hclBoneSpaceSkinPOperator", "hclMoveParticlesOperator",
            "hclSimulateOperator", "hclSimpleMeshBoneDeformOperator", "hclCopyVerticesOperator", "hclGatherAllVerticesOperator" };
        foreach (var op in cloth.Operators)
            if (!allowed.Contains(op.ClassName) || (op.InputBufferIndex is int input && simBufferIds.Contains(input) && op.ClassName != "hclSimpleMeshBoneDeformOperator"))
                throw new InvalidOperationException($"Particle deletion cannot safely remap {op.ClassName}. No changes were made.");
        byte[]? Remap(byte[] record, params int[] offsets)
        {
            var indices = offsets.Select(o => (int)U16(record, o)).ToArray();
            if (indices.Any(i => i >= map.Length)) throw new InvalidDataException("Particle reference is outside the simulation.");
            if (indices.Any(i => map[i] < 0)) return null;
            for (var n = 0; n < offsets.Length; n++) U16(record, offsets[n], map[indices[n]]);
            return record;
        }
        foreach (var set in sim.ConstraintSets)
        {
            var fields = set.ClassName switch
            {
                "hclStandardLinkConstraintSet" or "hclStretchLinkConstraintSet" or "hclBendLinkConstraintSet" or "hclBendStiffnessConstraintSet" => new[] { "links" },
                "hclLocalRangeConstraintSet" => new[] { "localConstraints", "localStiffnessConstraints", "localCapsuleConstraints", "localCapsuleStiffnessConstraints" },
                "hclTransitionConstraintSet" => new[] { "perParticleData" },
                "hclBonePlanesConstraintSet" => new[] { "bonePlanes" },
                _ => throw new InvalidOperationException($"Particle deletion cannot yet remap {set.ClassName}. No changes were made.")
            };
            foreach (var name in fields)
            {
                var member = edit.Member(document.GetItem(set.ItemIndex).TypeIndex, name);
                var field = set.DataOffset + member.Offset;
                if (edit.Array(field) is not { Count: > 0 } array) continue;
                var type = document.TypeDefinitions.Single(t => t.TypeIndex == array.TypeIndex);
                var refs = type.Members.Where(m => m.Name.TrimStart('m', '_') is "particleA" or "particleB" or "particleC" or "particleD" or "particleIndex")
                    .Select(m => checked((int)m.Offset)).ToArray();
                if (refs.Length == 0 || type.Size == null) throw new InvalidDataException($"Unknown particle layout in {set.ClassName}.{name}.");
                edit.Rewrite(field, checked((int)type.Size.Value), (r, _) => Remap(r, refs));
            }
        }
        edit.Rewrite(sim.DataOffset + 64, 16, (r, i) => removed.Contains(i) ? null : r);
        foreach (var pose in document.GetReferenceItemIndices(sim.DataOffset + 120))
            edit.Rewrite(document.GetItem(pose).DataOffset + 32, 16, (r, i) => removed.Contains(i) ? null : r);
        edit.Rewrite(sim.DataOffset + 80, 2, (r, _) => Remap(r, 0));
        edit.Rewrite(sim.DataOffset + 232, 4, (r, i) => removed.Contains(i) ? null : r);
        edit.Rewrite(sim.DataOffset + 392, 1, (r, i) => removed.Contains(i) ? null : r);
        var triangleMap = new Dictionary<int, int>();
        var nextTriangle = 0;
        edit.Rewrite(sim.DataOffset + 352, 6, (r, i) =>
        {
            var result = Remap(r, 0, 2, 4);
            triangleMap[i] = result == null ? -1 : nextTriangle++;
            return result;
        }, 3);
        edit.Rewrite(sim.DataOffset + 368, 1, (r, i) => triangleMap.GetValueOrDefault(i, -1) < 0 ? null : r);
        foreach (var buffer in simBuffers)
        {
            if (buffer.VertexCount != sim.Particles.Count) throw new InvalidDataException("Simulation buffer particle count mismatch.");
            edit.UInt(buffer.DataOffset + 48, checked((uint)kept));
            edit.UInt(buffer.DataOffset + 52, checked((uint)nextTriangle));
            foreach (var op in cloth.Operators.Where(o => o.OutputBufferIndex == buffer.Index))
            {
                if (op.ClassName == "hclCopyVerticesOperator")
                {
                    // Convert contiguous copies to a gather before compacting their output mapping.
                    edit.ExtendInitializer(op, sim.Particles.Count, 0);
                    var relocated = edit._items[op.ItemIndex].DataOffset;
                    edit.Rewrite(relocated + 72, 2, (r, i) => i == sim.Particles.Count || removed.Contains(i) ? null : r);
                }
                else if (op.ClassName == "hclGatherAllVerticesOperator")
                    edit.Rewrite(op.DataOffset + 72, 2, (r, i) => removed.Contains(i) ? null : r);
                else throw new InvalidOperationException($"Unsupported particle initializer: {op.ClassName}.");
            }
        }
        foreach (var op in cloth.Operators.Where(o => o.ClassName == "hclMoveParticlesOperator" && o.SimulationClothIndex == sim.Index))
            edit.Rewrite(op.DataOffset + 72, 4, (r, _) => Remap(r, 2));
        foreach (var deform in cloth.SimpleMeshBoneDeformers.Where(d => simBufferIds.Contains((int)d.InputBufferIndex)))
        {
            var removedPairs = new HashSet<int>();
            edit.Rewrite(deform.DataOffset + 80, 4, (r, i) =>
            {
                var old = U16(r, 2);
                if (old % 6 != 0 || !triangleMap.TryGetValue(old / 6, out var triangle)) throw new InvalidDataException("Invalid output-bone triangle reference.");
                if (triangle < 0) { removedPairs.Add(i); return null; }
                U16(r, 2, triangle * 6); return r;
            });
            if (deform.LocalBoneTransforms.Count != deform.TriangleBonePairs.Count) throw new InvalidDataException("Output-bone transforms are not aligned with triangles.");
            edit.Rewrite(deform.DataOffset + 96, 64, (r, i) => removedPairs.Contains(i) ? null : r);
        }
        var fixedIndices = sim.Particles.Where(p => p.Fixed && map[p.Index] >= 0).Select(p => map[p.Index]).ToHashSet();
        edit.UpdateStatistics(sim, fixedIndices);
        var pinch = edit.Array(sim.DataOffset + 392);
        var pinched = pinch == null ? System.Array.Empty<int>() : Enumerable.Range(0, kept)
            .Where(i => edit._data[(int)pinch.DataOffset + i] != 0).ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshal.AsSpan(edit._data).Slice((int)sim.DataOffset + 424), checked((ushort)pinched.DefaultIfEmpty(0).Min()));
        BinaryPrimitives.WriteUInt16LittleEndian(CollectionsMarshal.AsSpan(edit._data).Slice((int)sim.DataOffset + 426), checked((ushort)pinched.DefaultIfEmpty(0).Max()));
        return edit.Finish();
    }
}
