using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace HKCLTool;

// Writes the initial native object graph used by donor-free HKCL -> BPHCL
// conversion. It builds the common small-chain execution pipeline from HKCL
// data: buffers, fixed-particle motion, simulation, mesh-to-bone output, and
// pose copies. Packed skin/deformer blocks are deliberately kept separate;
// those need their own lossless extractor rather than guessed SIMD records.
public static class NativeBphclStandaloneGraphWriter
{
    private const int ObjectHeaderSize = 24;

    public static byte[] Write(StandaloneBphclDocumentGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        graph.Validate();

        var builder = new DataBuilder();
        var orderedOuterColliders = graph.OuterColliders
            .OrderBy(collider => collider.Index)
            .ToArray();
        var root = CreateRootScaffold(builder, graph.Cloths.Count, orderedOuterColliders.Length);
        var rootVariants = new List<RootVariant>();
        var colliderItemsByIndex = orderedOuterColliders.ToDictionary(
            collider => collider.Index,
            collider => WriteCollider(builder, collider));
        var allColliderItems = orderedOuterColliders
            .Select(collider => colliderItemsByIndex[collider.Index])
            .ToArray();

        foreach (var cloth in graph.Cloths)
        {
            var skeletonItem = WriteSkeleton(builder, cloth);
            var transformSetItem = WriteTransformSetDefinition(builder, cloth);
            var colliderItems = cloth.Colliders
                .Select(collider => colliderItemsByIndex.TryGetValue(collider.Index, out var itemIndex)
                    ? itemIndex
                    : throw new InvalidDataException(
                        $"Cloth '{cloth.Name}' references missing outer collider {collider.Index}."))
                .ToArray();
            var constraintItems = WriteConstraintSets(builder, cloth);
            var bufferItems = WriteBufferDefinitions(builder, cloth);
            var runtime = WriteRuntimePipeline(builder, cloth, constraintItems.Count);
            var simulationItem = WriteSimulation(
                builder,
                cloth,
                colliderItems,
                constraintItems,
                runtime.SimulateOperatorIndex);
            var clothItem = WriteCloth(
                builder,
                cloth,
                simulationItem,
                transformSetItem,
                bufferItems,
                runtime.OperatorItems,
                runtime.StateItems);

            rootVariants.Add(new RootVariant(
                cloth.Name,
                "hclClothContainer",
                clothItem,
                skeletonItem));
        }

        // One container owns all cloth and collider entries. The root ITEMs
        // themselves were allocated at the beginning to mirror native BPHCL
        // ownership order; fill their arrays only after the graph is complete.
        FillClothContainer(
            builder,
            root,
            rootVariants.Select(variant => variant.ClothItem).ToArray(),
            allColliderItems);
        FillResourceContainer(builder, root.ResourceContainerDataOffset);
        FillAnimationContainer(
            builder,
            root.AnimationContainerDataOffset,
            rootVariants.Select(variant => variant.SkeletonItem).ToArray());

        // BPHCL stores hkArray element counts in ITEM. Retail files leave the
        // inline hkArray size/capacity words zeroed, including every nested
        // array header. Keeping fresh files in that native convention avoids
        // a second, conflicting ownership description for the Havok loader.
        builder.NormalizeHavokArrayHeaders();
        return NativeBphclStandaloneEnvelopeWriter.Create(
            builder.ToDataPayload(),
            StandaloneTypeCatalog.CreateSection(),
            builder.Items,
            builder.BuildPatches(),
            NativeAampStandaloneWriter.Create(graph));
    }

    private static RootScaffold CreateRootScaffold(
        DataBuilder builder,
        int clothCount,
        int collidableCount)
    {
        // Native BPHCL reserves ITEM 0 as a null sentinel and ITEM 1 for the
        // root object. The next item identities and DATA layout are stable:
        // variants, six strings, cloth container, its two hkRefPtr arrays,
        // resource container, then animation container.
        var variants = builder.AllocateArray(StandaloneTypeCatalog.NamedVariant, 3, 24, alignment: 8);
        builder.WriteReference(0, variants.ItemIndex);
        builder.WriteUInt32(8, 3);

        var clothReservation = builder.ReserveObject(StandaloneTypeCatalog.ClothContainer);
        var resourceLayout = NativeBphclReflectionCatalog.GetTypeLayout("hkMemoryResourceContainer");
        var resourceReservation = builder.ReserveObject(resourceLayout.TypeIndex);
        var animationLayout = NativeBphclReflectionCatalog.GetTypeLayout("hkaAnimationContainer");
        var animationReservation = builder.ReserveObject(animationLayout.TypeIndex);

        // Native groups root names first, then their runtime class names.
        var clothName = builder.WriteString("Cloth Container");
        var resourceName = builder.WriteString("Resource Data");
        var animationName = builder.WriteString("Animation Container");
        var clothClass = builder.WriteString("hclClothContainer");
        var resourceClass = builder.WriteString("hkMemoryResourceContainer");
        var animationClass = builder.WriteString("hkaAnimationContainer");

        WriteNamedVariant(builder, variants.DataOffset, clothName, clothClass, clothReservation.ItemIndex);
        WriteNamedVariant(builder, variants.DataOffset + 24, resourceName, resourceClass, resourceReservation.ItemIndex);
        WriteNamedVariant(builder, variants.DataOffset + 48, animationName, animationClass, animationReservation.ItemIndex);

        var clothContainer = builder.Materialize(clothReservation, 56, alignment: 16);
        var collidableArrayType = RequireArrayElementType(StandaloneTypeCatalog.ClothContainer, ObjectHeaderSize);
        var clothArrayType = RequireArrayElementType(StandaloneTypeCatalog.ClothContainer, ObjectHeaderSize + 16);
        var collidableStorage = builder.AllocateArray(collidableArrayType, collidableCount, 8, alignment: 16);
        var clothStorage = builder.AllocateArray(clothArrayType, clothCount, 8, alignment: 8);
        var resourceContainer = builder.Materialize(resourceReservation, RequireSize(resourceLayout, 72), alignment: 8);
        var animationContainer = builder.Materialize(animationReservation, RequireSize(animationLayout, 104));

        builder.WriteReference(clothContainer.DataOffset + ObjectHeaderSize, collidableStorage.ItemIndex);
        builder.WriteUInt32(clothContainer.DataOffset + ObjectHeaderSize + 8, checked((uint)collidableCount));
        builder.WriteReference(clothContainer.DataOffset + ObjectHeaderSize + 16, clothStorage.ItemIndex);
        builder.WriteUInt32(clothContainer.DataOffset + ObjectHeaderSize + 24, checked((uint)clothCount));

        return new RootScaffold(
            clothContainer.DataOffset,
            resourceContainer.DataOffset,
            animationContainer.DataOffset,
            collidableStorage,
            clothStorage);
    }

    private static void WriteNamedVariant(DataBuilder builder, uint dataOffset, int nameItem, int classItem, int objectItem)
    {
        builder.WriteReference(dataOffset, nameItem);
        builder.WriteReference(dataOffset + 8, classItem);
        builder.WriteReference(dataOffset + 16, objectItem);
    }

    private static void FillClothContainer(
        DataBuilder builder,
        RootScaffold root,
        IReadOnlyList<int> clothItems,
        IReadOnlyList<int> colliderItems)
    {
        if (clothItems.Count != root.ClothStorage.Count || colliderItems.Count != root.CollidableStorage.Count)
            throw new InvalidOperationException("BPHCL root reference storage does not match the generated cloth graph.");

        for (var index = 0; index < colliderItems.Count; index++)
            builder.WriteReference(root.CollidableStorage.DataOffset + checked((uint)(index * 8)), colliderItems[index]);
        for (var index = 0; index < clothItems.Count; index++)
            builder.WriteReference(root.ClothStorage.DataOffset + checked((uint)(index * 8)), clothItems[index]);
    }

    private static void FillResourceContainer(DataBuilder builder, uint resourceDataOffset)
    {
        // Native cloth files include an empty resource container. Its name is an
        // empty string and both resource/child arrays remain null.
        builder.WriteReference(resourceDataOffset + ObjectHeaderSize, builder.WriteString(string.Empty));
    }

    private static void FillAnimationContainer(
        DataBuilder builder,
        uint containerDataOffset,
        IReadOnlyList<int> skeletonItems)
    {
        var skeletonArray = builder.WriteReferenceArray(skeletonItems);
        builder.WriteReference(containerDataOffset + ObjectHeaderSize, skeletonArray.ItemIndex);
        builder.WriteUInt32(containerDataOffset + ObjectHeaderSize + 8, checked((uint)skeletonItems.Count));
    }

