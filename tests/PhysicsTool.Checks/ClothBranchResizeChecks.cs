using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class ClothBranchResizeChecks
{
    private static void Require(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    private static Vector3 Position(ParticleEditRow p) => new(p.X, p.Y, p.Z);
    public static void Run(string path, string output)
    {
        Directory.CreateDirectory(output);
        var session = new EditorSession(); session.Load(path);
        var count = session.Document.GetClothSummaries().Count;
        for (int cloth = 0; cloth < count; cloth++)
        {
            session.Load(path); session.ClothIndex = cloth;
            var doc = session.Document; var original = doc.CaptureState();
            var bones = doc.GetBoneRows(cloth); var root = bones.First(b => b.ParentIndex < 0).Index;
            var before = doc.GetParticlePreview(cloth);
            var oldPoses = new HkclPreviewSimulator(before).GetBonePoses();
            var pivot = BoneEditGeometry.WorldTransforms(bones)[root].Translation;
            var particles = doc.GetParticleRows(cloth).ToDictionary(p => p.Index);
            var colliders = doc.GetColliderRows(cloth).ToDictionary(c => c.Index);
            using (var preview = new ClothBranchResizePreview(session, root))
            {
                preview.Sample(.5f, true); preview.Sample(.7f, true);
                Require(!session.CanUndo, "Preview polluted undo history");
                foreach (var p in doc.GetParticleRows(cloth))
                {
                    Require(Vector3.Distance(Position(p), pivot + (Position(particles[p.Index]) - pivot) * .7f) < 2e-6f, "Scale accumulated between previews");
                    Require(MathF.Abs(p.Radius - particles[p.Index].Radius * .7f) < 1e-6f, "Radius not baked");
                    Require(p.Mass == particles[p.Index].Mass && p.InverseMass == particles[p.Index].InverseMass && p.Fixed == particles[p.Index].Fixed && p.CollisionMask == particles[p.Index].CollisionMask, "Particle properties changed");
                }
                foreach (var b in doc.GetBoneRows(cloth))
                {
                    var old = bones.Single(x => x.Index == b.Index);
                    Require(b.ScaleX == old.ScaleX && b.ScaleY == old.ScaleY && b.ScaleZ == old.ScaleZ && b.ScaleW == old.ScaleW, "Wrote bone scale values");
                    Require(b.RotationX == old.RotationX && b.RotationY == old.RotationY && b.RotationZ == old.RotationZ && b.RotationW == old.RotationW, "Changed bone rotation");
                }
                foreach (var c in doc.GetColliderRows(cloth)) Require(MathF.Abs(c.Radius - colliders[c.Index].Radius * .7f) < 1e-6f, "Collider radius not baked");
                var after = doc.GetParticlePreview(cloth);
                for (int i = 0; i < before.Links.Count; i++)
                {
                    var a = before.Links[i]; var b = after.Links[i];
                    void Length(float? old, float? current) { if (old.HasValue) Require(current.HasValue && MathF.Abs(current.Value - old.Value * .7f) < 2e-5f, "Constraint length not scaled"); }
                    Length(a.RestLength, b.RestLength); Length(a.BendMinLength, b.BendMinLength); Length(a.StretchMaxLength, b.StretchMaxLength);
                    Require(a.Stiffness == b.Stiffness, "Changed stiffness");
                }
                for (int i = 0; i < before.BendStiffnessConstraints.Count; i++) Require(MathF.Abs(after.BendStiffnessConstraints[i].RestCurvature - before.BendStiffnessConstraints[i].RestCurvature / .7f) < 2e-4f, "Bend curvature scale wrong");
                CheckOutputs(doc, cloth, oldPoses, oldPoses.Keys.ToHashSet(), pivot, .7f);
                preview.Sample(1, true); Require(doc.CaptureState() == original, "Reset was not exact");
                preview.Sample(.7f, true); preview.Commit();
            }
            var resized = doc.CaptureState();
            Require(session.CanUndo, "Commit did not create undo step");
            session.Undo(); Require(doc.CaptureState() == original && !session.CanUndo, "Resize not a single undo step");
            session.Redo(); Require(doc.CaptureState() == resized, "Redo changed resize");
            foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
            {
                var file = Path.Combine(output, $"resize-{cloth}-{platform}.hkcl"); doc.SaveHkcl(file, platform);
                var loaded = new HkclService(); loaded.Load(file); CheckOutputs(loaded, cloth, oldPoses, oldPoses.Keys.ToHashSet(), pivot, .7f);
                Require(loaded.GetBoneRows(cloth).Zip(bones).All(p => p.First.ScaleX == p.Second.ScaleX && p.First.ScaleY == p.Second.ScaleY && p.First.ScaleZ == p.Second.ScaleZ), "Saved bone scale changed");
            }
            using (var canceled = new ClothBranchResizePreview(session, root)) canceled.Sample(.3f, false);
            Require(doc.CaptureState() == resized, "Cancel did not restore original");
            foreach (var bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                try { doc.ResizeClothBoneBranch(cloth, root, bad); throw new Exception("Invalid scale accepted"); } catch (ArgumentOutOfRangeException) { }
                Require(doc.CaptureState() == resized, "Invalid scale changed document");
            }
            doc.RestoreState(original);
            var selected = before.BoneBindings.Last().BoneIndex;
            var branch = new HashSet<int> { selected };
            for (int i = 0; i < bones.Count; i++) foreach (var b in bones) if (branch.Contains(b.ParentIndex)) branch.Add(b.Index);
            pivot = BoneEditGeometry.WorldTransforms(bones)[selected].Translation;
            doc.ResizeClothBoneBranch(cloth, selected, .9f, false);
            CheckOutputs(doc, cloth, oldPoses, branch, pivot, .9f);
            var newWorlds = BoneEditGeometry.WorldTransforms(doc.GetBoneRows(cloth)); var oldWorlds = BoneEditGeometry.WorldTransforms(bones);
            foreach (var b in bones.Where(b => !branch.Contains(b.Index))) Require(newWorlds[b.Index] == oldWorlds[b.Index], "Resized unrelated bone");
            Require(doc.GetColliderRows(cloth).All(c => c.Radius == colliders[c.Index].Radius), "Unchecked collider option changed radii");
            Console.WriteLine($"PASS: {doc.GetClothName(cloth)} baked resize, partial branch, native rest output, constraints, unchanged bone scales, undo/redo, cancel, Wii U/Switch round trips.");
        }
        Popup(path, output);
    }
    private static void CheckOutputs(HkclService doc, int cloth, IReadOnlyDictionary<int, SimulatedBonePreviewPose> old,
        HashSet<int> branch, Vector3 pivot, float scale)
    {
        var p = doc.GetParticlePreview(cloth); var solver = new HkclPreviewSimulator(p); var poses = solver.GetBonePoses();
        foreach (var b in p.BoneBindings)
        {
            var expected = branch.Contains(b.BoneIndex) ? pivot + (old[b.BoneIndex].Position - pivot) * scale : old[b.BoneIndex].Position;
            Require(Vector3.Distance(poses[b.BoneIndex].Position, expected) < 2e-5f, "Simulation initialization snapped resized bone");
            Require(Vector3.Distance(poses[b.BoneIndex].AxisX, old[b.BoneIndex].AxisX) < 2e-5f, "Resize changed bone axes");
        }
        solver.Step(1f / 60);
        Require(solver.GetPositions().Values.All(p => float.IsFinite(p.LengthSquared())), "Resize produced nonfinite simulation");
    }
    private static void Popup(string path, string output)
    {
        var owner = new MainWindow(graphics: false); owner.Show(); owner.LoadPath(path); owner.ShowEditor(); Dispatcher.UIThread.RunJobs();
        owner.GetVisualDescendants().OfType<TabStrip>().Single(t => t.Items.Count == 3).SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        var button = owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResizeBoneBranch");
        Require(button.IsEnabled, "Resize button not enabled for selected bone");
        var original = owner.Session.Document.CaptureState();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var window = owner.OwnedWindows.OfType<BoneBranchResizeWindow>().Single();
        var size = window.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
        Require(size.Length == 3 && size.All(n => n.Value == 1), "Popup does not use XYZ scale factors");
        foreach (var n in size) n.Value = .5m; Require(window.PreviewNow(), "Popup preview failed");
        foreach (var n in size) n.Value = .7m; Require(window.PreviewNow(), "Popup readjustment failed");
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "resize-popup.png"));
        window.Close(); Dispatcher.UIThread.RunJobs(); Require(owner.Session.Document.CaptureState() == original && !owner.Session.CanUndo, "Popup close did not cancel");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        window = owner.OwnedWindows.OfType<BoneBranchResizeWindow>().Single();
        foreach (var n in window.GetVisualDescendants().OfType<NumericUpDown>()) n.Value = .7m;
        Require(window.PreviewNow(), "Popup second preview failed");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyBranchResize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs(); Require(owner.Session.CanUndo, "Popup Apply did not commit");
        owner.Session.Undo(); Require(owner.Session.Document.CaptureState() == original, "Popup undo failed");
        owner.Refresh();
        Shortcut(owner, original, output);
        owner.Close();
        Console.WriteLine("PASS: live scale popup, baseline readjustment, factor notation, cancel, Apply and single undo.");
    }
    private static void Shortcut(MainWindow owner, string original, string output)
    {
        owner.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Particles follow bones")).IsChecked = false;
        var view = owner.Viewport; view.SetView(CameraView.Front, false); view.Fit();
        var old = owner.Session.Document.GetParticlePreview(0);
        var bones = owner.Session.Document.GetBoneRows(0);
        var pivot = old.Bones.Single(b => view.SelectedBones.Contains(b.Index)).Position;
        var mouse = view.TranslatePoint(new Point(view.Bounds.Width * .5, view.Bounds.Height * .5), owner)!.Value;
        void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None) => owner.KeyPress(key, modifiers, PhysicalKey.None, null);
        foreach (var mode in new[] { 0, 1, 2 })
        {
            owner.MouseMove(mouse); view.Focus(); Press(Key.S);
            Require(view.TransformActive, "S did not start baked bone scaling");
            if (mode == 1) Press(Key.X);
            if (mode == 2) Press(Key.Z, RawInputModifiers.Shift);
            owner.MouseMove(mouse + new Point(-30, 0));
            Require(owner.Session.Document.CaptureState() == original, "S drag mutated native data");
            var preview = view.Data;
            Require(preview.Particles.Any(p => Vector3.Distance(p.Position, old.Particles[p.Index].Position) > 1e-4f), "S did not move particles with follow disabled");
            var previewBones = preview.Bones.ToDictionary(b => b.Index);
            Require(preview.Bones.All(b => b.AxisX == old.Bones[b.Index].AxisX && b.AxisY == old.Bones[b.Index].AxisY && b.AxisZ == old.Bones[b.Index].AxisZ), "S preview scaled bone axes");
            Press(Key.Enter); Dispatcher.UIThread.RunJobs();
            var popup = owner.OwnedWindows.OfType<BoneBranchResizeWindow>().Single();
            var factors = popup.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            Require(factors[0].Value < 1 && (mode != 1 || factors[1].Value == 1) && (mode == 0 || factors[2].Value == 1), "Popup did not retain gesture axis factors");
            var committed = owner.Session.Document.GetParticlePreview(0);
            Require(committed.Particles.All(p => Vector3.Distance(p.Position, preview.Particles[p.Index].Position) < 1e-6f), "S preview and baked particle positions differ");
            Require(committed.Bones.All(b => Vector3.Distance(b.Position, previewBones[b.Index].Position) < 1e-6f), "S preview and baked bone positions differ");
            Require(owner.Session.Document.GetBoneRows(0).Zip(bones).All(p => p.First.ScaleX == p.Second.ScaleX && p.First.ScaleY == p.Second.ScaleY && p.First.ScaleZ == p.Second.ScaleZ), "S wrote bone scale values");
            foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
            {
                var file = Path.Combine(output, $"shortcut-{mode}-{platform}.hkcl");
                owner.Session.Document.SaveHkcl(file, platform);
                var loaded = new HkclService(); loaded.Load(file);
                Require(loaded.GetBoneRows(0).Zip(bones).All(p => p.First.ScaleX == p.Second.ScaleX && p.First.ScaleY == p.Second.ScaleY && p.First.ScaleZ == p.Second.ScaleZ), "S saved scaled bone fields");
                Require(loaded.GetParticlePreview(0).Particles.All(p => Vector3.Distance(p.Position, preview.Particles[p.Index].Position) < 1e-6f), "Saved S positions differ from preview");
            }
            foreach (var factor in factors) factor.Value = .6m;
            Require(popup.PreviewNow(), "Adjusting S scale failed");
            Require(owner.Session.Document.GetParticlePreview(0).Particles.All(p => Vector3.Distance(p.Position, pivot + (old.Particles[p.Index].Position - pivot) * .6f) < 2e-6f), "Popup compounded gesture scale");
            popup.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyBranchResize").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            owner.Session.Undo(); owner.Refresh(); Require(owner.Session.Document.CaptureState() == original && !owner.Session.CanUndo, "S gesture/popup was not one undo step");
        }
        owner.MouseMove(mouse); view.Focus(); Press(Key.S); owner.MouseMove(mouse + new Point(-20, 0)); Press(Key.Escape);
        Require(owner.Session.Document.CaptureState() == original && owner.OwnedWindows.Count == 0, "Escape failed to cancel S preview");
        Console.WriteLine("PASS: actual S / S-X / S-Shift-Z gestures, automatic XYZ popup, lightweight preview/bake parity, follow-off particles, scale-field preservation and single undo.");
    }
}
