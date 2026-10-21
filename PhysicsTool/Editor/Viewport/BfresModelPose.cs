using System.Numerics;

namespace HKCLTool;

internal sealed class BfresModelPose
{
    private readonly string _path;
    private readonly List<ToolboxActorModel> _models = new();
    private readonly List<string> _warnings = new();
    private readonly int _embedded;
    private readonly int _external;
    private readonly Dictionary<string, VisualModelBone> _canonicalBones = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<VisualModelBone>? _bones;
    public IReadOnlyList<VisualModelBone> Bones => _bones ??= Sample(null, null).Bones;

    public BfresModelPose(string path)
    {
        _path = Path.GetFullPath(path);
        var document = new ToolboxBfresDocument(File.ReadAllBytes(_path));
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.Ordinal);
        document.ReadTextures(textures, _warnings);
        _embedded = textures.Count;
        var stem = Path.Combine(Path.GetDirectoryName(_path)!, Path.GetFileNameWithoutExtension(_path));
        foreach (var suffix in document.IsSwitch ? new[] { ".Tex.bfres", ".Tex.sbfres" } : new[] { ".Tex1.bfres", ".Tex1.sbfres" })
        {
            var companion = stem + suffix;
            if (!File.Exists(companion)) continue;
            try { new ToolboxBfresDocument(File.ReadAllBytes(companion)).ReadTextures(textures, _warnings); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { _warnings.Add($"Texture resource {Path.GetFileName(companion)}: {ex.GetType().Name}"); }
        }
        _external = textures.Count - _embedded;
        foreach (var model in document.Models) _models.Add(new ToolboxActorModel(document, (string)model.Name, textures));
        if (_models.Count == 0) throw new InvalidDataException("This BFRES contains no models. Select the model resource, not a texture or animation resource.");
        foreach (var model in _models)
            foreach (var bone in model.RestPose.Bones) _canonicalBones.TryAdd(BoneName(bone.Name), bone);
    }

    public VisualModelData Sample(VisualModelData? appearance, IReadOnlyList<VisualBoneOverride>? overrides,
        ToolboxActorAnimation? animation = null, double frame = 0, ISet<string>? suppressed = null, bool skeletonOnly = false)
    {
        Dictionary<string, Matrix4x4>? deltas = null;
        if (overrides != null)
        {
            deltas = new(StringComparer.OrdinalIgnoreCase);
            foreach (var pose in overrides)
            {
                var name = BoneName(pose.Name);
                if (!_canonicalBones.TryGetValue(name, out var rest)) continue;
                if (pose.Position == rest.Position && pose.Rotation == rest.Rotation && pose.Scale == rest.Scale)
                    deltas[name] = Matrix4x4.Identity;
                else
                {
                    if (!Matrix4x4.Invert(Matrix(rest.Position, rest.Rotation, rest.Scale), out var inverse))
                        throw new InvalidDataException($"BFRES bone '{name}' has a singular bind transform.");
                    deltas[name] = inverse * Matrix(pose.Position, pose.Rotation, pose.Scale);
                }
            }
        }
        var result = new VisualModelData { SourcePath = _path, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue),
            EmbeddedTextureCount = _embedded, ExternalTextureCount = _external };
        result.Warnings.AddRange(_warnings);
        foreach (var model in _models)
        {
            // Editor names refer to the first occurrence, but every FMDL owns its bind pose.
            // Broadcast the requested motion, never another skeleton's absolute world matrix.
            var pose = skeletonOnly ? model.SkeletonPose(animation, frame, null, suppressed, restDeltas: deltas)
                : model.Pose(animation, frame, null, suppressed, restDeltas: deltas);
            var offset = result.Bones.Count;
            result.Bones.AddRange(pose.Bones.Select(b => new VisualModelBone { Index = b.Index + offset,
                ParentIndex = b.ParentIndex < 0 ? -1 : b.ParentIndex + offset, Name = b.Name,
                Position = b.Position, Rotation = b.Rotation, Scale = b.Scale }));
            foreach (var mesh in pose.Meshes)
            {
                var i = result.Meshes.Count;
                var source = appearance != null && i < appearance.Meshes.Count ? appearance.Meshes[i] : mesh;
                result.Meshes.Add(new VisualModelMesh { Name = _models.Count > 1 ? model.Name + "/" + mesh.Name : mesh.Name,
                    UseMissingTextureFallback = mesh.UseMissingTextureFallback,
                    Vertices = mesh.Vertices, Indices = mesh.Indices, BaseColor = source.BaseColor, CullingMode = source.CullingMode,
                    Texture = source.Texture, SpecularTexture = source.SpecularTexture, NormalTexture = source.NormalTexture,
                    AmbientOcclusionTexture = source.AmbientOcclusionTexture, AuxiliaryTexture = source.AuxiliaryTexture,
                    EyeGlintTexture = source.EyeGlintTexture, UvSets = source.UvSets, Material = source.Material });
            }
            if (pose.Meshes.Count > 0)
            {
                result.BoundsMinimum = Vector3.Min(result.BoundsMinimum, pose.BoundsMinimum);
                result.BoundsMaximum = Vector3.Max(result.BoundsMaximum, pose.BoundsMaximum);
                result.TriangleCount += pose.TriangleCount;
            }
        }
        if (!skeletonOnly && result.Meshes.Count == 0) throw new InvalidDataException("This BFRES has no renderable meshes.");
        result.DefaultTextureMeshCount = result.Meshes.Count(m => m.Texture == null);
        return result;
    }
    private static string BoneName(string name) => name[(name.LastIndexOf(':') + 1)..];
    private static Matrix4x4 Matrix(Vector3 position, Quaternion rotation, Vector3 scale) =>
        Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
}
