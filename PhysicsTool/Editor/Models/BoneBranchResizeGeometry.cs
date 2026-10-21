using System.Numerics;

namespace HKCLTool;

internal sealed class BoneBranchResizeGeometry
{
    private readonly IReadOnlyList<BoneEditRow> _bones;
    private readonly Dictionary<int, Matrix4x4> _worlds;
    private readonly Dictionary<int, float> _weights;
    internal HashSet<int> Branch { get; }
    internal bool WholeCloth { get; }
    internal Vector3 Pivot { get; }

    internal BoneBranchResizeGeometry(IReadOnlyList<BoneEditRow> bones, IReadOnlySet<int> selected,
        ParticlePreviewData preview, IReadOnlyList<ParticlePreviewAttachment> attachments, Vector3? pivot = null)
    {
        if (selected.Count == 0 || selected.Any(i => !bones.Any(b => b.Index == i)))
            throw new ArgumentException("Select a parent bone to resize.");
        _bones = bones; _worlds = BoneEditGeometry.WorldTransforms(bones);
        Pivot = pivot ?? selected.Aggregate(Vector3.Zero, (p, i) => p + _worlds[i].Translation) / selected.Count;
        Branch = selected.ToHashSet();
        while (true)
        {
            var added = false;
            foreach (var bone in bones) if (Branch.Contains(bone.ParentIndex)) added |= Branch.Add(bone.Index);
            if (!added) break;
        }
        if (!preview.BoneBindings.Any(b => Branch.Contains(b.BoneIndex)))
            throw new InvalidOperationException("This branch has no simulation bones. Select a simulation bone or one of its parents.");
        WholeCloth = preview.BoneBindings.All(b => Branch.Contains(b.BoneIndex));
        var weights = preview.Particles.ToDictionary(p => p.Index, _ => new List<float>());
        foreach (var binding in preview.BoneBindings)
            foreach (var particle in new[] { binding.ParticleA, binding.ParticleB, binding.ParticleC })
                weights[particle].Add(Branch.Contains(binding.BoneIndex) ? 1 : 0);
        foreach (var attachment in attachments)
        {
            if (weights[attachment.ParticleIndex].Count != 0) continue;
            var valid = attachment.BoneWeights.Where(w => float.IsFinite(w.Weight) && w.Weight > 0).ToArray();
            var total = valid.Sum(w => w.Weight);
            if (total > 0) weights[attachment.ParticleIndex].Add(valid.Where(w => Branch.Contains(w.BoneIndex)).Sum(w => w.Weight) / total);
        }
        // Shared output-triangle vertices blend once at branch boundaries.
        _weights = weights.ToDictionary(p => p.Key, p => WholeCloth ? 1 : p.Value.Count > 0 ? p.Value.Average() : 0);
    }
    internal static void Validate(Vector3 scale)
    {
        if (new[] { scale.X, scale.Y, scale.Z }.Any(s => !float.IsFinite(s) || s < .001f || s > 1000f))
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be between 0.001 and 1000.");
    }
    internal Vector3 Resize(Vector3 point, Vector3 scale) => Pivot + (point - Pivot) * scale;
    internal Vector3 ParticleScale(int particle, Vector3 scale) => Vector3.Lerp(Vector3.One, scale, _weights[particle]);
    internal Vector3 ParticlePosition(int particle, Vector3 position, Vector3 scale) => Resize(position, ParticleScale(particle, scale));
    internal static float RadiusScale(Vector3 scale) => Math.Max(scale.X, Math.Max(scale.Y, scale.Z));

    internal BoneEditRow[] BakeBones(Vector3 scale)
    {
        Validate(scale);
        return _bones.Where(b => Branch.Contains(b.Index)).Select(b => {
            var parent = b.ParentIndex < 0 ? Matrix4x4.Identity : _worlds[b.ParentIndex];
            if (Branch.Contains(b.ParentIndex)) parent.Translation = Resize(parent.Translation, scale);
            if (!Matrix4x4.Invert(parent, out var inverse)) throw new InvalidOperationException("The parent bone has a singular transform.");
            var point = Vector3.Transform(Resize(_worlds[b.Index].Translation, scale), inverse);
            return new BoneEditRow {
                Index = b.Index, Name = b.Name, ParentIndex = b.ParentIndex,
                X = point.X, Y = point.Y, Z = point.Z, TranslationW = b.TranslationW,
                RotationX = b.RotationX, RotationY = b.RotationY, RotationZ = b.RotationZ, RotationW = b.RotationW,
                ScaleX = b.ScaleX, ScaleY = b.ScaleY, ScaleZ = b.ScaleZ, ScaleW = b.ScaleW
            };
        }).ToArray();
    }
    internal ColliderEditRow[] BakeColliders(IEnumerable<ColliderEditRow> colliders, Vector3 scale)
    {
        return colliders.Where(c => Branch.Contains(c.BoneIndex)).Select(c => {
            var start = Resize(new(c.StartX, c.StartY, c.StartZ), scale);
            var end = Resize(new(c.EndX, c.EndY, c.EndZ), scale);
            var transform = c.Transform; transform.Translation = Resize(transform.Translation, scale);
            var normal = new Vector3(c.PlaneNormalX, c.PlaneNormalY, c.PlaneNormalZ) / scale;
            if (normal.LengthSquared() > 1e-12f) normal = Vector3.Normalize(normal);
            return new ColliderEditRow {
                Index = c.Index, ClothIndex = c.ClothIndex, Name = c.Name, ShapeType = c.ShapeType,
                BoneIndex = c.BoneIndex, BoneName = c.BoneName,
                StartX = start.X, StartY = start.Y, StartZ = start.Z, EndX = end.X, EndY = end.Y, EndZ = end.Z,
                Radius = c.Radius * RadiusScale(scale), Transform = transform,
                PlaneNormalX = normal.X, PlaneNormalY = normal.Y, PlaneNormalZ = normal.Z
            };
        }).ToArray();
    }
}
