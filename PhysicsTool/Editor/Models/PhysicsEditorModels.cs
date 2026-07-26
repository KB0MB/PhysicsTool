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
    public float? MaximumDistance { get; set; }

    public bool IsEditable => ConstraintSetIndex >= 0 && (LinkIndex >= 0 || LocalConstraintIndex >= 0);
}

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

public sealed class ParticlePreviewData
{
    public List<ParticlePreviewPoint> Particles { get; } = new();
    public List<ParticlePreviewLink> Links { get; } = new();
    public List<ParticlePreviewLocalRange> LocalRanges { get; } = new();
    public List<ParticlePreviewTriangle> Triangles { get; } = new();
    public List<ParticlePreviewBoneBinding> BoneBindings { get; } = new();
    public List<BonePreviewPoint> Bones { get; } = new();
    public List<ColliderPreviewShape> Colliders { get; } = new();
    public Vector3 ViewRoot { get; set; } = Vector3.Zero;
    public bool HasViewRoot { get; set; }
    public Vector3 Gravity { get; set; } = new(0.0f, -9.81f, 0.0f);
    public float DampingPerSecond { get; set; }
}

public sealed class ParticlePreviewLocalRange
{
    public int ParticleIndex { get; set; }
    public float MaximumDistance { get; set; }
}

public sealed class ParticlePreviewPoint
{
    public int Index { get; set; }
    public bool Fixed { get; set; }
    public Vector3 Position { get; set; }
    public float Radius { get; set; }
    public float Mass { get; set; }
    public float InverseMass { get; set; }
    public uint CollisionMask { get; set; }
}

public sealed class ParticlePreviewLink
{
    public int ParticleA { get; set; }
    public int ParticleB { get; set; }
    public string Kind { get; set; } = string.Empty;
    public float? RestLength { get; set; }
    public float? BendMinLength { get; set; }
    public float? StretchMaxLength { get; set; }
    public float? Stiffness { get; set; }
    public float? BendStiffness { get; set; }
    public float? StretchStiffness { get; set; }
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
    public Vector3 Start { get; set; }
    public Vector3 End { get; set; }
    public float Radius { get; set; }
    public float EndRadius { get; set; }
    public Vector3 PlaneNormal { get; set; } = Vector3.UnitY;
    public ColliderPreviewKind Kind { get; set; } = ColliderPreviewKind.Capsule;
}
