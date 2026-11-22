using System.Text.Json;
using HKCLTool;

namespace PhysicsTool.Core;

internal sealed partial class OotActorResources
{
    private sealed record ResourceStamp(bool Exists, long Length, long Modified)
    {
        internal static ResourceStamp Read(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new(true, info.Length, info.LastWriteTimeUtc.Ticks) : new(false, 0, 0);
        }
    }

    private sealed record CachedActor(ResourceStamp Pack, BotwActorCatalogEntry? Actor,
        string[] Warnings, Dictionary<string, ResourceStamp> Dependencies);
    private sealed record CatalogCache(int Version, string Root, Dictionary<string, ResourceStamp> Shared,
        Dictionary<string, CachedActor> Actors);
    private const int CatalogVersion = 1;

    internal static BotwActorCatalogResult Scan(string folder, IProgress<string>? progress, CancellationToken token,
        string? cachePath = null)
    {
        token.ThrowIfCancellationRequested();
        var root = ResourceRoot(folder);
        var sharedStamps = SharedPaths(root).Append(System.IO.Path.Combine(root, "Pack", "ZsDic.pack.zs"))
            .ToDictionary(p => p, ResourceStamp.Read, StringComparer.Ordinal);
        var cached = ReadCatalog(cachePath, root, sharedStamps);
        var updated = new Dictionary<string, CachedActor>(StringComparer.Ordinal);
        var checkedDependencies = new Dictionary<string, ResourceStamp>(StringComparer.Ordinal);
        var compression = new TotkResourceCompression();
        Dictionary<string, byte[]>? shared = null;
        var actors = new List<BotwActorCatalogEntry>();
        var warnings = new List<string>();
        var paths = Directory.GetFiles(System.IO.Path.Combine(root, "Pack", "Actor"), "*.pack.zs");
        Array.Sort(paths, StringComparer.Ordinal);
        for (int i = 0; i < paths.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (i % 25 == 0) progress?.Report($"{i + 1:N0} / {paths.Length:N0}");
            var path = paths[i];
            var name = System.IO.Path.GetFileName(path)[..^8];
            try
            {
                var stamp = ResourceStamp.Read(path);
                if (cached.TryGetValue(path, out var previous) && previous.Pack == stamp &&
                    previous.Dependencies.All(d => CurrentStamp(d.Key) == d.Value))
                {
                    updated.Add(path, previous);
                    if (previous.Actor != null) actors.Add(previous.Actor);
                    warnings.AddRange(previous.Warnings);
                    continue;
                }

                // Warm scans never decompress packs or retain game resource bytes in the cache.
                shared ??= ReadShared(root, compression, token);
                var pack = BotwSarcArchive.Parse(compression.Decode(File.ReadAllBytes(path), [root]));
                var resources = new OotActorResources(root, pack, shared, catalogOnly: true);
                resources.ResolveActor(name);
                var actorWarnings = resources.Warnings.Select(w => name + ": " + w).ToArray();
                BotwActorCatalogEntry? actor = resources.Assets.Count == 0 ? null : new(name, path, null, root, true,
                    resources.Assets.Any(a => a.IsCloth), resources.Assets.Any(a => !a.IsCloth)) { IsOot = true };
                if (actor != null) actors.Add(actor);
                warnings.AddRange(actorWarnings);
                updated.Add(path, new(stamp, actor, actorWarnings, resources._dependencies));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
            { warnings.Add(name + ": " + ex.Message); }
        }
        token.ThrowIfCancellationRequested();
        if (cachePath != null)
        {
            try { AtomicFile.WriteAllBytes(cachePath, JsonSerializer.SerializeToUtf8Bytes(new CatalogCache(CatalogVersion, root, sharedStamps, updated))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warnings.Add("Actor cache could not be saved."); }
        }
        return new(actors.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray(), warnings);

        ResourceStamp CurrentStamp(string path)
        {
            if (!checkedDependencies.TryGetValue(path, out var stamp))
                checkedDependencies.Add(path, stamp = ResourceStamp.Read(path));
            return stamp;
        }
    }

    private static Dictionary<string, CachedActor> ReadCatalog(string? path, string root, Dictionary<string, ResourceStamp> shared)
    {
        try
        {
            if (path == null || !File.Exists(path) || new FileInfo(path).Length > 32 * 1024 * 1024) return [];
            var cache = JsonSerializer.Deserialize<CatalogCache>(File.ReadAllBytes(path));
            if (cache?.Version != CatalogVersion || cache.Root != root || cache.Shared == null || cache.Actors == null ||
                cache.Shared.Count != shared.Count || shared.Any(p => !cache.Shared.TryGetValue(p.Key, out var saved) || saved != p.Value)) return [];
            return cache.Actors.Where(p => p.Value?.Pack != null && p.Value.Warnings != null && p.Value.Dependencies != null)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return []; }
    }
}
