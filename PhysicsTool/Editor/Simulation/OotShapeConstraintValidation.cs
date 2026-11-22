using System.Numerics;

namespace HKCLTool;

internal static class OotShapeConstraintValidation
{
    internal static void Validate(ParticlePreviewData source)
    {
        var particles = source.Particles.Select(p => p.Index).ToHashSet();
        var bones = source.Bones.Select(b => b.Index).ToHashSet();
        var sets = source.ConstraintSets.ToDictionary(s => s.Index, s => s.Kind);
        var seen = new HashSet<int>();
        foreach (var volume in source.OotVolumeConstraints)
        {
            if (!seen.Add(volume.ConstraintSetIndex) || sets.GetValueOrDefault(volume.ConstraintSetIndex) != "hclVolumeConstraint")
                throw new InvalidDataException("Invalid OoT volume constraint identity.");
            if (volume.Frames.Any(f => !particles.Contains(f.ParticleIndex) || !Finite(f.FrameVector) || !float.IsFinite(f.Weight)) ||
                volume.Applies.Any(a => !particles.Contains(a.ParticleIndex) || !Finite(a.FrameVector) || !float.IsFinite(a.Stiffness)))
                throw new InvalidDataException("OoT volume constraint contains an invalid particle or nonfinite value.");
        }
        foreach (var plane in source.OotBonePlanes)
            if (sets.GetValueOrDefault(plane.ConstraintSetIndex) != "hclBonePlanesConstraintSet" ||
                !particles.Contains(plane.ParticleIndex) || !bones.Contains(plane.BoneIndex) ||
                !Finite(new(plane.RestNormal, plane.Offset)) || !Finite(new(plane.RestOrigin, plane.Stiffness)))
                throw new InvalidDataException("OoT bone plane contains an invalid binding or nonfinite value.");
        if (sets.Any(s => s.Value == "hclVolumeConstraint" && !seen.Contains(s.Key)))
            throw new InvalidDataException("OoT volume constraint is missing its decoded records.");
    }

    private static bool Finite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
}
