using System.Numerics;

namespace HKCLTool;

// UI-facing rows and viewport geometry. These stay independent from the
// native HKCL/BPHCL object graphs so the editor can present either format.
public sealed class ParticleEditRow
{
    public int Index { get; set; }
    public bool Fixed { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }
    public float Mass { get; set; }
    public float InverseMass { get; set; }
    public float Radius { get; set; }
    public float Friction { get; set; }
    public uint CollisionMask { get; set; }
}

// Values owned by one simulation cloth rather than an individual particle.
// Damping is the primary control for how quickly a chain loses motion.
public sealed class ClothSimulationSettings
{
    public float GravityX { get; set; }
    public float GravityY { get; set; } = -9.81f;
    public float GravityZ { get; set; }
    public float DampingPerSecond { get; set; }
    public float CollisionTolerance { get; set; }
    public bool TransferTranslationMotion { get; set; }
    public float MinTranslationSpeed { get; set; }
    public float MaxTranslationSpeed { get; set; }
    public float MinTranslationBlend { get; set; }
    public float MaxTranslationBlend { get; set; }
    public bool TransferRotationMotion { get; set; }
    public float MinRotationSpeed { get; set; }
    public float MaxRotationSpeed { get; set; }
    public float MinRotationBlend { get; set; }
    public float MaxRotationBlend { get; set; }
}

public sealed class ParticleRelationshipRow
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Particles { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public int ConstraintSetIndex { get; set; } = -1;
    public int LinkIndex { get; set; } = -1;
    public int LocalConstraintIndex { get; set; } = -1;
    public int ParticleA { get; set; } = -1;
    public int ParticleB { get; set; } = -1;
    public float? RestLength { get; set; }
    public float? BendMinLength { get; set; }
    public float? StretchMaxLength { get; set; }
    public float? Stiffness { get; set; }
    public float? BendStiffness { get; set; }
    public float? StretchStiffness { get; set; }
    public float? CompressionLength { get; set; }
    public float? MaximumDistance { get; set; }

    public bool IsEditable => ConstraintSetIndex >= 0 && (LinkIndex >= 0 || LocalConstraintIndex >= 0);
}

// Used by format adapters and exporters. The UI continues to show the
// authored set name, while serializers need the actual Havok runtime class.
public sealed record ConstraintSetSummary(
    int Index,
    string ClassName,
    string Name,
    uint ConstraintId = 0,
    uint ConstraintType = 0);
// Bend-stiffness links use four particles and carry geometric weights. They
// are serializer data rather than normal two-particle editor relationships.
public sealed record BendStiffnessConstraintSetData(
    int ConstraintSetIndex,
    string Name,
    uint ConstraintId,
    uint ConstraintType,
    bool ClampBendStiffness,
    float MaxRestPoseHeightSq,
    bool UseRestPoseConfig,
    IReadOnlyList<BendStiffnessConstraintData> Links);

public sealed record BendStiffnessConstraintData(
    int LinkIndex,
    int ParticleA,
    int ParticleB,
    int ParticleC,
    int ParticleD,
    float BendStiffness,
    float RestCurvature,
    float WeightA,
    float WeightB,
    float WeightC,
    float WeightD);

// Transition constraints are a distinct solver phase. They are not links:
// each record controls one particle's handoff between animation and the cloth
// solver, so standalone conversion must retain them separately.
public sealed record TransitionConstraintSetData(
    int ConstraintSetIndex,
    string Name,
    uint ConstraintId,
    uint ConstraintType,
    float ToAnimationPeriod,
    float ToAnimationPlusDelayPeriod,
    float ToSimulationPeriod,
    float ToSimulationPlusDelayPeriod,
    uint ReferenceMeshBufferIndex,
    IReadOnlyList<TransitionConstraintParticleData> Particles);

public sealed record TransitionConstraintParticleData(
    int Index,
    int ParticleIndex,
    int ReferenceVertex,
    float ToAnimationDelay,
    float ToSimulationDelay,
    float ToSimulationMaximumDistance);

