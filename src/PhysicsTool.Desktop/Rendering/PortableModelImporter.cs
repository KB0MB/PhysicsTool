using Assimp;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop.Rendering;

public static class PortableModelImporter
{
    public static readonly string[] Extensions = [".fbx", ".dae", ".obj", ".gltf", ".glb", ".bfres", ".sbfres"];
    private static readonly Lazy<VisualModelTexture> EyeGlint = new(() => ModelTextures.Resource("EyeGlint.png"));
    internal static void ConfigureBfres() => HKCLTool.VisualModelImporter.EyeGlint = () => EyeGlint.Value;
    public static VisualModelData Load(string path)
    {
        path = Path.GetFullPath(path);
        if (!Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            throw new NotSupportedException("Select a BFRES, SBFRES, FBX, DAE, OBJ, glTF, or GLB model.");
        if (Path.GetExtension(path).Equals(".bfres", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".sbfres", StringComparison.OrdinalIgnoreCase))
        { ConfigureBfres(); return new BfresModelPose(path).Sample(null, null); }
        using var context = new AssimpContext();
        var scene = context.ImportFile(path, PostProcessSteps.Triangulate | PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.SortByPrimitiveType | PostProcessSteps.ValidateDataStructure);
        if (scene?.RootNode == null || !scene.HasMeshes) throw new InvalidDataException("The model contains no mesh geometry.");
        float unit = 1;
        if (Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase) && scene.Metadata.TryGetValue("UnitScaleFactor", out var scale))
            unit = Convert.ToSingle(scale.Data) * .01f;
        if (!float.IsFinite(unit) || unit <= 0) throw new InvalidDataException("Invalid model unit scale.");
        var result = new VisualModelData { SourcePath = path, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
        var textures = new Dictionary<string, VisualModelTexture?>();
        Visit(scene.RootNode, Matrix4x4.CreateScale(unit), -1, 0);
        if (result.Meshes.Count == 0) throw new InvalidDataException("The model contains no triangles.");
        result.DefaultTextureMeshCount = result.Meshes.Count(m => m.Texture == null);
        if (scene.HasAnimations) result.Warnings.Add("Animations are present; this preview currently displays the imported static pose.");
        return result;

        void Visit(Node node, Matrix4x4 parent, int parentIndex, int depth)
        {
            if (depth > 256 || result.Bones.Count > 100000) throw new InvalidDataException("Model hierarchy exceeds preview limits.");
            // Assimp retains column-vector matrices; Numerics uses row vectors.
            var world = Matrix4x4.Transpose(node.Transform) * parent;
            var boneIndex = result.Bones.Count;
            if (!Matrix4x4.Decompose(world, out var s, out var r, out var p))
                throw new InvalidDataException("Model contains a singular transform.");
            result.Bones.Add(new() { Index = boneIndex, ParentIndex = parentIndex, Name = node.Name, Position = p, Rotation = r, Scale = s });
            foreach (var index in node.MeshIndices) AddMesh(scene.Meshes[index], world, node.Name);
            foreach (var child in node.Children) Visit(child, world, boneIndex, depth + 1);
        }

        void AddMesh(Mesh source, Matrix4x4 world, string nodeName)
        {
            var indices = source.Faces.Where(f => f.IndexCount == 3).SelectMany(f => f.Indices).Select(i => checked((uint)i)).ToArray();
            if (indices.Length == 0) return;
            if (indices.Any(i => i >= source.VertexCount)) throw new InvalidDataException("Invalid model triangle.");
            result.TriangleCount += indices.Length / 3;
            if (result.TriangleCount > 2000000) throw new InvalidDataException("Model exceeds the two-million-triangle preview limit.");
            Matrix4x4.Invert(world, out var inverse); var normalMatrix = Matrix4x4.Transpose(inverse);
            if (world.GetDeterminant() < 0) for (int i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
            var vertices = new float[checked(source.VertexCount * 8)];
            for (int i = 0; i < source.VertexCount; i++)
            {
                var p = Vector3.Transform(source.Vertices[i], world);
                var n = Vector3.TransformNormal(source.HasNormals ? source.Normals[i] : Vector3.UnitY, normalMatrix);
                n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
                if (!Finite(p) || !Finite(n)) throw new InvalidDataException("Model contains non-finite vertices.");
                var uv = source.HasTextureCoords(0) ? source.TextureCoordinateChannels[0][i] : Vector3.Zero;
                float[] row = [p.X, p.Y, p.Z, n.X, n.Y, n.Z, uv.X, uv.Y]; row.CopyTo(vertices, i * 8);
                result.BoundsMinimum = Vector3.Min(result.BoundsMinimum, p); result.BoundsMaximum = Vector3.Max(result.BoundsMaximum, p);
            }
            var material = scene.Materials[source.MaterialIndex];
            var texture = Texture(material, TextureType.BaseColor) ?? Texture(material, TextureType.Diffuse);
            var normal = Texture(material, TextureType.Normals);
            var color = material.HasColorDiffuse ? material.ColorDiffuse : Vector4.One;
            var mesh = new VisualModelMesh { Name = (string.IsNullOrWhiteSpace(source.Name) ? nodeName : source.Name) + "__" + material.Name,
                Vertices = vertices, Indices = indices, BaseColor = color, Texture = texture, NormalTexture = normal,
                UseMissingTextureFallback = texture == null, CullingMode = VisualModelCullingMode.Off,
                Material = new() { Tint = color, UseAlbedo = true, UseNormal = normal != null, ShaderModel = VisualModelShaderModel.Standard } };
            for (int channel = 0; channel < 8; channel++) if (source.HasTextureCoords(channel))
                mesh.UvSets.Add(new() { Name = "UV" + channel, Coordinates = source.TextureCoordinateChannels[channel].SelectMany(v => new[] { v.X, v.Y }).ToArray() });
            if (material.GetMaterialTexture(TextureType.BaseColor, 0, out var albedoSlot) || material.GetMaterialTexture(TextureType.Diffuse, 0, out albedoSlot))
                mesh.Material.AlbedoUvSet = "UV" + albedoSlot.UVIndex;
            if (material.GetMaterialTexture(TextureType.Normals, 0, out var slot)) mesh.Material.NormalUvSet = "UV" + slot.UVIndex;
            result.Meshes.Add(mesh);
        }

        VisualModelTexture? Texture(Material material, TextureType type)
        {
            if (!material.GetMaterialTexture(type, 0, out var slot) || string.IsNullOrWhiteSpace(slot.FilePath)) return null;
            var name = slot.FilePath;
            if (textures.TryGetValue(name, out var cached)) return cached;
            try
            {
                VisualModelTexture texture;
                var embedded = name.StartsWith('*') && int.TryParse(name.AsSpan(1), out var i) && i >= 0 && i < scene.TextureCount
                    ? scene.Textures[i] : scene.Textures.FirstOrDefault(t => t.Filename == name);
                if (embedded != null)
                {
                    if (embedded.IsCompressed) texture = ModelTextures.Decode(embedded.CompressedData, name);
                    else
                    {
                        ModelTextures.Validate(embedded.Width, embedded.Height);
                        var pixels = new byte[checked(embedded.Width * embedded.Height * 4)];
                        for (int y = 0; y < embedded.Height; y++) for (int x = 0; x < embedded.Width; x++)
                        {
                            var t = embedded.NonCompressedData[y * embedded.Width + x]; var o = ((embedded.Height - 1 - y) * embedded.Width + x) * 4;
                            pixels[o] = t.R; pixels[o + 1] = t.G; pixels[o + 2] = t.B; pixels[o + 3] = t.A;
                        }
                        texture = ModelTextures.FromPixels(name, embedded.Width, embedded.Height, pixels);
                    }
                    result.EmbeddedTextureCount++;
                }
                else
                {
                    var normalized = name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    var file = Path.Combine(Path.GetDirectoryName(path)!, normalized);
                    if (!File.Exists(file)) file = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(normalized));
                    texture = ModelTextures.Decode(File.ReadAllBytes(file), Path.GetFileName(file)); result.ExternalTextureCount++;
                }
                return textures[name] = texture;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
            { result.Warnings.Add($"Texture {Path.GetFileName(name.Replace('\\', '/'))}: could not load ({ex.GetType().Name})."); return textures[name] = null; }
        }
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
