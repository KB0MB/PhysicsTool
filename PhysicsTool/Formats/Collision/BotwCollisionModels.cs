using System.Numerics;

namespace HKCLTool;

internal enum BotwCollisionTarget
{
    StaticCompound,
    ActorRigidBody
}

internal enum BotwCollisionPlatform
{
    WiiU,
    Switch
}

internal sealed class BotwCollisionMesh
{
    public required string Name { get; init; }
    public required IReadOnlyList<Vector3> Vertices { get; init; }
    public required IReadOnlyList<int> Indices { get; init; }
    public string Material { get; set; } = BotwCollisionMaterials.Default;
    public bool Included { get; set; } = true;
    public int TriangleCount => Indices.Count / 3;
}

internal static class BotwCollisionMaterials
{
    public const string Default = "Material_Wood";

    public static IReadOnlyList<string> Names { get; } =
    [
        Default,
        "Material_Soil", "Material_Stone", "Material_Sand", "Material_Metal",
        "Material_WireNet", "Material_Grass", "Material_Water",
        "Material_Snow", "Material_Ice", "Material_Lava", "Material_Bog",
        "Material_HeavySand", "Material_Cloth", "Material_Glass", "Material_Bone",
        "Material_Rope", "Material_CharControl", "Material_Ragdoll",
        "Material_Surfing", "Material_GuardianFoot", "Material_HeavySnow",
        "Material_Unused0", "Material_LaunchPad", "Material_Conveyer",
        "Material_Rail", "Material_Grudge", "Material_Meat", "Material_Vegetable",
        "Material_Bomb", "Material_MagicBall", "Material_Barrier",
        "Material_AirWall", "Material_Misc", "Material_GrudgeSlow"
    ];

    public static string Infer(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Default;

        foreach (var material in Names.Skip(1))
        {
            var shortName = material["Material_".Length..];
            if (name.Contains($"[{shortName}]", StringComparison.OrdinalIgnoreCase))
                return material;
        }
        return Default;
    }
}

internal sealed record BotwCollisionBuildOptions(
    string OutputPath,
    float Scale,
    Vector3 Offset,
    bool Yaz0Compress,
    BotwCollisionPlatform Platform = BotwCollisionPlatform.WiiU,
    BotwCollisionTarget Target = BotwCollisionTarget.StaticCompound,
    string? RigidBodyName = null);
