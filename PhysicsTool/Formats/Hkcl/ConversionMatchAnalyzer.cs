using System.Globalization;

namespace HKCLTool;

// Developer-facing analysis for building verified BPHCL-to-HKCL profiles from
// a folder of vanilla counterparts. It never writes either source format.
internal static class ConversionMatchAnalyzer
{
    internal static IReadOnlyList<ConversionMatch> Analyze(string matchDirectory)
    {
        var results = new List<ConversionMatch>();
        foreach (var bphclPath in Directory.EnumerateFiles(matchDirectory, "*.bphcl", SearchOption.TopDirectoryOnly))
        {
            var baseName = Path.GetFileNameWithoutExtension(bphclPath)
                .Replace("RaulSkin_Upper", "Upper", StringComparison.Ordinal);
            var hkclPath = Path.Combine(matchDirectory, baseName + ".hkcl");
            if (!File.Exists(hkclPath))
                continue;

            var bphcl = NativeBphclDocument.Open(bphclPath);
            var hkcl = new HkclService();
            hkcl.Load(hkclPath);
            for (var sourceIndex = 0; sourceIndex < bphcl.Cloths.Count; sourceIndex++)
            {
                var sourceCloth = bphcl.Cloths[sourceIndex];
                if (sourceCloth.SimCloths.Count != 1)
                    continue;

                var normalizedName = StripPrefix(sourceCloth.Name);
                var targetIndex = Enumerable.Range(0, hkcl.GetClothSummaries().Count)
                    .FirstOrDefault(index => string.Equals(
                        StripPrefix(hkcl.GetClothName(index)),
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase), -1);
                if (targetIndex < 0)
                    continue;

                var sourceParticles = sourceCloth.SimCloths[0].Particles.Where(particle => !particle.Fixed).ToArray();
                var targetParticles = hkcl.GetParticleRows(targetIndex).Where(particle => !particle.Fixed).ToArray();
                var sourceMass = sourceParticles.Sum(particle => particle.Mass);
                var targetMass = targetParticles.Sum(particle => particle.Mass);
                if (sourceMass <= 0.0f || targetMass <= 0.0f)
                    continue;

                results.Add(new ConversionMatch(
                    baseName,
                    normalizedName,
                    sourceParticles.Length,
                    targetParticles.Length,
                    sourceMass,
                    targetMass,
                    targetMass / sourceMass,
                    ConversionStructuralFingerprint.Create(
                        sourceCloth.SimCloths[0],
                        bphcl.Skeletons.ElementAtOrDefault(sourceIndex)?.BoneCount ?? 0)));
            }
        }

        return results
            .OrderBy(match => match.SourceFile, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.ClothName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string Format(IReadOnlyList<ConversionMatch> matches) => string.Join(
        Environment.NewLine,
        matches.Select(match => string.Join('|',
            match.SourceFile,
            match.ClothName,
            match.SourceDynamicParticleCount.ToString(CultureInfo.InvariantCulture),
            match.TargetDynamicParticleCount.ToString(CultureInfo.InvariantCulture),
            match.SourceTotalMass.ToString("G9", CultureInfo.InvariantCulture),
            match.TargetTotalMass.ToString("G9", CultureInfo.InvariantCulture),
            match.Scale.ToString("G9", CultureInfo.InvariantCulture),
            match.Fingerprint.TotalParticleCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.FixedParticleCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.TriangleCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.ConstraintRecordCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.ConstraintSetCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.ColliderCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.BoneCount.ToString(CultureInfo.InvariantCulture),
            match.Fingerprint.AverageRadius.ToString("G9", CultureInfo.InvariantCulture),
            match.Fingerprint.AverageFriction.ToString("G9", CultureInfo.InvariantCulture),
            match.Fingerprint.ConstraintSignature)));

    private static string StripPrefix(string name) => name.StartsWith("Link:", StringComparison.OrdinalIgnoreCase)
        ? name[5..]
        : name;
}

internal sealed record ConversionMatch(
    string SourceFile,
    string ClothName,
    int SourceDynamicParticleCount,
    int TargetDynamicParticleCount,
    float SourceTotalMass,
    float TargetTotalMass,
    float Scale,
    ConversionStructuralFingerprint Fingerprint);

internal sealed record ConversionStructuralFingerprint(
    int TotalParticleCount,
    int DynamicParticleCount,
    int FixedParticleCount,
    int TriangleCount,
    int ConstraintRecordCount,
    int ConstraintSetCount,
    int ColliderCount,
    int BoneCount,
    float AverageRadius,
    float AverageFriction,
    string ConstraintSignature)
{
    public static ConversionStructuralFingerprint Create(NativeBphclSimCloth simulation, int boneCount)
    {
        var dynamicParticles = simulation.Particles.Where(particle => !particle.Fixed).ToArray();
        var constraintSets = simulation.ConstraintSets;
        var records = constraintSets.Sum(set => set.Links.Count + set.LocalConstraints.Count + set.TransitionParticles.Count);
        var signature = string.Join(",", constraintSets
            .GroupBy(set => set.ClassName, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:{group.Sum(set => set.Links.Count + set.LocalConstraints.Count + set.TransitionParticles.Count)}"));

        return new ConversionStructuralFingerprint(
            simulation.Particles.Count,
            dynamicParticles.Length,
            simulation.Particles.Count - dynamicParticles.Length,
            simulation.TriangleCount,
            records,
            constraintSets.Count,
            simulation.CollidableItemIndices.Count,
            boneCount,
            dynamicParticles.Length == 0 ? 0.0f : dynamicParticles.Average(particle => particle.Radius),
            dynamicParticles.Length == 0 ? 0.0f : dynamicParticles.Average(particle => particle.Friction),
            signature);
    }
}
