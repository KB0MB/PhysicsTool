using System.Numerics;

namespace HKCLTool;

// This is the native-writer input, not a UI model and not a BPHCL template.
// It intentionally captures every cloth-local value that can be read from an
// HKCL before the TAG0 DATA/ITEM/PTCH serializer is introduced.
public sealed record StandaloneBphclDocumentGraph(
    IReadOnlyList<StandaloneBphclClothGraph> Cloths,
    IReadOnlyList<StandaloneBphclCollider> OuterColliders,
    bool PreservesNativeBphclSchedule = false,
    byte[]? NativeAampPayload = null,
    byte[]? NativeTypeSection = null,
    string? SourcePath = null)
{
    public void Validate()
    {
        if (Cloths.Count == 0)
            throw new InvalidDataException("The HKCL does not contain any cloth units.");

        var outerColliderIndices = OuterColliders.Select(collider => collider.Index).ToHashSet();
        if (outerColliderIndices.Count != OuterColliders.Count)
            throw new InvalidDataException("The outer BPHCL collider table has duplicate indices.");

        foreach (var cloth in Cloths)
        {
            cloth.Validate();
            foreach (var collider in cloth.Colliders)
            {
                if (!outerColliderIndices.Contains(collider.Index))
                {
                    throw new InvalidDataException(
                        $"Cloth '{cloth.Name}' references collider {collider.Index}, which is absent from the outer collider table.");
                }
            }
        }
    }
}

public sealed record StandaloneBphclClothGraph(
    int Index,
    string Name,
    string SkeletonName,
    StandaloneBphclSimulationSettings Settings,
    IReadOnlyList<StandaloneBphclParticle> Particles,
    IReadOnlyList<StandaloneBphclBone> Bones,
    IReadOnlyList<StandaloneBphclTriangle> Triangles,
    IReadOnlyList<byte> TriangleFlips,
    IReadOnlyList<StandaloneBphclConstraintSetMetadata> ConstraintSets,
    IReadOnlyList<StandaloneBphclConstraint> Constraints,
    IReadOnlyList<StandaloneBphclLocalRange> LocalRanges,
    IReadOnlyList<StandaloneBphclTransitionConstraintSet> TransitionSets,
    IReadOnlyList<StandaloneBphclBonePlaneConstraintSet> BonePlaneSets,
    ClothRuntimeData Runtime,
    NativeBphclSimulationRuntime SimulationRuntime,
    IReadOnlyList<SkinOperatorData> SkinOperators,
    IReadOnlyList<StandaloneBphclSimpleMeshBoneDeformer> BoneDeformers,
    IReadOnlyList<StandaloneBphclCollider> Colliders,
    IReadOnlyList<StandaloneBphclColliderBinding> ColliderBindings,
    IReadOnlyList<StandaloneBphclBoneBinding> BoneBindings,
    IReadOnlyList<VolumeConstraintSetData> VolumeSets)
{
    // Keep solver semantics independently of the regenerated buffer/operator layout.
    public ClothSimulateConfigData? GeneratedSimulateConfig { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new InvalidDataException($"Cloth {Index} has no name.");
        if (Particles.Count == 0)
            throw new InvalidDataException($"Cloth '{Name}' has no simulation particles.");
        if (Bones.Count == 0)
            throw new InvalidDataException($"Cloth '{Name}' has no cloth skeleton.");

        RequireContiguous(Particles.Select(particle => particle.Index), "particle");
        RequireContiguous(Bones.Select(bone => bone.Index), "bone");
        foreach (var particle in Particles)
        {
            if (!float.IsFinite(particle.Position.X) || !float.IsFinite(particle.Position.Y) || !float.IsFinite(particle.Position.Z) ||
                !float.IsFinite(particle.Mass) || !float.IsFinite(particle.InverseMass) ||
                particle.Mass < 0 || particle.InverseMass < 0 ||
                !float.IsFinite(particle.Radius) || !float.IsFinite(particle.Friction))
            {
                throw new InvalidDataException($"Cloth '{Name}' particle {particle.Index} has a non-finite value.");
            }

            if (particle.Fixed && (particle.Mass != 0.0f || particle.InverseMass != 0.0f))
                throw new InvalidDataException($"Cloth '{Name}' fixed particle {particle.Index} must have zero mass and inverse mass.");
        }

        foreach (var triangle in Triangles)
        {
            EnsureParticle(triangle.ParticleA, "triangle");
            EnsureParticle(triangle.ParticleB, "triangle");
            EnsureParticle(triangle.ParticleC, "triangle");
        }
        if (TriangleFlips.Count != 0 && TriangleFlips.Count != Triangles.Count)
            throw new InvalidDataException($"Cloth '{Name}' has {TriangleFlips.Count} triangle flips for {Triangles.Count} triangles.");
        var constraintSetIndices = ConstraintSets.Select(set => set.Index).ToHashSet();
        if (constraintSetIndices.Count != ConstraintSets.Count)
            throw new InvalidDataException($"Cloth '{Name}' has duplicate constraint-set indices.");
        foreach (var constraint in Constraints)
        {
            EnsureConstraintSet(constraint.ConstraintSetIndex, "link constraint");
            EnsureParticle(constraint.ParticleA, "constraint");
            EnsureParticle(constraint.ParticleB, "constraint");
        }
        foreach (var range in LocalRanges)
        {
            EnsureConstraintSet(range.ConstraintSetIndex, "local range");
            EnsureParticle(range.ParticleIndex, "local range");
        }
        foreach (var transitionSet in TransitionSets)
        {
            EnsureConstraintSet(transitionSet.ConstraintSetIndex, "transition constraint");
            foreach (var particle in transitionSet.Particles)
            {
                EnsureParticle(particle.ParticleIndex, "transition constraint");
                if (!float.IsFinite(particle.ToAnimationDelay) ||
                    !float.IsFinite(particle.ToSimulationDelay) ||
                    !float.IsFinite(particle.ToSimulationMaximumDistance))
                {
                    throw new InvalidDataException($"Cloth '{Name}' transition particle {particle.ParticleIndex} has a non-finite value.");
                }
            }
        }
        foreach (var bonePlaneSet in BonePlaneSets)
        {
            EnsureConstraintSet(bonePlaneSet.ConstraintSetIndex, "bone plane constraint");
            foreach (var plane in bonePlaneSet.Planes)
            {
                EnsureParticle(plane.ParticleIndex, "bone plane constraint");
                if (!float.IsFinite(plane.PlaneEquationBone.X) ||
                    !float.IsFinite(plane.PlaneEquationBone.Y) ||
                    !float.IsFinite(plane.PlaneEquationBone.Z) ||
                    !float.IsFinite(plane.PlaneEquationBone.W) ||
                    !float.IsFinite(plane.Stiffness))
                {
                    throw new InvalidDataException($"Cloth '{Name}' bone plane {plane.Index} has a non-finite value.");
                }
            }
        }
        foreach (var volumeSet in VolumeSets)
        {
            EnsureConstraintSet(volumeSet.ConstraintSetIndex, "volume constraint");
            foreach (var frame in volumeSet.Frames)
            {
                EnsureParticle(frame.ParticleIndex, "volume frame");
                if (!float.IsFinite(frame.FrameVector.X) ||
                    !float.IsFinite(frame.FrameVector.Y) ||
                    !float.IsFinite(frame.FrameVector.Z) ||
                    !float.IsFinite(frame.FrameVector.W) ||
                    !float.IsFinite(frame.Weight))
                    throw new InvalidDataException($"Cloth '{Name}' volume frame {frame.Index} has a non-finite value.");
            }
            foreach (var apply in volumeSet.Applies)
            {
                EnsureParticle(apply.ParticleIndex, "volume apply");
                if (!float.IsFinite(apply.FrameVector.X) ||
                    !float.IsFinite(apply.FrameVector.Y) ||
                    !float.IsFinite(apply.FrameVector.Z) ||
                    !float.IsFinite(apply.FrameVector.W) ||
                    !float.IsFinite(apply.Stiffness))
                    throw new InvalidDataException($"Cloth '{Name}' volume apply {apply.Index} has a non-finite value.");
            }
        }
        foreach (var deformer in BoneDeformers)
        {
            var transformCount = Runtime.TransformSets.Count == 1
                ? Runtime.TransformSets[0].TransformCount
                : Bones.Count;
            foreach (var pair in deformer.TriangleBonePairs)
            {
                var boneIndex = pair.BoneOffset / 64;
                var triangleIndex = pair.TriangleOffset / 6;
                if ((uint)boneIndex >= (uint)transformCount)
                    throw new InvalidDataException($"Cloth '{Name}' bone deformer references transform {boneIndex} outside its transform set.");
                if ((uint)triangleIndex >= (uint)Triangles.Count)
                    throw new InvalidDataException($"Cloth '{Name}' bone deformer references triangle {triangleIndex} outside the simulation.");
            }
            if (deformer.LocalBoneTransforms.Count != 0 &&
                deformer.LocalBoneTransforms.Count != deformer.TriangleBonePairs.Count)
            {
                throw new InvalidDataException($"Cloth '{Name}' bone deformer has unaligned local transforms.");
            }
        }
        foreach (var skin in SkinOperators)
        {
            if (skin.OutputBufferIndex < 0 || skin.TransformSetIndex < 0)
                throw new InvalidDataException($"Cloth '{Name}' skin operator has an invalid buffer or transform-set index.");
            var transformCount = Runtime.TransformSets.ElementAtOrDefault(skin.TransformSetIndex)?.TransformCount ?? Bones.Count;
            if (skin.TransformSubset.Any(index => index >= transformCount))
                throw new InvalidDataException($"Cloth '{Name}' skin operator references a transform outside its transform set.");
            // Native bone-space blocks store only packed vertex and bone
            // indices. Only object-space blocks carry explicit weight bytes.
            var skinHasExplicitWeights = string.Equals(skin.ClassName, "hclObjectSpaceSkinPOperator", StringComparison.Ordinal);
            foreach (var blendArray in skin.BlendArrays)
            {
                foreach (var block in blendArray.Blocks)
                {
                    if (block.VertexIndices.Count != blendArray.VertexCount ||
                        block.BoneIndices.Count != blendArray.BoneCount ||
                        (skinHasExplicitWeights && blendArray.BlendCount > 1 && block.BoneWeights.Count != blendArray.BoneCount) ||
                        ((!skinHasExplicitWeights || blendArray.BlendCount == 1) && block.BoneWeights.Count != 0))
                    {
                        throw new InvalidDataException($"Cloth '{Name}' skin blend block '{blendArray.MemberName}' has an invalid packed shape.");
                    }
                }
            }
            ValidatePositionBlocks(skin.LocalPositionBlocks, "local position");
            ValidatePositionBlocks(skin.LocalUnpackedPositionBlocks, "unpacked local position");
        }
        if (Runtime.Buffers.Count > 0)
        {
            RequireContiguous(Runtime.Buffers.Select(buffer => buffer.Index), "buffer");
            if (Runtime.TransformSets.Count > 1)
                throw new InvalidDataException($"Cloth '{Name}' uses multiple transform sets, which standalone BPHCL conversion does not yet support.");
            if (Runtime.TransformSets.Count == 1 && Runtime.TransformSets[0].TransformCount < Bones.Count)
                throw new InvalidDataException($"Cloth '{Name}' source transform-set capacity is smaller than its skeleton.");
            RequireContiguous(Runtime.Operators.Select(@operator => @operator.Index), "operator");
            foreach (var state in Runtime.States)
            {
                if (state.OperatorIndices.Any(index => index < 0 || index >= Runtime.Operators.Count))
                    throw new InvalidDataException($"Cloth '{Name}' state '{state.Name}' references an invalid operator.");
                if (state.BufferIndices.Any(index => index < 0 || index >= Runtime.Buffers.Count))
                    throw new InvalidDataException($"Cloth '{Name}' state '{state.Name}' references an invalid buffer.");
            }

            ValidateScratchVertexRouting();
        }
        var colliderIndices = Colliders.Select(collider => collider.Index).ToHashSet();
        foreach (var binding in ColliderBindings)
        {
            if (!colliderIndices.Contains(binding.ColliderIndex))
                throw new InvalidDataException($"Cloth '{Name}' collider binding references missing collider {binding.ColliderIndex}.");
            if (binding.BoneIndex < 0 || binding.BoneIndex >= Bones.Count)
                throw new InvalidDataException($"Cloth '{Name}' collider binding references bone {binding.BoneIndex} outside its skeleton.");
        }

        foreach (var binding in BoneBindings)
        {
            var transformCount = Runtime.TransformSets.Count == 1
                ? Runtime.TransformSets[0].TransformCount
                : Bones.Count;
            if (binding.BoneIndex < 0 || binding.BoneIndex >= transformCount)
                throw new InvalidDataException($"Cloth '{Name}' bone binding references transform {binding.BoneIndex} outside its transform set.");
            EnsureParticle(binding.ParticleA, "bone binding");
            EnsureParticle(binding.ParticleB, "bone binding");
            EnsureParticle(binding.ParticleC, "bone binding");
        }

        return;

        void ValidateScratchVertexRouting()
        {
            var gatherOperators = Runtime.Operators
                .Where(@operator => string.Equals(
                    @operator.ClassName,
                    "hclGatherAllVerticesOperator",
                    StringComparison.Ordinal) &&
                    @operator.VertexInputFromVertexOutput.Count > 0)
                .ToArray();
            if (gatherOperators.Length == 0)
                return;

            var mappings = gatherOperators
                .Select(@operator => @operator.VertexInputFromVertexOutput.ToArray())
                .ToArray();
            var mapping = mappings[0];
            if (mappings.Skip(1).Any(candidate => !candidate.AsSpan().SequenceEqual(mapping)))
            {
                throw new InvalidDataException(
                    $"Cloth '{Name}' has gather operators with different particle-to-reference mappings.");
            }

            if (mapping.Length != Particles.Count)
            {
                throw new InvalidDataException(
                    $"Cloth '{Name}' gathers {mapping.Length} reference vertices for {Particles.Count} particles.");
            }

            var inputBuffers = gatherOperators
                .Where(@operator => @operator.InputBufferIndex is not null)
                .Select(@operator => @operator.InputBufferIndex!.Value)
                .ToHashSet();
            foreach (var inputBuffer in inputBuffers)
            {
                var vertexCount = Runtime.Buffers.ElementAtOrDefault(inputBuffer)?.VertexCount;
                if (vertexCount is not null && mapping.Any(reference => reference < 0 || reference >= vertexCount.Value))
                {
                    throw new InvalidDataException(
                        $"Cloth '{Name}' gathers a reference vertex outside buffer {inputBuffer}.");
                }
            }

            foreach (var range in LocalRanges.Where(range =>
                         inputBuffers.Contains(checked((int)range.ReferenceMeshBufferIndex))))
            {
                RequireReference(range.ParticleIndex, range.ReferenceVertex, "local-range constraint");
            }

            foreach (var transitionSet in TransitionSets.Where(set =>
                         inputBuffers.Contains(checked((int)set.ReferenceMeshBufferIndex))))
            {
                foreach (var particle in transitionSet.Particles)
                    RequireReference(particle.ParticleIndex, particle.ReferenceVertex, "transition constraint");
            }

            foreach (var moveOperator in Runtime.Operators.Where(@operator => string.Equals(
                         @operator.ClassName,
                         "hclMoveParticlesOperator",
                         StringComparison.Ordinal)))
            {
                foreach (var pair in moveOperator.VertexParticlePairs)
                    RequireReference(pair.ParticleIndex, pair.VertexIndex, "fixed-particle move");
            }

            return;

            void RequireReference(int particleIndex, int referenceVertex, string owner)
            {
                if ((uint)particleIndex >= (uint)mapping.Length || mapping[particleIndex] != referenceVertex)
                {
                    var expected = (uint)particleIndex < (uint)mapping.Length ? mapping[particleIndex] : -1;
                    throw new InvalidDataException(
                        $"Cloth '{Name}' {owner} maps particle {particleIndex} to reference vertex {referenceVertex}; " +
                        $"the gather route requires {expected}.");
                }
            }
        }

        void EnsureParticle(int index, string owner)
        {
            if ((uint)index >= (uint)Particles.Count)
                throw new InvalidDataException($"Cloth '{Name}' {owner} references particle {index} outside the simulation.");
        }

        void EnsureConstraintSet(int index, string owner)
        {
            if (!constraintSetIndices.Contains(index))
                throw new InvalidDataException($"Cloth '{Name}' {owner} references missing constraint set {index}.");
        }

        void ValidatePositionBlocks(IReadOnlyList<SkinPositionBlockData> blocks, string owner)
        {
            foreach (var block in blocks)
            {
                // Native object-space BPHCL stores this block as 64 packed
                // int16 values. It intentionally has no unpacked Vector4
                // representation, and the writer preserves those bytes.
                if (block.NativePackedValues is { Count: 64 })
                    continue;

                if (block.Positions.Count != 16 || block.Positions.Any(position =>
                        !float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
                        !float.IsFinite(position.Z) || !float.IsFinite(position.W)))
                {
                    throw new InvalidDataException($"Cloth '{Name}' has an invalid {owner} block.");
                }
            }
        }
    }

    private static void RequireContiguous(IEnumerable<int> values, string itemName)
    {
        var expected = 0;
        foreach (var value in values.OrderBy(value => value))
        {
            if (value != expected++)
                throw new InvalidDataException($"The {itemName} indices are not contiguous from zero.");
        }
    }
}