// Local-range records keep simulated particles near their skinned reference
// vertices. In BPHCL, maximumDistance is stored as a shape radius, while the
// normal limits let a range be one-sided along the surface normal.
public sealed record LocalRangeConstraintSetData(
    int ConstraintSetIndex,
    string Name,
    uint ConstraintId,
    uint ConstraintType,
    uint ReferenceMeshBufferIndex,
    float Stiffness,
    int ShapeType,
    bool ApplyNormalComponent,
    IReadOnlyList<LocalRangeConstraintData> Constraints);

public sealed record LocalRangeConstraintData(
    int Index,
    int ParticleIndex,
    int ReferenceVertex,
    float ShapeRadius,
    float MaxNormalDistance,
    float MinNormalDistance,
    string RecordTypeName = "hclLocalRangeConstraintSet::LocalConstraint",
    float? RecordStiffness = null);

// Bone planes constrain individual particles against a plane expressed in a
// cloth transform-set bone's local space. They are neither links nor ranges,
// so native conversion retains their records separately.
public sealed record BonePlaneConstraintSetData(
    int ConstraintSetIndex,
    string Name,
    uint ConstraintId,
    uint ConstraintType,
    uint TransformSetIndex,
    IReadOnlyList<BonePlaneConstraintData> Planes);

public sealed record BonePlaneConstraintData(
    int Index,
    Vector4 PlaneEquationBone,
    int ParticleIndex,
    int TransformIndex,
    float Stiffness);

// Volume constraints preserve a cloth's enclosed volume with separate frame
// and application records. They use their own reflected BPHCL layout.
public sealed record VolumeConstraintSetData(
    int ConstraintSetIndex,
    string Name,
    uint ConstraintId,
    uint ConstraintType,
    IReadOnlyList<VolumeConstraintFrameData> Frames,
    IReadOnlyList<VolumeConstraintApplyData> Applies);

public sealed record VolumeConstraintFrameData(
    int Index,
    Vector4 FrameVector,
    int ParticleIndex,
    float Weight);

public sealed record VolumeConstraintApplyData(
    int Index,
    Vector4 FrameVector,
    int ParticleIndex,
    float Stiffness);

// SimpleMeshBoneDeform turns the simulated virtual-cloth surface back into
// animated skeleton transforms. The offsets are native Havok byte offsets:
// 64 bytes per bone matrix and 6 bytes per triangle index triplet.
public sealed record SimpleMeshBoneDeformData(
    int OperatorIndex,
    int InputBufferIndex,
    int OutputTransformSetIndex,
    int BoneAxis,
    IReadOnlyList<SimpleMeshTriangleBonePairData> TriangleBonePairs,
    IReadOnlyList<Matrix4x4> LocalBoneTransforms);

public sealed record SimpleMeshTriangleBonePairData(
    ushort BoneOffset,
    ushort TriangleOffset);

// Input skinning moves animated skeleton transforms into the virtual cloth
// buffer before simulation. Keep the packed blend topology explicit so native
// BPHCL output does not need a runtime donor or guessed weight records.
public sealed record SkinOperatorData(
    int OperatorIndex,
    string ClassName,
    int OutputBufferIndex,
    int TransformSetIndex,
    IReadOnlyList<Matrix4x4> BoneFromSkinMeshTransforms,
    IReadOnlyList<ushort> TransformSubset,
    IReadOnlyList<SkinBlendArrayData> BlendArrays,
    IReadOnlyList<byte> ControlBytes,
    ushort StartVertexIndex,
    ushort EndVertexIndex,
    ushort BatchSizeSpu,
    bool PartialWrite,
    IReadOnlyList<SkinPositionBlockData> LocalPositionBlocks,
    IReadOnlyList<SkinPositionBlockData> LocalUnpackedPositionBlocks);

