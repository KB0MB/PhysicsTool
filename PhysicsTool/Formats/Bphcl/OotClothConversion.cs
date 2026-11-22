using System.Buffers.Binary;

namespace HKCLTool;

/// <summary>Experimental cloth schema migration between Havok 2022 and 2025.</summary>
public static class OotClothConversion
{
    internal static int[][] BuildStateMasks(NativeBphclCloth cloth, NativeBphclClothState state)
    {
        var count = state.OperatorItemIndices.Count;
        var result = Enumerable.Range(0, 4).Select(_ => new int[count]).ToArray();
        var queue = new Queue<int>();
        for (int i = 0; i < count; i++)
        {
            if ((uint)state.OperatorItemIndices[i] >= cloth.Operators.Count) throw new InvalidDataException("State references a missing operator.");
            if (cloth.Operators[state.OperatorItemIndices[i]].ClassName == "hclSimulateOperator")
            { result[2][i] = 1; queue.Enqueue(i); }
        }
        if (queue.Count > 0 && (state.DependencyGraph == null || state.DependencyGraph.HasCycles || state.DependencyGraph.Parents.Count != count))
            throw new NotSupportedException($"{cloth.Name}/{state.Name}: a verified acyclic operator dependency graph is required.");
        while (queue.TryDequeue(out var index))
        {
            if ((uint)index >= count) throw new InvalidDataException("Invalid dependency index.");
            if (result[0][index] != 0) continue;
            result[0][index] = 1;
            foreach (var parent in state.DependencyGraph!.Parents[index]) queue.Enqueue(parent);
        }
        for (int i = 0; i < count; i++)
        {
            result[1][i] = 1 - result[0][i];
            var name = cloth.Operators[state.OperatorItemIndices[i]].ClassName;
            result[3][i] = result[0][i] != 0 && name is ("hclObjectSpaceSkinPOperator" or "hclBoneSpaceSkinPOperator") ? 1 : 0;
        }
        return result;
    }

    public static NativeBphclDocument ReadReference(NativeBphclDocument source, string romfs, bool targetOot = true)
    {
        if (!targetOot) return CreateTotkReference();
        var folder = Path.Combine(romfs, "Phive", "Cloth");
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Select the target game's romfs folder containing Phive/Cloth.");
        var sourceIds = NativeBphclTypeTable.ConversionIdentities(source);
        var required = source.Items.Select(i => i.TypeIndex).Concat(source.InternalPatches.Select(p => p.TypeIndex))
            .Where(i => i != 0).Select(i => sourceIds[i]).ToHashSet(StringComparer.Ordinal);
        NativeBphclDocument? result = null;
        foreach (var path in Directory.EnumerateFiles(folder, "*.bphcl").OrderBy(p => Path.GetFileName(p).StartsWith("Player_Adult.") ? 0 : 1).ThenBy(p => p))
        {
            var candidate = NativeBphclDocument.Open(path);
            if (targetOot ? !candidate.IsOot : candidate.SdkVersion != "20220100") continue;
            var identities = NativeBphclTypeTable.ConversionIdentities(candidate);
            if (result == null) result = candidate;
            else if (identities.Values.Any(required.Contains))
            {
                var union = NativeBphclTypeTable.Create(result, candidate);
                if (union.ReplacementSection != null)
                {
                    var items = result.Items.Select(i => i with { TypeIndex = union.TargetToMerged[i.TypeIndex],
                        Flags = (i.Flags & 0xff000000u) | union.TargetToMerged[i.TypeIndex] }).ToArray();
                    var patches = result.InternalPatches.Select(p => p with { TypeIndex = union.TargetToMerged[p.TypeIndex] }).ToArray();
                    result = NativeBphclDocument.Parse(NativeBphclTagFileBuilder.Rebuild(result,
                        result.Bytes.AsSpan(result.DataSection!.PayloadOffset, result.DataSection.PayloadSize), items, patches, union.ReplacementSection));
                }
            }
            required.ExceptWith(NativeBphclTypeTable.ConversionIdentities(result).Values);
            if (required.Count == 0) return result;
        }
        return result ?? throw new InvalidDataException("No compatible target-game cloth reference was found in this folder.");
    }

