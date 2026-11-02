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
        if (scale == Vector3.One) return 0;
        var snapshot = CaptureState();
        try
        {
            var geometry = new BoneBranchResizeGeometry(GetBoneRows(clothIndex), selected,
                GetClothFitPreview(clothIndex), GetParticleBoneAttachments(clothIndex), pivot);
            MakeBranchCollidersUnique(clothIndex, geometry.Branch);
            var bindings = CaptureResizeColliderBindings(clothIndex);
            var result = ResizeClothBoneBranchesCore(clothIndex, selected, scale, resizeColliders, pivot, false);
            RebaseResizeColliderBindings(clothIndex, bindings, geometry.Branch, scale, geometry.Pivot, resizeColliders);
            return result;
        }
        catch { RestoreState(snapshot); throw; }
    }

    private int ResizeClothBoneBranchesCore(int clothIndex, IReadOnlySet<int> selected, Vector3 scale,
        bool resizeColliders, Vector3? pivot, bool wholeFile)
    {
        BoneBranchResizeGeometry.Validate(scale);
        ValidateClothBoneFit(clothIndex);
        if (IsBphcl) return ResizeBphclBoneBranches(clothIndex, selected, scale, resizeColliders, pivot, wholeFile);
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
            if (wholeFile) UpdateBoneRowsCore(clothIndex, geometry.BakeBones(scale));
            else UpdateBoneRows(clothIndex, geometry.BakeBones(scale), false);
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

    private int ResizeBphclBoneBranches(int clothIndex, IReadOnlySet<int> selected, Vector3 scale, bool resizeColliders, Vector3? pivot, bool wholeFile)
    {
        var native = _bphcl!.NativeDocument!;
        var sim = native.Cloths[clothIndex].SimCloths[0];
        if (native.GetReferenceItemIndices(sim.DataOffset + 120).Count != 1)
            throw new InvalidOperationException("Branch resizing currently requires a single BPHCL rest pose. Fit cloth to bones supports multiple poses.");
        var preview = GetClothFitPreview(clothIndex);
        var geometry = new BoneBranchResizeGeometry(GetBoneRows(clothIndex), selected, preview, GetParticleBoneAttachments(clothIndex), pivot);
        if (scale == Vector3.One) return 0;
        var colliders = GetColliderRows(clothIndex);
        var snapshot = CaptureState();
        try
        {
            var old = GetParticleRows(clothIndex).ToDictionary(p => p.Index, ToPosition);
            var scales = old.Keys.ToDictionary(i => i, i => BoneBranchResizeGeometry.RadiusScale(geometry.ParticleScale(i, scale)));
            var positions = old.ToDictionary(p => p.Key, p => geometry.ParticlePosition(p.Key, p.Value, scale));
            foreach (var t in preview.Triangles) FitTriangleFrame(positions[t.ParticleA], positions[t.ParticleB], positions[t.ParticleC]);
            var poses = new HkclPreviewSimulator(preview).GetBonePoses();
            var targets = poses.ToDictionary(p => p.Key, p => FitBoneMatrix(
                geometry.Branch.Contains(p.Key) ? geometry.Resize(p.Value.Position, scale) : p.Value.Position,
                p.Value.AxisX, p.Value.AxisY, p.Value.AxisZ));
            if (wholeFile) UpdateBoneRowsCore(clothIndex, geometry.BakeBones(scale));
            else UpdateBoneRows(clothIndex, geometry.BakeBones(scale), false);
            ApplyClothRestShape(clothIndex, preview, old, positions, scales, targets);
            if (resizeColliders) UpdateColliderRows(geometry.BakeColliders(colliders, scale));
            return positions.Count(p => Vector3.DistanceSquared(p.Value, old[p.Key]) > 1e-12f);
        }
        catch { RestoreState(snapshot); throw; }
    }
}
