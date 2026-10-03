using Assimp;
using System.Numerics;

namespace HKCLTool;

internal static class BotwCollisionExporter
{
    public static void Export(IReadOnlyList<BotwCollisionMesh> meshes, string outputPath)
    {
        var selected = meshes.Where(mesh => mesh.Included && mesh.Indices.Count >= 3).ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("Select at least one collision mesh to export.");

        var extension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (extension is not ".dae" and not ".fbx")
            throw new NotSupportedException("Collision geometry can be exported as DAE or FBX.");

        var scene = new Scene { RootNode = new Node("Collision") };
        scene.Materials.Add(new Material { Name = "Collision" });
        foreach (var source in selected)
        {
            var mesh = new Mesh(source.Name, PrimitiveType.Triangle) { MaterialIndex = 0 };
            mesh.Vertices.AddRange(source.Vertices.Select(vertex => new Vector3(vertex.X, vertex.Y, vertex.Z)));
            for (var index = 0; index + 2 < source.Indices.Count; index += 3)
                mesh.Faces.Add(new Face([source.Indices[index], source.Indices[index + 1], source.Indices[index + 2]]));
            scene.RootNode.MeshIndices.Add(scene.Meshes.Count);
            scene.Meshes.Add(mesh);
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        using var context = new AssimpContext();
        var format = context.GetSupportedExportFormats()
            .FirstOrDefault(item => ("." + item.FileExtension).Equals(extension, StringComparison.OrdinalIgnoreCase));
        if (format == null)
            throw new NotSupportedException($"The bundled model library cannot export {extension} files.");
        if (!context.ExportFile(scene, fullPath, format.FormatId, PostProcessSteps.ValidateDataStructure))
            throw new IOException("The collision model exporter did not create the file.");
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
            throw new IOException("The collision model exporter created an empty file.");
    }
}