    public static NativeBphclDocument CreateTotkReference() =>
        NativeBphclTypeTable.CompleteTotkConversionLayouts(NativeBphclDocument.Parse(NativeBphclStandaloneGraphWriter.Write(
            HkclToBphclGameReadyGraphBuilder.Build(HkclService.CreateBlankCloth("ConversionReference", false))
                with { NativeTypeSection = NativeBphclGameReadyReflectionCatalog.TypeSection })));

    public static byte[] Convert(NativeBphclDocument source, NativeBphclDocument reference)
    {
        if (!(source.SdkVersion == "20220100" && reference.IsOot || source.IsOot && reference.SdkVersion == "20220100"))
            throw new InvalidOperationException("Choose different OoT and TotK cloth formats for source and target.");
        return new Migration(source, reference).Run();
    }

    private sealed class Migration(NativeBphclDocument source, NativeBphclDocument target)
    {
        private readonly Dictionary<uint, uint> _types = new() { [0] = 0 };
        private readonly IReadOnlyDictionary<uint, string> _sourceIdentities = NativeBphclTypeTable.ConversionIdentities(source);
        private readonly IReadOnlyDictionary<uint, string> _targetIdentities = NativeBphclTypeTable.ConversionIdentities(target);
        private readonly Dictionary<uint, uint> _pointers = new();
        private readonly HashSet<uint> _sourcePointers = source.RelocationOffsets.ToHashSet();
        private readonly HashSet<uint> _removedPointers = [];
        private readonly Dictionary<uint, NativeBphclTypeDefinition> _old = source.TypeDefinitions.Where(t => t.TypeIndex != 0).ToDictionary(t => t.TypeIndex);
        private readonly Dictionary<uint, NativeBphclTypeDefinition> _new = target.TypeDefinitions.Where(t => t.TypeIndex != 0).ToDictionary(t => t.TypeIndex);
        private readonly List<NativeBphclItem> _items = [];
        private byte[] _data = [];

        internal byte[] Run()
        {
            var identities = _sourceIdentities;
            var targets = _targetIdentities.GroupBy(p => p.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => _new.ContainsKey(p.Key)).First().Key);
            foreach (var pair in identities)
                if (targets.TryGetValue(pair.Value, out var index)) _types[pair.Key] = index;

