namespace HKCLTool;

internal sealed record BotwActorModelReference(string Resource, string Model, string BindBone);
internal sealed record BotwActorAnimationReference(string State, string Clip);

internal sealed class BotwActorResources
{
    private readonly BotwSarcArchive _pack;
    private readonly Dictionary<string, BotwSarcEntry> _entries;
    private readonly BotwSarcArchive? _container;
    private readonly Dictionary<string, BotwSarcArchive?> _titleArchives = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _roots = new();
    public string ActorName { get; }
    public string SourcePath { get; }
    public string? SourceEntry { get; }
    public bool IsSwitch => _pack.LittleEndian;
    public IReadOnlyList<string> Roots => _roots;
    public List<string> Warnings { get; } = new();
    public List<BotwActorModelReference> Models { get; } = new();
    public List<string> AnimationResources { get; } = new();
    public List<BotwActorAnimationReference> Animations { get; } = new();
    public List<string> PhysicsFiles { get; } = new();
    public byte[]? PhysicsParameters { get; private set; }

    public BotwActorResources(string path, IEnumerable<string>? roots = null, string? actorEntry = null)
    {
        SourcePath = Path.GetFullPath(path);
        SourceEntry = actorEntry;
        ActorName = Path.GetFileNameWithoutExtension(actorEntry ?? path);
        var archive = BotwSarcArchive.OpenIndexed(path);
        if (actorEntry != null)
        {
            _container = archive;
            var entry = archive.Entries.FirstOrDefault(e => e.Name == actorEntry) ?? throw new InvalidDataException("The selected actor is not in this archive.");
            _pack = BotwSarcArchive.Parse(archive.Read(entry).Span);
        }
        else _pack = archive;
        _entries = _pack.Entries.ToDictionary(e => e.Name.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        for (var folder = new DirectoryInfo(Path.GetDirectoryName(SourcePath)!); folder != null; folder = folder.Parent)
        {
            if (Directory.Exists(Path.Combine(folder.FullName, "Model")) || File.Exists(Path.Combine(folder.FullName, "Pack", "TitleBG.pack"))) AddRoot(folder.FullName);
            if (Directory.Exists(Path.Combine(folder.FullName, "content", "Model"))) AddRoot(Path.Combine(folder.FullName, "content"));
        }
        if (roots != null)
            foreach (var root in roots) AddRoot(root);
        if (!IsSwitch)
        {
            foreach (var content in _roots.Where(r => Path.GetFileName(r) == "content").ToArray())
            {
                var parent = Directory.GetParent(content)?.Parent;
                if (parent == null) continue;
                foreach (var sibling in parent.EnumerateDirectories())
                {
                    var candidate = Path.Combine(sibling.FullName, "content");
                    if (Directory.Exists(Path.Combine(candidate, "Model"))) AddRoot(candidate);
                }
            }
        }
        AddRoot(Path.GetDirectoryName(SourcePath)!);
        ReadReferences();
    }

    private void AddRoot(string path)
    {
        var root = Path.GetFullPath(path);
        if (Directory.Exists(Path.Combine(root, "content"))) root = Path.Combine(root, "content");
        if (!_roots.Contains(root, StringComparer.OrdinalIgnoreCase)) _roots.Add(root);
    }

    public byte[]? Read(string relativePath)
    {
        var relative = relativePath.Replace('\\', '/').TrimStart('/');
        if (Path.IsPathRooted(relative) || relative.Split('/').Any(p => p is ".." or "."))
            throw new InvalidDataException("An actor resource has an unsafe relative path.");
        if (_entries.TryGetValue(relative, out var entry)) return _pack.Read(entry).ToArray();
        if (_container?.Entries.FirstOrDefault(e => e.Name.Equals(relative, StringComparison.OrdinalIgnoreCase)) is { } embedded)
            return _container.Read(embedded).ToArray();
        foreach (var root in _roots)
        {
            var candidate = Path.GetFullPath(Path.Combine(root, relative));
            if (!candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("An actor resource lies outside its resource folder.");
            if (File.Exists(candidate)) return File.ReadAllBytes(candidate);
            var titlePath = Path.Combine(root, "Pack", "TitleBG.pack");
            if (!_titleArchives.TryGetValue(titlePath, out var title))
            {
                title = null;
                if (File.Exists(titlePath))
                {
                    try
                    {
                        title = BotwSarcArchive.OpenIndexed(titlePath);
                        if (title.LittleEndian != IsSwitch) { Warnings.Add("Skipped TitleBG.pack from a different game platform."); title = null; }
                    }
                    catch (Exception ex) { Warnings.Add("TitleBG.pack: " + UserFacingExceptionFormatter.Format(ex)); }
                }
                _titleArchives[titlePath] = title;
            }
            if (title?.Entries.FirstOrDefault(e => e.Name.Equals(relative, StringComparison.OrdinalIgnoreCase)) is { } titleEntry)
                return title.Read(titleEntry).ToArray();
        }
        return null;
    }

    public byte[]? ReadModel(string resource, string suffix = "") =>
        Read($"Model/{resource}{suffix}.sbfres") ?? Read($"Model/{resource}{suffix}.bfres");

    private AampReadList? Parameters(string folder, string name, string extension)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "Dummy") return null;
        var path = $"Actor/{folder}/{name}.{extension}";
        var bytes = Read(path);
        if (bytes == null) { Warnings.Add($"Missing {path}"); return null; }
        return BotwAampReader.Read(bytes).Root;
    }

