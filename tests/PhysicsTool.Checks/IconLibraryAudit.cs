using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using SkiaSharp;
using System.Text.Json;

internal static class IconLibraryAudit
{
    internal static void Run(string botw, string totk, string output)
    {
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        var library = IconCaptureLibrary.Load();
        var native = new List<IconCapturePreset>(); var failures = new List<string>();
        foreach (var actor in IconCaptureAsset.Catalog(totk).Where(a => a.Name.StartsWith("Armor_") || a.Name.StartsWith("Weapon_")))
        {
            try { native.AddRange(IconCaptureAsset.ReadCapturePresets(new BotwActorResources(actor.Path, [actor.ResourceRoot]))); }
            catch (Exception ex) { failures.Add(actor.Name + ": " + ex.Message); }
        }
        var missing = library.Where(p => !native.Any(n => n.Name == p.Name)).Select(p => p.Name).ToArray();
        var differences = library.Where(p => native.FirstOrDefault(n => n.Name == p.Name) is { } n &&
            (n.Rotation != p.Rotation || n.CameraPosition != p.CameraPosition || n.LightDirection != p.LightDirection || n.FieldOfView != p.FieldOfView)).Select(p => new { Saved = p, Native = native.First(n => n.Name == p.Name) }).ToArray();
        Console.WriteLine($"AUDIT {library.Count} saved presets, {native.Count} native presets, {differences.Length} differences, {missing.Length} missing");
        var rows = new List<object>(); int index = 0, page = 0;
        using var sheet = new SKBitmap(1280, 1280); using var canvas = new SKCanvas(sheet);
        using var text = new SKPaint { Color = SKColors.White, TextSize = 11, IsAntialias = true };
        canvas.Clear(new SKColor(36, 40, 41));
        void SavePage()
        {
            using var image = SKImage.FromBitmap(sheet); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(output, $"vanilla-{page++:D2}.png"), png.ToArray()); canvas.Clear(new SKColor(36, 40, 41));
        }
        foreach (string file in Directory.EnumerateFiles(Path.Combine(IconCaptureAsset.Root(botw), "UI/StockItem"), "*.sbitemico").Order())
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith("Armor_") && !name.StartsWith("Weapon_")) continue;
            try
            {
                var textures = new Dictionary<string, VisualModelTexture>(); new ToolboxBfresDocument(File.ReadAllBytes(file)).ReadTextures(textures, new());
                var texture = textures.Values.First();
                double weight = 0, bright = 0; int minX = texture.Width, minY = texture.Height, maxX = -1, maxY = -1;
                for (int y = 0; y < texture.Height; y++) for (int x = 0; x < texture.Width; x++)
                {
                    int offset = (y * texture.Width + x) * 4; double a = texture.Pixels[offset + 3] / 255.0;
                    if (a < .125) continue;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                    bright += a * (.2126 * texture.Pixels[offset] + .7152 * texture.Pixels[offset + 1] + .0722 * texture.Pixels[offset + 2]); weight += a;
                }
                var preset = library.FirstOrDefault(p => p.Name == name) ?? library.FirstOrDefault(p => name.StartsWith(p.Name + "_", StringComparison.Ordinal) || name.StartsWith(p.Name + ".", StringComparison.Ordinal));
                rows.Add(new { Name = name, texture.Width, texture.Height, Bounds = new[] { minX, minY, maxX, maxY }, MeanLuminance = weight > 0 ? bright / weight : 0,
                    Coverage = IconCaptureAsset.ReferenceCoverage(texture, name.Contains("_Head")), preset?.LightDirection, preset?.CameraPosition, preset?.FieldOfView, preset?.Source });
                using var bitmap = SKBitmap.Decode(IconToolView.Encode(texture.Pixels, texture.Width, texture.Height, false));
                int cell = index++ % 64, px = cell % 8 * 160, py = cell / 8 * 160;
                canvas.DrawBitmap(bitmap, new SKRect(px, py, px + 144, py + 144)); canvas.DrawText(name, px + 2, py + 156, text);
                if (index % 64 == 0) SavePage();
            }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); }
        }
        if (index % 64 != 0) SavePage();
        File.WriteAllBytes(Path.Combine(output, "audit.json"), JsonSerializer.SerializeToUtf8Bytes(new { SavedPresets = library.Count, NativePresets = native.Count, Differences = differences, Missing = missing, Failures = failures,
            LightGroups = native.GroupBy(p => p.LightDirection).Select(g => new { Direction = g.Key, Count = g.Count(), Actors = g.Select(p => p.Name).ToArray() }), Icons = rows }, json));
        Console.WriteLine($"AUDIT {rows.Count} vanilla armor/weapon icons, {page} contact sheets, {failures.Count} failures");
    }
}
