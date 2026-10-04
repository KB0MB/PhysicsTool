using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PhysicsTool.Desktop;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class GpuChecks : Application
{
    public static string ModelPath = "", Output = "";
    public static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new MainWindow { ShowInTaskbar = false };
        lifetime.MainWindow = window;
        int stage = 0; byte[]? original = null; bool finished = false;
        HKCLTool.VisualModelData? loaded = null;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timeout.Tick += (_, _) => Finish("GPU check timed out; a graphics context may be unavailable.");
        timeout.Start();
        window.ModelSurface!.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try { await window.LoadModelPathAsync(ModelPath); loaded = window.Viewport.Model; Request(); }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        window.ModelSurface.FrameCaptured += (pixels, width, height) =>
        {
            try
            {
                using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (height - 1 - y) * bitmap.RowBytes, width * 4);
                using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(Output, $"gpu-{stage}.png"), png.ToArray());
                var colors = Enumerable.Range(0, pixels.Length / 4).Select(i => BitConverter.ToUInt32(pixels, i * 4)).Distinct().Take(32).Count();
                if (stage is not 2 and not 4 and not 6 and not 7 && colors < 8) throw new Exception("GPU model is blank.");
                if (stage == 0) { original = SHA256.HashData(pixels); window.Viewport.Orbit(.8f, .2f); }
                if (stage == 1)
                {
                    if (original!.SequenceEqual(SHA256.HashData(pixels))) throw new Exception("Camera did not change the GPU image.");
                    for (int i = 0; i < window.Viewport.Model!.Meshes.Count; i++) window.Viewport.SetMeshVisible(i, false);
                }
                if (stage == 2)
                {
                    if (colors > 1) throw new Exception("Hidden meshes still render.");
                    for (int i = 0; i < window.Viewport.Model!.Meshes.Count; i++) window.Viewport.SetMeshVisible(i, true);
                    window.Width = 980; window.Height = 640;
                }
                if (stage == 3) window.Viewport.SetModel(null);
                if (stage == 4)
                {
                    if (colors != 1) throw new Exception("Cleared model still renders.");
                    window.Viewport.SetModel(loaded);
                }
                if (stage == 5) window.Viewport.SetView(CameraView.Top);
                if (stage == 6) window.Viewport.SetView(CameraView.Bottom);
                if (stage == 7)
                {
                    window.Viewport.ResetView(); window.ShowOverview(); window.ShowEditor();
                }
                if (stage == 8) { Result = 0; Console.WriteLine("PASS: real GPU shader, texture/model pixels, orbit, mesh hiding, compact resize, clear/reload, axis views, and overview/editor context recreation."); Finish(null); return; }
                stage++; Dispatcher.UIThread.Post(Request, DispatcherPriority.Background);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        void Request() { window.ModelSurface.CaptureNextFrame = true; window.ModelSurface.RequestNextFrameRendering(); }
        void Finish(string? error)
        {
            if (finished) return; finished = true; timeout.Stop();
            if (error != null) Console.Error.WriteLine(error);
            lifetime.Shutdown(Result);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
