using System.IO.Compression;
using System.Text.Json;
using HKCLTool;
using PhysicsTool.Core;

internal static class IconBuiltinPropsChecks
{
    internal static void Verify()
    {
        var textures = new Dictionary<string, VisualModelTexture>();
        IconBuiltinProps.LoadTextures(textures);
        var props = new ToolboxBfresDocument(IconBuiltinProps.Model("Armor_Icon.sbfres"));
        foreach (dynamic model in props.Models)
        foreach (dynamic material in ToolboxBfresDocument.Items((object)model.Materials))
        foreach (dynamic reference in ToolboxBfresDocument.Items((object)material.TextureRefs))
        {
            string name = reference is string text ? text : (string)reference.Name;
            if (!textures.ContainsKey(name)) throw new InvalidDataException("Unbundled prop texture: " + name);
        }
        if (textures.Values.Any(t => t.SourcePath.Length != 0)) throw new InvalidDataException("Developer texture paths in bundle.");
        var eyes = IconMannequinEyes.Load([]);
        if (eyes.RestPose.Meshes.Count != 2) throw new InvalidDataException("Bundled filled eyes are missing.");
        Console.WriteLine("PASS built-in mannequin, filled eyes and complete texture dependencies");
    }

    internal static void Export(string modelPath, string root, string output)
    {
        Directory.CreateDirectory(output);
        var actor = IconCaptureAsset.Catalog(root).First(a => a.Name == "Armor_001_Upper");
        var source = new BotwActorResources(actor.Path, [actor.ResourceRoot]);
        var props = new ToolboxBfresDocument(File.ReadAllBytes(modelPath));
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        props.ReadTextures(textures, source.Warnings);
        foreach (string resource in new[] { "Link", "Armor_001" })
            if (source.ReadModel(resource, source.IsSwitch ? ".Tex" : ".Tex1") is { } bytes)
                new ToolboxBfresDocument(bytes).ReadTextures(textures, source.Warnings);
        var eyes = new ToolboxBfresDocument(source.ReadModel("Link") ?? throw new FileNotFoundException("Link"));
        dynamic link = eyes.Models.First(m => (string)m.Name == "Link");
        foreach (dynamic shape in ToolboxBfresDocument.Items((object)link.Shapes).ToArray())
            if (!((string)shape.Name).StartsWith("Eye_", StringComparison.Ordinal)) link.Shapes.Remove(shape);
        if (ToolboxBfresDocument.Items((object)link.Shapes).Length != 2) throw new InvalidDataException("Expected two eye meshes.");
        var buffers = ToolboxBfresDocument.Items((object)link.VertexBuffers);
        var materials = ToolboxBfresDocument.Items((object)link.Materials);
        link.VertexBuffers.Clear();
        link.Materials.Clear();
        foreach (dynamic shape in ToolboxBfresDocument.Items((object)link.Shapes))
        {
            dynamic buffer = buffers[(int)shape.VertexBufferIndex];
            dynamic material = materials[(int)shape.MaterialIndex];
            shape.VertexBufferIndex = (ushort)link.VertexBuffers.Count;
            shape.MaterialIndex = (ushort)link.Materials.Count;
            link.VertexBuffers.Add(buffer);
            link.Materials.Add(material);
        }
        ((dynamic)eyes.Resource).Save(Path.Combine(output, "LinkEyes.bfres"));
        File.Copy(modelPath, Path.Combine(output, "Armor_Icon.sbfres"), true);
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (dynamic model in props.Models)
        foreach (dynamic material in ToolboxBfresDocument.Items((object)model.Materials))
        foreach (dynamic reference in ToolboxBfresDocument.Items((object)material.TextureRefs))
            needed.Add(reference is string text ? text : (string)reference.Name);
        var selected = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in needed.Order(StringComparer.Ordinal))
        {
            if (!textures.TryGetValue(name, out var t)) throw new FileNotFoundException("Missing bundled prop texture: " + name);
            selected[name] = new() { Name=t.Name, Width=t.Width, Height=t.Height, Pixels=t.Pixels,
                IsSrgb=t.IsSrgb, IsSingleChannel=t.IsSingleChannel, UseRedAsLuminance=t.UseRedAsLuminance, AlphaMode=t.AlphaMode };
            Console.WriteLine("TEXTURE " + name);
        }
        using var file = File.Create(Path.Combine(output, "Textures.json.gz"));
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        JsonSerializer.Serialize(gzip, selected);
        Console.WriteLine("Exported built-in mannequin, filled eyes and " + selected.Count + " textures.");
    }
}
