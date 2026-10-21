using System.Collections;
using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    public int ResizeClothBoneBranch(int clothIndex, int parentIndex, float factor, bool resizeColliders = true)
        => ResizeClothBoneBranches(clothIndex, new HashSet<int> { parentIndex }, new Vector3(factor), resizeColliders);

    public int ResizeClothBoneBranches(int clothIndex, IReadOnlySet<int> selected, Vector3 scale,
        bool resizeColliders = true, Vector3? pivot = null)
    {
        BoneBranchResizeGeometry.Validate(scale);
        ValidateClothBoneFit(clothIndex);
        var simulation = GetFirst(GetValue(GetClothDatas(RequireRoot()).ElementAt(clothIndex), "simClothDatas"));
        if ((GetList(GetValue(simulation, "antiPinchConstraintSets"))?.Count ?? 0) > 0)
            throw new InvalidOperationException("Baked resizing is not yet supported for cloths with anti-pinch constraint sets. No changes were applied.");
        var bones = GetBoneRows(clothIndex);
        var preview = GetParticlePreview(clothIndex);
        var geometry = new BoneBranchResizeGeometry(bones, selected, preview, GetParticleBoneAttachments(clothIndex), pivot);
        if (scale == Vector3.One) return 0;
        var snapshot = CaptureState();
        try
        {
            var old = GetParticleRows(clothIndex).ToDictionary(p => p.Index, ToPosition);
            var scales = old.Keys.ToDictionary(i => i, i => BoneBranchResizeGeometry.RadiusScale(geometry.ParticleScale(i, scale)));
            var positions = old.ToDictionary(p => p.Key, p => geometry.ParticlePosition(p.Key, p.Value, scale));
            foreach (var triangle in preview.Triangles)
                FitTriangleFrame(positions[triangle.ParticleA], positions[triangle.ParticleB], positions[triangle.ParticleC]);
            var poses = new HkclPreviewSimulator(preview).GetBonePoses();
            var targets = poses.ToDictionary(p => p.Key, p => FitBoneMatrix(
                geometry.Branch.Contains(p.Key) ? geometry.Resize(p.Value.Position, scale) : p.Value.Position,
                p.Value.AxisX, p.Value.AxisY, p.Value.AxisZ));
            var colliders = GetColliderRows(clothIndex);
            var sim = simulation;
            var extraPoses = (GetList(GetValue(sim, "simClothPoses")) ?? Array.Empty<object>()).Skip(1)
                .Select(p => (Pose: p, Positions: ((IList)GetValue(p, "positions")!).Cast<Vector4>().ToArray())).ToArray();
            UpdateBoneRows(clothIndex, geometry.BakeBones(scale), false);
            ApplyClothRestShape(clothIndex, preview, old, positions, scales, targets);
            if (geometry.WholeCloth)
                foreach (var set in GetList(GetValue(sim, "staticConstraintSets")) ?? Array.Empty<object>())
                    ScaleConstraintFloat(set, "maxRestPoseHeightSq", MathF.Pow(BoneBranchResizeGeometry.RadiusScale(scale), 2));
            foreach (var extra in extraPoses)
            {
                var values = (IList)GetValue(extra.Pose, "positions")!;
                for (var i = 0; i < extra.Positions.Length; i++)
                {
                    var value = extra.Positions[i];
                    SetListItem(values, i, new Vector4(geometry.ParticlePosition(i, new(value.X, value.Y, value.Z), scale), value.W));
                }
            }
            FitPoseNormals(sim!, preview.Triangles);
            if (resizeColliders) UpdateColliderRows(geometry.BakeColliders(colliders, scale));
            return positions.Count(p => Vector3.DistanceSquared(p.Value, old[p.Key]) > 1e-12f);
        }
        catch { RestoreState(snapshot); throw; }
    }
}
