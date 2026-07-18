using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace HKCLTool;

// Resolves the generic TAG0 graph into cloth-editor concepts. Offsets here
// are native Havok layouts verified against real BPHCL samples.
public sealed partial class NativeBphclDocument
{
    private IReadOnlyList<NativeBphclNamedVariant> ReadRootVariants()
    {
        if (!TryGetReferencedItem(0, out var variantItemIndex))
            return Array.Empty<NativeBphclNamedVariant>();

        var variantItem = GetItem(variantItemIndex);
        const int namedVariantSize = 24;
        if (variantItem.Count == 0 || variantItem.Count > int.MaxValue / namedVariantSize)
            return Array.Empty<NativeBphclNamedVariant>();

        var count = checked((int)variantItem.Count);
        var result = new List<NativeBphclNamedVariant>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = checked(variantItem.DataOffset + (uint)(index * namedVariantSize));
            if (!TryReadStringPointer(offset, out var name) ||
                !TryReadStringPointer(offset + 8, out var className) ||
                !TryGetReferencedItem(offset + 16, out var objectItemIndex))
            {
                throw new InvalidDataException($"Invalid hkRootLevelContainer named variant at index {index}.");
            }

            var objectItem = GetItem(objectItemIndex);
            result.Add(new NativeBphclNamedVariant(
                index,
                name,
                className,
                objectItemIndex,
                objectItem.DataOffset,
                GetTypeName(objectItem.TypeIndex) ?? $"type {objectItem.TypeIndex}"));
        }
        return result;
    }

    private void ReadClothAndSkeletonLists()
    {
        const uint objectHeaderSize = 24;
        const uint hkArraySize = 16;

        var clothContainer = FindRootVariants("hclClothContainer").SingleOrDefault();
        if (clothContainer is not null)
        {
            var colliderItems = ReadReferenceArray(clothContainer.ObjectDataOffset + objectHeaderSize);
            Colliders = colliderItems.Select((itemIndex, index) => ReadCollider(index, itemIndex)).ToArray();
            CollidableCount = Colliders.Count;

            var clothItems = ReadReferenceArray(clothContainer.ObjectDataOffset + objectHeaderSize + hkArraySize);
            var cloths = new List<NativeBphclCloth>(clothItems.Count);
            for (var index = 0; index < clothItems.Count; index++)
            {
                var itemIndex = clothItems[index];
                var item = GetItem(itemIndex);
                if (!TryReadStringPointer(item.DataOffset + objectHeaderSize, out var name))
                    name = $"Cloth {index}";

                var simulationItems = ReadReferenceArray(item.DataOffset + objectHeaderSize + 8);
                var simulations = simulationItems.Select((simItemIndex, simIndex) => ReadSimCloth(simIndex, simItemIndex)).ToArray();
                cloths.Add(new NativeBphclCloth(index, name, itemIndex, item.DataOffset, simulations));
            }
            Cloths = cloths;
        }

        var animationContainer = FindRootVariants("hkaAnimationContainer").SingleOrDefault();
        if (animationContainer is null)
            return;

        var skeletonItems = ReadReferenceArray(animationContainer.ObjectDataOffset + objectHeaderSize);
        var skeletons = new List<NativeBphclSkeleton>(skeletonItems.Count);
        for (var index = 0; index < skeletonItems.Count; index++)
        {
            var itemIndex = skeletonItems[index];
            var item = GetItem(itemIndex);
            if (!TryReadStringPointer(item.DataOffset + objectHeaderSize, out var name))
                name = $"Skeleton {index}";
            skeletons.Add(new NativeBphclSkeleton(index, name, itemIndex, item.DataOffset, ReadSkeletonBones(item.DataOffset)));
        }
        Skeletons = skeletons;
    }

    private NativeBphclSimCloth ReadSimCloth(int index, int itemIndex)
    {
        const uint objectHeaderSize = 24;
        var item = GetItem(itemIndex);
        if (!TryReadStringPointer(item.DataOffset + objectHeaderSize, out var name))
            name = $"Simulation {index}";

        // hclSimClothData: +0x40 particles, +0x50 fixed particle indices,
        // +0x78 simulation poses, +0x88/+0x98 constraint-set arrays.
        var particleData = ReadArrayItem(item.DataOffset + 64);
        var fixedParticles = ReadUInt16Array(ReadArrayItem(item.DataOffset + 80));
        var positions = ReadFirstSimulationPosePositions(item.DataOffset + 120);
        var particles = new List<NativeBphclParticle>();
        if (particleData is not null)
        {
            var count = checked((int)particleData.Count);
            particles.Capacity = count;
            for (var particleIndex = 0; particleIndex < count; particleIndex++)
            {
                var offset = checked(particleData.DataOffset + (uint)(particleIndex * 16));
                particles.Add(new NativeBphclParticle(
                    particleIndex,
                    particleIndex < positions.Count ? positions[particleIndex] : Vector4.Zero,
                    fixedParticles.Contains((ushort)particleIndex),
                    ReadSingle(offset),
                    ReadSingle(offset + 4u),
                    ReadSingle(offset + 8u),
                    ReadSingle(offset + 12u),
                    particleIndex < positions.Count ? checked(GetFirstSimulationPosePositionOffset(item.DataOffset + 120, particleIndex)) : 0,
                    offset));
            }
        }

        var constraintItems = ReadReferenceArray(item.DataOffset + 136)
            .Concat(ReadReferenceArray(item.DataOffset + 152))
            .ToArray();
        var constraintSets = constraintItems
            .Select((constraintItemIndex, constraintIndex) => ReadConstraintSet(constraintIndex, constraintItemIndex))
            .ToArray();

        // hclSimClothData::perInstanceCollidables is an hkArray<hclCollidable*>.
        // Keep the referenced ITEM indices so deletion can distinguish colliders
        // owned solely by one cloth from colliders shared with another cloth.
        var collidableItemIndices = ReadReferenceArray(item.DataOffset + 208);
        return new NativeBphclSimCloth(
            index,
            name,
            itemIndex,
            item.DataOffset,
            particles,
            constraintSets,
            collidableItemIndices);
    }

    private NativeBphclConstraintSet ReadConstraintSet(int index, int itemIndex)
    {
        const uint objectHeaderSize = 24;
        var item = GetItem(itemIndex);
        var className = GetTypeName(item.TypeIndex) ?? $"type {item.TypeIndex}";
        if (!TryReadStringPointer(item.DataOffset + objectHeaderSize, out var name))
            name = className;

        // Every specialised hcl constraint begins with hclConstraintSet.
        return new NativeBphclConstraintSet(
            index,
            name,
            className,
            ReadUInt32(item.DataOffset + 32),
            ReadUInt32(item.DataOffset + 36),
            itemIndex,
            item.DataOffset,
            ReadConstraintLinks(item.DataOffset, className));
    }

    private IReadOnlyList<NativeBphclConstraintLink> ReadConstraintLinks(uint constraintOffset, string className)
    {
        var linkType = TypeDefinitions.FirstOrDefault(type =>
            string.Equals(GetTypeName(type.TypeIndex), $"{className}::Link", StringComparison.Ordinal));
        if (linkType is null || linkType.Members.Count == 0)
            return Array.Empty<NativeBphclConstraintLink>();

        // All known link sets inherit hclConstraintSet (40 bytes) and then
        // place their hkArray<Link> at +0x28. The type table supplies the
        // actual Link member offsets and types.
        var linkArray = ReadArrayItem(constraintOffset + 40);
        if (linkArray is null || linkArray.Count == 0)
            return Array.Empty<NativeBphclConstraintLink>();

        var stride = GetStructStride(linkType.Members);
        if (stride <= 0 || linkArray.Count > int.MaxValue / stride)
            throw new InvalidDataException($"Invalid BPHCL link array for {className}.");

        var links = new List<NativeBphclConstraintLink>(checked((int)linkArray.Count));
        for (uint index = 0; index < linkArray.Count; index++)
        {
            var linkOffset = checked(linkArray.DataOffset + index * (uint)stride);
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            int? particleA = null;
            int? particleB = null;

            foreach (var member in linkType.Members)
            {
                var value = ReadPrimitive(member.TypeIndex, linkOffset + member.Offset);
                if (value is null)
                    continue;
                values[member.Name] = value.Value;
                if (member.Name == "particleA")
                    particleA = checked((int)value.Value);
                else if (member.Name == "particleB")
                    particleB = checked((int)value.Value);
            }
            links.Add(new NativeBphclConstraintLink(checked((int)index), particleA, particleB, values));
        }
        return links;
    }

    private int GetStructStride(IReadOnlyList<NativeBphclTypeMember> members)
    {
        var size = 0u;
        var alignment = 1u;
        foreach (var member in members)
        {
            var fieldSize = GetPrimitiveSize(member.TypeIndex);
            if (fieldSize == 0)
                return 0;
            size = Math.Max(size, member.Offset + fieldSize);
            alignment = Math.Max(alignment, Math.Min(fieldSize, 4u));
        }
        return checked((int)((size + alignment - 1) / alignment * alignment));
    }

    private uint GetPrimitiveSize(uint typeIndex) => GetTypeName(typeIndex) switch
    {
        "hkUint8" or "hkInt8" or "hkBool" => 1,
        "hkUint16" or "hkInt16" => 2,
        "hkUint32" or "hkInt32" or "hkReal" => 4,
        _ => 0
    };

    private double? ReadPrimitive(uint typeIndex, uint dataOffset) => GetTypeName(typeIndex) switch
    {
        "hkUint8" or "hkBool" => ReadByte(dataOffset),
        "hkInt8" => unchecked((sbyte)ReadByte(dataOffset)),
        "hkUint16" => ReadUInt16(dataOffset),
        "hkInt16" => unchecked((short)ReadUInt16(dataOffset)),
        "hkUint32" => ReadUInt32(dataOffset),
        "hkInt32" => ReadInt32(dataOffset),
        "hkReal" => ReadSingle(dataOffset),
        _ => null
    };

    private NativeBphclCollider ReadCollider(int index, int itemIndex)
    {
        const uint objectHeaderSize = 24;
        var item = GetItem(itemIndex);
        // hkRotationf aligns the transform from +0x18 to +0x20. Translation
        // then follows its three rotation columns at +0x50.
        var axisX = ReadVector4(item.DataOffset + 32);
        var axisY = ReadVector4(item.DataOffset + 48);
        var axisZ = ReadVector4(item.DataOffset + 64);
        var translation = ReadVector4(item.DataOffset + 80);
        if (!TryReadStringPointer(item.DataOffset + 144, out var name))
            name = $"Collidable {index}";

        var shape = new NativeBphclColliderShape("<none>", -1, Vector4.Zero, Vector4.Zero, 0.0f, 0.0f, Vector4.Zero);
        if (TryGetReferencedItem(item.DataOffset + 136, out var shapeItemIndex))
        {
            var shapeItem = GetItem(shapeItemIndex);
            var shapeTypeName = GetTypeName(shapeItem.TypeIndex) ?? $"type {shapeItem.TypeIndex}";
            var shapeKind = ReadInt32(shapeItem.DataOffset + objectHeaderSize);
            shape = shapeTypeName switch
            {
                "hclCapsuleShape" => new NativeBphclColliderShape(
                    shapeTypeName, shapeKind,
                    ReadVector4(shapeItem.DataOffset + 32),
                    ReadVector4(shapeItem.DataOffset + 48),
                    ReadSingle(shapeItem.DataOffset + 80),
                    ReadSingle(shapeItem.DataOffset + 80),
                    Vector4.Zero),
                "hclSphereShape" => new NativeBphclColliderShape(
                    shapeTypeName, shapeKind,
                    ReadVector4(shapeItem.DataOffset + 32),
                    Vector4.Zero,
                    ReadVector4(shapeItem.DataOffset + 32).W,
                    ReadVector4(shapeItem.DataOffset + 32).W,
                    Vector4.Zero),
                "hclTaperedCapsuleShape" => new NativeBphclColliderShape(
                    shapeTypeName, shapeKind,
                    ReadVector4(shapeItem.DataOffset + 32),
                    ReadVector4(shapeItem.DataOffset + 48),
                    ReadSingle(shapeItem.DataOffset + 144),
                    ReadSingle(shapeItem.DataOffset + 148),
                    Vector4.Zero),
                "hclPlaneShape" => new NativeBphclColliderShape(
                    shapeTypeName, shapeKind,
                    Vector4.Zero, Vector4.Zero, 0.0f, 0.0f,
                    ReadVector4(shapeItem.DataOffset + 32)),
                _ => shape
            };
        }

        return new NativeBphclCollider(index, name, itemIndex, item.DataOffset, translation, axisX, axisY, axisZ, shape, ReadByte(item.DataOffset + 159) != 0);
    }

    private IReadOnlyList<NativeBphclBone> ReadSkeletonBones(uint skeletonOffset)
    {
        var parents = ReadUInt16Array(ReadArrayItem(skeletonOffset + 32));
        var boneEntries = ReadArrayItem(skeletonOffset + 48);
        var poses = ReadArrayItem(skeletonOffset + 64);
        if (boneEntries is null)
            return Array.Empty<NativeBphclBone>();

        var count = checked((int)boneEntries.Count);
        var bones = new List<NativeBphclBone>(count);
        for (var index = 0; index < count; index++)
        {
            var boneOffset = checked(boneEntries.DataOffset + (uint)(index * 16));
            if (!TryReadStringPointer(boneOffset, out var name))
                name = $"Bone {index}";
            var poseOffset = poses is not null && index < poses.Count ? checked(poses.DataOffset + (uint)(index * 48)) : 0;
            bones.Add(new NativeBphclBone(
                index,
                name,
                index < parents.Count && parents[index] != ushort.MaxValue ? parents[index] : -1,
                ReadByte(boneOffset + 8u) != 0,
                poses is not null && index < poses.Count ? ReadVector4(poseOffset) : Vector4.Zero,
                poses is not null && index < poses.Count ? ReadVector4(poseOffset + 16u) : Vector4.Zero));
        }
        return bones;
    }

    private List<int> ReadReferenceArray(uint arrayFieldOffset)
    {
        var arrayItem = ReadArrayItem(arrayFieldOffset);
        if (arrayItem is null || arrayItem.Count == 0)
            return new List<int>();
        if (arrayItem.Count > int.MaxValue / 8)
            throw new InvalidDataException("BPHCL reference array is too large.");

        var items = new List<int>(checked((int)arrayItem.Count));
        for (uint index = 0; index < arrayItem.Count; index++)
        {
            var pointerOffset = checked(arrayItem.DataOffset + index * 8u);
            if (!TryGetReferencedItem(pointerOffset, out var itemIndex))
                throw new InvalidDataException($"BPHCL reference array contains an unresolved pointer at DATA+0x{pointerOffset:X}.");
            items.Add(itemIndex);
        }
        return items;
    }

    internal NativeBphclReferenceArray GetReferenceArray(uint arrayFieldOffset)
    {
        if (!TryGetReferencedItem(arrayFieldOffset, out var storageItemIndex))
            throw new InvalidDataException($"BPHCL array at DATA+0x{arrayFieldOffset:X} has no ITEM reference.");

        var storageItem = GetItem(storageItemIndex);
        var patch = InternalPatches.FirstOrDefault(group => group.Offsets.Contains(storageItem.DataOffset));
        if (patch is null)
        {
            throw new InvalidDataException(
                $"BPHCL array data at DATA+0x{storageItem.DataOffset:X} has no entry relocation patch.");
        }

        return new NativeBphclReferenceArray(
            arrayFieldOffset,
            storageItemIndex,
            storageItem,
            patch.TypeIndex);
    }

    internal IReadOnlyList<int> GetReferenceItemIndices(uint arrayFieldOffset) =>
        ReadReferenceArray(arrayFieldOffset);

    // Follows PTCH-backed pointers starting at a logical cloth root. This is
    // intentionally item-level rather than class-specific: it lets a merge
    // carry every nested buffer/operator the selected cloth actually needs.
    public IReadOnlyList<int> CollectItemClosure(IEnumerable<int> rootItemIndices)
    {
        var ranges = GetItemRanges();
        var included = new HashSet<int>();
        var queue = new Queue<int>(rootItemIndices);
        while (queue.Count > 0)
        {
            var itemIndex = queue.Dequeue();
            if (!included.Add(itemIndex))
                continue;
            if (!ranges.TryGetValue(itemIndex, out var range))
                throw new InvalidDataException($"BPHCL ITEM {itemIndex} has no DATA range.");

            foreach (var patch in InternalPatches)
            {
                foreach (var pointerOffset in patch.Offsets)
                {
                    if (pointerOffset < range.Start || pointerOffset >= range.End)
                        continue;
                    if (!TryGetReferencedItem(pointerOffset, out var referencedItemIndex))
                    {
                        throw new InvalidDataException(
                            $"BPHCL pointer at DATA+0x{pointerOffset:X} could not be resolved while collecting a cloth graph.");
                    }
                    if (!included.Contains(referencedItemIndex))
                        queue.Enqueue(referencedItemIndex);
                }
            }
        }
        return included.OrderBy(index => index).ToArray();
    }

    public IReadOnlyList<NativeBphclPatch> GetPatchesForItems(IEnumerable<int> itemIndices)
    {
        var ranges = GetItemRanges();
        var selectedRanges = itemIndices.Select(index =>
        {
            if (!ranges.TryGetValue(index, out var range))
                throw new InvalidDataException($"BPHCL ITEM {index} has no DATA range.");
            return range;
        }).ToArray();

        return InternalPatches.Select(patch => new NativeBphclPatch(
                patch.TypeIndex,
                patch.Offsets.Where(offset => selectedRanges.Any(range => offset >= range.Start && offset < range.End)).ToArray()))
            .Where(patch => patch.Offsets.Count > 0)
            .ToArray();
    }

    internal Dictionary<int, (uint Start, uint End)> GetItemRanges()
    {
        var ordered = Items.Select((item, index) => (item, index))
            .Where(entry => DataSection is not null && entry.item.DataOffset < DataSection.PayloadSize)
            .OrderBy(entry => entry.item.DataOffset)
            .ToArray();
        var result = new Dictionary<int, (uint Start, uint End)>();

        // Several ITEM records may intentionally describe the same DATA
        // allocation. Treating the later records as zero-length loses shared
        // arrays during graph copies and physical compaction. Every ITEM at a
        // given offset therefore owns the bytes through the next *distinct*
        // allocation offset.
        var groups = ordered.GroupBy(entry => entry.item.DataOffset).ToArray();
        for (var position = 0; position < groups.Length; position++)
        {
            var group = groups[position];
            var start = group.Key;
            var end = position + 1 < groups.Length
                ? groups[position + 1].Key
                : checked((uint)(DataSection?.PayloadSize ?? 0));
            if (end < start)
                throw new InvalidDataException("BPHCL ITEM data offsets are not ordered.");
            foreach (var entry in group)
                result[entry.index] = (start, end);
        }
        return result;
    }

    private NativeBphclItem? ReadArrayItem(uint arrayFieldOffset) =>
        TryGetReferencedItem(arrayFieldOffset, out var itemIndex) ? GetItem(itemIndex) : null;

    private List<Vector4> ReadFirstSimulationPosePositions(uint posesArrayOffset)
    {
        var poseItems = ReadReferenceArray(posesArrayOffset);
        if (poseItems.Count == 0)
            return new List<Vector4>();
        var pose = GetItem(poseItems[0]);
        var positions = ReadArrayItem(pose.DataOffset + 32);
        if (positions is null)
            return new List<Vector4>();

        var result = new List<Vector4>(checked((int)positions.Count));
        for (uint index = 0; index < positions.Count; index++)
            result.Add(ReadVector4(checked(positions.DataOffset + index * 16u)));
        return result;
    }

    private uint GetFirstSimulationPosePositionOffset(uint posesArrayOffset, int particleIndex)
    {
        var poseItems = ReadReferenceArray(posesArrayOffset);
        if (poseItems.Count == 0)
            return 0;
        var pose = GetItem(poseItems[0]);
        var positions = ReadArrayItem(pose.DataOffset + 32);
        return positions is not null && particleIndex >= 0 && particleIndex < positions.Count
            ? checked(positions.DataOffset + (uint)(particleIndex * 16))
            : 0;
    }

    private bool TryReadStringPointer(uint dataOffset, out string value)
    {
        value = string.Empty;
        if (!TryGetReferencedItem(dataOffset, out var itemIndex) || DataSection is null)
            return false;
        var item = GetItem(itemIndex);
        if (item.DataOffset >= DataSection.PayloadSize)
            return false;

        var start = DataSection.PayloadOffset + checked((int)item.DataOffset);
        var end = DataSection.PayloadOffset + DataSection.PayloadSize;
        var cursor = start;
        while (cursor < end && Bytes[cursor] != 0)
            cursor++;
        if (cursor == end)
            return false;
        value = Encoding.UTF8.GetString(Bytes, start, cursor - start);
        return true;
    }

    private List<ushort> ReadUInt16Array(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return new List<ushort>();
        var result = new List<ushort>(checked((int)item.Count));
        for (uint index = 0; index < item.Count; index++)
        {
            var offset = checked(item.DataOffset + index * 2u);
            if (DataSection is null || offset > DataSection.PayloadSize - 2)
                throw new InvalidDataException("BPHCL UInt16 array exceeds DATA.");
            result.Add(ReadUInt16(offset));
        }
        return result;
    }

    private Vector4 ReadVector4(uint dataOffset) => new(
        ReadSingle(dataOffset),
        ReadSingle(dataOffset + 4u),
        ReadSingle(dataOffset + 8u),
        ReadSingle(dataOffset + 12u));

    private float ReadSingle(uint dataOffset)
    {
        if (DataSection is null || dataOffset > DataSection.PayloadSize - 4)
            throw new InvalidDataException("BPHCL float read exceeds DATA.");
        return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(DataSection.PayloadOffset + checked((int)dataOffset), 4)));
    }

    private int ReadInt32(uint dataOffset)
    {
        if (DataSection is null || dataOffset > DataSection.PayloadSize - 4)
            throw new InvalidDataException("BPHCL Int32 read exceeds DATA.");
        return BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan(DataSection.PayloadOffset + checked((int)dataOffset), 4));
    }

    private uint ReadUInt32(uint dataOffset)
    {
        if (DataSection is null || dataOffset > DataSection.PayloadSize - 4)
            throw new InvalidDataException("BPHCL UInt32 read exceeds DATA.");
        return BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(DataSection.PayloadOffset + checked((int)dataOffset), 4));
    }

    private ushort ReadUInt16(uint dataOffset)
    {
        if (DataSection is null || dataOffset > DataSection.PayloadSize - 2)
            throw new InvalidDataException("BPHCL UInt16 read exceeds DATA.");
        return BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(DataSection.PayloadOffset + checked((int)dataOffset), 2));
    }

    private byte ReadByte(uint dataOffset)
    {
        if (DataSection is null || dataOffset >= DataSection.PayloadSize)
            throw new InvalidDataException("BPHCL byte read exceeds DATA.");
        return Bytes[DataSection.PayloadOffset + checked((int)dataOffset)];
    }
}
