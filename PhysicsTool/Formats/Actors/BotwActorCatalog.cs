using System.Text.Json;

namespace HKCLTool;

internal sealed record BotwActorCatalogEntry(string Name, string Path, string? ArchiveEntry,
    string ResourceRoot, bool IsSwitch, bool HasCloth, bool HasSupport)
{
    public string Platform => IsSwitch ? "Switch" : "Wii U";
    public string Source
    {
        get
        {
            var folder = new DirectoryInfo(ResourceRoot);
            var label = folder.Name is "content" or "romfs" ? folder.Parent?.Name + "/" + folder.Name : folder.Name;
            return label + "/" + (ArchiveEntry == null ? "Actor/Pack" : System.IO.Path.GetFileName(Path));
        }
    }
}

internal sealed record BotwActorCatalogResult(IReadOnlyList<BotwActorCatalogEntry> Actors, IReadOnlyList<string> Warnings);

internal sealed class BotwActorCatalog
{
    private sealed record CachedFile(long Length, DateTime Modified, string ResourceRoot, IReadOnlyList<BotwActorCatalogEntry> Actors, IReadOnlyList<string> Warnings);
    private sealed record CacheDocument(int Version, Dictionary<string, CachedFile> Files);
    private const int CacheVersion = 1;
    private readonly Dictionary<string, CachedFile> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scanGate = new();
    private readonly string _cachePath;