public sealed record SkinBlendArrayData(
    string MemberName,
    int BlendCount,
    int VertexCount,
    int BoneCount,
    IReadOnlyList<SkinBlendBlockData> Blocks);

public sealed record SkinBlendBlockData(
    IReadOnlyList<ushort> VertexIndices,
    IReadOnlyList<ushort> BoneIndices,
    IReadOnlyList<byte> BoneWeights);

// Native object-space BPHCL localPs data is already packed as 64 int16 values.
// Preserve it for native graph rebuilds; generated HKCL conversions use Positions.
public sealed record SkinPositionBlockData(
    IReadOnlyList<Vector4> Positions,
    IReadOnlyList<short>? NativePackedValues = null);

// Retains the source Havok execution graph for conversion. This is kept out
// of the UI model because it describes file-runtime plumbing, not authoring
// controls, and lets native BPHCL output preserve source routing exactly.
public sealed record ClothRuntimeData(
    IReadOnlyList<ClothBufferDefinitionData> Buffers,
    IReadOnlyList<ClothTransformSetDefinitionData> TransformSets,
    IReadOnlyList<ClothOperatorData> Operators,
    IReadOnlyList<ClothStateRuntimeData> States);

public sealed record ClothBufferDefinitionData(
    int Index,
    string ClassName,
    string MeshName,
    string BufferName,
    int Type,
    int SubType,
    int VertexCount,
    int TriangleCount,
    IReadOnlyList<ushort> TriangleIndices);

public sealed record ClothTransformSetDefinitionData(
    int Index,
    string Name,
    int Type,
    int TransformCount);

public sealed record ClothOperatorData(
    int Index,
    string ClassName,
    int? InputBufferIndex,
    int? OutputBufferIndex,
    int? TransformSetIndex,
    int? VertexCount,
    int? StartVertexIn,
    int? StartVertexOut,
    IReadOnlyList<short> VertexInputFromVertexOutput,
    bool GatherNormals,
    bool PartialGather,
    IReadOnlyList<ClothVertexParticlePairData> VertexParticlePairs,
    int? SimulationClothIndex,
    int? ReferenceBufferIndex,
    IReadOnlyList<ClothSimulateConfigData> SimulateConfigs,
    IReadOnlyList<ClothStateTransformSetAccessData>? OperatorTransformSetAccesses = null,
    string? Name = null);

public sealed record ClothVertexParticlePairData(ushort VertexIndex, ushort ParticleIndex);

public sealed record ClothSimulateConfigData(
    IReadOnlyList<int> ConstraintExecution,
    IReadOnlyList<int> InstanceCollidablesUsed,
    byte SubSteps,
    byte NumberOfSolveIterations,
    bool UseAllInstanceCollidables,
    bool AdaptConstraintStiffness);

public sealed record ClothStateRuntimeData(
    int Index,
    string Name,
    IReadOnlyList<int> OperatorIndices,
    IReadOnlyList<int> BufferIndices,
    IReadOnlyList<int> TransformSetIndices,
    IReadOnlyList<int> SimulationIndices,
    IReadOnlyList<ClothStateBufferAccessData>? BufferAccesses = null,
    IReadOnlyList<ClothStateTransformSetAccessData>? TransformSetAccesses = null);

// hclClothState keeps these structures inline. The short index lists are
// useful to the editor, but the access flags and transform trackers are
// required by Havok when a state starts or resumes simulation.
public sealed record ClothStateBufferAccessData(
    int BufferIndex,
    byte ComponentFlags0,
    byte ComponentFlags1,
    byte ComponentFlags2,
    byte ComponentFlags3,
    bool TrianglesRead,
    int ShadowBufferIndex);

public sealed record ClothStateBitFieldData(
    IReadOnlyList<uint> Words,
    uint NumBits);

public sealed record ClothStateTransformTrackerData(
    ClothStateBitFieldData Read,
    ClothStateBitFieldData ReadBeforeWrite,
    ClothStateBitFieldData Written);

