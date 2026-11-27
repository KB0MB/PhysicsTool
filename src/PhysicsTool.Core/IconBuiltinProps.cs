using System.IO.Compression;
using System.Text.Json;
using HKCLTool;

namespace PhysicsTool.Core;

internal static class IconBuiltinProps
{
    internal static Stream Open(string name) => typeof(IconBuiltinProps).Assembly
        .GetManifestResourceStream("PhysicsTool.IconProps." + name)
        ?? throw new InvalidDataException("Built-in icon prop resource is missing: " + name);

    internal static byte[] Model(string name)
    {
        using var stream = Open(name);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    internal static IconCharacterProfile Profile(string id)
    {
        using var stream = Open(id + ".Profile.json");
        var profile = (JsonSerializer.Deserialize<IconCharacterProfile>(stream)
            ?? throw new InvalidDataException("Built-in character definition is empty: " + id)) with { IsBuiltIn = true };
        if (profile.Id != id) throw new InvalidDataException("Built-in character ID mismatch: " + id);
        profile.Validate();
        return profile;
    }

    internal static IReadOnlyList<ToolboxActorModel> MaterialModels(string id, string actor, List<string> warnings)
    {
        using var stream = Open(id + ".Materials.json");
        var entries = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidDataException("Built-in native material list is empty: " + id);
        if (!entries.TryGetValue(actor, out var files)) throw new InvalidDataException("Built-in native material actor is missing: " + actor);
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        LoadTextures(textures, id + ".NativeTextures.json.gz");
        var models = new List<ToolboxActorModel>();
        foreach (string file in files)
        {
            var document = new ToolboxBfresDocument(Model(file));
            foreach (dynamic model in document.Models)
                models.Add(new(document, (string)model.Name, textures, warnings: warnings));
        }
        return models;
    }

    internal static void LoadTextures(Dictionary<string, VisualModelTexture> textures, string resource = "Textures.json.gz")
    {
        using var stream = Open(resource);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        var bundled = JsonSerializer.Deserialize<Dictionary<string, VisualModelTexture>>(gzip)
            ?? throw new InvalidDataException("Built-in icon prop textures are empty.");
        foreach (var (name, texture) in bundled)
        {
            if (texture.Width <= 0 || texture.Height <= 0 || texture.Pixels.Length != checked(texture.Width * texture.Height * 4))
                throw new InvalidDataException("Invalid built-in icon prop texture: " + name);
            textures.TryAdd(name, texture);
        }
    }
}
