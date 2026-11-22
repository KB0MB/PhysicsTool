using System.Numerics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;

internal static class HelperPoseChecks
{
    private static void Require(bool test, string message) { if (!test) throw new Exception(message); }
    private static void Near(float actual, float expected, string label) => Require(Math.Abs(actual - expected) < .0003f, $"{label}: {actual} != {expected}");
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var fixture = (BphyssbDocumentSummary)typeof(SupportBoneSimulationChecks).GetMethod("Fixture", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, ["Pose", false])!;
        fixture = BphyssbBridge.EditConnections(fixture, data => {
            foreach (var list in new[] { "main_bone_list", "support_bone_list" })
            foreach (var pose in SidecarConnectionEditing.List(data, list).Objects)
            {
                var axis = Quaternion.CreateFromYawPitchRoll(.3f, -.2f, .4f);
                var aim = Vector3.Transform(Vector3.UnitZ, axis); var up = Vector3.Transform(Vector3.UnitY, axis);
                pose.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(828945678, 4, new[] { aim.X, aim.Y, aim.Z }.SelectMany(BitConverter.GetBytes).ToArray()));
                pose.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(1133833840, 4, new[] { up.X, up.Y, up.Z }.SelectMany(BitConverter.GetBytes).ToArray()));
            }
        });
        var botw = Path.Combine(output, "pose-source.bphyssb"); File.WriteAllBytes(botw, fixture.Bytes);
        var forTotk = BphyssbBridge.EditConnections(fixture, data => {
            foreach (var item in SidecarConnectionEditing.List(data, "output_single_list").Objects)
                SidecarConnectionEditing.Set(item, BotwAampWriter.Crc32("connection"), 65536);
            foreach (var item in SidecarConnectionEditing.List(data, "output_double_list").Objects)
                SidecarConnectionEditing.Set(item, BotwAampWriter.Crc32("connection_0"), 65536);
            foreach (var list in new[] { "main_bone_list", "support_bone_list" })
                foreach (var pose in SidecarConnectionEditing.List(data, list).Objects)
                    SidecarConnectionEditing.Set(pose, BotwAampWriter.Crc32("space"), -1);
        });
        var totk = Path.Combine(output, "pose-source.bphhb");
        BphhbBridge.ExportWithParents(forTotk, totk, new Dictionary<string, string> { ["PoseDriver"] = "Parent", ["PoseHelper"] = "Parent" });
        foreach (var path in new[] { botw, totk }) Check(path, output);
        CheckFrames();
        Console.WriteLine("PASS: pose frame round trips, both native channel orders, capture fitting, selective rewiring, atomic rejection, undo/redo, save/reload and pose UI.");
    }
    private static void CheckFrames()
    {
        foreach (var botw in new[] { true, false })
        foreach (var space in new[] { -2, -1, 2 })
        {
            var bones = new[] {
                new VisualModelBone { Index = 1, ParentIndex = 2, Name = "Link:Helper", Position = new(1, 2, 3) },
                new VisualModelBone { Index = 2, Name = "Parent", Position = new(2, 3, 4), Rotation = Quaternion.CreateFromYawPitchRoll(.3f, .2f, -.1f), Scale = new(1.2f) }
            };
            var f = new HelperPoseFrame(0, 1, "Helper", space, "Parent", botw, new(1, 3, 2), Quaternion.CreateFromAxisAngle(Vector3.UnitX, .35f), Vector3.UnitZ, Vector3.UnitY);
            float[] values = [.2f, -.4f, .7f, 1, 2, 3];
            var p = f.Pose(bones, values);
            var measured = f.Measure([new VisualModelBone { Index = 1, ParentIndex = 2, Name = p.Name, Position = p.Position, Rotation = p.Rotation, Scale = p.Scale }, bones[1]]);
            for (var i = 0; i < 6; i++) Near(measured[i], values[i], $"Frame {botw}/{space}/{i}");
        }
    }
    private static void Check(string path, string output)
    {
        var session = new EditorSession(); session.Load(path); var doc = session.Document;
        var original = doc.CaptureState();
        var group = doc.GetSidecarDriverGroups().Single();
        var driver = doc.GetHelperPoseFrames(group.Index, true).Single();
        var helper = doc.GetHelperPoseFrames(group.Index, false).Single();
        Near(driver.Translation.X, 1, "Driver reference translation"); Near(helper.Translation.Y, 5, "Helper reference translation");
        Near(driver.Rotation.X, MathF.Sin(.35f / 2), "Driver reference rotation");
        var rest = new VisualModelData { BoundsMinimum = new(0), BoundsMaximum = new(7) };
        rest.Bones.Add(new VisualModelBone { Index = 0, Name = driver.Name, Position = driver.Translation, Rotation = driver.Rotation });
        rest.Bones.Add(new VisualModelBone { Index = 1, Name = helper.Name, Position = helper.Translation, Rotation = helper.Rotation });
        rest.Bones.Add(new VisualModelBone { Index = 2, Name = "Parent" });
        VisualModelData Sample(IReadOnlyList<VisualBoneOverride> overrides)
        {
            var result = new VisualModelData { BoundsMinimum = rest.BoundsMinimum, BoundsMaximum = rest.BoundsMaximum };
            foreach (var b in rest.Bones)
            {
                var p = overrides.LastOrDefault(o => o.Name == b.Name);
                result.Bones.Add(p == null ? b : new VisualModelBone { Index = b.Index, Name = b.Name, ParentIndex = b.ParentIndex, Position = p.Position, Rotation = p.Rotation, Scale = p.Scale });
            }
            return result;
        }
        var rig = new ImportedModelRig(rest, Sample);
        foreach (var inputChannel in Enumerable.Range(0, 6))
        foreach (var smooth in new[] { false, true })
        foreach (var count in new[] { 2, 3 })
        {
            doc.RestoreState(original);
            var samples = (count == 2 ? new[] { -.8f, .8f } : new[] { -.8f, 0f, .8f })
                .Select(x => new HelperPoseExample(x, new Dictionary<int, float[]> { [helper.Bone] = [x * .3f, x * .4f, x * -.2f, x * .5f, .1f, -.1f] })).ToArray();
            var fit = HelperPoseFitting.Fit(samples, new Dictionary<int, int[]> { [helper.Bone] = [0, 1, 2, 3, 4, 5] }, smooth);
            doc.ApplyHelperPoses(group.Index, driver.Record, inputChannel, fit, true);
            foreach (var sample in samples)
            {
                float[] input = new float[6]; input[inputChannel] = sample.Input;
                var posed = Sample([driver.Pose(rest.Bones, input)]);
                var evaluated = HelperPoseEvaluation.Evaluate(doc, posed.Bones, rest.Bones);
                var values = helper.Measure(Sample(evaluated.Poses).Bones);
                for (var c = 0; c < 6; c++) Near(values[c], sample.Helpers[helper.Bone][c], $"{Path.GetExtension(path)} input {inputChannel} output {c}, smooth {smooth}, poses {count}");
            }
            var between = samples[0].Input * .75f + samples[1].Input * .25f;
            float[] midwayInput = new float[6]; midwayInput[inputChannel] = between;
            var midwayDriver = Sample([driver.Pose(rest.Bones, midwayInput)]);
            var midwayOutput = helper.Measure(Sample(HelperPoseEvaluation.Evaluate(doc, midwayDriver.Bones, rest.Bones).Poses).Bones);
            var blend = smooth ? .15625f : .25f;
            for (var c = 0; c < 6; c++) Near(midwayOutput[c], samples[0].Helpers[helper.Bone][c] * (1 - blend) + samples[1].Helpers[helper.Bone][c] * blend, "Native interpolation between captured poses");
            var state = doc.CaptureState();
            try { doc.ApplyHelperPoses(group.Index, driver.Record, inputChannel, fit, false); throw new Exception("Overwrite was not guarded"); }
            catch (InvalidOperationException) { }
            Require(doc.CaptureState() == state, "Rejected overwrite changed document");
        }
        doc.RestoreState(original);
        var prior = doc.GetHelperConnections(helper.Bone).ToArray();
        var response = new HelperPoseResponse(helper.Bone, 5, [new(-1, -.3f, .3f, .3f), new(0, 0, .3f, .3f), new(1, .3f, .3f, .3f)]);
        session.Edit((d, _) => d.ApplyHelperPoses(group.Index, driver.Record, 1, [response], false));
        var changed = doc.CaptureState();
        Require(doc.GetHelperConnections(helper.Bone).Take(5).SequenceEqual(prior.Take(5)), "Unselected responses changed");
        session.Undo(); Require(doc.CaptureState() == original, "Pose undo failed");
        session.Redo(); Require(doc.CaptureState() == changed, "Pose redo failed");
        var saved = Path.Combine(output, "posed" + Path.GetExtension(path)); session.Save(saved);
        var reopened = new HkclService(); reopened.Load(saved);
        Require(reopened.GetHelperConnections(helper.Bone).SequenceEqual(doc.GetHelperConnections(helper.Bone)), "Connections lost on save");
        Require(reopened.GetHelperCurves(group.Index).Last().Keys.SequenceEqual(response.Keys), "Native pose curve lost on save");
        try {
            doc.ApplyHelperPoses(group.Index, driver.Record, 1, [response, new(helper.Bone, 4, [new(float.NaN), new(0), new(1)])], true);
            throw new Exception("Invalid late curve was accepted");
        } catch (ArgumentException) { }
        Require(doc.CaptureState() == changed, "Invalid late response was not atomic");
        doc.RestoreState(original);
        Ui(doc, group.Index, rig, output);
    }
    private static void Ui(HkclService doc, int group, ImportedModelRig rig, string output)
    {
        var original = doc.CaptureState();
        var window = new HelperPoseWindow(doc, group, rig); window.Show(); Dispatcher.UIThread.RunJobs();
        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        void Click(string name) { Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        var rotation = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Rotation"));
        rotation.IsChecked = true; Dispatcher.UIThread.RunJobs();
        Find<Slider>("PoseDriverValue").Value = .6; Dispatcher.UIThread.RunJobs();
        Find<NumericUpDown>("HelperPoseValue1").Value = .4m;
        Click("CaptureHelperPose"); Require(Find<ListBox>("CapturedHelperPoses").ItemCount == 2, "Capture UI did not add a second pose");
        Find<CheckBox>("PoseTest").IsChecked = true; Dispatcher.UIThread.RunJobs();
        Near((float)Find<NumericUpDown>("HelperPoseValue1").Value!, .4f, "Native test pose");
        Require(doc.CaptureState() == original, "Draft UI mutated source");
        using (var screenshot = window.CaptureRenderedFrame()) screenshot?.Save(Path.Combine(output, "pose-ui" + (doc.IsBphhb ? "-totk" : "-botw") + ".png"));
        Click("ApplyHelperPoses"); Require(window.Result == null, "UI silently overwrote response");
        Find<CheckBox>("PoseReplace").IsChecked = true; Click("ApplyHelperPoses");
        Require(window.Result is { Responses.Count: 3 }, "UI did not return rotation response draft");
        Require(doc.CaptureState() == original, "Modal applied outside session transaction");
        window.Close();
    }
}
