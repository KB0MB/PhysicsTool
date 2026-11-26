using BymlLibrary;

namespace HKCLTool;

internal enum PlayerArmorSlot { Head, Upper, Lower }

internal sealed record PlayerArmorEntry(PlayerArmorSlot Slot, string Name, string Path, string? ArchiveEntry = null)
{
    public string DisplayName { get; init; } = Name;
    public string ModelKey { get; init; } = "";
    public override string ToString() => DisplayName;
}

internal sealed class PlayerArmorCatalog
{
    public IReadOnlyList<PlayerArmorEntry> Entries { get; }
    public string? RightHandPath { get; }
    private readonly bool _totk;
    private readonly Dictionary<string, PlayerArmorEntry> _entries;
    internal PlayerEquipmentSources Sources { get; }
    public static bool Supports(BotwActorResources resources) => resources.IsTotk ? resources.ActorName == "Player" : resources.ActorName == "GameROMPlayer";

    public PlayerArmorCatalog(BotwActorResources player)
    {
        if (!Supports(player)) throw new InvalidOperationException("Armor slots are available only for the Player actor.");
        _totk = player.IsTotk;
        Sources = new(player);
        var entries = new Dictionary<string, PlayerArmorEntry>(StringComparer.OrdinalIgnoreCase);
        RightHandPath = Sources.Find("PlayerRaulHand")?.Path;
        foreach (var source in Sources.Actors.Values)
        {
            var name = source.Name;
            if (!name.StartsWith("Armor_", StringComparison.Ordinal)) continue;
            foreach (var slot in Enum.GetValues<PlayerArmorSlot>())
                if (name.EndsWith("_" + slot, StringComparison.Ordinal)) entries.TryAdd(name, new(slot, name, source.Path, source.ArchiveEntry));
        }
        foreach (var entry in entries.Values.ToArray())
        {
            try
            {
                var resources = Sources.Find(entry.Name)!.Open(player.Roots);
                string modelKey = System.Text.Json.JsonSerializer.Serialize(new { entry.Slot, resources.Models,
                    resources.VariationMatAnim, resources.VariationMatAnimFrame,
                    Ear = new[] { resources.ArmorEarRotationDegrees.X, resources.ArmorEarRotationDegrees.Y, resources.ArmorEarRotationDegrees.Z } });
                entries[entry.Name] = entry with { ModelKey = modelKey };
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or KeyNotFoundException)
            { player.Warnings.Add($"Armor identity {entry.Name}: {ex.Message}"); }
        }
        _entries = entries.ToDictionary(p => p.Key, p => p.Value with { DisplayName = PlayerEquipmentNames.Name(p.Key, _totk) }, StringComparer.OrdinalIgnoreCase);
        var visible = entries.Values.OrderBy(e => SortNumber(e.Name)).ThenBy(e => e.Name, StringComparer.Ordinal)
            .DistinctBy(e => string.IsNullOrEmpty(e.ModelKey) ? e.Name : e.ModelKey).ToArray();
        var labels = PlayerEquipmentNames.Labels(visible.Select(e => e.Name), _totk);
        Entries = visible.Select(e => e with { DisplayName = labels[e.Name] })
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static int SortNumber(string name) => name.StartsWith("Armor_", StringComparison.Ordinal) &&
        int.TryParse(name.AsSpan(6, Math.Max(0, name.IndexOf('_', 6) - 6)), out var number) ? number : -1;

    public string DefaultActorName(PlayerArmorSlot slot) => _totk ? DefaultName(slot) : "Armor_Default_" + slot;
    public PlayerArmorEntry? Default(PlayerArmorSlot slot) => Entries.FirstOrDefault(e => e.Name == DefaultActorName(slot));
    public static string DefaultName(PlayerArmorSlot slot) => slot == PlayerArmorSlot.Head ? "Armor_999_Head" : "Armor_Default_" + slot;
    public PlayerArmorEntry Find(PlayerArmorSlot slot, string name) => _entries.GetValueOrDefault(name) is { } entry && entry.Slot == slot
        ? Entries.FirstOrDefault(e => e.Name == entry.Name) ?? entry
        : throw new InvalidDataException($"Armor '{name}' is not available in the {slot} slot of this game dump.");
    public PlayerArmorEntry? VisibleEntry(PlayerArmorSlot slot, string? name)
    {
        if (name == null || !_entries.TryGetValue(name, out var entry) || entry.Slot != slot) return null;
        return Entries.FirstOrDefault(e => e.Name == name || !string.IsNullOrEmpty(entry.ModelKey) && e.ModelKey == entry.ModelKey);
    }
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
        if (!IsTotk)
        {
            var result = new ActorEquipmentVisibility();
            foreach (var group in Models.GroupBy(m => m.Resource))
            {
                var bytes = ReadModel(group.Key);
                if (bytes == null) continue;
                var document = new ToolboxBfresDocument(bytes);
                foreach (var modelReference in group)
                {
                    dynamic? model = document.Models.FirstOrDefault(m => (string)m.Name == modelReference.Model);
                    if (model == null) continue;
                    result.HideGroups.UnionWith(HideGroups((object)model.UserData));
                    foreach (object material in ToolboxBfresDocument.Items((object)model.Materials))
                    {
                        string name;
                        object data;
                        if (material is Syroot.NintenTools.NSW.Bfres.Material nx) { name = nx.Name; data = nx.UserDatas; }
                        else { var wii = (Syroot.NintenTools.Bfres.Material)material; name = wii.Name; data = wii.UserData; }
                        foreach (var groupName in HideGroups(data))
                            result.Groups[groupName] = result.Groups.GetValueOrDefault(groupName, []).Append(name).Distinct(StringComparer.Ordinal).ToArray();
                    }
                }
            }
            return result;
        }
        var actor = TotkParameters($"Actor/{ActorName}.engine__actor__ActorParam.bgyml");
        var components = actor["Components"].GetMap();
        var key = ActorName == "Player" ? "EquipmentUserRef" : "ArmorRef";
        return components.TryGetValue(key, out var reference)
            ? ActorEquipmentVisibility.Read(TotkParameters(reference.GetString())) : new();
    }

    private static IEnumerable<string> HideGroups(object? collection) => ToolboxBfresDocument.Items(collection)
        .Where(v => (string)v.Name == "HideMaterialGroup")
        .SelectMany(v => (string[])v.GetValueStringArray()).Where(s => !string.IsNullOrWhiteSpace(s));
}
