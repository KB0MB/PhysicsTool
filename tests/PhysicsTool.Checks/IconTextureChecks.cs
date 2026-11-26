using HKCLTool;
using PhysicsTool.Core;
using ResU = Syroot.NintenTools.Bfres;

internal static class IconTextureChecks
{
    internal static void Run(string path, string output, bool inspect)
    {
        var template = File.ReadAllBytes(path);
        var document = new ToolboxBfresDocument(template);
        if (document.Resource is ResU.ResFile wii)
        {
            var texture = wii.Textures.Values.Single();
            foreach (var property in typeof(ResU.Texture).GetProperties().Where(p => p.PropertyType.IsEnum || p.PropertyType.IsPrimitive))
                Console.WriteLine(property.Name + "=" + property.GetValue(texture));
        }
        if (inspect) return;
        Directory.CreateDirectory(output);
        foreach (int size in new[] { 96, 144, 256, 512 })
        {
            byte[] pixels = Enumerable.Range(0, size * size * 4).Select(i => (byte)(i * 17 + i / (size * 4))).ToArray();
            string name = "IconRoundtrip" + size;
            var bytes = IconItemTexture.Encode(template, name, pixels, size, size);
            var exported = new ToolboxBfresDocument(bytes);
            var textures = new Dictionary<string, VisualModelTexture>(); exported.ReadTextures(textures, new());
            var image = textures.Values.Single();
            if (image.Name != name || image.Width != size || image.Height != size || !image.IsSrgb || !image.Pixels.SequenceEqual(pixels))
                throw new InvalidOperationException("Pixel/alpha/layout roundtrip failed: " + name);
            File.WriteAllBytes(Path.Combine(output, name + ".sbitemico"), bytes);
            Console.WriteLine("PASS icon template conversion, exact pixels/alpha: " + size);
        }
    }
}
