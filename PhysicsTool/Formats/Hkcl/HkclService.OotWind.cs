namespace HKCLTool;

public sealed partial class HkclService
{
    private OotContactSurface? BuildOotWindSurface(int clothIndex)
    {
        var sim = _bphcl!.NativeDocument!.Cloths[clothIndex].SimCloths.Single();
        if (!sim.Runtime.DoNormals) return null;
        if (sim.TriangleIndices.Count % 3 != 0 || sim.TriangleIndices.Any(p => p >= sim.Particles.Count) ||
            sim.TriangleFlips.Count < (sim.TriangleIndices.Count / 3 + 7) / 8)
            throw new InvalidDataException("Invalid OoT wind-normal triangle data.");
        var triangles = Enumerable.Range(0, sim.TriangleIndices.Count / 3).Select(i => new ParticlePreviewTriangle
        {
            ParticleA = sim.TriangleIndices[i * 3], ParticleB = sim.TriangleIndices[i * 3 + 1],
            ParticleC = sim.TriangleIndices[i * 3 + 2]
        }).ToArray();
        return new(triangles, sim.TriangleFlips.ToArray(), []);
    }
}
