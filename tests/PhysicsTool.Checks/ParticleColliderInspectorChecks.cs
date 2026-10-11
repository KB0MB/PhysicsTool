using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class ParticleColliderInspectorChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        var paths = Enumerable.Range(0, 3).Select(f => Path.Combine(output, $"manual-{f}." + (f == 2 ? "bphcl" : "hkcl")))
            .Concat(new[] { Path.Combine(output, "native-animation", "animation-test.hkcl"), Path.Combine(output, "fixture-Armor_1106_RaulSkin_Upper.bphcl") }).Where(File.Exists);
        foreach (var path in paths)
        {
            var window = new MainWindow(graphics: false); window.Show(); window.LoadPath(path); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            var doc = window.Session.Document;
            T Find<T>(string id) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == id);
            void Click(string id) { Find<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            void Undo() { window.Session.Undo(); window.Refresh(); Dispatcher.UIThread.RunJobs(); }
            var items = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.SelectionMode.HasFlag(SelectionMode.Multiple));
            var tabs = window.GetVisualDescendants().OfType<TabStrip>().Single();
            var particle = doc.GetParticleRows(0).First(p => !p.Fixed);
            window.Session.Edit((d, c) => { var row = d.GetParticleRows(c).Single(p => p.Index == particle.Index); row.CollisionMask |= 0x80000000u; d.UpdateParticleRows(c, d.GetParticleRows(c).Select(p => p.Index == row.Index ? row : p).ToArray()); });
            window.Refresh(); items.SelectedIndex = particle.Index; Dispatcher.UIThread.RunJobs();
            var original = doc.CaptureState();
            Require(Find<NumericUpDown>("EditorInverseMass").IsEnabled == !doc.IsBphcl, "Inverse mass format policy incorrect");
            Find<NumericUpDown>("EditorMass").Value = .25m;
            Require(Find<NumericUpDown>("EditorInverseMass").Value == 4, "Mass did not update reciprocal");
            if (!doc.IsBphcl) Find<NumericUpDown>("EditorInverseMass").Value = 3;
            var options = doc.GetParticleColliderOptions(0);
            var available = options.Aggregate(0u, (m, o) => m | (1u << o.BitIndex));
            var originalMask = doc.GetParticleRows(0)[particle.Index].CollisionMask;
            var expanded = Find<Expander>("ParticleCollisionAssignments"); expanded.IsExpanded = true; Dispatcher.UIThread.RunJobs();
            foreach (var option in options) Find<CheckBox>("ColliderSlot" + option.BitIndex).IsChecked = option.BitIndex == options.Last().BitIndex;
            Click("ApplyParticleDetails");
            var edited = doc.GetParticleRows(0)[particle.Index];
            Require(edited.Mass == .25f && edited.InverseMass == (doc.IsBphcl ? 4 : 3), "Mass/inverse edit failed");
            var expectedMask = (originalMask & ~available) | (options.Count == 0 ? 0 : 1u << options.Last().BitIndex);
            Require(edited.CollisionMask == expectedMask, "Mask mapping or reserved bits changed");
            var saved = Path.Combine(output, "particle-inspector-" + Path.GetFileName(path)); window.Session.Save(saved);
            var reload = new HkclService(); reload.Load(saved);
            Require(reload.GetParticleRows(0)[particle.Index].CollisionMask == expectedMask && reload.GetParticleRows(0)[particle.Index].InverseMass == edited.InverseMass, "Particle save/reload failed");
            var inverseBefore = edited.InverseMass; Find<NumericUpDown>("EditorRadius").Value += .001m; Click("ApplyParticleDetails");
            Require(doc.GetParticleRows(0)[particle.Index].InverseMass == inverseBefore, "Radius edit rewrote inverse mass");
            Undo(); Undo(); Require(doc.CaptureState() == original, "Inspector undo failed");
            Find<NumericUpDown>("EditorX").Value = null; Click("ApplyParticleDetails"); Require(doc.CaptureState() == original, "Empty position was saved as zero"); window.Refresh();
            Find<CheckBox>("EditorFixed").IsChecked = true; Dispatcher.UIThread.RunJobs();
            edited = doc.GetParticleRows(0)[particle.Index]; Require(edited.Fixed && edited.Mass == 0 && edited.InverseMass == 0, "Fixed checkbox failed one-click commit");
            Undo();
            if (!doc.IsBphcl) { window.ToggleSimulation(); Require(!Find<Button>("ApplyParticleDetails").IsEffectivelyEnabled, "Simulation allowed particle edit"); window.StopSimulation(); Require(Find<Button>("ApplyParticleDetails").IsEffectivelyEnabled, "Stop did not restore inspector"); }
            var colliders = doc.GetColliderRows(0);
            if (colliders.Count > 0)
            {
                tabs.SelectedIndex = 2; items.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
                var c = colliders[0]; var snapshot = doc.CaptureState();
                Require(Find<ComboBox>("EditorColliderBone").IsEnabled == !doc.IsBphcl, "Collider binding policy incorrect");
                Find<TextBox>("EditorColliderName").Text = "Inspector collider";
                var endpoint = Find<NumericUpDown>("EditorStartX");
                var beforeStart = new Vector3(c.StartX, c.StartY, c.StartZ);
                if (endpoint.IsEnabled) endpoint.Value += .025m;
                Click("ApplyColliderDetails");
                var after = doc.GetColliderRows(0).Single(r => r.Index == c.Index);
                Require(after.Name == "Inspector collider", $"Collider rename failed in {path}: {after.Name}");
                if (endpoint.IsEnabled)
                {
                    var delta = c.IsPlane ? new Vector3(.025f, 0, 0) : Vector3.TransformNormal(new(.025f, 0, 0), c.Transform);
                    Require(Vector3.Distance(new(after.StartX, after.StartY, after.StartZ), beforeStart + delta) < 1e-5, "Collider local coordinate edit moved wrong axis");
                }
                saved = Path.Combine(output, "collider-inspector-" + Path.GetFileName(path)); window.Session.Save(saved); reload.Load(saved);
                var roundTrip = reload.GetColliderRows(0).Single(r => r.Index == c.Index);
                Require(roundTrip.Name == "Inspector collider", "Collider rename failed save/reload");
                Require(Vector3.Distance(new(roundTrip.StartX, roundTrip.StartY, roundTrip.StartZ), new(after.StartX, after.StartY, after.StartZ)) < 1e-5, "Collider geometry failed save/reload");
                Undo(); Require(doc.CaptureState() == snapshot, "Collider undo failed");
                Find<TextBox>("EditorColliderName").Text = " "; Click("ApplyColliderDetails"); Require(doc.CaptureState() == snapshot, "Empty collider name was accepted"); window.Refresh();
                if (!doc.IsBphcl)
                {
                    var binding = Find<ComboBox>("EditorColliderBone"); binding.SelectedIndex = c.BoneIndex < 0 ? 1 : 0; Click("ApplyColliderDetails");
                    after = doc.GetColliderRows(0).Single(r => r.Index == c.Index);
                    Require(after.BoneIndex != c.BoneIndex && Vector3.Distance(new(after.StartX, after.StartY, after.StartZ), beforeStart) < 1e-5, "Rebinding snapped collider");
                    window.Session.Save(saved); reload.Load(saved); roundTrip = reload.GetColliderRows(0).Single(r => r.Index == c.Index);
                    Require(roundTrip.BoneIndex == after.BoneIndex && Vector3.Distance(new(roundTrip.StartX, roundTrip.StartY, roundTrip.StartZ), beforeStart) < 1e-5, "Collider binding failed save/reload"); Undo();
                    window.ToggleSimulation(); Require(!Find<Button>("ApplyColliderDetails").IsEffectivelyEnabled, "Simulation allowed collider edit"); window.StopSimulation(); Require(Find<Button>("ApplyColliderDetails").IsEffectivelyEnabled, "Stop did not restore collider inspector");
                }
                foreach (var width in new[] { 1440, 980 })
                {
                    window.Width = width; window.Height = width == 980 ? 640 : 900; Dispatcher.UIThread.RunJobs();
                    using var capture = window.CaptureRenderedFrame()!; capture.Save(Path.Combine(output, $"collider-inspector-{doc.IsBphcl}-{width}.png"));
                }
                tabs.SelectedIndex = 0; items.SelectedIndex = particle.Index; Find<Expander>("ParticleCollisionAssignments").IsExpanded = true; Dispatcher.UIThread.RunJobs();
                Find<Expander>("ParticleCollisionAssignments").BringIntoView(); Dispatcher.UIThread.RunJobs();
                using var masks = window.CaptureRenderedFrame()!; masks.Save(Path.Combine(output, $"particle-colliders-{doc.IsBphcl}.png"));
            }
            window.Session.Clear(); window.Close();
        }
        Console.WriteLine("PASS: particle mass/inverse policy, collision slots/reserved bits, Fixed, validation, collider local coordinates/name/binding, undo and native save/reload.");
    }
}