public sealed record ClothStateTransformSetAccessData(
    int TransformSetIndex,
    byte ComponentFlags0,
    byte ComponentFlags1,
    IReadOnlyList<ClothStateTransformTrackerData> TransformTrackers);

// Collision masks are stored as bits in the native formats. The editor uses
// these options to expose the cloth-local collider list instead of raw bits.
public sealed record ParticleColliderOption(int BitIndex, int ColliderIndex, string Name);

public sealed class BoneEditRow
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ParentIndex { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float RotationW { get; set; }
    public float ScaleX { get; set; } = 1.0f;
    public float ScaleY { get; set; } = 1.0f;
    public float ScaleZ { get; set; } = 1.0f;

    // HKCL/BPHCL reference poses are three Vector4 rows. The editor exposes
    // only author-facing XYZ/quaternion data, but fresh BPHCL exports must
    // retain the row W values because native skeletons use them at runtime.
    public float TranslationW { get; set; }
    public float ScaleW { get; set; }
}

public sealed class ColliderEditRow
{
    // The per-instance transform map belongs to a cloth, not the global
    // collidable object. Keep its owner so edits stay scoped to that cloth.
    public int ClothIndex { get; set; } = -1;
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ShapeType { get; set; } = "Capsule";
    public int BoneIndex { get; set; }
    public string BoneName { get; set; } = string.Empty;
    public float StartX { get; set; }
    public float StartY { get; set; }
    public float StartZ { get; set; }
    public float EndX { get; set; }
    public float EndY { get; set; }
    public float EndZ { get; set; }
    public float Radius { get; set; }
    // Plane colliders have no endpoints or radius. The editor keeps their
    // world normal separately so viewport transforms redraw the real plane.
    public float PlaneNormalX { get; set; }
    public float PlaneNormalY { get; set; } = 1.0f;
    public float PlaneNormalZ { get; set; }

    public bool IsPlane => string.Equals(ShapeType, "Plane", StringComparison.Ordinal);

    // The editor exposes world-space endpoints. HKCL stores shape points in
    // this transform's local space, so the service bakes between the two.
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
}

public sealed class HkrgConstraintEditRow
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ConstraintType { get; set; } = string.Empty;
    public int BodyA { get; set; } = -1;
    public int BodyB { get; set; } = -1;
    public string BodyAName { get; set; } = string.Empty;
    public string BodyBName { get; set; } = string.Empty;
    public float? TwistMinAngle { get; set; }
    public float? TwistMaxAngle { get; set; }
    public float? TwistTau { get; set; }
    public float? TwistDamping { get; set; }
    public float? ConeMinAngle { get; set; }
    public float? ConeMaxAngle { get; set; }
    public float? ConeTau { get; set; }
    public float? ConeDamping { get; set; }
    public float? PlaneMinAngle { get; set; }
    public float? PlaneMaxAngle { get; set; }
    public float? PlaneTau { get; set; }
    public float? PlaneDamping { get; set; }
    public float? HingeMinAngle { get; set; }
    public float? HingeMaxAngle { get; set; }
    public float? HingeTau { get; set; }
    public float? HingeDamping { get; set; }
    public float? AngularFriction { get; set; }
}
public enum ClothSolverProfile { BotwPreview, TotkExperimental }

