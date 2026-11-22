using System.Text.Json;
using HKCLTool;

namespace PhysicsTool.Core;

// Cache only eligibility, never scene contents. File metadata invalidates edited resources.
internal sealed class MapSceneCatalogCache
{
    public sealed record Entry(long Length, long Modified, bool HasActors);
    public sealed record Snapshot(int Version, string Root, long DictionaryLength, long DictionaryModified, Dictionary<string, Entry> Files);
    private readonly string _root;
    private readonly string? _path;
    private readonly long _dictionaryLength, _dictionaryModified;
    private readonly Dictionary<string, Entry> _previous;
    private readonly Dictionary<string, Entry> _current = new(StringComparer.Ordinal);

    internal MapSceneCatalogCache(string root, string? path)
    {
        _root = Path.GetFullPath(root); _path = path;
        var dictionary = new FileInfo(Path.Combine(root, "Pack", "ZsDic.pack.zs"));
        if (dictionary.Exists) { _dictionaryLength = dictionary.Length; _dictionaryModified = dictionary.LastWriteTimeUtc.Ticks; }
        _previous = new(StringComparer.Ordinal);
        if (path == null) return;
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024 * 1024) return;
            var data = JsonSerializer.Deserialize<Snapshot>(File.ReadAllBytes(path));
            if (data is { Version: 1, Files: not null } && data.Root == _root &&
                data.DictionaryLength == _dictionaryLength && data.DictionaryModified == _dictionaryModified) _previous = data.Files;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }

    internal bool Read(string path, Func<bool> inspect)
    {
        var info = new FileInfo(path);
        string key = Path.GetRelativePath(_root, path);
        long length = info.Length, modified = info.LastWriteTimeUtc.Ticks;
        if (!_previous.TryGetValue(key, out var entry) || entry == null || entry.Length != length || entry.Modified != modified)
            entry = new(length, modified, inspect());
        _current[key] = entry;
        return entry.HasActors;
    }

    internal void Save()
    {
        if (_path == null) return;
        try { AtomicFile.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(new Snapshot(1, _root, _dictionaryLength, _dictionaryModified, _current))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
