using System.Security.Cryptography;
using System.Numerics;
using Avalonia;
using HKCLTool;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Core;
using PhysicsTool.Desktop;

internal static class ShrineSceneChecks
{
    internal static void Run(string output, string folder)
    {
        Directory.CreateDirectory(output);
        Geometry();
        var catalog = ShrineSceneCatalog.Scan(ShrineSceneCatalog.Root(folder));
        Require(catalog.Count >= 152 && catalog.Any(e => e.RelativePath.Contains("Merged")), "Catalog omitted BANC scenes");
        Require(catalog.Single(e => Path.GetFileName(e.Path) == "Dungeon004_Static.bcett.byml.zs").CompoundPath != null, "Catalog counterpart missing");
        Require(catalog.Count(e => e.ToString().EndsWith("Dungeon004")) == 1 && catalog.All(e => !e.ToString().EndsWith("_Static") && !e.ToString().EndsWith("_Dynamic")), "Static/dynamic pairs were not grouped");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { ShrineSceneCatalog.Scan(ShrineSceneCatalog.Root(folder), cancelled.Token); throw new Exception("Cancelled scan completed"); }
            catch (OperationCanceledException) { }
        }
        int count = 0, actors = 0;
        foreach (string file in Directory.EnumerateFiles(folder, "Dungeon*.bcett.byml.zs"))
        {
            var hash = SHA256.HashData(File.ReadAllBytes(file));
            var scene = ShrineSceneDocument.Load(file); count++; actors += scene.Actors.Count;
            Require(scene.Actors.All(a => a.Index >= 0 && !string.IsNullOrWhiteSpace(a.Name)), "Invalid actor");
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))), "BANC was modified");
        }
        Require(count > 0, "No scenes found");
        var main = new MainWindow(graphics: false); main.Show(); main.ShowShrineScene(); Dispatcher.UIThread.RunJobs();
        var view = main.GetVisualDescendants().OfType<ShrineSceneView>().Single();
        while (view.Busy) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ShrineRoot").Text = ShrineSceneCatalog.Root(folder);
        Await(view.Scan());
        var search = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ShrineSceneSearch");
        search.Text = "Dungeon004";
        Dispatcher.UIThread.RunJobs();
        var sceneList = view.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "ShrineScenes");
        Require(sceneList.ItemCount == 1, $"BANC search did not filter catalog: {sceneList.ItemCount}");
        Await(view.LoadPathAsync(Path.Combine(folder, "Dungeon004_Static.bcett.byml.zs")));
        Require(view.Assets?.Compound != null, "Matching BPHSC was not loaded");
        Require(view.Viewport.Model?.Meshes.Count > 0, "Scene models missing");
        Require(Vector3.Distance(view.Viewport.FlyPosition, (view.Viewport.Model!.BoundsMinimum + view.Viewport.Model.BoundsMaximum) * .5f + Vector3.UnitY * 1.7f) < .01f, "Camera did not start inside the scene");
        Require(view.Assets!.Scene.Actors.Count > ShrineSceneDocument.Load(Path.Combine(folder, "Dungeon004_Static.bcett.byml.zs")).Actors.Count, "Merged/companion scenes missing");
        File.WriteAllLines(Path.Combine(output, "model-notices.txt"), view.Assets.Warnings);
        File.WriteAllLines(Path.Combine(output, "material-notes.txt"), view.Assets.Model.Meshes.DistinctBy(m => m.Material).Select(m =>
            m.Name + "\n" + (m.Material.Totk is { } material ? string.Join(", ", material.Integers.Where(p => !p.Key.StartsWith("totkTexture"))) + "\n" +
            string.Join(", ", material.Textures.Select((t, i) => $"{i}:{t?.Name}")) + "\n" + string.Join(", ", material.Vectors.Where(p => p.Key.StartsWith("p_const"))) : "No TotK material")));
        Console.WriteLine($"Models: {view.Assets.RenderedActors} actors, {view.Viewport.Model!.Meshes.Count} meshes, {view.Viewport.Model.TriangleCount} triangles; {view.Assets.Warnings.Count} notices.");
        ShrinePlacementChecks.Editing(view.Assets, ShrineSceneCatalog.Root(folder), output);
        ShrinePopulationChecks.Editing(view.Assets, ShrineSceneCatalog.Root(folder), output);
        Require(view.Viewport.Data.Particles.Count > 0 && view.Viewport.FlyNavigation, "Scene origins or fly camera missing");
        var list = view.GetVisualDescendants().OfType<ListBox>().Single(b => b.Name == "ShrineActors");
        var before = view.Viewport.CameraState; list.SelectedIndex = 3;
        Require(view.Viewport.Selected.Count == 1 && before == view.Viewport.CameraState, "List selection reset camera");
        view.Viewport.Focus();
        var flyBefore = view.Viewport.FlyPosition;
        view.Viewport.RaiseEvent(new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.W });
        view.Viewport.StepFly(.1f);
        view.Viewport.RaiseEvent(new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyUpEvent, Key = Key.W });
        Require(Vector3.Distance(flyBefore, view.Viewport.FlyPosition) > 1, "WASD camera did not move");
        var flyAfter = view.Viewport.FlyPosition; view.Viewport.StepFly(.1f);
        Require(view.Viewport.FlyPosition == flyAfter, "Camera movement stuck after key release");
        void SetKey(Key key, bool down) => view.Viewport.RaiseEvent(new KeyEventArgs { RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent, Key = key });
        SetKey(Key.W, true); SetKey(Key.D, true); SetKey(Key.LeftShift, true);
        view.Viewport.StepFly(.1f);
        SetKey(Key.D, false);
        var heldBefore = view.Viewport.FlyPosition; view.Viewport.StepFly(.1f);
        Require(Math.Abs(Vector3.Distance(heldBefore, view.Viewport.FlyPosition) - 3.6f) < .01f, "Releasing D stopped held W/Shift");
        var lookPoint = view.Viewport.TranslatePoint(new Point(100, 100), main)!.Value;
        main.MouseDown(lookPoint, MouseButton.Right); main.MouseUp(lookPoint, MouseButton.Right);
        heldBefore = view.Viewport.FlyPosition; view.Viewport.StepFly(.1f);
        Require(Vector3.Distance(heldBefore, view.Viewport.FlyPosition) > 3.5f, "Releasing mouse look stopped held movement");
        SetKey(Key.LeftShift, false);
        heldBefore = view.Viewport.FlyPosition; view.Viewport.StepFly(.1f);
        Require(Math.Abs(Vector3.Distance(heldBefore, view.Viewport.FlyPosition) - 1.2f) < .01f, "Releasing Shift stopped W or kept sprinting");
        SetKey(Key.W, false);
        heldBefore = view.Viewport.FlyPosition; view.Viewport.StepFly(.1f);
        Require(heldBefore == view.Viewport.FlyPosition, "Camera drift after all keys released");
        var clip = Vector4.Transform(new Vector4(view.Viewport.FlyPosition - view.Viewport.ViewDirection * 10, 1), view.Viewport.CameraMatrix);
        Require(Math.Abs(clip.X / clip.W) < .001 && Math.Abs(clip.Y / clip.W) < .001, "Fly camera projection is not centered");
        var movable = view.Editing!.Scene.Actors.First(a => view.Editing.EditRestriction(a.Index) == null && a.Name == "SwitchStepOnce" && a.ParentIndex < 0);
        list.SelectedItem = movable; view.Viewport.FrameSelected(); view.Viewport.Focus(); Dispatcher.UIThread.RunJobs();
        var pointer = view.Viewport.TranslatePoint(new Avalonia.Point(view.Viewport.Bounds.Width / 2, view.Viewport.Bounds.Height / 2), main)!.Value;
        var modelBefore = view.Viewport.Model;
        main.MouseMove(pointer); main.KeyPress(Key.G, RawInputModifiers.None, PhysicalKey.None, null);
        Require(view.Viewport.TransformActive, "Scene G move did not start");
        main.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.None, null);
        main.MouseMove(pointer + new Avalonia.Point(30, 0));
        Require(!ReferenceEquals(view.Viewport.Model, modelBefore), "G move has no model preview");
        main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Require(ReferenceEquals(view.Viewport.Model, modelBefore) && !view.Editing.Dirty, "Cancelled G move changed the scene");
        foreach (var key in new[] { Key.R, Key.S })
        {
            main.MouseMove(pointer); main.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            Require(view.Viewport.TransformActive, $"Scene {key} transform did not start");
            main.MouseMove(pointer + new Point(30, 10));
            Require(!ReferenceEquals(view.Viewport.Model, modelBefore), $"{key} has no model preview");
            main.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Require(ReferenceEquals(view.Viewport.Model, modelBefore) && !view.Editing.Dirty, $"Cancelled {key} changed the scene");
        }
        void Apply(string name) => view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name)
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        void Undo() => view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Undo")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "ShrineRotationY").Value = .35m;
        Apply("ShrineRotationApply");
        Require(Math.Abs(view.Editing.Scene.Actors[movable.Index].Rotation.Y - .35f) < 1e-6f, "Rotation fields failed");
        Undo();
        foreach (string axis in new[] { "X", "Y", "Z" })
            view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "ShrineScale" + axis).Value = 1.5m;
        Apply("ShrineScaleApply");
        Require(view.Editing.Scene.Actors[movable.Index].Scale == new Vector3(1.5f), "Scale fields failed");
        Undo();
        Dispatcher.UIThread.RunJobs();
        using (var frame = main.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "actor-transform-controls.png"));
        view.Viewport.Focus();
        main.MouseDown(pointer, MouseButton.Right);
        SetKey(Key.S, true);
        var backwards = view.Viewport.FlyPosition; view.Viewport.StepFly(.1f);
        Require(!view.Viewport.TransformActive && Vector3.Distance(backwards, view.Viewport.FlyPosition) > 1,
            "S stopped working as backwards movement during mouse-look");
        SetKey(Key.S, false); main.MouseUp(pointer, MouseButton.Right);
        var fields = view.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
        fields[0].Value += 2;
        view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Apply position").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(view.Editing.Scene.Actors[movable.Index].LocalPosition.X == movable.LocalPosition.X + 2, "Apply position did not edit the scene");
        view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Undo").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(view.Editing.Scene.Actors[movable.Index].LocalPosition == movable.LocalPosition, "Scene UI undo failed");
        var second = view.Editing.Scene.Actors.First(a => a.Index != movable.Index && a.MergedPath.Length == 0 && a.ParentIndex < 0 && view.Editing.EditRestriction(a.Index) == null);
        list.SelectedItems!.Add(second);
        Require(view.Viewport.Selected.Count == 2, "Multiple actor selection missing");
        fields = view.GetVisualDescendants().OfType<NumericUpDown>().ToArray(); fields[0].Value = 3;
        view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Move selected").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(view.Editing.Scene.Actors[movable.Index].Position == movable.Position + Vector3.UnitX * 3 && view.Editing.Scene.Actors[second.Index].Position == second.Position + Vector3.UnitX * 3, "Group movement did not move both actors");
        view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Undo").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(view.Viewport.Selected.Count == 2 && view.Editing.Scene.Actors[second.Index].Position == second.Position, "Group undo lost selection or failed to restore positions");
        var standalone = view.Editing.Scene.Actors.First(a => a.ParentIndex < 0 && view.Editing.PopulationRestriction(a.Index, false) == null);
        list.SelectedItem = standalone; list.Focus(); Dispatcher.UIThread.RunJobs();
        int population = view.Editing.Scene.Actors.Count;
        byte[] populationCollision = view.Editing.Compound!.Snapshot();
        main.KeyPress(Key.D, RawInputModifiers.Control, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
        Require(view.Editing.Scene.Actors.Count == population + 1 && view.Viewport.Selected.Count == 1 &&
            ((ShrineActor)list.SelectedItem!).Hash != standalone.Hash, "Ctrl+D did not duplicate and select the copy");
        void Answer(string answer)
        {
            main.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, answer))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        }
        main.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
        Require(main.OwnedWindows.Count == 1, "Delete did not request confirmation"); Answer("Cancel");
        Require(view.Editing.Scene.Actors.Count == population + 1, "Cancelled deletion changed scene");
        view.Viewport.Focus(); main.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs(); Answer("Confirm");
        Require(view.Editing.Scene.Actors.Count == population, "Confirmed Delete did not remove copy");
        Undo(); Undo();
        Require(view.Editing.Scene.Actors.Count == population && view.Editing.Compound!.Snapshot().SequenceEqual(populationCollision), "Population UI undo did not restore collision");
        list.SelectedItem = view.Editing.Scene.Actors.First(a => a.Name.StartsWith("GroundControlActor")); Dispatcher.UIThread.RunJobs();
        Require(view.GetVisualDescendants().OfType<Button>().Where(b => b.Content is "Duplicate" or "Delete").All(b => !b.IsEnabled), "Unsafe group actions enabled");
        var controller = (ShrineActor)list.SelectedItem!;
        var group = view.Editing.GroundControl(controller.Index)!;
        Require(group.Restriction == null && group.MoveRestriction == null, "Dungeon004 group should be editable: " + group.Restriction);
        var previousSpeed = view.Editing.GroundControlValues(controller.Index)["LinearVelX"][0];
        view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "GroundLinearVelX").Value = 7;
        Apply("GroundMovementApply");
        Require(view.Editing.GroundControlValues(controller.Index)["LinearVelX"][0] == 7, "GroundControl movement UI did not apply");
        Undo(); Require(view.Editing.GroundControlValues(controller.Index)["LinearVelX"][0] == previousSpeed, "GroundControl UI undo lost movement setting");
        var groupBefore = view.Editing.Compound!.Snapshot();
        view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "GroundPivotX").Value += 3;
        Apply("GroundPivotApply");
        foreach (int member in group.Members)
            Require(Vector3.Distance(view.Editing.Scene.Actors[member].Position, view.Assets.Scene.Actors[member].Position + new Vector3(3, 0, 0)) < .001f, "GroundControl UI left a member behind");
        Require(!groupBefore.SequenceEqual(view.Editing.Compound!.Snapshot()), "GroundControl UI did not change collision");
        Undo(); Require(groupBefore.SequenceEqual(view.Editing.Compound!.Snapshot()), "GroundControl UI undo lost collision");
        view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "GroundMoveMembers").IsChecked = false;
        view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "GroundPivotX").Value += 1;
        Apply("GroundPivotApply");
        Require(group.Members.Where(i => i != controller.Index).All(i => view.Editing.Scene.Actors[i].Position == view.Assets.Scene.Actors[i].Position), "Pivot-only UI moved members");
        Undo();
        view.Editing.Save(output);
        view.GetVisualDescendants().OfType<CheckBox>().Single(c => (string?)c.Content == "GroundControl only").IsChecked = true;
        Require(list.ItemCount == 4 && view.Viewport.Data.Particles.Count == 4, "GroundControl filter mismatch");
        view.Viewport.Fit(); Dispatcher.UIThread.RunJobs();
        using (var frame = main.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "ground-controls.png"));
        var originalCamera = view.Viewport.CameraState; view.Viewport.Orbit(.3f, .15f); Dispatcher.UIThread.RunJobs();
        Require(view.Viewport.CameraState != originalCamera, "Orbit did not move camera");
        foreach (var size in new[] { (1440, 900), (980, 640) })
        {
            main.Width = size.Item1; main.Height = size.Item2; Dispatcher.UIThread.RunJobs();
            using var frame = main.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, $"scene-{size.Item1}.png"));
        }
        var previous = view.Viewport.Data;
        try { Await(view.LoadPathAsync(Path.Combine(folder, "not-found.bcett.byml"))); throw new Exception("Missing BANC loaded"); }
        catch (FileNotFoundException) { }
        Require(ReferenceEquals(previous, view.Viewport.Data) && !view.Busy, "Failed load lost scene or stayed busy");
        main.Close();
        Console.WriteLine($"PASS: {catalog.Count} catalog entries; {count} shrine BANC scenes / {actors} actors, model transforms and picking, source preservation, embedded scene UI, selection, GroundControl filtering, camera and failure recovery.");
    }
    private static void Geometry()
    {
        var source = new VisualModelMesh { Vertices = [-1,-1,0, 0,0,1, 0,0, 1,-1,0, 0,0,1, 1,0, 0,1,0, 0,0,1, .5f,1], Indices = [0,1,2] };
        var scene = new VisualModelData { BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
        var original = (float[])source.Vertices.Clone();
        ShrineSceneAssets.AddMesh(scene, source, Matrix4x4.CreateScale(2, 3, 1) * Matrix4x4.CreateTranslation(0,0,4), "7:test/mesh");
        ShrineSceneAssets.AddMesh(scene, source, Matrix4x4.CreateTranslation(0,0,2), "8:test/mesh");
        Require(source.Vertices.SequenceEqual(original), "Placement mutated shared geometry");
        Require(scene.BoundsMinimum == new Vector3(-2,-3,2) && scene.BoundsMaximum == new Vector3(2,3,4), "Transformed bounds wrong");
        var picker = new ShrineModelPicking(scene);
        Require(picker.Pick(new(0,0,10), -Vector3.UnitZ, new HashSet<int>()) == 7, "Picking did not find nearest geometry");
        Require(picker.Pick(new(0,0,10), -Vector3.UnitZ, new HashSet<int> { 0 }) == 8, "Hidden geometry remains pickable");
        Require(picker.Pick(new(10,10,10), -Vector3.UnitZ, new HashSet<int>()) == null, "Picking selected empty space");
        ShrineSceneAssets.AddMesh(scene, source, Matrix4x4.CreateScale(-1,1,1), "9:mirrored/mesh");
        Require(scene.Meshes[2].Indices.SequenceEqual(new uint[] { 0,2,1 }), "Mirrored placement did not preserve face orientation");
        var actor = new ShrineActor(0,"test","",1,new(1,2,3),new(0,0,MathF.PI / 2),new(2,2,2),0,0,0,"",[],[]);
        Require(Vector3.Distance(Vector3.Transform(Vector3.UnitX, ShrineSceneAssets.Placement(actor)),new(1,4,3)) < .0001f, "BANC radians/order incorrect");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Await(Task task)
    {
        var end = DateTime.UtcNow.AddMinutes(10);
        while (!task.IsCompleted && DateTime.UtcNow < end) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        if (!task.IsCompleted) throw new TimeoutException(); task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
    }
}
