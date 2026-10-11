using System.Numerics;

namespace HKCLTool;

internal sealed class BfresModelPose
{
    private readonly string _path;
    private readonly List<ToolboxActorModel> _models = new();
    private readonly List<string> _warnings = new();
    private readonly int _embedded;
    private readonly int _external;
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
    }

    public VisualModelData Sample(VisualModelData? appearance, IReadOnlyList<VisualBoneOverride>? overrides,
        ToolboxActorAnimation? animation = null, double frame = 0, ISet<string>? suppressed = null, bool skeletonOnly = false)
    {
        var mapped = overrides?.GroupBy(p => p.Name[(p.Name.LastIndexOf(':') + 1)..], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var result = new VisualModelData { SourcePath = _path, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue),
            EmbeddedTextureCount = _embedded, ExternalTextureCount = _external };
        result.Warnings.AddRange(_warnings);
        foreach (var model in _models)
        {
            var pose = skeletonOnly ? model.SkeletonPose(animation, frame, mapped, suppressed) : model.Pose(animation, frame, mapped, suppressed);
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
}
