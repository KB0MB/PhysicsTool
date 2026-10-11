using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;
using System.Security.Cryptography;

internal static class RagdollChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static Vector3 Start(ColliderEditRow b) => new(b.StartX, b.StartY, b.StartZ);
    private static Vector3 End(ColliderEditRow b) => new(b.EndX, b.EndY, b.EndZ);
    public static void Run(string output)
    {
        var fixture = Path.Combine(output, "fixture-Mipha.hkrg");
        if (!File.Exists(fixture)) { Console.WriteLine("SKIP: optional fixture-Mipha.hkrg ragdoll checks."); return; }
        var hash = SHA256.HashData(File.ReadAllBytes(fixture));
        var folder = Path.Combine(output, "ragdoll"); Directory.CreateDirectory(folder);
        foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
        {
            var source = new HkclService(); source.Load(fixture); var file = Path.Combine(folder, platform + ".hkrg"); source.SaveHkcl(file, platform);
            var window = new MainWindow(graphics: false); window.Show(); window.LoadPath(file); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            var doc = window.Session.Document; var before = doc.CaptureState();
            var originalSweepError = doc.GetHkrgMotionSweepPositionError();
            T Find<T>(string id) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == id);
            void Click(string id) { Find<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            void Undo() { window.Session.Undo(); window.Refresh(); Dispatcher.UIThread.RunJobs(); Require(doc.CaptureState() == before, "Ragdoll undo was not lossless"); }
            var tabs = window.GetVisualDescendants().OfType<TabStrip>().Single();
            var items = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.SelectionMode.HasFlag(SelectionMode.Multiple));
            void Select(int index) { items.SelectedIndex = index; Dispatcher.UIThread.RunJobs(); }
            Require(tabs.Items.Cast<string>().SequenceEqual(new[] { "Constraints", "Bones", "Rigid bodies" }), "Ragdoll tabs missing");
            Require(!window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Particles follow bones")).IsVisible, "Ragdoll exposed cloth-only follow control");
            var constraint = doc.GetHkrgConstraintRows().First(c => c.HingeMinAngle.HasValue || c.TwistMinAngle.HasValue);
            Select(constraint.Index); var group = constraint.HingeMinAngle.HasValue ? "Hinge" : "Twist";
            var minField = RagdollEdits.ConstraintFields.Single(f => f.Id == group + "MinAngle");
            var maxField = RagdollEdits.ConstraintFields.Single(f => f.Id == group + "MaxAngle");
            Require(window.Viewport.SelectedRagdollConstraints.Contains(constraint.Index) && window.Viewport.SelectedColliders.Contains(constraint.BodyA), "Constraint did not highlight linked bodies");
            Find<NumericUpDown>("Ragdoll" + group + "MinAngle").Value = (decimal)(minField.Read(constraint)!.Value - .05f);
            Click("ApplyRagdollConstraint"); var edited = doc.GetHkrgConstraintRows().Single(c => c.Index == constraint.Index);
            Require(Math.Abs(minField.Read(edited)!.Value - minField.Read(constraint)!.Value + .05f) < 1e-5, "Constraint inspector did not update native limit");
            foreach (var field in RagdollEdits.ConstraintFields.Where(f => f.Id != minField.Id)) Require(field.Read(edited) == field.Read(constraint), $"Constraint edit changed {field.Id}: {field.Read(constraint)} -> {field.Read(edited)}");
            var saved = Path.Combine(folder, "edited-" + platform + ".hkrg"); window.Session.Save(saved);
            var reload = new HkclService(); reload.Load(saved); Require(HkclService.DetectHkclPlatform(saved) == platform && minField.Read(reload.GetHkrgConstraintRows()[constraint.Index]) == minField.Read(edited), "Constraint save/platform failed");
            Undo(); Select(constraint.Index);
            Find<NumericUpDown>("Ragdoll" + group + "MinAngle").Value = (decimal)(maxField.Read(constraint)!.Value + 1);
            Click("ApplyRagdollConstraint"); Require(doc.CaptureState() == before, "Invalid angular range changed document"); window.Refresh();
            var fieldValues = new Dictionary<string, float> { [minField.Id] = float.NaN };
            try { window.Session.Edit((d, _) => RagdollEdits.Constraint(d, constraint.Index, constraint.Name, fieldValues)); throw new Exception("NaN accepted"); } catch (ArgumentException) { }
            Require(doc.CaptureState() == before, "Rejected constraint edit changed data");
            tabs.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
            var capsule = doc.GetColliderRows(0).First(c => c.ShapeType == "Capsule"); Select(capsule.Index);
            var affine = RagdollEdits.Affine(capsule.Transform); Matrix4x4.Invert(affine, out var inverse);
            var originalLocal = Vector3.Transform(Start(capsule), inverse);
            Find<NumericUpDown>("RagdollBodyX").Value += .08m; Find<NumericUpDown>("RagdollRadius").Value += .005m; Click("ApplyRagdollBody");
            var changed = doc.GetColliderRows(0).Single(c => c.Index == capsule.Index);
            Require(Vector3.Distance(Start(changed), Start(capsule) + new Vector3(.08f, 0, 0)) < 2e-5 && Math.Abs(changed.Radius - capsule.Radius - .005f) < 1e-6, "Rigid-body transform/radius edit failed");
            Require(changed.Transform.M14 == capsule.Transform.M14 && changed.Transform.M44 == capsule.Transform.M44, "Rigid-body padding changed");
            Require(doc.GetHkrgMotionSweepPositionError() <= originalSweepError + 1e-5, "Body movement left stale motion sweeps");
            window.Session.Save(saved); reload.Load(saved); Require(Vector3.Distance(Start(reload.GetColliderRows(0)[capsule.Index]), Start(changed)) < 2e-5, "Rigid-body pose save failed");
            Undo(); Select(capsule.Index);
            Find<NumericUpDown>("RagdollStartY").Value += .03m; Click("ApplyRagdollBody"); changed = doc.GetColliderRows(0)[capsule.Index];
            Require(Vector3.Distance(Vector3.Transform(Start(changed), inverse), originalLocal + new Vector3(0, .03f, 0)) < 2e-5, "Capsule local endpoint edit failed"); Undo();
            Select(capsule.Index);
            var angles = BoneRotationAngles.ToRadians(Quaternion.CreateFromRotationMatrix(affine));
            Find<NumericUpDown>("RagdollBodyRotationZ").Value = (decimal)(angles.Z + .2f); Click("ApplyRagdollBody");
            var expectedTransform = Matrix4x4.CreateFromQuaternion(BoneRotationAngles.FromRadians(angles + new Vector3(0, 0, .2f))); expectedTransform.Translation = affine.Translation;
            changed = doc.GetColliderRows(0)[capsule.Index];
            Require(Vector3.Distance(Start(changed), Vector3.Transform(originalLocal, expectedTransform)) < 3e-5 &&
                Vector3.Distance(End(changed), Vector3.Transform(Vector3.Transform(End(capsule), inverse), expectedTransform)) < 3e-5, "Body radians failed to rotate local shape");
            Require(doc.GetHkrgMotionSweepPositionError() <= originalSweepError + 1e-5, "Body rotation left stale motion sweeps");
            foreach (var other in doc.GetColliderRows(0).Where(b => b.Index != capsule.Index))
            {
                var old = source.GetColliderRows(0).Single(b => b.Index == other.Index);
                Require(Vector3.Distance(Start(other), Start(old)) < 1e-6 && other.Radius == old.Radius, "Body rotation changed an unrelated body");
            }
            Undo(); Select(capsule.Index);
            Find<NumericUpDown>("RagdollRadius").Value = 0; Click("ApplyRagdollBody");
            Require(doc.CaptureState() == before, "Zero-radius body edit was accepted"); window.Refresh();
            tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            var bones = doc.GetBoneRows(0); var target = bones.Single(b => b.Index == capsule.BoneIndex); if (target.ParentIndex >= 0) target = bones.Single(b => b.Index == target.ParentIndex);
            Select(target.Index); var beforeWorld = BoneEditGeometry.WorldTransforms(bones); var beforeBodies = doc.GetColliderRows(0);
            Find<NumericUpDown>("RagdollBoneX").Value += .04m; Click("ApplyRagdollBone");
            var afterWorld = BoneEditGeometry.WorldTransforms(doc.GetBoneRows(0));
            foreach (var body in doc.GetColliderRows(0).Where(c => c.BoneIndex >= 0))
            {
                var old = beforeBodies.Single(b => b.Index == body.Index); Matrix4x4.Invert(beforeWorld[body.BoneIndex], out var inv);
                Require(Vector3.Distance(Start(body), Vector3.Transform(Start(old), inv * afterWorld[body.BoneIndex])) < 3e-5, "Bound body failed to inherit ancestor movement");
            }
            var moved = doc.GetBoneRows(0)[target.Index]; Require(moved.RotationX == target.RotationX && moved.RotationW == target.RotationW, "Position edit changed bone quaternion");
            window.Session.Save(saved); reload.Load(saved); Require(Math.Abs(reload.GetBoneRows(0)[target.Index].X - moved.X) < 1e-6, "Ragdoll bone save failed"); Undo();
            Select(target.Index);
            var boneAngles = BoneRotationAngles.ToRadians(new(target.RotationX, target.RotationY, target.RotationZ, target.RotationW));
            Find<NumericUpDown>("RagdollBoneRotationY").Value = (decimal)(boneAngles.Y + .15f); Click("ApplyRagdollBone");
            var rotated = doc.GetBoneRows(0)[target.Index]; var expectedRotation = BoneRotationAngles.FromRadians(boneAngles + new Vector3(0, .15f, 0));
            Require(Math.Abs(Quaternion.Dot(new(rotated.RotationX, rotated.RotationY, rotated.RotationZ, rotated.RotationW), expectedRotation)) > .99999f, "Bone radians did not update its quaternion");
            Require(doc.GetHkrgMotionSweepPositionError() <= originalSweepError + 1e-5, "Bone rotation left stale body motion sweeps"); Undo();
            var skeleton = Find<ComboBox>("RagdollSkeleton"); skeleton.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            var modelBones = doc.GetHkrgModelBoneRows();
            Require(items.ItemCount == modelBones.Count, "Full model skeleton missing");
            Select(0); Require(window.Viewport.SelectedBones.Contains(100000), "Model bone preview index incorrect");
            Find<NumericUpDown>("RagdollBoneX").Value += .03m; Click("ApplyRagdollBone");
            Require(Math.Abs(doc.GetHkrgModelBoneRows()[0].X - modelBones[0].X - .03f) < 1e-5, "Model pose edit not applied");
            window.Session.Save(saved); reload.Load(saved);
            Require(Math.Abs(reload.GetHkrgModelBoneRows()[0].X - doc.GetHkrgModelBoneRows()[0].X) < 1e-6, "Model pose edit did not save"); Undo();
            skeleton.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); Select(target.Index);
            var alignTarget = Find<ComboBox>("RagdollAlignTarget"); alignTarget.SelectedIndex = 0;
            var targetWorld = BoneEditGeometry.WorldTransforms(doc.GetHkrgModelBoneRows())[0]; Click("AlignRagdollBone");
            Require(Vector3.Distance(BoneEditGeometry.WorldTransforms(doc.GetBoneRows(0))[target.Index].Translation, targetWorld.Translation) < 3e-5, "Ragdoll alignment not in world space");
            Require(doc.HkrgConstraintFramesAreFinite(), "Retarget produced invalid joint frames"); Undo(); Select(target.Index);
            window.Viewport.Focus(); window.KeyPress(Key.G, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
            Require(window.Viewport.TransformActive && doc.CaptureState() == before, "Ragdoll move did not start a non-mutating preview");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
            var selection = new HashSet<int> { target.Index, capsule.BoneIndex };
            var delta = Matrix4x4.CreateTranslation(.1f, .02f, -.03f);
            var preview = new SelectionTransformPreview(doc, 0, doc.GetParticlePreview(0), PhysicsSelectionKind.Bones, selection, true).Sample(delta);
            window.Session.Edit((d, _) => SelectionTransforms.Apply(d, 0, PhysicsSelectionKind.Bones, selection, delta, true));
            var batchBodies = doc.GetColliderRows(0);
            foreach (var b in preview.Colliders)
                Require(Vector3.Distance(b.Start, Start(batchBodies.Single(c => c.Index == b.Index))) < 3e-5, "Ragdoll preview and committed body transforms differ");
            Require(Vector3.Distance(Start(batchBodies.Single(c => c.Index == capsule.Index)), Start(capsule) + delta.Translation) < 3e-5, "Selected parent and child moved a ragdoll body twice");
            Require(doc.GetHkrgMotionSweepPositionError() <= originalSweepError + 1e-5, "Batch movement left stale sweeps"); Undo();
            var bodyIndices = doc.GetColliderRows(0).Where(b => b.ShapeType is "Capsule" or "Sphere").Take(3).Select(b => b.Index).ToHashSet();
            window.Session.Edit((d, _) => SelectionTransforms.Apply(d, 0, PhysicsSelectionKind.Colliders, bodyIndices, delta, false));
            foreach (var b in doc.GetColliderRows(0))
                Require(Vector3.Distance(Start(b), Start(beforeBodies.Single(c => c.Index == b.Index)) + (bodyIndices.Contains(b.Index) ? delta.Translation : Vector3.Zero)) < 3e-5, "Multi-body movement changed the wrong body");
            window.Session.Save(saved); reload.Load(saved); Require(reload.GetHkrgMotionSweepPositionError() <= originalSweepError + 1e-5, "Batch body save left stale sweeps"); Undo();
            try { window.Session.Edit((d, _) => SelectionTransforms.Apply(d, 0, PhysicsSelectionKind.Colliders, bodyIndices, Matrix4x4.CreateScale(2), false)); throw new Exception("Ragdoll scaling bypassed inertia guard"); }
            catch (InvalidOperationException) { }
            Require(doc.CaptureState() == before, "Rejected ragdoll scale changed the document");
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs(); Require(!window.OwnedWindows.Any() && doc.CaptureState() == before, "Delete bypassed ragdoll topology guard");
            window.ToggleSimulation(); Require(window.RagdollPreviewRunning, "Ragdoll stress preview did not start");
            for (var step = 0; step < 90; step++) window.AdvanceEditorPreview(1f / 60);
            Require(window.Viewport.SimulatedBones?.Count > 0 && window.Viewport.SimulatedColliders?.Count > 0, "Stress preview did not pose bones and bodies");
            Require(window.Viewport.SimulatedBones!.Values.All(p => float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z)), "Stress preview produced invalid poses");
            Require(doc.CaptureState() == before && !Find<Button>("ApplyRagdollBone").IsEffectivelyEnabled, "Stress preview changed the file or left editing enabled");
            window.ToggleSimulation(); Require(!window.RagdollPreviewRunning && window.Viewport.SimulatedBones == null && Find<Button>("ApplyRagdollBone").IsEffectivelyEnabled, "Stress preview did not restore editor controls and rest pose");
            tabs.SelectedIndex = 0; Select(constraint.Index);
            window.Width = 980; window.Height = 640; Dispatcher.UIThread.RunJobs(); using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(folder, "inspector-" + platform + ".png"));
            window.Session.Clear(); window.Refresh(); Dispatcher.UIThread.RunJobs();
            Require(tabs.Items.Cast<string>().SequenceEqual(new[] { "Particles", "Bones", "Colliders" }), "Leaving HKRG retained ragdoll tabs");
            Require(window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Particles follow bones")).IsVisible, "Leaving HKRG did not restore cloth controls");
            window.Close();
        }
        Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(fixture))), "Ragdoll fixture modified");
        Console.WriteLine("PASS: HKRG constraints, validation, rigid-body local geometry/pose, packed transforms, inherited bone movement, native save/platforms, undo and UI guards.");
    }
}
