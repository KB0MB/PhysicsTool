using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

namespace PhysicsTool.Desktop.Rendering;

internal sealed class OotActorPreview
{
    private sealed record Layer(ParticlePreviewData Source, HkclPreviewSimulator Solver, bool Simulates,
        Dictionary<int, ParticlePreviewPoint> Particles, Dictionary<int, BonePreviewPoint> Bones,
        Dictionary<int, ColliderPreviewShape> Colliders);
    private readonly List<Layer> _layers = [];
    private readonly List<HashSet<string>> _lengthPreservingChains = [];
    internal ParticlePreviewData Overlay { get; } = new();
    internal HashSet<string> DrivenNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal List<string> Warnings { get; } = [];
    internal int ClothCount => _layers.Count;
    internal int SimulatedClothCount => _layers.Count(l => l.Simulates);
    internal void Configure(Action<HkclPreviewSimulator> action) { foreach (var layer in _layers) action(layer.Solver); }

    internal OotActorPreview(IEnumerable<OotPhysicsAsset> assets)
    {
        foreach (var asset in assets.Where(a => a.IsCloth))
        {
            var document = new HkclService();
            document.RestoreState("BPHCL:" + Convert.ToBase64String(asset.Bytes));
            int clothCount = document.GetClothSummaries().Count;
            for (int i = 0; i < clothCount; i++)
            {
                var support = document.GetTotkSimulationSupport(i);
                var source = support.Supported ? document.GetTotkSimulationPreview(i) : document.GetParticlePreview(i);
                // Unsupported kernels can still use Reset to pose their decoded
                // skin inputs. Never step them or write their bones to the model.
                source.SolverProfile = ClothSolverProfile.OotExperimental;
                var solver = new HkclPreviewSimulator(source);
                if (!support.Supported) Warnings.Add(document.GetClothName(i) + ": " + string.Join(" ", support.Blockers));
                var particles = new Dictionary<int, ParticlePreviewPoint>();
                var bones = new Dictionary<int, BonePreviewPoint>();
                var colliders = new Dictionary<int, ColliderPreviewShape>();
                foreach (var p in source.Particles)
                {
                    var target = new ParticlePreviewPoint { Index = Overlay.Particles.Count, Position = p.Position, Radius = p.Radius, Fixed = p.Fixed };
                    particles.Add(p.Index, target); Overlay.Particles.Add(target);
                }
                foreach (var b in source.Bones)
                {
                    var target = new BonePreviewPoint { Index = Overlay.Bones.Count, Name = b.Name, Position = b.Position,
                        AxisX = b.AxisX, AxisY = b.AxisY, AxisZ = b.AxisZ, IsSelectable = false };
                    bones.Add(b.Index, target); Overlay.Bones.Add(target);
                    if (support.Supported && solver.DrivenBoneIndices.Contains(b.Index)) DrivenNames.Add(ModelAnimationClip.Normalize(b.Name));
                }
                foreach (var b in source.Bones) bones[b.Index].ParentIndex = bones.GetValueOrDefault(b.ParentIndex)?.Index ?? -1;
                foreach (var link in source.Links) Overlay.Links.Add(new() { ParticleA = particles[link.ParticleA].Index, ParticleB = particles[link.ParticleB].Index, Kind = link.Kind });
                foreach (var t in source.Triangles) Overlay.Triangles.Add(new() { ParticleA = particles[t.ParticleA].Index, ParticleB = particles[t.ParticleB].Index, ParticleC = particles[t.ParticleC].Index });
                foreach (var c in source.Colliders)
                {
                    var target = new ColliderPreviewShape { Index = Overlay.Colliders.Count, BoneIndex = bones.GetValueOrDefault(c.BoneIndex)?.Index ?? -1,
                        Name = c.Name, Kind = c.Kind, Start = c.Start, End = c.End, Radius = c.Radius, EndRadius = c.EndRadius,
                        PlaneNormal = c.PlaneNormal, TransformOrigin = c.TransformOrigin };
                    colliders.Add(c.Index, target); Overlay.Colliders.Add(target);
                }
                _layers.Add(new(source, solver, support.Supported, particles, bones, colliders));
                if (support.Supported && asset.KeepBoneLengthMeshes.Contains(document.GetClothName(i)))
                    _lengthPreservingChains.Add(source.Bones.Where(b => solver.DrivenBoneIndices.Contains(b.Index))
                        .Select(b => ModelAnimationClip.Normalize(b.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase));
            }
        }
    }

    internal IReadOnlyList<VisualBoneOverride> Update(VisualModelData? rest, VisualModelData? pose, bool simulate,
        float seconds, bool reset, bool looped, bool present)
    {
        var reference = (rest?.Bones ?? []).GroupBy(b => ModelAnimationClip.Normalize(b.Name), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var animated = (pose?.Bones ?? []).GroupBy(b => ModelAnimationClip.Normalize(b.Name), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var outputs = new Dictionary<string, VisualBoneOverride>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in _layers)
        {
            var solver = layer.Solver;
            bool run = simulate && layer.Simulates;
            var deltas = new Dictionary<int, Matrix4x4>();
            var bones = layer.Source.Bones.ToDictionary(b => b.Index);
            Matrix4x4 Delta(int index, HashSet<int> visiting)
            {
                if (deltas.TryGetValue(index, out var found)) return found;
                if (!bones.TryGetValue(index, out var b) || !visiting.Add(index)) return Matrix4x4.Identity;
                var name = ModelAnimationClip.Normalize(b.Name);
                return deltas[index] = reference.TryGetValue(name, out var r) && animated.TryGetValue(name, out var p) && Matrix4x4.Invert(Matrix(r), out var inverse)
                    ? inverse * Matrix(p) : Delta(b.ParentIndex, visiting);
            }
            foreach (var b in layer.Source.Bones) Delta(b.Index, []);
            solver.SetInputBoneDeltas(deltas);
            if (!run || reset) solver.Reset();
            else if (looped) solver.RebaseInputPose();
            if (run && seconds > 0) solver.Step(seconds);
            if (!present) continue;
            foreach (var p in solver.GetPositions()) layer.Particles[p.Key].Position = p.Value;
            foreach (var b in solver.GetBonePoses())
            {
                var target = layer.Bones[b.Key]; var value = b.Value;
                target.Position = value.Position; target.AxisX = value.AxisX; target.AxisY = value.AxisY; target.AxisZ = value.AxisZ;
                if (!run || !solver.DrivenBoneIndices.Contains(b.Key) || !reference.TryGetValue(ModelAnimationClip.Normalize(target.Name), out var r)) continue;
                if (!Matrix4x4.Invert(ClothModelBinding.Matrix(bones[b.Key]), out var inverse)) continue;
                var world = Matrix(r) * inverse * new Matrix4x4(value.AxisX.X, value.AxisX.Y, value.AxisX.Z, 0,
                    value.AxisY.X, value.AxisY.Y, value.AxisY.Z, 0, value.AxisZ.X, value.AxisZ.Y, value.AxisZ.Z, 0,
                    value.Position.X, value.Position.Y, value.Position.Z, 1);
                outputs[r.Name] = VisualBoneTransform.Override(r.Name, world);
            }
            foreach (var c in solver.GetColliderPoses())
            {
                var target = layer.Colliders[c.Key]; var value = c.Value;
                target.Start = value.Start; target.End = value.End; target.PlaneNormal = value.PlaneNormal; target.TransformOrigin = value.TransformOrigin;
            }
        }
        if (!present || pose == null) return [];
        if (simulate) PreserveBoneOffsets(pose, outputs, _lengthPreservingChains);
        var overrides = InheritOutputs(pose, outputs);
        var visible = overrides.ToDictionary(b => ModelAnimationClip.Normalize(b.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var b in Overlay.Bones)
            if (visible.TryGetValue(ModelAnimationClip.Normalize(b.Name), out var v))
            {
                var m = Matrix4x4.CreateFromQuaternion(v.Rotation);
                b.Position = v.Position; b.AxisX = new(m.M11, m.M12, m.M13); b.AxisY = new(m.M21, m.M22, m.M23); b.AxisZ = new(m.M31, m.M32, m.M33);
            }
        return overrides;
    }

    // KeepBoneLength is an actor output policy, not a change to the particle solve.
    // Preserve local translations within each driven chain while retaining simulated rotations.
    internal static void PreserveBoneOffsets(VisualModelData pose, Dictionary<string, VisualBoneOverride> outputs,
        IEnumerable<HashSet<string>> chains)
    {
        var bones = pose.Bones.ToDictionary(b => b.Index);
        var preserved = new HashSet<int>();
        foreach (var chain in chains)
            foreach (var bone in pose.Bones)
                if (chain.Contains(ModelAnimationClip.Normalize(bone.Name)) &&
                    bones.TryGetValue(bone.ParentIndex, out var parent) && chain.Contains(ModelAnimationClip.Normalize(parent.Name)))
                    preserved.Add(bone.Index);
        var done = new HashSet<int>();
        var visiting = new HashSet<int>();
        void Visit(VisualModelBone bone)
        {
            if (done.Contains(bone.Index)) return;
            if (!visiting.Add(bone.Index)) throw new InvalidDataException("Cycle in OoT model bone hierarchy.");
            if (preserved.Contains(bone.Index) && bones.TryGetValue(bone.ParentIndex, out var parent))
            {
                Visit(parent);
                if (outputs.TryGetValue(bone.Name, out var value) && outputs.TryGetValue(parent.Name, out var parentValue) &&
                    Matrix4x4.Invert(Matrix(parent), out var inverse))
                {
                    var local = Vector3.Transform(bone.Position, inverse);
                    var corrected = value.WorldMatrix;
                    corrected.Translation = Vector3.Transform(local, parentValue.WorldMatrix);
                    outputs[bone.Name] = value with { Position = corrected.Translation, AffineTransform = corrected };
                }
            }
            visiting.Remove(bone.Index);
            done.Add(bone.Index);
        }
        foreach (var bone in pose.Bones) Visit(bone);
    }

    // Unwritten descendants must inherit a cloth-driven parent, not be pinned to
    // their pre-solve world matrices when the final model is skinned.
    internal static IReadOnlyList<VisualBoneOverride> InheritOutputs(VisualModelData pose, IReadOnlyDictionary<string, VisualBoneOverride> outputs)
    {
        var bones = pose.Bones.ToDictionary(b => b.Index);
        var world = new Dictionary<int, Matrix4x4>();
        Matrix4x4 Visit(VisualModelBone b)
        {
            if (world.TryGetValue(b.Index, out var found)) return found;
            if (outputs.TryGetValue(b.Name, out var value)) return world[b.Index] = value.WorldMatrix;
            var result = Matrix(b);
            if (bones.TryGetValue(b.ParentIndex, out var parent) && Matrix4x4.Invert(Matrix(parent), out var inverse)) result = result * inverse * Visit(parent);
            return world[b.Index] = result;
        }
        return pose.Bones.Select(b =>
        {
            return VisualBoneTransform.Override(b.Name, Visit(b));
        }).ToArray();
    }
    private static Matrix4x4 Matrix(VisualModelBone b) => b.WorldMatrix;
}
