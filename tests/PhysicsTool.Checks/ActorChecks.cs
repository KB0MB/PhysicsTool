using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;
using System.Security.Cryptography;

internal static class ActorChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Run(string output, string[] packs)
    {
        Directory.CreateDirectory(output);
        AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        HKCLTool.VisualModelImporter.EyeGlint = () => ModelTextures.Resource("EyeGlint.png");
        foreach (var pack in packs)
        {
            var hash = SHA256.HashData(File.ReadAllBytes(pack));
            using var scene = BotwActorScene.Load(pack);
            Require(scene.CurrentModel.Meshes.Count > 0 && scene.TextureCount > 0, "Actor model/textures missing");
            Require(scene.HasCloth || scene.SupportCount > 0, "Actor physics missing");
            Require(!scene.Resources.Warnings.Any(w => w.StartsWith("Texture ") || w.StartsWith("Physics ")), string.Join("\n", scene.Resources.Warnings));
            var points = scene.Overlay.Particles.Select(p => p.Position).ToArray();
            var clip = scene.Animations.FirstOrDefault();
            for (int i = 0; i < 20; i++) scene.Update(clip, clip == null ? 0 : i % Math.Max(1, clip.FrameCount), true, 1f / 60);
            Require(scene.CurrentModel.Meshes.SelectMany(m => m.Vertices).All(float.IsFinite), "Actor pose nonfinite");
            Require(scene.Overlay.Particles.All(p => float.IsFinite(p.Position.X) && float.IsFinite(p.Position.Y) && float.IsFinite(p.Position.Z)), "Actor physics nonfinite");
            var window = new MainWindow(graphics: false); window.Show();
            window.Session.New("Keep editor", 0); var state = window.Session.Document.CaptureState();
            var load = window.LoadActorPathAsync(pack);
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (!load.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            Require(load.IsCompleted, "Actor loading timed out"); load.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
            Require(window.IsActorMode && window.IsEditorVisible && !window.Viewport.CanSelect, "Actor did not enter read-only main viewport");
            Require(window.Session.Document.CaptureState() == state, "Actor replaced editor document");
            Require(!window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add Particle")).IsVisible, "Actor exposes editor commands");
            Require(!window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save")).IsEnabled, "Actor allows saving editor state");
            if (scene.Animations.Count > 0)
            {
                window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ActorAnimation").SelectedIndex = 1;
                var timeline = window.GetVisualDescendants().OfType<Slider>().Single(c => c.Name == "ActorTimeline");
                Require(timeline.IsEnabled && timeline.Maximum == scene.Animations[0].FrameCount, "Actor timeline not initialized");
                timeline.Value = timeline.Maximum / 2; Dispatcher.UIThread.RunJobs();
                var expected = scene.Update(scene.Animations[0], timeline.Value, false, 0, true);
                Require(expected.Meshes[0].Vertices.SequenceEqual(window.Viewport.Model!.Meshes[0].Vertices), "Timeline did not sample selected animation");
            }
            foreach (var size in new[] { new Size(1440, 900), new Size(980, 640) })
            {
                window.Width = size.Width; window.Height = size.Height; Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame()!;
                frame.Save(Path.Combine(output, $"{(scene.Resources.IsSwitch ? "switch" : "wiiu")}-actor-{size.Width}.png"));
            }
            window.ToggleSimulation(); window.StopSimulation(); Dispatcher.UIThread.RunJobs();
            window.ShowOverview(); Require(!window.IsActorMode && !window.IsEditorVisible && window.Session.Document.CaptureState() == state, "Leaving actor lost editor data");
            window.Session.Clear(); window.Close(); Dispatcher.UIThread.RunJobs();
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(pack))), "Actor source changed");
            Console.WriteLine($"PASS: {scene.Resources.ActorName} {(scene.Resources.IsSwitch ? "Switch" : "Wii U")}: {scene.CurrentModel.Meshes.Count} meshes, {scene.TextureCount} textures, {scene.ClothCount} cloths, {scene.SupportCount} helper groups, {scene.Animations.Count} animations.");
        }
    }
}
