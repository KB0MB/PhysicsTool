using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

internal enum CollisionOutput { WiiUActor, SwitchActor, WiiUStatic, TotkPolytope }
internal sealed record CollisionSettings(CollisionOutput Target, float Scale, Vector3 Offset, bool ZUp, bool Compress, string BodyName);
internal sealed record CollisionPreview(IReadOnlyList<BotwCollisionMesh> Meshes, TotkPolytopeDocument? Polytope);

internal static class CollisionWorkflow
{
    internal static IReadOnlyList<BotwCollisionMesh> Snapshot(IEnumerable<BotwCollisionMesh> meshes) => meshes.Select(m => new BotwCollisionMesh
    { Name = m.Name, Material = m.Material, Included = m.Included, Vertices = m.Vertices.ToArray(), Indices = m.Indices.ToArray() }).ToArray();

    internal static CollisionPreview Prepare(IReadOnlyList<BotwCollisionMesh> source, CollisionSettings settings)
    {
        if (!float.IsFinite(settings.Scale) || settings.Scale <= 0 || !TotkPolytopeBuilder.Finite(settings.Offset))
            throw new ArgumentException("Scale must be positive and coordinates must be finite.");
        var meshes = source.Where(m => m.Included).Select(m => new BotwCollisionMesh
        {
            Name = m.Name, Material = m.Material.Trim(), Indices = m.Indices.ToArray(),
            Vertices = m.Vertices.Select(v => (settings.ZUp ? new Vector3(v.X, v.Z, -v.Y) : v) * settings.Scale + settings.Offset).ToArray()
        }).ToArray();
        if (meshes.Length == 0) throw new InvalidOperationException("Select at least one mesh.");
        foreach (var mesh in meshes)
        {
            if (mesh.Vertices.Any(v => !TotkPolytopeBuilder.Finite(v)) || mesh.Indices.Count == 0 || mesh.Indices.Count % 3 != 0 || mesh.Indices.Any(i => (uint)i >= mesh.Vertices.Count))
                throw new InvalidDataException($"Mesh '{mesh.Name}' contains invalid geometry.");
            if (string.IsNullOrWhiteSpace(mesh.Material)) throw new InvalidDataException($"Choose a material for '{mesh.Name}'.");
            if (settings.Target != CollisionOutput.TotkPolytope && !BotwCollisionMaterials.Names.Contains(mesh.Material))
                throw new InvalidDataException($"'{mesh.Material}' is not a BotW collision material. Choose a material for '{mesh.Name}'.");
        }
        if (settings.Target != CollisionOutput.TotkPolytope) return new(meshes, null);
        var generated = TotkPolytopeBuilder.Create(meshes, 1, Vector3.Zero, false);
        return new(generated.Meshes, generated);
    }

    internal static string Extension(CollisionSettings s) => s.Target switch
    { CollisionOutput.TotkPolytope => ".bgyml", CollisionOutput.WiiUStatic => s.Compress ? ".shksc" : ".hksc", _ => ".hkrb" };

    internal static void Write(CollisionPreview preview, CollisionSettings settings, string path, string source, bool meshExport = false)
    {
        path = Path.GetFullPath(path);
        if (string.Equals(path, Path.GetFullPath(source), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Choose a separate output; the source file cannot be replaced.");
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (meshExport ? extension is not (".dae" or ".fbx") : extension != Extension(settings))
            throw new InvalidOperationException("The output extension does not match the selected format.");
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".collision-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            if (meshExport)
            {
                BotwCollisionExporter.Export(preview.Meshes, temporary);
                if (BotwCollisionMeshImporter.Import(temporary).Count == 0) throw new InvalidDataException("Exported mesh validation failed.");
            }
            else if (preview.Polytope != null)
            {
                TotkPolytopeBuilder.Write(preview.Polytope, temporary);
                if (TotkPolytopeImporter.Import(temporary).Count != preview.Meshes.Count) throw new InvalidDataException("ShapeParam validation failed.");
            }
            else
            {
                BotwCollisionBuilder.Build(preview.Meshes, new(temporary, 1, Vector3.Zero, settings.Target == CollisionOutput.WiiUStatic && settings.Compress,
                    settings.Target == CollisionOutput.SwitchActor ? BotwCollisionPlatform.Switch : BotwCollisionPlatform.WiiU,
                    settings.Target == CollisionOutput.WiiUStatic ? BotwCollisionTarget.StaticCompound : BotwCollisionTarget.ActorRigidBody,
                    string.IsNullOrWhiteSpace(settings.BodyName) ? Path.GetFileNameWithoutExtension(path) : settings.BodyName));
                if (BotwCollisionFileImporter.Import(temporary).Count == 0) throw new InvalidDataException("Generated collision has no readable geometry.");
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
