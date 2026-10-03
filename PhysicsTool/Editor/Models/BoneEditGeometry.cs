using System.Numerics;

namespace HKCLTool;

internal static class BoneEditGeometry
{
    internal static Dictionary<int, Matrix4x4> WorldTransforms(IReadOnlyList<BoneEditRow> rows)
    {
        var bones = rows.ToDictionary(b => b.Index);
        var result = new Dictionary<int, Matrix4x4>();
        var visiting = new HashSet<int>();
        Matrix4x4 Resolve(int index)
        {
            if (result.TryGetValue(index, out var cached)) return cached;
            if (!bones.TryGetValue(index, out var b)) throw new ArgumentException("Bone parent does not exist.");
            if (!visiting.Add(index)) throw new ArgumentException("Bone parenting would create a cycle.");
            if (b.ParentIndex < -1) throw new ArgumentException("Bone parent must be -1 or an existing bone.");
            var values = new[] { b.X, b.Y, b.Z, b.RotationX, b.RotationY, b.RotationZ, b.RotationW, b.ScaleX, b.ScaleY, b.ScaleZ };
            if (values.Any(v => !float.IsFinite(v))) throw new ArgumentException("Bone transforms must be finite.");
            var rotation = new Quaternion(b.RotationX, b.RotationY, b.RotationZ, b.RotationW);
            rotation = rotation.LengthSquared() < 0.000001f ? Quaternion.Identity : Quaternion.Normalize(rotation);
            var local = Matrix4x4.CreateScale(b.ScaleX, b.ScaleY, b.ScaleZ) * Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(b.X, b.Y, b.Z);
            var world = b.ParentIndex < 0 ? local : local * Resolve(b.ParentIndex);
            visiting.Remove(index);
            return result[index] = world;
        }
        foreach (var b in rows) Resolve(b.Index);
        return result;
    }

    internal static IReadOnlyList<ParticleEditRow> FollowParticles(IReadOnlyList<BoneEditRow> before,
        IReadOnlyList<BoneEditRow> after, IEnumerable<ParticleEditRow> particles,
        IReadOnlyList<ParticlePreviewAttachment> attachments)
    {
        var oldWorld = WorldTransforms(before);
        var newWorld = WorldTransforms(after);
        var deltas = new Dictionary<int, Matrix4x4>();
        foreach (var (index, oldBone) in oldWorld)
        {
            if (!newWorld.TryGetValue(index, out var newBone) || oldBone == newBone) continue;
            if (!Matrix4x4.Invert(oldBone, out var inverse))
                throw new ArgumentException("Cannot move particles from a singular bone transform.");
            deltas[index] = inverse * newBone;
        }
        var bindings = attachments.ToDictionary(a => a.ParticleIndex, a => a.BoneWeights);
        var result = new List<ParticleEditRow>();
        foreach (var p in particles)
        {
            if (!bindings.TryGetValue(p.Index, out var weights)) continue;
            var position = new Vector3(p.X, p.Y, p.Z);
            var displacement = Vector3.Zero;
            var total = 0f;
            foreach (var weight in weights)
            {
                if (!float.IsFinite(weight.Weight) || weight.Weight <= 0 || !oldWorld.ContainsKey(weight.BoneIndex)) continue;
                total += weight.Weight;
                if (!deltas.TryGetValue(weight.BoneIndex, out var delta)) continue;
                var reference = weight.ReferencePosition ?? position;
                displacement += (Vector3.Transform(reference, delta) - reference) * weight.Weight;
            }
            if (total <= 0 || displacement.LengthSquared() < 1e-16f) continue;
            position += displacement / total;
            if (!float.IsFinite(position.LengthSquared())) throw new ArgumentException("Bone edit produced a non-finite particle position.");
            result.Add(new ParticleEditRow { Index = p.Index, X = position.X, Y = position.Y, Z = position.Z,
                W = p.W, Fixed = p.Fixed, Mass = p.Mass, InverseMass = p.InverseMass,
                Radius = p.Radius, Friction = p.Friction, CollisionMask = p.CollisionMask });
        }
        return result;
    }

    internal static IReadOnlyList<ColliderEditRow> FollowColliders(IReadOnlyList<BoneEditRow> before,
        IReadOnlyList<BoneEditRow> after, IEnumerable<ColliderEditRow> colliders)
    {
        var oldWorld = WorldTransforms(before);
        var newWorld = WorldTransforms(after);
        var changed = new List<ColliderEditRow>();
        foreach (var c in colliders)
        {
            if (!oldWorld.TryGetValue(c.BoneIndex, out var oldBone) ||
                !newWorld.TryGetValue(c.BoneIndex, out var newBone) || oldBone == newBone) continue;
            if (!Matrix4x4.Invert(oldBone, out var inverse))
                throw new ArgumentException("Cannot move a collider from a singular bone transform.");
            var delta = inverse * newBone;
            var x = new Vector3(delta.M11, delta.M12, delta.M13);
            var y = new Vector3(delta.M21, delta.M22, delta.M23);
            var z = new Vector3(delta.M31, delta.M32, delta.M33);
            if (MathF.Abs(x.LengthSquared() - 1) > 0.001f || MathF.Abs(y.LengthSquared() - 1) > 0.001f ||
                MathF.Abs(z.LengthSquared() - 1) > 0.001f || MathF.Abs(Vector3.Dot(x, y)) > 0.001f ||
                MathF.Abs(Vector3.Dot(x, z)) > 0.001f || MathF.Abs(Vector3.Dot(y, z)) > 0.001f ||
                !float.IsFinite(delta.GetDeterminant()) || delta.GetDeterminant() < 0)
                throw new ArgumentException("Scaling or reflecting a collider-bound bone hierarchy is not supported. Edit collider geometry separately.");
            if (!Matrix4x4.Invert(delta, out var inverseDelta))
                throw new ArgumentException("Cannot attach a collider to a singular bone transform.");
            var start = Vector3.Transform(new(c.StartX, c.StartY, c.StartZ), delta);
            var end = Vector3.Transform(new(c.EndX, c.EndY, c.EndZ), delta);
            var normal = Vector3.TransformNormal(new(c.PlaneNormalX, c.PlaneNormalY, c.PlaneNormalZ), Matrix4x4.Transpose(inverseDelta));
            if (normal.LengthSquared() > 0.000001f) normal = Vector3.Normalize(normal);
            // Havok's fourth column is padding, not a homogeneous coordinate.
            var transform = c.Transform;
            transform.M14 = transform.M24 = transform.M34 = 0; transform.M44 = 1;
            transform *= delta;
            transform.M14 = c.Transform.M14; transform.M24 = c.Transform.M24;
            transform.M34 = c.Transform.M34; transform.M44 = c.Transform.M44;
            changed.Add(new ColliderEditRow { Index = c.Index, ClothIndex = c.ClothIndex, Name = c.Name,
                ShapeType = c.ShapeType, BoneIndex = c.BoneIndex, BoneName = c.BoneName,
                StartX = start.X, StartY = start.Y, StartZ = start.Z, EndX = end.X, EndY = end.Y, EndZ = end.Z,
                Radius = c.Radius, PlaneNormalX = normal.X, PlaneNormalY = normal.Y, PlaneNormalZ = normal.Z,
                Transform = transform });
        }
        return changed;
    }
}
