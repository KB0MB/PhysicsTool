using BymlLibrary;
using Res = Syroot.NintenTools.NSW.Bfres;

namespace HKCLTool;

internal static class OotModelTextures
{
    // The game can redirect material texture names to shared BNTX companions.
    internal static void Load(ToolboxBfresDocument document, string path, Dictionary<string, VisualModelTexture> textures, List<string> warnings)
    {
        if (document.Resource is not Res.ResFile resource || !resource.Models.Any(m => m.Materials.Any(ToolboxOotMaterial.IsOot))) return;
        var root = TotkResourceCompression.Roots(path).FirstOrDefault(r => Directory.Exists(Path.Combine(r, "Tex")));
        if (root == null) { warnings.Add("OoT textures: keep the model's Model and Tex folders together."); return; }
        var compression = new TotkResourceCompression();
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = resource.Models.SelectMany(m => m.Materials).SelectMany(m => m.TextureRefs).Distinct().ToArray();
        for (int i = 0; i < resource.ExternalFileDict.Count; i++)
            if (resource.ExternalFileDict.GetKey(i) == "textureTable.byml")
                foreach (var pair in Byml.FromBinary(resource.ExternalFiles[i].Data).GetMap()) files[pair.Key] = pair.Value.GetString();
        var gfx = Path.Combine(root, "System", "Gfx");
        if (files.Count == 0 && Directory.Exists(gfx))
            foreach (var file in Directory.EnumerateFiles(gfx, "UnifyTex.Product.*.byml.zs").Order())
                foreach (var pair in Byml.FromBinary(compression.Decode(File.ReadAllBytes(file), [root])).GetMap())
                {
                    string target = pair.Value.GetString();
                    if (aliases.TryGetValue(pair.Key, out var previous) && previous != target) throw new InvalidDataException("Conflicting texture aliases: " + pair.Key);
                    aliases[pair.Key] = target;
                }
        var decoded = new Dictionary<string, VisualModelTexture>(StringComparer.Ordinal);
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (textures.ContainsKey(name)) continue;
            try
            {
                string target = name;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                while (!File.Exists(TexturePath(target)) && aliases.TryGetValue(target, out var next))
                { if (!seen.Add(target)) throw new InvalidDataException("Cyclic texture alias: " + name); target = next; }
                var file = TexturePath(files.GetValueOrDefault(name, target));
                if (loaded.Add(file))
                {
                    using var stream = new MemoryStream(compression.Decode(File.ReadAllBytes(file), [root]));
                    var bntx = new Syroot.NintenTools.NSW.Bntx.BntxFile(stream);
                    foreach (var tex in bntx.Textures)
                    {
                        var image = ToolboxActorTexture.Decode(tex, orin: bntx.PlatformTarget == "Ounc");
                        decoded[tex.Name] = new VisualModelTexture { Name = image.Name, SourcePath = file, Width = image.Width, Height = image.Height,
                            Pixels = image.Pixels, AlphaMode = image.AlphaMode, IsSrgb = tex.Format.ToString().Contains("SRGB", StringComparison.OrdinalIgnoreCase) };
                    }
                }
                if (decoded.TryGetValue(name, out var texture) || decoded.TryGetValue(target, out texture)) textures[name] = texture;
                else warnings.Add("BNTX does not contain texture " + name);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or NotSupportedException)
            { warnings.Add($"Texture {name}: {ex.Message}"); }
        }
        string TexturePath(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.Contains('/') || name.Contains('\\') || name is "." or "..")
                throw new InvalidDataException("Invalid companion texture name.");
            return Path.Combine(root, "Tex", name.EndsWith(".bntx.zs", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".bntx", StringComparison.OrdinalIgnoreCase) ? name : name + ".bntx.zs");
        }
    }
}
