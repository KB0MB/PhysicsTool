using System.Numerics;

namespace HKCLTool;

// Plain format and editor-facing data. Keeping these records separate from
// binary reading makes it possible to add a native writer later without
// coupling it to the UI or bridge code.
public sealed record NativeBphclHeader(
    byte Reserve0,
    byte Reserve1,
    ushort ByteOrderMark,
    byte FileType,
    byte MaxSectionCapacity,
    uint TagFileOffset,
    uint ParameterOffset,
    uint FileEndOffset,
    uint TagFileSize,
    uint ParameterSize,
    uint FileEndSize);

public sealed record NativeBphclItem(uint Flags, uint TypeIndex, uint DataOffset, uint Count);
public sealed record NativeBphclPatch(uint TypeIndex, IReadOnlyList<uint> Offsets);

// Read-only metadata for inspecting native hkArray storage while reverse
// engineering a field whose element type is not exposed by reflection.
public sealed record NativeBphclArrayLayout(
    int StorageItemIndex,
    uint Count,
    uint EntryPatchTypeIndex,
    string? EntryTypeName,
    uint StorageByteLength);

// Describes an hkArray<T*> field in DATA. It gives the writer enough context
// to replace the backing reference block without relying on spare capacity.
internal sealed record NativeBphclReferenceArray(
    uint FieldOffset,
    int StorageItemIndex,
    NativeBphclItem StorageItem,
    uint EntryPatchTypeIndex);

public sealed record NativeBphclTypeDefinition(
    uint TypeIndex,
    uint ParentTypeIndex,
    uint Flags,
    uint? Size,
    uint? Alignment,
    IReadOnlyList<NativeBphclTypeMember> Members);

public sealed record NativeBphclTypeMember(
    string Name,
    uint Offset,
    uint TypeIndex,
    uint Flags);

public sealed record NativeBphclNamedVariant(
    int Index,
    string Name,
    string ClassName,
    int ObjectItemIndex,
    uint ObjectDataOffset,
    string ObjectTypeName);

public sealed record NativeBphclCloth(
    int Index,
    string Name,
    int ItemIndex,
    uint DataOffset,
    IReadOnlyList<NativeBphclSimCloth> SimCloths,
    IReadOnlyList<NativeBphclBufferDefinition> BufferDefinitions,
    IReadOnlyList<NativeBphclTransformSetDefinition> TransformSetDefinitions,
    IReadOnlyList<NativeBphclOperatorLayout> Operators,
    IReadOnlyList<NativeBphclClothState> States,
    IReadOnlyList<NativeBphclObjectSpaceSkin> ObjectSpaceSkins,
    IReadOnlyList<NativeBphclBoneSpaceSkin> BoneSpaceSkins,
    IReadOnlyList<NativeBphclSimpleMeshBoneDeform> SimpleMeshBoneDeformers)
{
    public int SimClothCount => SimCloths.Count;
}

public sealed record NativeBphclClothState(
    int Index,
    int ItemIndex,
    string Name,
    IReadOnlyList<int> OperatorItemIndices,
    IReadOnlyList<int> SimClothItemIndices);

// Buffer definitions describe the real vertex topology consumed by cloth
// operators. Particle count alone is not enough to safely reconstruct a
// BPHCL cloth in HKCL.
public sealed record NativeBphclBufferDefinition(
    int Index,
    int ItemIndex,
    uint DataOffset,
    string ClassName,
    string MeshName,
    string BufferName,
    int Type,
    int SubType,
    uint VertexCount,
    uint TriangleCount);

public sealed record NativeBphclTransformSetDefinition(
    int Index,
    int ItemIndex,
    uint DataOffset,
    string Name,
    int Type,
    uint TransformCount);

// Operator routing determines which buffers are read and written. The
// specific skinning payload is parsed separately; this record captures the
// common topology necessary to distinguish CopyVertices from GatherAll.
public sealed record NativeBphclOperatorLayout(
    int Index,
    int ItemIndex,
    uint DataOffset,
    string ClassName,
    int? InputBufferIndex,
    int? OutputBufferIndex,
    int? TransformSetIndex,
    int? VertexCount,
    int? StartVertexIn,
    int? StartVertexOut,
    IReadOnlyList<short> VertexInputFromVertexOutput,
    IReadOnlyList<NativeBphclVertexParticlePair> VertexParticlePairs,
    int? SimulationClothIndex,
    int? ReferenceBufferIndex,
    IReadOnlyList<NativeBphclSimulateConfig> SimulateConfigs);

