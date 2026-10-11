using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.Styling;
using PhysicsTool.Desktop;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class CollisionGpuChecks : Application
{
    internal static string Source = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new CollisionWindow { ShowInTaskbar = false }; lifetime.MainWindow = window;
        var stage = 0; byte[]? original = null; var finished = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) }; timer.Tick += (_, _) => Finish("Collision GPU test timed out"); timer.Start();
        window.ModelSurface!.RenderFailed += Finish;
        window.Opened += async (_, _) => { try { await window.LoadPathAsync(Source, false); if (window.Viewport.Model == null) throw new Exception("Collision preview did not load"); Request(); } catch (Exception ex) { Finish(ex.ToString()); } };
        window.ModelSurface.FrameCaptured += (pixels, width, height) =>
        {
            try
            {
                var background = window.Viewport.ViewportBackground;
                var modelPixels = Enumerable.Range(0, pixels.Length / 4).Count(i => Math.Abs(pixels[i * 4] - pixels[i * 4 + 2]) > 20);
                if (modelPixels < 100) throw new Exception("Collision GPU surface appears blank");
                using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (height - 1 - y) * bitmap.RowBytes, width * 4);
                using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(Output, "collision-gpu-" + stage + ".png"), png.ToArray());
                if (stage == 0) { original = SHA256.HashData(pixels); window.Viewport.Orbit(.7f, .15f); }
                else if (stage == 1)
                {
                    if (original!.SequenceEqual(SHA256.HashData(pixels))) throw new Exception("Collision orbit did not update pixels");
                    window.Width = 900; window.Height = 600;
                }
                else { Result = 0; Console.WriteLine("PASS: real collision GPU surface, model pixels, orbit and compact resize."); Finish(null); return; }
                stage++; Dispatcher.UIThread.Post(Request, DispatcherPriority.Background);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        void Request() { window.ModelSurface.CaptureNextFrame = true; window.ModelSurface.RequestNextFrameRendering(); }
        void Finish(string? error) { if (finished) return; finished = true; timer.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
}
