namespace HKCLTool;

internal sealed record PlayerEquipmentSource(string Name, string Path, string? ArchiveEntry = null)
{
    public BotwActorResources Open(IEnumerable<string> roots) => new(Path, roots, ArchiveEntry);
}

// Equipment can be loose or embedded in BotW's shared startup archives.
internal sealed class PlayerEquipmentSources
{
    public Dictionary<string, PlayerEquipmentSource> Actors { get; } = new(StringComparer.OrdinalIgnoreCase);
    public PlayerEquipmentSource? Find(string name) => Actors.GetValueOrDefault(name);

    public PlayerEquipmentSources(BotwActorResources player)
    {
        string extension = player.IsTotk ? ".pack.zs" : ".sbactorpack";
        var folders = new[] { System.IO.Path.GetDirectoryName(player.SourcePath)! }.Concat(player.Roots.Select(r =>
            player.IsTotk ? System.IO.Path.Combine(r, "Pack", "Actor") : System.IO.Path.Combine(r, "Actor", "Pack")))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.Where(Directory.Exists))
        foreach (var path in Directory.EnumerateFiles(folder, "*" + extension))
        {
            var name = System.IO.Path.GetFileName(path)[..^extension.Length];
            Actors.TryAdd(name, new(name, path));
        }
        if (player.IsTotk) return;
        foreach (var path in new[] { player.SourcePath }.Concat(player.Roots.SelectMany(root => new[] {
            System.IO.Path.Combine(root, "Pack", "TitleBG.pack"), System.IO.Path.Combine(root, "Pack", "Bootup.pack") }))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(p => p.EndsWith(".pack", StringComparison.OrdinalIgnoreCase) && File.Exists(p)))
        {
            var archive = BotwSarcArchive.OpenIndexed(path);
            if (archive.LittleEndian != player.IsSwitch) continue;
            foreach (var entry in archive.Entries.Where(e => e.Name.StartsWith("Actor/Pack/", StringComparison.Ordinal) && e.Name.EndsWith(extension, StringComparison.Ordinal)))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(entry.Name);
                Actors.TryAdd(name, new(name, path, entry.Name));
            }
        }
    }
}
