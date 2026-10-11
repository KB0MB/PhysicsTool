using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

public static class HelperPoseEvaluation
{
    public static (IReadOnlyList<VisualBoneOverride> Poses, IReadOnlyList<string> Missing) Evaluate(
        HkclService document, IReadOnlyList<VisualModelBone> animated, IReadOnlyList<VisualModelBone> rest)
    {
        var groups = document.GetSidecarDriverGroups();
        var bones = animated.ToDictionary(b => b.Index);
        var local = new Dictionary<int, Matrix4x4>();
        static Matrix4x4 Matrix(VisualModelBone b) => Matrix4x4.CreateScale(b.Scale) * Matrix4x4.CreateFromQuaternion(b.Rotation) * Matrix4x4.CreateTranslation(b.Position);
        foreach (var b in animated)
        {
            var parent = bones.TryGetValue(b.ParentIndex, out var p) ? Matrix(p) : Matrix4x4.Identity;
            if (!Matrix4x4.Invert(parent, out var inverse)) throw new InvalidDataException("Helper model has a singular parent transform.");
            local[b.Index] = Matrix(b) * inverse;
        }
        var overrides = new Dictionary<string, Matrix4x4>(StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<VisualModelBone> current = animated;
        foreach (var group in groups)
        {
            var evaluation = document.EvaluateSidecarDriverGroup(group.Index, current, rest, groups);
            missing.UnionWith(evaluation.MissingDriverBones);
            foreach (var pose in evaluation.Poses.Where(p => p.IsDriven))
                overrides[pose.Name] = Matrix4x4.CreateScale(pose.Scale) * Matrix4x4.CreateFromQuaternion(pose.Rotation) * Matrix4x4.CreateTranslation(pose.Position);
            var worlds = new Dictionary<int, Matrix4x4>(); var visiting = new HashSet<int>();
            Matrix4x4 Visit(VisualModelBone bone)
            {
                if (worlds.TryGetValue(bone.Index, out var world)) return world;
                if (!visiting.Add(bone.Index)) throw new InvalidDataException("Helper model has a parent cycle.");
                world = overrides.TryGetValue(bone.Name, out var driven) ? driven : local[bone.Index] * (bones.TryGetValue(bone.ParentIndex, out var parent) ? Visit(parent) : Matrix4x4.Identity);
                visiting.Remove(bone.Index); return worlds[bone.Index] = world;
            }
            current = animated.Select(b => {
                if (!Matrix4x4.Decompose(Visit(b), out var scale, out var rotation, out var position)) throw new InvalidDataException("Helper pose cannot be decomposed.");
                return new VisualModelBone { Index = b.Index, ParentIndex = b.ParentIndex, Name = b.Name, Position = position, Rotation = rotation, Scale = scale };
            }).ToArray();
        }
        return (current.Select(b => new VisualBoneOverride(b.Name, b.Position, b.Rotation, b.Scale)).ToArray(), missing.OrderBy(n => n).ToArray());
    }
}
