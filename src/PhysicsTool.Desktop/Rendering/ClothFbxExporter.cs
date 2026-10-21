using Assimp;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop.Rendering;

internal static class ClothFbxExporter
{
    private const int ClothMaterialIndex = 0;
    private const int ColliderMaterialIndex = 1;

    internal static Scene CreateScene(HkclService document, int cloth)
    {
        if (!document.HasDocument || document.IsBphcl || document.IsHelperBoneSidecar || document.IsHkrg)
            throw new InvalidOperationException("Select an HKCL cloth to export.");

        var preview = document.GetParticlePreview(cloth);
        if (preview.Particles.Count == 0)
            throw new InvalidOperationException("The cloth has no particles to export.");

        var rows = document.GetBoneRows(cloth);
        var worldTransforms = BoneEditGeometry.WorldTransforms(rows);
        var inverseTransforms = worldTransforms.ToDictionary(
            pair => pair.Key,
            pair => Matrix4x4.Invert(pair.Value, out var inverse)
                ? inverse
                : throw new InvalidDataException("A cloth bone has a singular transform."));

        var scene = CreateEmptyScene();
        var usedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ClothExport", "Skeleton", "Particles", "Colliders"
        };
        var boneNodes = AddSkeleton(scene, rows, worldTransforms, inverseTransforms, usedNames);
        var particles = CreateParticleMesh(document.GetClothName(cloth), preview);
        AddSkinWeights(particles, preview, document.GetParticleBoneAttachments(cloth), boneNodes, inverseTransforms);
        AddMesh(scene, new Node("Particles"), scene.RootNode, particles);
        AddColliders(scene, preview.Colliders, boneNodes, inverseTransforms, usedNames);

        if (scene.Meshes.SelectMany(mesh => mesh.Vertices).Any(position => !float.IsFinite(position.LengthSquared())))
            throw new InvalidDataException("The cloth contains non-finite geometry.");