            uint cursor = 0;
            foreach (var item in source.Items)
            {
                if (item.TypeIndex == 0) { _items.Add(item); continue; }
                var type = Map(item.TypeIndex);
                if (!_new.ContainsKey(type)) throw new NotSupportedException($"Missing target layout for {target.GetTypeName(type)} ({type}), from {source.GetTypeName(item.TypeIndex)} ({item.TypeIndex}, count {item.Count}).");
                var size = Size(_new, type);
                var alignment = Math.Max(1u, Definition(_new, type).Alignment ?? 1);
                cursor = checked((cursor + alignment - 1) / alignment * alignment);
                _items.Add(new((item.Flags & 0xff000000u) | type, type, cursor, item.Count));
                cursor = checked(cursor + size * item.Count);
                if (cursor > 256 * 1024 * 1024) throw new InvalidDataException("Converted cloth exceeds the supported size.");
            }
            _data = new byte[checked((int)((cursor + 15) / 16 * 16))];
            foreach (var (item, i) in source.Items.Select((item, i) => (item, i)))
            {
                if (item.TypeIndex == 0) continue;
                var destination = _items[i];
                if (!_old.ContainsKey(item.TypeIndex)) throw new NotSupportedException($"Missing source layout for {source.GetTypeName(item.TypeIndex)} ({item.TypeIndex}, count {item.Count}).");
                for (uint element = 0; element < item.Count; element++)
                    CopyValue(item.TypeIndex, destination.TypeIndex, checked(item.DataOffset + element * Size(_old, item.TypeIndex)),
                        checked(destination.DataOffset + element * Size(_new, destination.TypeIndex)), 0);
            }
            var patches = source.InternalPatches.Where(p => p.Offsets.Any(o => !_removedPointers.Contains(o))).Select(p => new NativeBphclPatch(Map(p.TypeIndex), p.Offsets.Where(o => !_removedPointers.Contains(o)).Select(o =>
                _pointers.TryGetValue(o, out var moved) ? moved : throw new NotSupportedException($"Unmapped relocation at 0x{o:x}.")).ToArray())).ToList();
            if (target.IsOot) WriteStateMasks(patches);
            var parameter = source.Bytes.AsSpan(checked((int)source.Header.ParameterOffset), checked((int)source.Header.ParameterSize)).ToArray();
            var groupedPatches = patches.GroupBy(p => p.TypeIndex).Select(g => new NativeBphclPatch(g.Key,
                g.SelectMany(p => p.Offsets).Distinct().Order().ToArray())).ToArray();
            var result = NativeBphclTagFileBuilder.Rebuild(target, _data, _items, groupedPatches, replacementAamp: parameter);
            if (!target.IsOot) result = NativeBphclGameReadyCompactor.Compact(NativeBphclDocument.Parse(result));
            var check = NativeBphclDocument.Parse(result);
            if (check.SdkVersion != target.SdkVersion || check.Cloths.Count != source.Cloths.Count || check.Colliders.Count != source.Colliders.Count)
                throw new InvalidDataException("Converted cloth failed structural validation.");
            foreach (var offset in check.RelocationOffsets)
                if (!check.TryGetReferencedItem(offset, out _)) throw new InvalidDataException("Output has an unresolved relocation.");
            for (int i = 0; i < source.Colliders.Count; i++)
            {
                var a = source.Colliders[i]; var b = check.Colliders[i];
                if (a.Name != b.Name || a.Translation != b.Translation || a.AxisX != b.AxisX || a.AxisY != b.AxisY || a.AxisZ != b.AxisZ ||
                    (a.Shape with { DataOffset = 0 }) != (b.Shape with { DataOffset = 0 }))
                    throw new InvalidDataException("Conversion changed collider geometry.");
            }
            if (check.Skeletons.Count != source.Skeletons.Count) throw new InvalidDataException("Conversion changed skeleton count.");
            for (int i = 0; i < source.Skeletons.Count; i++)
            {
                var a = source.Skeletons[i]; var b = check.Skeletons[i];
                if (a.Name != b.Name || a.Bones.Count != b.Bones.Count) throw new InvalidDataException("Conversion changed skeleton identity.");
                for (int j = 0; j < a.Bones.Count; j++)
                    if (a.Bones[j].Name != b.Bones[j].Name || a.Bones[j].ParentIndex != b.Bones[j].ParentIndex ||
                        a.Bones[j].Translation != b.Bones[j].Translation || a.Bones[j].Rotation != b.Bones[j].Rotation || a.Bones[j].Scale != b.Bones[j].Scale)
                        throw new InvalidDataException("Conversion changed a skeleton rest pose.");
            }
            for (int i = 0; i < source.Cloths.Count; i++)
            {
                var a = source.Cloths[i]; var b = check.Cloths[i];
                if (a.Name != b.Name || a.SimCloths.Count != b.SimCloths.Count)
                    throw new InvalidDataException("Conversion changed cloth identity.");
                for (int j = 0; j < a.SimCloths.Count; j++)
                {
                    var x = a.SimCloths[j]; var y = b.SimCloths[j];
                    if (x.Particles.Count != y.Particles.Count || !x.TriangleIndices.SequenceEqual(y.TriangleIndices))
                        throw new InvalidDataException("Conversion changed particle topology.");
                    if (x.Runtime.TotalMass != y.Runtime.TotalMass || x.ConstraintSets.Count != y.ConstraintSets.Count)
                        throw new InvalidDataException("Conversion changed simulation mass or constraint count.");
                    for (int k = 0; k < x.ConstraintSets.Count; k++)
                    {
                        var before = x.ConstraintSets[k]; var after = y.ConstraintSets[k];
                        if (before.Name != after.Name || before.ClassName != after.ClassName ||
                            before.ConstraintId != after.ConstraintId || before.ConstraintType != after.ConstraintType ||
                            before.IsAntiPinch != after.IsAntiPinch || !SameValues(before.Values, after.Values) ||
                            !SameLinks(before.Links, after.Links) || !SameLinks(before.LocalConstraints, after.LocalConstraints) ||
                            !SameLinks(before.LocalStiffnessConstraints, after.LocalStiffnessConstraints) ||
                            !SameLinks(before.TransitionParticles, after.TransitionParticles) ||
                            !before.VolumeFrameData.SequenceEqual(after.VolumeFrameData) ||
                            !before.VolumeApplyData.SequenceEqual(after.VolumeApplyData))
                            throw new InvalidDataException("Conversion changed constraint data: " + before.Name);
                    }
                    for (int k = 0; k < x.Particles.Count; k++)
                        if (x.Particles[k].Position != y.Particles[k].Position || x.Particles[k].Mass != y.Particles[k].Mass ||
                            x.Particles[k].InverseMass != y.Particles[k].InverseMass || x.Particles[k].Fixed != y.Particles[k].Fixed ||
                            x.Particles[k].Radius != y.Particles[k].Radius || x.Particles[k].Friction != y.Particles[k].Friction)
                            throw new InvalidDataException("Conversion changed particle positions or mass.");
                }
            }
            return result;
        }

