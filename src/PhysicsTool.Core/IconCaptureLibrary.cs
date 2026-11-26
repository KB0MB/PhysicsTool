using System.Numerics;
using System.Text.Json;
using HKCLTool;

namespace PhysicsTool.Core;

internal static class IconCaptureLibrary
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    internal static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Presets", "TotkIconCapture.json");

    internal static IReadOnlyList<IconCapturePreset> Load(string? path = null)
    {
        var presets = JsonSerializer.Deserialize<IconCapturePreset[]>(File.ReadAllBytes(path ?? DefaultPath), Json)
            ?? throw new InvalidDataException("The capture preset library is empty.");
        foreach (var p in presets)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.Name) || !Finite(p.Rotation) ||
                p.CameraPosition is { } c && !Finite(c) || p.LightDirection is { } d && !Finite(d) ||
                p.FieldOfView is { } f && (!float.IsFinite(f) || f < .1f || f > 120) || p.Size < 32 || p.Size > 4096)
                throw new InvalidDataException("The capture preset library contains an invalid value.");
        }
        return presets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static void Extract(string root, string output)
    {
        root = IconCaptureAsset.Root(root);
        var presets = new List<IconCapturePreset>();
        foreach (var actor in IconCaptureAsset.Catalog(root).Where(a => a.Name.StartsWith("Armor_", StringComparison.Ordinal) || a.Name.StartsWith("Weapon_", StringComparison.Ordinal)))
        {
            var resources = new BotwActorResources(actor.Path, [root]);
            presets.AddRange(IconCaptureAsset.ReadCapturePresets(resources));
        }
        if (presets.Count == 0) throw new InvalidDataException("No weapon or armor capture presets were found.");
        AtomicFile.WriteAllBytes(output, JsonSerializer.SerializeToUtf8Bytes(presets, Json));
        Console.WriteLine($"Saved {presets.Count} weapon/armor capture presets to {output}");
    }

    internal static string Category(string name) => name.StartsWith("Armor_", StringComparison.Ordinal) ? "Armor" :
        name.StartsWith("Weapon_", StringComparison.Ordinal) ? "Weapon" : "";

    internal static (int Order, string Label) ActorType(string name)
    {
        var parts = name.Split('_');
        if (parts.Length < 3) return (9, "Other actors");
        return (parts[0], parts[0] == "Armor" ? parts[2] : parts[1]) switch
        {
            ("Armor", "Head") => (0, "Headgear"),
            ("Armor", "Upper") => (1, "Upper armor"),
            ("Armor", "Lower") => (2, "Lower armor"),
            ("Weapon", "Sword") => (3, "One-handed weapons"),
            ("Weapon", "Lsword") => (4, "Two-handed weapons"),
            ("Weapon", "Spear") => (5, "Spears"),
            ("Weapon", "Bow") => (6, "Bows"),
            ("Weapon", "Shield") => (7, "Shields"),
            ("Weapon", _) => (8, "Other weapons"),
            _ => (9, "Other actors")
        };
    }

    internal static IReadOnlyList<IconCapturePreset> ForActor(IEnumerable<IconCapturePreset> source, string? actor)
    {
        string category = Category(actor ?? "");
        var result = new List<IconCapturePreset>();
        foreach (var preset in source.Where(p => p.Name != "Manual" && (category.Length == 0 || Category(p.Name) == category))
            .DistinctBy(p => p.Name).OrderBy(p => p.Name, StringComparer.Ordinal))
            if (!result.Any(p => SameArmorCapture(p, preset))) result.Add(preset);
        return result.OrderBy(p => p.ToString(), StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.Ordinal).ToArray();
    }

    internal static IconCapturePreset? Resolve(IEnumerable<IconCapturePreset> choices, IconCapturePreset? requested) => requested == null ? null :
        choices.FirstOrDefault(p => p.Name == requested.Name) ?? choices.FirstOrDefault(p => SameArmorCapture(p, requested));

    private static bool SameArmorCapture(IconCapturePreset a, IconCapturePreset b) =>
        Category(a.Name) == "Armor" && Category(b.Name) == "Armor" && !string.IsNullOrEmpty(a.ModelKey) && a.ModelKey == b.ModelKey &&
        string.Join('_', a.Name.Split('_').Skip(2)) == string.Join('_', b.Name.Split('_').Skip(2)) &&
        a.Rotation == b.Rotation && a.CameraPosition == b.CameraPosition && a.LightDirection == b.LightDirection &&
        a.FieldOfView == b.FieldOfView && a.Size == b.Size && a.Source == b.Source && a.RequiresPose == b.RequiresPose;

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
