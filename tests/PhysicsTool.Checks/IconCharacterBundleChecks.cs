using System.IO.Compression;
using System.Text.Json;
using HKCLTool;
using PhysicsTool.Core;

internal static class IconCharacterBundleChecks
{
    internal static void Verify(string output, string dump)
    {
        Directory.CreateDirectory(output);
        string empty = Path.Combine(output, "empty-profiles-" + Guid.NewGuid().ToString("N"));
        var profiles = IconCharacterProfile.LoadAll(empty);
        if (!profiles.Select(p => p.Id).SequenceEqual(new[] { "Link", "Riju", "Zelda" }))
            throw new InvalidDataException("Fresh install does not have all three characters.");
        foreach (var profile in profiles)
        {
            if (!profile.IsBuiltIn || profile.ModelPath.Length != 0 || profile.TextureRoot.Length != 0)
                throw new InvalidDataException("Built-in character still depends on local paths: " + profile.Id);
            _ = new ToolboxBfresDocument(profile.ReadModel());
            if (profile.IsLink) continue;
            foreach (string actor in profile.MaterialSources.Values.Select(s => s.Actor).Distinct())
            {
                var models = IconBuiltinProps.MaterialModels(profile.Id, actor, []);
                foreach (string material in profile.MaterialSources.Values.Where(s => s.Actor == actor).Select(s => s.Material))
                    if (!models.Any(m => m.RestPose.Meshes.Any(s => s.Name.EndsWith("__" + material))))
                        throw new InvalidDataException("Missing built-in native material: " + material);
            }
        }
        Console.WriteLine("PASS all three built-ins without profiles, source paths or external shader strings");
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty,"OldRiju.json"), "{\"Id\":\"Riju\",\"Name\":\"Old Riju\",\"ModelPath\":\"missing\",\"TextureRoot\":\"missing\"}");
        File.WriteAllText(Path.Combine(empty,"OldZelda.json"), "{\"Id\":\"Zelda\",\"Name\":\"Old Zelda\",\"ModelPath\":\"missing\",\"TextureRoot\":\"missing\"}");
        if (IconCharacterProfile.LoadAll(empty).Count != 3) throw new InvalidDataException("Legacy profiles duplicate built-ins.");
        Console.WriteLine("PASS legacy local profiles cannot replace built-in characters");
        var catalog = IconCaptureAsset.Catalog(dump);
        foreach (var profile in profiles.Where(p => !p.IsLink))
        foreach (string actor in new[] { "Armor_001_Head", "Armor_001_Upper", "Armor_001_Lower" })
        {
            var entry = catalog.First(a => a.Name == actor);
            var asset = IconCaptureAsset.Load(entry.Path, entry.ResourceRoot, character: profile);
            var props = asset.Model.Meshes.Where(m => IconArmorProps.IsProp(m.Name)).ToArray();
            int expected = profile.Id == "Riju" ? 9 : 13;
            if (props.Length != expected || props.Any(p => p.Texture == null || p.Vertices.Any(v => !float.IsFinite(v))))
                throw new InvalidDataException("Incomplete built-in props: " + profile.Id + "/" + actor);
            if (profile.Id == "Zelda" && asset.PoseName != "Icon_Pose") throw new InvalidDataException("Zelda lost her pose.");
            Console.WriteLine("PASS vanilla actor with built-in " + profile.Id + ": " + actor);
        }
    }

    internal static void Export(string profilePath, string dump, string output)
    {
        var profile = JsonSerializer.Deserialize<IconCharacterProfile>(File.ReadAllBytes(profilePath))!;
        profile.Validate();
        Directory.CreateDirectory(output);
        string root = IconCaptureAsset.Root(profile.TextureRoot);
        var actor = new BotwActorResources(Path.Combine(root, "Pack/Actor/Player.pack.zs"), [root, dump]);
        actor.PrepareTotkStrings();
        var document = new ToolboxBfresDocument(File.ReadAllBytes(profile.ModelPath));
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        document.ReadTextures(textures, actor.Warnings);
        VisualModelTexture? ReadTexture(string name) => actor.Read("TexToGo/" + name + ".txtg") is { } data
            ? TexToGoTexture.Decode(data, name) : null;
        foreach (dynamic model in document.Models)
        foreach (dynamic material in ToolboxBfresDocument.Items((object)model.Materials))
        foreach (dynamic reference in ToolboxBfresDocument.Items((object)material.TextureRefs))
        {
            string name = reference is string text ? text : (string)reference.Name;
            if (ReadTexture(profile.TextureName(name)) is { } texture) textures[name] = texture;
        }
        foreach (string name in profile.MaterialTextures.Values.SelectMany(m => m.Values).Distinct())
            textures[name] = ReadTexture(name) ?? throw new FileNotFoundException(name);
        WriteTextures(Path.Combine(output, profile.Id + ".Textures.json.gz"), textures);
        File.Copy(profile.ModelPath, Path.Combine(output, "Armor_Icon_" + profile.Id + ".sbfres"), true);
        var nativeTextures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        var models = new Dictionary<string, string[]>();
        foreach (var group in profile.MaterialSources.Values.GroupBy(s => s.Actor))
        {
            var source = new BotwActorResources(Path.Combine(root, "Pack/Actor", group.Key + ".pack.zs"), [root, dump]);
            source.PrepareTotkStrings();
            var wanted = group.Select(s => s.Material).ToHashSet(StringComparer.Ordinal);
            var found = new HashSet<string>(StringComparer.Ordinal);
            var files = new List<string>();
            foreach (var reference in source.Models)
            {
                var native = new ToolboxBfresDocument(source.ReadModel(reference.Resource)!);
                dynamic model = native.Models.Single(m => (string)m.Name == reference.Model);
                var materials = ToolboxBfresDocument.Items((object)model.Materials);
                foreach (dynamic shape in ToolboxBfresDocument.Items((object)model.Shapes).ToArray())
                    if (!wanted.Contains((string)materials[(int)shape.MaterialIndex].Name)) model.Shapes.Remove(shape);
                if (ToolboxBfresDocument.Items((object)model.Shapes).Length == 0) continue;
                var buffers = ToolboxBfresDocument.Items((object)model.VertexBuffers);
                model.Materials.Clear(); model.VertexBuffers.Clear();
                var materialIndices = new Dictionary<int, ushort>();
                var bufferIndices = new Dictionary<int, ushort>();
                foreach (dynamic shape in ToolboxBfresDocument.Items((object)model.Shapes))
                {
                    dynamic material = materials[(int)shape.MaterialIndex];
                    dynamic buffer = buffers[(int)shape.VertexBufferIndex];
                    if (!materialIndices.TryGetValue((int)shape.MaterialIndex, out ushort materialIndex))
                    {
                        materialIndices[(int)shape.MaterialIndex] = materialIndex = (ushort)model.Materials.Count;
                        model.Materials.Add(material);
                    }
                    if (!bufferIndices.TryGetValue((int)shape.VertexBufferIndex, out ushort bufferIndex))
                    {
                        bufferIndices[(int)shape.VertexBufferIndex] = bufferIndex = (ushort)model.VertexBuffers.Count;
                        model.VertexBuffers.Add(buffer);
                    }
                    shape.MaterialIndex = materialIndex;
                    shape.VertexBufferIndex = bufferIndex;
                    found.Add((string)material.Name);
                }
                dynamic resource = native.Resource;
                resource.Models.Clear(); resource.Models.Add(model);
                string file = profile.Id + "." + group.Key + "." + files.Count + ".bfres";
                resource.Save(Path.Combine(output, file));
                files.Add(file);
                TexToGoTexture.Load(native, source, nativeTextures, default);
                if (wanted.IsSubsetOf(found)) break;
            }
            if (!wanted.IsSubsetOf(found)) throw new InvalidDataException("Missing native materials: " + string.Join(',', wanted.Except(found)));
            models[group.Key] = files.ToArray();
        }
        WriteTextures(Path.Combine(output, profile.Id + ".NativeTextures.json.gz"), nativeTextures);
        File.WriteAllBytes(Path.Combine(output, profile.Id + ".Materials.json"), JsonSerializer.SerializeToUtf8Bytes(models));
        File.WriteAllBytes(Path.Combine(output, profile.Id + ".Profile.json"), JsonSerializer.SerializeToUtf8Bytes(
            profile with { ModelPath = "", TextureRoot = "" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"BUNDLED {profile.Id}: {textures.Count} prop textures, {nativeTextures.Count} native textures, {models.Sum(m => m.Value.Length)} material models");
    }

    private static void WriteTextures(string path, Dictionary<string, VisualModelTexture> textures)
    {
        var clean = textures.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => new VisualModelTexture
        {
            Name=p.Value.Name, Width=p.Value.Width, Height=p.Value.Height, Pixels=p.Value.Pixels,
            IsSrgb=p.Value.IsSrgb, IsSingleChannel=p.Value.IsSingleChannel, UseRedAsLuminance=p.Value.UseRedAsLuminance,
            AlphaMode=p.Value.AlphaMode
        });
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        JsonSerializer.Serialize(gzip, clean);
    }
}
