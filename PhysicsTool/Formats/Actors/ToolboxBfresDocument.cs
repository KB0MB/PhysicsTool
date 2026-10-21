using System.Collections;
using System.Numerics;
using ResU = Syroot.NintenTools.Bfres;
using ResS = Syroot.NintenTools.NSW.Bfres;

namespace HKCLTool;

// The two Toolbox readers expose different collection types. Normalize them at
// the format boundary; the viewer and animation runtime use our own data types.
internal sealed class ToolboxBfresDocument
{
    public object Resource { get; }
    public bool IsSwitch { get; }
    public ToolboxBfresDocument(byte[] source)
    {
        var bytes = source.AsSpan().StartsWith("Yaz0"u8) ? BotwYaz0.Decode(source) : source;
        if (bytes.Length < 32 || !bytes.AsSpan().StartsWith("FRES"u8)) throw new InvalidDataException("The referenced model resource is not BFRES.");
        IsSwitch = bytes[0xc] == 0xff && bytes[0xd] == 0xfe;
        using var stream = new MemoryStream(bytes, false);
        Resource = IsSwitch ? new ResS.ResFile(stream) : new ResU.ResFile(stream);
    }
    public dynamic[] Models => Items(((dynamic)Resource).Models);
    public dynamic[] SkeletalAnimations => Items(((dynamic)Resource).SkeletalAnims);
    public static dynamic[] Items(object collection)
    {
        var values = collection.GetType().GetProperties().FirstOrDefault(p => p.Name == "Values")?.GetValue(collection) ?? collection;
        return ((IEnumerable)values).Cast<object>().Select(value => value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
            ? value.GetType().GetProperty("Value")!.GetValue(value)! : value).ToArray();
    }

    public void ReadTextures(Dictionary<string, VisualModelTexture> textures, List<string> warnings)
    {
        if (Resource is ResU.ResFile wii)
        {
            foreach (var tex in wii.Textures.Values)
                Try(tex.Name, () => ToolboxActorTexture.Decode(tex));
        }
        else if (Resource is ResS.ResFile nx)
        {
            foreach (var ext in nx.ExternalFiles)
            {
                if (!ext.Data.AsSpan().StartsWith("BNTX"u8)) continue;
                using var stream = new MemoryStream(ext.Data, false);
                var bntx = new Syroot.NintenTools.NSW.Bntx.BntxFile(stream);
                foreach (var tex in bntx.Textures) Try(tex.Name, () => ToolboxActorTexture.Decode(tex));
            }
        }
        void Try(string name, Func<VisualModelTexture> decode)
        {
            if (textures.ContainsKey(name)) return;
            try { textures[name] = decode(); }
            catch (Exception ex) { warnings.Add($"Texture {name}: {ex.GetType().Name}"); }
        }
    }
}

internal sealed class ToolboxActorModel
{
    private sealed record Bone(string Name, int Parent, Vector3 Position, Vector4 Rotation, Vector3 Scale, bool Euler, bool SegmentScale, bool Rigid);
    private sealed record Mesh(VisualModelMesh Visual, Vector3[] Positions, Vector3[] Normals, int[][] Bones, float[][] Weights);
    private readonly Bone[] _bones;
    private readonly Matrix4x4[] _restWorld;
    private readonly Matrix4x4[] _inverseRest;
    private readonly List<Mesh> _meshes = new();
    public string Name { get; }
    public VisualModelData RestPose { get; }

