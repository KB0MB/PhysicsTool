using BymlLibrary;

namespace HKCLTool;

internal enum PlayerArmorSlot { Head, Upper, Lower }

internal sealed record PlayerArmorEntry(PlayerArmorSlot Slot, string Name, string Path)
{
    public string DisplayName => Name.StartsWith("Armor_", StringComparison.Ordinal) && Name.EndsWith("_" + Slot, StringComparison.Ordinal)
        ? Name[6..^(Slot.ToString().Length + 1)] : Name;
    public override string ToString() => DisplayName;
}

internal sealed class PlayerArmorCatalog
{
    public IReadOnlyList<PlayerArmorEntry> Entries { get; }
    public string? RightHandPath { get; }
    public static bool Supports(BotwActorResources resources) => resources.IsTotk && resources.ActorName == "Player";

    public PlayerArmorCatalog(BotwActorResources player)
    {
        if (!Supports(player)) throw new InvalidOperationException("Armor slots are available only for the TotK Player actor.");
        var entries = new Dictionary<string, PlayerArmorEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in new[] { System.IO.Path.GetDirectoryName(player.SourcePath)! }
            .Concat(player.Roots.Select(r => System.IO.Path.Combine(r, "Pack", "Actor"))).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder)) continue;
            var hand = System.IO.Path.Combine(folder, "PlayerRaulHand.pack.zs");
            if (RightHandPath == null && File.Exists(hand)) RightHandPath = hand;
            foreach (var path in Directory.EnumerateFiles(folder, "Armor_*.pack.zs"))
            {
                var name = System.IO.Path.GetFileName(path)[..^8];
                foreach (var slot in Enum.GetValues<PlayerArmorSlot>())
                    if (name.EndsWith("_" + slot, StringComparison.Ordinal)) entries.TryAdd(name, new(slot, name, path));
            }
        }
        Entries = entries.Values.OrderBy(e => SortNumber(e.Name)).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static int SortNumber(string name) => name.StartsWith("Armor_", StringComparison.Ordinal) &&
        int.TryParse(name.AsSpan(6, Math.Max(0, name.IndexOf('_', 6) - 6)), out var number) ? number : -1;

    public PlayerArmorEntry? Default(PlayerArmorSlot slot) => Entries.FirstOrDefault(e => e.Name == DefaultName(slot));
    public static string DefaultName(PlayerArmorSlot slot) => slot == PlayerArmorSlot.Head ? "Armor_999_Head" : "Armor_Default_" + slot;
    public PlayerArmorEntry Find(PlayerArmorSlot slot, string name) => Entries.FirstOrDefault(e => e.Slot == slot && e.Name == name)
        ?? throw new InvalidDataException($"Armor '{name}' is not available in the {slot} slot of this game dump.");
}

internal sealed class ActorEquipmentVisibility
{
    public Dictionary<string, string[]> Groups { get; } = new(StringComparer.Ordinal);
    public HashSet<string> HideGroups { get; } = new(StringComparer.Ordinal);

    public static ActorEquipmentVisibility Read(Dictionary<string, Byml> parameters)
    {
        var result = new ActorEquipmentVisibility();
        if (parameters.TryGetValue("HiddenMaterialGroupList", out var groups))
            foreach (var value in groups.GetArray())
            {
                var group = value.GetMap();
                if (group.TryGetValue("GroupName", out var name) && group.TryGetValue("MaterialNameList", out var materials))
                    result.Groups[name.GetString()] = materials.GetArray().Select(m => m.GetString()).ToArray();
            }
        if (parameters.TryGetValue("HideMaterialGroupNameList", out var hidden))
            result.HideGroups.UnionWith(hidden.GetArray().Select(g => g.GetString()));
        return result;
    }

    public HashSet<string> HiddenBy(IEnumerable<ActorEquipmentVisibility> others) => others
        .SelectMany(o => o.HideGroups).Distinct(StringComparer.Ordinal).Where(Groups.ContainsKey)
        .SelectMany(g => Groups[g]).ToHashSet(StringComparer.Ordinal);
}

internal sealed partial class BotwActorResources
{
    internal ActorEquipmentVisibility ReadEquipmentVisibility()
    {
        if (!IsTotk) return new();
        var actor = TotkParameters($"Actor/{ActorName}.engine__actor__ActorParam.bgyml");
        var components = actor["Components"].GetMap();
        var key = ActorName == "Player" ? "EquipmentUserRef" : "ArmorRef";
        return components.TryGetValue(key, out var reference)
            ? ActorEquipmentVisibility.Read(TotkParameters(reference.GetString())) : new();
    }
}
