using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class CollisionChecks
{
    internal static void Native(string output, string[] paths)
    {
        Directory.CreateDirectory(output);
        for (var i = 0; i < paths.Length; i++)
        {
            var path = paths[i]; var original = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            var meshes = BotwCollisionFileImporter.Import(path); Require(meshes.Count > 0 && meshes.All(m => m.Vertices.All(TotkPolytopeBuilder.Finite)), "Native collision is empty/nonfinite");
            var window = new CollisionWindow(false); window.Show(); Await(window.LoadPathAsync(path, false));
            Require(window.Viewport.Model?.Meshes.Count == meshes.Count, "Native collision preview missing geometry");
            using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "native-" + i + ".png"));
            window.Close();
            Require(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)).SequenceEqual(original), "Native source changed");
            Console.WriteLine($"PASS: {Path.GetFileName(path)}: {meshes.Count} meshes, {meshes.Sum(m => m.TriangleCount)} triangles, {BotwCollisionFileImporter.DetectPlatform(path)}.");
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Await(Task task)
    {
        var started = DateTime.UtcNow;
        while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); if ((DateTime.UtcNow - started).TotalSeconds > 120) throw new TimeoutException("Collision test timed out"); }
        task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
    internal static void Run(string output)
    {
        var folder = Path.Combine(output, "collision"); Directory.CreateDirectory(folder);
        var source = new BotwCollisionMesh { Name = "Wood test", Vertices = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ], Indices = [0,2,1,0,1,3,0,3,2,1,2,3] };
        var modelPath = Path.Combine(folder, "source.dae"); BotwCollisionExporter.Export([source], modelPath);
        var original = File.ReadAllBytes(modelPath);
        foreach (var target in Enum.GetValues<CollisionOutput>())
        {
            var settings = new CollisionSettings(target, 2, new(3,4,5), true, false, "CollisionBody");
            var preview = CollisionWorkflow.Prepare([source], settings);
            Require(preview.Meshes.SelectMany(m => m.Vertices).ToHashSet().SetEquals(source.Vertices.Select(v => new Vector3(v.X, v.Z, -v.Y) * 2 + settings.Offset)), "Preview transform differs from source settings");
            var path = Path.Combine(folder, target + CollisionWorkflow.Extension(settings));
            CollisionWorkflow.Write(preview, settings, path, modelPath);
            var reloaded = target == CollisionOutput.TotkPolytope ? TotkPolytopeImporter.Import(path) : BotwCollisionFileImporter.Import(path);
            var expected = preview.Meshes.SelectMany(m => m.Vertices).ToArray(); var actual = reloaded.SelectMany(m => m.Vertices).ToArray();
            Require(Vector3.Distance(expected.Aggregate(Vector3.Min), actual.Aggregate(Vector3.Min)) < .01f && Vector3.Distance(expected.Aggregate(Vector3.Max), actual.Aggregate(Vector3.Max)) < .01f, "Generated collision bounds differ from preview: " + target);
            if (target is CollisionOutput.WiiUActor or CollisionOutput.SwitchActor) Require(HkclService.DetectHkclPlatform(path) == (target == CollisionOutput.SwitchActor ? HkclPlatform.Switch : HkclPlatform.WiiU), "HKRB output platform incorrect");
            if (target == CollisionOutput.WiiUStatic)
            {
                var compressed = settings with { Compress = true }; var compressedPath = Path.Combine(folder, "compressed.shksc");
                CollisionWorkflow.Write(preview, compressed, compressedPath, modelPath); Require(BotwCollisionFileImporter.Import(compressedPath).Count > 0, "Compressed HKSC cannot reopen");
            }
        }
        Require(File.ReadAllBytes(modelPath).SequenceEqual(original), "Collision source modified");
        Require(source.Vertices[0] == Vector3.Zero && source.Material == BotwCollisionMaterials.Default, "Preview mutated source mesh");
        var polySettings = new CollisionSettings(CollisionOutput.TotkPolytope, 1, Vector3.Zero, false, false, "");
        var hull = CollisionWorkflow.Prepare([source], polySettings);
        foreach (var extension in new[] { ".dae", ".fbx" })
        {
            var path = Path.Combine(folder, "export" + extension); CollisionWorkflow.Write(hull, polySettings, path, modelPath, true);
            Require(BotwCollisionMeshImporter.Import(path).Count > 0, "Mesh export cannot reopen");
        }
        try { CollisionWorkflow.Write(hull, polySettings, modelPath, modelPath, true); throw new Exception("Source overwrite accepted"); } catch (InvalidOperationException) { }
        try { CollisionWorkflow.Prepare([source], polySettings with { Scale = 0 }); throw new Exception("Zero scale accepted"); } catch (ArgumentException) { }
        Require(File.ReadAllBytes(modelPath).SequenceEqual(original), "Rejected operation changed source");
        var window = new CollisionWindow(false); window.Show(); Dispatcher.UIThread.RunJobs();
        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        Require(!Find<Button>("CollisionGenerate").IsEnabled, "Generate enabled before import");
        Await(window.LoadPathAsync(modelPath, true));
        Require(window.Viewport.Model != null && Find<Button>("CollisionGenerate").IsEnabled, "Collision import/preview failed: " + Find<TextBlock>("CollisionStatus").Text);
        Find<TabControl>("CollisionOptions").SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        Find<NumericUpDown>("CollisionScale").Value = 2;
        Require(window.Viewport.Model != null && !Find<Button>("CollisionGenerate").IsEnabled, "Stale preview could be exported or lost its reference view");
        Await(window.PrepareAsync()); Require(window.Viewport.Model != null, "Preview refresh failed");
        Find<ComboBox>("CollisionTarget").SelectedIndex = 3; Await(window.PrepareAsync());
        Require(window.Viewport.Model != null && Find<Button>("CollisionGenerate").IsEnabled, "Hull preview failed");
        var file = Path.Combine(folder, "ui.bgyml"); Await(window.WriteAsync(file)); Require(File.Exists(file), "UI export failed");
        var loaded = window.Viewport.Model;
        Await(window.LoadPathAsync(Path.Combine(folder, "missing.hkrb"), false)); Require(ReferenceEquals(loaded, window.Viewport.Model), "Failed import destroyed existing preview");
        Find<TabControl>("CollisionOptions").SelectedIndex = 0; Await(window.PrepareAsync());
        foreach (var size in new[] { (1180,780), (900,600) })
        {
            window.Width = size.Item1; window.Height = size.Item2; Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(folder, "workspace-" + size.Item1 + ".png"));
        }
        window.Close();
        var main = new MainWindow(graphics: false); main.Show();
        foreach (var extension in new[] { ".fbx", ".dae", ".obj", ".glb", ".anim" })
        {
            try { Await(main.LoadModelPathAsync("test" + extension)); throw new Exception("Non-BFRES model import accepted"); } catch (NotSupportedException) { }
            try { Await(main.LoadAnimationPathAsync("test" + extension)); throw new Exception("Non-BFRES animation import accepted"); } catch (NotSupportedException) { }
        }
        main.Close();
        Console.WriteLine("PASS: collision targets, transformed preview/output bounds, both HKRB platforms, compressed HKSC, polytope, DAE/FBX export, source isolation, invalidation, failure recovery, compact UI and BFRES-only entry points.");
    }
}
