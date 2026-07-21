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
    public int CollisionMask { get; set; }
}

public sealed class ParticleRelationshipRow
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Particles { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

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
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BoneIndex { get; set; }
    public string BoneName { get; set; } = string.Empty;
    public float StartX { get; set; }
    public float StartY { get; set; }
    public float StartZ { get; set; }
    public float EndX { get; set; }
    public float EndY { get; set; }
    public float EndZ { get; set; }
    public float Radius { get; set; }

    // The editor exposes world-space endpoints. HKCL stores shape points in
    // this transform's local space, so the service bakes between the two.
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
}

public sealed class ParticlePreviewData
{
    public List<ParticlePreviewPoint> Particles { get; } = new();
    public List<ParticlePreviewLink> Links { get; } = new();
    public List<ParticlePreviewTriangle> Triangles { get; } = new();
    public List<BonePreviewPoint> Bones { get; } = new();
    public List<ColliderPreviewShape> Colliders { get; } = new();
    public Vector3 ViewRoot { get; set; } = Vector3.Zero;
    public bool HasViewRoot { get; set; }
}

public sealed class ParticlePreviewPoint
{
    public int Index { get; set; }
    public bool Fixed { get; set; }
    public Vector3 Position { get; set; }
    public float Radius { get; set; }
}

public sealed class ParticlePreviewLink
{
    public int ParticleA { get; set; }
    public int ParticleB { get; set; }
    public string Kind { get; set; } = string.Empty;
}

public sealed class ParticlePreviewTriangle
{
    public int ParticleA { get; set; }
    public int ParticleB { get; set; }
    public int ParticleC { get; set; }
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
    public string Name { get; set; } = string.Empty;
    public int BoneIndex { get; set; }
    public Vector3 Start { get; set; }
    public Vector3 End { get; set; }
    public float Radius { get; set; }
    public float EndRadius { get; set; }
    public Vector3 PlaneNormal { get; set; } = Vector3.UnitY;
    public ColliderPreviewKind Kind { get; set; } = ColliderPreviewKind.Capsule;
}
