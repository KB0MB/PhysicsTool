using BymlLibrary;
using System.Numerics;

namespace HKCLTool;

internal enum PlayerWeaponKind { Sword, LSword, Spear, Shield, Bow }
internal sealed record PlayerWeaponSelection(string Name, bool Equipped = false, string Arrow = "NormalArrow");
internal sealed record PlayerArrowEntry(string Name, string DisplayName)
{
    public override string ToString() => DisplayName;
}
internal sealed record PlayerWeaponEntry(PlayerWeaponKind Kind, string Name, string Path, string? ArchiveEntry = null)
{
    public string DisplayName { get; init; } = Name;
    public override string ToString() => DisplayName;
}

internal sealed class PlayerWeaponCatalog
{
    private readonly PlayerEquipmentSources _sources;
    public IReadOnlyList<PlayerWeaponEntry> Entries { get; }
    public bool SupportsArrowTypes { get; }
    public IReadOnlyList<PlayerArrowEntry> Arrows { get; }
    public PlayerWeaponCatalog(BotwActorResources player, PlayerEquipmentSources? sources = null)
    {
        _sources = sources ?? new(player);
        SupportsArrowTypes = !player.IsTotk;
        Arrows = new PlayerArrowEntry[] { new("NormalArrow", "Arrow"), new("FireArrow", "Fire Arrow"),
            new("IceArrow", "Ice Arrow"), new("ElectricArrow", "Shock Arrow"), new("BombArrow_A", "Bomb Arrow"), new("AncientArrow", "Ancient Arrow") }
            .Where(a => (SupportsArrowTypes || a.Name == "NormalArrow") && _sources.Find(a.Name) != null)
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        var entries = new Dictionary<string, PlayerWeaponEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in Enum.GetValues<PlayerWeaponKind>())
        foreach (var source in _sources.Actors.Values.Where(s => s.Name.StartsWith($"Weapon_{kind}_", StringComparison.OrdinalIgnoreCase)))
        {
            entries.TryAdd(source.Name, new(kind, source.Name, source.Path, source.ArchiveEntry));
        }
        var labels = PlayerEquipmentNames.Labels(entries.Keys, player.IsTotk);
        Entries = entries.Values.Select(e => e with { DisplayName = labels[e.Name] }).OrderBy(e => e.Kind)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public PlayerWeaponEntry Find(PlayerWeaponKind kind, string name) => Entries.FirstOrDefault(e => e.Kind == kind && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"Weapon '{name}' is not available in the {kind} slot of this game dump.");
    public string? ActorPath(string name) => _sources.Find(name)?.Path;
    public PlayerEquipmentSource? ActorSource(string name) => _sources.Find(name);
    public string ArrowActor(string name) => Arrows.FirstOrDefault(a => a.Name == name)?.Name
        ?? throw new InvalidDataException($"Arrow '{name}' is not available in this game dump.");
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
    internal PlayerWeaponParameters ReadWeaponParameters(PlayerWeaponKind kind)
    {
        if (IsTotk) return PlayerWeaponParameters.Read(EquipmentParameters(kind switch
            { PlayerWeaponKind.Bow => "BowRef", PlayerWeaponKind.Shield => "ShieldRef", _ => "WeaponRef" }));
        var root = ReadEquipmentGeneralParameters();
        var common = Objects(root).FirstOrDefault(o => o.Hash == BotwAampWriter.Crc32("WeaponCommon"));
        var values = Objects(root).FirstOrDefault(o => o.Find("PlayerHoldTransOffset") != null);
        var bow = Objects(root).FirstOrDefault(o => o.Find("QuiverName") != null);
        return new()
        {
            DrawTranslation = values?.Find("PlayerEquipTransOffset")?.AsVector3() ?? Vector3.Zero,
            DrawRotation = values?.Find("PlayerEquipRotOffset")?.AsVector3() ?? Vector3.Zero,
            SheathTranslation = values?.Find("PlayerHoldTransOffset")?.AsVector3() ?? Vector3.Zero,
            SheathRotation = values?.Find("PlayerHoldRotOffset")?.AsVector3() ?? Vector3.Zero,
            Scale = common?.Find("PlayerEqScale")?.AsSingle() ?? 1,
            Sheath = values?.Find("PodName")?.AsString() ?? "",
            Quiver = bow?.Find("QuiverName")?.AsString() ?? ""
        };
    }

    private AampReadList? ReadEquipmentGeneralParameters()
    {
        var link = Parameters("ActorLink", ActorName, "bxml");
        var name = Objects(link).FirstOrDefault(o => o.Find("GParamUser") != null)?.Find("GParamUser")?.AsString()
            ?? _entries.Keys.Where(k => k.EndsWith(".bgparamlist", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension).FirstOrDefault();
        return name == null ? null : Parameters("GeneralParamList", name, "bgparamlist");
    }

    internal Dictionary<string, Byml> EquipmentParameters(string component)
    {
        var actor = TotkParameters($"Actor/{ActorName}.engine__actor__ActorParam.bgyml");
        var components = actor["Components"].GetMap();
        return components.TryGetValue(component, out var reference) && !string.IsNullOrEmpty(reference.GetString())
            ? TotkParameters(reference.GetString()) : new();
    }
}
