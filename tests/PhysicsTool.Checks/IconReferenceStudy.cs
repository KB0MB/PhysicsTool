using HKCLTool;
using SkiaSharp;

internal static class IconReferenceStudy
{
    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        var animationPath = Path.Combine(root, "Model", "Player_Animation.sbfres");
        if (File.Exists(animationPath))
        {
            var document = new ToolboxBfresDocument(File.ReadAllBytes(animationPath));
            dynamic clip = document.SkeletalAnimations.First(a => string.Equals((string)a.Name, "Nml_Wait", StringComparison.OrdinalIgnoreCase));
            string bfska = Path.Combine(output, document.IsSwitch ? "switch-wait.bfska" : "wiiu-wait.bfska");
            clip.Export(bfska, (dynamic)document.Resource);
            Console.WriteLine("BFSKA HEADER " + Convert.ToHexString(File.ReadAllBytes(bfska).AsSpan(0, 32)));
            var imported = PhysicsTool.Core.IconBfska.Load(File.ReadAllBytes(bfska));
            if (imported.Name != (string)clip.Name || imported.FrameCount != (int)clip.FrameCount) throw new Exception("BFSKA metadata round trip failed");
            Console.WriteLine("PASS BFSKA native import: " + imported.Name);
        }
        string[] names = ["Armor_009_Upper", "Armor_009_Lower", "Armor_001_Upper", "Armor_006_Upper",
            "Armor_017_Upper", "Armor_020_Upper", "Armor_048_Upper", "Armor_009_Head",
            "Weapon_Sword_070", "Weapon_Sword_001", "Weapon_Bow_023", "Weapon_Shield_001"];
        using var sheet = new SKBitmap(1024, 840);
        using var canvas = new SKCanvas(sheet);
        canvas.Clear(new SKColor(36, 41, 41));
        using var label = new SKPaint { Color = SKColors.White, TextSize = 14, IsAntialias = true };
        using var nearest = new SKPaint { FilterQuality = SKFilterQuality.None };
        for (int i = 0; i < names.Length; i++)
        {
            string path = Path.Combine(root, "UI", "StockItem", names[i] + ".sbitemico");
            if (!File.Exists(path)) continue;
            var textures = new Dictionary<string, VisualModelTexture>();
            new ToolboxBfresDocument(File.ReadAllBytes(path)).ReadTextures(textures, new List<string>());
            var texture = textures.Values.First();
            var png = PhysicsTool.Desktop.IconToolView.Encode(texture.Pixels, texture.Width, texture.Height, false);
            File.WriteAllBytes(Path.Combine(output, names[i] + ".png"), png);
            using var bitmap = SKBitmap.Decode(png);
            int x = i % 4 * 256, y = i / 4 * 280;
            canvas.DrawBitmap(bitmap, new SKRect(x, y, x + 256, y + 256), nearest);
            canvas.DrawText(names[i], x + 5, y + 274, label);
            Console.WriteLine($"REFERENCE {names[i]}: {texture.Width}x{texture.Height}");
        }
        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(output, "vanilla-contact-sheet.png"), data.ToArray());
    }
}