    private void ReadReferences()
    {
        var link = Parameters("ActorLink", ActorName, "bxml");
        var target = link?.Objects.FirstOrDefault(o => o.Hash == BotwAampWriter.Crc32("LinkTarget"));
        string Link(string key, string folder, string extension) => target?.Find(key)?.AsString()
            ?? _entries.Keys.Where(k => k.StartsWith($"Actor/{folder}/", StringComparison.OrdinalIgnoreCase) && k.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension).FirstOrDefault() ?? "Dummy";
        var modelList = Parameters("ModelList", Link("ModelUser", "ModelList", ".bmodellist"), "bmodellist");
        if (modelList != null)
        {
            foreach (var data in modelList.FindChild("ModelData")?.Children ?? Enumerable.Empty<AampReadList>())
            {
                var resource = data.Objects.FirstOrDefault(o => o.Hash == BotwAampWriter.Crc32("Base"))?.Find("Folder")?.AsString();
                if (string.IsNullOrEmpty(resource)) continue;
                foreach (var unit in data.FindChild("Unit")?.Objects ?? Enumerable.Empty<AampReadObject>())
                {
                    var name = unit.Find("UnitName")?.AsString();
                    if (!string.IsNullOrEmpty(name)) Models.Add(new(resource, name, unit.Find("BindBone")?.AsString() ?? ""));
                }
            }
        }
        var asList = Parameters("ASList", Link("ASUser", "ASList", ".baslist"), "baslist");
        if (asList != null)
        {
            foreach (var obj in Objects(asList.FindChild("AddReses")))
            {
                var name = obj.Find("Anim")?.AsString();
                if (!string.IsNullOrEmpty(name) && name != "Dummy" && !AnimationResources.Contains(name)) AnimationResources.Add(name);
            }
            foreach (var obj in Objects(asList.FindChild("ASDefines")))
            {
                var state = obj.Find("Name")?.AsString();
                var file = obj.Find("Filename")?.AsString();
                if (string.IsNullOrEmpty(file)) continue;
                var bas = Parameters("AS", file, "bas");
                foreach (var animation in Objects(bas))
                {
                    var clip = animation.Find("FileName")?.AsString();
                    if (!string.IsNullOrEmpty(clip)) Animations.Add(new(state ?? file, clip));
                }
            }
        }
        var physicsName = Link("PhysicsUser", "Physics", ".bphysics");
        if (physicsName != "Dummy")
        {
            PhysicsParameters = Read($"Actor/Physics/{physicsName}.bphysics");
            if (PhysicsParameters != null)
            {
                var physics = BotwAampReader.Read(PhysicsParameters).Root;
                foreach (var value in Objects(physics).SelectMany(o => o.Parameters).Where(p => p.Type is 7 or 8 or 15 or 20).Select(p => p.AsString()))
                {
                    var folder = Path.GetExtension(value).ToLowerInvariant() switch { ".hkcl" => "Cloth", ".bphyssb" => "SupportBone", ".hkrg" => "Ragdoll", _ => null };
                    if (folder != null) PhysicsFiles.Add(value.StartsWith("Physics/") ? value : $"Physics/{folder}/{value}");
                }
            }
        }
        // Some packs omit the optional profile link but still embed their setup files.
        foreach (var file in _entries.Keys.Where(k => k.EndsWith(".hkcl") || k.EndsWith(".bphyssb") || k.EndsWith(".hkrg")))
            if (!PhysicsFiles.Contains(file, StringComparer.OrdinalIgnoreCase)) PhysicsFiles.Add(file);
        if (Models.Count == 0) throw new InvalidDataException("This actor has no viewable model reference in its model list.");
    }

    internal static IEnumerable<AampReadObject> Objects(AampReadList? list)
    {
        if (list == null) yield break;
        foreach (var obj in list.Objects) yield return obj;
        foreach (var child in list.Children)
        foreach (var obj in Objects(child)) yield return obj;
    }
}