public sealed record StandaloneBphclSimulationSettings(
    Vector3 Gravity,
    float DampingPerSecond,
    float CollisionTolerance,
    bool TransferTranslationMotion,
    float MinTranslationSpeed,
    float MaxTranslationSpeed,
    float MinTranslationBlend,
    float MaxTranslationBlend,
    bool TransferRotationMotion,
    float MinRotationSpeed,
    float MaxRotationSpeed,
    float MinRotationBlend,
    float MaxRotationBlend);

public sealed record StandaloneBphclParticle(
    int Index,
    Vector4 Position,
    bool Fixed,
    float Mass,
    float InverseMass,
    float Radius,
    float Friction,
    uint CollisionMask);

// hkQsTransform is three complete Vector4 rows. The W values are not display
// scale/translation, but native BPHCL retains them and expects them at runtime.
public sealed record StandaloneBphclBone(
    int Index,
    string Name,
    int ParentIndex,
    Vector4 Translation,
    Vector4 Rotation,
    Vector4 Scale);

public sealed record StandaloneBphclTriangle(int ParticleA, int ParticleB, int ParticleC);

public sealed record StandaloneBphclConstraintSetMetadata(
    int Index,
    string Name,
    string TypeName,
    uint ConstraintId,
    uint ConstraintType,
    bool? ClampBendStiffness = null,
    float? MaxRestPoseHeightSq = null,
    bool? UseRestPoseConfig = null);

public sealed record StandaloneBphclConstraint(
    int ConstraintSetIndex,
    int LinkIndex,
    string TypeName,
    int ParticleA,
    int ParticleB,
    float? RestLength,
    float? BendMinLength,
    float? StretchMaxLength,
    float? Stiffness,
    float? BendStiffness,
    float? StretchStiffness,
    int? ParticleC = null,
    int? ParticleD = null,
    float? RestCurvature = null,
    float? WeightA = null,
    float? WeightB = null,
    float? WeightC = null,
    float? WeightD = null,
    float? CompressionLength = null);

public sealed record StandaloneBphclLocalRange(
    int ConstraintSetIndex,
    int LocalConstraintIndex,
    string TypeName,
    int ParticleIndex,
    int ReferenceVertex,
    float ShapeRadius,
    float MaxNormalDistance,
    float MinNormalDistance,
    uint ReferenceMeshBufferIndex,
    float Stiffness,
    int ShapeType,
    bool ApplyNormalComponent,
    string RecordTypeName = "hclLocalRangeConstraintSet::LocalConstraint",
    float? RecordStiffness = null);

public sealed record StandaloneBphclTransitionConstraintSet(
    int ConstraintSetIndex,
    string Name,
    uint ConstraintId,
    uint ConstraintType,
    float ToAnimationPeriod,
    float ToAnimationPlusDelayPeriod,
    float ToSimulationPeriod,
    float ToSimulationPlusDelayPeriod,
    uint ReferenceMeshBufferIndex,
    IReadOnlyList<StandaloneBphclTransitionParticle> Particles);

public sealed record StandaloneBphclTransitionParticle(
    int Index,
    int ParticleIndex,
    int ReferenceVertex,
    float ToAnimationDelay,
    float ToSimulationDelay,
    float ToSimulationMaximumDistance);

public sealed record StandaloneBphclBonePlaneConstraintSet(
    int ConstraintSetIndex,
    string Name,
    uint ConstraintId,
    uint ConstraintType,
    uint TransformSetIndex,
    IReadOnlyList<StandaloneBphclBonePlane> Planes);

public sealed record StandaloneBphclBonePlane(
    int Index,
    Vector4 PlaneEquationBone,
    int ParticleIndex,
    int TransformIndex,
    float Stiffness);

public sealed record StandaloneBphclSimpleMeshBoneDeformer(
    int OperatorIndex,
    int InputBufferIndex,
    int OutputTransformSetIndex,
    int BoneAxis,
    IReadOnlyList<SimpleMeshTriangleBonePairData> TriangleBonePairs,
    IReadOnlyList<Matrix4x4> LocalBoneTransforms);

