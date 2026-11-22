using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclPreviewSimulator
{
    private sealed record VirtualContactGroup(SimParticle Owner, SimParticle Other, SimParticle? Third,
        Vector3[] Weights, SimParticle Probe);
    private readonly VirtualContactGroup[] _virtualContacts;
    public int VirtualCollisionPointCount => _virtualContacts.Sum(g => g.Weights.Length);
    public long VirtualCollisionCorrectionCount { get; private set; }

    private VirtualContactGroup[] BuildVirtualContacts(ParticlePreviewData source)
    {
        if (source.SolverProfile != ClothSolverProfile.OotExperimental || source.VirtualCollisionData is not { } data) return [];
        if (!float.IsFinite(data.RadiusScale) || data.RadiusScale < 0) throw new InvalidDataException("Invalid virtual collision radius scale.");
        return data.Points.GroupBy(p => (p.ParticleA, p.ParticleB, p.ParticleC, p.Section))
            .OrderBy(g => g.Key.ParticleC < 0).ThenBy(g => g.Key.ParticleA).Select(group =>
            {
                var key = group.Key;
                if (!_particles.TryGetValue(key.ParticleA, out var owner) || !_particles.TryGetValue(key.ParticleB, out var other) ||
                    key.ParticleA == key.ParticleB || key.ParticleC < -1 ||
                    key.ParticleC >= 0 && (!_particles.ContainsKey(key.ParticleC) || key.ParticleC == key.ParticleA || key.ParticleC == key.ParticleB))
                    throw new InvalidDataException("Virtual collision point references a missing particle.");
                var weights = group.Select(p => p.Weights).ToArray();
                if (weights.Any(w => !IsFinite(w) || w.X < 0 || w.Y < 0 || w.Z < 0 || key.ParticleC < 0 && w.Z != 0 || MathF.Abs(w.X + w.Y + w.Z - 1) > 1e-5f))
                    throw new InvalidDataException("Invalid virtual collision weights.");
                float radius = owner.Radius * data.RadiusScale;
                if (!float.IsFinite(radius)) throw new InvalidDataException("Invalid virtual collision radius.");
                return new VirtualContactGroup(owner, other, key.ParticleC < 0 ? null : _particles[key.ParticleC], weights,
                    new SimParticle(new ParticlePreviewPoint { Radius = radius, InverseMass = 1, Mass = 1 }));
            }).ToArray();
    }

    private void ResolveVirtualCollisions(MovingCollider moving, int? pinchPriority = null)
    {
        if (_virtualContacts.Length == 0) return;
        // OoT 0x7101b3a3c0 / 0x7101b4c960: triangle fans precede edge fans.
        // Samples correct only the owner; they are not extra massive particles.
        var shape = moving.Shape;
        if (!shape.VirtualCollisionEnabled) return;
        foreach (var group in _virtualContacts)
        {
            var owner = group.Owner;
            if (owner.Fixed || !CanCollide(owner, shape)) continue;
            // Native pinch kernels gate fans on the owner's retained ordinary
            // contact. Virtual samples themselves do not create pinch contacts.
            if (pinchPriority.HasValue && _ootContacts.TryGetValue(owner.Index, out var cache) &&
                (cache.State.Pinched || cache.State.Priority > pinchPriority.Value)) continue;
            var edge = group.Other.Position - owner.Position;
            var otherEdge = group.Third == null ? Vector3.Zero : group.Third.Position - owner.Position;
            var normal = Vector3.Cross(edge, otherEdge);
            if (group.Third != null)
            {
                if (normal.LengthSquared() <= 1.17549435e-38f) continue;
                normal = Vector3.Normalize(normal);
            }
            foreach (var weights in group.Weights)
            {
                var point = owner.Position + edge * weights.Y + otherEdge * weights.Z;
                group.Probe.Position = group.Probe.PreviousPosition = point;
                if (!TryGetVirtualCollisionContact(group.Probe, shape, out var contact) || contact.Penetration <= 0) continue;
                // Native edge/triangle fans filter with the owner's cached
                // normal, before choosing a radial or face-normal correction.
                if (!AcceptOotContact(owner, shape, contact)) continue;
                var direction = group.Third == null ? contact.Normal :
                    Vector3.Dot(normal, contact.Normal) < 0 ? -normal : normal;
                if (direction == Vector3.Zero) continue;
                ApplyConstraintCorrection(owner, direction * contact.Penetration);
                VirtualCollisionCorrectionCount++;
            }
        }
    }

    private static bool TryGetVirtualCollisionContact(SimParticle probe, ColliderPreviewShape shape, out CollisionContact contact)
    {
        if (shape.Kind is not (ColliderPreviewKind.Sphere or ColliderPreviewKind.Capsule))
            return TryGetCollisionContact(probe, shape, out contact);

        var axis = shape.Kind == ColliderPreviewKind.Sphere ? Vector3.Zero : shape.End - shape.Start;
        float axisLengthSquared = axis.LengthSquared();
        float t = axisLengthSquared > 1.17549435e-38f
            ? Math.Clamp(Vector3.Dot(probe.Position - shape.Start, axis) / axisLengthSquared, 0, 1) : 0;
        var offset = probe.Position - (shape.Start + axis * t);
        float squaredDistance = offset.LengthSquared();
        float distance = MathF.Sqrt(squaredDistance);
        float penetration = Math.Max(0, shape.Radius) + probe.Radius - distance;
        // Native rsqrt returns zero on the axis: edges have no push direction,
        // while triangles still use their face normal with the positive sign.
        var normal = squaredDistance > 1.17549435e-38f ? offset / distance : Vector3.Zero;
        contact = new(normal, penetration, shape.Index);
        return penetration > 0;
    }
}