    public ToolboxActorModel(ToolboxBfresDocument document, string name, Dictionary<string, VisualModelTexture> textures,
        IReadOnlyDictionary<(string Material, string Sampler), string>? textureOverrides = null, List<string>? warnings = null,
        IReadOnlySet<string>? hiddenMaterials = null)
    {
        dynamic model = document.Models.FirstOrDefault(m => (string)m.Name == name)
            ?? throw new InvalidDataException($"BFRES does not contain the referenced model '{name}'. Available: {string.Join(", ", document.Models.Select(m => (string)m.Name))}");
        Name = name;
        var boneObjects = ToolboxBfresDocument.Items((object)model.Skeleton.Bones);
        _bones = boneObjects.Select(b => new Bone((string)b.Name, (int)b.ParentIndex, V3(b.Position), V4(b.Rotation), V3(b.Scale),
            b.FlagsRotation.ToString().Contains("Euler"), b.FlagsTransform.ToString().Contains("SegmentScaleCompensate"), (int)b.RigidMatrixIndex >= 0)).ToArray();
        _restWorld = WorldMatrices(null, 0, null, null);
        _inverseRest = _restWorld.Select(m => Matrix4x4.Invert(m, out var inv) ? inv : Matrix4x4.Identity).ToArray();
        // Rigid equipment can omit the palette entirely and use shape.BoneIndex.
        var matrixBones = ((IEnumerable?)model.Skeleton.MatrixToBoneList)?.Cast<object>().Select(Convert.ToInt32).ToArray() ?? [];
        var materials = ToolboxBfresDocument.Items((object)model.Materials);
        foreach (dynamic shape in ToolboxBfresDocument.Items((object)model.Shapes))
        {
            if (hiddenMaterials?.Contains((string)materials[(int)shape.MaterialIndex].Name) == true) continue;
            dynamic buffer = model.VertexBuffers[(int)shape.VertexBufferIndex];
            dynamic helper = document.IsSwitch
                ? new ResS.Helpers.VertexBufferHelper((ResS.VertexBuffer)buffer)
                : (object)new ResU.Helpers.VertexBufferHelper((ResU.VertexBuffer)buffer, Syroot.BinaryData.ByteOrder.BigEndian);
            var attributes = ToolboxBfresDocument.Items((object)helper.Attributes).ToDictionary(a => (string)a.Name, a => (Syroot.Maths.Vector4F[])a.Data);
            if (!attributes.TryGetValue("_p0", out var positions)) continue;
            var count = positions.Length;
            var verts = new float[count * 8];
            var sourcePositions = new Vector3[count];
            var normals = new Vector3[count];
            var indices = new int[count][];
            var weights = new float[count][];
            var skinCount = (int)shape.VertexSkinCount;
            Vector4 Attribute(string attr, int vertex, Vector4 fallback) => attributes.TryGetValue(attr, out var values) && vertex < values.Length ? V4(values[vertex]) : fallback;
            for (var v = 0; v < count; v++)
            {
                var p = V3(positions[v]);
                var n = new Vector3(Attribute("_n0", v, new Vector4(0, 1, 0, 0)).X, Attribute("_n0", v, Vector4.UnitY).Y, Attribute("_n0", v, Vector4.UnitY).Z);
                indices[v] = new int[Math.Max(1, skinCount)]; weights[v] = new float[indices[v].Length];
                for (var j = 0; j < indices[v].Length; j++)
                {
                    var palette = (int)Component(Attribute(j < 4 ? "_i0" : "_i1", v, Vector4.Zero), j % 4);
                    indices[v][j] = skinCount == 0 ? (int)shape.BoneIndex : palette >= 0 && palette < matrixBones.Length ? matrixBones[palette] : (int)shape.BoneIndex;
                    if (indices[v][j] < 0 || indices[v][j] >= _bones.Length) throw new InvalidDataException($"Mesh {shape.Name} has an invalid bone index.");
                    weights[v][j] = skinCount <= 1 ? 1 : Component(Attribute(j < 4 ? "_w0" : "_w1", v, Vector4.Zero), j % 4);
                }
                var sum = weights[v].Sum();
                if (sum <= 0) weights[v][0] = 1;
                else for (var j = 0; j < weights[v].Length; j++) weights[v][j] /= sum;
                if (skinCount == 0 || (skinCount == 1 && _bones[indices[v][0]].Rigid))
                { p = Vector3.Transform(p, _restWorld[indices[v][0]]); n = Vector3.TransformNormal(n, _restWorld[indices[v][0]]); }
                sourcePositions[v] = p; normals[v] = SafeNormal(n);
                var uv = Attribute("_u0", v, Vector4.Zero);
                verts[v * 8 + 6] = uv.X; verts[v * 8 + 7] = uv.Y;
            }
            var lods = ToolboxBfresDocument.Items((object)shape.Meshes);
            if (lods.Length == 0) continue;
            dynamic lod = lods[0];
            var faces = ((IEnumerable)lod.GetIndices()).Cast<object>().Select(i => checked(Convert.ToUInt32(i) + (uint)lod.FirstVertex)).ToArray();
            if (!lod.PrimitiveType.ToString().Contains("Triangles")) throw new InvalidDataException($"Mesh {shape.Name} uses unsupported primitive type {lod.PrimitiveType}.");
            if (faces.Any(i => i >= count)) throw new InvalidDataException($"Mesh {shape.Name} has an invalid vertex index.");
            dynamic material = materials[(int)shape.MaterialIndex];
            VisualModelMesh visual = MakeMaterial((string)shape.Name, material, textures, verts, faces, textureOverrides, warnings);
            foreach (var attr in attributes.Where(a => a.Key.StartsWith("_u", StringComparison.Ordinal)).OrderBy(a => a.Key))
                visual.UvSets.Add(new VisualModelUvSet { Name = attr.Key, Coordinates = attr.Value.SelectMany(uv => new[] { uv.X, uv.Y }).ToArray() });
            ToolboxBotwMaterial.Apply((object)material, visual, warnings);
            ToolboxTotkMaterial.Apply((object)material, visual, textures);
            if (visual.Material.Totk is { } totk)
                totk.VertexColors = Enumerable.Range(0, count).Select(v => Attribute("_c0", v, Vector4.One)).ToArray();
            if (attributes.ContainsKey("_t0"))
            {
                visual.Tangents = new Vector4[count];
                visual.TangentUvSet = visual.Material.NormalUvSet;
                for (int v = 0; v < count; v++)
                {
                    var raw = Attribute("_t0", v, Vector4.UnitX);
                    var t = new Vector3(raw.X, raw.Y, raw.Z);
                    var n = Attribute("_n0", v, Vector4.UnitY);
                    var b = Vector3.Cross(new(n.X, n.Y, n.Z), t) * (raw.W < 0 ? -1 : 1);
                    if (attributes.ContainsKey("_b0")) { var bitangent = Attribute("_b0", v, Vector4.Zero); b = new(bitangent.X, bitangent.Y, bitangent.Z); }
                    if (skinCount == 0 || skinCount == 1 && _bones[indices[v][0]].Rigid)
                    { t = Vector3.TransformNormal(t, _restWorld[indices[v][0]]); b = Vector3.TransformNormal(b, _restWorld[indices[v][0]]); }
                    t = SafeNormal(t - normals[v] * Vector3.Dot(normals[v], t));
                    visual.Tangents[v] = new(t, Vector3.Dot(Vector3.Cross(normals[v], t), b) < 0 ? -1 : 1);
                }
            }
            _meshes.Add(new(visual, sourcePositions, normals, indices, weights));
        }
        RestPose = Pose(null, 0);
    }

