using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    private IReadOnlyList<ParticlePreviewAntiPinch> BuildOotPinchRecovery(int clothIndex)
    {
        var document = _bphcl!.NativeDocument!;
        var cloth = document.Cloths[clothIndex];
        if (!cloth.SimCloths.Any(s => s.ConstraintSets.Any(c => c.IsAntiPinch))) return [];
        var sets = document.ReadAntiPinchSets(clothIndex);
        var sim = cloth.SimCloths.Single();
        var config = cloth.Operators.Single(o => o.ClassName == "hclSimulateOperator").SimulateConfigs.Single();
        if (!config.ConstraintExecution.Contains(-1) ||
            sets.Any(s => config.ConstraintExecution.Contains(s.Index)))
            throw new NotSupportedException("Anti-pinch requires a collision schedule entry and separate recovery sets.");
        if (sim.Runtime.LandscapeCollisionEnabled)
            throw new NotSupportedException("OoT pinch recovery with landscape contacts is not implemented yet.");
        if (sim.Runtime.CollidablePinchingData.Count != sim.CollidableItemIndices.Count ||
            sim.Runtime.PerParticlePinchDetectionEnabledFlags.Count != sim.Particles.Count)
            throw new InvalidDataException("Incomplete OoT pinch particle/collider flags.");
        if (sim.Runtime.PerParticlePinchDetectionEnabledFlags.Where((enabled, i) => enabled &&
                (i < sim.Runtime.MinPinchedParticleIndex || i > sim.Runtime.MaxPinchedParticleIndex)).Any())
            throw new InvalidDataException("Pinch particle flags exceed the native cache range.");
        BuildOotContactSurface(clothIndex);
        for (int i = 0; i < sim.CollidableItemIndices.Count; i++)
        {
            var pinch = sim.Runtime.CollidablePinchingData[i];
            if (!pinch.Enabled) continue;
            var collider = document.Colliders.Single(c => c.ItemIndex == sim.CollidableItemIndices[i]);
            if (collider.Shape.TypeName is not ("hclSphereShape" or "hclCapsuleShape"))
                throw new NotSupportedException("OoT pinch recovery currently supports sphere and capsule pinch colliders only.");
            if (pinch.Priority < 0 || !float.IsFinite(pinch.Radius) || pinch.Radius < 0)
                throw new InvalidDataException("Invalid OoT pinch priority or detection radius.");
        }
        var buffers = BuildOotReferenceBuffers(clothIndex);
        var result = new List<ParticlePreviewAntiPinch>();
        foreach (var set in sets)
        {
            foreach (var entry in set.Particles)
            {
                if (sim.Particles[entry.ParticleIndex].Fixed || sim.Particles[entry.ParticleIndex].InverseMass == 0 ||
                    entry.ParticleIndex < sim.Runtime.MinPinchedParticleIndex || entry.ParticleIndex > sim.Runtime.MaxPinchedParticleIndex)
                    throw new NotSupportedException("Anti-pinch entry is fixed or outside the native pinch particle range.");
                var weights = buffers.Resolve(set.ReferenceBufferIndex, entry.ReferenceVertex);
                result.Add(new(entry.ParticleIndex, set.ReferenceBufferIndex, entry.ReferenceVertex, set.ToAnimationPeriod,
                    set.ToSimulationPeriod, set.ToSimulationMaxDistance, weights));
            }
        }
        return result;
    }

    private OotContactSurface? BuildOotContactSurface(int clothIndex)
    {
        var document = _bphcl!.NativeDocument!;
        var sim = document.Cloths[clothIndex].SimCloths.Single();
        var setup = document.ReadOotSidednessSetup(sim.ItemIndex);
        if (setup.Count != 0 && setup.Count != sim.CollidableItemIndices.Count)
            throw new InvalidDataException("OoT sidedness setup does not match the collider slots.");
        // Native setup declines to select a side without computed normals and flips.
        if (!setup.Any(v => v != 0) || !sim.Runtime.DoNormals || sim.TriangleIndices.Count == 0 || sim.TriangleFlips.Count == 0)
            return null;
        if (sim.TriangleIndices.Count % 3 != 0 || sim.TriangleIndices.Any(p => p >= sim.Particles.Count) ||
            sim.TriangleFlips.Count < (sim.TriangleIndices.Count / 3 + 7) / 8)
            throw new InvalidDataException("Invalid OoT contact-normal triangle data.");
        var slots = Enumerable.Range(0, setup.Count).Where(i => setup[i] != 0).ToArray();
        if (slots.Any(i => sim.CollidableItemIndices.Count(item => item == sim.CollidableItemIndices[i]) > 1))
            throw new NotSupportedException("One-sided OoT colliders shared across slots require collider-identity routing.");
        foreach (int slot in slots)
            if (document.Colliders.Single(c => c.ItemIndex == sim.CollidableItemIndices[slot]).Shape.TypeName is not
                ("hclSphereShape" or "hclCapsuleShape"))
                throw new NotSupportedException("One-sided OoT contacts currently support spheres and capsules only.");
        var triangles = Enumerable.Range(0, sim.TriangleIndices.Count / 3).Select(i => new ParticlePreviewTriangle
        {
            ParticleA = sim.TriangleIndices[i * 3], ParticleB = sim.TriangleIndices[i * 3 + 1],
            ParticleC = sim.TriangleIndices[i * 3 + 2]
        }).ToArray();
        return new(triangles, sim.TriangleFlips, slots);
    }

    internal static IReadOnlyList<ParticlePreviewBoneWeight> ResolveOotPinchReference(
        IReadOnlyDictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> vertices, int vertex)
    {
        if (!vertices.TryGetValue(vertex, out var weights) || weights.Count == 0 || weights.Any(w =>
                !float.IsFinite(w.Weight) || w.Weight <= 0 || !w.ReferencePosition.HasValue ||
                !float.IsFinite(w.ReferencePosition.Value.LengthSquared())))
            throw new NotSupportedException($"OoT reference vertex {vertex} has no complete decoded skin input.");
        return weights;
    }
}
