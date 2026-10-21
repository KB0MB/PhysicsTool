using Assimp;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;

internal static class ModelBindingChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        using var exporter = new AssimpContext();
        var scene = new Scene { RootNode = new Node("Scene") };
        var skeleton = new Node("Root"); scene.RootNode.Children.Add(skeleton);
        skeleton.Children.Add(new Node("Joint") { Transform = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, 1, 0)) });
        var child = new Node("Tip") { Transform = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, .5f, 0)) };
        skeleton.Children[0].Children.Add(child);
        var meshNode = new Node("Mesh"); meshNode.MeshIndices.Add(0); scene.RootNode.Children.Add(meshNode);
        scene.Materials.Add(new Material { Name = "Skin" });
        var mesh = new Mesh("Weighted", PrimitiveType.Triangle);
        mesh.Vertices.AddRange([new(0, 0, 0), new(1, 1, 0), new(0, 1, 0)]);
        mesh.Normals.AddRange([Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);
        mesh.Faces.Add(new Face([0, 1, 2]));
        var root = new Bone { Name = "Root", OffsetMatrix = Matrix4x4.Identity };
        root.VertexWeights.Add(new(0, 1)); root.VertexWeights.Add(new(2, .5f));
        var joint = new Bone { Name = "Joint", OffsetMatrix = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(0, -1, 0)) };
        joint.VertexWeights.Add(new(1, 1)); joint.VertexWeights.Add(new(2, .5f));
        mesh.Bones.Add(root); mesh.Bones.Add(joint); scene.Meshes.Add(mesh);
        foreach (var (extension, format) in new[] { ("dae", "collada"), ("fbx", "fbxa"), ("glb", "glb2") })
        {
            var file = Path.Combine(output, "weighted." + extension);
            Require(exporter.ExportFile(scene, file, format), "Could not export weighted fixture");
            var rig = PortableModelImporter.LoadRig(file);
            CheckAlignment(rig);
            var rest = rig.Rest; var bone = rest.Bones.Single(b => b.Name == "Joint");
            var original = (float[])rest.Meshes[0].Vertices.Clone();
            var moved = rig.Sample([new(bone.Name, bone.Position + new Vector3(0, 0, .4f), bone.Rotation, bone.Scale)]);
            var vertices = moved.Meshes[0].Vertices;
            Require(Math.Abs(vertices[2] - original[2]) < .00001f, extension + ": unbound vertex moved");
            Require(Math.Abs(vertices[10] - original[10] - .4f) < .00001f, extension + ": weighted vertex did not follow bone");
            Require(Math.Abs(vertices[18] - original[18] - .2f) < .00001f, extension + ": blended weights were lost; delta=" + (vertices[18] - original[18]));
            var tip = rest.Bones.Single(b => b.Name == "Tip");
            Require(Vector3.Distance(moved.Bones.Single(b => b.Name == "Tip").Position, tip.Position + new Vector3(0, 0, .4f)) < .00001f, "Unbound child did not inherit its parent");
            Require(rest.Meshes[0].Vertices.SequenceEqual(original), "Posing mutated rest geometry");
            Require(ReferenceEquals(rest, rig.Sample([])), "Clearing pose did not restore imported rest mesh");
            Require(ReferenceEquals(rest.Meshes[0].Indices, moved.Meshes[0].Indices) && ReferenceEquals(rest.Meshes[0].Material, moved.Meshes[0].Material), "Posing needlessly replaced GPU appearance data");

            var physics = new ParticlePreviewData();
            physics.Bones.Add(new() { Index = 7, Name = "Rig:Joint", Position = new(5, 6, 7) });
            var binding = new ClothModelBinding(); binding.Capture(physics.Bones);
            Require(binding.Overrides(rest, physics).Count == 0, "Binding snapped unmatched bind offsets");
            physics.Bones[0].Position += new Vector3(0, 0, .4f);
            var overrides = binding.Overrides(rest, physics);
            Require(overrides.Count == 1 && Vector3.Distance(overrides[0].Position, bone.Position + new Vector3(0, 0, .4f)) < .00001f, "Cloth/model bind offset or namespace mismatch");
            var sim = new Dictionary<int, SimulatedBonePreviewPose> { [7] = new() { Position = new(5, 6, 7.8f) } };
            overrides = binding.Overrides(rest, physics, sim);
            Require(Vector3.Distance(overrides[0].Position, bone.Position + new Vector3(0, 0, .8f)) < .00001f, "Simulation output not used for model binding");
            var first = rig.Sample(overrides); var second = rig.Sample(overrides);
            Require(first.Meshes[0].Vertices.SequenceEqual(second.Meshes[0].Vertices), "Repeated skinning accumulated a transform");
            sim[7].Position = new(5, 6, 7);
            Require(binding.Overrides(rest, physics, sim).Count == 1, "A simulated rest pose must still override an animated bone");
        }
        Console.WriteLine("PASS: FBX/DAE/GLB weighted skinning, bind offsets, child inheritance, solver poses, rest restoration and GPU data reuse.");
    }

    public static void RunBfres(string path)
    {
        var rig = PortableModelImporter.LoadRig(path); var physics = new ParticlePreviewData();
        CheckAlignment(rig);
        var identity = rig.Sample(rig.Rest.Bones.GroupBy(b => b.Name).Select(g =>
            new VisualBoneOverride(g.Key, g.First().Position, g.First().Rotation, g.First().Scale)).ToArray());
        for (int m = 0; m < rig.Rest.Meshes.Count; m++)
            for (int o = 0; o < rig.Rest.Meshes[m].Vertices.Length; o += 8)
            {
                var a = rig.Rest.Meshes[m].Vertices; var b = identity.Meshes[m].Vertices;
                Require(Vector3.Distance(new(a[o], a[o + 1], a[o + 2]), new(b[o], b[o + 1], b[o + 2])) < 1e-5f,
                    "Unchanged BFRES override replaced another model's bind pose");
            }
        foreach (var bone in rig.Rest.Bones.GroupBy(b => b.Name).Select(g => g.First()))
            physics.Bones.Add(new() { Index = bone.Index, Name = bone.Name, Position = bone.Position,
                AxisX = bone.AxisX * bone.Scale.X, AxisY = bone.AxisY * bone.Scale.Y, AxisZ = bone.AxisZ * bone.Scale.Z });
        var binding = new ClothModelBinding(); binding.Capture(physics.Bones);
        foreach (var bone in physics.Bones) bone.Position += new Vector3(.2f, .1f, .3f);
        var posed = rig.Sample(binding.Overrides(rig.Rest, physics));
        Require(posed.Meshes.SelectMany(m => m.Vertices).All(float.IsFinite), "BFRES binding produced nonfinite geometry");
        var changed = false;
        for (int m = 0; m < posed.Meshes.Count; m++)
            for (int o = 0; o < posed.Meshes[m].Vertices.Length; o += 8)
            {
                var p = new Vector3(posed.Meshes[m].Vertices[o], posed.Meshes[m].Vertices[o + 1], posed.Meshes[m].Vertices[o + 2]);
                var rest = new Vector3(rig.Rest.Meshes[m].Vertices[o], rig.Rest.Meshes[m].Vertices[o + 1], rig.Rest.Meshes[m].Vertices[o + 2]);
                Require(Vector3.Distance(p - rest, new(.2f, .1f, .3f)) < 1e-5f, "BFRES weighted vertex did not follow cloth bone delta");
                changed |= Vector3.Distance(p, rest) > .1f;
            }
        Require(changed, "BFRES binding did not move the model");
        Console.WriteLine("PASS: real BFRES cloth-to-model binding, all mesh vertices and repeated skeleton names.");
    }

    private static void CheckAlignment(ImportedModelRig rig)
    {
        var offset = new Vector3(.25f, 2, -.4f);
        var physics = new ParticlePreviewData();
        foreach (var bone in rig.Rest.Bones.GroupBy(b => b.Name).Select(g => g.First()))
            physics.Bones.Add(new() { Index = bone.Index, Name = "Physics:" + bone.Name, Position = bone.Position + offset,
                AxisX = bone.AxisX * bone.Scale.X, AxisY = bone.AxisY * bone.Scale.Y, AxisZ = bone.AxisZ * bone.Scale.Z });
        Require(physics.Bones.Count >= 3, "Alignment fixture requires three named bones");
        var original = new ClothModelBinding();
        original.Capture(physics.Bones.Select(b => new BonePreviewPoint { Name = b.Name, Position = b.Position - offset }));
        Require(ReferenceEquals(rig, original.Align(rig)), "An already aligned model was changed");
        var binding = new ClothModelBinding(); binding.Capture(physics.Bones);
        var aligned = binding.Align(rig);
        Require(ReferenceEquals(aligned, binding.Align(rig)), "Alignment was recomputed on refresh");
        Require(Vector3.Distance(aligned.Rest.BoundsMinimum, rig.Rest.BoundsMinimum + offset) < .002f &&
            Vector3.Distance(aligned.Rest.BoundsMaximum, rig.Rest.BoundsMaximum + offset) < .002f, "Alignment did not translate model bounds");
        for (int m = 0; m < rig.Rest.Meshes.Count; m++)
        {
            var source = rig.Rest.Meshes[m]; var target = aligned.Rest.Meshes[m];
            for (int i = 0; i < source.Vertices.Length; i += 8)
                Require(Vector3.Distance(new(target.Vertices[i] - source.Vertices[i], target.Vertices[i + 1] - source.Vertices[i + 1],
                    target.Vertices[i + 2] - source.Vertices[i + 2]), offset) < .002f, "Alignment distorted or omitted model vertices");
            Require(ReferenceEquals(source.Material, target.Material), "Alignment discarded material settings");
        }
        var edited = physics.Bones.Last(); edited.Position += Vector3.UnitZ * .3f;
        binding.Capture(physics.Bones);
        Require(ReferenceEquals(aligned, binding.Align(rig)), "Bone editing recaptured the alignment origin");
        var posed = aligned.Sample(binding.Overrides(aligned.Rest, physics));
        var name = edited.Name["Physics:".Length..];
        Require(Vector3.Distance(posed.Bones.First(b => b.Name == name).Position, edited.Position) < .002f, "Edited bone lost the aligned origin");
        var simulation = physics.Bones.ToDictionary(b => b.Index, b => new SimulatedBonePreviewPose
            { Position = b.Position, AxisX = b.AxisX, AxisY = b.AxisY, AxisZ = b.AxisZ });
        var simulated = aligned.Sample(binding.Overrides(aligned.Rest, physics, simulation));
        Require(Vector3.Distance(simulated.Bones.First(b => b.Name == name).Position, edited.Position) < .002f, "Simulation applied alignment twice");
        Require(ReferenceEquals(aligned.Rest, aligned.Sample([])), "Aligned rest restoration failed");

        var unmatched = new ClothModelBinding();
        unmatched.Capture([new() { Name = "Missing", Position = offset }]);
        Require(ReferenceEquals(rig, unmatched.Align(rig)), "Unmatched skeleton was guessed from bounds");
        var single = new ClothModelBinding(); single.Capture(physics.Bones.Take(1));
        Require(ReferenceEquals(rig, single.Align(rig)), "Single-bone bind offset was treated as a shared origin");
        for (int i = 0; i < physics.Bones.Count; i++) physics.Bones[i].Position += new Vector3(i, 0, 0);
        var mismatched = new ClothModelBinding(); mismatched.Capture(physics.Bones);
        Require(ReferenceEquals(rig, mismatched.Align(rig)), "Different skeleton proportions were translated automatically");
        Console.WriteLine("PASS: shared-origin alignment, complete geometry, bounds, edits, simulation and conservative fallback: " + rig.Rest.SourcePath);
    }

    public static void RunEditor(string output)
    {
        RunEditorAlignment(output, false);
        RunEditorAlignment(output, true);
        RunIndependentClothBindings(output);
        var window = new MainWindow(graphics: false); window.Show();
        window.Session.New("Binding test", 0);
        window.Session.Edit((doc, cloth) =>
        {
            var index = doc.AddBone(cloth);
            var bone = doc.GetBoneRows(cloth).Single(b => b.Index == index);
            bone.Name = "Joint"; doc.UpdateBoneRows(cloth, [bone]);
        });
        window.Refresh(); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        var load = FixtureImports.Model(window, Path.Combine(output, "weighted.dae"));
        while (!load.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        load.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        var rest = window.Viewport.Model!;
        var original = (float[])rest.Meshes[0].Vertices.Clone();
        window.Session.Edit((doc, cloth) =>
        {
            var bone = doc.GetBoneRows(cloth).Single(b => b.Name == "Joint"); bone.Z += .4f;
            doc.UpdateBoneRows(cloth, [bone]);
        });
        window.Refresh(); Dispatcher.UIThread.RunJobs();
        var state = window.Session.Document.CaptureState();
        Require(window.Viewport.Model!.Meshes[0].Vertices.SequenceEqual(original), "Editor bone edit changed imported mesh");
        window.Refresh();
        Require(window.Session.Document.CaptureState() == state, "Visual skinning changed the physics document");
        window.ToggleSimulation(); window.StopSimulation();
        Require(window.Viewport.Model!.Meshes[0].Vertices.SequenceEqual(original), "Stopping simulation did not restore model rest pose");
        window.Session.Undo(); window.Refresh();
        Require(window.Viewport.Model!.Meshes[0].Vertices.SequenceEqual(original), "Undo did not restore mesh pose");
        window.Session.Redo(); window.Refresh();
        window.ShowOverview(); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        Require(window.Viewport.Model!.Meshes[0].Vertices.SequenceEqual(original), "Back changed model rest pose");
        window.Session.Clear(); window.Refresh(); window.Close();
        Console.WriteLine("PASS: editor bone fields preserve imported rest mesh; undo, redo, stop, Back and physics isolation.");
    }

    private static void RunIndependentClothBindings(string output)
    {
        var window = new MainWindow(graphics: false); window.Show();
        window.Session.New("First skeleton", 0);
        window.Session.Edit((doc, cloth) =>
        {
            while (doc.GetBoneRows(cloth).Count < 3) doc.AddBone(cloth);
            var bones = doc.GetBoneRows(cloth);
            for (int i = 0; i < 3; i++)
            {
                bones[i].Name = new[] { "Root", "Joint", "Tip" }[i];
                bones[i].ParentIndex = i - 1; bones[i].Y = i == 0 ? 0 : i == 1 ? 2 : 1;
            }
            doc.UpdateBoneRows(cloth, bones, false);
        });
        window.Session.DuplicateSelected();
        window.Session.Edit((doc, cloth) => {
            var bones = doc.GetBoneRows(cloth); bones[1].Y = 1; bones[2].Y = .5f;
            doc.UpdateBoneRows(cloth, bones, false);
        });
        window.Session.ClothIndex = 0; window.Refresh(); window.ShowEditor();
        var task = FixtureImports.Model(window, Path.Combine(output, "weighted.dae"));
        while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        task.GetAwaiter().GetResult();
        var baseline = window.Viewport.Model!.Meshes[0].Vertices.ToArray();
        foreach (var index in new[] { 1, 0, 1 })
        {
            window.Session.ClothIndex = index; window.Refresh();
            Require(window.Viewport.Model!.Meshes[0].Vertices.SequenceEqual(baseline), "Same-named bones in different cloths shared their rest binding");
        }
        window.Close();
        Console.WriteLine("PASS: independent rest bindings for same-named bones across cloths with different skeleton proportions.");
    }

    private static void RunEditorAlignment(string output, bool modelFirst)
    {
        var window = new MainWindow(graphics: false); window.Show();
        var path = Path.Combine(output, "weighted.dae");
        var rest = PortableModelImporter.LoadRig(path).Rest;
        void Import()
        {
            var task = FixtureImports.Model(window, path);
            while (!task.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            task.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        }
        if (modelFirst) Import();
        window.Session.New("Translated physics", 0);
        var offset = new Vector3(.25f, 2, -.4f);
        window.Session.Edit((doc, cloth) =>
        {
            var existing = doc.GetBoneRows(cloth).ToArray();
            foreach (var bone in existing) bone.Name = "Unmatched" + bone.Index;
            doc.UpdateBoneRows(cloth, existing);
            foreach (var source in rest.Bones)
            {
                var index = doc.AddBone(cloth);
                var bone = doc.GetBoneRows(cloth).Single(b => b.Index == index);
                bone.Name = "Physics:" + source.Name;
                bone.X = source.Position.X + offset.X; bone.Y = source.Position.Y + offset.Y; bone.Z = source.Position.Z + offset.Z;
                doc.UpdateBoneRows(cloth, [bone]);
            }
        });
        window.Refresh(); window.ShowEditor();
        var state = window.Session.Document.CaptureState();
        if (!modelFirst) Import();
        void Check()
        {
            Require(Vector3.Distance(window.Viewport.Model!.BoundsMinimum, rest.BoundsMinimum + offset) < .0001f,
                "Editor load order lost alignment: modelFirst=" + modelFirst);
            Require(window.Session.Document.CaptureState() == state, "Alignment changed authored physics");
        }
        Check(); window.Refresh(); Check();
        window.ToggleSimulation(); window.StopSimulation(); Check();
        window.ShowOverview(); window.ShowEditor(); Check();
        Import(); Check();
        window.Session.New("Different origin", 0); window.Refresh();
        Require(Vector3.Distance(window.Viewport.Model!.BoundsMinimum, rest.BoundsMinimum) < .0001f, "Document switch retained previous alignment");
        window.Close();
        Console.WriteLine("PASS: editor shared origin, refresh, stop, Back, reimport and document isolation; modelFirst=" + modelFirst);
    }
}
