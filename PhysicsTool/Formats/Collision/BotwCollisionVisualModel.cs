using System.Numerics;

namespace HKCLTool;

internal static class BotwCollisionVisualModel
{
    public static VisualModelData Create(IReadOnlyList<BotwCollisionMesh> meshes, string sourcePath)
    {
        var selected = meshes.Where(mesh => mesh.Included && mesh.Indices.Count >= 3).ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("Select at least one collision mesh to preview.");

        var model = new VisualModelData { SourcePath = sourcePath };
        var minimum = new Vector3(float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity);
        foreach (var mesh in selected)
        {
            var normals = CalculateNormals(mesh);
            var vertices = new float[mesh.Vertices.Count * 8];
            for (var index = 0; index < mesh.Vertices.Count; index++)
            {
                var position = mesh.Vertices[index];
                minimum = Vector3.Min(minimum, position);
                maximum = Vector3.Max(maximum, position);
                var offset = index * 8;
                vertices[offset] = position.X;
                vertices[offset + 1] = position.Y;
                vertices[offset + 2] = position.Z;
                vertices[offset + 3] = normals[index].X;
                vertices[offset + 4] = normals[index].Y;
                vertices[offset + 5] = normals[index].Z;
            }
            model.Meshes.Add(new VisualModelMesh
            {
                Name = mesh.Name,
                Vertices = vertices,
                Indices = mesh.Indices.Select(index => checked((uint)index)).ToArray(),
                BaseColor = MaterialColor(mesh.Material),
                CullingMode = VisualModelCullingMode.Off,
                Material = new VisualModelMaterialSettings { ShaderModel = VisualModelShaderModel.Standard, UseAlbedo = false, Tint = MaterialColor(mesh.Material) }
            });
            model.TriangleCount += mesh.TriangleCount;
        }
        model.BoundsMinimum = minimum;
        model.BoundsMaximum = maximum;
        return model;
    }

    private static Vector3[] CalculateNormals(BotwCollisionMesh mesh)
    {
        var normals = new Vector3[mesh.Vertices.Count];
        for (var index = 0; index + 2 < mesh.Indices.Count; index += 3)
        {
            var a = mesh.Indices[index];
            var b = mesh.Indices[index + 1];
            var c = mesh.Indices[index + 2];
            if ((uint)a >= normals.Length || (uint)b >= normals.Length || (uint)c >= normals.Length)
                continue;
            var normal = Vector3.Cross(mesh.Vertices[b] - mesh.Vertices[a], mesh.Vertices[c] - mesh.Vertices[a]);
            if (normal.LengthSquared() <= 1e-12f)
                continue;
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }
        for (var index = 0; index < normals.Length; index++)
            normals[index] = normals[index].LengthSquared() > 1e-12f ? Vector3.Normalize(normals[index]) : Vector3.UnitY;
        return normals;
    }

    private static Vector4 MaterialColor(string material)
    {
        if (material.Contains("Metal", StringComparison.OrdinalIgnoreCase)) return new(0.46f, 0.60f, 0.68f, 1);
        if (material.Contains("Wood", StringComparison.OrdinalIgnoreCase)) return new(0.48f, 0.29f, 0.15f, 1);
        if (material.Contains("Grass", StringComparison.OrdinalIgnoreCase)) return new(0.26f, 0.55f, 0.28f, 1);
        if (material.Contains("Water", StringComparison.OrdinalIgnoreCase)) return new(0.20f, 0.48f, 0.72f, 1);
        if (material.Contains("Sand", StringComparison.OrdinalIgnoreCase)) return new(0.72f, 0.62f, 0.35f, 1);
        if (material.Contains("Snow", StringComparison.OrdinalIgnoreCase) || material.Contains("Ice", StringComparison.OrdinalIgnoreCase)) return new(0.72f, 0.86f, 0.92f, 1);
        return new(0.58f, 0.60f, 0.62f, 1);
    }
}
