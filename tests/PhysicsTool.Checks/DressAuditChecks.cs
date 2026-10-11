using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using SkiaSharp;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

internal sealed class DressAuditChecks : Application
{
    public static string Output = "", ActorPath = "";
    public static int Result = 1;
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var view = new PhysicsViewport { ShowPhysics = false };
        var surface = new ModelGlControl(view);
        var window = new Window { Width = 900, Height = 1000, ShowInTaskbar = false, Content = new Grid { Children = { surface, view } } };
        lifetime.MainWindow = window;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        timeout.Tick += (_, _) => Finish("Dress diagnostic timed out"); timeout.Start();
        surface.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try
            {
                VisualModelImporter.EyeGlint = () => ModelTextures.Resource("EyeGlint.png");
                using var scene = BotwActorScene.Load(ActorPath);
                var clip = scene.Animations.FirstOrDefault(a => a.Name.Equals("Act_Hello", StringComparison.OrdinalIgnoreCase))
                    ?? scene.Animations.First(a => a.Name.Contains("Hello", StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"CLIP {clip.Name} {clip.FrameCount} frames");
                var supports = (List<HkclService>)typeof(BotwActorScene).GetField("_supports", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
                var saved = supports.ToArray();
                var options = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true,
                    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
                var layers = ((System.Collections.IEnumerable)typeof(BotwActorScene).GetField("_layers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!)
                    .Cast<object>().Select(l => (ParticlePreviewData)l.GetType().GetProperty("Source")!.GetValue(l)!).ToArray();
                File.WriteAllText(Path.Combine(Output, "inputs.json"), JsonSerializer.Serialize(new {
                    helpers = saved.SelectMany(s => s.GetSidecarDriverGroups()).ToArray(), layers,
                    meshes = scene.RestModel.Meshes.Select(m => new { m.Name, vertices = m.Vertices.Length / 8 }) }, options));
                var results = new List<object>();
                var checks = new List<string>();
                view.Data = scene.Overlay;
                view.SetModel(scene.CurrentModel); view.SetView(CameraView.Front, false); view.Orbit(.12f, .2f);
                foreach (var frame in new[] { 0d, clip.FrameCount * .25, clip.FrameCount * .5, clip.FrameCount * .75 })
                {
                    VisualModelBone[]? helperRoots = null;
                    foreach (var mode in new[] { "animation", "helpers", "simulation" })
                    {
                        supports.Clear(); if (mode != "animation") supports.AddRange(saved);
                        scene.Update(clip, 0, false, 0, true);
                        if (mode == "simulation")
                            for (var f = 0d; f < frame; f += .5) scene.Update(clip, f, true, 1f / 60);
                        var model = scene.Update(clip, frame, mode == "simulation", mode == "simulation" ? 1f / 60 : 0);
                        var roots = model.Bones.Where(b => b.Name is "Skirt_1_L" or "Skirt_1_R").OrderBy(b => b.Name).ToArray();
                        if (mode == "helpers") helperRoots = roots;
                        if (mode == "simulation" && roots.Length == 2 && helperRoots?.Length == 2)
                        {
                            if (roots.Zip(helperRoots).Any(p => System.Numerics.Vector3.Distance(p.First.Position, p.Second.Position) > 1e-4f
                                || MathF.Abs(System.Numerics.Quaternion.Dot(p.First.Rotation, p.Second.Rotation)) < .99999f))
                                throw new Exception("Simulation suppressed helper-driven skirt ancestors.");
                            checks.Add($"Frame {frame}: both helper-driven skirt roots retained with simulation enabled.");
                        }
                        view.SetModelPose(model);
                        results.Add(new { frame, mode, bones = model.Bones, particles = scene.Overlay.Particles.Select(p => new { p.Index, p.Position, p.Fixed }).ToArray(),
                            colliders = scene.Overlay.Colliders.Select(c => new { c.Index, c.Name, c.BoneIndex, c.Start, c.End, c.Radius, c.EndRadius }).ToArray() });
                        await Capture($"{frame:000.0}-{mode}.png");
                    }
                }
                File.WriteAllText(Path.Combine(Output, "poses.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                File.WriteAllLines(Path.Combine(Output, "checks.txt"), checks);
                Console.WriteLine("Dress comparison captures complete."); Result = 0; Finish(null);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        Task Capture(string name)
        {
            var completion = new TaskCompletionSource();
            void Receive(byte[] pixels, int width, int height)
            {
                surface.FrameCaptured -= Receive;
                try
                {
                    if (Enumerable.Range(0, pixels.Length / 4).Select(i => BitConverter.ToUInt32(pixels, i * 4)).Distinct().Take(16).Count() < 8)
                        throw new Exception("Blank diagnostic viewport");
                    using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (height - 1 - y) * bitmap.RowBytes, width * 4);
                    using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(Output, name), png.ToArray()); completion.SetResult();
                }
                catch (Exception ex) { completion.SetException(ex); }
            }
            surface.FrameCaptured += Receive; surface.CaptureNextFrame = true; surface.RequestNextFrameRendering();
            return completion.Task;
        }
        void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
}
