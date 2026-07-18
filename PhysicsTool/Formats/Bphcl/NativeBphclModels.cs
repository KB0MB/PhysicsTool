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
    IReadOnlyList<NativeBphclSimCloth> SimCloths)
{
    public int SimClothCount => SimCloths.Count;
}

public sealed record NativeBphclSimCloth(
    int Index,
    string Name,
    int ItemIndex,
    uint DataOffset,
    IReadOnlyList<NativeBphclParticle> Particles,
    IReadOnlyList<NativeBphclConstraintSet> ConstraintSets,
    IReadOnlyList<int> CollidableItemIndices)
{
    public int ConstraintSetCount => ConstraintSets.Count;
}

public sealed record NativeBphclConstraintSet(
    int Index,
    string Name,
    string ClassName,
    uint ConstraintId,
    uint ConstraintType,
    int ItemIndex,
    uint DataOffset,
    IReadOnlyList<NativeBphclConstraintLink> Links)
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
