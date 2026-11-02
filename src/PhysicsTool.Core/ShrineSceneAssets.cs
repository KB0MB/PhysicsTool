using System.Numerics;
using HKCLTool;

namespace PhysicsTool.Core;

internal sealed record ShrineSceneAssets(ShrineSceneDocument Scene, VisualModelData Model, string? CompoundPath,
    BphscDocument? Compound, IReadOnlyList<string> Warnings, int RenderedActors)
{
    internal static Matrix4x4 Placement(ShrineActor actor) => Matrix4x4.CreateScale(actor.Scale)
        * Matrix4x4.CreateRotationX(actor.Rotation.X) * Matrix4x4.CreateRotationY(actor.Rotation.Y)
        * Matrix4x4.CreateRotationZ(actor.Rotation.Z) * Matrix4x4.CreateTranslation(actor.Position);

    internal static ShrineSceneAssets Load(string path, string root, IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        root = ShrineSceneCatalog.Root(root);
        var warnings = new List<string>();
        var actors = new List<ShrineActor>();
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scenes = new Dictionary<string, ShrineSceneDocument>(StringComparer.OrdinalIgnoreCase);
        void Expand(string file, Matrix4x4 parent, int depth, int parentIndex = -1)
        {
            cancellation.ThrowIfCancellationRequested();
            file = System.IO.Path.GetFullPath(file);
            if (depth > 16 || !active.Add(file)) throw new InvalidDataException("Cyclic or excessively nested merged BANC: " + file);
            if (!scenes.TryGetValue(file, out var scene)) scenes[file] = scene = ShrineSceneDocument.Load(file, [root]);
            foreach (var actor in scene.Actors)
            {
                if (actors.Count >= 100000) throw new InvalidDataException("Expanded scene exceeds 100,000 actors.");
                var transform = Placement(actor) * parent;
                int index = actors.Count;
                actors.Add(actor with { Index = index, Position = transform.Translation, WorldTransform = transform, SourceScene = file,
                    SourceIndex = actor.Index, ParentIndex = parentIndex, LocalPosition = actor.Position });
                if (actor.MergedPath.Length == 0) continue;
                var relative = actor.MergedPath.Replace('/', System.IO.Path.DirectorySeparatorChar);
                var merged = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
                if (!merged.StartsWith(System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Merged BANC path leaves the resource folder.");
                if (!File.Exists(merged) && File.Exists(merged + ".zs")) merged += ".zs";
                if (File.Exists(merged)) Expand(merged, transform, depth + 1, index);
                else warnings.Add("Missing merged scene: " + actor.MergedPath);
            }
            active.Remove(file);
        }
        progress?.Report("Reading scene placements and merged scenes...");
        Expand(path, Matrix4x4.Identity, 0);
        if (ShrineSceneCatalog.Companion(path) is { } sibling) Expand(sibling, Matrix4x4.Identity, 0);

        var compoundPath = ShrineSceneCatalog.FindCompound(root, path);
        BphscDocument? compound = null;
        if (compoundPath != null)
        {
            try { compound = BphscDocument.Load(compoundPath, [root]); }
            catch (Exception ex) when (ex is not OperationCanceledException) { warnings.Add("BPHSC: " + ex.Message); }
        }
        var models = new Dictionary<string, VisualModelData?>(StringComparer.Ordinal);
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        var model = new VisualModelData { SourcePath = path, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
        int rendered = 0; long vertexCount = 0;
        foreach (var actor in actors)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!models.TryGetValue(actor.Name, out var source))
            {
                progress?.Report($"Loading models: {actor.Name} ({models.Count + 1})");
                source = null;
                var pack = System.IO.Path.Combine(root, "Pack", "Actor", actor.Name + ".pack.zs");
                if (File.Exists(pack))
                {
                    try { source = LoadModel(pack, root, textures, warnings, cancellation); }
                    catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException) { warnings.Add(actor.Name + ": " + ex.Message); }
                }
                else if (actor.MergedPath.Length == 0) warnings.Add("No actor pack: " + actor.Name);
                models[actor.Name] = source;
            }
            if (source == null || source.Meshes.Count == 0) continue;
            foreach (var mesh in source.Meshes)
            {
                vertexCount += mesh.Vertices.Length / 8;
                if (vertexCount > 16000000) throw new InvalidDataException("Scene exceeds the 16 million preview vertex limit. Open a smaller BANC scene.");
                AddMesh(model, mesh, actor.WorldTransform, actor.Index + ":" + actor.Name + "/" + mesh.Name);
            }
            rendered++;
        }
        if (model.Meshes.Count == 0) model.BoundsMinimum = model.BoundsMaximum = Vector3.Zero;
        return new(new(actors), model, compoundPath, compound, warnings.Distinct().ToArray(), rendered);
    }

    private static VisualModelData LoadModel(string pack, string root, Dictionary<string, VisualModelTexture> textures, List<string> warnings, CancellationToken cancellation)
    {
        var resources = new BotwActorResources(pack, [root]);
        resources.PrepareTotkStrings();
        var result = new VisualModelData();
        foreach (var group in resources.Models.GroupBy(m => m.Resource))
        {
            cancellation.ThrowIfCancellationRequested();
            var bytes = resources.ReadModel(group.Key) ?? throw new FileNotFoundException("Missing model: " + group.Key);
            var document = new ToolboxBfresDocument(bytes);
            document.ReadTextures(textures, resources.Warnings);
            TexToGoTexture.Load(document, resources, textures, cancellation);
            var appearance = ToolboxActorAppearance.Read(document, resources.VariationMatAnim, resources.VariationMatAnimFrame, resources.Warnings);
            foreach (var reference in group)
                result.Meshes.AddRange(new ToolboxActorModel(document, reference.Model, textures, appearance, resources.Warnings).RestPose.Meshes);
        }
        warnings.AddRange(resources.Warnings.Where(w => !w.StartsWith("TotK preview")));
        return result;
    }

    internal static void AddMesh(VisualModelData model, VisualModelMesh mesh, Matrix4x4 transform, string name)
    {
        if (!Matrix4x4.Invert(transform, out var inverse)) return;
        var normals = Matrix4x4.Transpose(inverse);
        var vertices = (float[])mesh.Vertices.Clone();
        var tangents = new Vector4[mesh.Tangents.Length];
        bool mirrored = transform.GetDeterminant() < 0;
        for (int v = 0; v < vertices.Length; v += 8)
        {
            var p = Vector3.Transform(new(vertices[v], vertices[v + 1], vertices[v + 2]), transform);
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) throw new InvalidDataException("Nonfinite placed model coordinates: " + name);
            var n = Unit(Vector3.TransformNormal(new(vertices[v + 3], vertices[v + 4], vertices[v + 5]), normals));
            vertices[v] = p.X; vertices[v + 1] = p.Y; vertices[v + 2] = p.Z;
            vertices[v + 3] = n.X; vertices[v + 4] = n.Y; vertices[v + 5] = n.Z;
            model.BoundsMinimum = Vector3.Min(model.BoundsMinimum, p); model.BoundsMaximum = Vector3.Max(model.BoundsMaximum, p);
            if (v / 8 < tangents.Length)
            {
                var t = mesh.Tangents[v / 8]; var direction = Vector3.TransformNormal(new(t.X, t.Y, t.Z), transform);
                tangents[v / 8] = new(Unit(direction - n * Vector3.Dot(n, direction)), t.W * (mirrored ? -1 : 1));
            }
        }
        var indices = mesh.Indices;
        if (mirrored) { indices = (uint[])indices.Clone(); for (int i = 0; i + 2 < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]); }
        model.Meshes.Add(new() { Name = name, Vertices = vertices, Indices = indices, Tangents = tangents,
            TangentUvSet = mesh.TangentUvSet, UvSets = mesh.UvSets, Material = mesh.Material, BaseColor = mesh.BaseColor,
            CullingMode = mesh.CullingMode, Texture = mesh.Texture, SpecularTexture = mesh.SpecularTexture,
            NormalTexture = mesh.NormalTexture, AmbientOcclusionTexture = mesh.AmbientOcclusionTexture,
            AuxiliaryTexture = mesh.AuxiliaryTexture, EyeGlintTexture = mesh.EyeGlintTexture, UseMissingTextureFallback = mesh.UseMissingTextureFallback });
        model.TriangleCount += indices.Length / 3;
    }
    private static Vector3 Unit(Vector3 value) => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : Vector3.UnitY;
}