    private static VisualModelMesh MakeMaterial(string shape, dynamic material, Dictionary<string, VisualModelTexture> textures, float[] vertices, uint[] indices,
        IReadOnlyDictionary<(string Material, string Sampler), string>? textureOverrides, List<string>? warnings)
    {
        var refs = ToolboxBfresDocument.Items((object)material.TextureRefs).Select(t => t is string text ? text : (string)t.Name).ToArray();
        string? albedoOverride = null;
        if (textureOverrides?.Count > 0)
        {
            var samplers = (object)material is ResS.Material nx
                ? Enumerable.Range(0, nx.SamplerDict.Count).Select(nx.SamplerDict.GetKey).ToArray()
                : ((ResU.Material)material).Samplers.Keys.ToArray();
            foreach (var pair in textureOverrides.Where(p => p.Key.Material == (string)material.Name))
            {
                var index = Array.IndexOf(samplers, pair.Key.Sampler);
                if (index < 0 || index >= refs.Length)
                { warnings?.Add($"Material {material.Name}: variation sampler {pair.Key.Sampler} was not found."); continue; }
                // Change only this model's bindings, never the shared BFRES or decoded textures.
                refs[index] = pair.Value;
                if (pair.Key.Sampler == "_a0") albedoOverride = pair.Value;
                if (!textures.ContainsKey(pair.Value)) warnings?.Add($"Material {material.Name}: missing variation texture {pair.Value}.");
            }
        }
        VisualModelTexture? Find(string suffix) => refs.Select(n => textures.GetValueOrDefault(n)).FirstOrDefault(t => t != null && t.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var alb = Find("_Alb") ?? refs.Select(n => textures.GetValueOrDefault(n)).FirstOrDefault(t => t != null && !t.Name.EndsWith("_Nrm") && !t.Name.EndsWith("_Spm"));
        if (albedoOverride != null) alb = textures.GetValueOrDefault(albedoOverride);
        var spm = Find("_Spm"); var nrm = Find("_Nrm"); var ao = Find("_Ao");
        var name = (string)material.Name;
        var shader = name.Contains("Eyelash", StringComparison.OrdinalIgnoreCase) ? VisualModelShaderModel.Eyelash
            : name.Contains("Eye", StringComparison.OrdinalIgnoreCase) ? VisualModelShaderModel.Eye
            : name.Contains("Hair", StringComparison.OrdinalIgnoreCase) ? VisualModelShaderModel.Hair
            : name.Contains("Skin", StringComparison.OrdinalIgnoreCase) || name.Contains("Face", StringComparison.OrdinalIgnoreCase) ? VisualModelShaderModel.Skin : VisualModelShaderModel.Cloth;
        return new VisualModelMesh
        {
            Name = shape + "__" + name, Vertices = vertices, Indices = indices, BaseColor = Vector4.One, UseMissingTextureFallback = true,
            CullingMode = VisualModelCullingMode.Off, Texture = alb, SpecularTexture = spm, NormalTexture = nrm, AmbientOcclusionTexture = ao,
            EyeGlintTexture = shader == VisualModelShaderModel.Eye ? VisualModelImporter.GetPackagedEyeGlintTexture() : null,
            Material = new() { ShaderModel = shader, UseAlbedo = alb != null, UseSpm = spm != null, UseNormal = nrm != null, UseAmbientOcclusion = ao != null,
                AlbedoUvSet = "_u0", SpmUvSet = "_u1", NormalUvSet = "_u1", AmbientOcclusionUvSet = "_u0", UseEyeGlint = shader == VisualModelShaderModel.Eye }
        };
    }

    public VisualModelData Pose(ToolboxActorAnimation? animation, double frame, IReadOnlyDictionary<string, VisualBoneOverride>? overrides = null, ISet<string>? unanimatedBones = null, Matrix4x4? attachment = null,
        IReadOnlyDictionary<string, Matrix4x4>? restDeltas = null)
    {
        var world = WorldMatrices(animation, frame, overrides, unanimatedBones, attachment, restDeltas);
        var delta = world.Select((m, i) => _inverseRest[i] * m).ToArray();
        var pose = new VisualModelData { SourcePath = Name + ".bfres", AnimationFrame = frame, AnimationSourcePath = animation?.Name ?? "",
            BoundsMinimum = new Vector3(float.MaxValue), BoundsMaximum = new Vector3(float.MinValue) };
        for (var i = 0; i < world.Length; i++)
        {
            Matrix4x4.Decompose(world[i], out var scale, out var rotation, out var position);
            pose.Bones.Add(new() { Index = i, ParentIndex = _bones[i].Parent, Name = _bones[i].Name, Position = position, Rotation = rotation, Scale = scale });
        }
        foreach (var mesh in _meshes)
        {
            var vertices = (float[])mesh.Visual.Vertices.Clone();
            var tangents = new Vector4[mesh.Visual.Tangents.Length];
            for (var v = 0; v < mesh.Positions.Length; v++)
            {
                var p = Vector3.Zero; var n = Vector3.Zero; var t = Vector3.Zero; var b = Vector3.Zero;
                var restT = tangents.Length > v ? new Vector3(mesh.Visual.Tangents[v].X, mesh.Visual.Tangents[v].Y, mesh.Visual.Tangents[v].Z) : Vector3.Zero;
                var restB = tangents.Length > v ? Vector3.Cross(mesh.Normals[v], restT) * mesh.Visual.Tangents[v].W : Vector3.Zero;
                for (var j = 0; j < mesh.Bones[v].Length; j++)
                {
                    var matrix = delta[mesh.Bones[v][j]]; var weight = mesh.Weights[v][j]; p += Vector3.Transform(mesh.Positions[v], matrix) * weight; n += Vector3.TransformNormal(mesh.Normals[v], matrix) * weight;
                    if (tangents.Length > v) { t += Vector3.TransformNormal(restT, matrix) * weight; b += Vector3.TransformNormal(restB, matrix) * weight; }
                }
                n = SafeNormal(n);
                if (tangents.Length > v) { t = SafeNormal(t - n * Vector3.Dot(n, t)); tangents[v] = new(t, Vector3.Dot(Vector3.Cross(n, t), b) < 0 ? -1 : 1); }
                vertices[v * 8] = p.X; vertices[v * 8 + 1] = p.Y; vertices[v * 8 + 2] = p.Z;
                vertices[v * 8 + 3] = n.X; vertices[v * 8 + 4] = n.Y; vertices[v * 8 + 5] = n.Z;
                pose.BoundsMinimum = Vector3.Min(pose.BoundsMinimum, p); pose.BoundsMaximum = Vector3.Max(pose.BoundsMaximum, p);
            }
            var src = mesh.Visual;
            pose.Meshes.Add(new() { Name = src.Name, Vertices = vertices, Indices = src.Indices, BaseColor = src.BaseColor, CullingMode = src.CullingMode,
                Tangents = tangents, TangentUvSet = src.TangentUvSet,
                UseMissingTextureFallback = src.UseMissingTextureFallback,
                Texture = src.Texture, SpecularTexture = src.SpecularTexture, NormalTexture = src.NormalTexture, AmbientOcclusionTexture = src.AmbientOcclusionTexture,
                EyeGlintTexture = src.EyeGlintTexture, UvSets = src.UvSets, Material = src.Material });
            pose.TriangleCount += src.Indices.Length / 3;
        }
        return pose;
    }

    public VisualModelData SkeletonPose(ToolboxActorAnimation? animation, double frame, IReadOnlyDictionary<string, VisualBoneOverride>? overrides, ISet<string>? unanimatedBones, Matrix4x4? attachment = null,
        IReadOnlyDictionary<string, Matrix4x4>? restDeltas = null)
    {
        var world = WorldMatrices(animation, frame, overrides, unanimatedBones, attachment, restDeltas);
        var pose = new VisualModelData();
        for (var i = 0; i < world.Length; i++)
        {
            Matrix4x4.Decompose(world[i], out var scale, out var rotation, out var position);
            pose.Bones.Add(new() { Index = i, ParentIndex = _bones[i].Parent, Name = _bones[i].Name, Position = position, Rotation = rotation, Scale = scale });
        }
        return pose;
    }

    private Matrix4x4[] WorldMatrices(ToolboxActorAnimation? animation, double frame, IReadOnlyDictionary<string, VisualBoneOverride>? overrides, ISet<string>? unanimated, Matrix4x4? attachment = null,
        IReadOnlyDictionary<string, Matrix4x4>? restDeltas = null)
    {
        var matrices = new Matrix4x4[_bones.Length]; var visited = new byte[_bones.Length];
        Matrix4x4 Visit(int i)
        {
            if (visited[i] == 2) return matrices[i];
            if (visited[i] == 1) throw new InvalidDataException("BFRES skeleton contains a parent cycle.");
            visited[i] = 1;
            var bone = _bones[i];
            var parent = bone.Parent >= 0 && bone.Parent < _bones.Length ? Visit(bone.Parent) : attachment ?? Matrix4x4.Identity;
            if (overrides != null && overrides.TryGetValue(bone.Name, out var pose))
                matrices[i] = Matrix4x4.CreateScale(pose.Scale) * Matrix4x4.CreateFromQuaternion(pose.Rotation) * Matrix4x4.CreateTranslation(pose.Position);
            else if (restDeltas != null && restDeltas.TryGetValue(bone.Name, out var delta))
                matrices[i] = _restWorld[i] * delta;
            else
            {
                var position = bone.Position; var scale = bone.Scale; var rotation = bone.Rotation; var euler = bone.Euler;
                if (animation != null && !(unanimated?.Contains(bone.Name) ?? false)) animation.Sample(bone.Name, frame, ref position, ref rotation, ref scale, ref euler);
                var q = euler ? Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateRotationX(rotation.X) * Matrix4x4.CreateRotationY(rotation.Y) * Matrix4x4.CreateRotationZ(rotation.Z))
                    : Quaternion.Normalize(new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W));
                var local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(q);
                if (bone.SegmentScale && bone.Parent >= 0)
                {
                    Matrix4x4.Decompose(parent, out var parentScale, out _, out _);
                    local *= Matrix4x4.CreateScale(new Vector3(Inv(parentScale.X), Inv(parentScale.Y), Inv(parentScale.Z)));
                }
                matrices[i] = local * Matrix4x4.CreateTranslation(position) * parent;
            }
            visited[i] = 2; return matrices[i];
        }
        for (var i = 0; i < matrices.Length; i++) Visit(i);
        return matrices;
    }
    private static float Inv(float v) => MathF.Abs(v) < 1e-8f ? 1 : 1 / v;
    private static Vector3 SafeNormal(Vector3 n) => n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
    internal static Vector3 V3(dynamic v) => new((float)v.X, (float)v.Y, (float)v.Z);
    internal static Vector4 V4(dynamic v) => new((float)v.X, (float)v.Y, (float)v.Z, (float)v.W);
    private static float Component(Vector4 v, int i) => i switch { 0 => v.X, 1 => v.Y, 2 => v.Z, _ => v.W };
}
