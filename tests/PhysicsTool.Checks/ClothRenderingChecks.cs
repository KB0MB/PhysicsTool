using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Collections;

internal static class ClothRenderingChecks
{
    private static T Field<T>(PhysicsViewport viewport, string name) => (T)typeof(PhysicsViewport).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewport)!;
    public static void Run(string output)
    {
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        var data = new ParticlePreviewData();
        data.Particles.Add(new() { Index = 7, Position = new(-.4f, 0, 0), Fixed = true });
        data.Particles.Add(new() { Index = 12, Position = new(.4f, 1, 0) });
        data.Links.Add(new() { ParticleA = 7, ParticleB = 12 });
        data.Links.Add(new() { ParticleA = 12, ParticleB = 7 });
        for (int i = 0; i < 2; i++) data.Colliders.Add(new() { Index = i, Start = Vector3.Zero, End = Vector3.UnitY, Radius = .15f });
        var viewport = new PhysicsViewport { Data = data, ShowBones = false, GpuReady = true };
        viewport.Measure(new Size(800, 600)); viewport.Arrange(new Rect(0, 0, 800, 600)); viewport.SetModel(new());
        using var target = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(800, 600));
        void Draw() => target.Render(viewport);
        Draw();
        Require(Field<IList>(viewport, "_drawColliders").Count == 2, "Editor merged selectable colliders");
        var mesh = Field<IDictionary>(viewport, "_colliderMeshes").Values.Cast<object>().Single();
        viewport.Orbit(.4f, -.2f); viewport.Pan(20, -15); viewport.ZoomAt(.2, new(400, 300)); Draw();
        Require(ReferenceEquals(mesh, Field<IDictionary>(viewport, "_colliderMeshes").Values.Cast<object>().Single()), "Camera motion rebuilt collider tessellation");
        foreach (var particle in data.Particles)
            Require(viewport.Project(particle.Position) == Field<Dictionary<int, Point>>(viewport, "_projectedParticles")[particle.Index], "Cached projection differs from picking projection");
        viewport.ReadOnlyActorView = true; Draw();
        Require(Field<IList>(viewport, "_drawColliders").Count == 1 && data.Colliders.Count == 2, "Actor collider deduplication changed data");
        data.Colliders[1].Radius = .2f; data.Colliders[1].Start += Vector3.UnitX; Draw();
        Require(Field<IList>(viewport, "_drawColliders").Count == 2 && Field<IDictionary>(viewport, "_colliderMeshes").Count == 2, "Edited collider geometry stayed cached");
        for (int i = 0; i < 80; i++) { data.Colliders[1].End += Vector3.UnitY * .01f; Draw(); }
        Require(Field<IDictionary>(viewport, "_colliderMeshes").Count == 2, "Collider cache retained animation history");
        viewport.SimulatedColliders = new Dictionary<int, ColliderPreviewShape> { [1] = new() { Index = 1, Start = new(2, 0, 0), Radius = .1f, Kind = ColliderPreviewKind.Sphere } };
        viewport.Simulated = new Dictionary<int, Vector3> { [12] = new(.8f, 1.2f, .2f) }; Draw();
        Require(Field<Dictionary<int, Point>>(viewport, "_projectedParticles")[12] == viewport.Project(viewport.Simulated[12]), "Simulated particle pose was not drawn");
        viewport.Simulated = null; viewport.SimulatedColliders = null;
        data.Particles.RemoveAt(1); data.Links.Clear(); Draw();
        Require(Field<Dictionary<int, Point>>(viewport, "_projectedParticles").Count == 1, "Deleted particle remains in drawing cache");
        viewport.ShowPhysics = false; Draw(); Require(Field<IDictionary>(viewport, "_colliderMeshes").Count == 0, "Hidden physics retained tessellation cache");
        viewport.ShowPhysics = true; viewport.ReadOnlyActorView = false; viewport.Selected.Add(7); viewport.SelectedColliders.Add(1);
        var window = new Window { Width = 800, Height = 600, Content = viewport };
        window.Show(); Dispatcher.UIThread.RunJobs();
        using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "overlay-selected.png"));
        window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; Dispatcher.UIThread.RunJobs();
        using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "overlay-selected-light.png"));
        window.Close();
        Console.WriteLine("PASS: overlay projection, editor collider identity, actor deduplication, edited/animated geometry, bounded cache, particle deletion, simulated poses, theme and selection rendering.");
    }
    public static void Profile(string path, string? output = null)
    {
        PortableModelImporter.ConfigureBfres();
        using var scene = System.Threading.Tasks.Task.Run(() => BotwActorScene.Load(path)).GetAwaiter().GetResult();
        var viewport = new PhysicsViewport { Data = scene.Overlay, ShowBones = false, GpuReady = true, ReadOnlyActorView = true };
        viewport.Measure(new Size(1280, 720)); viewport.Arrange(new Rect(0, 0, 1280, 720));
        viewport.SetModel(scene.CurrentModel);
        using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1280, 720)))
        foreach (var visible in new[] { false, true })
        {
            viewport.ShowPhysics = visible; var times = new List<double>();
            for (int i = 0; i < 25; i++)
            {
                viewport.Orbit(.01f, 0); var timer = Stopwatch.StartNew(); bitmap.Render(viewport);
                if (i >= 5) times.Add(timer.Elapsed.TotalMilliseconds);
            }
            Console.WriteLine($"RASTER visible={visible}: {times.Average():F2} ms, max {times.Max():F2} ms");
        }
        Console.WriteLine($"{scene.Resources.ActorName}: {scene.Overlay.Particles.Count} particles, {scene.Overlay.Links.Count} links, {scene.Overlay.Triangles.Count} triangles, {scene.Overlay.Colliders.Count} colliders");
        using var raster = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1280, 720));
        var oldView = new ReferenceOverlay(viewport);
        oldView.Measure(new Size(1280, 720)); oldView.Arrange(new Rect(0, 0, 1280, 720));
        foreach (var actor in new[] { false, true })
        foreach (var moving in new[] { false, true })
        foreach (var reference in new[] { true, false })
        foreach (var mode in new[] { "cloth", "colliders" })
        {
            viewport.ReadOnlyActorView = actor;
            scene.Update(null, 0, false, 0, true);
            viewport.ResetView();
            viewport.ShowPhysics = mode != "hidden"; viewport.ShowColliders = mode == "colliders";
            double milliseconds = 0; long bytes = 0;
            for (int i = 0; i < 20; i++)
            {
                if (moving) scene.Update(scene.Animations.FirstOrDefault(), i, false, 0);
                viewport.Orbit(.005f, 0);
                long before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                raster.Render(reference ? oldView : viewport);
                timer.Stop();
                if (i >= 5) { milliseconds += timer.Elapsed.TotalMilliseconds; bytes += GC.GetAllocatedBytesForCurrentThread() - before; }
            }
            Console.WriteLine($"{(actor ? "Actor" : "Editor")} {(moving ? "Animated" : "Static")} {(reference ? "Reference" : "Optimized")} {mode}: {milliseconds/15:F2} ms, {bytes/15/1024:F0} KiB/frame (software rasterization; excludes model and solver)");
        }
        if (output != null)
        {
            Directory.CreateDirectory(output);
            scene.Update(null, 0, false, 0, true); viewport.ResetView();
            foreach (var reference in new[] { true, false })
            {
                var window = new Window { Width = 1280, Height = 720, Background = new SolidColorBrush(Color.Parse("#242A2A")), Content = reference ? oldView : viewport };
                window.Show(); Dispatcher.UIThread.RunJobs();
                using (var bitmap = window.CaptureRenderedFrame()!) bitmap.Save(Path.Combine(output, reference ? "ganondorf-reference.png" : "ganondorf-optimized.png"));
                window.Close();
            }
        }
    }
    private sealed class ReferenceOverlay(PhysicsViewport viewport) : Control
    {
        public override void Render(DrawingContext context) => ClothRenderingReference.Draw(viewport, context);
    }
}
