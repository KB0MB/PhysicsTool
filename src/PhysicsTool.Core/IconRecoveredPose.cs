using System.Numerics;
using System.Text.Json;
using HKCLTool;

namespace PhysicsTool.Core;

internal static class IconRecoveredPose
{
    internal const string Label = "Recovered mannequin pose";
    private sealed record Transform(float[] Rotation, float[] Translation);
    private static readonly Dictionary<string, Dictionary<string, Transform>> Profiles = Read();
    private static Dictionary<string, Dictionary<string, Transform>> Read()
    {
        using var stream = typeof(IconRecoveredPose).Assembly.GetManifestResourceStream("PhysicsTool.HylianMannequinPose.json")
            ?? throw new InvalidDataException("Missing recovered armor pose.");
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Transform>>>(stream)!;
    }
    internal static bool Supports(ToolboxActorModel model) => Part(model.Name) != null && model.RestPose.Bones.Any(b => b.Name == "Waist")
        && model.RestPose.Bones.Any(b => b.Name == "Skl_Root");
    private static string? Part(string name) => !name.StartsWith("Armor_", StringComparison.Ordinal) ? null
        : name.EndsWith("_Upper", StringComparison.Ordinal) ? "Upper" : name.EndsWith("_Lower", StringComparison.Ordinal) ? "Lower" : null;
    internal static bool TryLocal(string model, string bone, Matrix4x4 rest, out Matrix4x4 local)
    {
        local = rest;
        var part = Part(model);
        // Garment-specific joints cannot be transferred to unrelated armor cuts.
        if (part == null || !model.StartsWith("Armor_001_", StringComparison.Ordinal) &&
            (bone.Contains("_Armor", StringComparison.Ordinal) || bone.StartsWith("Belt_", StringComparison.Ordinal) || bone.StartsWith("Pod_", StringComparison.Ordinal))) return false;
        if (!Profiles[part].TryGetValue(bone, out var t)) return false;
        var q = t.Rotation; var p = t.Translation;
        var position = rest.Translation + new Vector3(p[0], p[1], p[2]);
        local.Translation = Vector3.Zero;
        local *= Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(new(q[0], q[1], q[2], q[3])));
        local.Translation = position;
        return true;
    }
}
