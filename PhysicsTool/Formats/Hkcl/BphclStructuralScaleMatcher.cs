using System;
using System.Collections.Generic;
using System.Linq;

namespace HKCLTool;

/// <summary>
/// Uses structural measurements from clean TotK/BotW counterparts to suggest
/// a scale for a previously unseen BPHCL cloth. It is deliberately separate
/// from the exact-name catalog: a structural result is helpful evidence, not
/// a claim that the cloth has a known BotW counterpart.
/// </summary>
internal static class BphclStructuralScaleMatcher
{
    private static readonly StructuralProfile[] Profiles =
    [
        P("Hire_006_Havok", 10.000001f, 10, 6, 4, 6, 38, 5, 3, 12, .2f, .0201545544f),
        P("O_006_Havok", 5.00000048f, 5, 3, 2, 3, 19, 5, 5, 9, .4f, .0666666627f),
        P("Body_Metal_006_Havok", 3f, 3, 1, 2, 1, 7, 4, 13, 9, 1.66666663f, .001f),
        P("Add_Hair_008_Hacok", 4.39067554f, 7, 5, 2, 5, 31, 5, 0, 8, .227755398f, .0300380699f),
        P("Earring_028_Havok", 8f, 8, 6, 2, 4, 34, 6, 4, 8, .25f, 0f),
        P("Hair_1_008_Havok", 6.42588234f, 17, 8, 9, 16, 57, 5, 2, 13, .233430967f, .108172499f),
        P("Hair_2_008_Havok", 30.0000019f, 30, 18, 12, 18, 126, 6, 1, 21, .133333325f, .00433325907f),
        P("Hair_B_008_Havok", 4.02698898f, 6, 2, 4, 2, 14, 4, 1, 11, .496648997f, .00359019591f),
        P("Hair_D_008_Havok", 3f, 3, 1, 2, 1, 7, 4, 1, 9, .666666687f, 0f),
        P("Hair_E_008_Havok", .665974677f, 3, 1, 2, 1, 7, 4, 1, 9, 3.00311732f, .0169999991f),
        P("Hair_3_009_Havok", 12f, 12, 4, 8, 4, 28, 4, 3, 15, .0416666679f, 0f),
        P("Hair_2_009_Havok", 6.8399291f, 10, 6, 4, 6, 38, 5, 5, 15, .584801316f, 0f),
        P("Apron_009_Havok", 8.81909275f, 17, 12, 5, 20, 96, 5, 7, 14, .113390341f, .023162527f),
        P("Tunick_009_Havok", 8.3393755f, 15, 10, 5, 16, 79, 5, 5, 14, .119913049f, .0446386077f),
        P("Muffler_A_A_012_Havok", 7.02315235f, 14, 12, 2, 12, 84, 6, 10, 16, .213579312f, .0191473402f),
        P("Muffler_B_A_012_Havok", 10.0072289f, 16, 14, 2, 14, 98, 6, 10, 17, .199855521f, .0179884881f),
        P("Knot_Havok", 7.13639259f, 14, 10, 4, 10, 54, 4, 4, 12, .0350317061f, .0342907384f),
        P("Carabiner_Havok", 2.7277317f, 6, 5, 1, 4, 28, 5, 3, 7, .366604984f, .0253359508f),
        P("Rope_Havok", .126519278f, 7, 5, 2, 5, 27, 4, 4, 8, 15.8078671f, .0209803917f),
        P("Hair_Havok", 9.00000095f, 9, 6, 3, 8, 43, 5, 4, 12, .222222209f, .01f),
        P("Headrope_Havok", 10.000001f, 10, 6, 4, 6, 42, 6, 3, 12, .049999997f, .0399998426f),
        P("Hairtop_020_Havok", 5.86103058f, 11, 9, 2, 9, 55, 5, 2, 9, .17061846f, .0685728714f),
        P("Belt_A_Havok", 5.00000048f, 5, 3, 2, 3, 21, 6, 5, 8, .399999976f, .0102788778f),
        P("Tunic_001_Havok", 30.5641632f, 70, 40, 30, 100, 460, 6, 7, 25, .0654361099f, .0202689059f),
        P("Hair_030_A_Havok", 12.000001f, 12, 6, 6, 10, 42, 5, 1, 11, .166669995f, .00892902911f),
        P("Caudal_Fin_046_Havok", 14.9999971f, 15, 12, 3, 16, 69, 4, 1, 9, .0666666776f, 0f),
        P("Dread_Havok", 2f, 13, 10, 3, 14, 73, 5, 1, 12, 1f, .0443473607f),
        P("Pony_Havok", 2.50000024f, 33, 27, 6, 27, 165, 5, 11, 20, .99999994f, .0345293321f),
        P("AccSpine_Havok", 3f, 8, 4, 4, 4, 24, 4, 4, 8, 1f, .0569485277f),
        P("Skirt_Havok", 2f, 48, 36, 12, 70, 226, 4, 5, 29, 1f, .0230749957f),
        P("Hat_179_Havok", 16f, 16, 8, 8, 16, 86, 6, 1, 10, .25f, .0369989127f),
        P("Sode_179_Havok", 29.9999981f, 30, 10, 20, 32, 94, 4, 7, 14, .0666666701f, .01f),
        P("Hair_Back_Havok", 7.50394678f, 15, 9, 6, 9, 57, 5, 8, 20, .133263201f, .0223333295f)
    ];