public sealed record StandaloneBphclCollider(
    int Index,
    string Name,
    string ShapeType,
    int BoneIndex,
    string BoneName,
    Vector3 Start,
    Vector3 End,
    float Radius,
    Vector3 PlaneNormal,
    Matrix4x4 Transform,
    Vector4? NativeAxisX = null,
    Vector4? NativeAxisY = null,
    Vector4? NativeAxisZ = null,
    Vector4? NativeTranslation = null,
    Vector4? NativeStart = null,
    Vector4? NativeEnd = null,
    Vector4? NativePlaneEquation = null,
    int ShapeKind = 0,
    float PinchDetectionRadius = 0.0f,
    sbyte PinchDetectionPriority = 0,
    bool PinchDetectionEnabled = false,
    bool VirtualCollisionPointCollisionEnabled = false,
    bool Enabled = true,
    Vector4? NativeDirection = null,
    float? NativeCapsuleLengthSquaredInverse = null);

// Collider objects are globally shared. This binding belongs to one cloth
// simulation and preserves its authored bone-relative basis separately.
public sealed record StandaloneBphclColliderBinding(
    int ColliderIndex,
    int BoneIndex,
    Matrix4x4 Offset);

public sealed record StandaloneBphclBoneBinding(int BoneIndex, int ParticleA, int ParticleB, int ParticleC);

public static class HkclToBphclStandaloneGraphBuilder
{
    public static StandaloneBphclDocumentGraph Build(HkclService source) =>
        BuildCore(source, preserveNativeBphclValues: false);

    // Diagnostic-only control path. It proves whether the standalone writer
    // can reproduce a native BPHCL graph without HKCL conversion heuristics.
    public static StandaloneBphclDocumentGraph BuildNativeReconstruction(HkclService source) =>
        BuildCore(source, preserveNativeBphclValues: true);

    private static StandaloneBphclDocumentGraph BuildCore(HkclService source, bool preserveNativeBphclValues)
    {
        if (source.IsBphhb || !source.HasDocument ||
            (source.IsBphcl && !preserveNativeBphclValues))
        {
            throw new InvalidOperationException("Standalone BPHCL conversion requires a loaded normal HKCL file.");
        }

        if (preserveNativeBphclValues && !source.IsBphcl)
            throw new InvalidOperationException("Native BPHCL reconstruction requires a loaded BPHCL file.");

        var cloths = new List<StandaloneBphclClothGraph>();
        for (var clothIndex = 0; clothIndex < source.GetClothSummaries().Count; clothIndex++)
            cloths.Add(BuildCloth(source, clothIndex, preserveNativeBphclValues));


        var referencedByIndex = cloths
            .SelectMany(cloth => cloth.Colliders)
            .GroupBy(collider => collider.Index)
            .ToDictionary(group => group.Key, group => group.First());
        // Native BPHCL retains a complete root collider registry. The cloth-local
        // perInstanceCollidables lists below remain the sole collision selection.
        var nativeOuterColliders = preserveNativeBphclValues
            ? source.CaptureBphclNativeDocument()?.Colliders.ToDictionary(collider => collider.Index)
                ?? new Dictionary<int, NativeBphclCollider>()
            : new Dictionary<int, NativeBphclCollider>();
        var outerColliders = source.GetAllColliderRows()
            .OrderBy(collider => collider.Index)
            .Select(collider =>
            {
                if (referencedByIndex.TryGetValue(collider.Index, out var referenced))
                    return referenced;

                nativeOuterColliders.TryGetValue(collider.Index, out var native);
                return CreateStandaloneCollider(collider, native);
            })
            .ToArray();

        var graph = new StandaloneBphclDocumentGraph(
            cloths,
            outerColliders,
            PreservesNativeBphclSchedule: preserveNativeBphclValues,
            NativeAampPayload: preserveNativeBphclValues
                ? CopyEmbeddedAamp(source.CaptureBphclNativeDocument())
                : null,
            NativeTypeSection: preserveNativeBphclValues
                ? CopyTypeSection(source.CaptureBphclNativeDocument())
                : null,
            SourcePath: source.SourcePath);
        graph.Validate();
        return graph;
    }

    // A native self-rebuild is a writer diagnostic, not a metadata conversion.
    // Retaining this block byte-for-byte keeps AAMP out of that experiment.
    private static byte[]? CopyEmbeddedAamp(NativeBphclDocument? document)
    {
        if (document is null || document.Header.ParameterSize == 0)
            return null;

        var offset = checked((int)document.Header.ParameterOffset);
        var size = checked((int)document.Header.ParameterSize);
        if (offset < 0 || size < 4 || offset > document.Bytes.Length - size ||
            !document.Bytes.AsSpan(offset, 4).SequenceEqual("AAMP"u8))
        {
            throw new InvalidDataException("Native BPHCL has an invalid embedded AAMP range.");
        }

        return document.Bytes.AsSpan(offset, size).ToArray();
    }

    // The reflected type catalog is runtime metadata too. Keeping it verbatim
    // means a native self-rebuild isolates DATA/ITEM/PTCH from TYPE differences.
    private static byte[]? CopyTypeSection(NativeBphclDocument? document)
    {
        if (document?.TypeSection is not { } section)
            return null;

        if (section.Offset < 0 || section.Size < 8 || section.Offset > document.Bytes.Length - section.Size ||
            !document.Bytes.AsSpan(section.Offset + 4, 4).SequenceEqual("TYPE"u8))
        {
            throw new InvalidDataException("Native BPHCL has an invalid TYPE section.");
        }

        return document.Bytes.AsSpan(section.Offset, section.Size).ToArray();
    }