        return scene;
    }

    private static Scene CreateEmptyScene()
    {
        var scene = new Scene { RootNode = new Node("ClothExport") };
        // FBX uses centimeters; the preview geometry is expressed in meters.
        scene.Metadata.Add("UnitScaleFactor", new Metadata.Entry(MetaDataType.Double, 100d));
        scene.Materials.Add(new Material
        {
            Name = "Cloth",
            ColorDiffuse = new Vector4(.35f, .65f, .85f, 1)
        });
        scene.Materials.Add(new Material
        {
            Name = "Colliders",
            ColorDiffuse = new Vector4(.25f, .8f, .65f, 1),
            Opacity = .35f
        });
        return scene;
    }

    private static Dictionary<int, Node> AddSkeleton(
        Scene scene,
        IReadOnlyList<BoneEditRow> rows,
        IReadOnlyDictionary<int, Matrix4x4> worldTransforms,
        IReadOnlyDictionary<int, Matrix4x4> inverseTransforms,
        HashSet<string> usedNames)
    {
        var skeleton = new Node("Skeleton");
        scene.RootNode.Children.Add(skeleton);
        var nodes = rows.ToDictionary(bone => bone.Index, bone => new Node(UniqueName(bone.Name, usedNames)));

        foreach (var row in rows)
        {
            var parentInverse = row.ParentIndex < 0 ? Matrix4x4.Identity : inverseTransforms[row.ParentIndex];
            var localTransform = worldTransforms[row.Index] * parentInverse;
            var parent = row.ParentIndex < 0 ? skeleton : nodes[row.ParentIndex];

            // Assimp's node matrices use the opposite vector convention.
            nodes[row.Index].Transform = Matrix4x4.Transpose(localTransform);
            parent.Children.Add(nodes[row.Index]);
        }

        return nodes;
    }

    private static Mesh CreateParticleMesh(string name, ParticlePreviewData preview)
    {
        var mesh = new Mesh(name, PrimitiveType.Triangle | PrimitiveType.Point)
        {
            MaterialIndex = ClothMaterialIndex
        };
        var indices = ParticleIndices(preview);
        mesh.Vertices.AddRange(preview.Particles.Select(particle => particle.Position));
        var usedVertices = new HashSet<int>();

        foreach (var triangle in preview.Triangles)
        {
            var face = new[] { indices[triangle.ParticleA], indices[triangle.ParticleB], indices[triangle.ParticleC] };
            mesh.Faces.Add(new Face(face));
            usedVertices.UnionWith(face);
        }

        // Keep loose particles too: no welding, triangulation or vertex reordering.
        for (var index = 0; index < mesh.VertexCount; index++)
        {
            if (!usedVertices.Contains(index))
                mesh.Faces.Add(new Face([index]));
        }

        return mesh;
    }

    private static Dictionary<int, int> ParticleIndices(ParticlePreviewData preview) =>
        preview.Particles.Select((particle, vertexIndex) => (particle.Index, vertexIndex))
            .ToDictionary(pair => pair.Index, pair => pair.vertexIndex);

    private static void AddSkinWeights(
        Mesh mesh,
        ParticlePreviewData preview,
        IReadOnlyList<ParticlePreviewAttachment> attachments,
        IReadOnlyDictionary<int, Node> nodes,
        IReadOnlyDictionary<int, Matrix4x4> inverseTransforms)
    {
        var indices = ParticleIndices(preview);
        var bones = nodes.ToDictionary(pair => pair.Key, pair => new Bone
        {
            Name = pair.Value.Name,
            OffsetMatrix = Matrix4x4.Transpose(inverseTransforms[pair.Key])
        });

        foreach (var attachment in attachments)
        {
            if (!indices.TryGetValue(attachment.ParticleIndex, out var vertexIndex))
                continue;

            var weights = attachment.BoneWeights
                .Where(weight => bones.ContainsKey(weight.BoneIndex) && float.IsFinite(weight.Weight) && weight.Weight > 0)
                .GroupBy(weight => weight.BoneIndex)
                .Select(group => (BoneIndex: group.Key, Weight: group.Sum(weight => weight.Weight)))
                .ToArray();
            var totalWeight = weights.Sum(weight => weight.Weight);

            foreach (var weight in weights)
                bones[weight.BoneIndex].VertexWeights.Add(new(vertexIndex, weight.Weight / totalWeight));
        }

        // Include unweighted bones so the exported armature retains its full hierarchy.
        mesh.Bones.AddRange(bones.Values);
    }

    private static void AddColliders(
        Scene scene,
        IEnumerable<ColliderPreviewShape> shapes,
        IReadOnlyDictionary<int, Node> boneNodes,
        IReadOnlyDictionary<int, Matrix4x4> inverseTransforms,
        HashSet<string> usedNames)
    {
        var colliders = new Node("Colliders");
        scene.RootNode.Children.Add(colliders);

        foreach (var shape in shapes)
        {
            var node = new Node(UniqueName($"Collider_{shape.Index}_{shape.Name}_{shape.Kind}", usedNames));
            var parent = boneNodes.GetValueOrDefault(shape.BoneIndex) ?? colliders;
            var parentInverse = inverseTransforms.GetValueOrDefault(shape.BoneIndex, Matrix4x4.Identity);
            var faces = ColliderGeometry.Surface(shape);

            if (faces.Count == 0)
            {
                var position = Vector3.Transform(shape.Start, parentInverse);
                node.Transform = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(position));
                parent.Children.Add(node);
                continue;
            }

            var mesh = new Mesh(node.Name, PrimitiveType.Triangle) { MaterialIndex = ColliderMaterialIndex };
            var vertices = new Dictionary<Vector3, int>();

            int GetVertexIndex(Vector3 position)
            {
                if (vertices.TryGetValue(position, out var index))
                    return index;

                index = mesh.VertexCount;
                vertices.Add(position, index);
                mesh.Vertices.Add(Vector3.Transform(position, parentInverse));
                return index;
            }

            foreach (var triangle in faces)
                mesh.Faces.Add(new Face([GetVertexIndex(triangle.A), GetVertexIndex(triangle.B), GetVertexIndex(triangle.C)]));

            AddMesh(scene, node, parent, mesh);
        }
    }

    private static void AddMesh(Scene scene, Node node, Node parent, Mesh mesh)
    {
        node.MeshIndices.Add(scene.Meshes.Count);
        scene.Meshes.Add(mesh);
        parent.Children.Add(node);
    }

    private static string UniqueName(string name, HashSet<string> usedNames)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "Bone";

        var candidate = name;
        var suffix = 1;
        while (!usedNames.Add(candidate))
            candidate = name + "_" + suffix++;

        return candidate;
    }

    internal static void Export(HkclService document, int cloth, string path)
    {
        path = Path.GetFullPath(path);
        if (!Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an FBX output file.");

        var scene = CreateScene(document, cloth);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".cloth-" + Guid.NewGuid().ToString("N") + ".fbx");
        try
        {
            ExportAndValidate(scene, temporary);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void ExportAndValidate(Scene scene, string path)
    {
        using var context = new AssimpContext();
        if (!context.ExportFile(scene, path, "fbx", PostProcessSteps.ValidateDataStructure))
            throw new IOException("FBX export failed.");

        var imported = context.ImportFile(path, PostProcessSteps.ValidateDataStructure);
        if (imported == null || imported.MeshCount != scene.MeshCount ||
            imported.Meshes.Sum(mesh => mesh.VertexCount) < scene.Meshes[0].VertexCount)
            throw new InvalidDataException("The exported FBX could not be validated.");
    }
}
