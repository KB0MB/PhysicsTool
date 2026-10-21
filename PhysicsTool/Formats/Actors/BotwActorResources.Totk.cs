using BymlLibrary;
using System.Buffers.Binary;

namespace HKCLTool;

internal sealed partial class BotwActorResources
{
    private readonly TotkResourceCompression _totkCompression = new();
    private Dictionary<string, byte[]>? _totkSharedParameters;

    private byte[]? ReadTotkParameter(string path)
    {
        if (Read(path) is { } direct) return direct;
        if (_totkSharedParameters == null)
        {
            _totkSharedParameters = new(StringComparer.OrdinalIgnoreCase);
            foreach (var root in _roots)
            foreach (var name in new[] { "ResidentCommon.pack.zs", "Bootup.Nin_NX_NVN.pack.zs" })
            {
                var file = Path.Combine(root, "Pack", name);
                if (!File.Exists(file)) continue;
                var archive = BotwSarcArchive.Parse(_totkCompression.Decode(File.ReadAllBytes(file), _roots));
                foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".bgyml", StringComparison.OrdinalIgnoreCase)))
                    _totkSharedParameters.TryAdd(entry.Name, archive.Read(entry).ToArray());
            }
        }
        return _totkSharedParameters.GetValueOrDefault(path);
    }

    private static string ReferencePath(string path)
    {
        path = path.TrimStart('?').Replace('\\', '/');
        if (path.StartsWith("Work/", StringComparison.Ordinal)) path = path[5..];
        if (path.EndsWith(".gyml", StringComparison.Ordinal)) path = path[..^5] + ".bgyml";
        return path;
    }

    private Dictionary<string, Byml> TotkParameters(string path, HashSet<string>? chain = null)
    {
        path = ReferencePath(path);
        chain ??= new(StringComparer.OrdinalIgnoreCase);
        if (chain.Count >= 64 || !chain.Add(path)) throw new InvalidDataException("TotK actor parameters contain an inheritance cycle.");
        try
        {
            var bytes = ReadTotkParameter(path) ?? throw new InvalidDataException($"Missing actor parameter {Path.GetFileName(path)}.");
            var map = Byml.FromBinary(bytes).GetMap();
            var result = map.TryGetValue("$parent", out var parent) ? TotkParameters(parent.GetString(), chain) : new();
            Merge(result, map);
            return result;
        }
        finally { chain.Remove(path); }
    }

    private static void Merge(Dictionary<string, Byml> target, IEnumerable<KeyValuePair<string, Byml>> source)
    {
        foreach (var (key, value) in source)
        {
            if (key == "$parent") continue;
            if (value.Type == BymlNodeType.Map && target.TryGetValue(key, out var old) && old.Type == BymlNodeType.Map)
            {
                var combined = new Dictionary<string, Byml>(old.GetMap());
                Merge(combined, value.GetMap()); target[key] = new Byml(combined);
            }
            else target[key] = value;
        }
    }

    private void ReadTotkReferences()
    {
        var actor = TotkParameters($"Actor/{ActorName}.engine__actor__ActorParam.bgyml");
        var components = actor["Components"].GetMap();
        if (!components.TryGetValue("ModelInfoRef", out var modelRef)) throw new InvalidDataException("This TotK actor has no model reference.");
        var model = TotkParameters(modelRef.GetString());
        var project = model["ModelProjectName"].GetString();
        var name = model["FmdbName"].GetString();
        Models.Add(new(project + "." + name, name, ""));
        AnimationResources.Add(project + ".anim");
        if (components.TryGetValue("AnimationRef", out var animationRef))
        {
            var animation = TotkParameters(animationRef.GetString());
            if (animation.TryGetValue("AnimationResources", out var resources))
                foreach (var item in resources.GetArray())
                {
                    var values = item.GetMap();
                    if (!values.TryGetValue("ModelProjectName", out var value)) continue;
                    if (values.TryGetValue("IsRetarget", out var retarget) && retarget.GetBool())
                    { Warnings.Add($"Retargeted animation set {value.GetString()} is not supported yet."); continue; }
                    var resource = value.GetString() + ".anim";
                    if (!AnimationResources.Contains(resource)) AnimationResources.Add(resource);
                }
        }
        ReadTotkPhysicsReferences(components);
        Warnings.Add("TotK preview uses approximate lighting and cloth reactions; animation state machines and generated material expressions are not reproduced.");
    }

    private void ReadTotkPhysicsReferences(IDictionary<string, Byml> components)
    {
        if (!components.TryGetValue("PhysicsRef", out var physicsRef) || string.IsNullOrWhiteSpace(physicsRef.GetString())) return;
        var physics = TotkParameters(physicsRef.GetString());
        if (!physics.TryGetValue("ControllerSetPath", out var controllerPath) || string.IsNullOrWhiteSpace(controllerPath.GetString())) return;
        var controller = TotkParameters(controllerPath.GetString());
        PhysicsFiles.AddRange(ResolveTotkPhysicsFiles(controller, TotkParametersForReference));
    }

    private Dictionary<string, Byml> TotkParametersForReference(string path) => TotkParameters(path);

    internal static IReadOnlyList<string> ResolveTotkPhysicsFiles(Dictionary<string, Byml> controller,
        Func<string, Dictionary<string, Byml>> parameters)
    {
        var files = new List<string>();
        void Add(string path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = Path.ChangeExtension(ReferencePath(path), extension).Replace('\\', '/');
            if (!files.Contains(path, StringComparer.OrdinalIgnoreCase)) files.Add(path);
        }
        // Inherited packs contain alternate forms too. Only the resolved controller's
        // lists are active; an explicit empty list disables that component.
        if (controller.TryGetValue("ClothList", out var cloths))
        {
            foreach (var item in cloths.GetArray())
                if (item.GetMap().TryGetValue("Path", out var path)) Add(path.GetString(), ".bphcl");
        }
        else if (controller.TryGetValue("Cloth", out var clothParameters))
        {
            foreach (var item in clothParameters.GetArray())
                if (item.GetMap().TryGetValue("FilePath", out var file) && !string.IsNullOrWhiteSpace(file.GetString()) &&
                    parameters(file.GetString()).TryGetValue("HktPath", out var path)) Add(path.GetString(), ".bphcl");
        }
        if (controller.TryGetValue("HelperBoneList", out var helpers))
            foreach (var item in helpers.GetArray())
                if (item.GetMap().TryGetValue("FilePath", out var path)) Add(path.GetString(), ".bphhb");
        return files;
    }

    public void PrepareTotkStrings()
    {
        if (!IsTotk) return;
        var bytes = Read("Shader/ExternalBinaryString.bfres");
        if (bytes == null && Read("Shader/ExternalBinaryString.bfres.mc") is { } packed)
        {
            if (packed.Length < 13 || !packed.AsSpan().StartsWith("MCPK"u8)) throw new InvalidDataException("Invalid external string package.");
            // The string-only MCPK contains a magicless Zstandard frame, no GPU stream.
            var frame = new byte[packed.Length - 8];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, 0xfd2fb528);
            packed.AsSpan(12).CopyTo(frame.AsSpan(4));
            bytes = _totkCompression.Decode(frame, _roots);
        }
        if (bytes == null) throw new InvalidDataException("TotK model loading requires Shader/ExternalBinaryString.bfres.mc from the matching romfs.");
        _ = new ToolboxBfresDocument(bytes);
    }
}
