using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclPreviewSimulator
{
    private readonly List<OotRecoveryEntry> _ootRecovery = new();
    private readonly Dictionary<int, OotContactCache> _ootContacts = new();
    private bool _ootRecoveryEnabled;

    private sealed class OotRecoveryEntry(ParticlePreviewAntiPinch definition)
    {
        internal readonly ParticlePreviewAntiPinch Definition = definition;
        internal readonly OotPinchRecovery Recovery = new(definition.ToAnimationPeriod,
            definition.ToSimulationPeriod, definition.ToSimulationMaxDistance);
        internal Vector3 Reference;
    }

    private sealed class OotContactCache
    {
        internal OotPinchContactState State;
        internal CollisionContact Contact;
        internal Vector3 SurfacePoint;
        internal Vector3 SurfaceVelocity;
    }

    private void InitializeOotPinch(ParticlePreviewData source)
    {
        if (_profile != ClothSolverProfile.OotExperimental || source.AntiPinchRecovery.Count == 0) return;
        if (_colliders.Where(IsPinchCollider).Any(c => c.Kind is not (ColliderPreviewKind.Sphere or ColliderPreviewKind.Capsule)))
            throw new NotSupportedException("Unsupported OoT pinch collision configuration.");
        foreach (var settings in _colliderPinching.Values.Where(p => p.Enabled))
            if (settings.Priority is < 0 or > 127 || !float.IsFinite(settings.Radius) || settings.Radius < 0)
                throw new InvalidDataException("Invalid OoT pinch settings.");
        foreach (var definition in source.AntiPinchRecovery)
        {
            if (!_particles.TryGetValue(definition.ParticleIndex, out var particle) || particle.Fixed ||
                definition.ReferenceBoneWeights.Count == 0 || definition.ReferenceBoneWeights.Any(w =>
                    !w.ReferencePosition.HasValue || !IsFinite(w.ReferencePosition.Value) || !float.IsFinite(w.Weight) || w.Weight <= 0) ||
                !float.IsFinite(definition.ToAnimationPeriod) || definition.ToAnimationPeriod < 0 ||
                !float.IsFinite(definition.ToSimulationPeriod) || definition.ToSimulationPeriod < 0 ||
                !float.IsFinite(definition.ToSimulationMaxDistance) || definition.ToSimulationMaxDistance < 0)
                throw new InvalidDataException("Invalid OoT anti-pinch recovery entry.");
            _ootRecovery.Add(new(definition));
        }
        foreach (int index in _pinchParticles)
            if (_particles.TryGetValue(index, out var particle) && !particle.Fixed) _ootContacts.Add(index, new());
        _ootRecoveryEnabled = source.PinchDetectionEnabled;
    }

    private void SampleOotPinchReferences()
    {
        if (!_ootRecoveryEnabled) return;
        foreach (var entry in _ootRecovery)
        {
            var reference = Vector3.Zero;
            float total = 0;
            foreach (var weight in entry.Definition.ReferenceBoneWeights)
            {
                var position = weight.ReferencePosition!.Value;
                reference += (_inputBoneDeltas.TryGetValue(weight.BoneIndex, out var delta)
                    ? Vector3.Transform(position, delta) : position) * weight.Weight;
                total += weight.Weight;
            }
            entry.Reference = reference / total;
        }
    }

    private void ResolveOotPinchCollisions(IReadOnlyList<MovingCollider> colliders, float dt)
    {
        foreach (var cache in _ootContacts.Values) cache.State.Reset();
        foreach (var moving in colliders)
        {
            if (!IsPinchCollider(moving.Shape)) continue;
            var settings = _colliderPinching[moving.Shape.CollisionBit];
            foreach (var particle in _particles.Values)
            {
                if (particle.Fixed || !CanCollide(particle, moving.Shape)) continue;
                if (!_ootContacts.TryGetValue(particle.Index, out var cache))
                {
                    ResolveOrdinaryContact(particle, moving, dt);
                    continue;
                }
                if (!TryOotPinchContact(particle, moving.Shape, settings.Radius, out var contact) ||
                    !AcceptOotContact(particle, moving.Shape, contact)) continue;
                if (cache.State.Add(settings.Priority, contact.Penetration))
                {
                    cache.Contact = contact;
                    cache.SurfacePoint = particle.Position + contact.Normal * (contact.Penetration - particle.Radius);
                    var pivot = moving.Shape.TransformOrigin ?? (_inputBoneDeltas.TryGetValue(moving.Shape.BoneIndex, out var delta)
                        ? Vector3.Transform(moving.Motion.RestPivot, delta) : moving.Motion.RestPivot);
                    cache.SurfaceVelocity = moving.Motion.LinearVelocity +
                        Vector3.Cross(moving.Motion.AngularVelocity, cache.SurfacePoint - pivot);
                }
            }
            ResolveVirtualCollisions(moving, settings.Priority);
        }
        foreach (var pair in _ootContacts)
            if (pair.Value.State.Valid) ApplyOotRetainedContact(_particles[pair.Key], pair.Value, dt);
        foreach (var moving in colliders)
            if (!IsPinchCollider(moving.Shape))
            {
                foreach (var particle in _particles.Values)
                    if (!particle.Fixed) ResolveOrdinaryContact(particle, moving, dt);
                ResolveVirtualCollisions(moving);
            }
        foreach (var entry in _ootRecovery)
        {
            var particle = _particles[entry.Definition.ParticleIndex];
            bool pinched = _ootContacts.TryGetValue(particle.Index, out var cache) && cache.State.Pinched;
            particle.Position = entry.Recovery.Apply(pinched, particle.Position, entry.Reference);
        }
    }

    private static void ApplyOotRetainedContact(SimParticle particle, OotContactCache cache, float dt)
    {
        var normal = cache.Contact.Normal;
        // Virtual fans may have moved the owner since detection. Native recovery
        // projects onto the cached plane, including a negative correction.
        float correction = particle.Radius - Vector3.Dot(particle.Position - cache.SurfacePoint, normal);
        particle.Position += normal * correction;
        var relative = particle.Position - particle.PreviousPosition - cache.SurfaceVelocity * dt;
        particle.PreviousPosition += (relative - normal * Vector3.Dot(relative, normal)) * particle.Friction;
    }

    private static bool TryOotPinchContact(SimParticle particle, ColliderPreviewShape collider,
        float margin, out CollisionContact contact)
    {
        var center = collider.Start;
        if (collider.Kind == ColliderPreviewKind.Capsule)
        {
            var axis = collider.End - center;
            float squared = axis.LengthSquared();
            if (squared > 0) center += axis * Math.Clamp(Vector3.Dot(particle.Position - center, axis) / squared, 0, 1);
        }
        var offset = particle.Position - center;
        float lengthSquared = offset.LengthSquared();
        // Native zero/subnormal rsqrt yields no radial direction, not UnitY.
        float inverse = lengthSquared <= 1.17549435e-38f ? 0 : 1 / MathF.Sqrt(lengthSquared);
        float penetration = collider.Radius + particle.Radius - lengthSquared * inverse;
        contact = new(offset * inverse, penetration, collider.Index);
        return penetration + margin > 0;
    }
}
