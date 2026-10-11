using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using PhysicsTool.Core;
using System.Numerics;
using System.Security.Cryptography;

internal static class HelperEditorChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        var paths = new[] { "bphyssb", "bphhb" }.Select(ext => Path.Combine(output, "fixture-Npc_Zelda_AncientHyrule." + ext)).Where(File.Exists).ToArray();
        foreach (var path in paths)
        {
            HelperConnectionChecks.Run(path, output);
            var hash = SHA256.HashData(File.ReadAllBytes(path));
            var window = new MainWindow(graphics: false); window.Show(); window.LoadPath(path); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            var doc = window.Session.Document;
            T Find<T>(string id) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == id);
            void Click(string id) { Find<Button>(id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            void Answer(string text) { var button = window.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text)); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            void Undo() { window.Session.Undo(); window.Refresh(); Dispatcher.UIThread.RunJobs(); }
            var items = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.SelectionMode.HasFlag(SelectionMode.Multiple));
            void Select(int index) { items.SelectedIndex = doc.GetBoneRows(window.Session.ClothIndex).Select((b, i) => (b, i)).Single(v => v.b.Index == index).i; Dispatcher.UIThread.RunJobs(); }
            var group = doc.GetSidecarDriverGroups().First(g => g.DrivenBoneIndices.Count > 0 && doc.GetHelperCurves(g.Index).Any(c => c.Editable));
            window.Session.ClothIndex = group.Index; window.Refresh(); Select(group.DrivenBoneIndices[0]);
            var bone = doc.GetBoneRows(group.Index).Single(b => b.Index == group.DrivenBoneIndices[0]);
            var original = doc.CaptureState();
            string CurveSignature(HkclService d) => string.Join("|", d.GetSidecarDriverGroups().SelectMany(g => d.GetHelperCurves(g.Index)).OrderBy(c => c.Index).Select(c => $"{c.Index}:{c.DriverIndex}:{c.Attribute}:{string.Join(';', c.Keys)}"));
            var originalCurves = CurveSignature(doc);
            window.Session.Edit((d, _) => d.MoveHelperBone(0, d.GetSidecarBoneRows().Count - 1));
            Require(CurveSignature(doc) == originalCurves, "Bone table reorder changed driver-record curve references"); Undo(); Require(doc.CaptureState() == original, "Whole-table reorder undo failed"); Select(bone.Index);
            Require(!window.Session.IsDirty && Find<Button>("HelperCreate").IsEffectivelyEnabled, "Helper inspection changed data or creation was disabled");
            if (doc.IsBphyssb)
            {
                Find<ComboBox>("HelperBase").SelectedIndex = 1; Click("ApplyHelperDetails");
                Require(doc.GetSidecarBoneRows().Single(b => b.Index == bone.Index).ParentIndex == -2, "BotW model-space helper frame was unavailable");
                Undo(); Select(bone.Index);
            }
            Find<NumericUpDown>("HelperX").Value += .03m; Click("ApplyHelperDetails");
            var edited = doc.GetSidecarBoneRows().Single(b => b.Index == bone.Index);
            Require(Math.Abs(edited.X - bone.X - .03f) < 1e-5 && edited.RotationX == bone.RotationX && edited.RotationW == bone.RotationW, "Helper pose edit or quaternion preservation failed");
            var saved = Path.Combine(output, "helper-editor." + (doc.IsBphhb ? "bphhb" : "bphyssb")); window.Session.Save(saved);
            var reload = new HkclService(); reload.Load(saved); Require(Math.Abs(reload.GetSidecarBoneRows().Single(b => b.Index == bone.Index).X - edited.X) < 1e-5, "Helper pose save/reload failed");
            Undo(); Require(doc.CaptureState() == original, "Helper pose undo failed");
            Find<TextBox>("HelperName").Text = " "; Click("ApplyHelperDetails"); Require(doc.CaptureState() == original, "Empty helper name accepted"); window.Refresh();
            Find<NumericUpDown>("HelperRX").Value += .1m; Click("ApplyHelperDetails");
            edited = doc.GetSidecarBoneRows().Single(b => b.Index == bone.Index); Require(new Quaternion(edited.RotationX, edited.RotationY, edited.RotationZ, edited.RotationW) != new Quaternion(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW), "Helper radians ignored"); Undo();
            Click("HelperCreate"); Answer("Cancel"); Require(doc.CaptureState() == original, "Canceled helper creation changed file");
            Click("HelperCreate"); window.OwnedWindows.Single().GetVisualDescendants().OfType<TextBox>().Single().Text = "InspectorSupport"; Answer("Create");
            var added = doc.GetSidecarBoneRows().Single(b => b.Name == "InspectorSupport"); Require(doc.GetSidecarDriverGroups().Any(g => g.DrivenBoneIndices.Contains(added.Index)), "Created support is disconnected");
            window.Session.Save(saved); reload.Load(saved); Require(reload.GetSidecarBoneRows().Any(b => b.Name == "InspectorSupport"), "Created helper save/reload failed"); Undo(); Require(doc.CaptureState() == original, "Create helper undo failed"); Select(bone.Index);
            var count = doc.GetSidecarBoneRows().Count; Click("HelperDuplicate"); Require(doc.GetSidecarBoneRows().Count == count + 1, "Duplicate helper failed"); Undo(); Require(doc.CaptureState() == original, "Duplicate undo failed"); Select(bone.Index);
            Click("HelperMirror"); Require(doc.CaptureState() != original, "Mirror helper did nothing"); Undo(); Require(doc.CaptureState() == original, "Mirror undo failed");
            items.SelectedIndex = 0; Click("HelperDown"); Require(doc.CaptureState() != original && CurveSignature(doc) == originalCurves, "Helper reorder failed or changed curves"); Undo(); Require(doc.CaptureState() == original, "Helper reorder undo failed"); Select(bone.Index);
            Find<Expander>("HelperCurves").IsExpanded = true; Dispatcher.UIThread.RunJobs();
            var curve = (SidecarCurveRow)Find<ComboBox>("HelperCurve").SelectedItem!;
            var allCurves = doc.GetHelperCurves(group.Index);
            Require(Find<Control>("HelperCurvePlot").Bounds.Height > 0, "Helper response graph missing");
            Find<ComboBox>("HelperCurvePreset").SelectedIndex = 1; Click("HelperCurveSetPreset");
            Require(doc.CaptureState() == original, "Draft response preset changed the document before Apply");
            for (var k = 0; k < curve.Keys.Count; k++)
                Require(Find<NumericUpDown>($"HelperKey{k}_2").Value == 0 && Find<NumericUpDown>($"HelperKey{k}_3").Value == 0, "Smooth preset did not clear tangents");
            window.Refresh(); Dispatcher.UIThread.RunJobs();
            Find<NumericUpDown>("HelperKey0_1").Value += .125m; Click("ApplyHelperCurve");
            var changed = doc.GetHelperCurves(group.Index).Single(c => c.Index == curve.Index);
            Require(Math.Abs(changed.Keys[0].Y - curve.Keys[0].Y - .125f) < 1e-6, "Curve key edit failed");
            window.Session.Save(saved); reload.Load(saved);
            foreach (var before in allCurves)
            {
                var after = reload.GetHelperCurves(group.Index).Single(c => c.Index == before.Index);
                Require(after.DriverIndex == before.DriverIndex && after.Attribute == before.Attribute && after.Keys.Count == before.Keys.Count, "Curve edit changed wiring");
                for (var k = 0; k < before.Keys.Count; k++) Require(after.Keys[k] == (before.Index == curve.Index && k == 0 ? before.Keys[k] + new Vector4(0, .125f, 0, 0) : before.Keys[k]), "Curve edit changed unrelated keys");
            }
            Undo(); Require(doc.CaptureState() == original, "Curve edit undo failed");
            Select(bone.Index);
            Find<Expander>("HelperCurves").IsExpanded = true; Dispatcher.UIThread.RunJobs();
            Find<Expander>("NewHelperCurve").IsExpanded = true; Dispatcher.UIThread.RunJobs();
            var curveCount = doc.GetHelperCurves(group.Index).Count;
            Find<NumericUpDown>("NewHelperCurveGain").Value = .5m;
            Click("CreateHelperCurve");
            Require(doc.GetHelperCurves(group.Index).Count == curveCount + 1, "New curve button did not create a curve");
            var newCurve = (SidecarCurveRow)Find<ComboBox>("HelperCurve").SelectedItem!;
            Require(newCurve.Keys.Count == 3 && newCurve.Keys[2].Y == .5f, "New curve was not selected or initialized correctly");
            Find<ComboBox>("HelperOutputChannel").SelectedIndex = 3; Dispatcher.UIThread.RunJobs();
            var source = doc.IsBphhb ? newCurve.Index : 65536 | newCurve.Index;
            var firstChoice = Find<ComboBox>("HelperConnectionFirst");
            firstChoice.SelectedItem = firstChoice.ItemsSource!.Cast<HelperConnectionSource>().Single(s => s.Index == source);
            Find<ComboBox>("HelperConnectionSecond").SelectedIndex = 0;
            Click("ApplyHelperConnection");
            Require(doc.GetHelperConnections(bone.Index)[3].First == source, "Connection Apply button did not wire the new curve");
            foreach (var width in new[] { 1440, 980 })
            {
                window.Width = width; window.Height = width == 980 ? 640 : 900;
                Dispatcher.UIThread.RunJobs(); Find<Button>("ApplyHelperConnection").BringIntoView(); Dispatcher.UIThread.RunJobs();
                using var connectionImage = window.CaptureRenderedFrame()!; connectionImage.Save(Path.Combine(output, $"helper-connections-{doc.IsBphhb}-{width}.png"));
            }
            window.Width = 1440; window.Height = 900;
            Find<Expander>("NewHelperCurve").IsExpanded = true; Dispatcher.UIThread.RunJobs(); Find<Button>("CreateHelperCurve").BringIntoView(); Dispatcher.UIThread.RunJobs();
            using (var createImage = window.CaptureRenderedFrame()!) createImage.Save(Path.Combine(output, $"helper-new-curve-{doc.IsBphhb}.png"));
            Undo(); Undo(); Require(doc.CaptureState() == original, "Curve creation/connection undo failed"); Select(bone.Index);
            Click("HelperDeleteIndividual"); Answer("Cancel"); Require(doc.CaptureState() == original, "Canceled individual deletion changed data");
            Click("HelperDeleteIndividual"); Answer("Confirm");
            Require(!doc.GetSidecarDriverGroups().Any(g => g.DrivenBoneIndices.Contains(bone.Index)), "Individual delete button left the helper active");
            Undo(); Require(doc.CaptureState() == original, "Individual deletion undo failed"); Select(bone.Index);
            try { window.Session.Edit((d, c) => d.UpdateHelperCurve(c, curve.Index, [new Vector4(float.NaN)])); throw new Exception("Invalid curve accepted"); } catch (ArgumentException) { }
            Require(doc.CaptureState() == original, "Rejected curve changed state");
            Click("HelperRemoveGroup"); Answer("Cancel"); Require(doc.CaptureState() == original, "Canceled group removal changed state");
            var groups = doc.GetSidecarDriverGroups().Count; Click("HelperRemoveGroup"); Answer("Confirm"); Require(doc.GetSidecarDriverGroups().Count == groups - 1, "Group deletion failed"); Undo(); Require(doc.CaptureState() == original, "Group delete undo failed");
            foreach (var width in new[] { 1440, 980 })
            {
                window.Viewport.Fit();
                window.Width = width; window.Height = width == 980 ? 640 : 900; Find<Expander>("HelperCurves").IsExpanded = false; Dispatcher.UIThread.RunJobs();
                using var image = window.CaptureRenderedFrame()!; image.Save(Path.Combine(output, $"helper-inspector-{doc.IsBphhb}-{width}.png"));
            }
            window.Width = 1440; window.Height = 900;
            Find<Expander>("HelperCurves").IsExpanded = true; Dispatcher.UIThread.RunJobs(); Find<Button>("ApplyHelperCurve").BringIntoView(); Dispatcher.UIThread.RunJobs();
            using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, $"helper-curves-{doc.IsBphhb}.png"));
            Find<Control>("HelperCurvePlot").BringIntoView(); Dispatcher.UIThread.RunJobs();
            using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, $"helper-curve-plot-{doc.IsBphhb}.png"));
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Helper fixture modified");
            var restModel = new VisualModelData();
            foreach (var b in doc.GetSidecarBoneRows()) restModel.Bones.Add(new VisualModelBone { Index = b.Index, Name = b.Name, ParentIndex = -1, Position = new(b.X, b.Y, b.Z) });
            var rig = new ImportedModelRig(restModel, poses => {
                var result = new VisualModelData(); var byName = poses.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
                foreach (var b in restModel.Bones) result.Bones.Add(byName.TryGetValue(b.Name, out var p)
                    ? new VisualModelBone { Index = b.Index, Name = b.Name, ParentIndex = -1, Position = p.Position, Rotation = p.Rotation, Scale = p.Scale } : b);
                return result;
            });
            void Await(Task task) { while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs(); }
            Await(window.LoadModelAsync(() => rig, "helper-preview.bfres"));
            Require(Find<CheckBox>("EvaluateHelpers").IsEffectivelyEnabled, "Helper evaluation toggle stayed disabled after loading a model");
            var driverName = doc.GetSidecarBoneRows().Single(b => b.Index == group.DriverBoneIndices.First()).Name;
            var animation = new Assimp.Animation { Name = "HelperSweep", DurationInTicks = 30, TicksPerSecond = 30 };
            var track = new Assimp.NodeAnimationChannel { NodeName = driverName };
            track.RotationKeys.Add(new(0, Quaternion.Identity)); track.RotationKeys.Add(new(30, Quaternion.CreateFromAxisAngle(Vector3.UnitX, .3f)));
            animation.NodeAnimationChannels.Add(track); var clip = new ModelAnimationClip(animation, 1);
            Await(window.LoadAnimationsAsync(() => new[] { clip }, "helper-animation.bfres"));
            var snapshot = doc.CaptureState(); window.SeekEditorAnimation(20);
            var animatedModel = rig.AnimationSkeleton(clip, 20);
            var expected = HelperPoseEvaluation.Evaluate(doc, animatedModel.Bones, restModel.Bones).Poses;
            Require(window.Viewport.Model!.Bones.All(b => Vector3.Distance(b.Position, expected.Single(p => p.Name == b.Name).Position) < 1e-5f), "Standalone timeline bypassed the helper graph");
            Find<CheckBox>("EvaluateHelpers").IsChecked = false;
            Require(window.Viewport.Model!.Bones.Zip(animatedModel.Bones).All(p => Vector3.Distance(p.First.Position, p.Second.Position) < 1e-5f && Math.Abs(Quaternion.Dot(p.First.Rotation, p.Second.Rotation)) > .99999f), "Disabling helper evaluation did not restore animation-only poses");
            Find<CheckBox>("EvaluateHelpers").IsChecked = true;
            Require(doc.CaptureState() == snapshot, "Helper animation evaluation wrote poses to the source document");
            window.Session.Clear(); window.Close();
        }
        Console.WriteLine(paths.Length == 0 ? "SKIP: helper editor native fixtures unavailable." : "PASS: helper pose/radians, create/duplicate/mirror/order/delete, curves/wiring, validation, undo, native save/reload and source preservation.");
    }
}
