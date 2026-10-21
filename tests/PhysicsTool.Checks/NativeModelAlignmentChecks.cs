using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;

internal static class NativeModelAlignmentChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void CheckBoneEdits(string physicsPath, string modelPath)
    {
        var rig = PortableModelImporter.LoadRig(modelPath);
        var window = new MainWindow(graphics: false); window.Show();
        window.LoadPath(physicsPath); window.ShowEditor();
        var load = window.LoadModelAsync(() => rig, modelPath);
        while (!load.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        load.GetAwaiter().GetResult();
        window.Viewport.ShowPhysics = false;
        var doc = window.Session.Document;
        for (int cloth = 0; cloth < doc.GetClothSummaries().Count; cloth++)
        {
            window.Session.ClothIndex = cloth; window.Refresh();
            foreach (var row in doc.GetBoneRows(cloth).ToArray())
            {
                var before = window.Viewport.Data.Bones.Single(b => b.Index == row.Index);
                var target = window.Viewport.Model!.Bones.FirstOrDefault(b => b.Name == row.Name);
                if (target == null) { Console.WriteLine($"UNMATCHED {cloth} {row.Name}"); continue; }
                var original = window.Viewport.Model;
                window.Session.Edit((d, c) => {
                    var edit = d.GetBoneRows(c).Single(b => b.Index == row.Index);
                    edit.X += .2f; d.UpdateBoneRows(c, [edit], false);
                });
                window.Refresh();
                var delta = window.Viewport.Data.Bones.Single(b => b.Index == row.Index).Position - before.Position;
                var moved = window.Viewport.Model!.Bones.First(b => b.Name == row.Name);
                Require(delta.Length() > .1f, "Cloth bone did not move");
                Require(Vector3.Distance(moved.Position, target.Position) < 1e-5f, $"{cloth}:{row.Name}: editor altered BFRES rest bone");
                var displacement = 0f;
                for (int m = 0; m < original.Meshes.Count; m++)
                    for (int v = 0; v < original.Meshes[m].Vertices.Length; v += 8)
                    {
                        var a = original.Meshes[m].Vertices; var b = window.Viewport.Model.Meshes[m].Vertices;
                        displacement = Math.Max(displacement, Vector3.Distance(new(a[v], a[v+1], a[v+2]), new(b[v], b[v+1], b[v+2])));
                    }
                Require(displacement < 1e-5f, $"{cloth}:{row.Name}: editor deformed the BFRES");
                window.Session.Undo(); window.Refresh();
            }
            var originalState = doc.CaptureState();
            var model = window.Viewport.Model!;
            var targets = MatchModelBonesWindow.Choices(window.Viewport.Data.Bones, model).Where(c => c.Target != null)
                .ToDictionary(c => c.Index, c => MatchModelBonesWindow.Matrix(c.Target!));
            window.ApplyModelBoneMatch(targets);
            var fitted = doc.GetParticlePreview(cloth);
            foreach (var b in fitted.Bones.Where(b => targets.ContainsKey(b.Index)))
                Require(Vector3.Distance(b.Position, targets[b.Index].Translation) < 1e-4f, "Matching did not reach target bone " + b.Name);
            var solver = new HkclPreviewSimulator(fitted);
            foreach (var binding in fitted.BoneBindings)
                Require(Vector3.Distance(solver.GetBonePoses()[binding.BoneIndex].Position, fitted.Bones.Single(b => b.Index == binding.BoneIndex).Position) < 2e-4f,
                    "Matched bone snapped back on simulation initialization");
            var result = doc.CaptureState(); var references = window.Session.ModelBoneReferences;
            var output = Path.GetFullPath("artifacts/model-bone-match"); Directory.CreateDirectory(output);
            foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
            {
                var file = Path.Combine(output, $"match-{cloth}-{platform}.hkcl"); doc.SaveHkcl(file, platform);
                var loaded = new HkclService(); loaded.Load(file);
                var roundTrip = loaded.GetParticlePreview(cloth); var roundSolver = new HkclPreviewSimulator(roundTrip);
                foreach (var b in roundTrip.BoneBindings)
                    Require(Vector3.Distance(roundSolver.GetBonePoses()[b.BoneIndex].Position, fitted.Bones.Single(p => p.Index == b.BoneIndex).Position) < 2e-4f,
                        "Save/reload lost matched output binding");
            }
            window.Session.Undo(); window.Refresh();
            Require(doc.CaptureState() == originalState && !window.Session.ModelBoneReferences.ContainsKey(window.Session.SelectedEntryId), "Matching undo lost geometry or preview binding");
            window.Session.Redo(); window.Refresh();
            Require(doc.CaptureState() == result && ReferenceEquals(references, window.Session.ModelBoneReferences), "Matching redo lost preview binding");
            window.ToggleSimulation(); window.StopSimulation();
            Require(window.Viewport.Model!.Meshes.SelectMany(m => m.Vertices).SequenceEqual(model.Meshes.SelectMany(m => m.Vertices)), "Matching or simulation changed the rest model");
            window.Session.Undo(); window.Refresh();
            Console.WriteLine($"PASS: {doc.GetClothName(cloth)} bone edits leave BFRES untouched; matching, solver initialization and atomic undo/redo.");
        }
        window.Session.ClothIndex = 1; window.Refresh(); Dispatcher.UIThread.RunJobs();
        var sourceState = doc.CaptureState();
        var sourceRows = doc.GetBoneRows(1).ToDictionary(b => b.Index);
        var entry = window.Session.SelectedEntryId;
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var open = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "MatchBonesToModel");
        Require(open.IsEnabled, "Model matching is disabled after loading a BFRES");
        Click(open); Dispatcher.UIThread.RunJobs();
        var picker = window.OwnedWindows.OfType<MatchModelBonesWindow>().Single();
        var buttons = picker.GetVisualDescendants().OfType<Button>().ToArray();
        var apply = buttons.Single(b => b.Name == "ApplyModelBoneMatch");
        Click(buttons.Single(b => Equals(b.Content, "Select none")));
        Require(!apply.IsEnabled, "Matching accepted an empty selection");
        var check = picker.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "MatchBone4");
        check.IsChecked = true; Require(apply.IsEnabled, "One selected bone did not enable matching");
        Dispatcher.UIThread.RunJobs();
        using (var frame = picker.CaptureRenderedFrame()!) frame.Save(Path.GetFullPath("artifacts/model-bone-match/matching.png"));
        Click(buttons.Single(b => Equals(b.Content, "Cancel"))); Dispatcher.UIThread.RunJobs();
        Require(doc.CaptureState() == sourceState, "Cancel changed cloth data");
        Click(open); Dispatcher.UIThread.RunJobs();
        picker = window.OwnedWindows.OfType<MatchModelBonesWindow>().Single();
        buttons = picker.GetVisualDescendants().OfType<Button>().ToArray();
        Click(buttons.Single(b => Equals(b.Content, "Select none")));
        picker.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "MatchBone4").IsChecked = true;
        Click(buttons.Single(b => b.Name == "ApplyModelBoneMatch")); Dispatcher.UIThread.RunJobs();
        Require(window.OwnedWindows.Count == 0 && window.Session.ModelBoneReferences.ContainsKey(entry), "Checkbox matching did not complete");
        foreach (var b in doc.GetBoneRows(1).Where(b => b.Index != 4))
        {
            var old = sourceRows[b.Index];
            Require(b.X == old.X && b.Y == old.Y && b.Z == old.Z && b.RotationX == old.RotationX && b.RotationY == old.RotationY && b.RotationZ == old.RotationZ && b.RotationW == old.RotationW,
                "Partial match changed an unselected bone's local pose");
        }
        window.Session.Undo(); window.Refresh();
        Require(doc.CaptureState() == sourceState, "Partial matching undo failed");
        try { doc.MatchClothBonesToModel(1, new Dictionary<int, Matrix4x4> { [4] = new Matrix4x4() }); throw new Exception("Singular target accepted"); }
        catch (InvalidOperationException) { }
        Require(doc.CaptureState() == sourceState, "Failed matching left a partial edit");
        Console.WriteLine("PASS: matching checkbox window, cancellation, partial selection, preserved local poses, rollback and both-platform round trips.");
        window.Close();
    }
    internal static void AuditSimulation(string physicsPath, string modelPath)
    {
        var doc = new HkclService(); doc.Load(physicsPath); var rig = PortableModelImporter.LoadRig(modelPath);
        var originalFront = doc.GetParticlePreview(1);
        var frontBinding = originalFront.BoneBindings.Single(b => b.BoneIndex == 4);
        Console.WriteLine($"CONTACT MantFront1 triangle {frontBinding.ParticleA},{frontBinding.ParticleB},{frontBinding.ParticleC}");
        foreach (var index in new[] { frontBinding.ParticleA, frontBinding.ParticleB, frontBinding.ParticleC })
        {
            var part = originalFront.Particles.Single(p => p.Index == index);
            Console.WriteLine($"CONTACT particle={index} fixed={part.Fixed} radius={part.Radius} mask={part.CollisionMask:X}");
        }
        foreach (var collider in originalFront.Colliders)
        {
            var p = doc.GetParticlePreview(1); p.Colliders.Clear(); p.Colliders.Add(collider);
            var s = new HkclPreviewSimulator(p) { GravityScale = 0 }; s.Step(1f / 60);
            Console.WriteLine($"CONTACT slot={collider.CollisionBit} index={collider.Index} name={collider.Name} bone={collider.BoneIndex} kind={collider.Kind} radius={collider.Radius} start={collider.Start} end={collider.End} boneJump={Vector3.Distance(s.GetBonePoses()[4].Position, p.Bones[4].Position)}");
        }
        foreach (var mode in new[] { "all", "no-collisions", "no-constraints", "inputs-only", "no-attachments", "no-ranges", "no-links", "no-bends" })
        {
            var p = doc.GetParticlePreview(1);
            if (mode is "no-collisions" or "inputs-only") p.Colliders.Clear();
            if (mode is "no-constraints" or "inputs-only") { p.Links.Clear(); p.LocalRanges.Clear(); p.BendStiffnessConstraints.Clear(); }
            if (mode == "no-attachments") p.ParticleAttachments.Clear();
            if (mode == "no-ranges") p.LocalRanges.Clear();
            if (mode == "no-links") p.Links.Clear();
            if (mode == "no-bends") p.BendStiffnessConstraints.Clear();
            var s = new HkclPreviewSimulator(p) { GravityScale = 0 }; s.Step(1f / 60);
            Console.WriteLine($"ISOLATE {mode}: MantFront1 {Vector3.Distance(s.GetBonePoses()[4].Position, p.Bones[4].Position)}");
            var positions = s.GetPositions();
            foreach (var part in p.Particles.Where(part => Vector3.Distance(part.Position, positions[part.Index]) > .001f))
                Console.WriteLine($"PART {mode} {part.Index} fixed={part.Fixed} {part.Position} => {positions[part.Index]} distance={Vector3.Distance(part.Position, positions[part.Index])}");
        }
        var identity = rig.Sample(rig.Rest.Bones.GroupBy(b => b.Name).Select(g => new VisualBoneOverride(g.Key, g.First().Position, g.First().Rotation, g.First().Scale)).ToArray());
        PrintMeshes("IDENTITY", rig.Rest, identity);
        for (int i = 0; i < doc.GetClothSummaries().Count; i++)
        {
            var p = doc.GetParticlePreview(i); var solver = new HkclPreviewSimulator(p);
            var binding = new ClothModelBinding(); binding.Capture(p.Bones); var aligned = binding.Align(rig);
            Console.WriteLine($"CLOTH {i} {doc.GetClothName(i)}");
            foreach (var frames in new[] { 0, 1, 59 })
            {
                for (int step = 0; step < frames; step++) solver.Step(1f / 60);
                var poses = solver.GetBonePoses();
                var overrides = binding.Overrides(aligned.Rest, p, poses);
                Console.WriteLine($"FRAME +{frames}");
                foreach (var b in p.Bones)
                {
                    var q = poses[b.Index];
                    var dx = Vector3.Distance(b.Position, q.Position);
                    var axes = Vector3.Distance(b.AxisX, q.AxisX) + Vector3.Distance(b.AxisY, q.AxisY) + Vector3.Distance(b.AxisZ, q.AxisZ);
                    Console.WriteLine($"BONE {b.Index}:{b.Name} driven={solver.DrivenBoneIndices.Contains(b.Index)} position={dx:G5} axes={axes:G5} rest={b.Position} sim={q.Position} scale=<{b.AxisX.Length()}, {b.AxisY.Length()}, {b.AxisZ.Length()}>");
                }
                PrintMeshes("SIM", aligned.Rest, aligned.Sample(overrides));
            }
        }
    }
    private static void PrintMeshes(string tag, VisualModelData rest, VisualModelData posed)
    {
        for (int m = 0; m < rest.Meshes.Count; m++)
        {
            float maximum = 0;
            for (int j = 0; j < rest.Meshes[m].Vertices.Length; j += 8)
            {
                var a = rest.Meshes[m].Vertices; var b = posed.Meshes[m].Vertices;
                maximum = MathF.Max(maximum, Vector3.Distance(new(a[j], a[j + 1], a[j + 2]), new(b[j], b[j + 1], b[j + 2])));
            }
            Console.WriteLine($"MESH {tag} {rest.Meshes[m].Name}: {maximum:G6}");
        }
    }
    internal static void Run(string physicsPath, string modelPath)
    {
        var rig = PortableModelImporter.LoadRig(modelPath);
        var physics = new HkclService(); physics.Load(physicsPath);
        for (int cloth = 0; cloth < physics.GetClothSummaries().Count; cloth++)
        {
            var preview = physics.GetParticlePreview(cloth);
            var solver = new HkclPreviewSimulator(preview);
            var binding = new ClothModelBinding(); binding.Capture(preview.Bones);
            var aligned = binding.Align(rig);
            foreach (var steps in new[] { 0, 1, 59 })
            {
                for (int step = 0; step < steps; step++) solver.Step(1f / 60);
                var model = aligned.Sample(binding.Overrides(aligned.Rest, preview, solver.GetBonePoses()));
                for (int m = 0; m < model.Meshes.Count; m++)
                {
                    // The fin cloth legitimately drives vertices within the shared body meshes.
                    if (steps != 0 && (cloth == 2 || model.Meshes[m].Name.Contains("/Mant__"))) continue;
                    for (int j = 0; j < model.Meshes[m].Vertices.Length; j += 8)
                    {
                        var a = aligned.Rest.Meshes[m].Vertices; var b = model.Meshes[m].Vertices;
                        Require(Vector3.Distance(new(a[j], a[j + 1], a[j + 2]), new(b[j], b[j + 1], b[j + 2])) < 1e-5f,
                            $"Simulation distorted unrelated mesh {model.Meshes[m].Name}, cloth {cloth}, step {steps}");
                    }
                }
            }
        }
        Console.WriteLine("PASS: native simulation initialization and running cloth preserve unrelated body meshes.");
        foreach (var modelFirst in new[] { false, true })
        {
            var window = new MainWindow(graphics: false); window.Show();
            void Import()
            {
                var task = window.LoadModelAsync(() => rig, modelPath);
                while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
                task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
            }
            if (modelFirst) Import();
            window.LoadPath(physicsPath); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            if (!modelFirst) Import();
            var doc = window.Session.Document; var original = doc.CaptureState();
            var names = Enumerable.Range(0, doc.GetClothSummaries().Count).Select(doc.GetClothName).ToArray();
            ComboBox Selector() => window.GetVisualDescendants().OfType<ComboBox>().Single(c =>
                c.ItemsSource?.Cast<object>().OfType<string>().SequenceEqual(names) == true);
            void Select(int i) { Selector().SelectedIndex = i; Dispatcher.UIThread.RunJobs(); }
            void CheckRest(string context)
            {
                var posed = window.Viewport.Model!;
                Require(Vector3.Distance(posed.BoundsMinimum, rig.Rest.BoundsMinimum) < .001f &&
                    Vector3.Distance(posed.BoundsMaximum, rig.Rest.BoundsMaximum) < .001f, context + ": model origin or proportions changed");
                for (int m = 0; m < rig.Rest.Meshes.Count; m++)
                    for (int j = 0; j < rig.Rest.Meshes[m].Vertices.Length; j += 8)
                    {
                        var a = rig.Rest.Meshes[m].Vertices; var b = posed.Meshes[m].Vertices;
                        Require(Vector3.Distance(new(a[j], a[j + 1], a[j + 2]), new(b[j], b[j + 1], b[j + 2])) < .001f,
                            context + ": cloth switch deformed an unedited model");
                    }
            }
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < names.Length; i++)
            {
                Select(i); CheckRest(names[i]);
                window.ToggleSimulation(); window.StopSimulation(); Dispatcher.UIThread.RunJobs(); CheckRest(names[i] + " after stop");
            }
            Require(doc.CaptureState() == original, "Preview changed native physics data");
            Select(1);
            var id = window.Session.SelectedEntryId;
            window.Session.Edit((d, i) => {
                var bone = d.GetBoneRows(i).Single(b => b.Name == "Skl_Root"); bone.Y += .1f; d.UpdateBoneRows(i, [bone], false);
            });
            window.Refresh(); var edited = window.Viewport.Model!.BoundsMinimum;
            CheckRest("Bone edit leaves BFRES unchanged");
            Select(2); CheckRest("Other cloth after bone edit"); Select(1);
            Require(Vector3.Distance(window.Viewport.Model!.BoundsMinimum, edited) < .001f, "Switch lost edited binding");
            window.Session.RenameSelected("Renamed cloth"); window.Refresh();
            Require(window.Session.SelectedEntryId == id && Vector3.Distance(window.Viewport.Model!.BoundsMinimum, edited) < .001f, "Rename lost binding identity");
            window.Session.ClothIndex = 0; window.Refresh(); window.Session.RemoveSelected(); window.Refresh();
            Require(window.Session.SelectedEntryId == id && Vector3.Distance(window.Viewport.Model!.BoundsMinimum, edited) < .001f, "Removing neighbor reused another cloth binding");
            window.Session.Undo(); window.Refresh(); window.Session.ClothIndex = 1; window.Refresh();
            Require(window.Session.SelectedEntryId == id && Vector3.Distance(window.Viewport.Model!.BoundsMinimum, edited) < .001f, "Undo lost cloth identity");
            window.Session.Undo(); window.Session.Undo(); window.Refresh(); CheckRest("Undo bone edit");
            Require(doc.CaptureState() == original, "Undo did not restore source physics");
            window.Close();
            Console.WriteLine($"PASS: native cloth switching, all model vertices, simulation reset, bone edits, rename, neighbor removal and undo; modelFirst={modelFirst}.");
        }
    }
}