public sealed class ParticlePreviewData
{
    public ClothSolverProfile SolverProfile { get; set; }
    public List<ParticlePreviewPoint> Particles { get; } = new();
    public List<ParticlePreviewAttachment> ParticleAttachments { get; } = new();
    public List<ParticlePreviewConstraintSet> ConstraintSets { get; } = new();
    public List<ParticlePreviewLink> Links { get; } = new();
    public List<ParticlePreviewLocalRange> LocalRanges { get; } = new();
    public List<ParticlePreviewBendStiffness> BendStiffnessConstraints { get; } = new();
    public List<ParticlePreviewTriangle> Triangles { get; } = new();
    public List<ParticlePreviewBoneBinding> BoneBindings { get; } = new();
    public List<BonePreviewPoint> Bones { get; } = new();
    public List<ColliderPreviewShape> Colliders { get; } = new();
    public List<RagdollConstraintPreviewLink> RagdollConstraints { get; } = new();
    public Vector3 ViewRoot { get; set; } = Vector3.Zero;
    public bool HasViewRoot { get; set; }
    public Vector3 Gravity { get; set; } = new(0.0f, -9.81f, 0.0f);
    public float DampingPerSecond { get; set; }
    public float CollisionTolerance { get; set; }
    public List<int> ConstraintExecution { get; } = new();
    public bool PinchDetectionEnabled { get; set; }
    public bool HasAntiPinchConstraints { get; set; }
    public HashSet<int> PinchEnabledParticles { get; } = new();
    public List<ParticlePreviewColliderPinch> ColliderPinching { get; } = new();
    public int SimulationSubSteps { get; set; } = 1;
    public int SimulationSolveIterations { get; set; } = 1;
    public bool AdaptConstraintStiffness { get; set; }
    public bool UseNativeHkclInputSampling { get; set; }
    public bool TransferMotionEnabled { get; set; }
    public int TransferMotionTransformSetIndex { get; set; }
    public int TransferMotionTransformIndex { get; set; }
    public bool TransferTranslationMotion { get; set; }
    public float MinTranslationSpeed { get; set; }
    public float MaxTranslationSpeed { get; set; }
    public float MinTranslationBlend { get; set; }
    public float MaxTranslationBlend { get; set; }
    public bool TransferRotationMotion { get; set; }
    public float MinRotationSpeed { get; set; }
    public float MaxRotationSpeed { get; set; }
    public float MinRotationBlend { get; set; }
    public float MaxRotationBlend { get; set; }
}

