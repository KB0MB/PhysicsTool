using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop.Rendering;

public sealed class ImportedModelRig(VisualModelData rest, Func<IReadOnlyList<VisualBoneOverride>, VisualModelData> sample)
{
    public VisualModelData Rest { get; } = rest;
    internal float UnitScale { get; init; } = 1;
    internal Func<ToolboxActorAnimation, double, ISet<string>?, VisualModelData>? NativeSkeleton { get; init; }
    private Func<ModelAnimationClip, double, ISet<string>?, VisualModelData>? AlignedSkeleton { get; init; }
    public VisualModelData Sample(IReadOnlyList<VisualBoneOverride> overrides) => overrides.Count == 0 ? Rest : sample(overrides);
    internal ImportedModelRig Translate(Vector3 offset)
    {
        if (offset.LengthSquared() < 1e-12f) return this;
        var roots = Rest.Bones.Where(b => b.ParentIndex < 0)
            .Select(b => new VisualBoneOverride(b.Name, b.Position + offset, b.Rotation, b.Scale)).ToArray();
        VisualModelData Pose(IReadOnlyList<VisualBoneOverride> overrides) => Sample(roots.Concat(overrides).ToArray());
        return new(Pose([]), Pose)
        {
            UnitScale = UnitScale,
            AlignedSkeleton = (clip, frame, suppressed) =>
            {
                var source = AnimationSkeleton(clip, frame, suppressed);
                var result = new VisualModelData { SourcePath = source.SourcePath,
                    AnimationSourcePath = source.AnimationSourcePath, AnimationFrame = source.AnimationFrame };
                foreach (var bone in source.Bones)
                    result.Bones.Add(new() { Index = bone.Index, ParentIndex = bone.ParentIndex, Name = bone.Name,
                        Position = bone.Position + offset, Rotation = bone.Rotation, Scale = bone.Scale, Sources = bone.Sources });
                return result;
            }
        };
    }
    internal VisualModelData AnimationSkeleton(ModelAnimationClip clip, double frame, ISet<string>? suppressed = null)
    {
        frame = Math.Clamp(frame, 0, clip.EndFrame);
        if (AlignedSkeleton != null) return AlignedSkeleton(clip, frame, suppressed);
        if (clip.Native != null && NativeSkeleton != null) return NativeSkeleton(clip.Native, frame, suppressed);
        var result = new VisualModelData { SourcePath = Rest.SourcePath, AnimationFrame = frame };
        var bones = Rest.Bones.ToDictionary(b => b.Index);
        var matrices = new Dictionary<int, Matrix4x4>(); var visiting = new HashSet<int>();
        Matrix4x4 Matrix(VisualModelBone b) => Matrix4x4.CreateScale(b.Scale) * Matrix4x4.CreateFromQuaternion(b.Rotation) * Matrix4x4.CreateTranslation(b.Position);
        Matrix4x4 Visit(VisualModelBone bone)
        {
            if (matrices.TryGetValue(bone.Index, out var found)) return found;
            if (!visiting.Add(bone.Index)) throw new InvalidDataException("Animation skeleton contains a parent cycle.");
            var parentRest = bones.TryGetValue(bone.ParentIndex, out var parent) ? Matrix(parent) : Matrix4x4.CreateScale(UnitScale);
            if (!Matrix4x4.Invert(parentRest, out var inverse)) throw new InvalidDataException("Animation skeleton has a singular parent.");
            var local = Matrix(bone) * inverse;
            Matrix4x4.Decompose(local, out var scale, out var rotation, out var position);
            if (!(suppressed?.Contains(ModelAnimationClip.Normalize(bone.Name)) ?? false)) clip.Sample(bone.Name, frame, UnitScale, ref position, ref rotation, ref scale);
            var world = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position) *
                (parent != null ? Visit(parent) : Matrix4x4.CreateScale(UnitScale));
            visiting.Remove(bone.Index); return matrices[bone.Index] = world;
        }
        foreach (var bone in Rest.Bones)
        {
            Matrix4x4.Decompose(Visit(bone), out var scale, out var rotation, out var position);
            result.Bones.Add(new() { Index = bone.Index, ParentIndex = bone.ParentIndex, Name = bone.Name, Position = position, Rotation = rotation, Scale = scale });
        }
        return result;
    }
}

internal sealed class AssimpModelSkin
{
    internal sealed record Weight(string Bone, float Value);
    internal sealed record MeshWeights(int Node, List<Weight>[] Vertices);
    private readonly VisualModelData _rest;
    private readonly Matrix4x4[] _world, _inverse, _local;
    private readonly (int Bone, float Value)[][][] _weights;

