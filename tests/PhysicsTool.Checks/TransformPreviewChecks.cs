using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Diagnostics;
using System.Numerics;

internal static class TransformPreviewChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        foreach (var format in new[] { 0, 1, 2 })
        {
            var session = new EditorSession(); session.Load(Path.Combine(output, $"manual-{format}." + (format == 2 ? "bphcl" : "hkcl")));
            CheckPreview(session.Document);
            var before = session.Document.CaptureState();
            var particles = session.Document.GetParticleRows(0);
            var spacing = new Vector3(particles[1].X - particles[2].X, particles[1].Y - particles[2].Y, particles[1].Z - particles[2].Z);
            session.Edit((doc, cloth) => BoneAttachmentEdits.Apply(doc, cloth, PhysicsSelectionKind.Particles, new HashSet<int> { 1, 2 }, 1, 0, true));
            var attached = session.Document.GetParticleRows(0);
            var target = session.Document.GetParticlePreview(0).Bones[0].Position;
            Require(Vector3.Distance(new(attached[1].X, attached[1].Y, attached[1].Z), target) < 1e-5, "Anchor did not snap to target");
            Require(attached[1].Fixed && attached[1].InverseMass == 0 && attached[1].CollisionMask == 0 && !attached[2].Fixed, "Attach fixed the wrong particles");
            Require(Vector3.Distance(new Vector3(attached[1].X - attached[2].X, attached[1].Y - attached[2].Y, attached[1].Z - attached[2].Z), spacing) < 1e-5, "Attach changed group spacing");
            var path = Path.Combine(output, $"attached-{format}" + session.Document.CurrentExtension); session.Save(path);
            var reload = new EditorSession(); reload.Load(path);
            Require(reload.Document.GetParticleRows(0)[1].Fixed, "Fixed attachment did not survive save/reload");
            session.Undo(); Require(session.Document.CaptureState() == before, "Attach undo did not restore document");
        }
        var native = Path.Combine(output, "native-animation", "animation-test.hkcl");
        var nativeBphcl = Path.Combine(output, "fixture-Armor_1106_RaulSkin_Upper.bphcl");
        if (File.Exists(nativeBphcl)) { var nativeDoc = new HkclService(); nativeDoc.Load(nativeBphcl); CheckPreview(nativeDoc); }
        if (File.Exists(native))
        {
            var session = new EditorSession(); session.Load(native); CheckPreview(session.Document);
            var doc = session.Document; var source = doc.GetParticlePreview(0); var selected = source.Particles.Select(p => p.Index).ToHashSet();
            var preview = new SelectionTransformPreview(doc, 0, source, PhysicsSelectionKind.Particles, selected, true);
            var delta = Matrix4x4.CreateTranslation(.03f, .04f, .05f);
            for (int i = 0; i < 10; i++) preview.Sample(delta);
            var watch = Stopwatch.StartNew(); for (int i = 0; i < 500; i++) preview.Sample(delta);
            var fast = watch.Elapsed.TotalMilliseconds / 500;
            var snapshot = doc.CaptureState(); watch.Restart();
            for (int i = 0; i < 8; i++) { var copy = new HkclService(); copy.RestoreState(snapshot); SelectionTransforms.Apply(copy, 0, PhysicsSelectionKind.Particles, selected, delta, true); copy.GetParticlePreview(0); }
            var old = watch.Elapsed.TotalMilliseconds / 8;
            Console.WriteLine($"PROFILE: cached particle preview {fast:0.000} ms/update; previous graph rebuild {old:0.00} ms/update ({source.Particles.Count} particles).");
            if (source.Colliders.Count > 0 && source.Bones.Count > 1)
            {
                var collider = source.Colliders[0];
                var target = source.Bones.First(b => b.Index != collider.BoneIndex);
                session.Edit((d, cloth) => BoneAttachmentEdits.Apply(d, cloth, PhysicsSelectionKind.Colliders, new HashSet<int> { collider.Index }, collider.Index, target.Index, true));
                var result = session.Document.GetColliderRows(0).Single(c => c.Index == collider.Index);
                Require(result.BoneIndex == target.Index, "Collider native binding did not change");
                Require(Vector3.Distance(new Vector3(result.StartX + result.EndX, result.StartY + result.EndY, result.StartZ + result.EndZ) * .5f, target.Position) < 1e-4, "Collider did not snap to bone");
                var path = Path.Combine(output, "attached-collider.hkcl"); session.Save(path);
                var reload = new EditorSession(); reload.Load(path);
                Require(reload.Document.GetColliderRows(0).Single(c => c.Index == collider.Index).BoneIndex == target.Index, "Collider binding failed roundtrip");
                session.Undo(); Require(session.Document.CaptureState() == snapshot, "Collider attach undo failed");
            }
        }
        Console.WriteLine("PASS: lightweight preview/commit parity, immutable baselines, attach spacing/fixed state, collider origin/binding and native roundtrips.");
    }
    private static void CheckPreview(HkclService doc)
    {
        var baseline = doc.CaptureState(); var source = doc.GetParticlePreview(0);
        foreach (var kind in Enum.GetValues<PhysicsSelectionKind>())
        {
            var selection = kind switch { PhysicsSelectionKind.Bones => source.Bones.Take(1).Select(b => b.Index).ToHashSet(),
                PhysicsSelectionKind.Colliders => source.Colliders.Take(2).Select(c => c.Index).ToHashSet(), _ => source.Particles.Take(3).Select(p => p.Index).ToHashSet() };
            if (selection.Count == 0) continue;
            foreach (var follow in new[] { false, true })
            {
                var preview = new SelectionTransformPreview(doc, 0, source, kind, selection, follow);
                foreach (var delta in new[] { Matrix4x4.CreateTranslation(.1f, .2f, -.3f), Matrix4x4.CreateRotationY(.2f) * Matrix4x4.CreateTranslation(.05f, 0, 0) })
                {
                    var displayed = preview.Sample(delta); preview.Sample(Matrix4x4.CreateTranslation(Vector3.One)); displayed = preview.Sample(delta);
                    var copy = new HkclService(); copy.RestoreState(baseline); SelectionTransforms.Apply(copy, 0, kind, selection, delta, follow);
                    var committed = copy.GetParticlePreview(0);
                    bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 2e-4;
                    Require(displayed.Particles.Zip(committed.Particles).All(p => Near(p.First.Position, p.Second.Position)), $"{kind} particle preview disagrees with commit");
                    Require(displayed.Bones.Zip(committed.Bones).All(b => Near(b.First.Position, b.Second.Position) && Near(b.First.AxisX, b.Second.AxisX)), $"{kind} bone preview disagrees with commit");
                    Require(displayed.Colliders.Zip(committed.Colliders).All(c => Near(c.First.Start, c.Second.Start) && Near(c.First.End, c.Second.End)), $"{kind} collider preview disagrees with commit");
                    if (kind == PhysicsSelectionKind.Colliders)
                    {
                        var original = doc.GetColliderRows(0).First(c => selection.Contains(c.Index));
                        var moved = copy.GetColliderRows(0).Single(c => c.Index == original.Index);
                        Require(Near(moved.Transform.Translation, Vector3.Transform(original.Transform.Translation, delta)), "Moved collider left native origin behind");
                    }
                }
            }
        }
        Require(doc.CaptureState() == baseline, "Preview modified native data");
        Require(source.Particles.Zip(doc.GetParticlePreview(0).Particles).All(p => p.First.Position == p.Second.Position), "Preview mutated its geometry baseline");
    }
    public static void RunUi(string output)
    {
        var window = new MainWindow(graphics: false); window.Show(); window.LoadPath(Path.Combine(output, "manual-0.hkcl")); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        window.Viewport.SelectRectangle(new Rect(0, 0, window.Viewport.Bounds.Width, window.Viewport.Bounds.Height), SelectionOperation.Replace);
        var state = window.Session.Document.CaptureState();
        var attach = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Attach to Bone"));
        void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        Click(attach); var dialog = window.OwnedWindows.Single();
        dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "BoneSearch").Text = "does not exist";
        Dispatcher.UIThread.RunJobs();
        var apply = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Attach"));
        Require(!apply.IsEnabled, "Empty search allowed attach");
        dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "BoneSearch").Text = "Root";
        Dispatcher.UIThread.RunJobs();
        Require(apply.IsEnabled, "Searching for a bone did not select it");
        using (var screenshot = dialog.CaptureRenderedFrame()!) screenshot.Save(Path.Combine(output, "attach-bone.png"));
        Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
        Require(window.Session.Document.CaptureState() == state, "Canceled attach changed document");
        Click(attach); dialog = window.OwnedWindows.Single(); Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Attach")));
        Require(window.Session.Document.CaptureState() != state && window.Viewport.Selected.Count == 3, "Attach button did not commit/preserve selection");
        window.Session.Undo(); window.Refresh(); Require(window.Session.Document.CaptureState() == state, "UI attach undo failed");
        window.ToggleSimulation(); Require(!attach.IsEnabled, "Attach allowed during simulation"); window.StopSimulation();
        Require(attach.IsEnabled, "Stopping simulation left attach disabled"); window.Close();
        Console.WriteLine("PASS: Attach picker search, cancel, commit, selection retention, undo and simulation guard.");
        CheckRiggedDrag(output);
    }
    private static void CheckRiggedDrag(string output)
    {
        var window = new MainWindow(graphics: false); window.Show(); window.Session.New("Drag", 0);
        window.Session.Edit((doc, cloth) => { var index = doc.AddBone(cloth); var row = doc.GetBoneRows(cloth).Single(b => b.Index == index); row.Name = "Joint"; doc.UpdateBoneRows(cloth, [row]); });
        window.Refresh(); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        var load = FixtureImports.Model(window, Path.Combine(output, "weighted.dae"));
        while (!load.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        load.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        var tabs = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TabStrip>().Single(); tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        var bones = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Items.Count == 2); bones.SelectedIndex = 1;
        var view = window.Viewport; view.SetView(CameraView.Front, false); view.Fit(); view.Focus();
        var origin = view.TranslatePoint(default, window)!.Value;
        var start = origin + new Point(view.Bounds.Width * .4, view.Bounds.Height * .5);
        var rest = view.Model; var snapshot = window.Session.Document.CaptureState();
        var updates = 0; view.CameraChanged += () => updates++;
        var elapsed = Stopwatch.StartNew();
        window.MouseMove(start); window.KeyPress(Key.G, RawInputModifiers.None, PhysicalKey.None, null);
        Require(view.TransformActive, "Rigged bone drag did not start");
        for (int i = 1; i <= 100; i++) window.MouseMove(start + new Point(i, 0));
        Dispatcher.UIThread.RunJobs(); Thread.Sleep(30); Dispatcher.UIThread.RunJobs();
        Require(updates == 0 && ReferenceEquals(view.Model, rest), "Bone dragging changed the reference model");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Require(ReferenceEquals(view.Model, rest) && window.Session.Document.CaptureState() == snapshot, "Cancel did not restore cached model/document");
        tabs.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); view.Focus(); updates = 0; rest = view.Model;
        window.MouseMove(start); window.KeyPress(Key.G, RawInputModifiers.None, PhysicalKey.None, null);
        for (int i = 1; i <= 100; i++) window.MouseMove(start + new Point(i, 0));
        Thread.Sleep(20); Dispatcher.UIThread.RunJobs();
        Require(updates == 0 && ReferenceEquals(view.Model, rest), "Particle dragging unnecessarily re-skinned the model");
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.Session.Clear(); window.Close();
        Console.WriteLine("PASS: bone/particle dragging preserves the reference model and cancels without document changes.");
    }
}
