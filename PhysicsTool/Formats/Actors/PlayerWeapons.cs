using BymlLibrary;
using System.Numerics;

namespace HKCLTool;

internal enum PlayerWeaponKind { Sword, LSword, Spear, Shield, Bow }
internal sealed record PlayerWeaponSelection(string Name, bool Equipped = false);
internal sealed record PlayerWeaponEntry(PlayerWeaponKind Kind, string Name, string Path)
{
    public string DisplayName => Name[("Weapon_" + Kind + "_").Length..];
    public override string ToString() => DisplayName;
}

internal sealed class PlayerWeaponCatalog
{
    private readonly string[] _folders;
    public IReadOnlyList<PlayerWeaponEntry> Entries { get; }
    public PlayerWeaponCatalog(BotwActorResources player)
    {
        _folders = new[] { System.IO.Path.GetDirectoryName(player.SourcePath)! }
            .Concat(player.Roots.Select(r => System.IO.Path.Combine(r, "Pack", "Actor")))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists).ToArray();
        var entries = new Dictionary<string, PlayerWeaponEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _folders)
        foreach (var kind in Enum.GetValues<PlayerWeaponKind>())
        foreach (var path in Directory.EnumerateFiles(folder, $"Weapon_{(kind == PlayerWeaponKind.LSword ? "Lsword" : kind.ToString())}_*.pack.zs"))
        {
            var name = System.IO.Path.GetFileName(path)[..^8];
            entries.TryAdd(name, new(kind, name, path));
        }
        Entries = entries.Values.OrderBy(e => e.Kind).ThenBy(e => Number(e.DisplayName))
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static int Number(string value) => int.TryParse(value.Split('_')[0], out var n) ? n : int.MaxValue;
    public PlayerWeaponEntry Find(PlayerWeaponKind kind, string name) => Entries.FirstOrDefault(e => e.Kind == kind && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"Weapon '{name}' is not available in the {kind} slot of this game dump.");
    public string? ActorPath(string name) => _folders.Select(f => System.IO.Path.Combine(f, name + ".pack.zs")).FirstOrDefault(File.Exists);
}

internal sealed record PlayerEquipmentAttachment(string Actor, string Bone, Matrix4x4 Offset);

internal sealed class PlayerWeaponParameters
{
    public Vector3 DrawTranslation { get; init; }
    public Vector3 DrawRotation { get; init; }
    public Vector3 SheathTranslation { get; init; }
    public Vector3 SheathRotation { get; init; }
    public float Scale { get; init; } = 1;
    public string Sheath { get; init; } = "";
    public string Quiver { get; init; } = "";
    public static PlayerWeaponParameters Read(Dictionary<string, Byml> map) => new()
    {
        DrawTranslation = Vector(map, "DrawTransOffset"), DrawRotation = Vector(map, "DrawRotateOffset"),
        SheathTranslation = Vector(map, "SheathTransOffset"), SheathRotation = Vector(map, "SheathRotateOffset"),
        Scale = map.TryGetValue("PlayerEquipScale", out var scale) ? scale.GetFloat() : 1,
        Sheath = ActorName(map, "SheathName"), Quiver = ActorName(map, "QuiverName")
    };
    internal static Vector3 Vector(Dictionary<string, Byml> map, string key)
    {
        if (!map.TryGetValue(key, out var value)) return Vector3.Zero;
        var v = value.GetMap();
        float Component(string name) => v.TryGetValue(name, out var n) ? n.GetFloat() : 0;
        return new(Component("X"), Component("Y"), Component("Z"));
    }
    private static string ActorName(Dictionary<string, Byml> map, string key)
    {
        if (!map.TryGetValue(key, out var value)) return "";
        var name = value.GetString().Replace('\\', '/').Split('/').Last().Split('.')[0];
        if (name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) throw new InvalidDataException("Invalid equipment actor reference.");
        return name;
    }
    public Matrix4x4 Offset(bool equipped) => Transform(equipped ? DrawTranslation : SheathTranslation,
        equipped ? DrawRotation : SheathRotation, Scale);
    internal static Matrix4x4 Transform(Vector3 translation, Vector3 degrees, float scale = 1)
    {
        if (!float.IsFinite(translation.LengthSquared()) || !float.IsFinite(degrees.LengthSquared()) || !float.IsFinite(scale) || scale <= 0)
            throw new InvalidDataException("Equipment has an invalid attachment transform.");
        var r = degrees * (MathF.PI / 180);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) *
            Matrix4x4.CreateRotationZ(r.Z) * Matrix4x4.CreateTranslation(translation);
    }
    public static string Bone(PlayerWeaponKind kind, bool equipped) => !equipped ? "Pod_A" :
        kind is PlayerWeaponKind.Bow or PlayerWeaponKind.Shield ? "Weapon_L" : "Weapon_R";
}

internal sealed partial class BotwActorResources
{
    internal Dictionary<string, Byml> EquipmentParameters(string component)
    {
        var actor = TotkParameters($"Actor/{ActorName}.engine__actor__ActorParam.bgyml");
        var components = actor["Components"].GetMap();
        return components.TryGetValue(component, out var reference) && !string.IsNullOrEmpty(reference.GetString())
            ? TotkParameters(reference.GetString()) : new();
    }
}
