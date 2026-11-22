using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclPreviewSimulator
{
    private OotAerodynamicsData? _ootAerodynamics;
    private OotContactSidedness? _ootWindNormals;

    private void InitializeOotWind(ParticlePreviewData source)
    {
        if (_profile != ClothSolverProfile.OotExperimental || source.OotAerodynamics is not { } data) return;
        data.Validate(_particles.Count);
        if (_particles.Keys.Any(i => i < 0 || i >= data.ParticleCount))
            throw new InvalidDataException("OoT aerodynamic particle indices are not contiguous.");
        _ootAerodynamics = data with { LiftFactors = data.LiftFactors.ToArray(), DragFactors = data.DragFactors.ToArray() };
        if (source.OotWindSurface is { } surface)
        {
            if (surface.TriangleFlips.Count < (surface.Triangles.Count + 7) / 8 || surface.Triangles.Any(t =>
                !_particles.ContainsKey(t.ParticleA) || !_particles.ContainsKey(t.ParticleB) || !_particles.ContainsKey(t.ParticleC)))
                throw new InvalidDataException("Invalid OoT aerodynamic normal topology.");
            _ootWindNormals = new(surface);
            _ootWindNormals.Update(GetPositions());
        }
    }

    private Vector3 OotWindAcceleration(SimParticle particle, Vector3 air, Vector3 velocity, float dt)
    {
        if (!WindEnabled || _ootAerodynamics == null) return Vector3.Zero;
        var acceleration = OotClothWind.Acceleration(_ootAerodynamics, particle.Index, air,
            _currentWindDirection, velocity, _ootWindNormals?.Normal(particle.Index),
            particle.Mass, particle.EffectiveInverseMass, _totalMass);
        // Preview safety only: prevent very low density from overshooting air velocity.
        // The executable's simple-wind action has no such cap.
        float delta = (air - velocity).Length();
        return ClampMagnitude(acceleration, delta / dt);
    }
}
