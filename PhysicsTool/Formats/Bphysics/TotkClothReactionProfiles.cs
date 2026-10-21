using BymlLibrary;
using System.Numerics;

namespace HKCLTool;

public sealed record TotkReactionSample(float? Frequency, float? DragForce, float? SwingFactor,
    Vector3? SubWind, float? ReactionSpeed);

public sealed record TotkClothReactionProfile(string Name, string BaseBone, string Resource,
    TotkReactionSample Base, IReadOnlyList<TotkReactionSample> Reactions, TotkReactionSample Strong,
    bool? UseStrongWind);

/// <summary>Reads actor-side reactions, which are not stored in a standalone BPHCL.</summary>
public sealed class TotkClothReactionProfiles
{
    public IReadOnlyDictionary<string, TotkClothReactionProfile> Cloths { get; }
    public IReadOnlyList<string> Warnings { get; }
    private TotkClothReactionProfiles(Dictionary<string, TotkClothReactionProfile> cloths, List<string> warnings)
    { Cloths = cloths; Warnings = warnings; }

    public static TotkClothReactionProfiles Load(string path, IEnumerable<string>? roots = null)
    {
        var codec = new TotkResourceCompression();
        var folders = TotkResourceCompression.Roots(path).Concat(roots ?? [])
            .Where(Directory.Exists).Select(p => Directory.Exists(Path.Combine(p, "romfs")) ? Path.Combine(p, "romfs") : p)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var pack = BotwSarcArchive.Parse(codec.Decode(File.ReadAllBytes(path), folders));
        var entries = pack.Entries.ToDictionary(e => e.Name, StringComparer.Ordinal);
        var shared = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sharedLoaded = false;
        byte[]? Read(string relative)
        {
            if (entries.TryGetValue(relative, out var entry)) return pack.Read(entry).ToArray();
            foreach (var folder in folders)
            {
                var candidate = Path.Combine(folder, relative);
                if (File.Exists(candidate)) return File.ReadAllBytes(candidate);
                if (File.Exists(candidate + ".zs")) return codec.Decode(File.ReadAllBytes(candidate + ".zs"), folders);
            }
            if (!sharedLoaded)
            {
                sharedLoaded = true;
                foreach (var folder in folders)
                foreach (var name in new[] { "ResidentCommon.pack.zs", "Bootup.Nin_NX_NVN.pack.zs" })
                {
                    var common = Path.Combine(folder, "Pack", name);
                    if (!File.Exists(common)) continue;
                    var archive = BotwSarcArchive.Parse(codec.Decode(File.ReadAllBytes(common), folders));
                    foreach (var e in archive.Entries.Where(e => e.Name.EndsWith(".bgyml", StringComparison.Ordinal)))
                        shared.TryAdd(e.Name, archive.Read(e).ToArray());
                }
            }
            return shared.GetValueOrDefault(relative);
        }
        var name = Path.GetFileName(path);
        if (!name.EndsWith(".pack.zs", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a TotK actor .pack.zs file.");
        return ReadActor(name[..^8], Read);
    }

    internal static TotkClothReactionProfiles ReadActor(string actorName, Func<string, byte[]?> read)
    {
        var resolver = new Resolver(read);
        var actor = resolver.Read($"Actor/{actorName}.engine__actor__ActorParam.bgyml");
        var components = actor["Components"].GetMap();
        if (!components.TryGetValue("PhysicsRef", out var physicsRef)) throw new InvalidDataException("This actor has no physics component.");
        var physics = resolver.Read(physicsRef.GetString());
        var controllerPath = Text(physics, "ControllerSetPath");
        if (controllerPath.Length == 0) throw new InvalidDataException("This actor has no cloth controller.");
        var controller = resolver.Read(controllerPath);
        var warnings = new List<string>();
        var bases = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Array(controller, "Cloth"))
        {
            var file = Text(entry.GetMap(), "FilePath");
            if (file.Length == 0) continue;
            foreach (var mesh in Array(resolver.Read(file), "MeshList"))
            {
                var map = mesh.GetMap(); var name = NormalizeName(Text(map, "Name"));
                var bone = NormalizeName(Text(map, "BaseBone"));
                if (name.Length == 0) continue;
                if (!bases.TryAdd(name, bone)) ambiguous.Add(name);
            }
        }
        var result = new Dictionary<string, TotkClothReactionProfile>(StringComparer.Ordinal);
        foreach (var entry in Array(controller, "ClothReaction"))
        {
            var map = entry.GetMap(); var name = NormalizeName(Text(map, "Name")); var file = Text(map, "FilePath");
            if (name.Length == 0 || file.Length == 0) continue;
            if (result.ContainsKey(name) || ambiguous.Contains(name))
                throw new InvalidDataException($"Multiple cloths use the name {name}; reaction matching would be ambiguous.");
            var values = resolver.Read(file);
            var bone = bases.GetValueOrDefault(name, "");
            if (bone.Length == 0) warnings.Add($"{name}: no base-bone reference was found.");
            result.Add(name, new(name, bone, Path.GetFileName(Resolver.Normalize(file)), Sample(values),
                Array(values, "ReactionParam").Select(v => Sample(v.GetMap())).ToArray(), Sample(values, "Strong"),
                values.TryGetValue("UseStrongWind", out var enabled) ? enabled.GetBool() : null));
        }
        if (result.Count == 0) throw new InvalidDataException("This actor has no named cloth reactions.");
        return new(result, warnings);
    }

    // Retain absent fields as absent: engine schema defaults are not necessarily zero.
    private static TotkReactionSample Sample(IDictionary<string, Byml> map, string suffix = "") => new(
        Number(map, "Frequency" + suffix), Number(map, "DragForce" + suffix), Number(map, "SwingFactor" + suffix),
        Vector(map, "SubWind" + suffix), Number(map, "ReactionSpeed"));
    private static float? Number(IDictionary<string, Byml> map, string key)
    {
        if (!map.TryGetValue(key, out var node)) return null;
        var value = node.Type == BymlNodeType.Int ? node.GetInt() : node.GetFloat();
        if (!float.IsFinite(value)) throw new InvalidDataException($"Cloth reaction {key} must be finite.");
        return value;
    }
    private static Vector3? Vector(IDictionary<string, Byml> map, string key)
    {
        if (!map.TryGetValue(key, out var node)) return null;
        var vector = node.GetMap();
        var x = Number(vector, "X"); var y = Number(vector, "Y"); var z = Number(vector, "Z");
        return x.HasValue && y.HasValue && z.HasValue ? new(x.Value, y.Value, z.Value) : null;
    }
    private static string Text(IDictionary<string, Byml> map, string key) => map.TryGetValue(key, out var node) ? node.GetString() : "";
    private static IEnumerable<Byml> Array(IDictionary<string, Byml> map, string key) => map.TryGetValue(key, out var node) ? node.GetArray() : [];
    public static string NormalizeName(string name) => name.StartsWith("Link:", StringComparison.Ordinal) ? name[5..] : name;

    private sealed class Resolver(Func<string, byte[]?> read)
    {
        private readonly HashSet<string> _chain = new(StringComparer.Ordinal);
        internal static string Normalize(string value)
        {
            value = value.TrimStart('?').Replace('\\', '/');
            if (value.StartsWith("Work/", StringComparison.Ordinal)) value = value[5..];
            if (value.EndsWith(".gyml", StringComparison.Ordinal)) value = value[..^5] + ".bgyml";
            if (Path.IsPathRooted(value) || value.Contains(':') || value.Split('/').Any(p => p is "." or ".."))
                throw new InvalidDataException("Unsafe cloth reaction resource path.");
            return value;
        }
        internal Dictionary<string, Byml> Read(string path)
        {
            path = Normalize(path);
            if (_chain.Count >= 64 || !_chain.Add(path)) throw new InvalidDataException("Cloth reaction inheritance contains a cycle.");
            try
            {
                var data = read(path) ?? throw new InvalidDataException($"Missing cloth reaction resource: {path}");
                var map = Byml.FromBinary(data).GetMap();
                var result = map.TryGetValue("$parent", out var parent) ? Read(parent.GetString()) : new();
                Merge(result, map); return result;
            }
            finally { _chain.Remove(path); }
        }
        private static void Merge(Dictionary<string, Byml> target, IEnumerable<KeyValuePair<string, Byml>> source)
        {
            foreach (var (key, value) in source)
            {
                if (key == "$parent") continue;
                if (value.Type == BymlNodeType.Map && target.TryGetValue(key, out var old) && old.Type == BymlNodeType.Map)
                { var nested = new Dictionary<string, Byml>(old.GetMap()); Merge(nested, value.GetMap()); target[key] = new Byml(nested); }
                else target[key] = value;
            }
        }
    }
}
