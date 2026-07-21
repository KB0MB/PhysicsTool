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
                var bufferDefinitions = ReadBufferDefinitions(item.DataOffset + objectHeaderSize + 24);
                var transformSetDefinitions = ReadTransformSetDefinitions(item.DataOffset + objectHeaderSize + 40);
                var operators = ReadOperatorLayouts(item.DataOffset + objectHeaderSize + 56);
                var states = ReadClothStates(item.DataOffset + objectHeaderSize + 72);
                var objectSpaceSkins = ReadObjectSpaceSkins(operators);
                var boneSpaceSkins = ReadBoneSpaceSkins(operators);
                var simpleMeshBoneDeformers = ReadSimpleMeshBoneDeformers(item.DataOffset);
                cloths.Add(new NativeBphclCloth(
                    index,
                    name,
                    itemIndex,
                    item.DataOffset,
                    simulations,
                    bufferDefinitions,
                    transformSetDefinitions,
                    operators,
                    states,
                    objectSpaceSkins,
                    boneSpaceSkins,
                    simpleMeshBoneDeformers));
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

    private IReadOnlyList<NativeBphclClothState> ReadClothStates(uint arrayFieldOffset)
    {
        var itemIndices = ReadReferenceArray(arrayFieldOffset);
        var result = new List<NativeBphclClothState>(itemIndices.Count);
        for (var index = 0; index < itemIndices.Count; index++)
        {
            var itemIndex = itemIndices[index];
            var item = GetItem(itemIndex);
            if (!TryReadStringPointer(item.DataOffset + 24, out var name))
                name = $"State {index}";
            result.Add(new NativeBphclClothState(
                index,
                itemIndex,
                name,
                ReadUInt32Array(ReadArrayItem(item.DataOffset + 32)).Select(value => checked((int)value)).ToArray(),
                ReadUInt32Array(ReadArrayItem(item.DataOffset + 80)).Select(value => checked((int)value)).ToArray()));
        }
        return result;
    }

    private NativeBphclSimCloth ReadSimCloth(int index, int itemIndex)
    {
        const uint objectHeaderSize = 24;
        var item = GetItem(itemIndex);
        if (!TryReadStringPointer(item.DataOffset + objectHeaderSize, out var name))
            name = $"Simulation {index}";

        var simulationInfo = new NativeBphclSimulationInfo(
            ReadVector4(item.DataOffset + 32),
            ReadSingle(item.DataOffset + 48));

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
        var simulationOperatorIds = ReadUInt32Array(ReadMemberArray(item, "m_simOpIds"));
        var staticCollisionMasks = ReadUInt32Array(ReadMemberArray(item, "m_staticCollisionMasks"));

        // Keep the per-simulation controls separate from SimulationInfo.
        // They are native hclSimClothData fields, rather than fields on the
        // embedded overridable simulation-info record.
        var runtime = new NativeBphclSimulationRuntime(
            ReadByte(item.DataOffset + 96) != 0,
            ReadSingle(item.DataOffset + 224),
            ReadSingle(item.DataOffset + 264),
            new NativeBphclTransferMotionData(
                ReadInt32(item.DataOffset + 268),
                ReadInt32(item.DataOffset + 272),
                ReadByte(item.DataOffset + 276) != 0,
                ReadSingle(item.DataOffset + 280),
                ReadSingle(item.DataOffset + 284),
                ReadSingle(item.DataOffset + 288),
                ReadSingle(item.DataOffset + 292),
                ReadByte(item.DataOffset + 296) != 0,
                ReadSingle(item.DataOffset + 300),
                ReadSingle(item.DataOffset + 304),
                ReadSingle(item.DataOffset + 308),
                ReadSingle(item.DataOffset + 312)),
            ReadByte(item.DataOffset + 316) != 0,
            ReadByte(item.DataOffset + 317) != 0,
            new NativeBphclLandscapeCollisionData(
                ReadSingle(item.DataOffset + 320),
                ReadByte(item.DataOffset + 324) != 0,
                ReadSingle(item.DataOffset + 328),
                ReadByte(item.DataOffset + 332) != 0,
                ReadByte(item.DataOffset + 333),
                ReadSingle(item.DataOffset + 336),
                ReadSingle(item.DataOffset + 340)),
            ReadUInt32(item.DataOffset + 344),
            ReadByte(item.DataOffset + 384) != 0,
            ReadByteArray(ReadArrayItem(item.DataOffset + 392)).Select(value => value != 0).ToArray(),
            ReadUInt16(item.DataOffset + 424),
            ReadUInt16(item.DataOffset + 426),
            ReadUInt32(item.DataOffset + 428));

        // The virtual cloth surface is stored directly on hclSimClothData.
        // These are the source triangles that connect particle indices; they
        // are essential when translating a BPHCL cloth into an HKCL layout.
        var triangleIndices = ReadUInt16Array(ReadArrayItem(item.DataOffset + 352));
        var triangleFlips = ReadByteArray(ReadArrayItem(item.DataOffset + 368));

        // BPHCL stores the transform set, bone indices, and local offsets used
        // to drive each active collider. This must travel with the colliders:
        // leaving the HKCL template's shorter map behind produces a readable
        // file but gives Havok the wrong collider-to-bone bindings at runtime.
        var collidableTransformMap = ReadCollidableTransformMap(item);

        // hclSimClothData::perInstanceCollidables is an hkArray<hclCollidable*>.
        // Keep the referenced ITEM indices so deletion can distinguish colliders
        // owned solely by one cloth from colliders shared with another cloth.
        var collidableItemIndices = ReadReferenceArray(item.DataOffset + 208);
        return new NativeBphclSimCloth(
            index,
            name,
            itemIndex,
            item.DataOffset,
            simulationInfo,
            particles,
            triangleIndices,
            triangleFlips,
            constraintSets,
            simulationOperatorIds,
            staticCollisionMasks,
            runtime,
            collidableTransformMap,
            collidableItemIndices);
    }

    private NativeBphclCollidableTransformMap ReadCollidableTransformMap(NativeBphclItem item)
    {
        try
        {
            return new NativeBphclCollidableTransformMap(
                checked((int)ReadUInt32(item.DataOffset + 168)),
                ReadUInt32Array(ReadArrayItem(item.DataOffset + 176)),
                ReadMatrix4Array(ReadArrayItem(item.DataOffset + 192)));
        }
        catch (Exception exception) when (
            exception is OverflowException or InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            // Some compatible BPHCL variants do not expose this inline map at
            // the older hclSimClothData offsets. The document can still be
            // inspected and edited; conversion will report the missing map
            // explicitly if it is required.
            return new NativeBphclCollidableTransformMap(
                0,
                Array.Empty<uint>(),
                Array.Empty<Matrix4x4>());
        }
    }

    private IReadOnlyList<NativeBphclBufferDefinition> ReadBufferDefinitions(uint arrayFieldOffset)
    {
        const uint objectHeaderSize = 24;
        var itemIndices = ReadReferenceArray(arrayFieldOffset);
        var result = new List<NativeBphclBufferDefinition>(itemIndices.Count);

        for (var index = 0; index < itemIndices.Count; index++)
        {
            var itemIndex = itemIndices[index];
            var item = GetItem(itemIndex);
            var className = GetTypeName(item.TypeIndex) ?? "unknown";
            if (!className.EndsWith("BufferDefinition", StringComparison.Ordinal))
                throw new InvalidDataException($"Expected an hcl buffer definition at ITEM {itemIndex}, found {className}.");

            var offset = item.DataOffset;
            if (!TryReadStringPointer(offset + objectHeaderSize, out var meshName))
                meshName = string.Empty;
            if (!TryReadStringPointer(offset + objectHeaderSize + 8, out var bufferName))
                bufferName = $"Buffer {index}";

            result.Add(new NativeBphclBufferDefinition(
                index,
                itemIndex,
                offset,
                className,
                meshName,
                bufferName,
                ReadInt32(offset + objectHeaderSize + 16),
                ReadInt32(offset + objectHeaderSize + 20),
                ReadUInt32(offset + objectHeaderSize + 24),
                ReadUInt32(offset + objectHeaderSize + 28)));
        }

        return result;
    }

    private IReadOnlyList<NativeBphclTransformSetDefinition> ReadTransformSetDefinitions(uint arrayFieldOffset)
    {
        const uint objectHeaderSize = 24;
        var itemIndices = ReadReferenceArray(arrayFieldOffset);
        var result = new List<NativeBphclTransformSetDefinition>(itemIndices.Count);

        for (var index = 0; index < itemIndices.Count; index++)
        {
            var itemIndex = itemIndices[index];
            var item = GetItem(itemIndex);
            var offset = item.DataOffset;
            if (!TryReadStringPointer(offset + objectHeaderSize, out var name))
                name = $"Transform set {index}";
            result.Add(new NativeBphclTransformSetDefinition(
                index,
                itemIndex,
                offset,
                name,
                ReadInt32(offset + objectHeaderSize + 8),
                ReadUInt32(offset + objectHeaderSize + 12)));
        }

        return result;
    }

    private IReadOnlyList<NativeBphclOperatorLayout> ReadOperatorLayouts(uint arrayFieldOffset)
    {
        var itemIndices = ReadReferenceArray(arrayFieldOffset);
        var result = new List<NativeBphclOperatorLayout>(itemIndices.Count);

        for (var index = 0; index < itemIndices.Count; index++)
        {
            var itemIndex = itemIndices[index];
            var item = GetItem(itemIndex);
            var className = GetTypeName(item.TypeIndex) ?? $"type {item.TypeIndex}";
            int? inputBuffer = null;
            int? outputBuffer = null;
            int? transformSet = null;
            int? vertexCount = null;
            int? startVertexIn = null;
            int? startVertexOut = null;
            IReadOnlyList<short> gatherMap = Array.Empty<short>();
            IReadOnlyList<NativeBphclVertexParticlePair> vertexParticlePairs = Array.Empty<NativeBphclVertexParticlePair>();
            int? simulationClothIndex = null;
            int? referenceBufferIndex = null;
            IReadOnlyList<NativeBphclSimulateConfig> simulateConfigs = Array.Empty<NativeBphclSimulateConfig>();

            switch (className)
            {
                case "hclGatherAllVerticesOperator":
                    gatherMap = ReadInt16Array(ReadArrayItem(item.DataOffset + 72));
                    inputBuffer = checked((int)ReadUInt32(item.DataOffset + 88));
                    outputBuffer = checked((int)ReadUInt32(item.DataOffset + 92));
                    break;
                case "hclCopyVerticesOperator":
                    inputBuffer = checked((int)ReadUInt32(item.DataOffset + 72));
                    outputBuffer = checked((int)ReadUInt32(item.DataOffset + 76));
                    vertexCount = checked((int)ReadUInt32(item.DataOffset + 80));
                    startVertexIn = checked((int)ReadUInt32(item.DataOffset + 84));
                    startVertexOut = checked((int)ReadUInt32(item.DataOffset + 88));
                    break;
                case "hclMoveParticlesOperator":
                    vertexParticlePairs = ReadVertexParticlePairs(ReadArrayItem(item.DataOffset + 72));
                    simulationClothIndex = checked((int)ReadUInt32(item.DataOffset + 88));
                    referenceBufferIndex = checked((int)ReadUInt32(item.DataOffset + 92));
                    break;
                case "hclSimulateOperator":
                    simulationClothIndex = checked((int)ReadUInt32(item.DataOffset + 72));
                    simulateConfigs = ReadSimulateConfigs(ReadArrayItem(item.DataOffset + 80));
                    break;
                case "hclObjectSpaceSkinPOperator":
                case "hclObjectSpaceSkinPNOperator":
                case "hclObjectSpaceSkinPNTOperator":
                case "hclObjectSpaceSkinPNTBOperator":
                case "hclBoneSpaceSkinPOperator":
                    outputBuffer = checked((int)ReadUInt32(item.DataOffset + 104));
                    transformSet = checked((int)ReadUInt32(item.DataOffset + 108));
                    break;
                case "hclSimpleMeshBoneDeformOperator":
                    inputBuffer = checked((int)ReadUInt32(item.DataOffset + 72));
                    transformSet = checked((int)ReadUInt32(item.DataOffset + 76));
                    break;
            }

            result.Add(new NativeBphclOperatorLayout(
                index,
                itemIndex,
                item.DataOffset,
                className,
                inputBuffer,
                outputBuffer,
                transformSet,
                vertexCount,
                startVertexIn,
                startVertexOut,
                gatherMap,
                vertexParticlePairs,
                simulationClothIndex,
                referenceBufferIndex,
                simulateConfigs));
        }

        return result;
    }

    private IReadOnlyList<NativeBphclVertexParticlePair> ReadVertexParticlePairs(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return Array.Empty<NativeBphclVertexParticlePair>();

        var result = new List<NativeBphclVertexParticlePair>(checked((int)item.Count));
        for (uint index = 0; index < item.Count; index++)
        {
            var offset = checked(item.DataOffset + index * 4u);
            result.Add(new NativeBphclVertexParticlePair(ReadUInt16(offset), ReadUInt16(offset + 2u)));
        }
        return result;
    }

    private IReadOnlyList<NativeBphclSimulateConfig> ReadSimulateConfigs(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return Array.Empty<NativeBphclSimulateConfig>();

        // hclSimulateOperator::Config is 0x30 bytes. The first array controls
        // constraint execution. The pointer-like field at +0x18 is not an
        // hkArray in every known BPHCL layout, so it must not be followed as
        // one until its native representation is fully documented.
        const uint configSize = 48;
        var result = new List<NativeBphclSimulateConfig>(checked((int)item.Count));
        for (uint index = 0; index < item.Count; index++)
        {
            var offset = checked(item.DataOffset + index * configSize);
            result.Add(new NativeBphclSimulateConfig(
                ReadInt32Array(ReadArrayItem(offset + 8u)),
                Array.Empty<int>(),
                ReadByte(offset + 40u),
                ReadByte(offset + 41u),
                ReadByte(offset + 42u) != 0,
                ReadByte(offset + 43u) != 0));
        }
        return result;
    }

    private IReadOnlyList<int> ReadInt32Array(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return Array.Empty<int>();

        var result = new int[checked((int)item.Count)];
        for (uint index = 0; index < item.Count; index++)
            result[index] = unchecked((int)ReadUInt32(checked(item.DataOffset + index * 4u)));
        return result;
    }

    private IReadOnlyList<NativeBphclObjectSpaceSkin> ReadObjectSpaceSkins(
        IReadOnlyList<NativeBphclOperatorLayout> operators)
    {
        var result = new List<NativeBphclObjectSpaceSkin>();
        foreach (var op in operators)
        {
            if (!string.Equals(op.ClassName, "hclObjectSpaceSkinPOperator", StringComparison.Ordinal))
                continue;

            var item = GetItem(op.ItemIndex);
            var matrices = ReadMatrix4Array(ReadMemberArray(item, "boneFromSkinMeshTransforms"));
            var subset = ReadUInt16Array(ReadMemberArray(item, "transformSubset"));
            var localPs = ReadPackedPositionBlocks(ReadMemberArray(item, "localPs"));
            var unpacked = ReadPositionBlocks(ReadMemberArray(item, "localUnpackedPs"), positionsPerBlock: 16);
            var deformerOffset = GetMemberOffset(item.TypeIndex, "objectSpaceDeformer") is { } offset
                ? checked(item.DataOffset + offset)
                : throw new InvalidDataException("BPHCL object-space skin operator has no reflected objectSpaceDeformer field.");

            var deformer = ReadObjectSpaceDeformer(deformerOffset);
            result.Add(new NativeBphclObjectSpaceSkin(
                op.Index,
                op.ItemIndex,
                op.DataOffset,
                matrices,
                subset,
                op.OutputBufferIndex ?? 0,
                op.TransformSetIndex ?? 0,
                deformer,
                localPs,
                unpacked));
        }

        return result;
    }

    private NativeBphclObjectSpaceDeformer ReadObjectSpaceDeformer(uint dataOffset)
    {
        // hclObjectSpaceDeformer is an inline 0x98-byte value. Its first
        // eight hkArrays are blend-entry blocks, followed by control bytes.
        var arrays = Enumerable.Range(0, 8)
            .Select(index => ReadPackedArray(ReadArrayItem(checked(dataOffset + (uint)(index * 16)))))
            .ToArray();
        var controls = ReadByteArray(ReadArrayItem(dataOffset + 128));
        return new NativeBphclObjectSpaceDeformer(
            arrays[0], arrays[1], arrays[2], arrays[3], arrays[4], arrays[5], arrays[6], arrays[7],
            controls,
            ReadUInt16(dataOffset + 144),
            ReadUInt16(dataOffset + 146),
            ReadByte(dataOffset + 151) != 0);
    }

    private IReadOnlyList<NativeBphclBoneSpaceSkin> ReadBoneSpaceSkins(
        IReadOnlyList<NativeBphclOperatorLayout> operators)
    {
        var result = new List<NativeBphclBoneSpaceSkin>();
        foreach (var op in operators)
        {
            if (!string.Equals(op.ClassName, "hclBoneSpaceSkinPOperator", StringComparison.Ordinal))
                continue;

            var item = GetItem(op.ItemIndex);
            var subset = ReadUInt16Array(ReadMemberArray(item, "transformSubset"));
            var localPs = ReadPositionBlocks(ReadMemberArray(item, "localPs"), positionsPerBlock: 16);
            var unpacked = ReadPositionBlocks(ReadMemberArray(item, "localUnpackedPs"), positionsPerBlock: 16);
            var deformerOffset = GetMemberOffset(item.TypeIndex, "boneSpaceDeformer") is { } offset
                ? checked(item.DataOffset + offset)
                : throw new InvalidDataException("BPHCL bone-space skin operator has no reflected boneSpaceDeformer field.");

            result.Add(new NativeBphclBoneSpaceSkin(
                op.Index,
                op.ItemIndex,
                op.DataOffset,
                subset,
                op.OutputBufferIndex ?? 0,
                op.TransformSetIndex ?? 0,
                ReadBoneSpaceDeformer(deformerOffset),
                localPs,
                unpacked));
        }

        return result;
    }

    private NativeBphclBoneSpaceDeformer ReadBoneSpaceDeformer(uint dataOffset)
    {
        // hclBoneSpaceDeformer has four hkArray<Block> fields followed by
        // controls and a compact range/batch header. Its block type carries
        // the exact variable vertex count for each blend family.
        var arrays = Enumerable.Range(0, 4)
            .Select(index => ReadPackedArray(ReadArrayItem(checked(dataOffset + (uint)(index * 16)))))
            .ToArray();
        return new NativeBphclBoneSpaceDeformer(
            arrays[0], arrays[1], arrays[2], arrays[3],
            ReadByteArray(ReadArrayItem(dataOffset + 64)),
            ReadUInt16(dataOffset + 80),
            ReadUInt16(dataOffset + 82),
            ReadUInt16(dataOffset + 84),
            ReadByte(dataOffset + 86) != 0);
    }

    private IReadOnlyList<NativeBphclPackedArray> ReadPackedArray(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return Array.Empty<NativeBphclPackedArray>();

        var elementType = GetTypeName(item.TypeIndex) ?? $"type {item.TypeIndex}";
        var elementSize = TypeDefinitions.FirstOrDefault(type => type.TypeIndex == item.TypeIndex)?.Size;
        if (elementSize is not > 0)
            throw new InvalidDataException($"BPHCL packed skinning array {elementType} has no reflected element size.");

        var byteCount = checked((int)(item.Count * elementSize.Value));
        if (DataSection is null || item.DataOffset > DataSection.PayloadSize || byteCount > DataSection.PayloadSize - item.DataOffset)
            throw new InvalidDataException($"BPHCL packed skinning array {elementType} exceeds DATA bounds.");

        var bytes = new byte[byteCount];
        Buffer.BlockCopy(Bytes, DataSection.PayloadOffset + checked((int)item.DataOffset), bytes, 0, byteCount);
        return new[] { new NativeBphclPackedArray(elementType, checked((int)elementSize.Value), bytes) };
    }

    private IReadOnlyList<NativeBphclPositionBlock> ReadPositionBlocks(
        NativeBphclItem? item,
        int positionsPerBlock)
    {
        if (item is null || item.Count == 0)
            return Array.Empty<NativeBphclPositionBlock>();

        var result = new List<NativeBphclPositionBlock>(checked((int)item.Count));
        for (uint block = 0; block < item.Count; block++)
        {
            var positions = new Vector4[positionsPerBlock];
            var blockOffset = checked(item.DataOffset + block * (uint)(positionsPerBlock * 16));
            for (var index = 0; index < positions.Length; index++)
                positions[index] = ReadVector4(checked(blockOffset + (uint)(index * 16)));
            result.Add(new NativeBphclPositionBlock(positions));
        }
        return result;
    }

    private IReadOnlyList<NativeBphclPackedPositionBlock> ReadPackedPositionBlocks(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return Array.Empty<NativeBphclPackedPositionBlock>();

        const int positionsPerBlock = 64;
        const int bytesPerBlock = positionsPerBlock * sizeof(short);
        var result = new List<NativeBphclPackedPositionBlock>(checked((int)item.Count));
        for (uint block = 0; block < item.Count; block++)
        {
            var positions = new short[positionsPerBlock];
            var blockOffset = checked(item.DataOffset + block * bytesPerBlock);
            for (var index = 0; index < positions.Length; index++)
                positions[index] = unchecked((short)ReadUInt16(checked(blockOffset + (uint)(index * sizeof(short)))));
            result.Add(new NativeBphclPackedPositionBlock(positions));
        }
        return result;
    }

    private IReadOnlyList<Matrix4x4> ReadMatrix4Array(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return Array.Empty<Matrix4x4>();

        var result = new List<Matrix4x4>(checked((int)item.Count));
        for (uint index = 0; index < item.Count; index++)
        {
            var offset = checked(item.DataOffset + index * 64u);
            var r0 = ReadVector4(offset);
            var r1 = ReadVector4(offset + 16);
            var r2 = ReadVector4(offset + 32);
            var r3 = ReadVector4(offset + 48);
            result.Add(new Matrix4x4(
                r0.X, r0.Y, r0.Z, r0.W,
                r1.X, r1.Y, r1.Z, r1.W,
                r2.X, r2.Y, r2.Z, r2.W,
                r3.X, r3.Y, r3.Z, r3.W));
        }
        return result;
    }

    private NativeBphclItem? ReadMemberArray(NativeBphclItem item, string memberName)
    {
        var offset = GetMemberOffset(item.TypeIndex, memberName);
        return offset is null ? null : ReadArrayItem(checked(item.DataOffset + offset.Value));
    }

    private uint? GetMemberOffset(uint typeIndex, string memberName)
    {
        var visited = new HashSet<uint>();
        while (typeIndex != 0 && visited.Add(typeIndex))
        {
            var type = TypeDefinitions.FirstOrDefault(definition => definition.TypeIndex == typeIndex);
            if (type is null)
                return null;
            var member = type.Members.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, memberName, StringComparison.Ordinal));
            if (member is not null)
                return member.Offset;
            typeIndex = type.ParentTypeIndex;
        }
        return null;
    }

    private IReadOnlyList<NativeBphclSimpleMeshBoneDeform> ReadSimpleMeshBoneDeformers(uint clothOffset)
    {
        // hclClothData::operators lives at +0x50. The base hclOperator fields
        // are followed by hclSimpleMeshBoneDeformOperator's mesh/bone data.
        var operatorItems = ReadReferenceArray(clothOffset + 80);
        var result = new List<NativeBphclSimpleMeshBoneDeform>();

        for (var operatorIndex = 0; operatorIndex < operatorItems.Count; operatorIndex++)
        {
            var itemIndex = operatorItems[operatorIndex];
            var item = GetItem(itemIndex);
            if (!string.Equals(GetTypeName(item.TypeIndex), "hclSimpleMeshBoneDeformOperator", StringComparison.Ordinal))
                continue;

            var pairs = ReadTriangleBonePairs(ReadArrayItem(item.DataOffset + 80));
            // Reflection and native storage both identify this as hkArray<hkMatrix4>.
            // It is not an hkQsTransform array; treating it as one shifts every
            // element after the first and produces extreme output-bone stretching.
            var localTransforms = ReadMatrix4Array(ReadArrayItem(item.DataOffset + 96));
            result.Add(new NativeBphclSimpleMeshBoneDeform(
                operatorIndex,
                itemIndex,
                item.DataOffset,
                ReadUInt32(item.DataOffset + 72),
                ReadUInt32(item.DataOffset + 76),
                pairs,
                localTransforms,
                ReadInt32(item.DataOffset + 112)));
        }

        return result;
    }

    private List<NativeBphclTriangleBonePair> ReadTriangleBonePairs(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return new List<NativeBphclTriangleBonePair>();

        var result = new List<NativeBphclTriangleBonePair>(checked((int)item.Count));
        for (uint index = 0; index < item.Count; index++)
        {
            var offset = checked(item.DataOffset + index * 4u);
            result.Add(new NativeBphclTriangleBonePair(ReadUInt16(offset), ReadUInt16(offset + 2u)));
        }
        return result;
    }

    private List<NativeBphclQsTransform> ReadQsTransformArray(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return new List<NativeBphclQsTransform>();

        var result = new List<NativeBphclQsTransform>(checked((int)item.Count));
        for (uint index = 0; index < item.Count; index++)
        {
            var offset = checked(item.DataOffset + index * 48u);
            result.Add(new NativeBphclQsTransform(
                ReadVector4(offset),
                ReadVector4(offset + 16u),
                ReadVector4(offset + 32u)));
        }
        return result;
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
            ReadConstraintLinks(item.DataOffset, className),
            ReadLocalRangeConstraints(item.DataOffset, className),
            ReadTransitionParticleData(item.DataOffset, className),
            ReadConstraintValues(item.DataOffset, className));
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

        return ReadConstraintRecords(linkArray, linkType);
    }

    private IReadOnlyList<NativeBphclConstraintLink> ReadLocalRangeConstraints(uint constraintOffset, string className)
    {
        if (!string.Equals(className, "hclLocalRangeConstraintSet", StringComparison.Ordinal))
            return Array.Empty<NativeBphclConstraintLink>();

        var localType = TypeDefinitions.FirstOrDefault(type =>
            string.Equals(GetTypeName(type.TypeIndex), "hclLocalRangeConstraintSet::LocalConstraint", StringComparison.Ordinal));
        var stiffnessType = TypeDefinitions.FirstOrDefault(type =>
            string.Equals(GetTypeName(type.TypeIndex), "hclLocalRangeConstraintSet::LocalStiffnessConstraint", StringComparison.Ordinal));
        if (localType is null || localType.Members.Count == 0)
            return Array.Empty<NativeBphclConstraintLink>();

        // TotK commonly stores these in localStiffnessConstraints rather than
        // localConstraints. Both layouts describe the same local range, with
        // the stiffness form carrying one extra per-record value.
        var regular = ReadConstraintRecords(ReadArrayItem(constraintOffset + 40), localType);
        if (regular.Count > 0)
            return regular;

        return stiffnessType is null || stiffnessType.Members.Count == 0
            ? Array.Empty<NativeBphclConstraintLink>()
            : ReadConstraintRecords(ReadArrayItem(constraintOffset + 56), stiffnessType);
    }

    private IReadOnlyList<NativeBphclConstraintLink> ReadTransitionParticleData(uint constraintOffset, string className)
    {
        if (!string.Equals(className, "hclTransitionConstraintSet", StringComparison.Ordinal))
            return Array.Empty<NativeBphclConstraintLink>();

        var recordType = TypeDefinitions.FirstOrDefault(type =>
            string.Equals(GetTypeName(type.TypeIndex), "hclTransitionConstraintSet::PerParticle", StringComparison.Ordinal));
        return recordType is null || recordType.Members.Count == 0
            ? Array.Empty<NativeBphclConstraintLink>()
            : ReadConstraintRecords(ReadArrayItem(constraintOffset + 40), recordType);
    }

    private IReadOnlyDictionary<string, double> ReadConstraintValues(uint constraintOffset, string className)
    {
        var type = TypeDefinitions.FirstOrDefault(definition =>
            string.Equals(GetTypeName(definition.TypeIndex), className, StringComparison.Ordinal));
        if (type is null)
            return new Dictionary<string, double>(StringComparer.Ordinal);

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var member in type.Members)
        {
            var value = ReadPrimitive(member.TypeIndex, constraintOffset + member.Offset);
            if (value is not null)
                values[member.Name] = value.Value;
        }
        return values;
    }

    private IReadOnlyList<NativeBphclConstraintLink> ReadConstraintRecords(
        NativeBphclItem? recordArray,
        NativeBphclTypeDefinition recordType)
    {
        if (recordArray is null || recordArray.Count == 0)
            return Array.Empty<NativeBphclConstraintLink>();

        var stride = GetStructStride(recordType.Members);
        if (stride <= 0 || recordArray.Count > int.MaxValue / stride)
            throw new InvalidDataException($"Invalid BPHCL constraint record array for {GetTypeName(recordType.TypeIndex)}.");

        var links = new List<NativeBphclConstraintLink>(checked((int)recordArray.Count));
        for (uint index = 0; index < recordArray.Count; index++)
        {
            var linkOffset = checked(recordArray.DataOffset + index * (uint)stride);
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            int? particleA = null;
            int? particleB = null;

            foreach (var member in recordType.Members)
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

    public NativeBphclArrayLayout DescribeArrayLayout(uint arrayFieldOffset)
    {
        if (!TryGetReferencedItem(arrayFieldOffset, out var storageItemIndex))
            throw new InvalidDataException($"BPHCL array at DATA+0x{arrayFieldOffset:X} has no ITEM reference.");

        var storageItem = GetItem(storageItemIndex);
        var range = GetItemRanges()[storageItemIndex];
        var patch = InternalPatches.FirstOrDefault(group => group.Offsets.Contains(storageItem.DataOffset));
        var entryTypeIndex = patch?.TypeIndex ?? storageItem.TypeIndex;
        return new NativeBphclArrayLayout(
            storageItemIndex,
            storageItem.Count,
            entryTypeIndex,
            GetTypeName(entryTypeIndex),
            range.End - range.Start);
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

    private List<uint> ReadUInt32Array(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return new List<uint>();
        var result = new List<uint>(checked((int)item.Count));
        for (uint index = 0; index < item.Count; index++)
        {
            var offset = checked(item.DataOffset + index * 4u);
            if (DataSection is null || offset > DataSection.PayloadSize - 4)
                throw new InvalidDataException("BPHCL UInt32 array exceeds DATA.");
            result.Add(ReadUInt32(offset));
        }
        return result;
    }

    private List<short> ReadInt16Array(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return new List<short>();
        var result = new List<short>(checked((int)item.Count));
        for (uint index = 0; index < item.Count; index++)
        {
            var offset = checked(item.DataOffset + index * 2u);
            if (DataSection is null || offset > DataSection.PayloadSize - 2)
                throw new InvalidDataException("BPHCL Int16 array exceeds DATA.");
            result.Add(BinaryPrimitives.ReadInt16LittleEndian(Bytes.AsSpan(DataSection.PayloadOffset + checked((int)offset), 2)));
        }
        return result;
    }

    private List<byte> ReadByteArray(NativeBphclItem? item)
    {
        if (item is null || item.Count == 0)
            return new List<byte>();
        if (DataSection is null || item.DataOffset > DataSection.PayloadSize ||
            item.Count > DataSection.PayloadSize - item.DataOffset)
        {
            throw new InvalidDataException("BPHCL byte array exceeds DATA.");
        }

        var result = new List<byte>(checked((int)item.Count));
        for (uint index = 0; index < item.Count; index++)
            result.Add(ReadByte(item.DataOffset + index));
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
