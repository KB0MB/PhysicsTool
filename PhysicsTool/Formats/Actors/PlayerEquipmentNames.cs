using System.Text.Json;

namespace HKCLTool;

internal static class PlayerEquipmentNames
{
    // Bundled US English inventory names from each game's message archive.
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Tables = new(() =>
    {
        using var stream = typeof(PlayerEquipmentNames).Assembly.GetManifestResourceStream("PhysicsTool.EquipmentNames.en.json");
        var tables = stream == null ? new() : JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream) ?? new();
        return tables.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value, StringComparer.OrdinalIgnoreCase));
    });

    internal static string Name(string actor, bool totk)
    {
        if (actor.StartsWith("Armor_Default_", StringComparison.OrdinalIgnoreCase))
            return "Default " + actor[14..].ToLowerInvariant();
        if (totk && actor == "Armor_999_Head") return "Default head (loose hair)";
        if (totk && actor == "Armor_1151_Head") return "Default head (tied hair)";
        if (!Tables.Value.TryGetValue(totk ? "Totk" : "Botw", out var names)) return actor;
        if (names.TryGetValue(actor, out var name)) return name;
        var parts = actor.Split('_');
        return parts.Length > 3 && names.TryGetValue(string.Join('_', parts.Take(3)), out name)
            ? name + " (" + string.Join(' ', parts.Skip(3)) + ")" : actor;
    }

    internal static IReadOnlyDictionary<string, string> Labels(IEnumerable<string> actors, bool totk)
    {
        var labels = actors.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(a => a, a => Name(a, totk), StringComparer.OrdinalIgnoreCase);
        foreach (var group in labels.ToArray().GroupBy(p => p.Value, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var entry in group)
            {
                var parts = entry.Key.Split('_');
                string variant = parts[0] == "Armor" ? parts[1] : string.Join('_', parts.Skip(2));
                labels[entry.Key] = entry.Value + " (" + variant + ")";
            }
        return labels;
    }
}