    internal AssimpModelSkin(VisualModelData rest, IReadOnlyList<Matrix4x4> world, IReadOnlyList<MeshWeights> meshes)
    {
        _rest = rest; _world = world.ToArray(); _inverse = new Matrix4x4[_world.Length]; _local = new Matrix4x4[_world.Length];
        var names = rest.Bones.GroupBy(b => b.Name, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().Index, StringComparer.Ordinal);
        for (int i = 0; i < _world.Length; i++)
            if (!Matrix4x4.Invert(_world[i], out _inverse[i])) throw new InvalidDataException("Model has a singular bone transform.");
        for (int i = 0; i < _world.Length; i++)
            _local[i] = rest.Bones[i].ParentIndex is var parent && parent >= 0 ? _world[i] * _inverse[parent] : _world[i];
        _weights = meshes.Select(mesh => mesh.Vertices.Select(vertex =>
        {
            var weights = vertex.Where(w => w.Value > 0 && float.IsFinite(w.Value) && names.ContainsKey(w.Bone)).Select(w => (Bone: names[w.Bone], Value: w.Value)).ToArray();
            var sum = weights.Sum(w => w.Value);
            return sum > 0 ? weights.Select(w => (w.Bone, w.Value / sum)).ToArray() : [(mesh.Node, 1f)];
        }).ToArray()).ToArray();
    }

    internal VisualModelData Sample(IReadOnlyList<VisualBoneOverride> input)
    {
        var overrides = input.GroupBy(b => b.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var world = new Matrix4x4[_world.Length];
        var delta = new Matrix4x4[_world.Length];
        var normals = new Matrix4x4[_world.Length];
        var result = new VisualModelData { SourcePath = _rest.SourcePath, TriangleCount = _rest.TriangleCount,
            EmbeddedTextureCount = _rest.EmbeddedTextureCount, ExternalTextureCount = _rest.ExternalTextureCount,
            DefaultTextureMeshCount = _rest.DefaultTextureMeshCount, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
        result.Warnings.AddRange(_rest.Warnings);
        for (int i = 0; i < world.Length; i++)
        {
            var bone = _rest.Bones[i];
            world[i] = overrides.TryGetValue(bone.Name, out var pose)
                ? Matrix4x4.CreateScale(pose.Scale) * Matrix4x4.CreateFromQuaternion(pose.Rotation) * Matrix4x4.CreateTranslation(pose.Position)
                : _local[i] * (bone.ParentIndex >= 0 ? world[bone.ParentIndex] : Matrix4x4.Identity);
            delta[i] = _inverse[i] * world[i];
            normals[i] = Matrix4x4.Invert(delta[i], out var inverse) ? Matrix4x4.Transpose(inverse) : Matrix4x4.Identity;
            Matrix4x4.Decompose(world[i], out var scale, out var rotation, out var position);
            result.Bones.Add(new() { Index = bone.Index, ParentIndex = bone.ParentIndex, Name = bone.Name, Position = position, Rotation = rotation, Scale = scale });
        }
        for (int meshIndex = 0; meshIndex < _rest.Meshes.Count; meshIndex++)
        {
            var source = _rest.Meshes[meshIndex];
            var vertices = (float[])source.Vertices.Clone();
            for (int i = 0; i < vertices.Length / 8; i++)
            {
                var o = i * 8; var p = new Vector3(vertices[o], vertices[o + 1], vertices[o + 2]);
                var n = new Vector3(vertices[o + 3], vertices[o + 4], vertices[o + 5]);
                var position = Vector3.Zero; var normal = Vector3.Zero;
                foreach (var weight in _weights[meshIndex][i])
                {
                    position += Vector3.Transform(p, delta[weight.Bone]) * weight.Value;
                    normal += Vector3.TransformNormal(n, normals[weight.Bone]) * weight.Value;
                }
                normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
                vertices[o] = position.X; vertices[o + 1] = position.Y; vertices[o + 2] = position.Z;
                vertices[o + 3] = normal.X; vertices[o + 4] = normal.Y; vertices[o + 5] = normal.Z;
                result.BoundsMinimum = Vector3.Min(result.BoundsMinimum, position); result.BoundsMaximum = Vector3.Max(result.BoundsMaximum, position);
            }
            result.Meshes.Add(new() { Name = source.Name, Vertices = vertices, Indices = source.Indices, BaseColor = source.BaseColor,
                Material = source.Material, UvSets = source.UvSets, CullingMode = source.CullingMode, UseMissingTextureFallback = source.UseMissingTextureFallback,
                Texture = source.Texture, SpecularTexture = source.SpecularTexture, NormalTexture = source.NormalTexture,
                AmbientOcclusionTexture = source.AmbientOcclusionTexture, AuxiliaryTexture = source.AuxiliaryTexture, EyeGlintTexture = source.EyeGlintTexture });
        }
        return result;
    }
}
