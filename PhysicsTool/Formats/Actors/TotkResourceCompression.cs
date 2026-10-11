using System.Buffers.Binary;
using ZstdSharp;

namespace HKCLTool;

internal sealed class TotkResourceCompression
{
    private readonly Dictionary<uint, byte[]> _dictionaries = new();
    private readonly HashSet<string> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public byte[] Decode(byte[] bytes, IEnumerable<string> roots)
    {
        if (bytes.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0xfd2fb528) return bytes;
        if (bytes.Length < 6) throw new InvalidDataException("Truncated Zstandard resource.");
        var offset = (bytes[4] & 0x20) != 0 ? 5 : 6;
        var size = (bytes[4] & 3) switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };
        if (bytes.Length < offset + size) throw new InvalidDataException("Truncated Zstandard dictionary ID.");
        uint id = size switch { 0 => 0, 1 => bytes[offset], 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)), _ => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) };
        using var decoder = new Decompressor();
        if (id != 0)
        {
            foreach (var root in roots)
            {
                var path = Path.Combine(root, "Pack", "ZsDic.pack.zs");
                if (_dictionaries.ContainsKey(id)) break;
                if (!File.Exists(path) || !_loaded.Add(path)) continue;
                using var dictionaryDecoder = new Decompressor();
                var archive = BotwSarcArchive.Parse(dictionaryDecoder.Unwrap(File.ReadAllBytes(path)).ToArray());
                foreach (var entry in archive.Entries)
                {
                    var data = archive.Read(entry).ToArray();
                    if (data.Length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 0xec30a437)
                        _dictionaries.TryAdd(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)), data);
                }
            }
            if (!_dictionaries.TryGetValue(id, out var dictionary))
                throw new InvalidDataException($"Missing TotK compression dictionary {id}. Select the matching romfs folder containing Pack/ZsDic.pack.zs.");
            decoder.LoadDictionary(dictionary);
        }
        try { return decoder.Unwrap(bytes).ToArray(); }
        catch (ZstdException ex) { throw new InvalidDataException("Could not decompress the TotK resource.", ex); }
    }

    public static IEnumerable<string> Roots(string path)
    {
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); folder != null; folder = folder.Parent)
        {
            yield return folder.FullName;
            if (Directory.Exists(Path.Combine(folder.FullName, "romfs"))) yield return Path.Combine(folder.FullName, "romfs");
        }
    }
}
