using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PhysicsTool.Desktop;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class ShrineGpuChecks : Application
{
    internal static string Source = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1440, Height = 900, Title = "Shrine rendering check", ShowInTaskbar = false };
        var view = new ShrineSceneView(window); window.Content = view; lifetime.MainWindow = window;
        int stage = -1; byte[]? first = null; bool finished = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) }; timer.Tick += (_, _) => Finish("Shrine rendering timed out"); timer.Start();
        view.ModelSurface!.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try
            {
                while (view.Busy) await Task.Delay(20);
                await view.LoadPathAsync(Source);
                Request();
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        view.ModelSurface.FrameCaptured += (pixels, width, height) =>
        {
            try
            {
                // Let the scene's initial layout/camera placement finish before
                // choosing a close view of the loaded geometry.
                if (stage < 0)
                {
                    stage = 0;
                    view.Viewport.StartFlyAt(view.Assets!.Scene.Actors[0].Position + new System.Numerics.Vector3(0, 2, 12));
                    Dispatcher.UIThread.Post(Request, DispatcherPriority.Background);
                    return;
                }
                using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (height - 1 - y) * bitmap.RowBytes, width * 4);
                using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                Console.WriteLine($"Frame {stage}: {view.ModelSurface.LastVisibleMeshes} visible meshes, {view.ModelSurface.LastMaterialUpdates} material uploads, {view.ModelSurface.LastRenderMilliseconds:0.00} ms render submission; depth {view.ModelSurface.DepthBits} bits.");
                if (view.ModelSurface.DepthBits < 24) throw new Exception("Shrine framebuffer has insufficient depth precision");
                File.WriteAllBytes(Path.Combine(Output, $"shrine-gpu-{stage}.png"), png.ToArray());
                var background = view.Viewport.ViewportBackground;
                if (Enumerable.Range(0, pixels.Length / 4).Count(i => Math.Abs(pixels[i * 4] - background.R) + Math.Abs(pixels[i * 4 + 1] - background.G) + Math.Abs(pixels[i * 4 + 2] - background.B) > 60) < 100)
                    throw new Exception($"Shrine GPU surface appears blank: {width}x{height}, bounds {view.Viewport.Model?.BoundsMinimum} / {view.Viewport.Model?.BoundsMaximum}");
                if (stage == 0) { first = SHA256.HashData(pixels); view.Viewport.Orbit(.5f, .1f); }
                else if (stage == 1)
                {
                    if (first!.SequenceEqual(SHA256.HashData(pixels))) throw new Exception("Shrine orbit did not update pixels");
                    window.Width = 1000; window.Height = 700; view.Viewport.DisplayMode = ModelDisplayMode.EdgedFaces;
                }
                else { Result = 0; Console.WriteLine("PASS: shrine models rendered, orbit changes pixels, compact viewport and edged faces render."); Finish(null); return; }
                stage++; Dispatcher.UIThread.Post(Request, DispatcherPriority.Background);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        void Request() { view.ModelSurface.CaptureNextFrame = true; view.ModelSurface.RequestNextFrameRendering(); }
        void Finish(string? error) { if (finished) return; finished = true; timer.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
}
