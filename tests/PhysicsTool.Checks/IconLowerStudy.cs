using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using SkiaSharp;

internal sealed class IconLowerStudy : Application
{
    internal static string Root = "", Output = "";
    internal static string? Before;
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1150, Height = 850, ShowInTaskbar = false };
        var view = new IconToolView(window); window.Content = view; lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                var actors = IconCaptureAsset.Catalog(Root);
                foreach (string name in new[] { "Armor_001_Lower", "Armor_009_Lower" })
                {
                    var actor = actors.First(a => a.Name == name);
                    await view.LoadPathAsync(actor.Path, actor.ResourceRoot);
                    view.SetBeltVisible(true);
                    if (view.Asset?.Reference is { } reference)
                        File.WriteAllBytes(Path.Combine(Output, name + "-reference.png"), IconToolView.Encode(reference.Pixels, reference.Width, reference.Height, false));
                    var surface = view.Surface!;
                    var originalLight = surface.KeyLightDirection;
                    Console.WriteLine(name + " KEY " + originalLight);
                    var directions = new (string Name, Vector3? Light)[] { ("preset", originalLight), ("native-env", new(-.5f,.5f,.5f)), ("front-high", new(.2f,.9f,1)), ("front-left", new(-.4f,.8f,1)) };
                    foreach (var candidate in directions)
                    {
                        surface.KeyLightDirection = candidate.Light;
                        surface.CastShadows = true;
                        await Save(candidate.Name);
                        surface.CastShadows = false;
                        await Save(candidate.Name + "-no-shadow");
                    }
                    surface.KeyLightDirection = originalLight; surface.CastShadows = true;
                    Sheet(name, new[] { "reference", "preset", "preset-no-shadow", "native-env", "front-high", "front-left" });
                    foreach (float ambient in new[] { .35f, .5f, .65f })
                    {
                        surface.AmbientIntensity = ambient;
                        surface.DirectIntensity = 1.15f;
                        await Save("fill-" + ambient.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture));
                    }
                    surface.AmbientIntensity = 1; surface.DirectIntensity = 1;
                    Sheet(name, new[] { "reference", "preset", "fill-0.35", "fill-0.50", "fill-0.65" });
                    if (Before != null)
                    {
                        File.Copy(Path.Combine(Before,name+"-preset.png"),Path.Combine(Output,name+"-before.png"),true);
                        Sheet(name,new[] { "reference", "before", "preset" });
                    }
                    async Task Save(string variant)
                    {
                        File.WriteAllBytes(Path.Combine(Output, name + "-" + variant + ".png"), await view.CapturePngAsync(1024));
                    }
                }
                Result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { window.Close(); lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }

    private static void Sheet(string name, string[] variants)
    {
        using var sheet = new SKBitmap(variants.Length * 240, 600);
        using var canvas = new SKCanvas(sheet); canvas.Clear(new SKColor(36,40,41));
        using var text = new SKPaint { Color = SKColors.White, TextSize = 13, IsAntialias = true };
        for (int i = 0; i < variants.Length; i++)
        {
            using var bitmap = SKBitmap.Decode(Path.Combine(Output, name + "-" + variants[i] + ".png"));
            int left=bitmap.Width, top=bitmap.Height, right=0, bottom=0;
            for(int y=0;y<bitmap.Height;y++) for(int x=0;x<bitmap.Width;x++)
                if(bitmap.GetPixel(x,y).Alpha > 127) { left=Math.Min(left,x); right=Math.Max(right,x); top=Math.Min(top,y); bottom=Math.Max(bottom,y); }
            var source = new SKRect(left,top,right+1,bottom+1);
            float width = source.Width / source.Height * 360;
            canvas.DrawText(variants[i],i*240+8,20,text);
            canvas.DrawBitmap(bitmap,source,new SKRect(i*240+(240-width)/2,30,i*240+(240+width)/2,390));
            var waist = new SKRect(left,top,right+1,top+source.Height*.18f);
            canvas.DrawBitmap(bitmap,waist,new SKRect(i*240+10,415,i*240+230,415+waist.Height/waist.Width*220));
        }
        using var png = sheet.Encode(SKEncodedImageFormat.Png,100);
        File.WriteAllBytes(Path.Combine(Output,name+"-comparison-"+variants[^1]+".png"),png.ToArray());
    }
}
