using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

public static class SelectionMirror
{
    private static Matrix4x4 Reflection(int axis) => axis switch { 0 => Matrix4x4.CreateScale(-1, 1, 1), 1 => Matrix4x4.CreateScale(1, -1, 1), 2 => Matrix4x4.CreateScale(1, 1, -1), _ => throw new ArgumentOutOfRangeException(nameof(axis)) };
    public static void Apply(HkclService doc, int cloth, PhysicsSelectionKind kind, IReadOnlySet<int> selection, int axis, bool aroundCenter, bool followBones)
    {
        if (!doc.HasDocument || doc.IsHelperBoneSidecar || doc.IsHkrg || selection.Count == 0) throw new InvalidOperationException("Select cloth items to mirror first.");
        var reflection = Reflection(axis);
        var points = kind switch
        {
            PhysicsSelectionKind.Particles => doc.GetParticleRows(cloth).Where(p => selection.Contains(p.Index)).Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray(),
            PhysicsSelectionKind.Bones => BoneEditGeometry.WorldTransforms(doc.GetBoneRows(cloth)).Where(b => selection.Contains(b.Key)).Select(b => b.Value.Translation).ToArray(),
            _ => doc.GetColliderRows(cloth).Where(c => selection.Contains(c.Index)).Select(c => (new Vector3(c.StartX, c.StartY, c.StartZ) + new Vector3(c.EndX, c.EndY, c.EndZ)) / 2).ToArray()
        };
        if (points.Length != selection.Count) throw new ArgumentException("The selection contains missing items.");
        var pivot = aroundCenter ? points.Aggregate(Vector3.Zero, (a, b) => a + b) / points.Length : Vector3.Zero;
        var delta = Matrix4x4.CreateTranslation(-pivot) * reflection * Matrix4x4.CreateTranslation(pivot);
        if (kind == PhysicsSelectionKind.Particles) SelectionTransforms.Apply(doc, cloth, kind, selection, delta, followBones);
        else if (kind == PhysicsSelectionKind.Colliders)
            doc.UpdateColliderRows(doc.GetColliderRows(cloth).Where(c => selection.Contains(c.Index)).Select(c => ReflectCollider(c, axis, pivot)).ToArray());
        else
        {
            // Mirror bone positions, retaining authored orientation and scale.
            // Compute selected descendants against the already moved parent.
            var source = doc.GetBoneRows(cloth); var worlds = BoneEditGeometry.WorldTransforms(source);
            var rows = source.ToDictionary(b => b.Index, SelectionClipboard.Clone);
            var after = new Dictionary<int, Matrix4x4>(); var visiting = new HashSet<int>();
            Matrix4x4 Move(int index)
            {
                if (after.TryGetValue(index, out var ready)) return ready;
                if (!visiting.Add(index)) throw new InvalidOperationException("Bone hierarchy contains a cycle.");
                var b = rows[index]; var parent = b.ParentIndex < 0 ? Matrix4x4.Identity : Move(b.ParentIndex);
                var oldParent = b.ParentIndex < 0 ? Matrix4x4.Identity : worlds[b.ParentIndex];
                if (!Matrix4x4.Invert(oldParent, out var inverseOld) || !Matrix4x4.Invert(parent, out var inverseParent)) throw new InvalidOperationException("Bone parent transform is singular.");
                var world = worlds[index] * inverseOld * parent;
                if (selection.Contains(index))
                {
                    world.Translation = Vector3.Transform(worlds[index].Translation, delta);
                    var local = world * inverseParent; b.X = local.M41; b.Y = local.M42; b.Z = local.M43;
                }
                visiting.Remove(index); return after[index] = world;
            }
            foreach (var b in source) Move(b.Index);
            doc.UpdateBoneRows(cloth, rows.Values.Where(b => selection.Contains(b.Index)).ToArray(), followBones);
        }
    }
    internal static ColliderEditRow ReflectCollider(ColliderEditRow source, int axis, Vector3 pivot)
    {
        var row = SelectionClipboard.Clone(source); var r = Reflection(axis);
        var delta = Matrix4x4.CreateTranslation(-pivot) * r * Matrix4x4.CreateTranslation(pivot);
        var a = Vector3.Transform(new(source.StartX, source.StartY, source.StartZ), delta);
        var b = Vector3.Transform(new(source.EndX, source.EndY, source.EndZ), delta);
        row.StartX = a.X; row.StartY = a.Y; row.StartZ = a.Z; row.EndX = b.X; row.EndY = b.Y; row.EndZ = b.Z;
        var n = Vector3.TransformNormal(new(source.PlaneNormalX, source.PlaneNormalY, source.PlaneNormalZ), r);
        row.PlaneNormalX = n.X; row.PlaneNormalY = n.Y; row.PlaneNormalZ = n.Z;
        var m = source.Transform; m.M14 = m.M24 = m.M34 = 0; m.M44 = 1;
        m = r * m * delta;
        m.M14 = source.Transform.M14; m.M24 = source.Transform.M24; m.M34 = source.Transform.M34; m.M44 = source.Transform.M44;
        row.Transform = m; return row;
    }
}
