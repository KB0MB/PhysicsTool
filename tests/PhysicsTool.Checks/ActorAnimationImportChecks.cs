using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using System.Reflection;

internal static class ActorAnimationImportChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Field<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void Await(Task task)
    {
        while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Run(string output, string path)
    {
        Directory.CreateDirectory(output);
        var window = new MainWindow(graphics: false);
        window.Show();
        try
        {
            var import = Field<Button>(window, "_importAnimation");
            Require(!import.IsEnabled, "Animation import enabled without a model or actor");
            window.Session.New("Preserved editor cloth", 0);
            window.Refresh();
            Await(window.LoadActorPathAsync(path));
            window.Viewport.ShowPhysics = false;
            var scene = Field<BotwActorScene>(window, "_actorScene");
            Require(import.IsVisible && import.IsEnabled, "Actor animation import is hidden or disabled");
            var source = scene.Animations.FirstOrDefault(a => a.Name is "Act_Accordion" or "Assassin_DotonCharge") ?? scene.Animations.First();
            var animationPath = Path.Combine(output, "ImportedAnimations.sbfres");
            File.WriteAllBytes(animationPath, scene.Resources.ReadModel(source.ResourceName)!);
            var loaded = ModelAnimationClip.Load(animationPath);
            var names = scene.RestModel.Bones.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matching = loaded.Where(c => c.BoneNames.Any(names.Contains)).ToArray();
            var initialClips = scene.Animations.ToArray();
            var camera = window.Viewport.CameraState;
            var model = scene.RestModel;
            var overlay = scene.Overlay;
            var document = window.Session.Document.CaptureState();
            Await(window.LoadAnimationPathAsync(animationPath));
            Require(scene.Animations.Count == initialClips.Length + matching.Length, "Imported actor clips did not join the existing list");
            Require(initialClips.All(scene.Animations.Contains), "Import removed native actor clips");
            Require(window.IsActorMode && ReferenceEquals(scene.RestModel, model) && ReferenceEquals(scene.Overlay, overlay), "Import replaced actor model or physics");
            Require(window.Viewport.CameraState == camera, "Animation import changed the camera");
            var dropdown = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ActorAnimation");
            Require(dropdown.SelectedIndex == initialClips.Length + 1, "First imported clip was not selected");
            var target = scene.Animations.Last(a => a.Name == source.Name);
            dropdown.SelectedIndex = scene.Animations.IndexOf(target) + 1;
            var slider = window.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "ActorTimeline");
            slider.Value = target.FrameCount * .4;
            Require(scene.CurrentModel.Meshes.SelectMany(m => m.Vertices).All(float.IsFinite), "Imported animation produced invalid model vertices");
            Require(!scene.CurrentModel.Meshes.SelectMany(m => m.Vertices).SequenceEqual(scene.RestModel.Meshes.SelectMany(m => m.Vertices)), "Imported animation did not pose the actor");

            var previousClips = scene.Animations.ToArray();
            var previousFrame = slider.Value;
            try { Await(window.LoadAnimationsAsync(() => [], "unmatched.bfres")); throw new Exception("Empty actor animation import succeeded"); }
            catch (InvalidDataException) { }
            Require(scene.Animations.SequenceEqual(previousClips) && slider.Value == previousFrame && import.IsEnabled,
                "Failed actor animation import changed the previous preview or disabled import");

            window.ToggleSimulation();
            Await(window.LoadAnimationPathAsync(animationPath));
            Require(Field<bool>(window, "_actorSimulating") && slider.Value == 0, "Import did not reset the pose while retaining simulation");
            Require(scene.Animations.Count == previousClips.Length, "Reimport duplicated actor clips");
            Require(scene.Overlay.Particles.All(p => float.IsFinite(p.Position.LengthSquared())), "Reimport produced nonfinite particles");
            window.StopSimulation();

            if (scene.ArmorCatalog != null)
            {
                var selected = scene.Animations[dropdown.SelectedIndex - 1];
                slider.Value = selected.FrameCount * .25;
                var frame = slider.Value;
                Await(window.SwapPlayerArmorAsync(new Dictionary<PlayerArmorSlot, string>(scene.EquippedArmor), CancellationToken.None));
                scene = Field<BotwActorScene>(window, "_actorScene");
                Require(ReferenceEquals(scene.Animations[dropdown.SelectedIndex - 1], selected) && slider.Value == frame,
                    "Equipment swap lost imported animation or current frame");
            }
            Require(window.Session.Document.CaptureState() == document, "Actor animation import modified the editor document");
            Dispatcher.UIThread.RunJobs();
            using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "actor-animation-import.png"));

            using var gate = new ManualResetEventSlim();
            var pending = window.LoadAnimationsAsync(() => { gate.Wait(); return loaded; }, animationPath);
            try { window.ShowOverview(); }
            finally { gate.Set(); }
            Await(pending);
            Require(!window.IsActorMode && Field<List<ToolboxActorAnimation>>(window, "_actorImportedAnimations").Count == 0,
                "Late import restored a closed actor or leaked imported clips");
            Console.WriteLine($"PASS: {Path.GetFileName(path)} actor BFRES imports, timeline, simulation reset, repeat/failed imports, equipment retention and exit cancellation.");
        }
        finally { window.Close(); }
    }
}
