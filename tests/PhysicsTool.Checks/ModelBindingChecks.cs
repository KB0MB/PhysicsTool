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
                Require(Vector3.Distance(p - rest, new(.2f, .1f, .3f)) < .002f, "BFRES weighted vertex did not follow cloth bone delta");
                changed |= Vector3.Distance(p, rest) > .1f;
            }
        Require(changed, "BFRES binding did not move the model");
        Console.WriteLine("PASS: real BFRES cloth-to-model binding, all mesh vertices and repeated skeleton names.");
    }

    public static void RunEditor(string output)
    {
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
        Require(Math.Abs(window.Viewport.Model!.Meshes[0].Vertices[10] - original[10] - .4f) < .00001f, "Editor bone edit did not pose imported mesh");
        window.Refresh();
        Require(window.Session.Document.CaptureState() == state, "Visual skinning changed the physics document");
        window.ToggleSimulation(); window.StopSimulation();
        Require(Math.Abs(window.Viewport.Model!.Meshes[0].Vertices[10] - original[10] - .4f) < .00001f, "Stopping simulation lost edited model pose");
        window.Session.Undo(); window.Refresh();
        Require(window.Viewport.Model!.Meshes[0].Vertices.SequenceEqual(original), "Undo did not restore mesh pose");
        window.Session.Redo(); window.Refresh();
        window.ShowOverview(); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        Require(Math.Abs(window.Viewport.Model!.Meshes[0].Vertices[10] - original[10] - .4f) < .00001f, "Back lost imported model binding");
        window.Session.Clear(); window.Refresh(); window.Close();
        Console.WriteLine("PASS: editor bone fields drive imported skin; undo, redo, stop, Back and physics isolation.");
    }
}
