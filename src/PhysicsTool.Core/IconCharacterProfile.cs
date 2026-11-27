using System.Text.Json;

namespace PhysicsTool.Core;

internal sealed record IconCharacterProfile(string Id, string Name, string ModelPath, string TextureRoot)
{
    public Dictionary<string, string> TexturePrefixes { get; init; } = new();
    public Dictionary<string, Dictionary<string, string>> MaterialTextures { get; init; } = new();
    public Dictionary<string, string> NeckSplices { get; init; } = new();
    public sealed record MaterialSource(string Actor, string Material);
    public Dictionary<string, MaterialSource> MaterialSources { get; init; } = new();
    public Dictionary<string, string> TextureColors { get; init; } = new();
    internal bool IsBuiltIn { get; init; }
    internal bool IsLink => Id == "Link";
    internal static IconCharacterProfile Link => new("Link", "Link", "", "") { IsBuiltIn = true };
    internal static IReadOnlyList<IconCharacterProfile> BuiltIns => [Link, IconBuiltinProps.Profile("Riju"), IconBuiltinProps.Profile("Zelda")];
    internal static bool IsBuiltInId(string id) => new[] { "Link", "Riju", "Zelda" }.Contains(id, StringComparer.OrdinalIgnoreCase);
    internal string ModelSource => IsBuiltIn ? "Built-in/" + Id : ModelPath;
    internal byte[] ReadModel() => IsBuiltIn
        ? IconBuiltinProps.Model(IsLink ? "Armor_Icon.sbfres" : "Armor_Icon_" + Id + ".sbfres")
        : File.ReadAllBytes(ModelPath);
    internal static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhysicsTool", "IconTool", "Characters");
    public override string ToString() => Name;

    internal static IReadOnlyList<IconCharacterProfile> LoadAll(string? folder = null)
    {
        var profiles = BuiltIns.ToList();
        folder ??= Folder;
        if (!Directory.Exists(folder)) return profiles;
        foreach (var path in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            var profile = JsonSerializer.Deserialize<IconCharacterProfile>(File.ReadAllBytes(path))
                ?? throw new InvalidDataException("Empty character profile: " + path);
            // Earlier builds installed these as machine-local profiles. Built-ins
            // take precedence without touching those original user files.
            if (IsBuiltInId(profile.Id)) continue;
            profile.Validate();
            if (profiles.Any(p => p.Id == profile.Id)) throw new InvalidDataException("Duplicate character profile: " + profile.Id);
            profiles.Add(profile);
        }
        return profiles;
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) ||
            Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Id is "." or ".." ||
            (IsBuiltIn ? !IsBuiltInId(Id) || ModelPath.Length != 0 || TextureRoot.Length != 0 :
                !Path.IsPathFullyQualified(ModelPath) || !Path.IsPathFullyQualified(TextureRoot)) ||
            TexturePrefixes == null || TexturePrefixes.Any(p => string.IsNullOrEmpty(p.Key) || string.IsNullOrEmpty(p.Value)) ||
            NeckSplices == null || NeckSplices.Any(p => string.IsNullOrEmpty(p.Key) || string.IsNullOrEmpty(p.Value)) ||
            MaterialSources == null || MaterialSources.Any(p => string.IsNullOrEmpty(p.Key) || p.Value == null || string.IsNullOrEmpty(p.Value.Actor) || string.IsNullOrEmpty(p.Value.Material)) ||
            TextureColors == null || TextureColors.Any(p => string.IsNullOrEmpty(p.Key) || p.Value == null || p.Value.Length != 8 || !p.Value.All(Uri.IsHexDigit)) ||
            MaterialTextures == null || MaterialTextures.Any(p => string.IsNullOrEmpty(p.Key) || p.Value == null || p.Value.Any(t => string.IsNullOrEmpty(t.Key) || string.IsNullOrEmpty(t.Value))))
            throw new InvalidDataException("Invalid mannequin character profile.");
    }

    internal void Save()
    {
        Validate();
        if (IsBuiltInId(Id)) throw new InvalidOperationException("Built-in characters cannot be replaced. Use a different name for a custom character.");
        Directory.CreateDirectory(Folder);
        HKCLTool.AtomicFile.WriteAllBytes(Path.Combine(Folder, Id + ".json"), JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal string TextureName(string name)
    {
        foreach (var pair in TexturePrefixes.OrderByDescending(p => p.Key.Length))
            if (name.StartsWith(pair.Key, StringComparison.Ordinal)) return pair.Value + name[pair.Key.Length..];
        return name;
    }

    internal void ApplyTextureColors(Dictionary<string, HKCLTool.VisualModelTexture> textures)
    {
        foreach (var (name, hex) in TextureColors)
        {
            if (!textures.TryGetValue(name, out var source)) continue;
            var color=Convert.FromHexString(hex);
            var pixels=(byte[])source.Pixels.Clone();
            for(int i=0;i<pixels.Length;i+=4)
            {
                pixels[i]=color[0]; pixels[i+1]=color[1]; pixels[i+2]=color[2];
                pixels[i+3]=(byte)((pixels[i+3]*color[3]+127)/255);
            }
            textures[name]=new() { Name=name, SourcePath=source.SourcePath, Width=source.Width, Height=source.Height,
                IsSrgb=true, Pixels=pixels, AlphaMode=source.AlphaMode };
        }
    }
}