public sealed class ParticlePreviewConstraintSet
{
    public int Index { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed record ParticlePreviewColliderPinch(int CollisionBit, bool Enabled, int Priority, float Radius);

// hclMoveParticlesOperator copies selected reference-buffer vertices into the
// simulation. These weights identify the animated skeleton transforms that
// produced each copied vertex, so fixed particles can follow the model pose.
public sealed class ParticlePreviewAttachment
{
    public int ParticleIndex { get; set; }
    public List<ParticlePreviewBoneWeight> BoneWeights { get; } = new();
}

public sealed class ParticlePreviewBoneWeight
{
    public int BoneIndex { get; set; }
    public float Weight { get; set; }
    // The authored reference vertex in this influence's bind-world frame.
    public Vector3? ReferencePosition { get; set; }
}


public sealed class RagdollConstraintPreviewLink
{
    public int Index { get; set; }
    public int BodyA { get; set; }
    public int BodyB { get; set; }
    public int BoneA { get; set; } = -1;
    public int BoneB { get; set; } = -1;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}
public sealed class ParticlePreviewLocalRange
{
    public int ConstraintSetIndex { get; set; } = -1;
    public int ParticleIndex { get; set; }
    public int ReferenceVertex { get; set; } = -1;
    public int ReferenceBufferIndex { get; set; }
    public List<ParticlePreviewBoneWeight> ReferenceBoneWeights { get; } = new();
    public float MaximumDistance { get; set; }
    public float MinNormalDistance { get; set; } = float.NegativeInfinity;
    public float MaxNormalDistance { get; set; } = float.PositiveInfinity;
    public float Stiffness { get; set; } = 1.0f;
    public bool ApplyNormalComponent { get; set; }
}

public sealed class ParticlePreviewPoint
{
    public int Index { get; set; }
    public bool Fixed { get; set; }
    public Vector3 Position { get; set; }
    public float Radius { get; set; }
    public float Mass { get; set; }
    public float InverseMass { get; set; }
    public float Friction { get; set; }
    public uint CollisionMask { get; set; }
}

public sealed class ParticlePreviewLink
{
    public int ConstraintSetIndex { get; set; } = -1;
    public int ParticleA { get; set; }
    public int ParticleB { get; set; }
    public string Kind { get; set; } = string.Empty;
    public float? RestLength { get; set; }
    public float? CompressionLength { get; set; }
    public float? BendMinLength { get; set; }
    public float? StretchMaxLength { get; set; }
    public float? Stiffness { get; set; }
    public float? BendStiffness { get; set; }
    public float? StretchStiffness { get; set; }
}

public sealed class ParticlePreviewBendStiffness
{
    public bool UseRestPoseConfig { get; set; }
    public bool ClampBendStiffness { get; set; }
    public float MaxRestPoseHeightSq { get; set; }
    public int ConstraintSetIndex { get; set; } = -1;
    public int ParticleA { get; set; }
    public int ParticleB { get; set; }
    public int ParticleC { get; set; }
    public int ParticleD { get; set; }
    public float WeightA { get; set; }
    public float WeightB { get; set; }
    public float WeightC { get; set; }
    public float WeightD { get; set; }
    public float BendStiffness { get; set; }
    public float RestCurvature { get; set; }
}

public sealed class ParticlePreviewTriangle
{
    public int ParticleA { get; set; }
    public int ParticleB { get; set; }
    public int ParticleC { get; set; }
}

public sealed class ParticlePreviewBoneBinding
{
    public int BoneIndex { get; set; }
    public int ParticleA { get; set; }
    public int ParticleB { get; set; }
    public int ParticleC { get; set; }
    public int BoneAxis { get; set; } = 3;
    public bool UseNativeSimpleDeformer { get; set; }
    public Matrix4x4 LocalBoneTransform { get; set; } = Matrix4x4.Identity;
}

// A runtime-only pose used by the preview solver. It is never written back to HKCL.
public sealed class SimulatedBonePreviewPose
{
    public Vector3 Position { get; set; }
    public Vector3 AxisX { get; set; } = Vector3.UnitX;
    public Vector3 AxisY { get; set; } = Vector3.UnitY;
    public Vector3 AxisZ { get; set; } = Vector3.UnitZ;
    public float StretchScale { get; set; } = 1.0f;
}

public sealed class BonePreviewPoint
{
    public int Index { get; set; }
    public int ParentIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    // Some formats carry a complete visual skeleton alongside the smaller
    // physics skeleton. Reference bones are drawn for orientation, but cannot
    // be picked or transformed through the physics editor.
    public bool IsReference { get; set; }
    // Index used by the active editor tab. HKRG can show model and ragdoll
    // skeletons together, each with its own compact 0-based index range.
    public int EditorIndex { get; set; } = -1;
    public bool IsSelectable { get; set; } = true;
    public Vector3 Position { get; set; }
    public Vector3 AxisX { get; set; } = Vector3.UnitX;
    public Vector3 AxisY { get; set; } = Vector3.UnitY;
    public Vector3 AxisZ { get; set; } = Vector3.UnitZ;
    public float StretchScale { get; set; } = 1.0f;
}

public enum ColliderPreviewKind
{
    Capsule,
    Sphere,
    TaperedCapsule,
    Plane,
    Point
}

public sealed class ColliderPreviewShape
{
    public int Index { get; set; }
    public int CollisionBit { get; set; } = -1;
    public string Name { get; set; } = string.Empty;
    public int BoneIndex { get; set; }
    // Authored rigid-body origin, which need not be the attached bone or shape center.
    public Vector3? TransformOrigin { get; set; }
    public Vector3 Start { get; set; }
    public Vector3 End { get; set; }
    public float Radius { get; set; }
    public float EndRadius { get; set; }
    public Vector3 PlaneNormal { get; set; } = Vector3.UnitY;
    public ColliderPreviewKind Kind { get; set; } = ColliderPreviewKind.Capsule;
}

