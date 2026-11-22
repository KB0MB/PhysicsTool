using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HKCLTool;
using PhysicsTool.Core;

internal sealed class OotGpuChecks : Application
{
    internal static string Root = "", Output = "";
    internal static string Actor = "Player_Adult", ClipName = "Bottle_Catch";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var viewport = new PhysicsViewport { ShowPhysics = false };
        var surface = new ModelGlControl(viewport);
        var window = new Window { Width = 1000, Height = 850, Content = surface, ShowInTaskbar = false };
        lifetime.MainWindow = window;
        int stage = 0; byte[]? previous = null;
        ImportedModelRig? rig = null;
        ModelAnimationClip? clip = null;
        OotHelperEvaluator? helpers = null;
        OotActorPreview? scene = null;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timeout.Tick += (_, _) => Finish("OoT GPU test timed out"); timeout.Start();
        surface.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try
            {
                var resources = await Task.Run(() => OotActorResources.Load(new(Actor, Path.Combine(Root, "Pack/Actor", Actor + ".pack.zs"), null, Root, true, true, true) { IsOot = true }));
                rig = await Task.Run(() => PortableModelImporter.LoadRig(resources.ModelPath!));
                var clips = await Task.Run(() => resources.AnimationPaths.SelectMany(p => ModelAnimationClip.Load(p, allowEmptyBfres: true)).ToArray());
                clip = clips.Single(c => c.Name == ClipName);
                helpers = new(resources.Assets.Single(a => !a.IsCloth).Bytes, rig.Rest.Bones);
                scene = new(resources.Assets);
                viewport.SetModel(rig.Rest); viewport.FrameModel(); Request();
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        surface.FrameCaptured += async (pixels, width, height) =>
        {
            try
            {
                var bg = viewport.ViewportBackground;
                int colored = Enumerable.Range(0, pixels.Length / 4).Count(i => Math.Abs(pixels[i*4]-bg.R) + Math.Abs(pixels[i*4+1]-bg.G) + Math.Abs(pixels[i*4+2]-bg.B) > 60);
                if (colored < 1000) throw new Exception("Blank OoT model frame");
                var hash = SHA256.HashData(pixels);
                if (previous?.SequenceEqual(hash) == true) throw new Exception("Orbit did not change model render");
                previous = hash;
                using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                for (int y = 0; y < height; y++) Marshal.Copy(pixels, y*width*4, bitmap.GetPixels() + (height-1-y)*bitmap.RowBytes, width*4);
                using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(Output, $"oot-model-{stage}.png"), png.ToArray());
                Console.WriteLine($"Frame {stage}: {colored} model pixels; {surface.LastVisibleMeshes} meshes");
                if (stage++ == 2)
                {
                    foreach (var fps in new[] { 30, 60 })
                    {
                        ViewportFrameRate.FramesPerSecond = fps;
                        long start = surface.RenderedFrames;
                        var clock = System.Diagnostics.Stopwatch.StartNew();
                        using (DispatcherTimer.Run(() => { viewport.Orbit(.005f, 0); return true; }, TimeSpan.FromMilliseconds(2)))
                            await Task.Delay(1200);
                        double elapsed = clock.Elapsed.TotalSeconds;
                        long frames = surface.RenderedFrames - start;
                        if (frames < 3 || frames > fps * elapsed + 4) throw new Exception($"Viewport FPS pacing failed: {frames} in {elapsed:F2}s at {fps} FPS");
                        Console.WriteLine($"PASS: {fps} FPS viewport target, {frames} GPU draws in {elapsed:F2}s under continuous orbit input");
                    }
                    ViewportFrameRate.FramesPerSecond = 60;
                    Result = 0; Finish(null); return;
                }
                var skeleton = helpers!.Evaluate(rig!.AnimationSkeleton(clip!, stage * 8, stage == 2 ? scene!.DrivenNames : null));
                var output = scene!.Update(rig.Rest, skeleton, stage == 2, 1f / 60, true, false, true);
                viewport.SetModelPose(rig.Sample(output));
                viewport.Orbit(.5f, .05f);
                if (stage == 2) { window.Width = 720; window.Height = 650; }
                Dispatcher.UIThread.Post(Request, DispatcherPriority.Background);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        void Request() { surface.CaptureNextFrame = true; surface.RequestNextFrameRendering(); }
        void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
}