    private static StandaloneBphclCollider CreateStandaloneCollider(
        ColliderEditRow collider,
        NativeBphclCollider? native)
    {
        var transform = collider.Transform;
        var useNative = native is not null;
        var localStart = useNative
            ? new Vector3(native!.Shape.Start.X, native.Shape.Start.Y, native.Shape.Start.Z)
            : ToColliderLocalPoint(transform, new Vector3(collider.StartX, collider.StartY, collider.StartZ));
        var localEnd = useNative
            ? new Vector3(native!.Shape.End.X, native.Shape.End.Y, native.Shape.End.Z)
            : ToColliderLocalPoint(transform, new Vector3(collider.EndX, collider.EndY, collider.EndZ));
        var localPlaneNormal = useNative
            ? new Vector3(native!.Shape.PlaneEquation.X, native.Shape.PlaneEquation.Y, native.Shape.PlaneEquation.Z)
            : ToColliderLocalDirection(transform, new Vector3(collider.PlaneNormalX, collider.PlaneNormalY, collider.PlaneNormalZ));
        var planeEquation = useNative
            ? native!.Shape.PlaneEquation
            : string.Equals(collider.ShapeType, "hclPlaneShape", StringComparison.Ordinal)
                ? new Vector4(localPlaneNormal, -Vector3.Dot(localPlaneNormal, localStart))
                : (Vector4?)null;
        return new StandaloneBphclCollider(
            collider.Index,
            useNative ? native!.Name : ToBphclRuntimeColliderName(collider.Name, collider.BoneName),
            useNative ? native!.Shape.TypeName : collider.ShapeType,
            collider.BoneIndex,
            StripLinkPrefix(collider.BoneName),
            localStart,
            localEnd,
            collider.Radius,
            localPlaneNormal,
            transform,
            useNative ? native!.AxisX : null,
            useNative ? native!.AxisY : null,
            useNative ? native!.AxisZ : null,
            useNative ? native!.Translation : null,
            useNative ? native!.Shape.Start : null,
            useNative ? native!.Shape.End : null,
            planeEquation,
            useNative ? native!.Shape.Kind : 0,
            useNative ? native!.PinchDetectionRadius : 0.0f,
            useNative ? native!.PinchDetectionPriority : (sbyte)0,
            useNative && native!.PinchDetectionEnabled,
            useNative && native!.VirtualCollisionPointCollisionEnabled,
            !useNative || native!.Enabled,
            useNative ? native!.Shape.Direction : null,
            useNative ? native!.Shape.CapsuleLengthSquaredInverse : null);
    }
    private static StandaloneBphclClothGraph BuildCloth(
        HkclService source,
        int clothIndex,
        bool preserveNativeBphclValues)
    {
        var preview = source.GetParticlePreview(clothIndex);
        var settings = source.GetSimulationSettings(clothIndex);
        var sourceClothName = source.GetClothName(clothIndex);
        var targetClothName = preserveNativeBphclValues
            ? sourceClothName
            : BphclConversionProfileCatalog.GetReverseClothName(source.SourcePath, sourceClothName);
        var simulationRuntime = source.GetSimulationRuntimeData(clothIndex);
        var massScale = 1.0f;
        var constraintScale = 1.0f;
        if (!preserveNativeBphclValues)
        {
            BphclConversionProfileCatalog.TryGetReverseScales(
                source.SourcePath,
                sourceClothName,
                out massScale,
                out constraintScale);
        }

        float? MapConstraint(string constraintClass, string fieldName, float? value) =>
            preserveNativeBphclValues
                ? value
                : ReverseConstraintValue(
                    source.SourcePath,
                    sourceClothName,
                    constraintClass,
                    fieldName,
                    value,
                    constraintScale);
        var sourceParticles = source.GetParticleRows(clothIndex)
            .OrderBy(particle => particle.Index)
            .ToArray();
        var landscapeParticles = preserveNativeBphclValues
            ? new HashSet<int>()
            : SelectLandscapeParticles(sourceParticles, BphclConversionProfileCatalog.ShouldEnableReverseLandscapeCollision(source.SourcePath, sourceClothName));
        var particles = sourceParticles
            .Select(particle => new StandaloneBphclParticle(
                particle.Index,
                new Vector4(particle.X, particle.Y, particle.Z, particle.W),
                particle.Fixed,
                preserveNativeBphclValues ? particle.Mass : ToBphclMass(particle.Fixed, particle.Mass, massScale),
                preserveNativeBphclValues ? particle.InverseMass : ToBphclInverseMass(particle.Fixed, particle.Mass, massScale),
                particle.Radius,
                particle.Friction,
                preserveNativeBphclValues ? particle.CollisionMask : ApplyLandscapeCollisionMask(particle, landscapeParticles.Contains(particle.Index))))
            .ToArray();
        var bones = source.GetBoneRows(clothIndex)
            .OrderBy(bone => bone.Index)
            .Select(bone => new StandaloneBphclBone(
                bone.Index,
                preserveNativeBphclValues ? bone.Name : ToBphclRuntimeBoneName(bone.Name),
                bone.ParentIndex,
                new Vector4(bone.X, bone.Y, bone.Z, bone.TranslationW),
                new Vector4(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW),
                // Native BPHCL reference poses use a homogeneous scale W of one.
                // Preserve native BPHCL values verbatim when rebuilding them.
                new Vector4(bone.ScaleX, bone.ScaleY, bone.ScaleZ, preserveNativeBphclValues ? bone.ScaleW : 1.0f)))
            .ToArray();
        var sourceConstraintSets = source.GetConstraintSetSummaries(clothIndex);
        var bendStiffnessSets = source.GetBendStiffnessConstraintSets(clothIndex)
            .ToDictionary(set => set.ConstraintSetIndex);
        var constraintSets = sourceConstraintSets
            .Select(set =>
            {
                bendStiffnessSets.TryGetValue(set.Index, out var bendStiffness);
                return new StandaloneBphclConstraintSetMetadata(
                    set.Index,
                    set.Name,
                    set.ClassName,
                    set.ConstraintId,
                    set.ConstraintType,
                    preserveNativeBphclValues
                        ? bendStiffness?.ClampBendStiffness
                        : string.Equals(set.ClassName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal),
                    bendStiffness?.MaxRestPoseHeightSq,
                    preserveNativeBphclValues
                        ? bendStiffness?.UseRestPoseConfig
                        : string.Equals(set.ClassName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal));
            })
            .ToList();
        var constraints = new Dictionary<(int Set, int Link), StandaloneBphclConstraint>();
        var localRanges = source.GetLocalRangeConstraintSets(clothIndex)
            .SelectMany(set => set.Constraints.Select(range => new StandaloneBphclLocalRange(
                set.ConstraintSetIndex,
                range.Index,
                "hclLocalRangeConstraintSet",
                range.ParticleIndex,
                range.ReferenceVertex,
                range.ShapeRadius,
                range.MaxNormalDistance,
                range.MinNormalDistance,
                set.ReferenceMeshBufferIndex,
                set.Stiffness,
                set.ShapeType,
                set.ApplyNormalComponent,
                range.RecordTypeName,
                range.RecordStiffness)))
            .ToArray();
        var constraintTypes = sourceConstraintSets
            .ToDictionary(set => set.Index, set => set.ClassName);
        foreach (var particle in particles)
        {
            foreach (var relationship in source.GetParticleRelationships(clothIndex, particle.Index))
            {
                var constraintType = constraintTypes.GetValueOrDefault(relationship.ConstraintSetIndex, relationship.Name);
                if (relationship.LinkIndex >= 0 && relationship.ParticleA >= 0 && relationship.ParticleB >= 0 &&
                    !string.Equals(constraintType, "hclBendStiffnessConstraintSet", StringComparison.Ordinal))
                {
                    constraints.TryAdd(
                        (relationship.ConstraintSetIndex, relationship.LinkIndex),
                        new StandaloneBphclConstraint(
                            relationship.ConstraintSetIndex,
                            relationship.LinkIndex,
                            constraintType,
                            relationship.ParticleA,
                            relationship.ParticleB,
                            MapConstraint( constraintType, "restLength", relationship.RestLength),
                            MapConstraint( constraintType, "bendMinLength", relationship.BendMinLength),
                            MapConstraint( constraintType, "stretchMaxLength", relationship.StretchMaxLength),
                            MapConstraint( constraintType, "stiffness", relationship.Stiffness),
                            MapConstraint( constraintType, "bendStiffness", relationship.BendStiffness),
                            MapConstraint( constraintType, "stretchStiffness", relationship.StretchStiffness),
                            CompressionLength: MapConstraint(constraintType, "compressionLength", relationship.CompressionLength)));
                }

            }
        }

        foreach (var set in bendStiffnessSets.Values)
        {
            foreach (var link in set.Links)
            {
                constraints[(set.ConstraintSetIndex, link.LinkIndex)] = new StandaloneBphclConstraint(
                    set.ConstraintSetIndex,
                    link.LinkIndex,
                    "hclBendStiffnessConstraintSet",
                    link.ParticleA,
                    link.ParticleB,
                    null,
                    null,
                    null,
                    null,
                    MapConstraint( "hclBendStiffnessConstraintSet", "bendStiffness", link.BendStiffness),
                    null,
                    link.ParticleC,
                    link.ParticleD,
                    link.RestCurvature,
                    link.WeightA,
                    link.WeightB,
                    link.WeightC,
                    link.WeightD);
            }
        }
        if (ShouldClampLocalRangeRadius(out var localRangeRadiusLimit))
        {
            localRanges = localRanges
                .Select(range => range with { ShapeRadius = MathF.Min(range.ShapeRadius, localRangeRadiusLimit) })
                .ToArray();
        }

        var transitionSets = source.GetTransitionConstraintSets(clothIndex)
            .Select(set => new StandaloneBphclTransitionConstraintSet(
                set.ConstraintSetIndex,
                set.Name,
                set.ConstraintId,
                set.ConstraintType,
                set.ToAnimationPeriod,
                set.ToAnimationPlusDelayPeriod,
                set.ToSimulationPeriod,
                set.ToSimulationPlusDelayPeriod,
                set.ReferenceMeshBufferIndex,
                set.Particles.Select(particle => new StandaloneBphclTransitionParticle(
                    particle.Index,
                    particle.ParticleIndex,
                    particle.ReferenceVertex,
                    particle.ToAnimationDelay,
                    particle.ToSimulationDelay,
                    particle.ToSimulationMaximumDistance)).ToArray()))
            .ToArray();
        var bonePlaneSets = source.GetBonePlaneConstraintSets(clothIndex)
            .Select(set => new StandaloneBphclBonePlaneConstraintSet(
                set.ConstraintSetIndex,
                set.Name,
                set.ConstraintId,
                set.ConstraintType,
                set.TransformSetIndex,
                set.Planes.Select(plane => new StandaloneBphclBonePlane(
                    plane.Index,
                    plane.PlaneEquationBone,
                    plane.ParticleIndex,
                    plane.TransformIndex,
                    plane.Stiffness)).ToArray()))
            .ToArray();
        var skinOperators = source.GetSkinOperatorData(clothIndex);
        var colliderBindings = source.GetStandaloneColliderBindings(clothIndex);
        var runtime = source.GetClothRuntimeData(clothIndex);

        // TotK uses a separate bend-stiffness phase for the fixed-length bend
        // pattern that BotW encodes as a repeated bend-link pass. Generate it
        // only when the rest pose provides an unambiguous four-particle quad.
        if (!preserveNativeBphclValues &&
            TryCreateDerivedBendStiffnessSet(
                preview,
                runtime,
                constraintSets,
                constraints.Values,
                out var sourceBendSetIndex,
                out var derivedBendStiffness,
                out var derivedBendStiffnessLinks))
        {
            constraintSets.Add(derivedBendStiffness);
            foreach (var link in derivedBendStiffnessLinks)
                constraints[(link.ConstraintSetIndex, link.LinkIndex)] = link;

            runtime = ReplaceRepeatedBendPass(
                runtime,
                sourceBendSetIndex,
                derivedBendStiffness.Index);
        }
        // A fresh ToTK file must use the native constraint vocabulary and
        // ordinal layout. BotW-only bone-plane sets are not present in the
        // matched BPHCL corpus, so omit them here and remap every remaining
        // record and solver-program reference. Native BPHCL round trips keep
        // their original set order and data verbatim.
        if (!preserveNativeBphclValues)
        {
            (constraintSets, constraints, localRanges, transitionSets, bonePlaneSets, runtime) =
                CanonicalizeFreshConstraintProgram(
                    constraintSets,
                    constraints,
                    localRanges,
                    transitionSets,
                    bonePlaneSets,
                    runtime);
        }

        // Native BPHCL round trips retain zero-valued set headers exactly.
        // Fresh HKCL-to-BPHCL output, however, may arrive with an existing
        // bend-stiffness set but no set-level rest-pose limit. Rebuild that
        // derived limit from the four-particle links so the solver has the
        // same guard value as native BPHCL.
        if (!preserveNativeBphclValues)
            RecoverMissingBendStiffnessRestPoseLimits(preview, constraintSets, constraints.Values);

        if (ShouldDropLocalRangeSet())
            (constraintSets, constraints, localRanges, runtime) = DropLocalRangeSets(constraintSets, constraints, localRanges, runtime);
        else if (ShouldDropLocalRangeExecution())
            runtime = DropConstraintExecution(runtime, constraintSets, "hclLocalRangeConstraintSet");

        if (ShouldDropBendStiffnessSet())
            (constraintSets, constraints, runtime) = DropBendStiffnessSets(constraintSets, constraints, runtime);
        else if (ShouldDropBendStiffnessExecution())
            runtime = DropBendStiffnessExecution(runtime, constraintSets);

        if (ShouldDropObjectSkinExecution())
            runtime = DropRuntimeStateOperatorClassExecution(runtime, "hclObjectSpaceSkinPOperator", "hclBoneSpaceSkinPOperator");
        if (ShouldDropMoveParticlesExecution())
            runtime = DropRuntimeStateOperatorClassExecution(runtime, "hclMoveParticlesOperator");
        if (ShouldDropSimulateExecution())
            runtime = DropRuntimeStateOperatorClassExecution(runtime, "hclSimulateOperator");
        if (ShouldDropStateSimulationReferences())
            runtime = DropRuntimeStateSimulationReferences(runtime);

        var nativeColliders = preserveNativeBphclValues
            ? source.CaptureBphclNativeDocument()?.Colliders.ToDictionary(collider => collider.Index)
                ?? new Dictionary<int, NativeBphclCollider>()
            : new Dictionary<int, NativeBphclCollider>();
        var sourceColliders = source.GetColliderRows(clothIndex)
            .OrderBy(collider => collider.Index)
            .Select(collider =>
            {
                nativeColliders.TryGetValue(collider.Index, out var native);
                return CreateStandaloneCollider(collider, native);
            })
            .ToArray();
        var sourceCollidersByIndex = sourceColliders.ToDictionary(collider => collider.Index);

        // A fresh BPHCL gives outer colliders stable global ITEM indices. Keep
        // every cloth-local array aligned with that order: collision-mask bits,
        // pinch data, and explicit simulate-operator selections all index the
        // per-instance collider slots rather than collider names.
        if (false && !preserveNativeBphclValues && colliderBindings.Count > 1)
        {
            (particles, colliderBindings, runtime, simulationRuntime) = NormalizeFreshColliderSlots(
                particles,
                sourceCollidersByIndex,
                colliderBindings,
                runtime,
                simulationRuntime);
        }

        // Collision masks, pinch records, references, transform indices, and
        // offsets are all addressed by the per-instance collider slot. HKCL's
        // object enumeration can differ from collidableTransformMap. Preserve
        // the source transform-map order exactly: particle collision masks use
        // these positions as bit indices, so even a reorder changes the
        // simulation's collider assignments.
        var mappedColliderIndices = colliderBindings
            .Select(binding => binding.ColliderIndex)
            .Distinct()
            .ToArray();
        var colliders = mappedColliderIndices
            .Where(sourceCollidersByIndex.ContainsKey)
            .Select(index => sourceCollidersByIndex[index])
            .Concat(sourceColliders.Where(collider => !mappedColliderIndices.Contains(collider.Index)))
            .ToArray();


        var bindings = preview.BoneBindings
            .Select(binding => new StandaloneBphclBoneBinding(
                binding.BoneIndex,
                binding.ParticleA,
                binding.ParticleB,
                binding.ParticleC))
            .Distinct()
            .ToArray();
        var dropSimpleMeshDeformerExecution = ShouldDropSimpleMeshDeformerExecution();
        var deformers = source.GetSimpleMeshBoneDeformData(clothIndex)
            .Select(deformer => new StandaloneBphclSimpleMeshBoneDeformer(
                deformer.OperatorIndex,
                deformer.InputBufferIndex,
                deformer.OutputTransformSetIndex,
                // BotW HKCL exposes its local source axis as 0, while every
                // matched native BPHCL mesh-to-bone deformer uses axis 3.
                // Preserve a BPHCL document verbatim, but use the native BPHCL
                // axis when constructing a fresh cross-game graph.
                preserveNativeBphclValues ? deformer.BoneAxis : 3,
                deformer.TriangleBonePairs.ToArray(),
                deformer.LocalBoneTransforms.ToArray()))
            .ToArray();
        if (dropSimpleMeshDeformerExecution)
        {
            runtime = DropRuntimeOperatorClass(runtime, "hclSimpleMeshBoneDeformOperator");
            deformers = Array.Empty<StandaloneBphclSimpleMeshBoneDeformer>();
        }
        if (!dropSimpleMeshDeformerExecution && !preserveNativeBphclValues && deformers.Length == 0 && bindings.Length > 0)
        {
            var triangles = preview.Triangles
                .Select((triangle, index) => (triangle, index))
                .ToDictionary(
                    entry => (entry.triangle.ParticleA, entry.triangle.ParticleB, entry.triangle.ParticleC),
                    entry => entry.index);
            var pairs = bindings
                .Where(binding => triangles.TryGetValue((binding.ParticleA, binding.ParticleB, binding.ParticleC), out _))
                .Select(binding => new SimpleMeshTriangleBonePairData(
                    checked((ushort)(binding.BoneIndex * 64)),
                    checked((ushort)(triangles[(binding.ParticleA, binding.ParticleB, binding.ParticleC)] * 6))))
                .ToArray();
            deformers = new[]
            {
                new StandaloneBphclSimpleMeshBoneDeformer(
                    -1,
                    1,
                    0,
                    3,
                    pairs,
                    Enumerable.Repeat(Matrix4x4.Identity, pairs.Length).ToArray())
            };
        }

        // BotW HKCL commonly leaves this field at its class default of zero,
        // while native BPHCL simulations author 0.2. Preserve exact native
        // BPHCL values, but use the observed BPHCL baseline for a fresh
        // cross-game graph rather than emitting a disabled tolerance.
        var collisionTolerance = preserveNativeBphclValues || settings.CollisionTolerance > 0.0f
            ? settings.CollisionTolerance
            : 0.2f;

        return new StandaloneBphclClothGraph(
            clothIndex,
            targetClothName,
            "cloth_skeleton_" + targetClothName,
            new StandaloneBphclSimulationSettings(
                new Vector3(settings.GravityX, settings.GravityY, settings.GravityZ),
                settings.DampingPerSecond,
                collisionTolerance,
                settings.TransferTranslationMotion,
                settings.MinTranslationSpeed,
                settings.MaxTranslationSpeed,
                settings.MinTranslationBlend,
                settings.MaxTranslationBlend,
                settings.TransferRotationMotion,
                settings.MinRotationSpeed,
                settings.MaxRotationSpeed,
                settings.MinRotationBlend,
                settings.MaxRotationBlend),
            particles,
            bones,
            preview.Triangles.Select(triangle => new StandaloneBphclTriangle(
                triangle.ParticleA,
                triangle.ParticleB,
                triangle.ParticleC)).ToArray(),
            source.GetTriangleFlips(clothIndex),
            constraintSets,
            constraints.Values.OrderBy(constraint => constraint.ConstraintSetIndex).ThenBy(constraint => constraint.LinkIndex).ToArray(),
            localRanges.OrderBy(range => range.ConstraintSetIndex).ThenBy(range => range.LocalConstraintIndex).ToArray(),
            transitionSets,
            bonePlaneSets,
            runtime,
            simulationRuntime,
            skinOperators,
            deformers,
            colliders,
            colliderBindings,
            bindings,
            Array.Empty<VolumeConstraintSetData>());
    }

