using System.Numerics;
using System.Buffers.Binary;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class BphclColliderPoseChecks
{
    internal static void InspectSlots(string path)
    {
        var doc = new HkclService(); doc.Load(path); var native = doc.CaptureBphclNativeDocument()!;
        foreach (var cloth in native.Cloths)
        foreach (var sim in cloth.SimCloths)
        {
            Console.WriteLine($"{cloth.Name}: {sim.Particles.Count} particles, {sim.StaticCollisionMasks.Count} mask words, {sim.CollidableItemIndices.Count} colliders, masks={string.Join(',', sim.StaticCollisionMasks.Distinct().Select(m => m.ToString("X8")))}");
            foreach (var config in cloth.Operators.SelectMany(o => o.SimulateConfigs))
                Console.WriteLine($"  All={config.UseAllInstanceCollidables}, used={string.Join(',', config.InstanceCollidablesUsed)}");
            foreach (var (item, slot) in sim.CollidableItemIndices.Select((item, slot) => (item, slot)))
                Console.WriteLine($"  {slot}: {native.Colliders.First(c => c.ItemIndex == item).Name}");
        }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Near(Vector3 a, Vector3 b, string message) => Check(Vector3.Distance(a, b) < 3e-5f, $"{message}: {a} != {b}");
    private static void Shape(ColliderPreviewShape actual, ColliderPreviewShape expected)
    {
        Near(actual.Start, expected.Start, "Collider start jumped"); Near(actual.End, expected.End, "Collider end jumped");
        Near(actual.PlaneNormal, expected.PlaneNormal, "Plane orientation jumped");
        Check(actual.Radius == expected.Radius && actual.EndRadius == expected.EndRadius && actual.BoneIndex == expected.BoneIndex,
            "Collider sizes or attachment changed between rest and simulation");
    }

    internal static void Run(string path, string output)
    {
        Directory.CreateDirectory(output);
        var doc = new HkclService(); doc.Load(path);
        if (!doc.IsBphcl) throw new ArgumentException("A BPHCL fixture is required.");
        Verify(doc);
        doc.ResizeAllCloths(new Vector3(.435f)); Verify(doc);
        var saved = Path.Combine(output, "resized.bphcl"); doc.SaveHkcl(saved, HkclPlatform.Switch);
        doc.Load(saved); Verify(doc);
        // An actual displayed collider edit must update the runtime map too.
        var cloth = Enumerable.Range(0, doc.GetClothSummaries().Count).First(i => doc.GetColliderEditRows(i).Count > 0);
        var before = doc.CaptureState();
        var row = doc.GetColliderEditRows(cloth)[0]; var move = new Vector3(.13f, -.07f, .11f);
        var oldStart = new Vector3(row.StartX, row.StartY, row.StartZ);
        row.StartX += move.X; row.StartY += move.Y; row.StartZ += move.Z;
        row.EndX += move.X; row.EndY += move.Y; row.EndZ += move.Z;
        var transform = row.Transform; transform.Translation += move; row.Transform = transform;
        doc.UpdateColliderEditRows(new[] { row });
        var after = doc.GetParticlePreview(cloth).Colliders.First(c => c.Index == row.Index);
        Near(after.Start, oldStart + move, "Displayed collider edit did not reach rest preview");
        Verify(doc);
        doc.SaveHkcl(Path.Combine(output, "moved.bphcl"), HkclPlatform.Switch);
        var reopened = new HkclService(); reopened.Load(Path.Combine(output, "moved.bphcl")); Verify(reopened);
        doc.RestoreState(before); Verify(doc);
        var selected = new HashSet<int> { row.Index };
        var rotation = Matrix4x4.CreateRotationY(.23f) * Matrix4x4.CreateTranslation(.03f, 0, -.04f);
        var visual = new SelectionTransformPreview(doc, cloth, doc.GetParticlePreview(cloth), PhysicsSelectionKind.Colliders, selected, false).Sample(rotation);
        SelectionTransforms.Apply(doc, cloth, PhysicsSelectionKind.Colliders, selected, rotation, false);
        Shape(doc.GetParticlePreview(cloth).Colliders.First(c => c.Index == row.Index), visual.Colliders.First(c => c.Index == row.Index));
        Verify(doc);
        // Disabled maps intentionally use the stored world transform, despite bone-like names.
        var disabled = doc.CaptureBphclNativeDocument()!;
        var bytes = disabled.Bytes.ToArray();
        foreach (var sim in disabled.Cloths.SelectMany(c => c.SimCloths))
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(checked(disabled.DataSection!.PayloadOffset + (int)sim.DataOffset + 168), 4), -1);
        var disabledPath = Path.Combine(output, "disabled-maps.bphcl"); File.WriteAllBytes(disabledPath, bytes);
        reopened.Load(disabledPath); Verify(reopened);
        Console.WriteLine("PASS collider rest/simulation parity, resizing, numeric editing, animation deltas and save/reopen");
    }

    private static void Verify(HkclService doc)
    {
        var native = doc.CaptureBphclNativeDocument()!; var checkedCloths = 0; var mapped = 0; var differentStored = 0;
        var state = doc.CaptureState();
        for (var cloth = 0; cloth < native.Cloths.Count; cloth++)
        {
            var support = doc.GetTotkSimulationSupport(cloth);
            Check(support.Supported, $"Cloth {native.Cloths[cloth].Name} must be tested, not skipped: {string.Join(" ", support.Blockers)}");
            var rest = doc.GetParticlePreview(cloth);
            var sim = doc.GetTotkSimulationPreview(cloth);
            var slots = native.Cloths[cloth].SimCloths[0].CollidableItemIndices.Count;
            var options = doc.GetParticleColliderOptions(cloth);
            Check(options.Count == Math.Min(slots, 31) && options.Sum(o => o.SlotCount) == slots,
                "Particle inspector omitted colliders or exposed wrapped mask bits");
            if (slots > 31)
                Check(options.Last().BitIndex == 30 && options.Last().SlotCount == slots - 30,
                    "High collider slots must share the inspector's bit-30 option");
            var raw = doc.GetColliderRows(cloth).ToDictionary(c => c.Index);
            var edited = doc.GetColliderEditRows(cloth).ToDictionary(c => c.Index);
            foreach (var collider in sim.Colliders)
            {
                var shown = rest.Colliders.Single(c => c.CollisionBit == collider.CollisionBit);
                Shape(shown, collider);
                var row = edited[collider.Index];
                Near(shown.Start, new(row.StartX, row.StartY, row.StartZ), "Editor coordinates differ from rendered collider");
                Near(shown.End, new(row.EndX, row.EndY, row.EndZ), "Editor endpoints differ from rendered collider");
                if (collider.BoneIndex >= 0) mapped++;
                if (Vector3.Distance(shown.Start, new(raw[collider.Index].StartX, raw[collider.Index].StartY, raw[collider.Index].StartZ)) > 1e-4f) differentStored++;
            }
            var solver = new HkclPreviewSimulator(sim);
            foreach (var c in sim.Colliders.DistinctBy(c => c.Index)) Shape(solver.GetColliderPoses()[c.Index], c);
            for (var i = 0; i < 8; i++) solver.Step(1f / 60);
            foreach (var c in sim.Colliders.DistinctBy(c => c.Index)) Shape(solver.GetColliderPoses()[c.Index], c);
            var delta = Matrix4x4.CreateRotationZ(.15f) * Matrix4x4.CreateTranslation(.1f, .2f, -.15f);
            solver.SetInputBoneDeltas(sim.Bones.ToDictionary(b => b.Index, _ => delta));
            foreach (var c in sim.Colliders.DistinctBy(c => c.Index))
            {
                var actual = solver.GetColliderPoses()[c.Index];
                Near(actual.Start, c.BoneIndex >= 0 ? Vector3.Transform(c.Start, delta) : c.Start, "Animated collider start incorrect");
                Near(actual.End, c.BoneIndex >= 0 ? Vector3.Transform(c.End, delta) : c.End, "Animated collider end incorrect");
            }
            checkedCloths++;
        }
        Check(checkedCloths > 0, "No supported cloths tested");
        Check(doc.CaptureState() == state, "Preview changed file data");
        Console.WriteLine($"PASS {checkedCloths} cloths, {mapped} mapped collider slots, {differentStored} poses different from raw storage");
    }

    internal static void Ui(string path)
    {
        var window = new MainWindow(graphics: false); window.Show();
        try
        {
            window.LoadPath(path); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            var doc = window.Session.Document; var state = doc.CaptureState();
            for (var cloth = 0; cloth < doc.GetClothSummaries().Count; cloth++)
            {
                Check(doc.GetTotkSimulationSupport(cloth).Supported, $"UI must test cloth {cloth}, not skip it");
                window.Session.ClothIndex = cloth; window.Refresh();
                Dispatcher.UIThread.RunJobs();
                var run = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Run TotK preview"));
                Check(run.IsEffectivelyEnabled, $"Run button disabled for cloth {cloth}");
                var rest = window.Viewport.Data.Colliders.DistinctBy(c => c.Index).ToDictionary(c => c.Index);
                run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(window.EditorSimulating, "Run button failed to start simulation");
                window.AdvanceEditorPreview(1f / 60);
                var running = window.Viewport.SimulatedColliders;
                Check(running != null, "Simulation collider overlay was not populated");
                foreach (var c in running!.Values) Shape(c, rest[c.Index]);
                Check(run.IsEffectivelyEnabled && Equals(run.Content, "Stop simulation"), "Stop button unavailable");
                run.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!window.EditorSimulating, "Stop button failed");
                Check(window.Viewport.SimulatedColliders == null, "Simulation collider overlay remained after stopping");
                foreach (var c in window.Viewport.Data.Colliders) Shape(c, rest[c.Index]);
                window.Viewport.Focus(); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
                Check(window.EditorSimulating, "Space failed to start simulation");
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
                Check(!window.EditorSimulating && run.IsEffectivelyEnabled, "Space failed to stop simulation");
            }
            Check(doc.CaptureState() == state, "Starting/stopping simulation edited the file");
            Console.WriteLine("PASS editor Run/Stop simulation collider overlay parity");
        }
        finally { window.StopSimulation(); window.Close(); }
    }
}