    public static bool TrySuggest(NativeBphclSimCloth simulation, int boneCount, out float scale, out string basis)
    {
        var source = StructuralFingerprint.Create(simulation, boneCount);
        var best = Profiles
            .Select(profile => (Profile: profile, Score: source.GetSimilarity(profile.Fingerprint)))
            .OrderByDescending(candidate => candidate.Score)
            .First();

        if (best.Score < 0.63f)
        {
            scale = 1.0f;
            basis = "Topology fallback";
            return false;
        }

        scale = best.Profile.Scale;
        var confidence = (int)MathF.Round(best.Score * 100.0f);
        var qualifier = best.Score >= 0.80f ? "Strong" : "Probable";
        basis = $"{qualifier} structural match: {best.Profile.Name} ({confidence}%)";
        return true;
    }

    private static StructuralProfile P(
        string name, float scale, int total, int dynamic, int fixedCount, int triangles,
        int constraints, int sets, int colliders, int bones, float mass, float radius) =>
        new(name, scale, new StructuralFingerprint(total, dynamic, fixedCount, triangles, constraints, sets, colliders, bones, mass, radius));

    private sealed record StructuralProfile(string Name, float Scale, StructuralFingerprint Fingerprint);

    private readonly record struct StructuralFingerprint(
        int TotalParticles,
        int DynamicParticles,
        int FixedParticles,
        int Triangles,
        int ConstraintRecords,
        int ConstraintSets,
        int Colliders,
        int Bones,
        float DynamicMass,
        float AverageRadius)
    {
        public static StructuralFingerprint Create(NativeBphclSimCloth simulation, int boneCount)
        {
            var dynamicParticles = simulation.Particles.Where(particle => !particle.Fixed).ToArray();
            return new StructuralFingerprint(
                simulation.Particles.Count,
                dynamicParticles.Length,
                simulation.Particles.Count - dynamicParticles.Length,
                simulation.TriangleCount,
                simulation.ConstraintSets.Sum(set => set.Links.Count + set.LocalConstraints.Count + set.TransitionParticles.Count),
                simulation.ConstraintSets.Count,
                simulation.CollidableItemIndices.Count,
                boneCount,
                dynamicParticles.Sum(particle => particle.Mass),
                dynamicParticles.Length == 0 ? 0.0f : dynamicParticles.Average(particle => particle.Radius));
        }

        public float GetSimilarity(StructuralFingerprint other)
        {
            return
                0.13f * Similarity(TotalParticles, other.TotalParticles) +
                0.18f * Similarity(DynamicParticles, other.DynamicParticles) +
                0.10f * Similarity(FixedParticles, other.FixedParticles) +
                0.15f * Similarity(Triangles, other.Triangles) +
                0.15f * Similarity(ConstraintRecords, other.ConstraintRecords) +
                0.05f * Similarity(ConstraintSets, other.ConstraintSets) +
                0.05f * Similarity(Colliders, other.Colliders) +
                0.06f * Similarity(Bones, other.Bones) +
                0.08f * Similarity(DynamicMass, other.DynamicMass) +
                0.05f * Similarity(AverageRadius, other.AverageRadius);
        }

        private static float Similarity(int left, int right)
        {
            if (left == right)
                return 1.0f;
            var largest = Math.Max(Math.Max(left, right), 1);
            return Math.Max(0.0f, 1.0f - Math.Abs(left - right) / (float)largest);
        }

        private static float Similarity(float left, float right)
        {
            if (left == right || (Math.Abs(left) < 0.000001f && Math.Abs(right) < 0.000001f))
                return 1.0f;
            var largest = Math.Max(Math.Max(Math.Abs(left), Math.Abs(right)), 0.000001f);
            return Math.Max(0.0f, 1.0f - Math.Abs(left - right) / largest);
        }
    }
}