    private static int WriteCloth(
        DataBuilder builder,
        StandaloneBphclClothGraph cloth,
        int simulationItem,
        int transformSetItem,
        IReadOnlyList<int> bufferItems,
        IReadOnlyList<int> operatorItems,
        IReadOnlyList<int> stateItems)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclClothData");
        var data = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 112), alignment: 16);
        var simulations = builder.WriteReferenceArray(new[] { simulationItem });
        var transformSets = builder.WriteReferenceArray(new[] { transformSetItem });
        var buffers = builder.WriteReferenceArray(bufferItems);
        var operators = builder.WriteReferenceArray(operatorItems);
        var states = builder.WriteReferenceArray(stateItems);
        builder.WriteReference(data.DataOffset + ObjectHeaderSize, builder.WriteString(cloth.Name));
        builder.WriteReference(data.DataOffset + ObjectHeaderSize + 8, simulations.ItemIndex);
        builder.WriteUInt32(data.DataOffset + ObjectHeaderSize + 16, 1);
        builder.WriteReference(data.DataOffset + 48, buffers.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 56, checked((uint)bufferItems.Count));
        builder.WriteReference(data.DataOffset + 64, transformSets.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 72, 1);
        builder.WriteReference(data.DataOffset + 80, operators.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 88, checked((uint)operatorItems.Count));
        builder.WriteReference(data.DataOffset + 96, states.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 104, checked((uint)stateItems.Count));

        // Native TotK BPHCL serializes this as the Switch platform identifier.
        // Leaving the enum at zero produces an internally readable graph but
        // makes Havok select an invalid runtime layout while loading it.
        builder.WriteUInt32(data.DataOffset + 148, 0x0002_0000u);
        return data.ItemIndex;
    }

    private static int WriteTransformSetDefinition(DataBuilder builder, StandaloneBphclClothGraph cloth)
    {
        var source = cloth.Runtime.TransformSets.SingleOrDefault();
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclTransformSetDefinition");
        var data = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 40), alignment: 8);
        builder.WriteReference(data.DataOffset + ObjectHeaderSize, builder.WriteString(source?.Name ?? cloth.SkeletonName));
        builder.WriteInt32(data.DataOffset + 32, source?.Type ?? 1);
        builder.WriteUInt32(data.DataOffset + 36, checked((uint)(source?.TransformCount ?? cloth.Bones.Count)));
        return data.ItemIndex;
    }

    private static IReadOnlyList<int> WriteBufferDefinitions(DataBuilder builder, StandaloneBphclClothGraph cloth)
    {
        if (cloth.Runtime.Buffers.Count > 0)
        {
            return cloth.Runtime.Buffers
                .OrderBy(buffer => buffer.Index)
                .Select(buffer => WriteSourceBufferDefinition(builder, cloth, buffer))
                .ToArray();
        }

        var scratchLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclScratchBufferDefinition");
        var scratch = builder.AllocateObject(scratchLayout.TypeIndex, RequireSize(scratchLayout, 112), alignment: 16);
        WriteBufferBase(
            builder,
            scratch.DataOffset,
            cloth.Name,
            cloth.Name + "_Scratch",
            type: 6,
            subType: 0,
            vertexCount: cloth.Particles.Count,
            triangleCount: 0);

        // Native BPHCL leaves the scratch buffer's optional triangle list empty.
        // Simulation geometry is stored by hclSimClothData, not duplicated here.
        return new[]
        {
            scratch.ItemIndex,
            WriteBufferDefinition(builder, cloth, cloth.Name + "_Current", type: 1),
            WriteBufferDefinition(builder, cloth, cloth.Name + "_Previous", type: 2)
        };
    }

    private static int WriteSourceBufferDefinition(
        DataBuilder builder,
        StandaloneBphclClothGraph cloth,
        ClothBufferDefinitionData source)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout(source.ClassName);
        var buffer = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 88), alignment: 16);

        // HKCL exports its scratch buffer with the cloth name in `name` and
        // leaves `meshName` empty. BPHCL uses the cloth name as the mesh owner
        // for every buffer, with stable runtime buffer labels. Keep the source
        // capacity/topology; only translate this format-level naming convention.
        var meshName = cloth.Name;
        var bufferName = source.Type switch
        {
            6 => "SkinScratchBuf",
            1 => "SimCurrentBuf",
            2 => "SimPrevBuf",
            _ => string.IsNullOrWhiteSpace(source.BufferName) ? $"Buffer{source.Index}" : source.BufferName
        };
        WriteBufferBase(
            builder,
            buffer.DataOffset,
            meshName,
            bufferName,
            source.Type,
            source.SubType,
            source.VertexCount,
            source.TriangleCount);

        return buffer.ItemIndex;
    }

    private static int WriteBufferDefinition(DataBuilder builder, StandaloneBphclClothGraph cloth, string bufferName, int type)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclBufferDefinition");
        var buffer = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 88), alignment: 16);
        WriteBufferBase(
            builder,
            buffer.DataOffset,
            cloth.Name,
            bufferName,
            type,
            subType: 0,
            vertexCount: cloth.Particles.Count,
            triangleCount: cloth.Triangles.Count);
        return buffer.ItemIndex;
    }

    private static void WriteBufferBase(
        DataBuilder builder,
        uint dataOffset,
        string meshName,
        string bufferName,
        int type,
        int subType,
        int vertexCount,
        int triangleCount)
    {
        builder.WriteReference(dataOffset + 24, builder.WriteString(meshName));
        builder.WriteReference(dataOffset + 32, builder.WriteString(bufferName));
        builder.WriteInt32(dataOffset + 40, type);
        builder.WriteInt32(dataOffset + 44, subType);
        builder.WriteUInt32(dataOffset + 48, checked((uint)vertexCount));
        builder.WriteUInt32(dataOffset + 52, checked((uint)triangleCount));

        // Native BPHCL uses this inline layout for every position-only cloth
        // buffer. A zeroed hclBufferLayout is not a valid equivalent: it
        // omits the position element descriptor that Havok needs at runtime.
        builder.WriteUInt32(dataOffset + 56, 0x0000_1000u);
        builder.WriteUInt32(dataOffset + 60, 250u);
        builder.WriteUInt32(dataOffset + 64, 250u);
        builder.WriteUInt32(dataOffset + 68, 250u);
        builder.WriteUInt32(dataOffset + 72, 0x0000_1001u);
        builder.WriteUInt32(dataOffset + 76, 0u);
        builder.WriteByte(dataOffset + 80, 1);
        builder.WriteByte(dataOffset + 81, type == 6 ? (byte)2 : (byte)1);
    }

    private static RuntimePipeline WriteRuntimePipeline(
        DataBuilder builder,
        StandaloneBphclClothGraph cloth,
        int constraintSetCount)
    {
        if (cloth.Runtime.Operators.Count > 0)
            return WriteSourceRuntimePipeline(builder, cloth, constraintSetCount);

        var operators = new List<int>();
        var skinIndices = new List<int>();
        foreach (var skin in cloth.SkinOperators)
        {
            var skinIndex = operators.Count;
            skinIndices.Add(skinIndex);
            operators.Add(WriteSkinOperator(builder, skin, skinIndex));
        }

        var moveIndex = operators.Count;
        operators.Add(WriteMoveParticlesOperator(builder, cloth, moveIndex));

        var simulateIndex = operators.Count;
        operators.Add(WriteSimulateOperator(builder, cloth, constraintSetCount, simulateIndex));

        var deformerIndices = new List<int>();
        foreach (var deformer in cloth.BoneDeformers)
        {
            var deformerIndex = operators.Count;
            deformerIndices.Add(deformerIndex);
            operators.Add(WriteSimpleMeshBoneDeformOperator(builder, deformer, deformerIndex));
        }

        var currentCopyIndex = operators.Count;
        operators.Add(WriteCopyVerticesOperator(builder, inputBufferIndex: 0, outputBufferIndex: 1, cloth.Particles.Count, operatorId: currentCopyIndex));
        var previousCopyIndex = operators.Count;
        operators.Add(WriteCopyVerticesOperator(builder, inputBufferIndex: 0, outputBufferIndex: 2, cloth.Particles.Count, operatorId: previousCopyIndex));

        var defaultOperators = skinIndices.Concat(new[] { moveIndex, simulateIndex }).Concat(deformerIndices).ToArray();
        var stateItems = new[]
        {
            WriteClothState(builder, "Default", defaultOperators, new[] { 0u, 1u }, new[] { 0u }, new[] { 0u }),
            WriteClothState(builder, "AnimToSimCurrent", skinIndices.Concat(new[] { currentCopyIndex }).ToArray(), new[] { 0u, 1u }, new[] { 0u }, new[] { 0u }),
            WriteClothState(builder, "AnimToSimPrev", skinIndices.Concat(new[] { previousCopyIndex }).ToArray(), new[] { 0u, 2u }, new[] { 0u }, new[] { 0u })
        };

        return new RuntimePipeline(operators, stateItems, simulateIndex);
    }

    private static RuntimePipeline WriteSourceRuntimePipeline(
        DataBuilder builder,
        StandaloneBphclClothGraph cloth,
        int constraintSetCount)
    {
        var skins = cloth.SkinOperators.ToDictionary(skin => skin.OperatorIndex);
        var deformers = cloth.BoneDeformers
            .Where(deformer => deformer.OperatorIndex >= 0)
            .ToDictionary(deformer => deformer.OperatorIndex);
        var operators = new List<int>(cloth.Runtime.Operators.Count);
        var sourceTrackerPlans = BuildSourceOperatorTransformTrackerPlans(cloth);
        var simulateIndex = -1;

        foreach (var source in cloth.Runtime.Operators.OrderBy(@operator => @operator.Index))
        {
            if (source.Index != operators.Count)
                throw new InvalidDataException($"Cloth '{cloth.Name}' has non-contiguous runtime operator indices.");

            int item = source.ClassName switch
            {
                "hclObjectSpaceSkinPOperator" or "hclBoneSpaceSkinPOperator" when skins.TryGetValue(source.Index, out var skin) =>
                    WriteSkinOperator(builder, skin, source.Index, GetSourceOperatorTransformTrackers(sourceTrackerPlans, source.Index)),
                "hclMoveParticlesOperator" => WriteMoveParticlesOperator(builder, source, source.Index),
                "hclSimulateOperator" => WriteSimulateOperator(builder, cloth, source, constraintSetCount, source.Index, GetSourceOperatorTransformTrackers(sourceTrackerPlans, source.Index)),
                "hclSimpleMeshBoneDeformOperator" when deformers.TryGetValue(source.Index, out var deformer) =>
                    WriteSimpleMeshBoneDeformOperator(builder, deformer, source.Index, GetSourceOperatorTransformTrackers(sourceTrackerPlans, source.Index)),
                "hclGatherAllVerticesOperator" => WriteGatherAllVerticesOperator(builder, source),
                "hclCopyVerticesOperator" => WriteCopyVerticesOperator(
                    builder,
                    source.InputBufferIndex ?? 0,
                    source.OutputBufferIndex ?? 0,
                    source.VertexCount ?? 0,
                    source.StartVertexIn ?? 0,
                    source.StartVertexOut ?? 0,
                    source.Index),
                _ => throw new InvalidDataException(
                    $"Standalone BPHCL writer does not support source operator {source.Index} '{source.ClassName}'.")
            };
            if (source.ClassName == "hclSimulateOperator")
                simulateIndex = source.Index;
            operators.Add(item);
        }

        if (simulateIndex < 0)
            throw new InvalidDataException($"Cloth '{cloth.Name}' has no hclSimulateOperator.");

        var states = cloth.Runtime.States.Count == 0
            ? new[]
            {
                new ClothStateRuntimeData(
                    0,
                    "Default",
                    Enumerable.Range(0, operators.Count).ToArray(),
                    cloth.Runtime.Buffers.Select(buffer => buffer.Index).ToArray(),
                    new[] { 0 },
                    new[] { 0 })
            }
            : cloth.Runtime.States;
        var stateItems = states
            .OrderBy(state => state.Index)
            .Select(state => WriteClothState(builder, state))
            .ToArray();

        return new RuntimePipeline(operators, stateItems, simulateIndex);
    }

    private static int WriteSkinOperator(
        DataBuilder builder,
        SkinOperatorData skin,
        int operatorId,
        IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers = null)
    {
        return skin.ClassName switch
        {
            "hclObjectSpaceSkinPOperator" => WriteObjectSpaceSkinOperator(builder, skin, operatorId, transformTrackers),
            "hclBoneSpaceSkinPOperator" => WriteBoneSpaceSkinOperator(builder, skin, operatorId, transformTrackers),
            _ => throw new InvalidDataException($"Standalone BPHCL writer does not support skin operator '{skin.ClassName}'.")
        };
    }

    private static int WriteObjectSpaceSkinOperator(
        DataBuilder builder,
        SkinOperatorData skin,
        int operatorId,
        IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers)
    {
        const string className = "hclObjectSpaceSkinPOperator";
        var layout = NativeBphclReflectionCatalog.GetTypeLayout(className);
        var op = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 296), alignment: 16);
        WriteOperatorBase(
            builder,
            op.DataOffset,
            operatorId,
            "Skin",
            new[] { CreateOperatorBufferAccess(skin.OutputBufferIndex, 6) },
            new[] { CreateOperatorTransformAccess(skin.TransformSetIndex, 9, transformTrackers) });
        WriteSkinBaseArrays(builder, op.DataOffset, className, skin, includeBoneFromSkinMatrices: true);

        // BotW keeps these positions in localUnpackedPs, but native TotK
        // BPHCL uses the packed localPs field and leaves localUnpackedPs
        // empty. Writing both fields produces a structurally readable file
        // that the runtime does not initialize correctly.
        var localPositions = skin.LocalPositionBlocks.Count > 0
            ? skin.LocalPositionBlocks
            : skin.LocalUnpackedPositionBlocks;
        var deformerOffset = RequireMemberOffset(className, "objectSpaceDeformer");
        WriteObjectSpaceDeformer(builder, op.DataOffset + deformerOffset, skin);
        WriteArrayField(
            builder,
            op.DataOffset + RequireMemberOffset(className, "localPs"),
            WritePackedObjectPositionBlocks(builder, localPositions));
        return op.ItemIndex;
    }

    private static int WriteBoneSpaceSkinOperator(
        DataBuilder builder,
        SkinOperatorData skin,
        int operatorId,
        IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers)
    {
        const string className = "hclBoneSpaceSkinPOperator";
        var layout = NativeBphclReflectionCatalog.GetTypeLayout(className);
        var op = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 216), alignment: 16);
        WriteOperatorBase(
            builder,
            op.DataOffset,
            operatorId,
            "Skin",
            new[] { CreateOperatorBufferAccess(skin.OutputBufferIndex, 6) },
            new[] { CreateOperatorTransformAccess(skin.TransformSetIndex, 9, transformTrackers) });
        WriteSkinBaseArrays(builder, op.DataOffset, className, skin, includeBoneFromSkinMatrices: false);

        // As above, the HKCL source position blocks map to TotK's localPs
        // field. Native BPHCL leaves the localUnpackedPs fallback empty.
        var localPositions = skin.LocalPositionBlocks.Count > 0
            ? skin.LocalPositionBlocks
            : skin.LocalUnpackedPositionBlocks;
        var deformerOffset = RequireMemberOffset(className, "boneSpaceDeformer");
        WriteBoneSpaceDeformer(builder, op.DataOffset + deformerOffset, skin);
        WriteArrayField(
            builder,
            op.DataOffset + RequireMemberOffset(className, "localPs"),
            WriteUnpackedPositionBlocks(builder, "hclBoneSpaceDeformer::LocalBlockP", localPositions));
        return op.ItemIndex;
    }

    private static void WriteSkinBaseArrays(
        DataBuilder builder,
        uint dataOffset,
        string className,
        SkinOperatorData skin,
        bool includeBoneFromSkinMatrices)
    {
        if (includeBoneFromSkinMatrices)
        {
            WriteArrayField(
                builder,
                dataOffset + RequireMemberOffset(className, "boneFromSkinMeshTransforms"),
                builder.WriteMatrix4Array(skin.BoneFromSkinMeshTransforms));
        }
        WriteArrayField(
            builder,
            dataOffset + RequireMemberOffset(className, "transformSubset"),
            builder.WriteUInt16Array(skin.TransformSubset));
        builder.WriteUInt32(dataOffset + RequireMemberOffset(className, "outputBufferIndex"), checked((uint)skin.OutputBufferIndex));
        builder.WriteUInt32(dataOffset + RequireMemberOffset(className, "transformSetIndex"), checked((uint)skin.TransformSetIndex));
    }

    private static void WriteObjectSpaceDeformer(DataBuilder builder, uint dataOffset, SkinOperatorData skin)
    {
        const string className = "hclObjectSpaceDeformer";
        foreach (var blend in skin.BlendArrays)
        {
            WriteArrayField(
                builder,
                dataOffset + RequireMemberOffset(className, blend.MemberName),
                WriteBlendBlocks(builder, className, blend),
                RequireArrayType(className, blend.MemberName));
        }
        WriteArrayField(
            builder,
            dataOffset + RequireMemberOffset(className, "controlBytes"),
            builder.WriteByteArray(skin.ControlBytes),
            RequireArrayType(className, "controlBytes"));
        builder.WriteUInt16(dataOffset + RequireMemberOffset(className, "startVertexIndex"), skin.StartVertexIndex);
        builder.WriteUInt16(dataOffset + RequireMemberOffset(className, "endVertexIndex"), skin.EndVertexIndex);
        builder.WriteByte(dataOffset + RequireMemberOffset(className, "partialWrite"), skin.PartialWrite ? (byte)1 : (byte)0);
    }

    private static void WriteBoneSpaceDeformer(DataBuilder builder, uint dataOffset, SkinOperatorData skin)
    {
        const string className = "hclBoneSpaceDeformer";
        foreach (var blend in skin.BlendArrays)
        {
            WriteArrayField(
                builder,
                dataOffset + RequireMemberOffset(className, blend.MemberName),
                WriteBlendBlocks(builder, className, blend),
                RequireArrayType(className, blend.MemberName));
        }
        WriteArrayField(
            builder,
            dataOffset + RequireMemberOffset(className, "controlBytes"),
            builder.WriteByteArray(skin.ControlBytes),
            RequireArrayType(className, "controlBytes"));
        builder.WriteUInt16(dataOffset + RequireMemberOffset(className, "startVertexIndex"), skin.StartVertexIndex);
        builder.WriteUInt16(dataOffset + RequireMemberOffset(className, "endVertexIndex"), skin.EndVertexIndex);
        var batchSizeOffset = NativeBphclReflectionCatalog.GetMemberOffset(className, "batchSizeSpu");
        if (batchSizeOffset is uint batchOffset)
            builder.WriteUInt16(dataOffset + batchOffset, skin.BatchSizeSpu == 0 ? (ushort)512 : skin.BatchSizeSpu);
        builder.WriteByte(dataOffset + RequireMemberOffset(className, "partialWrite"), skin.PartialWrite ? (byte)1 : (byte)0);
    }

    private static DataBuilder.Allocation? WriteBlendBlocks(
        DataBuilder builder,
        string deformerClassName,
        SkinBlendArrayData blend)
    {
        if (blend.Blocks.Count == 0)
            return null;

        var blockType = deformerClassName + "::" + ToBlendBlockTypeName(blend.MemberName);
        // Bone-space blocks carry only packed vertex and bone indices. The extra
        // weight bytes belong exclusively to object-space skinning blocks.
        var hasExplicitWeights = string.Equals(deformerClassName, "hclObjectSpaceDeformer", StringComparison.Ordinal);
        var rawSize = checked((blend.VertexCount + blend.BoneCount) * sizeof(ushort) +
            (hasExplicitWeights && blend.BlendCount > 1 ? blend.BoneCount : 0));
        var stride = AlignUp(rawSize, 16);
        var storage = builder.AllocateArray(
            NativeBphclReflectionCatalog.GetTypeIndex(blockType),
            blend.Blocks.Count,
            stride,
            alignment: 16);
        for (var blockIndex = 0; blockIndex < blend.Blocks.Count; blockIndex++)
        {
            var block = blend.Blocks[blockIndex];
            var offset = storage.DataOffset + checked((uint)(blockIndex * stride));
            for (var index = 0; index < block.VertexIndices.Count; index++)
                builder.WriteUInt16(offset + checked((uint)(index * sizeof(ushort))), block.VertexIndices[index]);
            var boneOffset = offset + checked((uint)(block.VertexIndices.Count * sizeof(ushort)));
            for (var index = 0; index < block.BoneIndices.Count; index++)
                builder.WriteUInt16(boneOffset + checked((uint)(index * sizeof(ushort))), block.BoneIndices[index]);
            var weightOffset = boneOffset + checked((uint)(block.BoneIndices.Count * sizeof(ushort)));
            for (var index = 0; index < block.BoneWeights.Count; index++)
                builder.WriteByte(weightOffset + checked((uint)index), block.BoneWeights[index]);
        }
        return storage;
    }

    private static DataBuilder.Allocation? WritePackedObjectPositionBlocks(
        DataBuilder builder,
        IReadOnlyList<SkinPositionBlockData> blocks)
    {
        if (blocks.Count == 0)
            return null;
        var storage = builder.AllocateArray(
            NativeBphclReflectionCatalog.GetTypeIndex("hclObjectSpaceDeformer::LocalBlockP"),
            blocks.Count,
            128,
            alignment: 16);
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var block = blocks[blockIndex];
            var bytes = block.NativePackedValues is { Count: 64 } raw
                ? WriteNativePackedObjectPositionBlock(raw)
                : PackObjectPositionBlock(block);
            builder.WriteBytes(storage.DataOffset + checked((uint)(blockIndex * 128)), bytes);
        }
        return storage;
    }

    private static DataBuilder.Allocation? WriteUnpackedPositionBlocks(
        DataBuilder builder,
        string typeName,
        IReadOnlyList<SkinPositionBlockData> blocks)
    {
        if (blocks.Count == 0)
            return null;
        var storage = builder.AllocateArray(
            NativeBphclReflectionCatalog.GetTypeIndex(typeName),
            blocks.Count,
            256,
            alignment: 16);
        for (var blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
        {
            var offset = storage.DataOffset + checked((uint)(blockIndex * 256));
            for (var index = 0; index < 16; index++)
                builder.WriteVector4(offset + checked((uint)(index * 16)), blocks[blockIndex].Positions[index]);
        }
        return storage;
    }

    private static byte[] WriteNativePackedObjectPositionBlock(IReadOnlyList<short> values)
    {
        var bytes = new byte[values.Count * sizeof(short)];
        for (var index = 0; index < values.Count; index++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * sizeof(short), sizeof(short)), values[index]);
        return bytes;
    }
    private static byte[] PackObjectPositionBlock(SkinPositionBlockData block)
    {
        const int positionCount = 16;
        const int valuesPerPosition = 4;
        var maximum = block.Positions.SelectMany(position => new[]
            { MathF.Abs(position.X), MathF.Abs(position.Y), MathF.Abs(position.Z) }).DefaultIfEmpty().Max();
        var exponent = maximum < 0.0000000001f
            ? (short)0
            : checked((short)Math.Clamp(
                (int)MathF.Ceiling(128.0f * (MathF.Log2(maximum / short.MaxValue) + 111.0f)),
                short.MinValue,
                short.MaxValue));
        var scale = MathF.Pow(2.0f, exponent / 128.0f - 111.0f);
        var bytes = new byte[positionCount * valuesPerPosition * sizeof(short)];
        for (var index = 0; index < positionCount; index++)
        {
            var position = block.Positions[index];
            WritePackedComponent(bytes, (index * valuesPerPosition + 0) * sizeof(short), position.X, scale);
            WritePackedComponent(bytes, (index * valuesPerPosition + 1) * sizeof(short), position.Y, scale);
            WritePackedComponent(bytes, (index * valuesPerPosition + 2) * sizeof(short), position.Z, scale);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan((index * valuesPerPosition + 3) * sizeof(short), sizeof(short)), exponent);
        }
        return bytes;
    }

    private static void WritePackedComponent(byte[] bytes, int offset, float value, float scale)
    {
        var mantissa = scale <= 0.0f ? 0 : (int)MathF.Round(value / scale);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset, sizeof(short)), checked((short)Math.Clamp(mantissa, short.MinValue, short.MaxValue)));
    }

    private static void WriteArrayField(
        DataBuilder builder,
        uint fieldOffset,
        DataBuilder.Allocation? storage,
        uint? patchTypeOverride = null)
    {
        if (storage is null)
            return;
        builder.WriteReference(fieldOffset, storage.ItemIndex, patchTypeOverride);
        builder.WriteUInt32(fieldOffset + 8, checked((uint)storage.Count));
    }

    private static uint RequireMemberOffset(string className, string memberName) =>
        NativeBphclReflectionCatalog.GetMemberOffset(className, memberName)
        ?? throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no member '{className}.{memberName}'.");

    private static uint RequireArrayElementType(string className, string memberName)
    {
        var arrayType = RequireArrayType(className, memberName);
        return NativeBphclReflectionCatalog.GetArrayElementType(arrayType)
            ?? throw new InvalidDataException($"'{className}.{memberName}' is not a reflected hkArray.");
    }

    private static uint RequireArrayType(string className, string memberName)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout(className);
        var offset = RequireMemberOffset(className, memberName);
        var arrayType = NativeBphclReflectionCatalog.GetMemberTypeIndex(layout.TypeIndex, offset)
            ?? throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no member type for '{className}.{memberName}'.");
        if (!string.Equals(NativeBphclReflectionCatalog.GetTypeName(arrayType), "hkArray", StringComparison.Ordinal))
            throw new InvalidDataException($"'{className}.{memberName}' is not a reflected hkArray.");
        return arrayType;
    }

    private static string ToBlendBlockTypeName(string memberName) => memberName switch
    {
        "eightBlendEntries" => "EightBlendEntryBlock",
        "sevenBlendEntries" => "SevenBlendEntryBlock",
        "sixBlendEntries" => "SixBlendEntryBlock",
        "fiveBlendEntries" => "FiveBlendEntryBlock",
        "fourBlendEntries" => "FourBlendEntryBlock",
        "threeBlendEntries" => "ThreeBlendEntryBlock",
        "twoBlendEntries" => "TwoBlendEntryBlock",
        "oneBlendEntries" => "OneBlendEntryBlock",
        _ => throw new InvalidDataException($"Unknown skin blend array '{memberName}'.")
    };

    private static int AlignUp(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static ClothStateBufferAccessData CreateOperatorBufferAccess(
        int bufferIndex,
        byte componentFlags,
        bool trianglesRead = false) =>
        new(bufferIndex, componentFlags, 0, 0, 0, trianglesRead, bufferIndex);

    private static ClothStateTransformSetAccessData CreateOperatorTransformAccess(
        int transformSetIndex,
        byte componentFlags,
        IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers = null) =>
        new(transformSetIndex, componentFlags, 0, transformTrackers ?? Array.Empty<ClothStateTransformTrackerData>());

    // Native BPHCL stores transform tracker arrays on operator accesses with a
    // zero inline count. The pointer still owns the real two-tracker storage.
    // hclClothState accesses use a normal count and are written separately.
    private static IReadOnlyDictionary<int, IReadOnlyList<ClothStateTransformTrackerData>>
        BuildSourceOperatorTransformTrackerPlans(StandaloneBphclClothGraph cloth)
    {
        var states = cloth.Runtime.States;
        if (states.Count == 0)
            return new Dictionary<int, IReadOnlyList<ClothStateTransformTrackerData>>();

        var defaultState = states.FirstOrDefault(state => string.Equals(state.Name, "Default", StringComparison.OrdinalIgnoreCase))
            ?? states.OrderBy(state => state.Index).First();
        var animationState = states.FirstOrDefault(state =>
            state.Name.Contains("AnimToSimCurrent", StringComparison.OrdinalIgnoreCase))
            ?? states.FirstOrDefault(state =>
                state.Name.Contains("AnimToSim", StringComparison.OrdinalIgnoreCase));
        var plans = new Dictionary<int, IReadOnlyList<ClothStateTransformTrackerData>>();

        foreach (var source in cloth.Runtime.Operators)
        {
            var transformSetIndex = GetSourceOperatorTransformSetIndex(cloth, source);
            var defaultTrackers = GetStateTransformTrackers(defaultState, transformSetIndex);
            var animationTrackers = animationState is null
                ? Array.Empty<ClothStateTransformTrackerData>()
                : GetStateTransformTrackers(animationState, transformSetIndex);

            IReadOnlyList<ClothStateTransformTrackerData> trackers = source.ClassName switch
            {
                "hclObjectSpaceSkinPOperator" or "hclBoneSpaceSkinPOperator" =>
                    animationTrackers.Count > 0
                        ? ExtractTrackerReads(animationTrackers)
                        : ExtractTrackerReads(defaultTrackers),
                "hclSimulateOperator" => SubtractTrackerReads(defaultTrackers, animationTrackers),
                "hclSimpleMeshBoneDeformOperator" => ExtractTrackerWrites(defaultTrackers),
                _ => Array.Empty<ClothStateTransformTrackerData>()
            };

            if (HasTrackedTransforms(trackers))
                plans[source.Index] = trackers;
        }

        return plans;
    }

    private static IReadOnlyList<ClothStateTransformTrackerData>? GetSourceOperatorTransformTrackers(
        IReadOnlyDictionary<int, IReadOnlyList<ClothStateTransformTrackerData>> plans,
        int operatorIndex) =>
        plans.TryGetValue(operatorIndex, out var trackers) ? trackers : null;

    private static int GetSourceOperatorTransformSetIndex(StandaloneBphclClothGraph cloth, ClothOperatorData source)
    {
        var skin = cloth.SkinOperators.FirstOrDefault(candidate => candidate.OperatorIndex == source.Index);
        if (skin is not null)
            return skin.TransformSetIndex;

        var deformer = cloth.BoneDeformers.FirstOrDefault(candidate => candidate.OperatorIndex == source.Index);
        if (deformer is not null)
            return deformer.OutputTransformSetIndex;

        return source.TransformSetIndex ?? 0;
    }

    private static IReadOnlyList<ClothStateTransformTrackerData> GetStateTransformTrackers(
        ClothStateRuntimeData state,
        int transformSetIndex) =>
        state.TransformSetAccesses?
            .FirstOrDefault(access => access.TransformSetIndex == transformSetIndex)?
            .TransformTrackers
        ?? Array.Empty<ClothStateTransformTrackerData>();

    private static IReadOnlyList<ClothStateTransformTrackerData> ExtractTrackerReads(
        IReadOnlyList<ClothStateTransformTrackerData> source) =>
        source.Select(tracker => new ClothStateTransformTrackerData(
            tracker.Read,
            tracker.ReadBeforeWrite,
            EmptyBitFieldLike(tracker.Written))).ToArray();

    private static IReadOnlyList<ClothStateTransformTrackerData> ExtractTrackerWrites(
        IReadOnlyList<ClothStateTransformTrackerData> source) =>
        source.Select(tracker => new ClothStateTransformTrackerData(
            EmptyBitFieldLike(tracker.Read),
            EmptyBitFieldLike(tracker.ReadBeforeWrite),
            tracker.Written)).ToArray();

    private static IReadOnlyList<ClothStateTransformTrackerData> SubtractTrackerReads(
        IReadOnlyList<ClothStateTransformTrackerData> whole,
        IReadOnlyList<ClothStateTransformTrackerData> consumed)
    {
        if (whole.Count == 0)
            return Array.Empty<ClothStateTransformTrackerData>();

        return whole.Select((tracker, index) =>
        {
            var consumedTracker = index < consumed.Count ? consumed[index] : null;
            return new ClothStateTransformTrackerData(
                SubtractBitField(tracker.Read, consumedTracker?.Read),
                SubtractBitField(tracker.ReadBeforeWrite, consumedTracker?.ReadBeforeWrite),
                EmptyBitFieldLike(tracker.Written));
        }).ToArray();
    }

    private static ClothStateBitFieldData SubtractBitField(
        ClothStateBitFieldData whole,
        ClothStateBitFieldData? consumed)
    {
        var words = whole.Words
            .Select((word, index) => word & ~(consumed?.Words.ElementAtOrDefault(index) ?? 0u))
            .ToArray();
        return new ClothStateBitFieldData(words, whole.NumBits);
    }

    private static ClothStateBitFieldData EmptyBitFieldLike(ClothStateBitFieldData source) =>
        new(Enumerable.Repeat(0u, source.Words.Count).ToArray(), source.NumBits);

    private static bool HasTrackedTransforms(IReadOnlyList<ClothStateTransformTrackerData> trackers) =>
        trackers.Any(tracker =>
            tracker.Read.Words.Any(word => word != 0)
            || tracker.ReadBeforeWrite.Words.Any(word => word != 0)
            || tracker.Written.Words.Any(word => word != 0));

    private static IReadOnlyList<ClothStateBufferAccessData> CreateMoveParticlesBufferAccesses() =>
        new[]
        {
            CreateOperatorBufferAccess(0, 1),
            CreateOperatorBufferAccess(1, 2),
            CreateOperatorBufferAccess(2, 2)
        };

    private static IReadOnlyList<ClothStateBufferAccessData> CreateSimulateBufferAccesses() =>
        new[]
        {
            CreateOperatorBufferAccess(0, 1),
            CreateOperatorBufferAccess(1, 15),
            CreateOperatorBufferAccess(2, 15)
        };

    private static void WriteOperatorBase(
        DataBuilder builder,
        uint dataOffset,
        int operatorId,
        string name,
        IReadOnlyList<ClothStateBufferAccessData> bufferAccesses,
        IReadOnlyList<ClothStateTransformSetAccessData> transformSetAccesses)
    {
        builder.WriteReference(dataOffset + 24, builder.WriteString(name));
        builder.WriteUInt32(dataOffset + 32, checked((uint)operatorId));
        builder.WriteUInt32(dataOffset + 36, 0);
        if (bufferAccesses.Count > 0)
        {
            var buffers = WriteClothStateBufferAccesses(builder, bufferAccesses);
            builder.WriteReference(dataOffset + 40, buffers.ItemIndex);
            builder.WriteUInt32(dataOffset + 48, checked((uint)bufferAccesses.Count));
        }
        if (transformSetAccesses.Count > 0)
        {
            var transformSets = WriteOperatorTransformSetAccesses(builder, transformSetAccesses);
            builder.WriteReference(dataOffset + 56, transformSets.ItemIndex);
            builder.WriteUInt32(dataOffset + 64, checked((uint)transformSetAccesses.Count));
        }
    }
    private static int WriteMoveParticlesOperator(DataBuilder builder, StandaloneBphclClothGraph cloth, int operatorId)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclMoveParticlesOperator");
        var op = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 96), alignment: 16);
        WriteOperatorBase(builder, op.DataOffset, operatorId, "MoveFixedParticles", CreateMoveParticlesBufferAccesses(), Array.Empty<ClothStateTransformSetAccessData>());
        var pairs = cloth.Particles.Where(particle => particle.Fixed).ToArray();
        var pairLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclMoveParticlesOperator::VertexParticlePair");
        var pairStorage = builder.AllocateArray(pairLayout.TypeIndex, pairs.Length, RequireSize(pairLayout, 4), alignment: 2);
        for (var index = 0; index < pairs.Length; index++)
        {
            var offset = pairStorage.DataOffset + checked((uint)(index * RequireSize(pairLayout, 4)));
            builder.WriteUInt16(offset, checked((ushort)pairs[index].Index));
            builder.WriteUInt16(offset + 2, checked((ushort)pairs[index].Index));
        }

        builder.WriteReference(op.DataOffset + 72, pairStorage.ItemIndex);
        builder.WriteUInt32(op.DataOffset + 80, checked((uint)pairs.Length));
        builder.WriteUInt32(op.DataOffset + 88, 0);
        builder.WriteUInt32(op.DataOffset + 92, 0);
        return op.ItemIndex;
    }

    private static int WriteMoveParticlesOperator(DataBuilder builder, ClothOperatorData source, int operatorId)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclMoveParticlesOperator");
        var op = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 96), alignment: 16);
        WriteOperatorBase(builder, op.DataOffset, operatorId, "MoveFixedParticles", CreateMoveParticlesBufferAccesses(), Array.Empty<ClothStateTransformSetAccessData>());
        var pairLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclMoveParticlesOperator::VertexParticlePair");
        var pairs = builder.AllocateArray(
            pairLayout.TypeIndex,
            source.VertexParticlePairs.Count,
            RequireSize(pairLayout, 4),
            alignment: 2);
        for (var index = 0; index < source.VertexParticlePairs.Count; index++)
        {
            var pair = source.VertexParticlePairs[index];
            var offset = pairs.DataOffset + checked((uint)(index * RequireSize(pairLayout, 4)));
            builder.WriteUInt16(offset, pair.VertexIndex);
            builder.WriteUInt16(offset + 2, pair.ParticleIndex);
        }

        builder.WriteReference(op.DataOffset + 72, pairs.ItemIndex);
        builder.WriteUInt32(op.DataOffset + 80, checked((uint)source.VertexParticlePairs.Count));
        builder.WriteUInt32(op.DataOffset + 88, checked((uint)(source.SimulationClothIndex ?? 0)));
        builder.WriteUInt32(op.DataOffset + 92, checked((uint)(source.ReferenceBufferIndex ?? 0)));
        return op.ItemIndex;
    }

    private static int WriteSimulateOperator(
        DataBuilder builder,
        StandaloneBphclClothGraph cloth,
        int constraintSetCount,
        int operatorId,
        IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers = null)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclSimulateOperator");
        var op = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 96), alignment: 16);
        WriteOperatorBase(builder, op.DataOffset, operatorId, "Simulate", CreateSimulateBufferAccesses(), new[] { CreateOperatorTransformAccess(0, 9, transformTrackers) });
        var configLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclSimulateOperator::Config");
        var config = builder.AllocateArray(configLayout.TypeIndex, 1, RequireSize(configLayout, 48), alignment: 8);
        var execution = Enumerable.Range(0, constraintSetCount)
            .Append(-1)
            .ToArray();
        var executionData = builder.WriteInt32Array(
            execution,
            RequireArrayElementType("hclSimulateOperator::Config", "constraintExecution"));

        builder.WriteUInt32(op.DataOffset + 72, 0);
        builder.WriteReference(op.DataOffset + 80, config.ItemIndex);
        builder.WriteUInt32(op.DataOffset + 88, 1);
        builder.WriteReference(config.DataOffset, builder.WriteString(cloth.Name + "_Simulate"));
        builder.WriteReference(config.DataOffset + 8, executionData.ItemIndex);
        builder.WriteUInt32(config.DataOffset + 16, checked((uint)execution.Length));
        builder.WriteByte(config.DataOffset + 40, 1);
        builder.WriteByte(config.DataOffset + 41, 1);
        builder.WriteByte(config.DataOffset + 42, 1);
        builder.WriteByte(config.DataOffset + 43, 1);
        return op.ItemIndex;
    }

    private static int WriteSimulateOperator(
        DataBuilder builder,
        StandaloneBphclClothGraph cloth,
        ClothOperatorData source,
        int constraintSetCount,
        int operatorId,
        IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclSimulateOperator");
        var op = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 96), alignment: 16);
        WriteOperatorBase(builder, op.DataOffset, operatorId, "Simulate", CreateSimulateBufferAccesses(), new[] { CreateOperatorTransformAccess(0, 9, transformTrackers) });
        var sourceConfigs = source.SimulateConfigs.Count > 0
            ? source.SimulateConfigs
            : new[]
            {
                new ClothSimulateConfigData(
                    Enumerable.Range(0, constraintSetCount).Append(-1).ToArray(),
                    Array.Empty<int>(),
                    1,
                    1,
                    true,
                    true)
            };
        var configLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclSimulateOperator::Config");
        var configs = builder.AllocateArray(
            configLayout.TypeIndex,
            sourceConfigs.Count,
            RequireSize(configLayout, 48),
            alignment: 8);

        builder.WriteUInt32(op.DataOffset + 72, checked((uint)(source.SimulationClothIndex ?? 0)));
        builder.WriteReference(op.DataOffset + 80, configs.ItemIndex);
        builder.WriteUInt32(op.DataOffset + 88, checked((uint)sourceConfigs.Count));
        for (var index = 0; index < sourceConfigs.Count; index++)
        {
            var config = sourceConfigs[index];
            var offset = configs.DataOffset + checked((uint)(index * RequireSize(configLayout, 48)));
            var execution = ConvertHkclConstraintExecutionToBphcl(
                cloth,
                config.ConstraintExecution,
                constraintSetCount);
            var executionStorage = builder.WriteInt32Array(
                execution,
                RequireArrayElementType("hclSimulateOperator::Config", "constraintExecution"));
            var useAllInstanceCollidables = config.InstanceCollidablesUsed.Count == 0
                || config.UseAllInstanceCollidables;
            // BPHCL stores this as a bool mask indexed by the cloth's own
            // per-instance collidable list, not as an integer index list.
            var instanceColliderFlags = useAllInstanceCollidables
                ? Array.Empty<bool>()
                : Enumerable.Range(0, cloth.Colliders.Count)
                    .Select(colliderIndex => config.InstanceCollidablesUsed.Contains(colliderIndex))
                    .ToArray();
            var instanceCollidables = instanceColliderFlags.Length > 0
                ? builder.WriteBoolArray(
                    instanceColliderFlags,
                    RequireArrayElementType("hclSimulateOperator::Config", "instanceCollidablesUsed"))
                : null;
            builder.WriteReference(offset, builder.WriteString($"{cloth.Name}_Simulate_{index}"));
            builder.WriteReference(offset + 8, executionStorage.ItemIndex);
            builder.WriteUInt32(offset + 16, checked((uint)execution.Length));
            if (instanceCollidables is not null)
                builder.WriteReference(offset + 24, instanceCollidables.ItemIndex);
            builder.WriteUInt32(offset + 32, checked((uint)instanceColliderFlags.Length));
            builder.WriteByte(offset + 40, config.SubSteps == 0 ? (byte)1 : config.SubSteps);
            builder.WriteByte(offset + 41, config.NumberOfSolveIterations == 0 ? (byte)1 : config.NumberOfSolveIterations);
            builder.WriteByte(offset + 42, useAllInstanceCollidables ? (byte)1 : (byte)0);
            builder.WriteByte(offset + 43, config.AdaptConstraintStiffness ? (byte)1 : (byte)0);
        }
        return op.ItemIndex;
    }

    private static int[] ConvertHkclConstraintExecutionToBphcl(
        StandaloneBphclClothGraph cloth,
        IReadOnlyList<int> sourceExecution,
        int constraintSetCount)
    {
        var constraintSets = cloth.ConstraintSets
            .ToDictionary(constraintSet => constraintSet.Index);
        var primary = (sourceExecution.Count > 0
                ? sourceExecution.Where(index => index >= 0)
                : Enumerable.Range(0, constraintSetCount))
            .Where(constraintSets.ContainsKey)
            .ToList();
        var bendStiffnessIndices = constraintSets
            .Where(pair => string.Equals(
                pair.Value.TypeName,
                "hclBendStiffnessConstraintSet",
                StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray();
        if (bendStiffnessIndices.Length == 0)
            return primary.Append(-1).ToArray();

        var transitionIndices = constraintSets
            .Where(pair => string.Equals(
                pair.Value.TypeName,
                "hclTransitionConstraintSet",
                StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToHashSet();
        var deferredTransitions = primary
            .Where(transitionIndices.Contains)
            .Distinct()
            .ToArray();
        primary.RemoveAll(transitionIndices.Contains);

        // BPHCL runs its genuine bend-stiffness set in place of the final
        // duplicate bend-link pass, then defers transition constraints until
        // the phase after -1. This reproduces native long-hair/earring
        // schedules while leaving ranged duplicate bends untouched.
        foreach (var bendStiffnessIndex in bendStiffnessIndices)
        {
            if (primary.Contains(bendStiffnessIndex))
                continue;

            var lastBendPosition = primary
                .Select((constraintIndex, position) => (constraintIndex, position))
                .Where(pair => constraintSets.TryGetValue(pair.constraintIndex, out var constraintSet)
                    && string.Equals(
                        constraintSet.TypeName,
                        "hclBendLinkConstraintSet",
                        StringComparison.Ordinal))
                .Select(pair => pair.position)
                .LastOrDefault(-1);
            if (lastBendPosition >= 0)
                primary[lastBendPosition] = bendStiffnessIndex;
        }

        primary.Add(-1);
        primary.AddRange(deferredTransitions);
        return primary.ToArray();
    }

    private static int WriteSimpleMeshBoneDeformOperator(
        DataBuilder builder,
        StandaloneBphclSimpleMeshBoneDeformer deformer,
        int operatorId,
        IReadOnlyList<ClothStateTransformTrackerData>? transformTrackers = null)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclSimpleMeshBoneDeformOperator");
        var op = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 120), alignment: 16);
        WriteOperatorBase(
            builder,
            op.DataOffset,
            operatorId,
            "MeshBone",
            new[] { CreateOperatorBufferAccess(deformer.InputBufferIndex, 1, trianglesRead: true) },
            new[] { CreateOperatorTransformAccess(deformer.OutputTransformSetIndex, 2, transformTrackers) });
        var pairLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclSimpleMeshBoneDeformOperator::TriangleBonePair");
        var pairs = builder.AllocateArray(
            pairLayout.TypeIndex,
            deformer.TriangleBonePairs.Count,
            RequireSize(pairLayout, 4),
            alignment: 2);
        for (var index = 0; index < deformer.TriangleBonePairs.Count; index++)
        {
            var pair = deformer.TriangleBonePairs[index];
            var offset = pairs.DataOffset + checked((uint)(index * RequireSize(pairLayout, 4)));
            builder.WriteUInt16(offset, pair.BoneOffset);
            builder.WriteUInt16(offset + 2, pair.TriangleOffset);
        }

        var transforms = deformer.LocalBoneTransforms.Count == deformer.TriangleBonePairs.Count
            ? deformer.LocalBoneTransforms
            : Enumerable.Repeat(Matrix4x4.Identity, deformer.TriangleBonePairs.Count).ToArray();
        var transformStorage = builder.WriteMatrix4Array(transforms);
        builder.WriteUInt32(op.DataOffset + 72, checked((uint)deformer.InputBufferIndex));
        builder.WriteUInt32(op.DataOffset + 76, checked((uint)deformer.OutputTransformSetIndex));
        builder.WriteReference(op.DataOffset + 80, pairs.ItemIndex);
        builder.WriteUInt32(op.DataOffset + 88, checked((uint)deformer.TriangleBonePairs.Count));
        builder.WriteReference(op.DataOffset + 96, transformStorage.ItemIndex);
        builder.WriteUInt32(op.DataOffset + 104, checked((uint)transforms.Count));
        builder.WriteInt32(op.DataOffset + 112, deformer.BoneAxis);
        return op.ItemIndex;
    }

    private static int WriteGatherAllVerticesOperator(DataBuilder builder, ClothOperatorData source)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclGatherAllVerticesOperator");
        var op = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 104), alignment: 16);
        WriteOperatorBase(
            builder,
            op.DataOffset,
            source.Index,
            "GatherVertices",
            new[]
            {
                CreateOperatorBufferAccess(source.InputBufferIndex ?? 0, 1),
                CreateOperatorBufferAccess(source.OutputBufferIndex ?? 0, 6)
            },
            Array.Empty<ClothStateTransformSetAccessData>());
        var gatherMap = builder.WriteInt16Array(source.VertexInputFromVertexOutput);
        builder.WriteReference(op.DataOffset + 72, gatherMap.ItemIndex);
        builder.WriteUInt32(op.DataOffset + 80, checked((uint)source.VertexInputFromVertexOutput.Count));
        builder.WriteUInt32(op.DataOffset + 88, checked((uint)(source.InputBufferIndex ?? 0)));
        builder.WriteUInt32(op.DataOffset + 92, checked((uint)(source.OutputBufferIndex ?? 0)));
        builder.WriteByte(op.DataOffset + 96, source.GatherNormals ? (byte)1 : (byte)0);
        builder.WriteByte(op.DataOffset + 97, source.PartialGather ? (byte)1 : (byte)0);
        return op.ItemIndex;
    }

    private static int WriteCopyVerticesOperator(
        DataBuilder builder,
        int inputBufferIndex,
        int outputBufferIndex,
        int vertexCount,
        int startVertexIn = 0,
        int startVertexOut = 0,
        int operatorId = 0)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclCopyVerticesOperator");
        var op = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 96), alignment: 16);
        var name = outputBufferIndex == 1 ? "VertexGatherCurrent" : outputBufferIndex == 2 ? "VertexGatherPrev" : "CopyVertices";
        WriteOperatorBase(
            builder,
            op.DataOffset,
            operatorId,
            name,
            new[]
            {
                CreateOperatorBufferAccess(inputBufferIndex, 1),
                CreateOperatorBufferAccess(outputBufferIndex, 6)
            },
            Array.Empty<ClothStateTransformSetAccessData>());
        builder.WriteUInt32(op.DataOffset + 72, checked((uint)inputBufferIndex));
        builder.WriteUInt32(op.DataOffset + 76, checked((uint)outputBufferIndex));
        builder.WriteUInt32(op.DataOffset + 80, checked((uint)vertexCount));
        builder.WriteUInt32(op.DataOffset + 84, checked((uint)startVertexIn));
        builder.WriteUInt32(op.DataOffset + 88, checked((uint)startVertexOut));
        return op.ItemIndex;
    }

    private static int WriteClothState(
        DataBuilder builder,
        string name,
        IReadOnlyList<int> operatorIndices,
        IReadOnlyList<uint> bufferIndices,
        IReadOnlyList<uint> transformSetIndices,
        IReadOnlyList<uint> simulationIndices)
    {
        return WriteClothState(
            builder,
            new ClothStateRuntimeData(
                0,
                name,
                operatorIndices,
                bufferIndices.Select(index => checked((int)index)).ToArray(),
                transformSetIndices.Select(index => checked((int)index)).ToArray(),
                simulationIndices.Select(index => checked((int)index)).ToArray(),
                CreateDefaultBufferAccesses(name, bufferIndices),
                CreateDefaultTransformSetAccesses(name, transformSetIndices)));
    }

    private static int WriteClothState(DataBuilder builder, ClothStateRuntimeData source)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclClothState");
        var state = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 104), alignment: 8);
        var bufferAccesses = source.BufferAccesses?.Count > 0
            ? source.BufferAccesses
            : CreateDefaultBufferAccesses(source.Name, source.BufferIndices.Select(index => checked((uint)index)).ToArray());
        bufferAccesses = NormalizeNativeStateBufferAccesses(source.Name, bufferAccesses);
        var transformSetAccesses = source.TransformSetAccesses?.Count > 0
            ? source.TransformSetAccesses
            : CreateDefaultTransformSetAccesses(source.Name, source.TransformSetIndices.Select(index => checked((uint)index)).ToArray());
        var operators = builder.WriteUInt32Array(source.OperatorIndices.Select(index => checked((uint)index)).ToArray());
        var buffers = WriteClothStateBufferAccesses(builder, bufferAccesses);
        var transformSets = WriteOperatorTransformSetAccesses(builder, transformSetAccesses);
        var simulations = builder.WriteUInt32Array(source.SimulationIndices.Select(index => checked((uint)index)).ToArray());
        var dependencyGraph = WriteClothStateDependencyGraph(builder, source.OperatorIndices);
        builder.WriteReference(state.DataOffset + 24, builder.WriteString(string.IsNullOrWhiteSpace(source.Name) ? $"State {source.Index}" : source.Name));
        builder.WriteReference(state.DataOffset + 32, operators.ItemIndex);
        builder.WriteUInt32(state.DataOffset + 40, checked((uint)source.OperatorIndices.Count));
        builder.WriteReference(state.DataOffset + 48, buffers.ItemIndex);
        builder.WriteUInt32(state.DataOffset + 56, checked((uint)bufferAccesses.Count));
        builder.WriteReference(state.DataOffset + 64, transformSets.ItemIndex);
        builder.WriteUInt32(state.DataOffset + 72, checked((uint)transformSetAccesses.Count));
        builder.WriteReference(state.DataOffset + 80, simulations.ItemIndex);
        builder.WriteUInt32(state.DataOffset + 88, checked((uint)source.SimulationIndices.Count));
        builder.WriteReference(state.DataOffset + 96, dependencyGraph);
        return state.ItemIndex;
    }

    // Native BPHCL schedules each cloth state through an explicit dependency
    // graph. The converted states are linear, so one root branch is sufficient.
    private static int WriteClothStateDependencyGraph(DataBuilder builder, IReadOnlyList<int> operatorIndices)
    {
        var graphLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclStateDependencyGraph");
        var graph = builder.AllocateObject(graphLayout.TypeIndex, RequireSize(graphLayout, 96), alignment: 8);
        var branchArrayType = NativeBphclReflectionCatalog.GetMemberTypeIndex(graphLayout.TypeIndex, 24)
            ?? throw new InvalidDataException("hclStateDependencyGraph has no reflected branches field.");
        var branchType = NativeBphclReflectionCatalog.GetArrayElementType(branchArrayType)
            ?? throw new InvalidDataException("hclStateDependencyGraph branches has no element type.");
        var branchLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclStateDependencyGraph::Branch");
        var branches = builder.AllocateArray(branchType, 1, RequireSize(branchLayout, 56), alignment: 8);
        var stateOperatorIndices = builder.AllocateArray(NativeBphclReflectionCatalog.GetTypeIndex("int"), operatorIndices.Count, 4, alignment: 4);
        for (var index = 0; index < operatorIndices.Count; index++)
            builder.WriteInt32(stateOperatorIndices.DataOffset + checked((uint)(index * 4)), index);

        var rootBranchIds = builder.AllocateArray(NativeBphclReflectionCatalog.GetTypeIndex("int"), 1, 4, alignment: 4);
        builder.WriteInt32(rootBranchIds.DataOffset, 0);

        // Each entry represents an index within the state's operator array, not
        // a global cloth operator index. Native BPHCL uses this linear schedule
        // for the standard skin -> simulate -> deform pipeline.
        var dependencyArrayType = NativeBphclReflectionCatalog.GetMemberTypeIndex(graphLayout.TypeIndex, 56)
            ?? throw new InvalidDataException("hclStateDependencyGraph has no reflected children field.");
        var children = WriteNestedIntArrays(
            builder,
            dependencyArrayType,
            Enumerable.Range(0, operatorIndices.Count)
                .Select(index => index + 1 < operatorIndices.Count
                    ? (IReadOnlyList<int>)new[] { index + 1 }
                    : Array.Empty<int>())
                .ToArray());
        var parents = WriteNestedIntArrays(
            builder,
            dependencyArrayType,
            Enumerable.Range(0, operatorIndices.Count)
                .Select(index => index > 0
                    ? (IReadOnlyList<int>)new[] { index - 1 }
                    : Array.Empty<int>())
                .ToArray());

        var branchOffset = branches.DataOffset;
        builder.WriteInt32(branchOffset, 0);
        builder.WriteReference(branchOffset + 8, stateOperatorIndices.ItemIndex);
        builder.WriteUInt32(branchOffset + 16, checked((uint)operatorIndices.Count));

        builder.WriteReference(graph.DataOffset + 24, branches.ItemIndex);
        builder.WriteUInt32(graph.DataOffset + 32, 1);
        builder.WriteReference(graph.DataOffset + 40, rootBranchIds.ItemIndex);
        builder.WriteUInt32(graph.DataOffset + 48, 1);
        builder.WriteReference(graph.DataOffset + 56, children.ItemIndex);
        builder.WriteUInt32(graph.DataOffset + 64, checked((uint)operatorIndices.Count));
        builder.WriteReference(graph.DataOffset + 72, parents.ItemIndex);
        builder.WriteUInt32(graph.DataOffset + 80, checked((uint)operatorIndices.Count));
        builder.WriteByte(graph.DataOffset + 88, 0);
        return graph.ItemIndex;
    }

    private static DataBuilder.Allocation WriteNestedIntArrays(
        DataBuilder builder,
        uint nestedArrayType,
        IReadOnlyList<IReadOnlyList<int>> values)
    {
        var nestedListType = NativeBphclReflectionCatalog.GetArrayElementType(nestedArrayType)
            ?? throw new InvalidDataException("BPHCL state dependency array has no nested list type.");
        var nestedListSize = checked((int)NativeBphclReflectionCatalog.GetTypeSize(nestedListType));
        if (nestedListSize < 16)
            throw new InvalidDataException("BPHCL state dependency nested list is smaller than hkArray.");

        var outer = builder.AllocateArray(nestedListType, values.Count, nestedListSize, alignment: 8);
        var intType = NativeBphclReflectionCatalog.GetTypeIndex("int");
        for (var index = 0; index < values.Count; index++)
        {
            var links = values[index];
            if (links.Count == 0)
                continue;

            var inner = builder.AllocateArray(intType, links.Count, 4, alignment: 4);
            for (var linkIndex = 0; linkIndex < links.Count; linkIndex++)
                builder.WriteInt32(inner.DataOffset + checked((uint)(linkIndex * 4)), links[linkIndex]);

            var nestedOffset = outer.DataOffset + checked((uint)(index * nestedListSize));
            builder.WriteReference(nestedOffset, inner.ItemIndex, nestedListType);

            // BPHCL stores the nested hkArray length in the referenced ITEM,
            // not in this inline header. Retail dependency graphs leave the
            // inline size/capacity words zeroed, as they do for other TAG0
            // hkArray headers. A duplicate count is parseable but gives the
            // runtime conflicting ownership for these links.
        }

        return outer;
    }

    private static IReadOnlyList<ClothStateBufferAccessData> CreateDefaultBufferAccesses(
        string stateName,
        IReadOnlyList<uint> bufferIndices)
    {
        var isDefault = string.Equals(stateName, "Default", StringComparison.OrdinalIgnoreCase);
        return bufferIndices.Select((bufferIndex, index) => new ClothStateBufferAccessData(
            checked((int)bufferIndex),
            index == 0 ? (byte)7 : isDefault ? (byte)15 : (byte)6,
            0,
            0,
            0,
            index > 0 && isDefault,
            checked((int)bufferIndex))).ToArray();
    }

    private static IReadOnlyList<ClothStateTransformSetAccessData> CreateDefaultTransformSetAccesses(
        string stateName,
        IReadOnlyList<uint> transformSetIndices)
    {
        var componentFlags = string.Equals(stateName, "Default", StringComparison.OrdinalIgnoreCase)
            ? (byte)11
            : (byte)9;
        return transformSetIndices.Select(index => new ClothStateTransformSetAccessData(
            checked((int)index),
            componentFlags,
            0,
            Array.Empty<ClothStateTransformTrackerData>())).ToArray();
    }

    // BotW's default state records use a scheduler-specific 7/9 layout. TotK's
    // native BPHCL stores the equivalent skin/simulation accesses as 7/0 and
    // 15/0. Only normalize that known default-state pair; transition states and
    // already-native BPHCL records pass through untouched.
    private static IReadOnlyList<ClothStateBufferAccessData> NormalizeNativeStateBufferAccesses(
        string stateName,
        IReadOnlyList<ClothStateBufferAccessData> accesses)
    {
        if (!string.Equals(stateName, "Default", StringComparison.OrdinalIgnoreCase))
            return accesses;

        return accesses.Select(access =>
        {
            var componentFlags0 = access.ComponentFlags0;
            var componentFlags1 = access.ComponentFlags1;

            if (access.TrianglesRead && componentFlags0 == 9)
                componentFlags0 = 15;
            else if (!access.TrianglesRead && componentFlags0 == 7 && componentFlags1 == 9)
                componentFlags1 = 0;

            return new ClothStateBufferAccessData(
                access.BufferIndex,
                componentFlags0,
                componentFlags1,
                access.ComponentFlags2,
                access.ComponentFlags3,
                access.TrianglesRead,
                access.ShadowBufferIndex);
        }).ToArray();
    }
    private static DataBuilder.Allocation WriteClothStateBufferAccesses(
        DataBuilder builder,
        IReadOnlyList<ClothStateBufferAccessData> accesses)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclClothState::BufferAccess");
        var stride = RequireSize(layout, 16);
        var storage = builder.AllocateArray(layout.TypeIndex, accesses.Count, stride, alignment: 4);
        for (var index = 0; index < accesses.Count; index++)
        {
            var access = accesses[index];
            var offset = storage.DataOffset + checked((uint)(index * stride));
            builder.WriteUInt32(offset, checked((uint)access.BufferIndex));
            builder.WriteByte(offset + 4, access.ComponentFlags0);
            builder.WriteByte(offset + 5, access.ComponentFlags1);
            builder.WriteByte(offset + 6, access.ComponentFlags2);
            builder.WriteByte(offset + 7, access.ComponentFlags3);
            builder.WriteByte(offset + 8, access.TrianglesRead ? (byte)1 : (byte)0);
            builder.WriteUInt32(offset + 12, checked((uint)access.ShadowBufferIndex));
        }
        return storage;
    }

    private static DataBuilder.Allocation WriteOperatorTransformSetAccesses(
        DataBuilder builder,
        IReadOnlyList<ClothStateTransformSetAccessData> accesses)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclClothState::TransformSetAccess");
        var stride = RequireSize(layout, 32);
        var storage = builder.AllocateArray(layout.TypeIndex, accesses.Count, stride, alignment: 8);
        for (var index = 0; index < accesses.Count; index++)
        {
            var access = accesses[index];
            var offset = storage.DataOffset + checked((uint)(index * stride));
            builder.WriteUInt32(offset, checked((uint)access.TransformSetIndex));
            builder.WriteByte(offset + 8, access.ComponentFlags0);
            builder.WriteByte(offset + 9, access.ComponentFlags1);
            if (access.TransformTrackers.Count > 0)
            {
                var trackers = WriteClothStateTransformTrackers(builder, access.TransformTrackers);
                builder.WriteReference(offset + 16, trackers.ItemIndex);
            }
            // Native operator access records leave this count at zero even
            // when the tracker pointer has valid storage.
            builder.WriteUInt32(offset + 24, 0);
        }
        return storage;
    }
    private static DataBuilder.Allocation WriteClothStateTransformSetAccesses(
        DataBuilder builder,
        IReadOnlyList<ClothStateTransformSetAccessData> accesses)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclClothState::TransformSetAccess");
        var stride = RequireSize(layout, 32);
        var storage = builder.AllocateArray(layout.TypeIndex, accesses.Count, stride, alignment: 8);
        for (var index = 0; index < accesses.Count; index++)
        {
            var access = accesses[index];
            var offset = storage.DataOffset + checked((uint)(index * stride));
            var trackers = WriteClothStateTransformTrackers(builder, access.TransformTrackers);
            builder.WriteUInt32(offset, checked((uint)access.TransformSetIndex));
            builder.WriteByte(offset + 8, access.ComponentFlags0);
            builder.WriteByte(offset + 9, access.ComponentFlags1);
            builder.WriteReference(offset + 16, trackers.ItemIndex);
            builder.WriteUInt32(offset + 24, checked((uint)access.TransformTrackers.Count));
        }
        return storage;
    }

    private static DataBuilder.Allocation WriteClothStateTransformTrackers(
        DataBuilder builder,
        IReadOnlyList<ClothStateTransformTrackerData> trackers)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hclTransformSetUsage::TransformTracker");
        var stride = RequireSize(layout, 72);
        var storage = builder.AllocateArray(layout.TypeIndex, trackers.Count, stride, alignment: 8);
        for (var index = 0; index < trackers.Count; index++)
        {
            var offset = storage.DataOffset + checked((uint)(index * stride));
            var tracker = trackers[index];
            WriteClothStateBitField(builder, offset, tracker.Read);
            WriteClothStateBitField(builder, offset + 24, tracker.ReadBeforeWrite);
            WriteClothStateBitField(builder, offset + 48, tracker.Written);
        }
        return storage;
    }

    private static void WriteClothStateBitField(DataBuilder builder, uint offset, ClothStateBitFieldData bitField)
    {
        var words = builder.WriteUInt32Array(
            bitField.Words,
            RequireArrayElementType("hkBitFieldStorage", "words"));
        builder.WriteReference(offset, words.ItemIndex, RequireArrayType("hkBitFieldStorage", "words"));
        builder.WriteUInt32(offset + 16, bitField.NumBits);
    }

    private static int WriteSimulation(
        DataBuilder builder,
        StandaloneBphclClothGraph cloth,
        IReadOnlyList<int> colliderItems,
        IReadOnlyList<int> constraintItems,
        int simulateOperatorIndex)
    {
        // Keep the complete reflected object extent allocated. Its trailing
        // virtual-collision structure is currently empty, but it must not be
        // overlapped by the next DATA allocation.
        var simulationLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclSimClothData");
        var data = builder.AllocateObject(
            simulationLayout.TypeIndex,
            RequireSize(simulationLayout, 432),
            alignment: 16);
        var particles = builder.AllocateArray(StandaloneTypeCatalog.ParticleData, cloth.Particles.Count, 16, alignment: 16);
        var fixedIndices = cloth.Particles.Where(particle => particle.Fixed).Select(particle => checked((ushort)particle.Index)).ToArray();
        var fixedParticleData = builder.WriteUInt16Array(fixedIndices);
        // Particle totals are derived from the editable particle list. Fresh
        // standalone BPHCL uses the terrain and pinch defaults measured from
        // the vanilla match corpus, then derives the terrain count from masks.
        var dynamicParticles = cloth.Particles.Where(particle => !particle.Fixed).ToArray();
        var targetMaxParticleRadius = cloth.Particles.Count == 0
            ? 0.0f
            : cloth.Particles.Max(particle => particle.Radius);
        var targetTotalMass = dynamicParticles.Sum(particle => particle.Mass);
        var sourceRuntime = cloth.SimulationRuntime;
        var landscape = sourceRuntime.LandscapeCollisionData;
        var targetCollisionTolerance = 0.2f;
        var targetPinchFlags = builder.WriteBoolArray(
            new bool[cloth.Particles.Count],
            RequireArrayElementType("hclSimClothData", "perParticlePinchDetectionEnabledFlags"));
        var targetLandscapeParticleCount = checked((uint)cloth.Particles.Count(particle =>
            !particle.Fixed && (particle.CollisionMask & 0x80000000u) != 0));
        var landscapeCollisionEnabled = targetLandscapeParticleCount != 0;

        var simulationOperatorIds = builder.WriteUInt32Array(
            new[] { checked((uint)simulateOperatorIndex) },
            RequireArrayElementType("hclSimClothData", "simOpIds"));
        var positions = builder.WriteVector4Array(cloth.Particles.Select(particle => particle.Position).ToArray());
        var pose = builder.AllocateObject(StandaloneTypeCatalog.SimClothPose, 48);
        var poses = builder.WriteReferenceArray(new[] { pose.ItemIndex });
        var colliderBindings = cloth.ColliderBindings.Count == cloth.Colliders.Count
            ? cloth.ColliderBindings
            : cloth.Colliders.Select(collider => new StandaloneBphclColliderBinding(
                collider.Index,
                collider.BoneIndex >= 0 && collider.BoneIndex < cloth.Bones.Count ? collider.BoneIndex : 0,
                BuildColliderOffset(cloth, collider))).ToArray();
        var collidersByIndex = cloth.Colliders.ToDictionary(collider => collider.Index);
        if (colliderBindings.Any(binding => !collidersByIndex.ContainsKey(binding.ColliderIndex)))
            throw new InvalidDataException($"Cloth '{cloth.Name}' has a collider transform-map entry for a missing collider.");
        if (colliderBindings.Count != colliderItems.Count)
            throw new InvalidDataException($"Cloth '{cloth.Name}' has an incomplete collider transform map.");

        // References, transform indices, and offsets share one slot order.
        // Preserve that map from HKCL rather than deriving it from the
        // collidable object's transform, whose basis may be different.
        var colliderItemsByIndex = cloth.Colliders
            .Select((collider, index) => (collider.Index, Item: colliderItems[index]))
            .ToDictionary(entry => entry.Index, entry => entry.Item);
        var colliderArray = builder.WriteReferenceArray(colliderBindings
            .Select(binding => colliderItemsByIndex[binding.ColliderIndex])
            .ToArray());
        var colliderTransformIndices = builder.WriteUInt32Array(colliderBindings
            .Select(binding => checked((uint)binding.BoneIndex))
            .ToArray());
        var colliderOffsets = builder.WriteMatrix4Array(colliderBindings
            .Select(binding => binding.Offset)
            .ToArray());        var constraintArray = builder.WriteReferenceArray(constraintItems);
        var triangles = builder.WriteUInt16Array(cloth.Triangles.SelectMany(triangle => new[]
        {
            checked((ushort)triangle.ParticleA),
            checked((ushort)triangle.ParticleB),
            checked((ushort)triangle.ParticleC)
        }).ToArray());
        var masks = builder.WriteUInt32Array(
            cloth.Particles.Select(particle => particle.CollisionMask).ToArray(),
            RequireArrayElementType("hclSimClothData", "staticCollisionMasks"));

        DataBuilder.Allocation? pinchingData = null;
        if (cloth.Colliders.Count > 0)
        {
            var pinchingLayout = NativeBphclReflectionCatalog.GetTypeLayout("hclSimClothData::CollidablePinchingData");
            pinchingData = builder.AllocateArray(
                pinchingLayout.TypeIndex,
                cloth.Colliders.Count,
                RequireSize(pinchingLayout, 8),
                alignment: 4);
            var sourcePinching = sourceRuntime.CollidablePinchingData.Count == cloth.Colliders.Count
                ? sourceRuntime.CollidablePinchingData
                : null;
            for (var index = 0; index < cloth.Colliders.Count; index++)
            {
                var offset = pinchingData.DataOffset + checked((uint)(index * 8));
                var value = sourcePinching?[index] ?? new NativeBphclCollidablePinchingData(false, 0, 0.01f);
                builder.WriteByte(offset, value.Enabled ? (byte)1 : (byte)0);
                builder.WriteByte(offset + 1, unchecked((byte)value.Priority));
                builder.WriteSingle(offset + 4, value.Radius);
            }
        }

        builder.WriteReference(data.DataOffset + ObjectHeaderSize, builder.WriteString(cloth.Name));
        builder.WriteVector4(data.DataOffset + 32, new Vector4(cloth.Settings.Gravity, 0.0f));
        builder.WriteSingle(data.DataOffset + 48, cloth.Settings.DampingPerSecond);
        builder.WriteReference(data.DataOffset + 64, particles.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 72, checked((uint)cloth.Particles.Count));
        builder.WriteReference(data.DataOffset + 80, fixedParticleData.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 88, checked((uint)fixedIndices.Length));
        builder.WriteByte(data.DataOffset + 96, sourceRuntime.DoNormals ? (byte)1 : (byte)0);
        builder.WriteReference(data.DataOffset + 104, simulationOperatorIds.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 112, 1);
        builder.WriteReference(data.DataOffset + 120, poses.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 128, 1);
        builder.WriteReference(data.DataOffset + 136, constraintArray.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 144, checked((uint)constraintItems.Count));
        builder.WriteInt32(data.DataOffset + 168, 0);
        builder.WriteReference(data.DataOffset + 176, colliderTransformIndices.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 184, checked((uint)colliderBindings.Count));
        builder.WriteReference(data.DataOffset + 192, colliderOffsets.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 200, checked((uint)colliderBindings.Count));
        builder.WriteReference(data.DataOffset + 208, colliderArray.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 216, checked((uint)colliderBindings.Count));
        builder.WriteSingle(data.DataOffset + 224, targetMaxParticleRadius);
        builder.WriteSingle(data.DataOffset + 264, targetTotalMass);
        var transfer = cloth.SimulationRuntime.TransferMotionData;
        builder.WriteInt32(data.DataOffset + 268, transfer.TransformSetIndex);
        builder.WriteInt32(data.DataOffset + 272, transfer.TransformIndex);
        builder.WriteByte(data.DataOffset + 276, transfer.TransferTranslationMotion ? (byte)1 : (byte)0);
        builder.WriteSingle(data.DataOffset + 280, transfer.MinTranslationSpeed);
        builder.WriteSingle(data.DataOffset + 284, transfer.MaxTranslationSpeed);
        builder.WriteSingle(data.DataOffset + 288, transfer.MinTranslationBlend);
        builder.WriteSingle(data.DataOffset + 292, transfer.MaxTranslationBlend);
        builder.WriteByte(data.DataOffset + 296, transfer.TransferRotationMotion ? (byte)1 : (byte)0);
        builder.WriteSingle(data.DataOffset + 300, transfer.MinRotationSpeed);
        builder.WriteSingle(data.DataOffset + 304, transfer.MaxRotationSpeed);
        builder.WriteSingle(data.DataOffset + 308, transfer.MinRotationBlend);
        builder.WriteSingle(data.DataOffset + 312, transfer.MaxRotationBlend);
        builder.WriteByte(data.DataOffset + 316, sourceRuntime.TransferMotionEnabled ? (byte)1 : (byte)0);
        builder.WriteByte(data.DataOffset + 317, landscapeCollisionEnabled ? (byte)1 : (byte)0);
        builder.WriteSingle(data.DataOffset + 320, landscapeCollisionEnabled ? 0.0f : 0.05f);
        builder.WriteByte(data.DataOffset + 324, 0);
        builder.WriteSingle(data.DataOffset + 328, 9.0f);
        builder.WriteByte(data.DataOffset + 332, 0);
        builder.WriteByte(data.DataOffset + 333, 0);
        builder.WriteSingle(data.DataOffset + 336, landscapeCollisionEnabled ? 0.01f : 0.0f);
        builder.WriteSingle(data.DataOffset + 340, targetCollisionTolerance);
        builder.WriteUInt32(data.DataOffset + 344, targetLandscapeParticleCount);
        builder.WriteReference(data.DataOffset + 352, triangles.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 360, checked((uint)(cloth.Triangles.Count * 3)));
        builder.WriteByte(data.DataOffset + 384, sourceRuntime.PinchDetectionEnabled ? (byte)1 : (byte)0);
        builder.WriteReference(data.DataOffset + 392, targetPinchFlags.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 400, checked((uint)cloth.Particles.Count));
        if (pinchingData is not null)
        {
            builder.WriteReference(data.DataOffset + 408, pinchingData.ItemIndex);
            builder.WriteUInt32(data.DataOffset + 416, checked((uint)cloth.Colliders.Count));
        }
        builder.WriteUInt16(data.DataOffset + 424, 0);
        builder.WriteUInt16(data.DataOffset + 426, 0);
        builder.WriteUInt32(data.DataOffset + 428, 0);

        var maskOffset = NativeBphclReflectionCatalog.GetMemberOffset("hclSimClothData", "m_staticCollisionMasks")
            ?? NativeBphclReflectionCatalog.GetMemberOffset("hclSimClothData", "staticCollisionMasks");
        if (maskOffset is uint maskMemberOffset)
        {
            builder.WriteReference(data.DataOffset + maskMemberOffset, masks.ItemIndex);
            builder.WriteUInt32(data.DataOffset + maskMemberOffset + 8, checked((uint)cloth.Particles.Count));
        }

        builder.WriteReference(pose.DataOffset + 24, builder.WriteString($"{cloth.Name}_SimClothPose"));
        builder.WriteReference(pose.DataOffset + 32, positions.ItemIndex);
        builder.WriteUInt32(pose.DataOffset + 40, checked((uint)cloth.Particles.Count));

        for (var index = 0; index < cloth.Particles.Count; index++)
        {
            var particle = cloth.Particles[index];
            var particleOffset = particles.DataOffset + checked((uint)(index * 16));
            builder.WriteSingle(particleOffset, particle.Mass);
            builder.WriteSingle(particleOffset + 4, particle.InverseMass);
            builder.WriteSingle(particleOffset + 8, particle.Radius);
            builder.WriteSingle(particleOffset + 12, particle.Friction);
        }

        return data.ItemIndex;
    }

    private static Matrix4x4 BuildColliderOffset(StandaloneBphclClothGraph cloth, StandaloneBphclCollider collider)
    {
        if (collider.BoneIndex < 0 || collider.BoneIndex >= cloth.Bones.Count)
            return collider.Transform;

        var boneWorld = GetBoneWorldTransform(cloth.Bones, collider.BoneIndex);
        return Matrix4x4.Invert(boneWorld, out var inverseBone)
            ? collider.Transform * inverseBone
            : collider.Transform;
    }

    private static Matrix4x4 GetBoneWorldTransform(IReadOnlyList<StandaloneBphclBone> bones, int boneIndex)
    {
        var byIndex = bones.ToDictionary(bone => bone.Index);
        var chain = new Stack<StandaloneBphclBone>();
        var visited = new HashSet<int>();
        var current = boneIndex;
        while (current >= 0 && visited.Add(current) && byIndex.TryGetValue(current, out var bone))
        {
            chain.Push(bone);
            current = bone.ParentIndex;
        }

        var world = Matrix4x4.Identity;
        while (chain.Count > 0)
        {
            var bone = chain.Pop();
            var rotationRow = new Quaternion(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W);
            var rotation = rotationRow.LengthSquared() < 0.000001f
                ? Quaternion.Identity
                : Quaternion.Normalize(rotationRow);
            var local = Matrix4x4.CreateScale(bone.Scale.X, bone.Scale.Y, bone.Scale.Z)
                * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(bone.Translation.X, bone.Translation.Y, bone.Translation.Z);
            world = local * world;
        }
        return world;
    }

    private static IReadOnlyList<int> WriteConstraintSets(DataBuilder builder, StandaloneBphclClothGraph cloth)
    {
        var linkSets = cloth.Constraints.GroupBy(constraint => constraint.ConstraintSetIndex)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var rangeSets = cloth.LocalRanges.GroupBy(range => range.ConstraintSetIndex)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var transitionSets = cloth.TransitionSets.ToDictionary(set => set.ConstraintSetIndex);
        var bonePlaneSets = cloth.BonePlaneSets.ToDictionary(set => set.ConstraintSetIndex);
        var result = new List<int>();

        foreach (var metadata in cloth.ConstraintSets.OrderBy(set => set.Index))
        {
            var setIndex = metadata.Index;
            var hasLinks = linkSets.TryGetValue(setIndex, out var links);
            var hasRanges = rangeSets.TryGetValue(setIndex, out var ranges);
            var hasTransition = transitionSets.TryGetValue(setIndex, out var transition);
            var hasBonePlanes = bonePlaneSets.TryGetValue(setIndex, out var bonePlanes);
            if ((hasLinks ? 1 : 0) + (hasRanges ? 1 : 0) + (hasTransition ? 1 : 0) + (hasBonePlanes ? 1 : 0) > 1)
            {
                throw new InvalidDataException(
                    $"Cloth '{cloth.Name}' constraint set {setIndex} mixes incompatible record layouts.");
            }

            if (string.Equals(metadata.TypeName, "hclTransitionConstraintSet", StringComparison.Ordinal))
            {
                result.Add(WriteTransitionConstraintSet(builder, metadata, transition ?? new StandaloneBphclTransitionConstraintSet(
                    setIndex,
                    metadata.Name,
                    metadata.ConstraintId,
                    metadata.ConstraintType,
                    0.0f,
                    0.0f,
                    0.0f,
                    0.0f,
                    0u,
                    Array.Empty<StandaloneBphclTransitionParticle>())));
            }
            else if (string.Equals(metadata.TypeName, "hclLocalRangeConstraintSet", StringComparison.Ordinal))
            {
                result.Add(WriteLocalRangeConstraintSet(builder, cloth.Name, metadata, ranges ?? Array.Empty<StandaloneBphclLocalRange>()));
            }
            else if (string.Equals(metadata.TypeName, "hclBonePlanesConstraintSet", StringComparison.Ordinal))
            {
                result.Add(WriteBonePlanesConstraintSet(builder, cloth.Name, metadata, bonePlanes ?? new StandaloneBphclBonePlaneConstraintSet(
                    setIndex,
                    metadata.Name,
                    metadata.ConstraintId,
                    metadata.ConstraintType,
                    0u,
                    Array.Empty<StandaloneBphclBonePlane>())));
            }
            else
            {
                result.Add(WriteLinkConstraintSet(builder, cloth.Name, metadata, links ?? Array.Empty<StandaloneBphclConstraint>()));
            }
        }

        return result;
    }

    // BotW HKCL exports use zeroed IDs, while native BPHCL assigns the static
    // constraint-set ordinal. Preserve an explicit nonzero ID, otherwise use
    // that native ordinal so every set remains individually addressable.
    private static uint ResolveConstraintId(StandaloneBphclConstraintSetMetadata metadata) =>
        metadata.ConstraintId != 0 ? metadata.ConstraintId : checked((uint)metadata.Index);

    private static int WriteLinkConstraintSet(
        DataBuilder builder,
        string clothName,
        StandaloneBphclConstraintSetMetadata metadata,
        IReadOnlyList<StandaloneBphclConstraint> links)
    {
        var className = metadata.TypeName;
        if (links.Any(link => !string.Equals(link.TypeName, className, StringComparison.Ordinal)))
            throw new InvalidDataException($"Cloth '{clothName}' constraint set {metadata.Index} has inconsistent Havok link classes.");

        var setLayout = NativeBphclReflectionCatalog.GetTypeLayout(className);
        var linkLayout = NativeBphclReflectionCatalog.GetTypeLayout(className + "::Link");
        var set = builder.AllocateObject(setLayout.TypeIndex, RequireSize(setLayout, 40));
        var records = builder.AllocateArray(linkLayout.TypeIndex, links.Count, RequireSize(linkLayout, 1), alignment: 4);
        var linkOffset = NativeBphclReflectionCatalog.GetMemberOffset(className, "links") ?? 40;

        builder.WriteReference(set.DataOffset + ObjectHeaderSize, builder.WriteString(metadata.Name));
        builder.WriteUInt32(set.DataOffset + 32, ResolveConstraintId(metadata));
        builder.WriteUInt32(set.DataOffset + 36, metadata.ConstraintType);
        WriteBendStiffnessSetValues(builder, set.DataOffset, setLayout, metadata);
        builder.WriteReference(set.DataOffset + linkOffset, records.ItemIndex);
        builder.WriteUInt32(set.DataOffset + linkOffset + 8, checked((uint)links.Count));

        for (var index = 0; index < links.Count; index++)
            WriteLinkRecord(builder, records.DataOffset + checked((uint)(index * RequireSize(linkLayout, 1))), linkLayout, links[index]);

        return set.ItemIndex;
    }

    private static int WriteBonePlanesConstraintSet(
        DataBuilder builder,
        string clothName,
        StandaloneBphclConstraintSetMetadata metadata,
        StandaloneBphclBonePlaneConstraintSet bonePlanes)
    {
        const string className = "hclBonePlanesConstraintSet";
        var setLayout = NativeBphclReflectionCatalog.GetTypeLayout(className);
        var planeLayout = NativeBphclReflectionCatalog.GetTypeLayout(className + "::BonePlane");
        var set = builder.AllocateObject(setLayout.TypeIndex, RequireSize(setLayout, 64));
        var records = builder.AllocateArray(planeLayout.TypeIndex, bonePlanes.Planes.Count, RequireSize(planeLayout, 24), alignment: 16);
        var planeOffset = NativeBphclReflectionCatalog.GetMemberOffset(className, "bonePlanes") ?? 40;
        var transformSetOffset = NativeBphclReflectionCatalog.GetMemberOffset(className, "transformSetIndex") ?? 56;

        builder.WriteReference(set.DataOffset + ObjectHeaderSize, builder.WriteString(bonePlanes.Name));
        builder.WriteUInt32(set.DataOffset + 32, bonePlanes.ConstraintId != 0 ? bonePlanes.ConstraintId : checked((uint)bonePlanes.ConstraintSetIndex));
        builder.WriteUInt32(set.DataOffset + 36, bonePlanes.ConstraintType);
        builder.WriteReference(set.DataOffset + planeOffset, records.ItemIndex);
        builder.WriteUInt32(set.DataOffset + planeOffset + 8, checked((uint)bonePlanes.Planes.Count));
        builder.WriteUInt32(set.DataOffset + transformSetOffset, bonePlanes.TransformSetIndex);

        for (var index = 0; index < bonePlanes.Planes.Count; index++)
        {
            var plane = bonePlanes.Planes[index];
            var recordOffset = records.DataOffset + checked((uint)(index * RequireSize(planeLayout, 24)));
            foreach (var member in planeLayout.Members)
            {
                if (member.Name == "planeEquationBone")
                    builder.WriteVector4(recordOffset + member.Offset, plane.PlaneEquationBone);
                else if (member.Name == "particleIndex")
                    builder.WriteScalar(recordOffset + member.Offset, member.TypeName, plane.ParticleIndex);
                else if (member.Name == "transformIndex")
                    builder.WriteScalar(recordOffset + member.Offset, member.TypeName, plane.TransformIndex);
                else if (member.Name == "stiffness")
                    builder.WriteScalar(recordOffset + member.Offset, member.TypeName, plane.Stiffness);
            }
        }

        if (!string.Equals(metadata.Name, bonePlanes.Name, StringComparison.Ordinal))
            throw new InvalidDataException($"Cloth '{clothName}' bone-plane metadata does not match its set name.");
        return set.ItemIndex;
    }

    private static int WriteLocalRangeConstraintSet(
        DataBuilder builder,
        string clothName,
        StandaloneBphclConstraintSetMetadata metadata,
        IReadOnlyList<StandaloneBphclLocalRange> ranges)
    {
        var className = metadata.TypeName;
        if (!string.Equals(className, "hclLocalRangeConstraintSet", StringComparison.Ordinal))
            throw new InvalidDataException($"Cloth '{clothName}' local range set {metadata.Index} has unsupported class '{className}'.");

        var setLayout = NativeBphclReflectionCatalog.GetTypeLayout(className);
        var recordLayout = NativeBphclReflectionCatalog.GetTypeLayout(className + "::LocalConstraint");
        var set = builder.AllocateObject(setLayout.TypeIndex, RequireSize(setLayout, 40));
        var records = builder.AllocateArray(recordLayout.TypeIndex, ranges.Count, RequireSize(recordLayout, 1), alignment: 4);
        var rangeOffset = NativeBphclReflectionCatalog.GetMemberOffset(className, "localConstraints") ?? 40;

        builder.WriteReference(set.DataOffset + ObjectHeaderSize, builder.WriteString(metadata.Name));
        builder.WriteUInt32(set.DataOffset + 32, ResolveConstraintId(metadata));
        builder.WriteUInt32(set.DataOffset + 36, metadata.ConstraintType);
        builder.WriteReference(set.DataOffset + rangeOffset, records.ItemIndex);
        builder.WriteUInt32(set.DataOffset + rangeOffset + 8, checked((uint)ranges.Count));

        // The set-level controls select which simulated reference mesh and
        // local-range shape the records use. Leaving them at zero changes a
        // range into a hard pin in TotK, even if the particle data is valid.
        var source = ranges.FirstOrDefault();
        if (source != null)
        {
            var referenceMeshOffset = NativeBphclReflectionCatalog.GetMemberOffset(className, "referenceMeshBufferIdx");
            if (referenceMeshOffset is uint referenceOffset)
                builder.WriteUInt32(set.DataOffset + referenceOffset, source.ReferenceMeshBufferIndex);

            var stiffnessOffset = NativeBphclReflectionCatalog.GetMemberOffset(className, "stiffness");
            if (stiffnessOffset is uint setStiffnessOffset)
                builder.WriteSingle(set.DataOffset + setStiffnessOffset, source.Stiffness);

            var shapeTypeOffset = NativeBphclReflectionCatalog.GetMemberOffset(className, "shapeType");
            if (shapeTypeOffset is uint rangeShapeOffset)
                builder.WriteInt32(set.DataOffset + rangeShapeOffset, source.ShapeType);

            var normalComponentOffset = NativeBphclReflectionCatalog.GetMemberOffset(className, "applyNormalComponent");
            if (normalComponentOffset is uint normalOffset)
                builder.WriteByte(set.DataOffset + normalOffset, source.ApplyNormalComponent ? (byte)1 : (byte)0);
        }

        for (var index = 0; index < ranges.Count; index++)
        {
            var range = ranges[index];
            var recordOffset = records.DataOffset + checked((uint)(index * RequireSize(recordLayout, 1)));
            foreach (var member in recordLayout.Members)
            {
                switch (member.Name)
                {
                    case "particleIndex":
                        builder.WriteScalar(recordOffset + member.Offset, member.TypeName, range.ParticleIndex);
                        break;
                    case "referenceVertex":
                        builder.WriteScalar(recordOffset + member.Offset, member.TypeName, range.ReferenceVertex);
                        break;
                    case "shapeRadius":
                    case "maximumDistance":
                    case "maxDistance":
                        builder.WriteScalar(recordOffset + member.Offset, member.TypeName, range.ShapeRadius);
                        break;
                    case "maxNormalDistance":
                        builder.WriteScalar(recordOffset + member.Offset, member.TypeName, range.MaxNormalDistance);
                        break;
                    case "minNormalDistance":
                        builder.WriteScalar(recordOffset + member.Offset, member.TypeName, range.MinNormalDistance);
                        break;
                }
            }
        }

        return set.ItemIndex;
    }

    private static int WriteTransitionConstraintSet(
        DataBuilder builder,
        StandaloneBphclConstraintSetMetadata metadata,
        StandaloneBphclTransitionConstraintSet transition)
    {
        const string className = "hclTransitionConstraintSet";
        var setLayout = NativeBphclReflectionCatalog.GetTypeLayout(className);
        var recordLayout = NativeBphclReflectionCatalog.GetTypeLayout(className + "::PerParticle");
        var set = builder.AllocateObject(setLayout.TypeIndex, RequireSize(setLayout, 80));
        var records = builder.AllocateArray(recordLayout.TypeIndex, transition.Particles.Count, RequireSize(recordLayout, 16), alignment: 4);
        var dataOffset = set.DataOffset;

        builder.WriteReference(dataOffset + ObjectHeaderSize, builder.WriteString(transition.Name));
        builder.WriteUInt32(dataOffset + 32, transition.ConstraintId != 0 ? transition.ConstraintId : checked((uint)transition.ConstraintSetIndex));
        builder.WriteUInt32(dataOffset + 36, transition.ConstraintType);
        builder.WriteReference(dataOffset + 40, records.ItemIndex);
        builder.WriteUInt32(dataOffset + 48, checked((uint)transition.Particles.Count));
        builder.WriteSingle(dataOffset + 56, transition.ToAnimationPeriod);
        builder.WriteSingle(dataOffset + 60, transition.ToAnimationPlusDelayPeriod);
        builder.WriteSingle(dataOffset + 64, transition.ToSimulationPeriod);
        builder.WriteSingle(dataOffset + 68, transition.ToSimulationPlusDelayPeriod);
        builder.WriteUInt32(dataOffset + 72, transition.ReferenceMeshBufferIndex);

        for (var index = 0; index < transition.Particles.Count; index++)
        {
            var particle = transition.Particles[index];
            var offset = records.DataOffset + checked((uint)(index * RequireSize(recordLayout, 16)));
            builder.WriteUInt16(offset, checked((ushort)particle.ParticleIndex));
            builder.WriteUInt16(offset + 2, checked((ushort)particle.ReferenceVertex));
            builder.WriteSingle(offset + 4, particle.ToAnimationDelay);
            builder.WriteSingle(offset + 8, particle.ToSimulationDelay);
            builder.WriteSingle(offset + 12, particle.ToSimulationMaximumDistance);
        }

        return set.ItemIndex;
    }

    private static void WriteBendStiffnessSetValues(
        DataBuilder builder,
        uint setOffset,
        NativeBphclTypeLayout layout,
        StandaloneBphclConstraintSetMetadata metadata)
    {
        if (!string.Equals(metadata.TypeName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal))
            return;

        foreach (var member in layout.Members)
        {
            object? value = member.Name switch
            {
                "clampBendStiffness" => true,
                "maxRestPoseHeightSq" => metadata.MaxRestPoseHeightSq,
                "useRestPoseConfig" => true,
                _ => null
            };
            if (value is not null)
                builder.WriteScalar(setOffset + member.Offset, member.TypeName, value);
        }
    }
    private static void WriteLinkRecord(
        DataBuilder builder,
        uint recordOffset,
        NativeBphclTypeLayout layout,
        StandaloneBphclConstraint link)
    {
        foreach (var member in layout.Members)
        {
            object? value = member.Name switch
            {
                "particleA" => link.ParticleA,
                "particleB" => link.ParticleB,
                "particleC" => link.ParticleC,
                "particleD" => link.ParticleD,
                "restCurvature" => link.RestCurvature,
                "weightA" => link.WeightA,
                "weightB" => link.WeightB,
                "weightC" => link.WeightC,
                "weightD" => link.WeightD,
                "restLength" => link.RestLength,
                "bendMinLength" => link.BendMinLength,
                "stretchMaxLength" => link.StretchMaxLength,
                "stiffness" => link.Stiffness,
                "bendStiffness" => link.BendStiffness,
                "stretchStiffness" => link.StretchStiffness,
                _ => null
            };
            if (value is not null)
                builder.WriteScalar(recordOffset + member.Offset, member.TypeName, value);
        }
    }

    private static uint RequireArrayElementType(uint ownerType, uint memberOffset)
    {
        var arrayType = NativeBphclReflectionCatalog.GetMemberTypeIndex(ownerType, memberOffset)
            ?? throw new InvalidDataException($"BPHCL type {ownerType} has no array field at +0x{memberOffset:X}.");
        return NativeBphclReflectionCatalog.GetArrayElementType(arrayType)
            ?? throw new InvalidDataException($"BPHCL array field at +0x{memberOffset:X} has no reflected element type.");
    }
    private static int RequireSize(NativeBphclTypeLayout layout, int minimumSize)
    {
        if (layout.Size < minimumSize || layout.Size > int.MaxValue)
            throw new InvalidDataException($"Built-in BPHCL TYPE layout '{layout.Name}' has an invalid size {layout.Size}.");
        return checked((int)layout.Size);
    }

    private static int WriteSkeleton(DataBuilder builder, StandaloneBphclClothGraph cloth)
    {
        var layout = NativeBphclReflectionCatalog.GetTypeLayout("hkaSkeleton");
        var data = builder.AllocateObject(layout.TypeIndex, RequireSize(layout, 144));
        // hkaSkeleton stores parent indices as signed 16-bit values so the
        // root's -1 parent remains a real root after Havok resolves ITEM data.
        var parents = builder.WriteInt16Array(cloth.Bones.Select(bone =>
            bone.ParentIndex < 0 ? (short)-1 : checked((short)bone.ParentIndex)).ToArray());
        var bones = builder.AllocateArray(StandaloneTypeCatalog.Bone, cloth.Bones.Count, 16, alignment: 8);
        var poses = builder.AllocateArray(StandaloneTypeCatalog.QsTransform, cloth.Bones.Count, 48, alignment: 16);

        builder.WriteReference(data.DataOffset + ObjectHeaderSize, builder.WriteString(cloth.SkeletonName));
        builder.WriteReference(data.DataOffset + 32, parents.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 40, checked((uint)cloth.Bones.Count));
        builder.WriteReference(data.DataOffset + 48, bones.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 56, checked((uint)cloth.Bones.Count));
        builder.WriteReference(data.DataOffset + 64, poses.ItemIndex);
        builder.WriteUInt32(data.DataOffset + 72, checked((uint)cloth.Bones.Count));

        for (var index = 0; index < cloth.Bones.Count; index++)
        {
            var bone = cloth.Bones[index];
            var boneOffset = bones.DataOffset + checked((uint)(index * 16));
            var poseOffset = poses.DataOffset + checked((uint)(index * 48));
            builder.WriteReference(boneOffset, builder.WriteString(bone.Name));
            builder.WriteVector4(poseOffset, bone.Translation);
            builder.WriteVector4(poseOffset + 16, bone.Rotation);
            builder.WriteVector4(poseOffset + 32, bone.Scale);
        }

        return data.ItemIndex;
    }

    private static int GetOrWriteSharedCollider(
        DataBuilder builder,
        StandaloneBphclCollider collider,
        IDictionary<int, (StandaloneBphclCollider Definition, int ItemIndex)> sharedColliders,
        ICollection<int> outerColliders)
    {
        if (sharedColliders.TryGetValue(collider.Index, out var existing))
        {
            if (!ColliderDefinitionMatches(existing.Definition, collider))
            {
                throw new InvalidDataException(
                    $"Collider {collider.Index} has incompatible geometry in more than one cloth. " +
                    "A standalone BPHCL must preserve one collider definition for each HKCL object identity.");
            }

            return existing.ItemIndex;
        }

        var itemIndex = WriteCollider(builder, collider);
        sharedColliders.Add(collider.Index, (collider, itemIndex));
        outerColliders.Add(itemIndex);
        return itemIndex;
    }

    private static bool ColliderDefinitionMatches(
        StandaloneBphclCollider left,
        StandaloneBphclCollider right) =>
        string.Equals(left.ShapeType, right.ShapeType, StringComparison.Ordinal) &&
        left.Start == right.Start &&
        left.End == right.End &&
        left.Radius.Equals(right.Radius) &&
        left.PlaneNormal == right.PlaneNormal &&
        left.Transform.Equals(right.Transform);
    private static int WriteCollider(DataBuilder builder, StandaloneBphclCollider collider)
    {
        var shape = WriteColliderShape(builder, collider);
        var data = builder.AllocateObject(StandaloneTypeCatalog.Collidable, 160);
        var transform = collider.Transform;
        builder.WriteVector4(data.DataOffset + 32, collider.NativeAxisX ?? new Vector4(transform.M11, transform.M12, transform.M13, 0.0f));
        builder.WriteVector4(data.DataOffset + 48, collider.NativeAxisY ?? new Vector4(transform.M21, transform.M22, transform.M23, 0.0f));
        builder.WriteVector4(data.DataOffset + 64, collider.NativeAxisZ ?? new Vector4(transform.M31, transform.M32, transform.M33, 0.0f));
        builder.WriteVector4(data.DataOffset + 80, collider.NativeTranslation ?? new Vector4(transform.M41, transform.M42, transform.M43, 0.0f));
        builder.WriteReference(data.DataOffset + 136, shape);
        builder.WriteReference(data.DataOffset + 144, builder.WriteString(collider.Name));
        builder.WriteSingle(data.DataOffset + 152, collider.PinchDetectionRadius);
        builder.WriteByte(data.DataOffset + 156, unchecked((byte)collider.PinchDetectionPriority));
        builder.WriteByte(data.DataOffset + 157, collider.PinchDetectionEnabled ? (byte)1 : (byte)0);
        builder.WriteByte(data.DataOffset + 158, collider.VirtualCollisionPointCollisionEnabled ? (byte)1 : (byte)0);
        builder.WriteByte(data.DataOffset + 159, collider.Enabled ? (byte)1 : (byte)0);
        return data.ItemIndex;
    }

    private static int WriteColliderShape(DataBuilder builder, StandaloneBphclCollider collider)
    {
        var isSphere = collider.ShapeType is "hclSphereShape" or "Sphere";
        var isPlane = collider.ShapeType is "hclPlaneShape" or "Plane";
        var typeIndex = isSphere
            ? StandaloneTypeCatalog.SphereShape
            : isPlane
                ? StandaloneTypeCatalog.PlaneShape
                : StandaloneTypeCatalog.CapsuleShape;
        var data = builder.AllocateObject(typeIndex, isPlane ? 48 : 96);
        builder.WriteInt32(data.DataOffset + ObjectHeaderSize, collider.ShapeKind);
        if (isPlane)
        {
            builder.WriteVector4(data.DataOffset + 32, collider.NativePlaneEquation ?? new Vector4(collider.PlaneNormal, 0.0f));
        }
        else if (isSphere)
        {
            builder.WriteVector4(data.DataOffset + 32, collider.NativeStart ?? new Vector4(collider.Start, collider.Radius));
        }
        else
        {
            var start = collider.NativeStart ?? new Vector4(collider.Start, 0.0f);
            var end = collider.NativeEnd ?? new Vector4(collider.End, 0.0f);
            var delta = new Vector3(end.X - start.X, end.Y - start.Y, end.Z - start.Z);
            var lengthSquared = delta.LengthSquared();
            var direction = collider.NativeDirection ?? new Vector4(delta, 0.0f);
            var capLenSqrdInv = collider.NativeCapsuleLengthSquaredInverse
                ?? (lengthSquared > 0.0000001f ? 1.0f / lengthSquared : 0.0f);

            builder.WriteVector4(data.DataOffset + 32, start);
            builder.WriteVector4(data.DataOffset + 48, end);
            builder.WriteVector4(data.DataOffset + 64, direction);
            builder.WriteSingle(data.DataOffset + 80, collider.Radius);
            builder.WriteSingle(data.DataOffset + 84, capLenSqrdInv);
        }
        return data.ItemIndex;
    }
    private sealed record RootScaffold(
        uint ClothContainerDataOffset,
        uint ResourceContainerDataOffset,
        uint AnimationContainerDataOffset,
        DataBuilder.Allocation CollidableStorage,
        DataBuilder.Allocation ClothStorage);

    private sealed record RootVariant(string Name, string ClassName, int ClothItem, int SkeletonItem);
    private sealed record RuntimePipeline(
        IReadOnlyList<int> OperatorItems,
        IReadOnlyList<int> StateItems,
        int SimulateOperatorIndex);

    private sealed class DataBuilder
    {
        private readonly List<byte> _data = new(new byte[16]);
        private readonly List<NativeBphclItem> _items = new();
        private readonly List<AllocationInfo> _allocations = new();
        private readonly List<uint> _referenceOffsets = new();
        private readonly Dictionary<uint, uint> _referencePatchTypeOverrides = new();

        public DataBuilder()
        {
            var rootType = NativeBphclReflectionCatalog.GetTypeIndex("hkRootLevelContainer");
            _items.Add(new NativeBphclItem(0, 0, 0, 0));
            _items.Add(new NativeBphclItem(0x1000_0000u | rootType, rootType, 0, 1));
        }

        public IReadOnlyList<NativeBphclItem> Items => _items;

        // ITEM uses distinct high-bit categories for object instances and backing
        // storage. The game accepts both records only when these match native TAG0.
        public Allocation AllocateObject(uint typeIndex, int size, int alignment = 8) =>
            Allocate(typeIndex, 1, size, alignment, 0x1000_0000u);

        public Reservation ReserveObject(uint typeIndex)
        {
            var itemIndex = _items.Count;
            _items.Add(new NativeBphclItem(0x1000_0000u | typeIndex, typeIndex, 0, 1));
            return new Reservation(itemIndex, typeIndex);
        }

        public Allocation Materialize(Reservation reservation, int size, int alignment = 8)
        {
            if ((uint)reservation.ItemIndex >= (uint)_items.Count ||
                _items[reservation.ItemIndex].TypeIndex != reservation.TypeIndex ||
                _allocations.Any(candidate => candidate.ItemIndex == reservation.ItemIndex))
            {
                throw new InvalidOperationException("Invalid or already materialized BPHCL ITEM reservation.");
            }

            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size));

            Align(alignment);
            var offset = checked((uint)_data.Count);
            _data.AddRange(new byte[size]);
            _items[reservation.ItemIndex] = _items[reservation.ItemIndex] with { DataOffset = offset };
            _allocations.Add(new AllocationInfo(reservation.ItemIndex, reservation.TypeIndex, offset, size));
            return new Allocation(reservation.ItemIndex, offset, size, this);
        }

        public Allocation AllocateArray(uint typeIndex, int count, int stride, int alignment = 8) =>
            Allocate(typeIndex, checked((uint)count), checked(count * stride), alignment, 0x2000_0000u);

        public Allocation WriteReferenceArray(IReadOnlyList<int> itemIndices)
        {
            var array = AllocateArray(StandaloneTypeCatalog.Pointer, itemIndices.Count, 8, alignment: 8);
            for (var index = 0; index < itemIndices.Count; index++)
                WriteReference(array.DataOffset + checked((uint)(index * 8)), itemIndices[index]);
            return array;
        }

        public Allocation WriteUInt16Array(IReadOnlyList<ushort> values)
        {
            var array = AllocateArray(StandaloneTypeCatalog.UInt16, values.Count, 2, alignment: 2);
            for (var index = 0; index < values.Count; index++)
                WriteUInt16(array.DataOffset + checked((uint)(index * 2)), values[index]);
            return array;
        }

        public Allocation WriteInt16Array(IReadOnlyList<short> values)
        {
            var array = AllocateArray(StandaloneTypeCatalog.Int16, values.Count, 2, alignment: 2);
            for (var index = 0; index < values.Count; index++)
                WriteInt16(array.DataOffset + checked((uint)(index * 2)), values[index]);
            return array;
        }

        public Allocation WriteUInt32Array(IReadOnlyList<uint> values, uint? elementType = null)
        {
            var array = AllocateArray(elementType ?? StandaloneTypeCatalog.UInt32, values.Count, 4, alignment: 4);
            for (var index = 0; index < values.Count; index++)
                WriteUInt32(array.DataOffset + checked((uint)(index * 4)), values[index]);
            return array;
        }

        public Allocation WriteInt32Array(IReadOnlyList<int> values, uint elementType)
        {
            var array = AllocateArray(elementType, values.Count, 4, alignment: 4);
            for (var index = 0; index < values.Count; index++)
                WriteInt32(array.DataOffset + checked((uint)(index * 4)), values[index]);
            return array;
        }

        public Allocation WriteBoolArray(IReadOnlyList<bool> values, uint elementType)
        {
            var array = AllocateArray(elementType, values.Count, 1, alignment: 1);
            for (var index = 0; index < values.Count; index++)
                WriteByte(array.DataOffset + checked((uint)index), values[index] ? (byte)1 : (byte)0);
            return array;
        }

        public Allocation WriteByteArray(IReadOnlyList<byte> values)
        {
            var array = AllocateArray(StandaloneTypeCatalog.UInt8, values.Count, 1, alignment: 1);
            for (var index = 0; index < values.Count; index++)
                WriteByte(array.DataOffset + checked((uint)index), values[index]);
            return array;
        }

        public Allocation WriteVector4Array(IReadOnlyList<Vector4> values)
        {
            var array = AllocateArray(StandaloneTypeCatalog.Vector4, values.Count, 16, alignment: 16);
            for (var index = 0; index < values.Count; index++)
                WriteVector4(array.DataOffset + checked((uint)(index * 16)), values[index]);
            return array;
        }

        public Allocation WriteMatrix4Array(IReadOnlyList<Matrix4x4> values)
        {
            var array = AllocateArray(StandaloneTypeCatalog.Matrix4, values.Count, 64, alignment: 16);
            for (var index = 0; index < values.Count; index++)
            {
                var offset = array.DataOffset + checked((uint)(index * 64));
                var matrix = values[index];
                WriteVector4(offset, new Vector4(matrix.M11, matrix.M12, matrix.M13, matrix.M14));
                WriteVector4(offset + 16, new Vector4(matrix.M21, matrix.M22, matrix.M23, matrix.M24));
                WriteVector4(offset + 32, new Vector4(matrix.M31, matrix.M32, matrix.M33, matrix.M34));
                WriteVector4(offset + 48, new Vector4(matrix.M41, matrix.M42, matrix.M43, matrix.M44));
            }
            return array;
        }

        public int WriteString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value + '\0');
            // A string pointer targets a raw char buffer. hkStringPtr is the
            // pointer field type, not the type of its allocated bytes.
            var stringData = AllocateArray(StandaloneTypeCatalog.Char, bytes.Length, 1, alignment: 1);
            bytes.AsSpan().CopyTo(GetSpan(stringData.DataOffset, bytes.Length));
            return stringData.ItemIndex;
        }

        public void WriteReference(uint dataOffset, int itemIndex, uint? patchTypeOverride = null)
        {
            if ((uint)itemIndex >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(itemIndex));
            WriteUInt32(dataOffset, checked((uint)itemIndex));
            PromoteReferenceArrayStorage(itemIndex, GetReferencePatchType(dataOffset));
            if (patchTypeOverride is uint patchType)
                _referencePatchTypeOverrides[dataOffset] = patchType;
            _referenceOffsets.Add(dataOffset);
        }

        public void WriteUInt16(uint dataOffset, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(GetSpan(dataOffset, 2), value);

        public void WriteInt16(uint dataOffset, short value) =>
            BinaryPrimitives.WriteInt16LittleEndian(GetSpan(dataOffset, 2), value);

        public void WriteUInt32(uint dataOffset, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(GetSpan(dataOffset, 4), value);

        public void WriteInt32(uint dataOffset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(GetSpan(dataOffset, 4), value);

        public void WriteByte(uint dataOffset, byte value) =>
            GetSpan(dataOffset, 1)[0] = value;

        public void WriteBytes(uint dataOffset, ReadOnlySpan<byte> values) =>
            values.CopyTo(GetSpan(dataOffset, values.Length));

        public void WriteSingle(uint dataOffset, float value) =>
            BinaryPrimitives.WriteSingleLittleEndian(GetSpan(dataOffset, 4), value);

        public void WriteVector4(uint dataOffset, Vector4 value)
        {
            WriteSingle(dataOffset, value.X);
            WriteSingle(dataOffset + 4, value.Y);
            WriteSingle(dataOffset + 8, value.Z);
            WriteSingle(dataOffset + 12, value.W);
        }

        public void WriteScalar(uint dataOffset, string typeName, object value)
        {
            switch (typeName)
            {
                case "hkUint8":
                case "hkBool":
                    GetSpan(dataOffset, 1)[0] = value switch
                    {
                        bool boolean => boolean ? (byte)1 : (byte)0,
                        _ => checked((byte)Convert.ToInt32(value, CultureInfo.InvariantCulture))
                    };
                    break;
                case "hkInt8":
                    GetSpan(dataOffset, 1)[0] = unchecked((byte)Convert.ToSByte(value, CultureInfo.InvariantCulture));
                    break;
                case "hkUint16":
                    WriteUInt16(dataOffset, checked((ushort)Convert.ToInt32(value, CultureInfo.InvariantCulture)));
                    break;
                case "hkInt16":
                    WriteUInt16(dataOffset, unchecked((ushort)Convert.ToInt16(value, CultureInfo.InvariantCulture)));
                    break;
                case "hkUint32":
                    WriteUInt32(dataOffset, checked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)));
                    break;
                case "hkInt32":
                    WriteInt32(dataOffset, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                    break;
                case "hkReal":
                    WriteSingle(dataOffset, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new InvalidDataException($"Standalone BPHCL writer cannot serialize scalar field type '{typeName}'.");
            }
        }

        public void NormalizeHavokArrayHeaders()
        {
            NormalizeHavokArrayHeaders(
                dataOffset: 0,
                typeIndex: NativeBphclReflectionCatalog.GetTypeIndex("hkRootLevelContainer"),
                elementCount: 1,
                byteLength: 16);

            foreach (var allocation in _allocations)
            {
                var elementSize = NativeBphclReflectionCatalog.GetTypeSize(allocation.TypeIndex);
                if (elementSize == 0)
                    continue;

                var elementCount = Math.Min(
                    _items[allocation.ItemIndex].Count,
                    checked((uint)(allocation.ByteLength / elementSize)));
                NormalizeHavokArrayHeaders(
                    allocation.Offset,
                    allocation.TypeIndex,
                    elementCount,
                    allocation.ByteLength);
            }
        }

        public byte[] ToDataPayload() => _data.ToArray();

        public IReadOnlyList<NativeBphclPatch> BuildPatches() => _referenceOffsets
            .GroupBy(offset => _referencePatchTypeOverrides.TryGetValue(offset, out var patchType)
                ? patchType
                : GetReferencePatchType(offset))
            .OrderBy(group => group.Key)
            .Select(group => new NativeBphclPatch(group.Key, group.OrderBy(offset => offset).ToArray()))
            .ToArray();

        private void NormalizeHavokArrayHeaders(uint dataOffset, uint typeIndex, uint elementCount, int byteLength)
        {
            NativeBphclTypeLayout layout;
            try
            {
                layout = NativeBphclReflectionCatalog.GetTypeLayoutIncludingBaseTypes(typeIndex);
            }
            catch (InvalidDataException)
            {
                return;
            }

            if (layout.Size == 0 || byteLength <= 0)
                return;

            var allocationEnd = checked(dataOffset + (uint)byteLength);
            for (uint elementIndex = 0; elementIndex < elementCount; elementIndex++)
            {
                var elementOffset = checked(dataOffset + elementIndex * layout.Size);
                if (elementOffset >= allocationEnd)
                    break;

                var elementEnd = Math.Min(allocationEnd, checked(elementOffset + layout.Size));
                NormalizeHavokArrayHeadersAt(
                    elementOffset,
                    typeIndex,
                    elementEnd,
                    new HashSet<(uint Offset, uint TypeIndex)>());
            }
        }

        private void NormalizeHavokArrayHeadersAt(
            uint dataOffset,
            uint typeIndex,
            uint objectEnd,
            ISet<(uint Offset, uint TypeIndex)> visited)
        {
            if (!visited.Add((dataOffset, typeIndex)))
                return;

            NativeBphclTypeLayout layout;
            try
            {
                layout = NativeBphclReflectionCatalog.GetTypeLayoutIncludingBaseTypes(typeIndex);
            }
            catch (InvalidDataException)
            {
                return;
            }

            foreach (var member in layout.Members)
            {
                var fieldOffset = checked(dataOffset + member.Offset);
                if (fieldOffset >= objectEnd)
                    continue;

                if (member.TypeName == "hkArray")
                {
                    if ((ulong)fieldOffset + 16UL > objectEnd)
                        continue;

                    WriteUInt32(fieldOffset + 8, 0);
                    WriteUInt32(fieldOffset + 12, 0);
                    continue;
                }

                var memberSize = NativeBphclReflectionCatalog.GetTypeSize(member.TypeIndex);
                if (memberSize == 0 || (ulong)fieldOffset + memberSize > objectEnd)
                    continue;

                // Value-type members such as hclBoneSpaceDeformer contain
                // their own hkArray headers. Follow those records as well;
                // pointer/reference members have no reflected child fields.
                NormalizeHavokArrayHeadersAt(fieldOffset, member.TypeIndex, fieldOffset + memberSize, visited);
            }
        }

        private Allocation Allocate(uint typeIndex, uint count, int byteLength, int alignment, uint itemCategory)
        {
            if (byteLength < 0)
                throw new ArgumentOutOfRangeException(nameof(byteLength));
            Align(alignment);
            var offset = checked((uint)_data.Count);

            // A zero-count hkArray still has an ITEM identity when another
            // reflected object points at it. Reserving one inert byte keeps
            // that ITEM from sharing its DATA address with the next object.
            // Shared addresses turn a harmless empty array into an invalid
            // runtime pointer graph once the game follows its fixups.
            var storageLength = count == 0 && byteLength == 0 ? 1 : byteLength;
            _data.AddRange(new byte[storageLength]);
            var itemIndex = _items.Count;
            _items.Add(new NativeBphclItem(itemCategory | typeIndex, typeIndex, offset, count));
            _allocations.Add(new AllocationInfo(itemIndex, typeIndex, offset, storageLength));
            return new Allocation(itemIndex, offset, storageLength, this);
        }

        private uint GetReferencePatchType(uint dataOffset)
        {
            // hkRootLevelContainer lives in the fixed DATA header rather than
            // as an ITEM allocation. Its first field is the named-variant hkArray.
            if (dataOffset == 0)
                return StandaloneTypeCatalog.Array;

            foreach (var allocation in _allocations)
            {
                if (dataOffset < allocation.Offset || dataOffset >= allocation.Offset + (uint)allocation.ByteLength)
                    continue;

                // An hkRefPtr array is itself the relocation type. Its
                // embedded pointer member is T*, but native PTCH tags each
                // array entry as hkRefPtr so Havok retains ownership semantics.
                if (string.Equals(
                        NativeBphclReflectionCatalog.GetTypeName(allocation.TypeIndex),
                        "hkRefPtr",
                        StringComparison.Ordinal))
                {
                    return allocation.TypeIndex;
                }

                var typeSize = NativeBphclReflectionCatalog.GetTypeSize(allocation.TypeIndex);
                if (typeSize != 0)
                {
                    var memberOffset = (dataOffset - allocation.Offset) % typeSize;
                    var memberType = NativeBphclReflectionCatalog.GetMemberTypeIndex(allocation.TypeIndex, memberOffset);
                    if (memberType.HasValue)
                        return memberType.Value;
                }

                // Arrays of raw pointer entries have no reflected member at
                // their element offset. Their ITEM type is the relocation type.
                return allocation.TypeIndex;
            }

            throw new InvalidDataException($"BPHCL pointer at DATA+0x{dataOffset:X} is outside every ITEM allocation.");
        }

        private void PromoteReferenceArrayStorage(int itemIndex, uint fieldTypeIndex)
        {
            if (_items[itemIndex].TypeIndex != StandaloneTypeCatalog.Pointer)
                return;

            var elementType = NativeBphclReflectionCatalog.GetArrayElementType(fieldTypeIndex);
            if (!elementType.HasValue)
                return;

            var allocation = _allocations.FirstOrDefault(candidate => candidate.ItemIndex == itemIndex);
            if (allocation is null)
                return;

            allocation.TypeIndex = elementType.Value;
            _items[itemIndex] = _items[itemIndex] with
            {
                Flags = 0x2000_0000u | elementType.Value,
                TypeIndex = elementType.Value
            };
        }

        private void Align(int alignment)
        {
            var aligned = (_data.Count + alignment - 1) & -alignment;
            if (aligned > _data.Count)
                _data.AddRange(new byte[aligned - _data.Count]);
        }

        private Span<byte> GetSpan(uint offset, int length)
        {
            var start = checked((int)offset);
            if (start < 0 || start > _data.Count - length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            return CollectionsMarshal.AsSpan(_data).Slice(start, length);
        }

        private sealed class AllocationInfo
        {
            public AllocationInfo(int itemIndex, uint typeIndex, uint offset, int byteLength)
            {
                ItemIndex = itemIndex;
                TypeIndex = typeIndex;
                Offset = offset;
                ByteLength = byteLength;
            }

            public int ItemIndex { get; }
            public uint TypeIndex { get; set; }
            public uint Offset { get; }
            public int ByteLength { get; }
        }

        public sealed record Reservation(int ItemIndex, uint TypeIndex);

        public sealed class Allocation
        {
            private readonly DataBuilder _owner;

            internal Allocation(int itemIndex, uint dataOffset, int byteLength, DataBuilder owner)
            {
                ItemIndex = itemIndex;
                DataOffset = dataOffset;
                ByteLength = byteLength;
                _owner = owner;
            }

            public int ItemIndex { get; }
            public uint DataOffset { get; }
            public int ByteLength { get; }
            public uint Count => _owner._items[ItemIndex].Count;

            public void SetByte(int relativeOffset, byte value)
            {
                if ((uint)relativeOffset >= (uint)ByteLength)
                    throw new ArgumentOutOfRangeException(nameof(relativeOffset));
                _owner.GetSpan(DataOffset + checked((uint)relativeOffset), 1)[0] = value;
            }
        }
    }

    // The native reflection catalog is packaged with PhysicsTool. It is TYPE
    // metadata only, so standalone export has no runtime donor-file path.
    private static class StandaloneTypeCatalog
    {
        public static uint Pointer => NativeBphclReflectionCatalog.GetTypeIndex("T*");
        public static uint RefPtr => NativeBphclReflectionCatalog.GetTypeIndex("hkRefPtr");
        public static uint Array => NativeBphclReflectionCatalog.GetTypeIndex("hkArray");
        public static uint Char => NativeBphclReflectionCatalog.GetTypeIndex("char");
        public static uint NamedVariant => NativeBphclReflectionCatalog.GetTypeIndex("hkRootLevelContainer::NamedVariant");
        public static uint ClothContainer => NativeBphclReflectionCatalog.GetTypeIndex("hclClothContainer");
        public static uint ClothData => NativeBphclReflectionCatalog.GetTypeIndex("hclClothData");
        public static uint SimClothData => NativeBphclReflectionCatalog.GetTypeIndex("hclSimClothData");
        public static uint SimClothPose => NativeBphclReflectionCatalog.GetTypeIndex("hclSimClothPose");
        public static uint AnimationContainer => NativeBphclReflectionCatalog.GetTypeIndex("hkaAnimationContainer");
        public static uint Skeleton => NativeBphclReflectionCatalog.GetTypeIndex("hkaSkeleton");
        public static uint Bone => NativeBphclReflectionCatalog.GetTypeIndex("hkaBone");
        public static uint QsTransform => NativeBphclReflectionCatalog.GetTypeIndex("hkQsTransform");
        public static uint Collidable => NativeBphclReflectionCatalog.GetTypeIndex("hclCollidable");
        public static uint CapsuleShape => NativeBphclReflectionCatalog.GetTypeIndex("hclCapsuleShape");
        public static uint SphereShape => NativeBphclReflectionCatalog.GetTypeIndex("hclSphereShape");
        public static uint PlaneShape => NativeBphclReflectionCatalog.GetTypeIndex("hclPlaneShape");
        public static uint ParticleData => NativeBphclReflectionCatalog.GetTypeIndex("hclSimClothData::ParticleData");
        public static uint Int16 => NativeBphclReflectionCatalog.GetTypeIndex("hkInt16");
        public static uint UInt16 => NativeBphclReflectionCatalog.GetTypeIndex("hkUint16");
        public static uint UInt8 => NativeBphclReflectionCatalog.GetTypeIndex("hkUint8");
        public static uint UInt32 => NativeBphclReflectionCatalog.GetTypeIndex("hkUint32");
        public static uint Vector4 => NativeBphclReflectionCatalog.GetTypeIndex("hkVector4");
        public static uint Matrix4 => NativeBphclReflectionCatalog.GetTypeIndex("hkMatrix4");

        public static byte[] CreateSection() => NativeBphclReflectionCatalog.TypeSection;
    }
}