    private static (
        StandaloneBphclParticle[] Particles,
        IReadOnlyList<StandaloneBphclColliderBinding> Bindings,
        ClothRuntimeData Runtime,
        NativeBphclSimulationRuntime SimulationRuntime)
        NormalizeFreshColliderSlots(
            IReadOnlyList<StandaloneBphclParticle> particles,
            IReadOnlyDictionary<int, StandaloneBphclCollider> collidersByIndex,
            IReadOnlyList<StandaloneBphclColliderBinding> bindings,
            ClothRuntimeData runtime,
            NativeBphclSimulationRuntime simulationRuntime)
    {
        // The global collider table is written in ascending source index order.
        // Use that same order for each simulation's local reference array. This
        // is significant because masks and offsets are addressed by local slot.
        var orderedSlots = bindings
            .Select((binding, oldSlot) => new { Binding = binding, OldSlot = oldSlot })
            .OrderBy(slot => slot.Binding.ColliderIndex)
            .ThenBy(slot => slot.OldSlot)
            .ToArray();
        var oldToNewSlot = new int[bindings.Count];
        for (var newSlot = 0; newSlot < orderedSlots.Length; newSlot++)
            oldToNewSlot[orderedSlots[newSlot].OldSlot] = newSlot;

        var remappedParticles = particles
            .Select(particle => particle with
            {
                CollisionMask = RemapPerInstanceColliderMask(
                    particle.CollisionMask,
                    oldToNewSlot)
            })
            .ToArray();
        var remappedPinching = simulationRuntime.CollidablePinchingData;
        if (remappedPinching.Count == bindings.Count)
        {
            var values = new NativeBphclCollidablePinchingData[bindings.Count];
            for (var oldSlot = 0; oldSlot < bindings.Count; oldSlot++)
                values[oldToNewSlot[oldSlot]] = remappedPinching[oldSlot];
            remappedPinching = values;
        }

        var remappedRuntime = runtime with
        {
            Operators = runtime.Operators.Select(@operator => @operator with
            {
                SimulateConfigs = @operator.SimulateConfigs.Select(config => config with
                {
                    InstanceCollidablesUsed = config.InstanceCollidablesUsed
                        .Select(slot => slot >= 0 && slot < oldToNewSlot.Length ? oldToNewSlot[slot] : slot)
                        .ToArray()
                }).ToArray()
            }).ToArray()
        };

        return (
            remappedParticles,
            orderedSlots.Select(slot => slot.Binding).ToArray(),
            remappedRuntime,
            simulationRuntime with { CollidablePinchingData = remappedPinching });
    }

    private static uint RemapPerInstanceColliderMask(uint mask, IReadOnlyList<int> oldToNewSlot)
    {
        // Bit 31 is landscape collision. Bits outside the local collider range
        // may be future native extensions, so retain them unchanged.
        var localBitMask = oldToNewSlot.Count >= 31
            ? 0x7fffffffu
            : (1u << oldToNewSlot.Count) - 1u;
        var remapped = mask & ~localBitMask;
        for (var oldSlot = 0; oldSlot < oldToNewSlot.Count; oldSlot++)
        {
            if ((mask & (1u << oldSlot)) != 0)
                remapped |= 1u << oldToNewSlot[oldSlot];
        }

        return remapped;
    }
    private static bool TryCreateDerivedBendStiffnessSet(
        ParticlePreviewData preview,
        ClothRuntimeData runtime,
        IReadOnlyList<StandaloneBphclConstraintSetMetadata> constraintSets,
        IEnumerable<StandaloneBphclConstraint> constraints,
        out int sourceBendSetIndex,
        out StandaloneBphclConstraintSetMetadata metadata,
        out IReadOnlyList<StandaloneBphclConstraint> links)
    {
        sourceBendSetIndex = -1;
        metadata = null!;
        links = Array.Empty<StandaloneBphclConstraint>();

        if (constraintSets.Any(set => string.Equals(
                set.TypeName,
                "hclBendStiffnessConstraintSet",
                StringComparison.Ordinal)))
        {
            return false;
        }

        var execution = runtime.Operators
            .Where(@operator => string.Equals(
                @operator.ClassName,
                "hclSimulateOperator",
                StringComparison.Ordinal))
            .SelectMany(@operator => @operator.SimulateConfigs)
            .SelectMany(config => config.ConstraintExecution)
            .Where(index => index >= 0)
            .ToArray();
        var duplicateBendSets = execution
            .GroupBy(index => index)
            .Where(group => group.Count() == 2)
            .Select(group => group.Key)
            .Where(index => constraintSets.Any(set =>
                set.Index == index &&
                string.Equals(set.TypeName, "hclBendLinkConstraintSet", StringComparison.Ordinal)))
            .ToArray();
        if (duplicateBendSets.Length != 1)
            return false;

        var bendSetIndex = duplicateBendSets[0];
        sourceBendSetIndex = bendSetIndex;
        var sourceLinks = constraints
            .Where(link => link.ConstraintSetIndex == bendSetIndex &&
                string.Equals(link.TypeName, "hclBendLinkConstraintSet", StringComparison.Ordinal))
            .OrderBy(link => link.LinkIndex)
            .ToArray();
        if (sourceLinks.Length == 0)
            return false;

        // Native matched BPHCL files only add a bend-stiffness set for
        // fixed-length bend links. A ranged bend link is solved twice rather
        // than converted to this derived four-particle constraint.
        const float fixedLengthTolerance = 0.00001f;
        if (sourceLinks.Any(link =>
                link.BendMinLength is not { } minimum ||
                link.StretchMaxLength is not { } maximum ||
                MathF.Abs(minimum - maximum) > fixedLengthTolerance))
        {
            return false;
        }

        var positions = preview.Particles.ToDictionary(particle => particle.Index, particle => particle.Position);
        var derivedLinks = new List<StandaloneBphclConstraint>(sourceLinks.Length);
        var maxRestDistance = 0.0f;
        foreach (var sourceLink in sourceLinks)
        {
            if (!TryFindBendQuad(
                    preview.Triangles,
                    sourceLink.ParticleA,
                    sourceLink.ParticleB,
                    out var particleC,
                    out var particleD) ||
                !positions.TryGetValue(sourceLink.ParticleA, out var a) ||
                !positions.TryGetValue(sourceLink.ParticleB, out var b) ||
                !positions.TryGetValue(particleC, out var c) ||
                !positions.TryGetValue(particleD, out var d) ||
                !TryGetBendRestConfiguration(
                    a,
                    b,
                    c,
                    d,
                    out var weightA,
                    out var weightB,
                    out var weightC,
                    out var weightD,
                    out var restCurvature,
                    out var restDistance))
            {
                return false;
            }

            var sourceStiffness = MathF.Abs(sourceLink.BendStiffness ?? sourceLink.StretchStiffness ?? 0.0f);
            var sourceLength = sourceLink.StretchMaxLength ?? sourceLink.BendMinLength ?? 0.0f;
            // The matched corpus contains both /10 and /15 generated bend
            // coefficients. Length is still the conservative fallback rule:
            // it preserves native Hair_2 until we can derive the exceptional
            // authored Muffler links from a stronger source signal.
            var bendStiffness = sourceStiffness <= 0.0f
                ? 0.0f
                : -sourceStiffness / (sourceLength >= 0.15f ? 15.0f : 10.0f);
            // Havok derives this set-level limit from the greatest rest-pose
            // separation of the two opposite edges in any bend quad.
            maxRestDistance = MathF.Max(maxRestDistance, MathF.Abs(restDistance));
            derivedLinks.Add(new StandaloneBphclConstraint(
                -1,
                sourceLink.LinkIndex,
                "hclBendStiffnessConstraintSet",
                sourceLink.ParticleA,
                sourceLink.ParticleB,
                null,
                null,
                null,
                null,
                bendStiffness,
                null,
                particleC,
                particleD,
                restCurvature,
                weightA,
                weightB,
                weightC,
                weightD));
        }

        var derivedIndex = constraintSets.Count == 0
            ? 0
            : constraintSets.Max(set => set.Index) + 1;
        metadata = new StandaloneBphclConstraintSetMetadata(
            derivedIndex,
            "Bend Stiffness",
            "hclBendStiffnessConstraintSet",
            checked((uint)derivedIndex),
            0,
            true,
            maxRestDistance > 0.0f ? 64.0f * maxRestDistance * maxRestDistance : 0.0f,
            true);
        links = derivedLinks
            .Select(link => link with { ConstraintSetIndex = derivedIndex })
            .ToArray();
        return true;
    }

