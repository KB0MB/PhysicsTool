using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class ActorGpuChecks : Application
{
    public static string PathToActor = "", Output = "";
    public static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new MainWindow { ShowInTaskbar = false }; lifetime.MainWindow = window;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timeout.Tick += (_, _) => Finish("Actor GPU check timed out."); timeout.Start();
        window.ModelSurface!.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try
            {
                await window.LoadActorPathAsync(PathToActor);
                window.Viewport.ShowPhysics = false;
                var first = await Capture("actor-rest.png");
                var animations = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ActorAnimation");
                if (animations.ItemCount < 2) throw new Exception("Animated actor fixture required.");
                animations.SelectedIndex = 1;
                var timeline = window.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "ActorTimeline");
                timeline.Value = timeline.Maximum / 2;
                var posed = await Capture("actor-posed.png");
                if (SHA256.HashData(first).SequenceEqual(SHA256.HashData(posed))) throw new Exception("Actor pose did not update GPU geometry.");
                var play = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), "Play / pause animation"));
                var before = timeline.Value; play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(250);
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (timeline.Value == before) throw new Exception("Actor playback clock did not advance.");
                window.ToggleSimulation(); await Task.Delay(250); window.StopSimulation();
                await Capture("actor-simulated.png");
                window.Width = 980; window.Height = 640; await Task.Delay(50); await Capture("actor-compact.png");
                window.ShowOverview();
                if (window.IsActorMode) throw new Exception("Actor was not released on Back.");
                Result = 0; Console.WriteLine("PASS: actor GPU rest/animated poses, live playback, simulation, compact viewport and Back cleanup."); Finish(null);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        async Task<byte[]> Capture(string name)
        {
            var source = new TaskCompletionSource<byte[]>();
            void Receive(byte[] pixels, int width, int height)
            {
                window.ModelSurface.FrameCaptured -= Receive;
                try
                {
                    var colors = Enumerable.Range(0, pixels.Length / 4).Select(i => BitConverter.ToUInt32(pixels, i * 4)).Distinct().Take(32).Count();
                    if (colors < 8) throw new Exception("Actor model is blank.");
                    using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (height - 1 - y) * bitmap.RowBytes, width * 4);
                    using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(System.IO.Path.Combine(Output, name), png.ToArray()); source.SetResult(pixels);
                }
                catch (Exception ex) { source.SetException(ex); }
            }
            window.ModelSurface.FrameCaptured += Receive; window.ModelSurface.CaptureNextFrame = true; window.ModelSurface.RequestNextFrameRendering();
            return await source.Task;
        }
        void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
}