    public BotwActorCatalog(string? cachePath = null)
    {
        _cachePath = cachePath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "actor-catalog-cache.json");
        try
        {
            if (!File.Exists(_cachePath) || new FileInfo(_cachePath).Length > 32 * 1024 * 1024) return;
            var saved = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllBytes(_cachePath));
            if (saved?.Version != CacheVersion || saved.Files == null) return;
            foreach (var (path, file) in saved.Files)
                if (file?.Actors != null && file.Warnings != null) _cache[path] = file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
    }

    public BotwActorCatalogResult Scan(IEnumerable<string> folders, IProgress<string>? progress, CancellationToken cancellation)
    {
        lock (_scanGate) return ScanCore(folders, progress, cancellation);
    }

    private BotwActorCatalogResult ScanCore(IEnumerable<string> folders, IProgress<string>? progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var warnings = new List<string>();
        var roots = FindResourceRoots(folders, warnings, cancellation);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var directory in new[] { root, System.IO.Path.Combine(root, "Actor", "Pack"), System.IO.Path.Combine(root, "Pack") }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(directory)) continue;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                        if (IsActorPack(file) || System.IO.Path.GetExtension(file).Equals(".pack", StringComparison.OrdinalIgnoreCase))
                            files.TryAdd(System.IO.Path.GetFullPath(file), root);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { warnings.Add(System.IO.Path.GetFileName(directory) + ": " + UserFacingExceptionFormatter.Format(ex)); }
            }
        }
        var actors = new List<BotwActorCatalogEntry>();
        var scanned = 0;
        foreach (var (path, root) in files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            if (scanned++ % 25 == 0) progress?.Report($"{scanned:N0} / {files.Count:N0}");
            try
            {
                var info = new FileInfo(path);
                if (_cache.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc &&
                    string.Equals(cached.ResourceRoot, root, StringComparison.OrdinalIgnoreCase))
                { actors.AddRange(cached.Actors); warnings.AddRange(cached.Warnings); continue; }
                var warningStart = warnings.Count;
                var entries = new List<BotwActorCatalogEntry>();
                var archive = BotwSarcArchive.OpenIndexed(path);
                if (IsActorPack(path))
                    AddActor(archive, path, null, root, entries, warnings);
                else
                    foreach (var entry in archive.Entries.Where(e => IsActorPack(e.Name)))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        try { AddActor(BotwSarcArchive.Parse(archive.Read(entry).Span), path, entry.Name, root, entries, warnings); }
                        catch (Exception ex) when (IsReadError(ex))
                        { warnings.Add(System.IO.Path.GetFileName(entry.Name) + ": " + UserFacingExceptionFormatter.Format(ex)); }
                    }
                // Keep only metadata, never decompressed model/actor archives.
                _cache[path] = new(info.Length, info.LastWriteTimeUtc, root, entries, warnings.Skip(warningStart).ToArray());
                actors.AddRange(entries);
            }
            catch (Exception ex) when (IsReadError(ex))
            { warnings.Add(System.IO.Path.GetFileName(path) + ": " + UserFacingExceptionFormatter.Format(ex)); }
        }
        foreach (var stale in _cache.Keys.Where(k => !files.ContainsKey(k)).ToArray()) _cache.Remove(stale);
        cancellation.ThrowIfCancellationRequested();
        try { AtomicFile.WriteAllBytes(_cachePath, JsonSerializer.SerializeToUtf8Bytes(new CacheDocument(CacheVersion, _cache))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { warnings.Add(AppLocalization.Translate("Actor cache could not be saved.")); }
        return new(actors.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Platform).ThenBy(a => a.Path).ToArray(), warnings);
    }

    internal static bool IsActorPack(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".sbactorpack" or ".bactorpack";
    private static bool IsReadError(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException or NotSupportedException;

    private static void AddActor(BotwSarcArchive pack, string path, string? entry, string root,
        List<BotwActorCatalogEntry> result, List<string> warnings)
    {
        var cloth = pack.Entries.Any(e => e.Name.EndsWith(".hkcl", StringComparison.OrdinalIgnoreCase));
        var support = pack.Entries.Any(e => e.Name.EndsWith(".bphyssb", StringComparison.OrdinalIgnoreCase));
        // References also count: shared physics may live outside the actor pack.
        foreach (var physics in pack.Entries.Where(e => e.Name.EndsWith(".bphysics", StringComparison.OrdinalIgnoreCase)))
        {
            if (cloth && support) break;
            try
            {
                var parameters = BotwAampReader.Read(pack.Read(physics).ToArray()).Root;
                foreach (var value in BotwActorResources.Objects(parameters).SelectMany(o => o.Parameters)
                    .Where(p => p.Type is 7 or 8 or 15 or 20).Select(p => p.AsString()))
                {
                    cloth |= value.EndsWith(".hkcl", StringComparison.OrdinalIgnoreCase);
                    support |= value.EndsWith(".bphyssb", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) when (IsReadError(ex))
            { warnings.Add(System.IO.Path.GetFileName(entry ?? path) + ": " + UserFacingExceptionFormatter.Format(ex)); }
        }
        if (cloth || support)
            result.Add(new(System.IO.Path.GetFileNameWithoutExtension(entry ?? path), path, entry, root, pack.LittleEndian, cloth, support));
    }

    private static IReadOnlyList<string> FindResourceRoots(IEnumerable<string> folders, List<string> warnings, CancellationToken cancellation)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<(string Path, int Depth)>();
        foreach (var folder in folders) pending.Enqueue((System.IO.Path.GetFullPath(folder), 0));
        while (pending.TryDequeue(out var item))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!visited.Add(item.Path)) continue;
            if (!Directory.Exists(item.Path)) { warnings.Add(item.Path + ": " + AppLocalization.Translate("Folder unavailable")); continue; }
            try
            {
                var contentRoot = Directory.Exists(System.IO.Path.Combine(item.Path, "Actor")) || Directory.Exists(System.IO.Path.Combine(item.Path, "Model"));
                if (contentRoot ||
                    Directory.Exists(System.IO.Path.Combine(item.Path, "Pack")) || Directory.EnumerateFiles(item.Path).Any(f => IsActorPack(f) || f.EndsWith(".pack", StringComparison.OrdinalIgnoreCase)))
                    result.Add(item.Path);
                if (contentRoot) continue;
                if (item.Depth >= 4) continue;
                foreach (var child in Directory.EnumerateDirectories(item.Path))
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pending.Enqueue((child, item.Depth + 1));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warnings.Add(System.IO.Path.GetFileName(item.Path) + ": " + UserFacingExceptionFormatter.Format(ex)); }
        }
        return result.ToArray();
    }
}
