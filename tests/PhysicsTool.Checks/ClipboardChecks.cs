using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class ClipboardChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        foreach (var format in new[] { 0, 1, 2 })
        {
            var session = new EditorSession(); session.Load(Path.Combine(output, $"manual-{format}." + (format == 2 ? "bphcl" : "hkcl")));
            var d = session.Document; var clipboard = new SelectionClipboard(); var before = d.CaptureState();
            var particles = d.GetParticleRows(0).Take(2).ToArray(); var selection = particles.Select(p => p.Index).ToHashSet();
            clipboard.Copy(d, 0, PhysicsSelectionKind.Particles, selection); Require(d.CaptureState() == before, "Copy mutated document");
            var ids = clipboard.Paste(session, true); var pastedState = d.CaptureState();
            for (var i = 0; i < ids.Length; i++)
            {
                var p = d.GetParticleRows(0).Single(p => p.Index == ids[i]); var source = particles[i];
                Require(p.X == -source.X && p.Y == source.Y && p.Z == source.Z && p.Fixed == source.Fixed && p.Mass == source.Mass && p.CollisionMask == source.CollisionMask, "Particle paste lost values");
            }
            var file = Path.Combine(output, $"clipboard-{format}." + (format == 2 ? "bphcl" : "hkcl")); session.Save(file); var reload = new HkclService(); reload.Load(file);
            Require(reload.GetParticleRows(0).Count == d.GetParticleRows(0).Count, "Particle paste save failed");
            clipboard.Paste(session); session.Undo(); Require(d.CaptureState() == pastedState, "Repeated paste undo failed"); session.Undo(); Require(d.CaptureState() == before, "Paste undo failed"); session.Redo(); Require(d.CaptureState() == pastedState, "Paste redo failed"); session.Undo();
            session.Edit((doc, cloth) => doc.AddParticle(cloth)); var changed = d.CaptureState();
            try { clipboard.Paste(session); throw new Exception("Stale clipboard accepted"); } catch (InvalidOperationException) { }
            Require(d.CaptureState() == changed, "Rejected stale paste changed data"); session.Undo();
            Require(!clipboard.CanPaste(new HkclService(), 0, PhysicsSelectionKind.Particles) && !clipboard.CanPaste(d, 1, PhysicsSelectionKind.Particles), "Cross-context paste allowed");
            session.Edit((doc, cloth) => SelectionMirror.Apply(doc, cloth, PhysicsSelectionKind.Particles, selection, 0, false, false));
            Require(d.GetParticleRows(0).Where(p => selection.Contains(p.Index)).Zip(particles).All(pair => pair.First.X == -pair.Second.X), "Particle mirror failed"); session.Undo(); Require(d.CaptureState() == before, "Particle mirror undo failed");
            if (format != 2)
            {
                session.Edit((doc, cloth) => {
                    var root = doc.GetBoneRows(cloth)[0]; root.X = .3f; doc.UpdateBoneRows(cloth, [root], false);
                    var child = doc.AddBone(cloth, root); var row = doc.GetBoneRows(cloth).Single(b => b.Index == child); row.X = .2f; row.Name = "Child"; doc.UpdateBoneRows(cloth, [row], false);
                });
                var bones = d.GetBoneRows(0).ToArray(); var worlds = BoneEditGeometry.WorldTransforms(bones);
                clipboard.Copy(d, 0, PhysicsSelectionKind.Bones, bones.Select(b => b.Index).ToHashSet()); ids = clipboard.Paste(session, true);
                var after = d.GetBoneRows(0); var mirrored = BoneEditGeometry.WorldTransforms(after);
                for (var i = 0; i < bones.Length; i++) Require(Vector3.Distance(mirrored[ids[i]].Translation, worlds[bones[i].Index].Translation * new Vector3(-1, 1, 1)) < 1e-5, "Bone mirrored paste world pose failed");
                Require(after.Single(b => b.Index == ids[1]).ParentIndex == ids[0] && after.Select(b => b.Name).Distinct().Count() == after.Count, "Copied hierarchy or unique names failed");
                session.Save(file); reload.Load(file); Require(reload.GetBoneRows(0).Count == after.Count, "Bone paste save failed");
            }
            else Require(!SelectionClipboard.Supports(d, PhysicsSelectionKind.Bones) && !SelectionClipboard.Supports(d, PhysicsSelectionKind.Colliders), "Unsupported BPHCL copy allowed");
        }
        var native = Path.Combine(output, "native-animation", "animation-test.hkcl");
        if (File.Exists(native))
        {
            var session = new EditorSession(); session.Load(native); var d = session.Document;
            var colliders = d.GetColliderRows(0).Take(2).ToArray(); var before = d.CaptureState(); var clipboard = new SelectionClipboard();
            clipboard.Copy(d, 0, PhysicsSelectionKind.Colliders, colliders.Select(c => c.Index).ToHashSet());
            var ids = clipboard.Paste(session, true);
            for (var i = 0; i < ids.Length; i++)
            {
                var row = d.GetColliderRows(0).Single(c => c.Index == ids[i]); var c = colliders[i];
                Require(Vector3.Distance(new(row.StartX, row.StartY, row.StartZ), new(-c.StartX, c.StartY, c.StartZ)) < 1e-5 && row.BoneIndex == c.BoneIndex && row.Radius == c.Radius, "Collider mirrored paste lost world pose/binding");
            }
            var file = Path.Combine(output, "clipboard-colliders.hkcl"); session.Save(file); var reload = new HkclService(); reload.Load(file);
            Require(reload.GetColliderRows(0).Count == d.GetColliderRows(0).Count, "Collider paste save failed"); session.Undo(); Require(d.CaptureState() == before, "Collider paste undo failed");
        }
        var bphcl = Path.Combine(output, "fixture-Armor_1106_RaulSkin_Upper.bphcl");
        if (File.Exists(bphcl))
        {
            var session = new EditorSession(); session.Load(bphcl); var d = session.Document; var before = d.CaptureState();
            var c = d.GetColliderRows(0).First(c => c.ShapeType == "Capsule");
            session.Edit((doc, cloth) => SelectionMirror.Apply(doc, cloth, PhysicsSelectionKind.Colliders, new HashSet<int> { c.Index }, 0, false, false));
            var mirrored = d.GetColliderRows(0).Single(v => v.Index == c.Index);
            Require(Vector3.Distance(new(mirrored.StartX, mirrored.StartY, mirrored.StartZ), new(-c.StartX, c.StartY, c.StartZ)) < 1e-5, "BPHCL collider mirror failed");
            var file = Path.Combine(output, "mirror-collider.bphcl"); session.Save(file); var reload = new HkclService(); reload.Load(file);
            var saved = reload.GetColliderRows(0).Single(v => v.Index == c.Index);
            Require(Vector3.Distance(new(saved.StartX, saved.StartY, saved.StartZ), new(mirrored.StartX, mirrored.StartY, mirrored.StartZ)) < 1e-5 && Vector3.Distance(new(saved.EndX, saved.EndY, saved.EndZ), new(mirrored.EndX, mirrored.EndY, mirrored.EndZ)) < 1e-5, "BPHCL mirror save failed");
            session.Undo(); Require(d.CaptureState() == before, "BPHCL collider mirror undo failed");
        }
        var transform = Matrix4x4.CreateRotationZ(.7f) * Matrix4x4.CreateTranslation(1, 2, 3);
        transform.M14 = .25f; transform.M24 = .3f; transform.M34 = .4f; transform.M44 = .5f;
        var plane = new ColliderEditRow { ShapeType = "Plane", Transform = transform, StartX = 2, StartY = 3, StartZ = 4, EndX = 2, EndY = 3, EndZ = 4, PlaneNormalX = .6f, PlaneNormalY = .8f };
        for (var axis = 0; axis < 3; axis++)
        {
            var reflected = SelectionMirror.ReflectCollider(plane, axis, new(.2f, .4f, .6f));
            var restored = SelectionMirror.ReflectCollider(reflected, axis, new(.2f, .4f, .6f));
            Require(Vector3.Distance(new(restored.StartX, restored.StartY, restored.StartZ), new(plane.StartX, plane.StartY, plane.StartZ)) < 1e-5 && restored.PlaneNormalX == plane.PlaneNormalX && restored.PlaneNormalY == plane.PlaneNormalY, "Plane reflection round-trip failed");
            Require(reflected.Transform.M14 == transform.M14 && reflected.Transform.M44 == transform.M44, "Reflection corrupted packed transform values");
        }
        RunUi(output);
        Console.WriteLine("PASS: multi-item clipboard, mirrored paste, hierarchy, stale/context guards, mirror, native round-trips, undo/redo and shortcuts.");
    }
    private static void RunUi(string output)
    {
        var window = new MainWindow(graphics: false); window.Show(); window.LoadPath(Path.Combine(output, "manual-0.hkcl")); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        T Find<T>(string id) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == id);
        var items = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.SelectionMode.HasFlag(SelectionMode.Multiple));
        items.SelectedItems!.Clear(); items.SelectedItems.Add(items.Items[0]); items.SelectedItems.Add(items.Items[1]); window.Viewport.Focus();
        var camera = window.Viewport.CameraState; var count = window.Session.Document.GetParticleRows(0).Count;
        window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.None, null); window.KeyPress(Key.V, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
        Require(window.Session.Document.GetParticleRows(0).Count == count + 2 && items.SelectedItems.Count == 2 && camera == window.Viewport.CameraState, "Clipboard shortcuts lost selection or moved camera");
        window.Session.Undo(); window.Refresh();
        items.SelectedItems!.Clear(); items.SelectedItems.Add(items.Items[0]); items.SelectedItems.Add(items.Items[1]);
        var before = window.Session.Document.CaptureState();
        var mirror = items.ContextMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Mirror about selection center"));
        mirror.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(window.Session.Document.CaptureState() != before, "Mirror menu did nothing"); window.Session.Undo(); window.Refresh(); Require(window.Session.Document.CaptureState() == before, "Mirror menu undo failed");
        var input = Find<NumericUpDown>("EditorX"); input.Focus(); window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.None, null); Require(window.Session.Document.GetParticleRows(0).Count == count, "Numeric input paste added physics items");
        window.ToggleSimulation(); Require(!Find<Button>("CopySelection").IsEnabled && !Find<Button>("PasteSelection").IsEnabled && !Find<Button>("MirrorSelection").IsEnabled, "Simulation left clipboard controls enabled"); window.StopSimulation(); Require(Find<Button>("CopySelection").IsEnabled, "Stop did not restore clipboard controls");
        window.Width = 980; window.Height = 640; Dispatcher.UIThread.RunJobs(); using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "clipboard-980.png"));
        window.Session.Clear(); window.Close();
    }
}