public sealed record NativeBphclVertexParticlePair(ushort VertexIndex, ushort ParticleIndex);

public sealed record NativeBphclSimulateConfig(
    IReadOnlyList<int> ConstraintExecution,
    IReadOnlyList<int> InstanceCollidablesUsed,
    byte SubSteps,
    byte NumberOfSolveIterations,
    bool UseAllInstanceCollidables,
    bool AdaptConstraintStiffness);

// hclObjectSpaceSkinPOperator is the missing part of a real topology rebuild:
// it maps animated bones to the scratch vertices that initialize the cloth.
// These records preserve the source layout verbatim while the HKCL writer work
// is built around the matching generated C# structures.
public sealed record NativeBphclObjectSpaceSkin(
    int OperatorIndex,
    int ItemIndex,
    uint DataOffset,
    IReadOnlyList<Matrix4x4> BoneFromSkinMeshTransforms,
    IReadOnlyList<ushort> TransformSubset,
    int OutputBufferIndex,
    int TransformSetIndex,
    NativeBphclObjectSpaceDeformer Deformer,
    IReadOnlyList<NativeBphclPackedPositionBlock> LocalPs,
    IReadOnlyList<NativeBphclPositionBlock> LocalUnpackedPs);

public sealed record NativeBphclObjectSpaceDeformer(
    IReadOnlyList<NativeBphclPackedArray> EightBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> SevenBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> SixBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> FiveBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> FourBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> ThreeBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> TwoBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> OneBlendEntries,
    IReadOnlyList<byte> ControlBytes,
    ushort StartVertexIndex,
    ushort EndVertexIndex,
    bool PartialWrite);

// hclBoneSpaceSkinPOperator writes a vertex buffer from local positions
// weighted against the selected skeleton transforms. TotK uses it for cloth
// units that do not take the object-space skinning path.
public sealed record NativeBphclBoneSpaceSkin(
    int OperatorIndex,
    int ItemIndex,
    uint DataOffset,
    IReadOnlyList<ushort> TransformSubset,
    int OutputBufferIndex,
    int TransformSetIndex,
    NativeBphclBoneSpaceDeformer Deformer,
    IReadOnlyList<NativeBphclPositionBlock> LocalPs,
    IReadOnlyList<NativeBphclPositionBlock> LocalUnpackedPs);

public sealed record NativeBphclBoneSpaceDeformer(
    IReadOnlyList<NativeBphclPackedArray> FourBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> ThreeBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> TwoBlendEntries,
    IReadOnlyList<NativeBphclPackedArray> OneBlendEntries,
    IReadOnlyList<byte> ControlBytes,
    ushort StartVertexIndex,
    ushort EndVertexIndex,
    ushort BatchSizeSpu,
    bool PartialWrite);

// Each block contains 64 packed positions or 16 unpacked positions. Keeping
// its block boundary is important because Havok's SIMD layout depends on it.
public sealed record NativeBphclPositionBlock(IReadOnlyList<Vector4> Positions);

public sealed record NativeBphclPackedPositionBlock(IReadOnlyList<short> Positions);

// A raw block is retained alongside its reflected element type. The writer
// will later decode it into HKX2's generated blend-entry structures.
public sealed record NativeBphclPackedArray(
    string ElementType,
    int ElementSize,
    IReadOnlyList<byte> Bytes)
{
    public int Count => ElementSize == 0 ? 0 : Bytes.Count / ElementSize;
}

// hclSimpleMeshBoneDeformOperator converts the simulated virtual cloth mesh
// back into output bone transforms. It is the key source structure for a
// future BPHCL -> HKCL cloth-layout conversion.
public sealed record NativeBphclSimpleMeshBoneDeform(
    int OperatorIndex,
    int ItemIndex,
    uint DataOffset,
    uint InputBufferIndex,
    uint OutputTransformSetIndex,
    IReadOnlyList<NativeBphclTriangleBonePair> TriangleBonePairs,
    IReadOnlyList<Matrix4x4> LocalBoneTransforms,
    int BoneAxis);

public sealed record NativeBphclTriangleBonePair(ushort BoneOffset, ushort TriangleOffset);

public sealed record NativeBphclQsTransform(Vector4 Translation, Vector4 Rotation, Vector4 Scale);

public sealed record NativeBphclCollidableTransformMap(
    int TransformSetIndex,
    IReadOnlyList<uint> TransformIndices,
    IReadOnlyList<Matrix4x4> Offsets);

public sealed record NativeBphclSimulationInfo(Vector4 Gravity, float GlobalDampingPerSecond);

