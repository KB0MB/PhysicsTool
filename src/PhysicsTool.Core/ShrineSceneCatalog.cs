using HKCLTool;

namespace PhysicsTool.Core;

internal sealed record ShrineSceneEntry(string Path, string RelativePath, string? CompoundPath)
{
    public override string ToString() => ShrineSceneCatalog.SceneName(RelativePath);
}

internal static class ShrineSceneCatalog
{
    internal static string SceneName(string path)
    {
        var name = path.EndsWith(".zs", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;
        if (name.EndsWith(".bcett.byml", StringComparison.OrdinalIgnoreCase)) name = name[..^11];
        foreach (var suffix in new[] { "_Static", "_Dynamic" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name[..^suffix.Length];
        return name;
    }
    internal static string Root(string path)
    {
        if (Directory.Exists(System.IO.Path.Combine(path, "romfs", "Banc"))) return System.IO.Path.Combine(path, "romfs");
        if (Directory.Exists(System.IO.Path.Combine(path, "Banc"))) return path;
        return TotkResourceCompression.Roots(path).FirstOrDefault(r => Directory.Exists(System.IO.Path.Combine(r, "Banc")))
            ?? throw new DirectoryNotFoundException("Select the TotK romfs folder containing Banc, Model and Pack.");
    }

    internal static string? Companion(string path)
    {
        string plain = path.EndsWith(".zs", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;
        string other = plain.Contains("_Static.bcett.") ? plain.Replace("_Static.bcett.", "_Dynamic.bcett.")
            : plain.Replace("_Dynamic.bcett.", "_Static.bcett.");
        if (other == plain) return null;
        return File.Exists(other + ".zs") ? other + ".zs" : File.Exists(other) ? other : null;
    }

    internal static IReadOnlyList<ShrineSceneEntry> Scan(string root, CancellationToken cancellation = default, IProgress<string>? progress = null, string? cachePath = null)
    {
        cancellation.ThrowIfCancellationRequested();
        root = Root(root);
        var banc = System.IO.Path.Combine(root, "Banc");
        var files = new List<string>();
        foreach (string file in Directory.EnumerateFiles(banc, "*.bcett.byml*", SearchOption.AllDirectories))
        {
            cancellation.ThrowIfCancellationRequested();
            if (file.EndsWith(".bcett.byml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".bcett.byml.zs", StringComparison.OrdinalIgnoreCase)) files.Add(file);
        }
        // Reuse dictionary data while checking contents; do not load models or merged geometry.
        var compression = new TotkResourceCompression();
        var cache = new MapSceneCatalogCache(root, cachePath);
        var entries = new List<ShrineSceneEntry>();
        int checkedFiles = 0;
        foreach (var group in files.GroupBy(p => SceneName(System.IO.Path.GetRelativePath(banc, p)), StringComparer.OrdinalIgnoreCase))
        {
            foreach (string file in group.OrderBy(p => p.Contains("_Static.bcett.", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p.EndsWith(".zs", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                cancellation.ThrowIfCancellationRequested();
                if (++checkedFiles % 250 == 0) progress?.Report($"Checking map scenes: {checkedFiles:N0} files, {entries.Count:N0} scenes found...");
                bool hasActors;
                try { hasActors = cache.Read(file, () => HasActors(file, root, compression)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if (!hasActors) continue;
                entries.Add(new(file, group.Key, FindCompound(root, file)));
                break;
            }
        }
        cancellation.ThrowIfCancellationRequested();
        cache.Save();
        return entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool HasActors(string path, string root, TotkResourceCompression compression)
    {
        try
        {
            var scene = ShrineSceneDocument.Parse(BphscDocument.ReadResource(path, [root], compression), allowMissingActors: true);
            return scene.Actors.Any(a => !string.IsNullOrWhiteSpace(a.Name) && (!a.IsMergeParts || a.MergedPath.Length > 0));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or
            InvalidOperationException or IndexOutOfRangeException or OverflowException or NotSupportedException) { return false; }
    }

    internal static string? FindCompound(string root, string scene)
    {
        var relative = System.IO.Path.GetRelativePath(System.IO.Path.Combine(root, "Banc"), scene);
        var folder = System.IO.Path.GetDirectoryName(relative) ?? "";
        // A merged scene is reusable; its owning scene determines the compound.
        if (folder.Split(System.IO.Path.DirectorySeparatorChar).Contains("Merged", StringComparer.OrdinalIgnoreCase)) return null;
        var name = System.IO.Path.GetFileName(scene).Split('.')[0];
        foreach (var suffix in new[] { "_Static", "_Dynamic" })
            if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name[..^suffix.Length];
        foreach (string extension in new[] { ".Nin_NX_NVN.bphsc.zs", ".Nin_NX_NVN.bphsc" })
        {
            string path = System.IO.Path.Combine(root, "Phive", "StaticCompoundBody", folder, name + extension);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
