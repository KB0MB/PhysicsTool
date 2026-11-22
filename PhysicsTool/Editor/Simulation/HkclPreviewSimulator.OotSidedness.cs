using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclPreviewSimulator
{
    private OotContactSidedness? _ootSidedness;
    private readonly Dictionary<int, byte> _ootColliderSides = new();

    private void InitializeOotSidedness(ParticlePreviewData source)
    {
        if (_profile != ClothSolverProfile.OotExperimental || source.OotContactSurface is not { } surface) return;
        if (surface.Triangles.Count == 0 || surface.TriangleFlips.Count < (surface.Triangles.Count + 7) / 8 ||
            surface.Triangles.Any(t => !_particles.ContainsKey(t.ParticleA) || !_particles.ContainsKey(t.ParticleB) || !_particles.ContainsKey(t.ParticleC)))
            throw new InvalidDataException("Incomplete OoT contact-normal topology.");
        if (_colliders.Where(c => surface.SetupColliderSlots.Contains(c.CollisionBit))
            .Any(c => _colliders.Count(other => other.Index == c.Index) > 1))
            throw new NotSupportedException("One-sided OoT colliders shared across slots require collider-identity routing.");
        _ootSidedness = new(surface);
        UpdateOotContactNormals();
        foreach (var collider in _colliders.Where(c => surface.SetupColliderSlots.Contains(c.CollisionBit)))
        {
            if (collider.Kind is not (ColliderPreviewKind.Sphere or ColliderPreviewKind.Capsule))
                throw new NotSupportedException("One-sided OoT contacts currently support spheres and capsules only.");
            int negative = 0, zero = 0, positive = 0;
            // Setup includes fixed particles and ignores distance, but honors masks.
            foreach (var particle in _particles.Values)
            {
                if (!CanCollide(particle, collider)) continue;
                TryOotPinchContact(particle, collider, 0, out var contact);
                float dot = Vector3.Dot(contact.Normal, _ootSidedness.Normal(particle.Index));
                if (dot < 0) negative++;
                else if (dot == 0) zero++;
                else positive++;
            }
            _ootColliderSides[collider.CollisionBit] = OotContactSidedness.ChooseSide(negative, zero, positive);
        }
    }

    private void UpdateOotContactNormals()
    {
        if (_ootSidedness != null) _ootSidedness.Update(GetPositions());
        if (_ootWindNormals != null) _ootWindNormals.Update(GetPositions());
    }

    private bool AcceptOotContact(SimParticle particle, ColliderPreviewShape collider, CollisionContact contact) =>
        _ootSidedness == null || OotContactSidedness.Accept(_ootColliderSides.GetValueOrDefault(collider.CollisionBit),
            Vector3.Dot(contact.Normal, _ootSidedness.Normal(particle.Index)));
}
