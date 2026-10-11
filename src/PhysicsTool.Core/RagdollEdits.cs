using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

public static class RagdollEdits
{
    public sealed record ConstraintField(string Id, string Label, bool NonNegative)
    {
        public float? Read(HkrgConstraintEditRow row) => (float?)typeof(HkrgConstraintEditRow).GetProperty(Id)!.GetValue(row);
        internal void Write(HkrgConstraintEditRow row, float value) => typeof(HkrgConstraintEditRow).GetProperty(Id)!.SetValue(row, value);
    }
    public static IReadOnlyList<ConstraintField> ConstraintFields { get; } = new[] { "Twist", "Cone", "Plane", "Hinge" }
        .SelectMany(group => new[] { new ConstraintField(group + "MinAngle", group + " min (rad)", false),
            new ConstraintField(group + "MaxAngle", group + " max (rad)", false), new ConstraintField(group + "Tau", group + " tau", true),
            new ConstraintField(group + "Damping", group + " damping", true) })
        .Append(new("AngularFriction", "Angular friction", true)).ToArray();

    private static void Require(HkclService doc)
    {
        if (!doc.IsHkrg) throw new InvalidOperationException("Open an HKRG ragdoll first.");
    }
    private static void Name(string name) { if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name is required."); }
    private static void Finite(Vector3 v)
    {
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)) throw new ArgumentException("Coordinates must be finite.");
    }
    public static Matrix4x4 Affine(Matrix4x4 matrix)
    {
        matrix.M14 = matrix.M24 = matrix.M34 = 0; matrix.M44 = 1; return matrix;
    }
    public static void Transform(HkclService doc, PhysicsSelectionKind kind, IReadOnlySet<int> selection, Matrix4x4 delta)
    {
        Require(doc); ValidateTransform(delta);
        if (kind == PhysicsSelectionKind.Particles) throw new InvalidOperationException("Select bones or rigid bodies to transform, not constraints.");
        var bodies = doc.GetColliderRows(0);
        var moved = new List<ColliderEditRow>();
        BoneEditRow[] edits = [];
        if (kind == PhysicsSelectionKind.Bones)
        {
            var bones = doc.GetBoneRows(0);
            if (selection.Any(i => !bones.Any(b => b.Index == i))) throw new ArgumentException("The selection contains missing bones.");
            edits = SelectionTransforms.TransformBones(bones, selection, delta);
            var byIndex = edits.ToDictionary(b => b.Index);
            var before = BoneEditGeometry.WorldTransforms(bones);
            var after = BoneEditGeometry.WorldTransforms(bones.Select(b => byIndex.GetValueOrDefault(b.Index, b)).ToArray());
            foreach (var body in bodies)
            {
                if (!before.TryGetValue(body.BoneIndex, out var oldWorld) || !after.TryGetValue(body.BoneIndex, out var newWorld) || oldWorld == newWorld) continue;
                if (!Matrix4x4.Invert(oldWorld, out var inverse)) throw new InvalidOperationException("The bound bone has a singular transform.");
                doc.ApplyHkrgRigidBodyWorldDelta(body, inverse * newWorld); moved.Add(body);
            }
        }
        else
        {
            if (selection.Any(i => !bodies.Any(b => b.Index == i))) throw new ArgumentException("The selection contains missing bodies.");
            foreach (var body in bodies.Where(b => selection.Contains(b.Index)))
            {
                if (body.ShapeType is not ("Capsule" or "Sphere")) throw new InvalidOperationException("This rigid-body shape is inspect-only.");
                doc.ApplyHkrgRigidBodyWorldDelta(body, delta); moved.Add(body);
            }
        }
        var snapshot = doc.CaptureState();
        try
        {
            if (edits.Length > 0) doc.UpdateBoneRows(0, edits, false);
            doc.UpdateColliderRows(moved);
            if (moved.Count > 0) doc.RebuildHkrgMotionSweepsFromTransforms(moved.Select(b => b.Index).ToHashSet());
            if (edits.Length > 0) Retarget(doc, snapshot, true);
        }
        catch { doc.RestoreState(snapshot); throw; }
    }
    public static void ValidateTransform(Matrix4x4 delta)
    {
        Finite(delta.Translation);
        var x = new Vector3(delta.M11, delta.M12, delta.M13); var y = new Vector3(delta.M21, delta.M22, delta.M23); var z = new Vector3(delta.M31, delta.M32, delta.M33);
        Finite(x); Finite(y); Finite(z);
        if (MathF.Abs(x.LengthSquared() - 1) > 1e-4f || MathF.Abs(y.LengthSquared() - 1) > 1e-4f || MathF.Abs(z.LengthSquared() - 1) > 1e-4f ||
            MathF.Abs(Vector3.Dot(x, y)) > 1e-4f || MathF.Abs(Vector3.Dot(x, z)) > 1e-4f || MathF.Abs(Vector3.Dot(y, z)) > 1e-4f ||
            Vector3.Dot(Vector3.Cross(x, y), z) < .9999f || delta.M14 != 0 || delta.M24 != 0 || delta.M34 != 0 || delta.M44 != 1)
            throw new InvalidOperationException("Ragdoll viewport edits support move and rotate. Edit capsule dimensions in the inspector; scaling requires rebuilding mass and inertia.");
    }
    public static void Constraint(HkclService doc, int index, string name, IReadOnlyDictionary<string, float> values)
    {
        Require(doc); Name(name);
        var row = doc.GetHkrgConstraintRows().Single(r => r.Index == index);
        foreach (var (id, value) in values)
        {
            var field = ConstraintFields.SingleOrDefault(f => f.Id == id);
            if (field == null || !field.Read(row).HasValue) throw new ArgumentException("This constraint does not support " + id + ".");
            if (!float.IsFinite(value) || field.NonNegative && value < 0) throw new ArgumentException(field.Label + " must be finite" + (field.NonNegative ? " and nonnegative." : "."));
            field.Write(row, value);
        }
        foreach (var group in new[] { "Twist", "Cone", "Plane", "Hinge" })
        {
            var min = ConstraintFields.Single(f => f.Id == group + "MinAngle").Read(row);
            var max = ConstraintFields.Single(f => f.Id == group + "MaxAngle").Read(row);
            if (min.HasValue && max.HasValue && min > max) throw new ArgumentException(group + " minimum cannot exceed maximum.");
        }
        row.Name = name.Trim(); doc.UpdateHkrgConstraintRows([row]);
    }
    private static void Retarget(HkclService doc, string snapshot, bool joints)
    {
        var source = new HkclService(); source.RestoreState(snapshot);
        if (joints) doc.RetargetHkrgConstraintFramesForTargetPose(source);
        doc.RetargetHkrgSkeletonMappersForCurrentPose(source);
    }
    public static void ModelBone(HkclService doc, int index, string name, Vector3 position, Vector3? radians)
    {
        Require(doc); Name(name); Finite(position); if (radians.HasValue) Finite(radians.Value);
        var row = doc.GetHkrgModelBoneRows().Single(b => b.Index == index);
        var snapshot = doc.CaptureState();
        row.Name = name.Trim(); row.X = position.X; row.Y = position.Y; row.Z = position.Z;
        if (radians.HasValue)
        {
            var q = BoneRotationAngles.FromRadians(radians.Value); row.RotationX = q.X; row.RotationY = q.Y; row.RotationZ = q.Z; row.RotationW = q.W;
        }
        try { doc.UpdateHkrgModelBoneRows([row]); Retarget(doc, snapshot, false); }
        catch { doc.RestoreState(snapshot); throw; }
    }
    public static void AlignBone(HkclService doc, int index, bool modelSkeleton, int targetIndex)
    {
        Require(doc);
        var rows = modelSkeleton ? doc.GetHkrgModelBoneRows() : doc.GetBoneRows(0);
        var targets = modelSkeleton ? doc.GetBoneRows(0) : doc.GetHkrgModelBoneRows();
        var row = rows.Single(b => b.Index == index);
        var world = BoneEditGeometry.WorldTransforms(rows);
        var target = BoneEditGeometry.WorldTransforms(targets)[targetIndex];
        var parent = row.ParentIndex < 0 ? Matrix4x4.Identity : world[row.ParentIndex];
        if (!Matrix4x4.Invert(parent, out var inverse) || !Matrix4x4.Decompose(target * inverse, out _, out var rotation, out var position))
            throw new InvalidOperationException("The target pose cannot be represented by this bone.");
        if (modelSkeleton) ModelBone(doc, index, row.Name, position, BoneRotationAngles.ToRadians(rotation));
        else Bone(doc, index, row.Name, position, BoneRotationAngles.ToRadians(rotation));
    }
    public static void Bone(HkclService doc, int index, string name, Vector3 position, Vector3? radians)
    {
        Require(doc); Name(name); Finite(position); if (radians.HasValue) Finite(radians.Value);
        var bones = doc.GetBoneRows(0); var before = BoneEditGeometry.WorldTransforms(bones);
        var row = bones.Single(b => b.Index == index);
        row.Name = name.Trim(); row.X = position.X; row.Y = position.Y; row.Z = position.Z;
        if (radians.HasValue)
        {
            var q = BoneRotationAngles.FromRadians(radians.Value); row.RotationX = q.X; row.RotationY = q.Y; row.RotationZ = q.Z; row.RotationW = q.W;
        }
        var after = BoneEditGeometry.WorldTransforms(bones);
        var moved = new List<ColliderEditRow>();
        foreach (var body in doc.GetColliderRows(0))
        {
            if (!before.TryGetValue(body.BoneIndex, out var oldWorld) || !after.TryGetValue(body.BoneIndex, out var newWorld) || oldWorld == newWorld) continue;
            if (!Matrix4x4.Invert(oldWorld, out var inverse)) throw new InvalidOperationException("The bound bone has a singular transform.");
            doc.ApplyHkrgRigidBodyWorldDelta(body, inverse * newWorld); moved.Add(body);
        }
        var snapshot = doc.CaptureState();
        try
        {
            doc.UpdateBoneRows(0, [row], false); doc.UpdateColliderRows(moved);
            if (moved.Count > 0) doc.RebuildHkrgMotionSweepsFromTransforms(moved.Select(b => b.Index).ToHashSet());
            Retarget(doc, snapshot, true);
        }
        catch { doc.RestoreState(snapshot); throw; }
    }
    public static void Body(HkclService doc, int index, string name, Vector3 position, Vector3? radians, Vector3 localStart, Vector3 localEnd, float radius)
    {
        Require(doc); Name(name); Finite(position); Finite(localStart); Finite(localEnd); if (radians.HasValue) Finite(radians.Value);
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentException("Radius must be positive and finite.");
        var row = doc.GetColliderRows(0).Single(b => b.Index == index);
        if (row.ShapeType is not ("Capsule" or "Sphere")) throw new InvalidOperationException("This rigid-body shape is inspect-only.");
        var original = Affine(row.Transform);
        if (!Matrix4x4.Invert(original, out var inverse)) throw new InvalidOperationException("The rigid body has a singular transform.");
        var shapeChanged = row.ShapeType == "Capsule" && (localStart != Vector3.Transform(new(row.StartX, row.StartY, row.StartZ), inverse)
            || localEnd != Vector3.Transform(new(row.EndX, row.EndY, row.EndZ), inverse));
        var transform = radians.HasValue ? Matrix4x4.CreateFromQuaternion(BoneRotationAngles.FromRadians(radians.Value)) : original;
        transform.Translation = position;
        if (original != transform) doc.ApplyHkrgRigidBodyWorldDelta(row, inverse * transform);
        row.Name = name.Trim(); row.Radius = radius;
        if (shapeChanged)
        {
            var start = Vector3.Transform(localStart, transform); var end = Vector3.Transform(localEnd, transform);
            row.StartX = start.X; row.StartY = start.Y; row.StartZ = start.Z; row.EndX = end.X; row.EndY = end.Y; row.EndZ = end.Z;
        }
        doc.UpdateColliderRows([row]);
        var indices = new HashSet<int> { index };
        if (shapeChanged) doc.RebuildHkrgCapsuleCentersOfMass(indices);
        if (shapeChanged || original != transform) doc.RebuildHkrgMotionSweepsFromTransforms(indices);
    }
}
