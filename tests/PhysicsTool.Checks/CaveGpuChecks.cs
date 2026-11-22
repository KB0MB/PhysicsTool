using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using SkiaSharp;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class CaveGpuChecks : Application
{
    internal static string Root = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var viewport = new PhysicsViewport { FlyNavigation = true, ShowPhysics = false };
        var surface = new ModelGlControl(viewport);
        var window = new Window { Width = 1000, Height = 750, Content = surface, ShowInTaskbar = false };
        lifetime.MainWindow = window;
        var warnings = new List<string>();
        VisualModelData? caves = null;
        int stage = 0; byte[]? first = null;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        timeout.Tick += (_, _) => Finish("Cave GPU test timed out"); timeout.Start();
        surface.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try
            {
                caves = await Task.Run(() => TotkCaveScene.Load(Root, "MainField/A-2_Static.bcett.byml.zs", warnings, null, default));
                Console.WriteLine(string.Join("\n", warnings));
                SelectCave("Cave_Hebra_0000"); Request();
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        surface.FrameCaptured += (pixels, width, height) =>
        {
            try
            {
                var background = viewport.ViewportBackground;
                int colored = Enumerable.Range(0, pixels.Length / 4).Count(i =>
                    Math.Abs(pixels[4*i]-background.R) + Math.Abs(pixels[4*i+1]-background.G) + Math.Abs(pixels[4*i+2]-background.B) > 60);
                if (colored < 1000 || surface.DepthBits < 24) throw new Exception("Cave viewport is blank or lacks depth precision");
                using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (height-1-y) * bitmap.RowBytes, width * 4);
                using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(Output, $"cave-{stage}.png"), png.ToArray());
                Console.WriteLine($"Cave frame {stage}: {surface.LastVisibleMeshes} visible batches / {colored} surface pixels / {surface.LastRenderMilliseconds:0.00} ms submission");
                if (stage == 0) { first = SHA256.HashData(pixels); viewport.Orbit(.5f, .1f); }
                else if (stage == 1)
                {
                    if (first!.SequenceEqual(SHA256.HashData(pixels))) throw new Exception("Cave orbit did not update the image");
                    SelectCave("Cave_Hebra_0039"); window.Width = 750; window.Height = 600;
                }
                else { Result = 0; Console.WriteLine("PASS: native A-2 caves render, orbit and resize."); Finish(null); return; }
                stage++; Dispatcher.UIThread.Post(Request, DispatcherPriority.Background);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        void SelectCave(string name)
        {
            var model = new VisualModelData { BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
            foreach (var mesh in caves!.Meshes.Where(m => m.Name.StartsWith("cave:" + name + "/")))
            {
                model.Meshes.Add(mesh);
                for (int i = 0; i < mesh.Vertices.Length; i += 8)
                {
                    var p = new Vector3(mesh.Vertices[i], mesh.Vertices[i+1], mesh.Vertices[i+2]);
                    model.BoundsMinimum = Vector3.Min(model.BoundsMinimum, p); model.BoundsMaximum = Vector3.Max(model.BoundsMaximum, p);
                }
            }
            viewport.SetModel(model); viewport.FrameModel();
        }
        void Request() { surface.CaptureNextFrame = true; surface.RequestNextFrameRendering(); }
        void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
}