public sealed record NativeBphclTransferMotionData(
    int TransformSetIndex,
    int TransformIndex,
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

public sealed record NativeBphclLandscapeCollisionData(
    float LandscapeRadius,
    bool EnableStuckParticleDetection,
    float StuckParticlesStretchFactorSq,
    bool PinchDetectionEnabled,
    byte PinchDetectionPriority,
    float PinchDetectionRadius,
    float CollisionTolerance);

// These flags sit beside the particle and constraint data. They are easy to
// overlook because they are not part of the simulation-info subobject, but
// Havok uses them while it initializes a simulation instance.
public sealed record NativeBphclSimulationRuntime(
    bool DoNormals,
    float MaxParticleRadius,
    float TotalMass,
    NativeBphclTransferMotionData TransferMotionData,
    bool TransferMotionEnabled,
    bool LandscapeCollisionEnabled,
    NativeBphclLandscapeCollisionData LandscapeCollisionData,
    uint NumLandscapeCollidableParticles,
    bool PinchDetectionEnabled,
    IReadOnlyList<bool> PerParticlePinchDetectionEnabledFlags,
    ushort MinPinchedParticleIndex,
    ushort MaxPinchedParticleIndex,
    uint MaxCollisionPairs);

public sealed record NativeBphclSimCloth(
    int Index,
    string Name,
    int ItemIndex,
    uint DataOffset,
    NativeBphclSimulationInfo SimulationInfo,
    IReadOnlyList<NativeBphclParticle> Particles,
    IReadOnlyList<ushort> TriangleIndices,
    IReadOnlyList<byte> TriangleFlips,
    IReadOnlyList<NativeBphclConstraintSet> ConstraintSets,
    IReadOnlyList<uint> SimulationOperatorIds,
    IReadOnlyList<uint> StaticCollisionMasks,
    NativeBphclSimulationRuntime Runtime,
    NativeBphclCollidableTransformMap CollidableTransformMap,
    IReadOnlyList<int> CollidableItemIndices)
{
    public int ConstraintSetCount => ConstraintSets.Count;
    public int TriangleCount => TriangleIndices.Count / 3;
}

public sealed record NativeBphclConstraintSet(
    int Index,
    string Name,
    string ClassName,
    uint ConstraintId,
    uint ConstraintType,
    int ItemIndex,
    uint DataOffset,
    IReadOnlyList<NativeBphclConstraintLink> Links,
    IReadOnlyList<NativeBphclConstraintLink> LocalConstraints,
    IReadOnlyList<NativeBphclConstraintLink> TransitionParticles,
    IReadOnlyDictionary<string, double> Values)
{
    public int LinkCount => Links.Count;
}

public sealed record NativeBphclConstraintLink(
    int Index,
    int? ParticleA,
    int? ParticleB,
    IReadOnlyDictionary<string, double> Values);

public sealed record NativeBphclParticle(
    int Index,
    Vector4 Position,
    bool Fixed,
    float Mass,
    float InverseMass,
    float Radius,
    float Friction,
    uint PositionDataOffset,
    uint PhysicsDataOffset);

// The first native write surface is intentionally narrow. These fields do
// not alter array sizes, ITEM entries, or PTCH fixups, making them safe for
// a real binary round-trip before merge support is introduced.
public sealed record NativeBphclParticleEdit(
    Vector4? Position = null,
    float? Mass = null,
    float? Radius = null,
    float? Friction = null);

// The shape stays separate from its hclCollidable transform. Shape positions
// are local to that transform, while Translation/Axis* place it in the scene.
public sealed record NativeBphclColliderShape(
    string TypeName,
    int Kind,
    Vector4 Start,
    Vector4 End,
    float Radius,
    float EndRadius,
    Vector4 PlaneEquation);

public sealed record NativeBphclCollider(
    int Index,
    string Name,
    int ItemIndex,
    uint DataOffset,
    Vector4 Translation,
    Vector4 AxisX,
    Vector4 AxisY,
    Vector4 AxisZ,
    NativeBphclColliderShape Shape,
    bool Enabled);

public sealed record NativeBphclSkeleton(
    int Index,
    string Name,
    int ItemIndex,
    uint DataOffset,
    IReadOnlyList<NativeBphclBone> Bones)
{
    public int BoneCount => Bones.Count;
}

public sealed record NativeBphclBone(
    int Index,
    string Name,
    int ParentIndex,
    bool LockTranslation,
    Vector4 Translation,
    Vector4 Rotation);
