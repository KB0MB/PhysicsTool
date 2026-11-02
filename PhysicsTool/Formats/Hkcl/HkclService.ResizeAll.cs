using System.Collections;
using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    /// <summary>Bakes a file-wide resize about the file origin without changing bone scales or mass.</summary>
    public int ResizeAllCloths(Vector3 scale, bool resizeColliders = true)
    {
        BoneBranchResizeGeometry.Validate(scale);
        var count = GetClothSummaries().Count;
        if (count == 0 || IsHelperBoneSidecar || IsHkrg)
            throw new InvalidOperationException("Open a cloth file first.");
        for (var i = 0; i < count; i++) ValidateClothBoneFit(i);
        if (scale == Vector3.One) return 0;
        var snapshot = CaptureState();
        try
        {
            var bones = Enumerable.Range(0, count).Select(GetBoneRows).ToArray();
            var bindings = Enumerable.Range(0, count).Select(CaptureResizeColliderBindings).ToArray();
            var colliders = Enumerable.Range(0, count).SelectMany(GetColliderRows).DistinctBy(c => c.Index).ToArray();
            var geometry = new BoneBranchResizeGeometry(bones[0], bones[0].Select(b => b.Index).ToHashSet(),
                GetClothFitPreview(0), GetParticleBoneAttachments(0), Vector3.Zero);
            if (resizeColliders) ValidateSharedShapeResize(colliders, scale);
            var moved = 0;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    moved += ResizeClothBoneBranchesCore(i, bones[i].Select(b => b.Index).ToHashSet(),
                        scale, false, Vector3.Zero, true);
                }
                catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
                {
                    throw new InvalidOperationException($"{GetClothName(i)}: {e.Message} The file was not resized.", e);
                }
            }
            // Shared colliders are global objects. Never scale them once per cloth.
            if (resizeColliders) UpdateColliderRows(geometry.BakeColliders(colliders, scale, includeAll: true));
            for (var i = 0; i < count; i++)
                RebaseResizeColliderBindings(i, bindings[i], bones[i].Select(b => b.Index).ToHashSet(),
                    scale, Vector3.Zero, resizeColliders, preserveWorld: !resizeColliders);
            return moved;
        }
        catch { RestoreState(snapshot); throw; }
    }

    private void MakeBranchCollidersUnique(int clothIndex, IReadOnlySet<int> branch)
    {
        var affected = GetColliderRows(clothIndex).Where(c => branch.Contains(c.BoneIndex)).Select(c => c.Index).ToHashSet();
        if (affected.Count == 0) return;
        var usedElsewhere = Enumerable.Range(0, GetClothSummaries().Count).Where(i => i != clothIndex)
            .SelectMany(GetColliderRows).Select(c => c.Index).ToHashSet();
        if (_bphcl?.NativeDocument is { } native)
        {
            // Separate shape objects can be shared even when the collidable is not.
            var otherShapes = native.Colliders.Where(c => !affected.Contains(c.Index) || usedElsewhere.Contains(c.Index))
                .Select(c => c.Shape.DataOffset).ToHashSet();
            affected.RemoveWhere(i => !usedElsewhere.Contains(i) && !otherShapes.Contains(native.Colliders[i].Shape.DataOffset));
            if (affected.Count > 0)
                _bphcl = CreateBphclSummary(NativeBphclParticleEditor.MakeCollidersUnique(native, clothIndex, affected), _bphcl.SourcePath);
            return;
        }
        var root = RequireRoot();
        var globals = GetMutableCollidables(root);
        var otherShapesHkcl = globals.Cast<object>().Where((_, i) => !affected.Contains(i) || usedElsewhere.Contains(i))
            .Select(c => GetValue(c, "shape")).Where(s => s != null).ToHashSet(ReferenceEqualityComparer.Instance);
        var replacements = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach (var index in affected)
        {
            var original = globals[index]!;
            if (!usedElsewhere.Contains(index) && !otherShapesHkcl.Contains(GetValue(original, "shape"))) continue;
            var copy = CloneForCurrentGraph(original);
            globals.Add(copy);
            replacements.Add(original, copy);
        }
        foreach (var sim in GetList(GetValue(GetClothDatas(root)[clothIndex], "simClothDatas")) ?? Array.Empty<object>())
        {
            if (GetValue(sim, "perInstanceCollidables") is not IList references) continue;
            // Replace in place: collision masks and pinching tables refer to these slots.
            for (var slot = 0; slot < references.Count; slot++)
                if (replacements.TryGetValue(references[slot]!, out var copy)) SetListItem(references, slot, copy);
        }
    }

    private sealed record ResizeColliderBinding(int ColliderIndex, int BoneIndex, Matrix4x4 World, Vector3 BonePosition);

    private ResizeColliderBinding[] CaptureResizeColliderBindings(int clothIndex)
    {
        var bones = BoneEditGeometry.WorldTransforms(GetBoneRows(clothIndex));
        return GetStandaloneColliderBindings(clothIndex).Where(b => bones.ContainsKey(b.BoneIndex))
            .Select(b => new ResizeColliderBinding(b.ColliderIndex, b.BoneIndex, b.Offset * bones[b.BoneIndex], bones[b.BoneIndex].Translation)).ToArray();
    }

    private void RebaseResizeColliderBindings(int clothIndex, IReadOnlyList<ResizeColliderBinding> bindings,
        IReadOnlySet<int> branch, Vector3 scale, Vector3 pivot, bool resize, bool preserveWorld = false)
    {
        var bones = BoneEditGeometry.WorldTransforms(GetBoneRows(clothIndex));
        var offsets = new Dictionary<int, Matrix4x4>();
        foreach (var binding in bindings)
        {
            if (!branch.Contains(binding.BoneIndex)) continue;
            var world = binding.World;
            if (branch.Contains(binding.BoneIndex) && !preserveWorld)
                world.Translation = resize ? pivot + (world.Translation - pivot) * scale
                    : world.Translation + bones[binding.BoneIndex].Translation - binding.BonePosition;
            if (!Matrix4x4.Invert(bones[binding.BoneIndex], out var inverse))
                throw new InvalidOperationException("A collider's attachment bone has a singular transform.");
            offsets[binding.ColliderIndex] = world * inverse;
        }
        if (offsets.Count == 0) return;
        if (_bphcl?.NativeDocument is { } native)
        {
            _bphcl = CreateBphclSummary(NativeBphclParticleEditor.UpdateColliderOffsets(native, clothIndex, offsets), _bphcl.SourcePath);
            return;
        }
        var root = RequireRoot();
        var globals = GetCollidables(root);
        foreach (var sim in GetList(GetValue(GetClothDatas(root)[clothIndex], "simClothDatas")) ?? Array.Empty<object>())
        {
            var refs = GetList(GetValue(sim, "perInstanceCollidables"));
            var map = GetValue(sim, "collidableTransformMap");
            if (refs == null || GetValue(map, "offsets") is not IList values) continue;
            for (var slot = 0; slot < refs.Count && slot < values.Count; slot++)
                if (offsets.TryGetValue(IndexOfReference(globals, refs[slot]), out var offset)) SetListItem(values, slot, offset);
        }
    }

    private void ValidateSharedShapeResize(IReadOnlyList<ColliderEditRow> rows, Vector3 scale)
    {
        if (scale.X == scale.Y && scale.Y == scale.Z) return;
        // A shared local shape cannot represent different anisotropic edits in different bases.
        IEnumerable<IGrouping<object, ColliderEditRow>> groups;
        if (_bphcl?.NativeDocument is { } native)
            groups = rows.GroupBy(c => (object)native.Colliders[c.Index].Shape.DataOffset);
        else
        {
            var colliders = GetCollidables(RequireRoot());
            groups = rows.GroupBy(c => GetValue(colliders[c.Index], "shape")!, ReferenceEqualityComparer.Instance);
        }
        if (groups.Any(g => g.Select(c => c.Transform with { Translation = Vector3.Zero }).Distinct().Count() > 1))
            throw new InvalidOperationException("These colliders share a shape with different orientations. Use the same X, Y and Z scale to resize them together.");
    }
}
