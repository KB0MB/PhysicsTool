using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

public enum PhysicsSelectionKind { Particles, Bones, Colliders }

public static class SelectionTransforms
{
    public static void Apply(HkclService doc, int cloth, PhysicsSelectionKind kind,
        IReadOnlySet<int> selection, Matrix4x4 delta, bool followBones)
    {
        if (doc.IsHkrg) { RagdollEdits.Transform(doc, kind, selection, delta); return; }
        if (kind == PhysicsSelectionKind.Particles)
        {
            var rows = doc.GetParticleRows(cloth).Where(p => selection.Contains(p.Index)).ToArray();
            foreach (var p in rows) { var v = Vector3.Transform(new(p.X, p.Y, p.Z), delta); p.X = v.X; p.Y = v.Y; p.Z = v.Z; }
            ParticleEdits.Apply(doc, cloth, rows);
        }
        else if (kind == PhysicsSelectionKind.Bones)
        {
            doc.UpdateBoneRows(cloth, TransformBones(doc.GetBoneRows(cloth), selection, delta), followBones);
        }
        else
            doc.UpdateColliderRows(TransformColliders(doc.GetColliderRows(cloth), selection, delta));
    }

    internal static BoneEditRow[] TransformBones(IReadOnlyList<BoneEditRow> source, IReadOnlySet<int> selection, Matrix4x4 delta)
    {
        var rows = source.Select(b => new BoneEditRow
        {
            Index = b.Index,
            Name = b.Name,
            ParentIndex = b.ParentIndex,
            X = b.X,
            Y = b.Y,
            Z = b.Z,
            RotationX = b.RotationX,
            RotationY = b.RotationY,
            RotationZ = b.RotationZ,
            RotationW = b.RotationW,
            ScaleX = b.ScaleX,
            ScaleY = b.ScaleY,
            ScaleZ = b.ScaleZ,
            TranslationW = b.TranslationW,
            ScaleW = b.ScaleW
        }).ToArray();
        var worlds = BoneEditGeometry.WorldTransforms(rows);
        var byIndex = rows.ToDictionary(b => b.Index);
        bool AncestorSelected(BoneEditRow b)
        {
            while (byIndex.TryGetValue(b.ParentIndex, out var parent)) { if (selection.Contains(parent.Index)) return true; b = parent; }
            return false;
        }
        // Selected descendants inherit their selected parent's transform once.
        var edits = rows.Where(b => selection.Contains(b.Index) && !AncestorSelected(b)).ToArray();
        foreach (var b in edits)
        {
            var parent = b.ParentIndex < 0 ? Matrix4x4.Identity : worlds[b.ParentIndex];
            if (!Matrix4x4.Invert(parent, out var inverse)) throw new InvalidOperationException("The parent bone has a singular transform.");
            var local = worlds[b.Index] * delta * inverse;
            if (!Matrix4x4.Decompose(local, out var scale, out var rotation, out var position))
                throw new InvalidOperationException("This transform cannot be represented by the bone's position, rotation and scale.");
            b.X = position.X; b.Y = position.Y; b.Z = position.Z;
            rotation = Quaternion.Normalize(rotation);
            b.RotationX = rotation.X; b.RotationY = rotation.Y; b.RotationZ = rotation.Z; b.RotationW = rotation.W;
            b.ScaleX = scale.X; b.ScaleY = scale.Y; b.ScaleZ = scale.Z;
        }
        return edits;
    }

    internal static ColliderEditRow[] TransformColliders(IReadOnlyList<ColliderEditRow> source, IReadOnlySet<int> selection, Matrix4x4 delta)
    {
        var rows = source.Where(c => selection.Contains(c.Index)).Select(c => new ColliderEditRow
        {
            Index = c.Index,
            ClothIndex = c.ClothIndex,
            Name = c.Name,
            ShapeType = c.ShapeType,
            BoneIndex = c.BoneIndex,
            BoneName = c.BoneName,
            StartX = c.StartX,
            StartY = c.StartY,
            StartZ = c.StartZ,
            EndX = c.EndX,
            EndY = c.EndY,
            EndZ = c.EndZ,
            Radius = c.Radius,
            PlaneNormalX = c.PlaneNormalX,
            PlaneNormalY = c.PlaneNormalY,
            PlaneNormalZ = c.PlaneNormalZ,
            Transform = c.Transform
        }).ToArray();
        var scale = Math.Max(Vector3.TransformNormal(Vector3.UnitX, delta).Length(),
            Math.Max(Vector3.TransformNormal(Vector3.UnitY, delta).Length(), Vector3.TransformNormal(Vector3.UnitZ, delta).Length()));
        if (!Matrix4x4.Invert(delta, out var inverse)) throw new InvalidOperationException("Collider transform is singular.");
        if (!Matrix4x4.Decompose(delta, out _, out var rotation, out _)) throw new InvalidOperationException("Collider transform cannot be decomposed.");
        foreach (var c in rows)
        {
            var start = Vector3.Transform(new(c.StartX, c.StartY, c.StartZ), delta);
            var end = Vector3.Transform(new(c.EndX, c.EndY, c.EndZ), delta);
            c.StartX = start.X; c.StartY = start.Y; c.StartZ = start.Z;
            c.EndX = end.X; c.EndY = end.Y; c.EndZ = end.Z; c.Radius *= scale;
            var normal = Vector3.TransformNormal(new(c.PlaneNormalX, c.PlaneNormalY, c.PlaneNormalZ), Matrix4x4.Transpose(inverse));
            if (normal.LengthSquared() > 1e-12f) normal = Vector3.Normalize(normal);
            c.PlaneNormalX = normal.X; c.PlaneNormalY = normal.Y; c.PlaneNormalZ = normal.Z;
            // Move the rigid origin too; otherwise animation rotates the moved shape around its old pivot.
            var stored = c.Transform;
            var origin = Vector3.Transform(new(stored.M41, stored.M42, stored.M43), delta);
            var rigid = stored; rigid.M14 = rigid.M24 = rigid.M34 = 0; rigid.M44 = 1;
            rigid *= Matrix4x4.CreateFromQuaternion(rotation); rigid.Translation = origin;
            rigid.M14 = stored.M14; rigid.M24 = stored.M24; rigid.M34 = stored.M34; rigid.M44 = stored.M44;
            c.Transform = rigid;
        }
        return rows;
    }
}
