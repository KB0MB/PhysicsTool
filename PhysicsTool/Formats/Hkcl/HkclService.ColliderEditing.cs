using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    public IReadOnlyList<ColliderEditRow> GetColliderEditRows(int clothIndex)
    {
        var rows = GetColliderRows(clothIndex);
        if (_bphcl?.NativeDocument is not { } native) return rows;

        foreach (var row in rows)
        {
            var collider = native.Colliders[row.Index];
            if (!TryGetNativeColliderEditBinding(native, clothIndex, collider, out var boneIndex, out var world))
                continue;

            var shape = collider.Shape;
            var start = TransformHkclColliderPoint(world, row.IsPlane
                ? new Vector4(GetPlanePoint(shape.PlaneEquation), 0) : shape.Start);
            var end = row.IsPlane ? start : TransformHkclColliderPoint(world, shape.End);
            row.StartX = start.X; row.StartY = start.Y; row.StartZ = start.Z;
            row.EndX = end.X; row.EndY = end.Y; row.EndZ = end.Z;
            if (row.IsPlane)
            {
                var normal = NormalizePlaneNormal(TransformHkclColliderDirection(world,
                    new Vector3(shape.PlaneEquation.X, shape.PlaneEquation.Y, shape.PlaneEquation.Z)));
                row.PlaneNormalX = normal.X; row.PlaneNormalY = normal.Y; row.PlaneNormalZ = normal.Z;
            }
            row.BoneIndex = boneIndex;
            row.BoneName = native.Skeletons[clothIndex].Bones.First(b => b.Index == boneIndex).Name;
            row.Transform = world;
        }
        return rows;
    }

    public void UpdateColliderEditRows(IEnumerable<ColliderEditRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (_bphcl?.NativeDocument is not { } native)
        {
            UpdateColliderRows(rows);
            return;
        }

        var submitted = rows.ToArray();
        if (submitted.Length == 0) return;
        var previous = _bphcl;
        var offsets = new Dictionary<int, Dictionary<int, Matrix4x4>>();
        var preservedBases = new Dictionary<int, NativeBphclCollider>();
        foreach (var group in submitted.GroupBy(row => row.ClothIndex))
        {
            if (group.Key < 0 || group.Key >= native.Cloths.Count)
                throw new ArgumentException("A collider edit must identify its owning cloth.", nameof(rows));
            var displayed = GetColliderEditRows(group.Key).ToDictionary(row => row.Index);
            var clothOffsets = new Dictionary<int, Matrix4x4>();
            foreach (var row in group)
            {
                if (!displayed.TryGetValue(row.Index, out var current))
                    throw new ArgumentException("The edited collider is not referenced by its cloth.", nameof(rows));
                if (row.BoneIndex != current.BoneIndex)
                    throw new InvalidOperationException("Native BPHCL collider rebinding is not supported.");
                var collider = native.Colliders[row.Index];
                if (!TryGetNativeColliderEditBinding(native, group.Key, collider, out var boneIndex, out _))
                    continue;
                if (!Matrix4x4.Invert(GetBphclBoneWorldTransform(native.Skeletons[group.Key], boneIndex), out var inverse))
                    throw new InvalidOperationException("A collider's attachment bone has a singular transform.");
                clothOffsets.Add(row.Index, row.Transform * inverse);
                if (SameColliderEditBasis(row.Transform, current.Transform))
                    preservedBases[row.Index] = collider;
            }
            offsets.Add(group.Key, clothOffsets);
        }

        try
        {
            // Derive local geometry in the displayed map frame before restoring any
            // deliberately different authored raw basis for non-rotation edits.
            UpdateColliderRows(submitted);
            var updated = _bphcl!.NativeDocument!;
            if (preservedBases.Count > 0)
                updated = NativeBphclWriter.ApplyColliderEdits(updated, preservedBases.ToDictionary(
                    pair => pair.Key, pair => new NativeBphclColliderEdit(pair.Value.AxisX,
                        pair.Value.AxisY, pair.Value.AxisZ, updated.Colliders[pair.Key].Translation)));
            foreach (var (clothIndex, clothOffsets) in offsets)
                updated = NativeBphclParticleEditor.UpdateColliderOffsets(updated, clothIndex, clothOffsets);
            _bphcl = CreateBphclSummary(updated, previous.SourcePath);
        }
        catch
        {
            _bphcl = previous;
            throw;
        }
    }

    private static bool TryGetNativeColliderEditBinding(NativeBphclDocument native, int clothIndex,
        NativeBphclCollider collider, out int boneIndex, out Matrix4x4 world)
    {
        boneIndex = -1;
        world = GetNativeColliderTransform(collider);
        var cloth = native.Cloths.ElementAtOrDefault(clothIndex);
        var skeleton = native.Skeletons.ElementAtOrDefault(clothIndex);
        if (cloth == null || skeleton == null) return false;
        foreach (var simulation in cloth.SimCloths)
        {
            var map = simulation.CollidableTransformMap;
            if (map.TransformSetIndex != 0) continue;
            var slot = simulation.CollidableItemIndices.ToList().IndexOf(collider.ItemIndex);
            if (slot < 0 || slot >= map.TransformIndices.Count || slot >= map.Offsets.Count ||
                map.TransformIndices[slot] >= skeleton.Bones.Count) continue;
            boneIndex = checked((int)map.TransformIndices[slot]);
            world = map.Offsets[slot] * GetBphclBoneWorldTransform(skeleton, boneIndex);
            return true;
        }
        return false;
    }

    private static bool SameColliderEditBasis(Matrix4x4 a, Matrix4x4 b) =>
        MathF.Abs(a.M11 - b.M11) <= 1e-6f && MathF.Abs(a.M12 - b.M12) <= 1e-6f && MathF.Abs(a.M13 - b.M13) <= 1e-6f &&
        MathF.Abs(a.M21 - b.M21) <= 1e-6f && MathF.Abs(a.M22 - b.M22) <= 1e-6f && MathF.Abs(a.M23 - b.M23) <= 1e-6f &&
        MathF.Abs(a.M31 - b.M31) <= 1e-6f && MathF.Abs(a.M32 - b.M32) <= 1e-6f && MathF.Abs(a.M33 - b.M33) <= 1e-6f;
}
