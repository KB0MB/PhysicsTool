using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using HKCLTool;
using SkiaSharp;

internal sealed class IconCaptureExportChecks : Application
{
    internal static string Root = "", Actor = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1100, Height = 850, ShowInTaskbar = false };
        var view = new IconToolView(window); window.Content = view; lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                var source = IconCaptureAsset.Catalog(Root).First(a => a.Name == Actor);
                await view.LoadPathAsync(source.Path, source.ResourceRoot);
                if (view.Asset?.Name != Actor) throw new Exception("Mod actor did not load");
                var presets = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconCaptureMode");
                presets.SelectedItem = presets.Items.OfType<IconCapturePreset>().Single(p => p.Name == "Weapon_Shield_004");
                view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconAutoMaterials").IsChecked = false;
                var png = await view.CapturePngAsync(256);
                var encoded = await view.CaptureIconBytesAsync(Actor, IconImageFormat.Sbitemico);
                var textures = new Dictionary<string, VisualModelTexture>();
                new ToolboxBfresDocument(encoded).ReadTextures(textures, new());
                var native = textures.Values.Single();
                using var image = SKImage.FromEncodedData(png);
                using var bitmap = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
                byte[] pixels = new byte[256 * 256 * 4]; System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
                if (!native.Pixels.SequenceEqual(pixels)) throw new Exception("Mod screenshot/native RGBA mismatch");
                if (bitmap.Pixels.Count(p => p.Alpha > 128) < 1000 || !bitmap.Pixels.Any(p => p.Alpha == 0)) throw new Exception("Blank or opaque mod capture");
                File.WriteAllBytes(Path.Combine(Output, Actor + ".png"), png);
                File.WriteAllBytes(Path.Combine(Output, Actor + ".sbitemico"), encoded);
                Console.WriteLine("PASS actual mod GPU capture/export: " + Actor + "; exact screenshot pixels and alpha");
                Result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }
}
