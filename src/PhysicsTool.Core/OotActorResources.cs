using BymlLibrary;
using HKCLTool;

namespace PhysicsTool.Core;

internal sealed record OotPhysicsAsset(string Path, byte[] Bytes, bool IsCloth)
{
    internal IReadOnlySet<string> KeepBoneLengthMeshes { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public override string ToString() => System.IO.Path.GetFileName(Path) + (IsCloth ? "  |  Cloth" : "  |  Helper bones");
}

// Actor parameters are resolved independently of model loading and shader support.
internal sealed partial class OotActorResources
{
    private readonly string _root;
    private readonly BotwSarcArchive _pack;
    private readonly Dictionary<string, BotwSarcEntry> _entries;
    private readonly Dictionary<string, byte[]> _shared;
    private readonly Dictionary<string, ResourceStamp> _dependencies = new(StringComparer.Ordinal);
    private readonly bool _catalogOnly;
    private readonly Dictionary<string, Dictionary<string, Byml>> _resolved = new(StringComparer.Ordinal);
    public List<OotPhysicsAsset> Assets { get; } = [];
    public List<string> Warnings { get; } = [];
    public string? ModelPath { get; private set; }
    public List<string> AnimationPaths { get; } = [];

    internal static string ResourceRoot(string folder)
    {
        var root = System.IO.Path.GetFullPath(folder);
        if (Directory.Exists(System.IO.Path.Combine(root, "romfs"))) root = System.IO.Path.Combine(root, "romfs");
        if (!Directory.Exists(System.IO.Path.Combine(root, "Phive", "Cloth")) ||
            !Directory.EnumerateFiles(System.IO.Path.Combine(root, "Phive", "Cloth"), "*.Nin_Ounce_NVN2.bphcl").Any())
            throw new InvalidDataException("Choose the OoT romfs containing Phive/Cloth and Pack/Actor.");
        return root;
    }

    private OotActorResources(string root, BotwSarcArchive pack, Dictionary<string, byte[]> shared, bool catalogOnly = false)
    {
        _root = root; _pack = pack; _shared = shared; _catalogOnly = catalogOnly;
        _entries = pack.Entries.ToDictionary(e => e.Name, StringComparer.Ordinal);
    }

    internal static OotActorResources Load(BotwActorCatalogEntry entry)
    {
        var root = ResourceRoot(entry.ResourceRoot);
        var compression = new TotkResourceCompression();
        var pack = BotwSarcArchive.Parse(compression.Decode(File.ReadAllBytes(entry.Path), [root]));
        var result = new OotActorResources(root, pack, ReadShared(root, compression));
        result.ResolveActor(entry.Name);
        return result;
    }

