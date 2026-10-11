using Assimp;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;

internal static class EditorAnimationChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run(string output)
    {
        using var importer = new AssimpContext();
        var scene = importer.ImportFile(Path.Combine(output, "weighted.dae"), PostProcessSteps.ValidateDataStructure);
        var clip = new Animation { Name = "RootMotion", DurationInTicks = 30, TicksPerSecond = 30 };
        var track = new NodeAnimationChannel { NodeName = "Root" };
        track.PositionKeys.Add(new(0, Vector3.Zero)); track.PositionKeys.Add(new(30, new(0, 0, .6f)));
        track.RotationKeys.Add(new(0, Quaternion.Identity)); track.ScalingKeys.Add(new(0, Vector3.One));
        clip.NodeAnimationChannels.Add(track); scene.Animations.Add(clip);
        var path = Path.Combine(output, "animated.glb");
        Require(importer.ExportFile(scene, path, "glb2"), "Could not export animated fixture");
        var rig = PortableModelImporter.LoadRig(Path.Combine(output, "weighted.dae"));
        var imported = ModelAnimationClip.Load(path).Single();
        Require(Math.Abs(imported.EndFrame - 30) < .001 && imported.FramesPerSecond == 30, "Assimp ticks were not normalized to transport frames");
        var sampled = rig.AnimationSkeleton(imported, imported.EndFrame / 2);
        var root = sampled.Bones.Single(b => b.Name == "Root");
        Require(Math.Abs(root.Position.Z - .3f) < .0001f, "Animation key interpolation or unit conversion failed");
        var suppressed = rig.AnimationSkeleton(imported, imported.EndFrame / 2, new HashSet<string> { "Root" });
        Require(Math.Abs(suppressed.Bones.Single(b => b.Name == "Root").Position.Z) < .0001f, "Cloth-owned animation track was not suppressed");

        var window = new MainWindow(graphics: false); window.Show();
        window.Session.New("Animated cloth", 0); window.Refresh(); window.ShowEditor();
        Await(FixtureImports.Model(window, Path.Combine(output, "weighted.dae")));
        var state = window.Session.Document.CaptureState();
        var rest = (float[])window.Viewport.Model!.Meshes[0].Vertices.Clone();
        Await(FixtureImports.Animation(window, path));
        window.SeekEditorAnimation(imported.EndFrame / 2);
        Require(Math.Abs(window.Viewport.Model!.Meshes[0].Vertices[2] - rest[2] - .3f) < .0001f, "Timeline did not pose standalone model");
        if (window.Viewport.Data.Colliders.Count > 0)
        {
            var original = window.Viewport.Data.Colliders[0];
            var posed = window.Viewport.SimulatedColliders![original.Index];
            Require(Math.Abs(posed.Start.Z - original.Start.Z - .3f) < .0001f, "Animated collider did not follow input bone");
        }
        window.ToggleEditorPlayback(); window.AdvanceEditorPreview(1f / 60); window.ToggleEditorPlayback();
        Require(window.EditorAnimationFrame > imported.EndFrame / 2, "Standalone playback did not advance");
        window.SeekEditorAnimation(0); window.AnimationPlaybackFps = 30; window.ToggleEditorPlayback(); window.AdvanceEditorPreview(.2f); window.ToggleEditorPlayback();
        Require(Math.Abs(window.EditorAnimationFrame - 3) < .001, "30 fps did not run at half speed");
        window.SeekEditorAnimation(0); window.AnimationPlaybackFps = 60; window.ToggleEditorPlayback(); window.AdvanceEditorPreview(.2f); window.ToggleEditorPlayback();
        Require(Math.Abs(window.EditorAnimationFrame - 6) < .001, "60 fps did not run at normal speed");
        var loop = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Loop"));
        loop.IsChecked = false;
        window.SeekEditorAnimation(imported.EndFrame - .001); window.ToggleEditorPlayback(); window.AdvanceEditorPreview(1f / 60);
        Require(!window.EditorAnimationPlaying && window.EditorAnimationFrame == imported.EndFrame, "Non-looping animation did not stop at final frame");
        window.SeekEditorAnimation(0);
        loop.IsChecked = true; window.ToggleSimulation();
        window.SeekEditorAnimation(imported.EndFrame - .001); window.ToggleEditorPlayback(); window.AdvanceEditorPreview(1f / 60);
        Require(window.EditorAnimationFrame < imported.EndFrame / 2 && window.EditorSimulating, "Animation loop stopped simulation");
        Require(window.Viewport.Simulated!.Values.All(p => float.IsFinite(p.LengthSquared()) && p.Length() < 10), "Loop rebase produced invalid or spiking particles");
        window.ToggleEditorPlayback(); window.StopSimulation();
        Require(!window.EditorSimulating && window.Viewport.Model!.Meshes.SelectMany(m => m.Vertices).All(float.IsFinite), "Stopping standalone simulation broke the model pose");
        var previousFrame = window.EditorAnimationFrame;
        try { Await(window.LoadAnimationPathAsync(Path.Combine(output, "model.obj"))); throw new Exception("Unsupported animation import succeeded"); }
        catch (NotSupportedException) { }
        Require(window.EditorAnimationFrame == previousFrame, "Failed animation import destroyed existing clip");
        Require(window.Session.Document.CaptureState() == state, "Animation changed authored cloth");
        window.SelectEditorAnimation(-1);
        Require(window.Viewport.Model!.Meshes[0].Vertices.SequenceEqual(rest), "Rest-pose choice did not restore model");
        window.SelectEditorAnimation(0); window.SeekEditorAnimation(imported.EndFrame / 2);
        Require(window.Viewport.SimulatedColliders != null, "Switching away from Rest pose lost physics input binding");
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "editor-animation-1440.png"));
        window.Width = 980; window.Height = 640; Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "editor-animation-980.png"));
        window.ShowOverview(); Require(!window.EditorAnimationPlaying && !window.EditorSimulating, "Back left playback running");
        window.Session.Clear(); window.Close();
        Console.WriteLine("PASS: standalone clip import, interpolation, track suppression, timeline, play/pause, loop/stop, animated colliders, simulation rebase and failed-import isolation.");
    }
    private static void Await(Task task)
    {
        while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
    public static void Native(string output, string pack)
    {
        Directory.CreateDirectory(output);
        var resource = new BotwActorResources(pack);
        var name = resource.AnimationResources.First();
        var path = Path.Combine(output, name + ".sbfres");
        File.WriteAllBytes(path, resource.ReadModel(name)!);
        var modelName = resource.Models[0].Resource;
        var modelPath = Path.Combine(output, modelName + ".sbfres");
        File.WriteAllBytes(modelPath, resource.ReadModel(modelName)!);
        var physics = resource.PhysicsFiles.FirstOrDefault(p => p.EndsWith(".hkcl", StringComparison.OrdinalIgnoreCase));
        if (physics != null) File.WriteAllBytes(Path.Combine(output, "animation-test.hkcl"), resource.Read(physics)!);
        var rig = PortableModelImporter.LoadRig(modelPath);
        var clips = ModelAnimationClip.Load(path);
        Require(clips.Count > 0, "Native standalone clips missing");
        var sample = rig.AnimationSkeleton(clips[0], clips[0].EndFrame / 2);
        var model = rig.Sample(sample.Bones.Select(b => new VisualBoneOverride(b.Name, b.Position, b.Rotation, b.Scale)).ToArray());
        Require(model.Meshes.SelectMany(m => m.Vertices).All(float.IsFinite), "Native animation produced nonfinite geometry");
        Require(!model.Meshes[0].Vertices.SequenceEqual(rig.Rest.Meshes[0].Vertices), "Native standalone clip left mesh at rest");
        Console.WriteLine($"PASS: {clips.Count} native BFRES standalone clips and model deformation.");
    }
}
