using Assimp;
using HKCLTool;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;

internal static class ClothFbxChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run(string output, string? input = null)
    {
        Directory.CreateDirectory(output);
        var doc = HkclService.CreateBlankCloth("ClothExportTest", false);
        for (int i = 0; i < 4; i++) doc.AddParticle(0, new ParticleEditRow { X = i % 2, Y = i / 2, Z = .25f, Mass = 1, InverseMass = 1, Radius = .01f });
        doc.LinkParticles(0, [0, 1, 2]);
        var child = doc.AddBone(0); var rows = doc.GetBoneRows(0);
        var row = rows.Single(b => b.Index == child); row.Name = "Child"; row.ParentIndex = 0; row.Y = .8f;
        doc.UpdateBoneRows(0, [row], false);
        Check(doc, 0, Path.Combine(output, "synthetic.fbx"));
        if (input != null)
        {
            doc.Load(input);
            for (int i = 0; i < doc.GetClothSummaries().Count; i++) Check(doc, i, Path.Combine(output, $"cloth-{i}.fbx"));
        }
    }
    private static void Check(HkclService doc, int cloth, string path)
    {
        var state = doc.CaptureState();
        var source = ClothFbxExporter.CreateScene(doc, cloth);
        var expectedMeshes = new Dictionary<string, float[][]>();
        var expectedBones = new Dictionary<string, float[]>();
        var boneNames = source.Meshes[0].Bones.Select(b => b.Name).ToHashSet();
        void Record(Node n, Matrix4x4 parent)
        {
            var world = Matrix4x4.Transpose(n.Transform) * parent;
            if (boneNames.Contains(n.Name)) expectedBones[n.Name] = [world.M41, world.M42, world.M43];
            foreach (var index in n.MeshIndices) expectedMeshes[n.Name] = source.Meshes[index].Vertices.Select(v => Vector3.Transform(v, world)).Select(v => new[] { v.X, v.Y, v.Z }).ToArray();
            foreach (var c in n.Children) Record(c, world);
        }
        Record(source.RootNode, Matrix4x4.Identity);
        File.WriteAllText(path + ".json", System.Text.Json.JsonSerializer.Serialize(new { meshes = expectedMeshes, bones = expectedBones }));
        ClothFbxExporter.Export(doc, cloth, path);
        using var context = new AssimpContext();
        var scene = context.ImportFile(path, PostProcessSteps.ValidateDataStructure | PostProcessSteps.JoinIdenticalVertices);
        Require(doc.CaptureState() == state, "FBX export mutated the physics");
        Require(scene.MeshCount == source.MeshCount, "FBX lost a collider mesh");
        var clothMesh = scene.Meshes[scene.RootNode.FindNode("Particles").MeshIndices[0]];
        Require(clothMesh.VertexCount == source.Meshes[0].VertexCount, "FBX changed the particle count");
        foreach (var vertex in source.Meshes[0].Vertices)
            Require(clothMesh.Vertices.Any(p => Vector3.Distance(vertex, p) < 1e-5f), "FBX changed particle coordinates");
        Require(clothMesh.FaceCount == source.Meshes[0].FaceCount, "FBX lost triangles or loose particles");
        var actual = new Dictionary<string, Matrix4x4>();
        void Visit(Node n, Matrix4x4 parent)
        {
            var world = Matrix4x4.Transpose(n.Transform) * parent; actual[n.Name] = world;
            foreach (var c in n.Children) Visit(c, world);
        }
        Visit(scene.RootNode, Matrix4x4.Identity);
        void Compare(Node n, Matrix4x4 parent)
        {
            var world = Matrix4x4.Transpose(n.Transform) * parent;
            Require(actual.TryGetValue(n.Name, out var imported), "FBX lost skeleton/collider node " + n.Name);
            Require(Vector3.Distance(world.Translation, imported.Translation) < 1e-4f &&
                Vector3.Distance(Vector3.TransformNormal(Vector3.UnitX, world), Vector3.TransformNormal(Vector3.UnitX, imported)) < 1e-4f,
                "FBX changed bone/collider transform " + n.Name);
            foreach (var c in n.Children) Compare(c, world);
        }
        foreach (var n in source.RootNode.Children) Compare(n, Matrix4x4.Identity);
        Require(clothMesh.BoneCount == source.Meshes[0].BoneCount, "FBX did not retain the complete armature");
        Console.WriteLine($"PASS: FBX {doc.GetClothName(cloth)}: {clothMesh.VertexCount} particles, {clothMesh.BoneCount} bones, {scene.MeshCount - 1} collider meshes; topology, poses and source preservation.");
    }
}
