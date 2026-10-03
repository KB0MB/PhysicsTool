using Assimp;
using System.Numerics;

namespace HKCLTool;

internal static class BotwCollisionMeshImporter
{
    private const int MaximumSourceTriangles = 2_000_000;

    public static IReadOnlyList<BotwCollisionMesh> Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("The collision model could not be found.", path);

        using var context = new AssimpContext();
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension) || !context.IsImportFormatSupported(extension))
            throw new InvalidDataException($"The model format '{extension}' is not supported.");

        const PostProcessSteps steps =
            PostProcessSteps.Triangulate |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.PreTransformVertices |
            PostProcessSteps.SortByPrimitiveType |
            PostProcessSteps.ValidateDataStructure;
        var scene = context.ImportFile(path, steps);
        if (scene == null || !scene.HasMeshes)
            throw new InvalidDataException("The model contains no mesh geometry.");

        var result = new List<BotwCollisionMesh>();
        var totalTriangles = 0;
        for (var meshIndex = 0; meshIndex < scene.MeshCount; meshIndex++)
        {
            var source = scene.Meshes[meshIndex];
            if (!source.HasVertices || !source.HasFaces)
                continue;

            var vertices = source.Vertices
                .Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z))
                .ToArray();
            if (vertices.Any(vertex => !IsFinite(vertex)))
                throw new InvalidDataException($"Mesh '{DisplayName(source, meshIndex)}' contains a non-finite vertex.");

            var indices = new List<int>(source.FaceCount * 3);
            foreach (var face in source.Faces)
            {
                if (face.IndexCount != 3)
                    continue;
                var a = face.Indices[0];
                var b = face.Indices[1];
                var c = face.Indices[2];
                if ((uint)a >= vertices.Length || (uint)b >= vertices.Length || (uint)c >= vertices.Length)
                    throw new InvalidDataException($"Mesh '{DisplayName(source, meshIndex)}' contains an invalid triangle index.");
                if (a == b || b == c || a == c)
                    continue;
                if (Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).LengthSquared() < 1e-12f)
                    continue;
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }
            if (indices.Count == 0)
                continue;

            totalTriangles += indices.Count / 3;
            if (totalTriangles > MaximumSourceTriangles)
                throw new InvalidDataException($"The model exceeds the {MaximumSourceTriangles:N0}-triangle safety limit.");

            var name = DisplayName(source, meshIndex);
            result.Add(new BotwCollisionMesh
            {
                Name = name,
                Vertices = vertices,
                Indices = indices,
                Material = BotwCollisionMaterials.Infer(name)
            });
        }

        if (result.Count == 0)
            throw new InvalidDataException("The model contains no usable triangles.");
        return result;
    }

    private static string DisplayName(Mesh mesh, int index) =>
        string.IsNullOrWhiteSpace(mesh.Name) ? $"Mesh_{index}" : mesh.Name;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
