using HKCLTool;
using System.Text.Json;
using System.Numerics;

internal static class IconNeckStudy
{
    internal static void Inset(string input, string output, float depth)
    {
        if (Path.GetFullPath(input) == Path.GetFullPath(output) || depth <= 0 || depth > .05f) throw new ArgumentException("Use a separate output and an inset up to 5 cm.");
        var doc = new ToolboxBfresDocument(File.ReadAllBytes(input));
        dynamic model = doc.Models.Single(m => (string)m.Name == "Armor_Icon");
        dynamic shape = ToolboxBfresDocument.Items((object)model.Shapes).Single(s => (string)s.Name == "Icon_Upper_Neck");
        object buffer = model.VertexBuffers[(int)shape.VertexBufferIndex];
        var helperType = doc.Resource.GetType().Assembly.GetTypes().Single(t => t.Name == "VertexBufferHelper");
        var ctor = helperType.GetConstructors().Single(c => c.GetParameters().Length > 0 && c.GetParameters()[0].ParameterType.IsInstanceOfType(buffer));
        dynamic helper = ctor.Invoke(ctor.GetParameters().Select((p, i) => i == 0 ? buffer : p.DefaultValue).ToArray());
        var attributes = ToolboxBfresDocument.Items((object)helper.Attributes).ToDictionary(a => (string)a.Name);
        var positions = (Syroot.Maths.Vector4F[])attributes["_p0"].Data;
        var normals = (Syroot.Maths.Vector4F[])attributes["_n0"].Data;
        Vector3 P(int i) => new(positions[i].X, positions[i].Y, positions[i].Z);
        var cap = Enumerable.Range(0, positions.Length).Where(i => normals[i].Y > .85f).ToHashSet();
        var centerGroup = cap.GroupBy(P).OrderByDescending(g => g.Count()).First();
        if (centerGroup.Count() != 16) throw new InvalidDataException("Expected the original sixteen-triangle neck cap.");
        var direction = Vector3.Normalize(new Vector3(normals[centerGroup.First()].X, normals[centerGroup.First()].Y, normals[centerGroup.First()].Z));
        foreach (int i in centerGroup) { var p = P(i) - direction * depth; positions[i].X = p.X; positions[i].Y = p.Y; positions[i].Z = p.Z; }
        var mesh = new ToolboxActorModel(doc, "Armor_Icon", new()).RestPose.Meshes.Single(m => m.Name.StartsWith("Icon_Upper_Neck__"));
        var sums = new Dictionary<Vector3, Vector3>();
        for (int t = 0; t < mesh.Indices.Length; t += 3)
        {
            int a = (int)mesh.Indices[t], b = (int)mesh.Indices[t + 1], c = (int)mesh.Indices[t + 2];
            if (!cap.Contains(a) || !cap.Contains(b) || !cap.Contains(c)) continue;
            var normal = Vector3.Cross(P(b) - P(a), P(c) - P(a));
            if (Vector3.Dot(normal, direction) < 0) normal = -normal;
            foreach (int i in new[] { a, b, c }) sums[P(i)] = sums.GetValueOrDefault(P(i)) + normal;
        }
        var tangents = attributes.TryGetValue("_t0", out var tangentAttribute) ? (Syroot.Maths.Vector4F[])tangentAttribute.Data : null;
        foreach (int i in cap)
        {
            var n = Vector3.Normalize(sums[P(i)]); normals[i].X = n.X; normals[i].Y = n.Y; normals[i].Z = n.Z;
            if (tangents != null)
            {
                var t = new Vector3(tangents[i].X, tangents[i].Y, tangents[i].Z); t = Vector3.Normalize(t - n * Vector3.Dot(n, t));
                tangents[i].X = t.X; tangents[i].Y = t.Y; tangents[i].Z = t.Z;
            }
        }
        model.VertexBuffers[(int)shape.VertexBufferIndex] = helper.ToVertexBuffer();
        ((dynamic)doc.Resource).Save(output);
        var before = new ToolboxActorModel(new ToolboxBfresDocument(File.ReadAllBytes(input)), "Armor_Icon", new()).RestPose;
        var after = new ToolboxActorModel(new ToolboxBfresDocument(File.ReadAllBytes(output)), "Armor_Icon", new()).RestPose;
        if (before.Bones.Count != after.Bones.Count || before.Bones.Zip(after.Bones).Any(p => p.First.WorldMatrix != p.Second.WorldMatrix) ||
            before.Meshes.Zip(after.Meshes).Any(p => !p.First.Indices.SequenceEqual(p.Second.Indices) || !p.First.Name.StartsWith("Icon_Upper_Neck__") && !p.First.Vertices.SequenceEqual(p.Second.Vertices)))
            throw new InvalidDataException("Neck edit changed another mesh, skeleton or topology.");
        Console.WriteLine("PASS neck inset roundtrip; all other meshes, topology and skeleton unchanged");
    }
    internal static void Run(string input, string output)
    {
        var doc = new ToolboxBfresDocument(File.ReadAllBytes(input));
        var model = new ToolboxActorModel(doc, "Armor_Icon", new());
        var mesh = model.RestPose.Meshes.Single(m => m.Name.StartsWith("Icon_Upper_Neck__"));
        File.WriteAllText(output, JsonSerializer.Serialize(new { mesh.Vertices, mesh.Indices }));
    }
}