    private static IEnumerable<string> SharedPaths(string root) =>
        Directory.EnumerateFiles(System.IO.Path.Combine(root, "Pack"), "*.pack.zs")
            .Where(p => System.IO.Path.GetFileName(p).StartsWith("ResidentCommon", StringComparison.Ordinal) ||
                System.IO.Path.GetFileName(p).StartsWith("Bootup.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static Dictionary<string, byte[]> ReadShared(string root, TotkResourceCompression compression, CancellationToken token = default)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in SharedPaths(root))
        {
            token.ThrowIfCancellationRequested();
            var archive = BotwSarcArchive.Parse(compression.Decode(File.ReadAllBytes(path), [root]));
            foreach (var item in archive.Entries.Where(e => e.Name.EndsWith(".bgyml", StringComparison.Ordinal)))
                result.TryAdd(item.Name, archive.Read(item).ToArray());
        }
        return result;
    }

    private static string Normalize(string path)
    {
        path = path.TrimStart('?').Replace('\\', '/');
        if (path.StartsWith("Work/", StringComparison.Ordinal)) path = path[5..];
        if (path.EndsWith(".gyml", StringComparison.Ordinal)) path = path[..^5] + ".bgyml";
        if (System.IO.Path.IsPathRooted(path) || path.Split('/').Any(p => p is ".." or "."))
            throw new InvalidDataException("Actor reference escapes the resource folder.");
        return path;
    }

    private byte[] Read(string path)
    {
        path = Normalize(path);
        if (_entries.TryGetValue(path, out var entry)) return _pack.Read(entry).ToArray();
        if (_shared.TryGetValue(path, out var bytes)) return bytes;
        var file = System.IO.Path.Combine(_root, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        if (_catalogOnly) _dependencies[file] = ResourceStamp.Read(file);
        if (!File.Exists(file)) throw new FileNotFoundException("Missing physics resource: " + path);
        return File.ReadAllBytes(file);
    }

    private Dictionary<string, Byml> Parameters(string path, HashSet<string>? stack = null)
    {
        path = Normalize(path);
        if (_resolved.TryGetValue(path, out var cached)) return cached;
        stack ??= new(StringComparer.Ordinal);
        if (stack.Count >= 64 || !stack.Add(path)) throw new InvalidDataException("Actor parameter inheritance contains a cycle.");
        try
        {
            var map = Byml.FromBinary(Read(path)).GetMap();
            var result = map.TryGetValue("$parent", out var parent)
                ? new Dictionary<string, Byml>(Parameters(parent.GetString(), stack)) : new();
            Merge(result, map); _resolved[path] = result; return result;
        }
        finally { stack.Remove(path); }
    }

    private static void Merge(Dictionary<string, Byml> target, IDictionary<string, Byml> source)
    {
        foreach (var (key, value) in source)
        {
            if (key == "$parent") continue;
            if (value.Type == BymlNodeType.Map && target.TryGetValue(key, out var old) && old.Type == BymlNodeType.Map)
            { var nested = new Dictionary<string, Byml>(old.GetMap()); Merge(nested, value.GetMap()); target[key] = new(nested); }
            else target[key] = value;
        }
    }

    private void ResolveActor(string name)
    {
        var actor = Parameters($"Actor/{name}.engine__actor__ActorParam.bgyml");
        if (!_catalogOnly && actor.TryGetValue("Components", out var modelComponents) && modelComponents.GetMap().TryGetValue("ModelInfoRef", out var modelReference) && !string.IsNullOrWhiteSpace(modelReference.GetString()))
        {
            try
            {
                var model = Parameters(modelReference.GetString());
                if (model.TryGetValue("ModelProjectName", out var project) && model.TryGetValue("FmdbName", out var fmdb))
                {
                    string file = project.GetString() + "." + fmdb.GetString() + ".bfres.zs";
                    if (file != System.IO.Path.GetFileName(file) || file.Contains('/') || file.Contains('\\')) throw new InvalidDataException("Invalid model resource name.");
                    var candidate = System.IO.Path.Combine(_root, "Model", file);
                    if (File.Exists(candidate)) ModelPath = candidate;
                    else Warnings.Add("Model resource not found: " + file);
                    AddAnimation(project.GetString(), required: false);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { Warnings.Add("Model: " + ex.Message); }
        }
        if (!_catalogOnly && actor.TryGetValue("Components", out var animationComponents) &&
            animationComponents.GetMap().TryGetValue("AnimationRef", out var animationRef) && !string.IsNullOrWhiteSpace(animationRef.GetString()))
        {
            try
            {
                var animation = Parameters(animationRef.GetString());
                if (animation.TryGetValue("AnimationResources", out var resources))
                    foreach (var item in resources.GetArray())
                    {
                        var values = item.GetMap();
                        if (!values.TryGetValue("ModelProjectName", out var project)) continue;
                        if (values.TryGetValue("IsRetarget", out var retarget) && retarget.GetBool())
                        { Warnings.Add("Retargeted animation set is not supported: " + project.GetString()); continue; }
                        AddAnimation(project.GetString(), required: true);
                    }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { Warnings.Add("Animation: " + ex.Message); }
        }
        if (!actor.TryGetValue("Components", out var components) || !components.GetMap().TryGetValue("PhysicsRef", out var physicsRef)) return;
        if (string.IsNullOrWhiteSpace(physicsRef.GetString())) return;
        var physics = Parameters(physicsRef.GetString());
        if (!physics.TryGetValue("ControllerSetPath", out var controllerRef)) return;
        if (string.IsNullOrWhiteSpace(controllerRef.GetString())) return;
        var controller = Parameters(controllerRef.GetString());
        if (controller.TryGetValue("Cloth", out var cloths))
            foreach (var cloth in cloths.GetArray())
                if (cloth.GetMap().TryGetValue("FilePath", out var parameter) && !string.IsNullOrWhiteSpace(parameter.GetString()))
                {
                    try
                    {
                        var settings = Parameters(parameter.GetString());
                        if (!settings.TryGetValue("HktPath", out var hkt)) continue;
                        if (string.IsNullOrWhiteSpace(hkt.GetString())) continue;
                        var path = Normalize(hkt.GetString());
                        path = System.IO.Path.ChangeExtension(path, null) + ".Nin_Ounce_NVN2.bphcl";
                        var preserve = new HashSet<string>(StringComparer.Ordinal);
                        if (settings.TryGetValue("MeshList", out var meshes))
                            foreach (var mesh in meshes.GetArray())
                            {
                                var values = mesh.GetMap();
                                if (values.TryGetValue("Name", out var meshName) &&
                                    values.TryGetValue("KeepBoneLength", out var keep) && keep.GetBool())
                                    preserve.Add(meshName.GetString());
                            }
                        Add(path, true, preserve);
                    }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException) { Warnings.Add(ex.Message); }
                }
        if (controller.TryGetValue("HelperBoneList", out var helpers))
            foreach (var helper in helpers.GetArray())
                if (helper.GetMap().TryGetValue("FilePath", out var path) && !string.IsNullOrWhiteSpace(path.GetString())) Add(Normalize(path.GetString()), false);
    }

    private void Add(string path, bool cloth, IReadOnlySet<string>? keepBoneLength = null)
    {
        if (Assets.Any(a => a.Path == path)) return;
        try
        {
            if (!_catalogOnly)
            {
                Assets.Add(new(path, Read(path), cloth) { KeepBoneLengthMeshes = keepBoneLength ?? new HashSet<string>(StringComparer.Ordinal) });
                return;
            }
            path = Normalize(path);
            if (!_entries.ContainsKey(path) && !_shared.ContainsKey(path))
            {
                var file = System.IO.Path.Combine(_root, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                var stamp = _dependencies[file] = ResourceStamp.Read(file);
                if (!stamp.Exists) throw new FileNotFoundException("Missing physics resource: " + path);
            }
            Assets.Add(new(path, [], cloth));
        }
        catch (IOException ex) { Warnings.Add(ex.Message); }
    }

    private void AddAnimation(string project, bool required)
    {
        if (string.IsNullOrWhiteSpace(project)) return;
        if (project != System.IO.Path.GetFileName(project) || project.Contains('/') || project.Contains('\\'))
            throw new InvalidDataException("Invalid animation resource name.");
        var path = System.IO.Path.Combine(_root, "Model", project + ".anim.bfres.zs");
        if (File.Exists(path))
        {
            if (!AnimationPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) AnimationPaths.Add(path);
        }
        else if (required) Warnings.Add("Animation resource not found: " + project);
    }
}