        private static bool SameValues(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b) =>
            a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var value) && value.Equals(p.Value));

        private static bool SameLinks(IReadOnlyList<NativeBphclConstraintLink> a, IReadOnlyList<NativeBphclConstraintLink> b) =>
            a.Count == b.Count && a.Zip(b).All(p => p.First.ParticleA == p.Second.ParticleA &&
                p.First.ParticleB == p.Second.ParticleB && p.First.PlaneEquationBone == p.Second.PlaneEquationBone &&
                SameValues(p.First.Values, p.Second.Values));

        private void WriteStateMasks(List<NativeBphclPatch> patches)
        {
            string[] names = ["simAndDependenciesOperatorMask", "simAndDependenciesComplementOperatorMask", "simOnlyOperatorMask", "simDependencySkinOnlyOperatorMask"];
            foreach (var cloth in source.Cloths)
            foreach (var state in cloth.States)
            {
                var masks = BuildStateMasks(cloth, state);
                var item = _items[state.ItemIndex];
                var members = Members(_new, item.TypeIndex);
                for (int mask = 0; mask < names.Length; mask++)
                {
                    if (masks[mask].Length == 0) continue;
                    var member = members[names[mask]];
                    var elementType = NativeBphclTypeTable.GetArrayElementType(target, member.TypeIndex);
                    if (Size(_new, elementType) != 4) throw new InvalidDataException("Unexpected OoT operator mask element size.");
                    var offset = checked((uint)_data.Length);
                    var length = checked(masks[mask].Length * 4);
                    Array.Resize(ref _data, checked(_data.Length + (length + 15) / 16 * 16));
                    var itemIndex = _items.Count;
                    _items.Add(new(0x20000000u | elementType, elementType, offset, (uint)masks[mask].Length));
                    for (int i = 0; i < masks[mask].Length; i++)
                        BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan((int)offset + i * 4, 4), masks[mask][i]);
                    var field = checked(item.DataOffset + member.Offset);
                    BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan((int)field, 4), itemIndex);
                    BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan((int)field + 8, 4), masks[mask].Length);
                    patches.Add(new(elementType, new[] { field }));
                }
            }
        }

        private uint Map(uint type) => _types.TryGetValue(type, out var mapped) ? mapped :
            throw new NotSupportedException($"The target reference does not contain a compatible type for {source.GetTypeName(type)}.");

        private static NativeBphclTypeDefinition Definition(Dictionary<uint, NativeBphclTypeDefinition> table, uint type) =>
            table.TryGetValue(type, out var definition) ? definition : throw new NotSupportedException($"Missing reflected layout for type {type}.");

        private static uint Size(Dictionary<uint, NativeBphclTypeDefinition> table, uint type)
        {
            var definition = Definition(table, type);
            return definition.Size ?? (definition.ParentTypeIndex != 0 ? Size(table, definition.ParentTypeIndex) :
                throw new NotSupportedException($"Missing reflected size for type {type}."));
        }

        private static Dictionary<string, NativeBphclTypeMember> Members(Dictionary<uint, NativeBphclTypeDefinition> table, uint type)
        {
            var result = new Dictionary<string, NativeBphclTypeMember>();
            var visited = new HashSet<uint>();
            while (type != 0 && visited.Add(type))
            {
                var definition = Definition(table, type);
                foreach (var member in definition.Members) result.TryAdd(member.Name, member);
                type = definition.ParentTypeIndex;
            }
            return result;
        }

        private void CopyValue(uint oldType, uint newType, uint oldOffset, uint newOffset, int depth)
        {
            if (depth > 64) throw new InvalidDataException("Recursive inline type layout.");
            if (_sourceIdentities[oldType] != _targetIdentities[newType])
                throw new NotSupportedException($"Changed field type: {_sourceIdentities[oldType]} to {_targetIdentities[newType]}.");
            uint oldSize = Size(_old, oldType), newSize = Size(_new, newType);
            var oldMembers = Members(_old, oldType); var newMembers = Members(_new, newType);
            if (oldMembers.Count == 0 && newMembers.Count == 0)
            {
                if (oldSize != newSize) throw new NotSupportedException($"Changed primitive layout: {source.GetTypeName(oldType)} ({oldType}/{newType}, {oldSize}/{newSize} bytes).");
                CopyBytes(oldOffset, newOffset, oldSize);
                return;
            }
            // Copy named fields; additions start empty. Only explicitly documented
            // newer fields may be discarded; other nonzero removals are rejected.
            foreach (var member in oldMembers.Values)
            {
                if (newMembers.TryGetValue(member.Name, out var destination))
                {
                    if (source.GetTypeName(oldType) == "hclTransformSetUsage" && member.Name == "perComponentFlags" &&
                        Size(_old, member.TypeIndex) == 2 && Size(_new, destination.TypeIndex) == 1)
                    {
                        if (SourceBytes(oldOffset + member.Offset + 1, 1)[0] != 0)
                            throw new NotSupportedException("The second transform component is in use; OoT supports one component.");
                        CopyBytes(oldOffset + member.Offset, newOffset + destination.Offset, 1);
                        continue;
                    }
                    if (source.GetTypeName(oldType) == "hclTransformSetUsage" && member.Name == "perComponentFlags" &&
                        Size(_old, member.TypeIndex) == 1 && Size(_new, destination.TypeIndex) == 2)
                    {
                        CopyBytes(oldOffset + member.Offset, newOffset + destination.Offset, 1);
                        continue;
                    }
                    try { CopyValue(member.TypeIndex, destination.TypeIndex, checked(oldOffset + member.Offset), checked(newOffset + destination.Offset), depth + 1); }
                    catch (NotSupportedException ex) { throw new NotSupportedException($"{source.GetTypeName(oldType)}.{member.Name}: {ex.Message}", ex); }
                }
                else
                {
                    var size = Size(_old, member.TypeIndex);
                    var data = SourceBytes(oldOffset + member.Offset, size);
                    bool emptyArray = source.GetTypeName(member.TypeIndex) == "hkArray" && data.Length == 16 &&
                        BinaryPrimitives.ReadUInt64LittleEndian(data) == 0 && BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) == 0;
                    if (source.IsOot && !target.IsOot && (emptyArray || DiscardNewerField(source.GetTypeName(oldType), member.Name, data)))
                    {
                        foreach (var pointer in _sourcePointers.Where(p => p >= oldOffset + member.Offset && p < oldOffset + member.Offset + size))
                            _removedPointers.Add(pointer);
                        continue;
                    }
                    if (data.IndexOfAnyExcept((byte)0) >= 0)
                        throw new NotSupportedException($"Nonempty source field is not supported by the target: {source.GetTypeName(oldType)}.{member.Name}.");
                }
            }
            if (source.IsOot && source.GetTypeName(oldType) == "hkPreciseTransform" && target.GetTypeName(newType) == "hkTransform")
            {
                uint adjustment = oldOffset + oldMembers["translationAdjustment"].Offset;
                uint translation = newOffset + newMembers["translation"].Offset;
                for (uint component = 0; component < 3; component++)
                {
                    var value = BinaryPrimitives.ReadSingleLittleEndian(_data.AsSpan((int)(translation + component * 4), 4)) +
                        BinaryPrimitives.ReadSingleLittleEndian(SourceBytes(adjustment + component * 4, 4));
                    if (!float.IsFinite(value)) throw new InvalidDataException("Invalid precise collider translation.");
                    BinaryPrimitives.WriteSingleLittleEndian(_data.AsSpan((int)(translation + component * 4), 4), value);
                }
            }
            if (target.GetTypeName(newType) == "hclSimClothData" && newMembers.TryGetValue("virtualCollisionPointsRadiusScale", out var radius))
                BinaryPrimitives.WriteSingleLittleEndian(_data.AsSpan((int)(newOffset + radius.Offset), 4), 1f);
            if (target.IsOot && !source.IsOot && target.GetTypeName(newType) == "hclSimClothData")
            {
                var aero = newMembers["aerodynamicsData"];
                var fields = Members(_new, aero.TypeIndex);
                int At(string name) => checked((int)(newOffset + aero.Offset + fields[name].Offset));
                var particles = source.Cloths.SelectMany(c => c.SimCloths).First(s => s.DataOffset == oldOffset).Particles.Count;
                BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(At("numParticles"), 4), checked((uint)particles));
                // Empty per-particle arrays require constant factors. Keep lift/drag
                // disabled, and do not reinterpret older simple-wind coefficients.
                foreach (var name in new[] { "surfaceDensity", "constantParticleLiftFactor", "constantParticleDragFactor" })
                    BinaryPrimitives.WriteSingleLittleEndian(_data.AsSpan(At(name), 4), 1f);
                _data[At("useConstantLiftFactor")] = _data[At("useConstantDragFactor")] = 1;
            }
            if (target.GetTypeName(newType) == "hclClothData" && newMembers.TryGetValue("targetPlatform", out var platform))
            {
                var native = target.GetItem(target.Cloths[0].ItemIndex);
                target.Bytes.AsSpan(checked(target.DataSection!.PayloadOffset + (int)(native.DataOffset + platform.Offset)), 4)
                    .CopyTo(_data.AsSpan(checked((int)(newOffset + platform.Offset)), 4));
            }
        }

        private bool DiscardNewerField(string? type, string name, ReadOnlySpan<byte> data)
        {
            // State masks are a 2025 scheduling cache derived from the retained
            // dependency graph. Aerodynamics has no 2022 equivalent; the export
            // UI explicitly warns about this loss before conversion.
            if (type == "hclClothState" && name is "simAndDependenciesOperatorMask" or
                "simAndDependenciesComplementOperatorMask" or "simOnlyOperatorMask" or "simDependencySkinOnlyOperatorMask") return true;
            if (type == "hclSimClothData" && name == "aerodynamicsData") return true;
            if (type == "hclSimClothData" && name == "setupCollidableSidedness")
            {
                int itemIndex = BinaryPrimitives.ReadInt32LittleEndian(data);
                if (itemIndex == 0) return true;
                var item = source.GetItem(itemIndex);
                if (Size(_old, item.TypeIndex) == 1 && SourceBytes(item.DataOffset, item.Count).IndexOfAnyExcept((byte)0) < 0) return true;
                throw new NotSupportedException("OoT nondefault collider sidedness cannot be represented in TotK.");
            }
            if (type == "hclSimClothData" && name == "virtualCollisionPointsRadiusScale")
            {
                var scale = BinaryPrimitives.ReadSingleLittleEndian(data);
                if (scale != 1) throw new NotSupportedException("Non-unit virtual collision radius scaling cannot be represented in TotK.");
                return true;
            }
            return type == "hkPreciseTransform" && name == "translationAdjustment";
        }

        private ReadOnlySpan<byte> SourceBytes(uint offset, uint size)
        {
            var section = source.DataSection!;
            if (offset > section.PayloadSize || size > section.PayloadSize - offset) throw new InvalidDataException("Source item exceeds DATA bounds.");
            return source.Bytes.AsSpan(checked(section.PayloadOffset + (int)offset), checked((int)size));
        }

        private void CopyBytes(uint from, uint to, uint count)
        {
            SourceBytes(from, count).CopyTo(_data.AsSpan(checked((int)to), checked((int)count)));
            foreach (var pointer in _sourcePointers.Where(p => p >= from && p < from + count))
            {
                if (pointer + 4 > from + count) throw new InvalidDataException("Split pointer field.");
                _pointers[pointer] = checked(to + pointer - from);
            }
        }
    }
}