    private static ClothRuntimeData ReplaceRepeatedBendPass(
        ClothRuntimeData runtime,
        int sourceBendSetIndex,
        int bendStiffnessSetIndex)
    {
        var changed = false;
        var operators = runtime.Operators.Select(@operator =>
        {
            if (!string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal) ||
                @operator.SimulateConfigs.Count == 0)
            {
                return @operator;
            }

            var configs = @operator.SimulateConfigs.Select(config =>
            {
                var occurrences = 0;
                var execution = config.ConstraintExecution.Select(index =>
                {
                    if (index != sourceBendSetIndex)
                        return index;

                    occurrences++;
                    if (occurrences != 2)
                        return index;

                    changed = true;
                    return bendStiffnessSetIndex;
                }).ToArray();
                return occurrences == 2
                    ? config with { ConstraintExecution = execution }
                    : config;
            }).ToArray();
            return @operator with { SimulateConfigs = configs };
        }).ToArray();

        return changed ? runtime with { Operators = operators } : runtime;
    }

    private static (
        List<StandaloneBphclConstraintSetMetadata> ConstraintSets,
        Dictionary<(int Set, int Link), StandaloneBphclConstraint> Constraints,
        StandaloneBphclLocalRange[] LocalRanges,
        StandaloneBphclTransitionConstraintSet[] TransitionSets,
        StandaloneBphclBonePlaneConstraintSet[] BonePlaneSets,
        ClothRuntimeData Runtime) CanonicalizeFreshConstraintProgram(
        IReadOnlyList<StandaloneBphclConstraintSetMetadata> sourceSets,
        IReadOnlyDictionary<(int Set, int Link), StandaloneBphclConstraint> sourceConstraints,
        IReadOnlyList<StandaloneBphclLocalRange> sourceLocalRanges,
        IReadOnlyList<StandaloneBphclTransitionConstraintSet> sourceTransitionSets,
        IReadOnlyList<StandaloneBphclBonePlaneConstraintSet> sourceBonePlaneSets,
        ClothRuntimeData sourceRuntime)
    {
        // The standalone writer emits ToTK BPHCL, not a verbatim HKCL object
        // graph. Every matched native BPHCL uses this compact solver ordering.
        // Bone planes are a BotW-only authoring feature in the matched corpus;
        // retaining them in fresh output produces an unobserved BPHCL program.
        var orderedSourceSets = sourceSets
            .Where(set => !string.Equals(
                set.TypeName,
                "hclBonePlanesConstraintSet",
                StringComparison.Ordinal))
            .OrderBy(set => GetFreshBphclConstraintOrder(set.TypeName))
            .ThenBy(set => set.Index)
            .ToArray();
        var indexMap = orderedSourceSets
            .Select((set, index) => (OldIndex: set.Index, NewIndex: index))
            .ToDictionary(entry => entry.OldIndex, entry => entry.NewIndex);
        var constraintSets = orderedSourceSets
            .Select((set, index) => set with
            {
                Index = index,
                ConstraintId = checked((uint)index)
            })
            .ToList();

        var constraints = sourceConstraints.Values
            .Where(link => indexMap.ContainsKey(link.ConstraintSetIndex))
            .Select(link => link with { ConstraintSetIndex = indexMap[link.ConstraintSetIndex] })
            .ToDictionary(link => (link.ConstraintSetIndex, link.LinkIndex));
        var localRanges = sourceLocalRanges
            .Where(range => indexMap.ContainsKey(range.ConstraintSetIndex))
            .Select(range => range with { ConstraintSetIndex = indexMap[range.ConstraintSetIndex] })
            .ToArray();
        var transitionSets = sourceTransitionSets
            .Where(set => indexMap.ContainsKey(set.ConstraintSetIndex))
            .Select(set => set with
            {
                ConstraintSetIndex = indexMap[set.ConstraintSetIndex],
                ConstraintId = checked((uint)indexMap[set.ConstraintSetIndex])
            })
            .ToArray();

        var operators = sourceRuntime.Operators.Select(@operator =>
        {
            if (!string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal) ||
                @operator.SimulateConfigs.Count == 0)
            {
                return @operator;
            }

            var configs = @operator.SimulateConfigs.Select(config => config with
            {
                ConstraintExecution = config.ConstraintExecution
                    .Where(index => index == -1 || indexMap.ContainsKey(index))
                    .Select(index => index == -1 ? -1 : indexMap[index])
                    .ToArray()
            }).ToArray();
            return @operator with { SimulateConfigs = configs };
        }).ToArray();

        return (
            constraintSets,
            constraints,
            localRanges,
            transitionSets,
            Array.Empty<StandaloneBphclBonePlaneConstraintSet>(),
            sourceRuntime with { Operators = operators });
    }

    private static int GetFreshBphclConstraintOrder(string typeName) => typeName switch
    {
        "hclStandardLinkConstraintSet" => 0,
        "hclStretchLinkConstraintSet" => 1,
        "hclLocalRangeConstraintSet" => 2,
        "hclTransitionConstraintSet" => 3,
        "hclBendLinkConstraintSet" => 4,
        "hclBendStiffnessConstraintSet" => 5,
        _ => 100
    };

    private static bool ShouldClampLocalRangeRadius(out float limit)
    {
        limit = 0.0f;
        var value = Environment.GetEnvironmentVariable("PHYSICSTOOL_CLAMP_LOCAL_RANGE_RADIUS");
        return !string.IsNullOrWhiteSpace(value) &&
            float.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out limit) &&
            limit > 0.0f;
    }

    private static bool ShouldDropLocalRangeSet() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PHYSICSTOOL_DROP_LOCAL_RANGE_SET"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldDropLocalRangeExecution() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PHYSICSTOOL_DROP_LOCAL_RANGE_EXECUTION"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static ClothRuntimeData DropConstraintExecution(
        ClothRuntimeData runtime,
        IReadOnlyList<StandaloneBphclConstraintSetMetadata> constraintSets,
        string typeName)
    {
        var removedSetIndices = constraintSets
            .Where(set => string.Equals(set.TypeName, typeName, StringComparison.Ordinal))
            .Select(set => set.Index)
            .ToHashSet();
        if (removedSetIndices.Count == 0)
            return runtime;

        var changed = false;
        var operators = runtime.Operators.Select(@operator =>
        {
            if (!string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal) ||
                @operator.SimulateConfigs.Count == 0)
            {
                return @operator;
            }

            var configs = @operator.SimulateConfigs.Select(config =>
            {
                var execution = config.ConstraintExecution
                    .Where(index => !removedSetIndices.Contains(index))
                    .ToArray();
                if (execution.Length == config.ConstraintExecution.Count)
                    return config;

                changed = true;
                return config with { ConstraintExecution = execution };
            }).ToArray();
            return @operator with { SimulateConfigs = configs };
        }).ToArray();

        return changed ? runtime with { Operators = operators } : runtime;
    }

    private static (
        List<StandaloneBphclConstraintSetMetadata> ConstraintSets,
        Dictionary<(int Set, int Link), StandaloneBphclConstraint> Constraints,
        StandaloneBphclLocalRange[] LocalRanges,
        ClothRuntimeData Runtime) DropLocalRangeSets(
        IReadOnlyList<StandaloneBphclConstraintSetMetadata> constraintSets,
        IReadOnlyDictionary<(int Set, int Link), StandaloneBphclConstraint> constraints,
        IReadOnlyList<StandaloneBphclLocalRange> localRanges,
        ClothRuntimeData runtime)
    {
        var removedIndices = constraintSets
            .Where(set => string.Equals(
                set.TypeName,
                "hclLocalRangeConstraintSet",
                StringComparison.Ordinal))
            .Select(set => set.Index)
            .ToHashSet();
        if (removedIndices.Count == 0)
        {
            return (
                constraintSets.ToList(),
                constraints.ToDictionary(entry => entry.Key, entry => entry.Value),
                localRanges.ToArray(),
                runtime);
        }

        var indexMap = new Dictionary<int, int>();
        var keptSets = new List<StandaloneBphclConstraintSetMetadata>();
        foreach (var set in constraintSets.OrderBy(set => set.Index))
        {
            if (removedIndices.Contains(set.Index))
                continue;

            var newIndex = keptSets.Count;
            indexMap[set.Index] = newIndex;
            keptSets.Add(set with
            {
                Index = newIndex,
                ConstraintId = checked((uint)newIndex)
            });
        }

        var keptConstraints = constraints.Values
            .Where(link => indexMap.ContainsKey(link.ConstraintSetIndex))
            .Select(link => link with { ConstraintSetIndex = indexMap[link.ConstraintSetIndex] })
            .ToDictionary(link => (link.ConstraintSetIndex, link.LinkIndex));
        var keptLocalRanges = localRanges
            .Where(range => indexMap.ContainsKey(range.ConstraintSetIndex))
            .Select(range => range with { ConstraintSetIndex = indexMap[range.ConstraintSetIndex] })
            .ToArray();
        var operators = runtime.Operators.Select(@operator =>
        {
            if (!string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal) ||
                @operator.SimulateConfigs.Count == 0)
            {
                return @operator;
            }

            var configs = @operator.SimulateConfigs.Select(config => config with
            {
                ConstraintExecution = config.ConstraintExecution
                    .Where(index => index == -1 || indexMap.ContainsKey(index))
                    .Select(index => index == -1 ? -1 : indexMap[index])
                    .ToArray()
            }).ToArray();
            return @operator with { SimulateConfigs = configs };
        }).ToArray();

        return (keptSets, keptConstraints, keptLocalRanges, runtime with { Operators = operators });
    }

    private static bool ShouldDropBendStiffnessExecution() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PHYSICSTOOL_DROP_BEND_STIFFNESS_EXECUTION"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldDropBendStiffnessSet() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PHYSICSTOOL_DROP_BEND_STIFFNESS_SET"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldDropSimpleMeshDeformerExecution() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PHYSICSTOOL_DROP_SIMPLE_MESH_DEFORMER_EXECUTION"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldDropObjectSkinExecution() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PHYSICSTOOL_DROP_OBJECT_SKIN_EXECUTION"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldDropMoveParticlesExecution() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PHYSICSTOOL_DROP_MOVE_PARTICLES_EXECUTION"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldDropSimulateExecution() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PHYSICSTOOL_DROP_SIMULATE_EXECUTION"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static bool ShouldDropStateSimulationReferences() =>
        string.Equals(
            Environment.GetEnvironmentVariable("PHYSICSTOOL_DROP_STATE_SIMULATION_REFERENCES"),
            "1",
            StringComparison.OrdinalIgnoreCase);

    private static ClothRuntimeData DropRuntimeStateSimulationReferences(ClothRuntimeData runtime) =>
        runtime with
        {
            States = runtime.States.Select(state => state with
            {
                SimulationIndices = Array.Empty<int>()
            }).ToArray()
        };

    private static ClothRuntimeData DropRuntimeStateOperatorClassExecution(ClothRuntimeData runtime, params string[] classNames)
    {
        var names = classNames.ToHashSet(StringComparer.Ordinal);
        var removed = runtime.Operators
            .Where(@operator => names.Contains(@operator.ClassName))
            .Select(@operator => @operator.Index)
            .ToHashSet();
        if (removed.Count == 0)
            return runtime;

        return runtime with
        {
            States = runtime.States.Select(state => state with
            {
                OperatorIndices = state.OperatorIndices
                    .Where(index => !removed.Contains(index))
                    .ToArray()
            }).ToArray()
        };
    }

    private static ClothRuntimeData DropRuntimeOperatorClass(ClothRuntimeData runtime, string className)
    {
        var removed = runtime.Operators
            .Where(@operator => string.Equals(@operator.ClassName, className, StringComparison.Ordinal))
            .Select(@operator => @operator.Index)
            .ToHashSet();
        if (removed.Count == 0)
            return runtime;

        var indexMap = new Dictionary<int, int>();
        var operators = new List<ClothOperatorData>();
        foreach (var @operator in runtime.Operators.OrderBy(@operator => @operator.Index))
        {
            if (removed.Contains(@operator.Index))
                continue;

            var newIndex = operators.Count;
            indexMap[@operator.Index] = newIndex;
            operators.Add(@operator with { Index = newIndex });
        }

        var states = runtime.States.Select(state => state with
        {
            OperatorIndices = state.OperatorIndices
                .Where(index => indexMap.ContainsKey(index))
                .Select(index => indexMap[index])
                .ToArray()
        }).ToArray();

        return runtime with
        {
            Operators = operators,
            States = states
        };
    }

    private static (
        List<StandaloneBphclConstraintSetMetadata> ConstraintSets,
        Dictionary<(int Set, int Link), StandaloneBphclConstraint> Constraints,
        ClothRuntimeData Runtime) DropBendStiffnessSets(
        IReadOnlyList<StandaloneBphclConstraintSetMetadata> constraintSets,
        IReadOnlyDictionary<(int Set, int Link), StandaloneBphclConstraint> constraints,
        ClothRuntimeData runtime)
    {
        var removedIndices = constraintSets
            .Where(set => string.Equals(
                set.TypeName,
                "hclBendStiffnessConstraintSet",
                StringComparison.Ordinal))
            .Select(set => set.Index)
            .ToHashSet();
        if (removedIndices.Count == 0)
            return (constraintSets.ToList(), constraints.ToDictionary(entry => entry.Key, entry => entry.Value), runtime);

        var indexMap = new Dictionary<int, int>();
        var keptSets = new List<StandaloneBphclConstraintSetMetadata>();
        foreach (var set in constraintSets.OrderBy(set => set.Index))
        {
            if (removedIndices.Contains(set.Index))
                continue;

            var newIndex = keptSets.Count;
            indexMap[set.Index] = newIndex;
            keptSets.Add(set with
            {
                Index = newIndex,
                ConstraintId = checked((uint)newIndex)
            });
        }

        var keptConstraints = constraints.Values
            .Where(link => indexMap.ContainsKey(link.ConstraintSetIndex))
            .Select(link => link with { ConstraintSetIndex = indexMap[link.ConstraintSetIndex] })
            .ToDictionary(link => (link.ConstraintSetIndex, link.LinkIndex));

        var operators = runtime.Operators.Select(@operator =>
        {
            if (!string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal) ||
                @operator.SimulateConfigs.Count == 0)
            {
                return @operator;
            }

            var configs = @operator.SimulateConfigs.Select(config => config with
            {
                ConstraintExecution = config.ConstraintExecution
                    .Where(index => index == -1 || indexMap.ContainsKey(index))
                    .Select(index => index == -1 ? -1 : indexMap[index])
                    .ToArray()
            }).ToArray();
            return @operator with { SimulateConfigs = configs };
        }).ToArray();

        return (keptSets, keptConstraints, runtime with { Operators = operators });
    }

    private static ClothRuntimeData DropBendStiffnessExecution(
        ClothRuntimeData runtime,
        IReadOnlyList<StandaloneBphclConstraintSetMetadata> constraintSets)
    {
        var bendStiffnessSetIndices = constraintSets
            .Where(set => string.Equals(
                set.TypeName,
                "hclBendStiffnessConstraintSet",
                StringComparison.Ordinal))
            .Select(set => set.Index)
            .ToHashSet();
        if (bendStiffnessSetIndices.Count == 0)
            return runtime;

        var changed = false;
        var operators = runtime.Operators.Select(@operator =>
        {
            if (!string.Equals(@operator.ClassName, "hclSimulateOperator", StringComparison.Ordinal) ||
                @operator.SimulateConfigs.Count == 0)
            {
                return @operator;
            }

            var configs = @operator.SimulateConfigs.Select(config =>
            {
                var execution = config.ConstraintExecution
                    .Where(index => !bendStiffnessSetIndices.Contains(index))
                    .ToArray();
                if (execution.Length == config.ConstraintExecution.Count)
                    return config;

                changed = true;
                return config with { ConstraintExecution = execution };
            }).ToArray();
            return @operator with { SimulateConfigs = configs };
        }).ToArray();

        return changed ? runtime with { Operators = operators } : runtime;
    }
    private static bool TryFindBendQuad(
        IReadOnlyList<ParticlePreviewTriangle> triangles,
        int particleA,
        int particleB,
        out int particleC,
        out int particleD)
    {
        particleC = -1;
        particleD = -1;
        foreach (var triangleA in triangles)
        {
            if (!TriangleContains(triangleA, particleA) || TriangleContains(triangleA, particleB) ||
                !TryGetNeighborsAroundVertex(triangleA, particleA, out var previous, out var next))
            {
                continue;
            }

            if (!triangles.Any(triangleB =>
                    TriangleContains(triangleB, particleB) &&
                    TriangleContains(triangleB, previous) &&
                    TriangleContains(triangleB, next)))
            {
                continue;
            }

            particleC = previous;
            particleD = next;
            return true;
        }

        return false;
    }

    private static bool TriangleContains(ParticlePreviewTriangle triangle, int particle) =>
        triangle.ParticleA == particle || triangle.ParticleB == particle || triangle.ParticleC == particle;

    private static bool TryGetNeighborsAroundVertex(
        ParticlePreviewTriangle triangle,
        int particle,
        out int previous,
        out int next)
    {
        if (triangle.ParticleA == particle)
        {
            previous = triangle.ParticleC;
            next = triangle.ParticleB;
            return true;
        }
        if (triangle.ParticleB == particle)
        {
            previous = triangle.ParticleA;
            next = triangle.ParticleC;
            return true;
        }
        if (triangle.ParticleC == particle)
        {
            previous = triangle.ParticleB;
            next = triangle.ParticleA;
            return true;
        }

        previous = -1;
        next = -1;
        return false;
    }

    internal static bool TryGetBendRestConfiguration(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        out float weightA,
        out float weightB,
        out float weightC,
        out float weightD,
        out float restCurvature,
        out float restDistance,
        bool validateCandidate = true)
    {
        var edge = b - a;
        var oppositeEdge = d - c;
        var edgeLengthSq = edge.LengthSquared();
        var oppositeLengthSq = oppositeEdge.LengthSquared();
        var edgeDot = Vector3.Dot(edge, oppositeEdge);
        var denominator = edgeLengthSq * oppositeLengthSq - edgeDot * edgeDot;
        if (validateCandidate ? edgeLengthSq < 0.0000001f || oppositeLengthSq < 0.0000001f || MathF.Abs(denominator) < 0.0000001f
            : edgeLengthSq < 1e-16f || oppositeLengthSq < 1e-16f || MathF.Abs(denominator) < 1e-10f * edgeLengthSq * oppositeLengthSq)
        {
            weightA = weightB = weightC = weightD = restCurvature = restDistance = 0.0f;
            return false;
        }

        var ac = a - c;
        var closestA = (edgeDot * Vector3.Dot(oppositeEdge, ac) -
                        oppositeLengthSq * Vector3.Dot(edge, ac)) / denominator;
        var closestC = (edgeLengthSq * Vector3.Dot(oppositeEdge, ac) -
                        edgeDot * Vector3.Dot(edge, ac)) / denominator;
        if (validateCandidate && (closestA is < -0.25f or > 1.25f || closestC is < -0.25f or > 1.25f))
        {
            weightA = weightB = weightC = weightD = restCurvature = restDistance = 0.0f;
            return false;
        }

        // Havok's four-point bend constraint is not based on the closest
        // points between the two edges. It first balances the shared edge
        // using the two triangle heights, then projects that balanced point
        // back to the opposite bend edge. This matches native BPHCL weights
        // on both regular and tapered cloth quads.
        var edgeLength = MathF.Sqrt(edgeLengthSq);
        var heightCFromBendEdge = Vector3.Cross(c - a, edge).Length() / edgeLength;
        var heightDFromBendEdge = Vector3.Cross(d - a, edge).Length() / edgeLength;
        var heightSum = heightCFromBendEdge + heightDFromBendEdge;
        if (heightSum < 0.0000001f)
        {
            weightA = weightB = weightC = weightD = restCurvature = restDistance = 0.0f;
            return false;
        }

        var sharedCWeight = heightDFromBendEdge / heightSum;
        var sharedDWeight = heightCFromBendEdge / heightSum;
        var balancedSharedPoint = sharedCWeight * c + sharedDWeight * d;
        var bendEdgeProjection = Vector3.Dot(balancedSharedPoint - a, edge) / edgeLengthSq;
        if (validateCandidate && (bendEdgeProjection is < -0.25f or > 1.25f))
        {
            weightA = weightB = weightC = weightD = restCurvature = restDistance = 0.0f;
            return false;
        }

        weightA = 1.0f - bendEdgeProjection;
        weightB = bendEdgeProjection;
        weightC = -sharedCWeight;
        weightD = -sharedDWeight;

        var closestOnAb = a + closestA * edge;
        var closestOnCd = c + closestC * oppositeEdge;
        var cross = Vector3.Cross(edge, oppositeEdge);
        var crossLength = cross.Length();
        restDistance = crossLength > 0.0000001f
            ? Vector3.Dot(closestOnCd - closestOnAb, cross / crossLength)
            : 0.0f;

        var sharedLength = MathF.Sqrt(oppositeLengthSq);
        var normalA = Vector3.Cross(oppositeEdge, a - c);
        var normalB = Vector3.Cross(b - c, oppositeEdge);
        var heightA = normalA.Length() / sharedLength;
        var heightB = normalB.Length() / sharedLength;
        var normalALength = normalA.Length();
        var normalBLength = normalB.Length();
        if (heightA < 0.0000001f || heightB < 0.0000001f ||
            normalALength < 0.0000001f || normalBLength < 0.0000001f)
        {
            restCurvature = 0.0f;
            return true;
        }

        var sine = Vector3.Dot(
            Vector3.Cross(normalA / normalALength, normalB / normalBLength),
            oppositeEdge / sharedLength);
        // Native BPHCL uses the full signed rest curvature; matched files
        // legitimately exceed +/- 0.75 (for example, folded cape geometry).
        // Clamping here flattens those bends and changes their solver response.
        restCurvature = sine / (heightA + heightB);
        return float.IsFinite(restCurvature) && float.IsFinite(restDistance);
    }
    private static void RecoverMissingBendStiffnessRestPoseLimits(
        ParticlePreviewData preview,
        List<StandaloneBphclConstraintSetMetadata> constraintSets,
        IEnumerable<StandaloneBphclConstraint> constraints)
    {
        var positions = preview.Particles.ToDictionary(particle => particle.Index, particle => particle.Position);
        var allConstraints = constraints.ToArray();
        for (var setIndex = 0; setIndex < constraintSets.Count; setIndex++)
        {
            var metadata = constraintSets[setIndex];
            if (!string.Equals(metadata.TypeName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal) ||
                metadata.MaxRestPoseHeightSq is > 0.0000001f)
            {
                continue;
            }

            var maxRestDistance = 0.0f;
            var foundLink = false;
            foreach (var link in allConstraints.Where(link =>
                         link.ConstraintSetIndex == metadata.Index &&
                         string.Equals(link.TypeName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal)))
            {
                if (link.ParticleC is not { } particleC || link.ParticleD is not { } particleD ||
                    !positions.TryGetValue(link.ParticleA, out var a) ||
                    !positions.TryGetValue(link.ParticleB, out var b) ||
                    !positions.TryGetValue(particleC, out var c) ||
                    !positions.TryGetValue(particleD, out var d) ||
                    !TryGetBendRestConfiguration(
                        a, b, c, d,
                        out _, out _, out _, out _, out _, out var restDistance))
                {
                    continue;
                }

                maxRestDistance = MathF.Max(maxRestDistance, MathF.Abs(restDistance));
                foundLink = true;
            }

            if (foundLink && maxRestDistance > 0.0f)
            {
                constraintSets[setIndex] = metadata with
                {
                    ClampBendStiffness = true,
                    MaxRestPoseHeightSq = 64.0f * maxRestDistance * maxRestDistance,
                    UseRestPoseConfig = true
                };
            }
        }
    }
    // BPHCL resolves actor-skeleton inputs through the Link: namespace.
    // Cloth-only output bones stay local to the armor and must remain unprefixed.
    private static string ToBphclRuntimeBoneName(string value)
    {
        var name = StripLinkPrefix(value);
        return UsesLinkRuntimeNamespace(name) ? "Link:" + name : name;
    }

    private static bool UsesLinkRuntimeNamespace(string name)
    {
        if (name is "Root" or "Skl_Root" or "Spine_1" or "Spine_2" or "Neck" or "Head" or
            "Waist" or "Clavicle_L" or "Clavicle_R" or
            "Arm_1_L" or "Arm_1_R" or "Arm_2_L" or "Arm_2_R" or
            "Arm_3_L" or "Arm_3_R" or "Wrist_L" or "Wrist_R" or
            "Hand_L" or "Hand_R" or "Leg_1_L" or "Leg_1_R" or
            "Leg_2_L" or "Leg_2_R" or "Ankle_L" or "Ankle_R" or
            "Foot_L" or "Foot_R" or "Toe_L" or "Toe_R")
        {
            return true;
        }

        return name.StartsWith("Finger", StringComparison.OrdinalIgnoreCase) ||
               IsSharedHairBone(name) ||
               name.StartsWith("Momi_", StringComparison.OrdinalIgnoreCase) ||
               (name.StartsWith("Ponytail_", StringComparison.OrdinalIgnoreCase) &&
                !name.EndsWith("_Armor", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSharedHairBone(string name)
    {
        // Native BPHCL uses Link: for the actor's canonical Hair_A_1-style
        // chains, but not for armor-local names such as Hair_Acs_A,
        // Hair_A_1_Armor, or Hair_Back_C_1.
        if (string.Equals(name, "Hair_Root", StringComparison.OrdinalIgnoreCase))
            return true;

        var parts = name.Split('_');
        return parts.Length == 3 &&
            string.Equals(parts[0], "Hair", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Length == 1 &&
            char.IsLetter(parts[1][0]) &&
            int.TryParse(parts[2], out _);
    }

    private static string ToBphclRuntimeColliderName(string colliderName, string attachedBoneName)
    {
        var name = StripLinkPrefix(colliderName);
        var runtimeBone = ToBphclRuntimeBoneName(attachedBoneName);
        // Root-table entries are not necessarily mapped by a cloth. Native
        // BPHCL still names their normal actor colliders with the Link prefix.
        return string.IsNullOrEmpty(attachedBoneName) ||
               runtimeBone.StartsWith("Link:", StringComparison.Ordinal)
            ? "Link:" + name
            : name;
    }
    private static string StripLinkPrefix(string value)
    {
        while (value.StartsWith("Link:", StringComparison.OrdinalIgnoreCase))
            value = value[5..];
        return value;
    }

    // TotK's landscape bit is not retained by an ordinary BotW HKCL. Native
    // TotK cloths enable it for every dynamic particle in the standard case.
    // File-specific exceptions can be added to the conversion profile catalog.
    private static HashSet<int> SelectLandscapeParticles(
        IReadOnlyList<ParticleEditRow> particles,
        bool enableLandscapeCollision) =>
        enableLandscapeCollision
            ? particles.Where(particle => !particle.Fixed)
                .Select(particle => particle.Index)
                .ToHashSet()
            : [];

    private static uint ApplyLandscapeCollisionMask(ParticleEditRow particle, bool useLandscapeCollision)
    {
        const uint landscapeBit = 0x80000000u;
        var mask = particle.CollisionMask & ~landscapeBit;
        return !particle.Fixed && useLandscapeCollision ? mask | landscapeBit : mask;
    }
    private static float ToBphclMass(bool fixedParticle, float sourceMass, float scale)
    {
        return fixedParticle || !float.IsFinite(scale) || scale <= 0.0f ? 0.0f : sourceMass / scale;
    }

    private static float ToBphclInverseMass(bool fixedParticle, float sourceMass, float scale)
    {
        var mass = ToBphclMass(fixedParticle, sourceMass, scale);
        return mass > float.Epsilon ? 1.0f / mass : 0.0f;
    }

    private static float? ReverseConstraintValue(
        string? sourcePath,
        string clothName,
        string constraintClass,
        string fieldName,
        float? value,
        float constraintScale)
    {
        if (!value.HasValue)
            return null;

        var reverseProfiled = (float)BphclConversionProfileCatalog.ReverseConstraintFieldProfile(
            sourcePath,
            clothName,
            constraintClass,
            fieldName,
            value.Value);
        return ScaleSoftConstraint(constraintClass, fieldName, reverseProfiled, constraintScale);
    }
    private static float? ScaleSoftConstraint(
        string constraintClass,
        string fieldName,
        float? value,
        float scale)
    {
        if (!value.HasValue || !float.IsFinite(scale) || scale <= 0.0f)
            return value;

        var isSoftCoefficient = constraintClass switch
        {
            "hclStandardLinkConstraintSet" => fieldName == "stiffness",
            "hclBendLinkConstraintSet" => fieldName is "bendStiffness" or "stretchStiffness",
            "hclBendStiffnessConstraintSet" => fieldName == "bendStiffness",
            _ => false
        };
        return isSoftCoefficient ? value.Value / scale : value;
    }
    private static Vector3 ToColliderLocalPoint(Matrix4x4 transform, Vector3 world)
    {
        var basis = new Matrix4x4(
            transform.M11, transform.M12, transform.M13, 0.0f,
            transform.M21, transform.M22, transform.M23, 0.0f,
            transform.M31, transform.M32, transform.M33, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);
        if (!Matrix4x4.Invert(basis, out var inverse))
            throw new InvalidDataException("A collider transform cannot be inverted.");

        var translated = world - new Vector3(transform.M41, transform.M42, transform.M43);
        return new Vector3(
            inverse.M11 * translated.X + inverse.M21 * translated.Y + inverse.M31 * translated.Z,
            inverse.M12 * translated.X + inverse.M22 * translated.Y + inverse.M32 * translated.Z,
            inverse.M13 * translated.X + inverse.M23 * translated.Y + inverse.M33 * translated.Z);
    }

    private static Vector3 ToColliderLocalDirection(Matrix4x4 transform, Vector3 worldDirection)
    {
        var translated = worldDirection + new Vector3(transform.M41, transform.M42, transform.M43);
        var local = ToColliderLocalPoint(transform, translated);
        return local.LengthSquared() < 0.000001f ? Vector3.UnitY : Vector3.Normalize(local);
    }
}
