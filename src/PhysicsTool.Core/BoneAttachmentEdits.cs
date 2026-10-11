using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

public static class BoneAttachmentEdits
{
    public static void Apply(HkclService document, int cloth, PhysicsSelectionKind kind,
        IReadOnlySet<int> selection, int anchorIndex, int targetBone, bool followBones)
    {
        if (!selection.Contains(anchorIndex)) throw new ArgumentException("Select an anchor item first.");
        var bones = document.GetBoneRows(cloth);
        var target = bones.SingleOrDefault(b => b.Index == targetBone) ?? throw new ArgumentException("The target bone does not exist.");
        var world = BoneEditGeometry.WorldTransforms(bones);
        var position = world[targetBone].Translation;
        if (kind == PhysicsSelectionKind.Particles)
        {
            var rows = document.GetParticleRows(cloth).Where(p => selection.Contains(p.Index)).ToArray();
            if (rows.Length != selection.Count) throw new ArgumentException("A selected particle no longer exists.");
            var anchor = rows.Single(p => p.Index == anchorIndex);
            var delta = position - new Vector3(anchor.X, anchor.Y, anchor.Z);
            foreach (var p in rows) { p.X += delta.X; p.Y += delta.Y; p.Z += delta.Z; }
            // Original Attach behavior: snap the group and fix its active anchor, retaining authored skin weights.
            anchor.Fixed = true; anchor.Mass = 0; anchor.InverseMass = 0; anchor.CollisionMask = 0;
            ParticleEdits.Apply(document, cloth, rows);
        }
        else if (kind == PhysicsSelectionKind.Bones)
        {
            var ancestor = targetBone;
            while (ancestor >= 0)
            {
                if (selection.Contains(ancestor)) throw new ArgumentException("Choose a target outside the selected bones and their children.");
                ancestor = bones.Single(b => b.Index == ancestor).ParentIndex;
            }
            if (selection.Any(i => !world.ContainsKey(i))) throw new ArgumentException("A selected bone no longer exists.");
            SelectionTransforms.Apply(document, cloth, kind, selection,
                Matrix4x4.CreateTranslation(position - world[anchorIndex].Translation), followBones);
        }
        else
        {
            if (document.IsBphcl) throw new InvalidOperationException("Native BPHCL collider rebinding is not available yet. The existing bindings were not changed.");
            var rows = document.GetColliderRows(cloth);
            var anchor = rows.Single(c => c.Index == anchorIndex);
            if (rows.Count(c => selection.Contains(c.Index)) != selection.Count) throw new ArgumentException("A selected collider no longer exists.");
            var center = new Vector3(anchor.StartX + anchor.EndX, anchor.StartY + anchor.EndY, anchor.StartZ + anchor.EndZ) * .5f;
            var moved = SelectionTransforms.TransformColliders(rows, selection, Matrix4x4.CreateTranslation(position - center));
            foreach (var collider in moved) { collider.BoneIndex = target.Index; collider.BoneName = target.Name; }
            document.UpdateColliderRows(moved);
        }
    }
}
