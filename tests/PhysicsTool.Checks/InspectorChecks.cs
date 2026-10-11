using Avalonia;
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

internal static class InspectorChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        foreach (var format in new[] { 0, 1, 2 })
        {
            var window = new MainWindow(graphics: false); window.Show();
            window.LoadPath(Path.Combine(output, $"manual-{format}." + (format == 2 ? "bphcl" : "hkcl")));
            window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            T Find<T>(string id) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == id);
            void Click(string id) { Find<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            void Undo() { window.Viewport.Focus(); window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null); }
            var items = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.SelectionMode.HasFlag(SelectionMode.Multiple));
            items.SelectedIndex = 1;
            var relationships = window.GetVisualDescendants().OfType<ListBox>().Single(l => !l.SelectionMode.HasFlag(SelectionMode.Multiple));
            relationships.SelectedIndex = Enumerable.Range(0, relationships.Items.Count).First(i => relationships.Items[i]!.ToString()!.Contains("Distance", StringComparison.OrdinalIgnoreCase));
            Require(Find<Button>("ApplyRelationship").IsEnabled, "Existing link values unavailable");
            var before = window.Session.Document.CaptureState();
            Find<NumericUpDown>("RelationRestLength").Value = .7m; Find<NumericUpDown>("RelationStiffness").Value = .125m;
            Click("ApplyRelationship");
            Require(window.Session.Document.GetParticleRelationships(0, 1).Any(r => r.RestLength == .7f && r.Stiffness == .125f), "Relationship Apply failed");
            Require(relationships.SelectedItem != null, "Applying relationship lost its selection");
            Undo(); Require(window.Session.Document.CaptureState() == before, "Relationship undo failed");
            var unchanged = window.Session.Document.CaptureState();
            Find<NumericUpDown>("RelationRestLength").Value = null; Click("ApplyRelationship");
            Require(window.Session.Document.CaptureState() == unchanged && Find<TextBlock>("RelationshipEditError").Text!.Length > 0, "Missing constraint value changed data");
            window.Refresh();
            window.Session.Edit((doc, cloth) => doc.AddClothConstraint(cloth, [0, 2], new("hclBendLinkConstraintSet", .8f, .1f, .2f, .3f)));
            var bend = window.Session.Document.GetParticleRelationships(0, 2).Single(r => r.BendMinLength.HasValue);
            unchanged = window.Session.Document.CaptureState();
            try { window.Session.Edit((doc, cloth) => RelationshipEdits.Apply(doc, cloth, 2, bend, new Dictionary<string, float> { ["BendMinLength"] = 2 })); throw new Exception("Inverted bend range accepted"); }
            catch (ArgumentException) { }
            Require(window.Session.Document.CaptureState() == unchanged, "Rejected bend edit changed document");
            window.Session.Edit((doc, cloth) => RelationshipEdits.Apply(doc, cloth, 2, bend, new Dictionary<string, float> { ["BendMinLength"] = .25f, ["StretchMaxLength"] = 1, ["BendStiffness"] = .2f }));
            var saved = Path.Combine(output, $"inspector-{format}." + (format == 2 ? "bphcl" : "hkcl")); window.Session.Save(saved);
            var reload = new EditorSession(); reload.Load(saved);
            Require(reload.Document.GetParticleRelationships(0, 2).Any(r => r.BendMinLength == .25f && r.StretchMaxLength == 1), "Constraint values failed save/reload");
            window.Refresh();
            if (format != 2)
            {
                window.Session.Edit((doc, cloth) => { doc.AddBone(cloth); doc.AddBone(cloth); var rows = doc.GetBoneRows(cloth).ToArray(); rows[1].ParentIndex = 0; rows[2].ParentIndex = 1; rows[1].X = .2f; rows[2].Y = .2f; doc.UpdateBoneRows(cloth, rows, false); });
            }
            var tabs = window.GetVisualDescendants().OfType<TabStrip>().Single(); tabs.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            var index = format == 2 ? 0 : 1; items.SelectedIndex = index;
            var parent = Find<ComboBox>("EditorBoneParent");
            Require(parent.Items.Count == (format == 2 ? 1 : 2), "Parent picker allows self or descendant");
            var boneBefore = window.Session.Document.CaptureState();
            Find<TextBox>("EditorBoneName").Text = "EditedBone";
            Find<NumericUpDown>("EditorRotationX").Value = .23m; Find<NumericUpDown>("EditorRotationY").Value = .41m; Find<NumericUpDown>("EditorRotationZ").Value = -.17m;
            parent.SelectedIndex = 0; Click("ApplyBoneDetails");
            var bone = window.Session.Document.GetBoneRows(0)[index]; var expected = BoneRotationAngles.FromRadians(new(.23f, .41f, -.17f));
            Require(bone.Name == "EditedBone" && bone.ParentIndex == -1 && Math.Abs(Quaternion.Dot(new(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW), expected)) > .99999f, "Bone radians/name/parent edit failed");
            var rotation = new Quaternion(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW);
            Find<TextBox>("EditorBoneName").Text = "RenamedOnly"; Click("ApplyBoneDetails");
            bone = window.Session.Document.GetBoneRows(0)[index];
            Require(new Quaternion(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW) == rotation, "Renaming re-encoded the quaternion");
            window.Session.Save(saved); reload.Load(saved);
            var savedBone = reload.Document.GetBoneRows(0)[index];
            Require(savedBone.Name == "RenamedOnly" && savedBone.ParentIndex == -1 && new Quaternion(savedBone.RotationX, savedBone.RotationY, savedBone.RotationZ, savedBone.RotationW) == rotation, "Bone inspector values failed save/reload");
            Undo(); Undo(); Require(window.Session.Document.CaptureState() == boneBefore, "Bone inspector undo failed");
            Find<NumericUpDown>("EditorScaleX").Value = 0; Click("ApplyBoneDetails");
            Require(window.Session.Document.CaptureState() == boneBefore, "Zero scale changed bone data");
            window.Refresh();
            if (format == 0)
            {
                using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, "bone-inspector.png"));
                tabs.SelectedIndex = 0; items.SelectedIndex = 2;
                relationships.SelectedIndex = Enumerable.Range(0, relationships.Items.Count).First(i => relationships.Items[i]!.ToString()!.Contains("Bend", StringComparison.OrdinalIgnoreCase));
                Dispatcher.UIThread.RunJobs();
                using var relationFrame = window.CaptureRenderedFrame()!; relationFrame.Save(Path.Combine(output, "relationship-inspector.png"));
                window.Width = 980; window.Height = 640; Dispatcher.UIThread.RunJobs();
                using var compactRelation = window.CaptureRenderedFrame()!; compactRelation.Save(Path.Combine(output, "relationship-inspector-compact.png"));
                Find<Button>("ApplyRelationship").BringIntoView(); Dispatcher.UIThread.RunJobs();
                using var compactApply = window.CaptureRenderedFrame()!; compactApply.Save(Path.Combine(output, "relationship-inspector-compact-apply.png"));
                tabs.SelectedIndex = 1; items.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
                Find<Button>("ApplyBoneDetails").BringIntoView(); Dispatcher.UIThread.RunJobs();
                using var compactBone = window.CaptureRenderedFrame()!; compactBone.Save(Path.Combine(output, "bone-inspector-compact.png"));
            }
            window.Session.Clear(); window.Close();
        }
        Console.WriteLine("PASS: HKCL/BPHCL constraint value UI, save/reload/undo, validation, bone radians/name/parent controls, cycle exclusion and quaternion preservation.");
    }
}
