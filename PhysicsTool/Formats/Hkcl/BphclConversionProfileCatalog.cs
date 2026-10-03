using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HKCLTool;

/// <summary>
/// Verified BPHCL-to-HKCL mass scales measured from paired vanilla TotK and
/// BotW files. These are intentionally data-driven rather than guesses based
/// on particle count. Unknown cloths still use the converter's fallback.
/// </summary>
internal static class BphclConversionProfileCatalog
{
    private static readonly BphclConversionProfile[] Profiles =
    [
        new("Armor_006_Head", "Hire_006_Havok", 10.000001f),
        new("Armor_006_Head", "O_006_Havok", 5.00000048f),
        new("Armor_006_Upper", "Body_Metal_006_Havok", 3f),
        new("Armor_008_Head", "Add_Hair_008_Hacok", 4.39067554f),
        new("Armor_008_Head", "Earring_028_Havok", 8f),
        new("Armor_008_Head", "Hair_1_008_Havok", 6.42588234f),
        new("Armor_008_Head", "Hair_2_008_Havok", 30.0000019f),
        new("Armor_008_Head", "Hair_B_008_Havok", 4.02698898f),
        new("Armor_008_Head", "Hair_D_008_Havok", 3f),
        new("Armor_008_Head", "Hair_E_008_Havok", 0.665974677f),
        new("Armor_008_Head", "Hair_F_008_Havok", 3f),
        new("Armor_008_Head", "Hair_H_008_Havok", 3f),
        new("Armor_009_Head", "Hair_2_009_Havok", 6.8399291f),
        new("Armor_009_Head", "Hair_1_009_Havok", 8.56784344f),
        new("Armor_009_Head", "Hair_3_009_Havok", 12f),
        new("Armor_009_Head", "Hair_B_009_Havok", 4.02698898f),
        new("Armor_009_Head", "Hair_D_009_Havok", 3f),
        new("Armor_009_Head", "Hair_E_009_Havok", 0.665974677f),
        new("Armor_009_Head", "Hair_F_009_Havok", 3f),
        new("Armor_009_Head", "Hair_G_009_Havok", 3f),
        new("Armor_009_Head", "Hair_H_009_Havok", 3f),
        new("Armor_009_Head", "Hair_I_009_Havok", 1.93435538f),
        new("Armor_009_Head", "Hair_J_009_Havok", 1.09449458f),
        new("Armor_009_Upper", "Apron_009_Havok", 8.81909275f),
        new("Armor_009_Upper", "Tunick_009_Havok", 8.3393755f),
        new("Armor_012_Head_A", "Add_Hair_A_Hacok", 1.8061713f),
        new("Armor_012_Head_A", "Hair_1_A_012_Havok", 6.42588234f),
        new("Armor_012_Head_A", "Hair_2_A_012_Havok", 30.0000019f),
        new("Armor_012_Head_A", "Hair_B_A_012_Havok", 4.02698946f),
        new("Armor_012_Head_A", "Hair_E_A_012_Havok", 0.665974677f),
        new("Armor_012_Head_A", "Hair_F_A_012_Havok", 5.00000048f),
        new("Armor_012_Head_A", "Hair_H_A_012_Havok", 5.00000048f),
        new("Armor_012_Head_A", "Muffler_A_A_012_Havok", 7.02315235f),
        new("Armor_012_Head_A", "Muffler_B_A_012_Havok", 10.0072289f),
        new("Armor_014_Head", "Hair_2_Havok", 30.0000019f),
        new("Armor_014_Head", "Hair_Acs_Havok", 5.00000048f),
        new("Armor_014_Head", "Hair_H_Havok", 3f),
        new("Armor_014_Head", "Knot_Havok", 7.13639259f),
        new("Armor_014_Upper", "Carabiner_Havok", 2.7277317f),
        new("Armor_014_Upper", "Poach_B_Havok", 3f),
        new("Armor_014_Upper", "Poach_C_Havok", 3f),
        new("Armor_014_Upper", "Rope_Havok", 0.126519278f),
        new("Armor_017_Head", "Hair_Havok", 9.00000095f),
        new("Armor_017_Head", "Headrope_Havok", 10.000001f),
        new("Armor_020_Head", "Hairtop_020_Havok", 5.86103058f),
        new("Armor_020_Head", "Hair_2_Havok", 30.0000019f),
        new("Armor_020_Head", "Hair_3_Havok", 12f),
        new("Armor_020_Upper", "Belt_A_Havok", 5.00000048f),
        new("Armor_020_Upper", "Tunic_001_Havok", 30.5641632f),
        new("Armor_030_Head", "Hair_030_A_Havok", 12.000001f),
        // Hair_030_B has different fixed particles; a total-mass ratio is not a valid particle profile.
        new("Armor_046_Head", "Beard_046_Havok", 10.000001f),
        new("Armor_046_Head", "Caudal_Fin_046_Havok", 15f),
        new("Armor_048_Head", "Dread_Havok", 2f),
        new("Armor_048_Head", "Hair_2_Havok", 30.0000019f),
        new("Armor_048_Head", "Pony_Havok", 2.50000024f),
        new("Armor_048_Upper", "AccSpine_Havok", 3f),
        new("Armor_048_Upper", "Skirt_Havok", 2f),
        // Measured from the Armor_210 paired vanilla HKCL. Its soft constraints use the same scale as mass.
        new("Armor_210_RaulSkin_Upper", "Outer_210_Havok", 20.9737225f, 20.9737225f, "Measured paired match"),
        // The Dinraal outer skirt has the same 54-particle topology and soft-link coefficients as Outer_210.
        // It uses that measured solver conversion rather than the generic tunic fallback.
        new("Armor_1061_RaulSkin_Upper", "SimulationMesh_Outer_1061", 20.9737225f, 20.9737225f, "Measured Outer_210 coefficient match"),
        new("Armor_1036_RaulSkin_Upper", "SimulationMesh_Crotch", 2f),
        new("Armor_1036_RaulSkin_Upper", "SimulationMesh_Necklace", 4.39067554f),
        new("Armor_1036_RaulSkin_Upper", "SimulationMesh_Necklace_2", 5.86103058f),
        new("Armor_1036_RaulSkin_Upper", "SimulationMesh_Rope", 0.665974677f),
        new("Armor_1036_RaulSkin_Upper", "SimulationMesh_Skirt", 2f),
        new("Armor_1036_RaulSkin_Upper", "SimulationMesh_Tail", 5.86103058f),
        new("Armor_1043_RaulSkin_Upper", "SimulationMesh_BackCloth", 5f),
        new("Armor_1043_RaulSkin_Upper", "SimulationMesh_ShoulderCloth", 5f),
        new("Armor_1043_RaulSkin_Upper", "SimulationMesh_Skirt_1", 2f),
        new("Armor_1043_RaulSkin_Upper", "SimulationMesh_Sleeve", 8.3393755f),
        new("Armor_1043_RaulSkin_Upper", "SimulationMesh_Sleeve_D", 5f),
        new("Armor_1086_Head", "HairLong_1086_SimulationMesh", 30.5641632f),
        new("Armor_179_Head", "Hair_1_179_Havok", 6.42588186f),
        new("Armor_179_Head", "Hair_2_179_Havok", 30f),
        new("Armor_179_Head", "Hair_3_179_Havok", 12f),
        new("Armor_179_Head", "Hair_B_179_Havok", 4.02698898f),
        new("Armor_179_Head", "Hair_D_179_Havok", 3f),
        new("Armor_179_Head", "Hat_179_Havok", 16f),
        new("Armor_179_Upper", "Sode_179_Havok", 29.9999981f),
        // Outer_179 has 62 versus 54 particles; retain authored mass instead of applying a total ratio.
        new("Armor_180_RaulSkin_Upper", "Mant_Havok", 33.5024753f),
        new("Armor_182_Head", "Hair_2_Havok", 30.0000019f),
        new("Armor_182_Head", "Hair_Back_Havok", 7.50394678f)
    ];

    // AAMP metadata is a separate native contract from Havok particle data.
    // These exact values are from paired retail HKCL/BPHCL files and only
    // apply when the source actor and cloth name both match.
    private static readonly ReverseAampProfile[] ReverseAampProfiles =
    [
        new("Armor_006_Head", "O_006_Havok", "Head", "leather"),
        new("Armor_006_Upper", "Body_Metal_006_Havok", "Spine_2", "satin"),
        new("Armor_008_Head", "Hair_1_008_Havok", "Hair_Root", "velvet"),
        new("Armor_008_Head", "Hair_2_008_Havok", "Hair_Root", "burlap"),
        new("Armor_009_Head", "Hair_1_009_Havok", "Hair_Root", "velvet"),
        new("Armor_009_Head", "Hair_2_009_Havok", "Hair_Root", "ArmorDefault_Head_Sideburns"),
        new("Armor_009_Upper", "Apron_009_Havok", "Waist", "velvet"),
        new("Armor_009_Upper", "Tunick_009_Havok", "Waist", "velvet"),
        new("Armor_012_Head_A", "Add_Hair_A_Hacok", "Head", "hair"),
        new("Armor_012_Head_A", "Hair_1_A_012_Havok", "Head", "velvet"),
        new("Armor_012_Head_A", "Hair_2_A_012_Havok", "Hair_Root", "burlap"),
        new("Armor_012_Head_A", "Hair_B_A_012_Havok", "Hair_B_1", "hair"),
        new("Armor_012_Head_A", "Hair_E_A_012_Havok", "Hair_E_1", "hair"),
        new("Armor_012_Head_A", "Hair_F_A_012_Havok", "Hair_F_1", "hair"),
        new("Armor_012_Head_A", "Hair_H_A_012_Havok", "Hair_H_1", "hair"),
        new("Armor_012_Head_A", "Muffler_A_A_012_Havok", "Spine_2", "burlap"),
        new("Armor_012_Head_A", "Muffler_B_A_012_Havok", "Spine_2", "burlap"),
        new("Armor_014_Head", "Hair_2_Havok", "Hair_Root", "burlap"),
        new("Armor_014_Head", "Hair_Acs_Havok", "Head", "leather"),
        new("Armor_014_Head", "Knot_Havok", "Head", "satin"),
        new("Armor_014_Upper", "Rope_Havok", "Waist", "silk"),
        new("Armor_017_Head", "Headrope_Havok", "Head", "burlap"),
        new("Armor_020_Head", "Hair_2_Havok", "Hair_Root", "burlap"),
        new("Armor_020_Head", "Hair_3_Havok", "Hair_Root_Armor", "hair"),
        new("Armor_020_Head", "Hairtop_020_Havok", "Head", "velvet"),
        new("Armor_020_Upper", "Belt_A_Havok", "Waist", "burlap"),
        new("Armor_020_Upper", "Tunic_001_Havok", "Skirt_1_FL_Armor", "old_leather"),
        new("Armor_030_Head", "Hair_030_A_Havok", "Head", "hair"),
        new("Armor_030_Head", "Hair_030_B_Havok", "Head", "hair"),
        new("Armor_046_Head", "Caudal_Fin_046_Havok", "Head", "satin"),
        new("Armor_048_Head", "Dread_Havok", "Head", "velvet"),
        new("Armor_048_Head", "Hair_2_Havok", "Head", "burlap"),
        new("Armor_048_Head", "Pony_Havok", "Head", "velvet"),
        new("Armor_048_Upper", "AccSpine_Havok", "Spine_2", "satin"),
        new("Armor_048_Upper", "Skirt_Havok", "Skirt_1_BC_Armor", "satin"),
        new("Armor_179_Head", "Hair_1_179_Havok", "Head", "hair"),
        new("Armor_179_Head", "Hair_2_179_Havok", "Hair_Root", "ArmorDefault_Head_Sideburns"),
        new("Armor_179_Head", "Hat_179_Havok", "Head", "old_leather"),
        new("Armor_179_Upper", "Sode_179_Havok", "Arm_1_L", "satin"),
        new("Armor_182_Head", "Hair_2_Havok", "Head", "ArmorDefault_Head_Sideburns"),
        new("Armor_182_Head", "Hair_Back_Havok", "Head", "hair")
    ];

    // Native solver programs measured from paired retail HKCL/BPHCL files.
    // Only entries that differ from the general fresh-conversion layout are
    // listed here. Unknown cloths keep the converter's established fallback.
    private static readonly ReverseConstraintExecutionProfile[] ReverseConstraintExecutionProfiles =
    [
        new("Armor_006_Head", "Hire_006_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_006_Head", "O_006_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_008_Head", "Add_Hair_008_Hacok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_008_Head", "Hair_B_008_Havok", [2, 3, 0, 1, -1]),
        new("Armor_008_Head", "Hair_D_008_Havok", [2, 3, 0, 1, -1]),
        new("Armor_008_Head", "Hair_E_008_Havok", [2, 3, 0, 1, -1]),
        new("Armor_008_Head", "Hair_F_008_Havok", [2, 3, 0, 1, -1]),
        new("Armor_008_Head", "Hair_H_008_Havok", [2, 3, 0, 1, -1]),
        new("Armor_009_Head", "Hair_1_009_Havok", [2, 0, 4, 1, -1, 3]),
        new("Armor_009_Head", "Hair_2_009_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_009_Head", "Hair_3_009_Havok", [2, 3, 0, 1, -1]),
        new("Armor_009_Head", "Hair_B_009_Havok", [2, 3, 0, 1, -1]),
        new("Armor_009_Head", "Hair_D_009_Havok", [2, 3, 0, 1, -1]),
        new("Armor_009_Head", "Hair_E_009_Havok", [2, 3, 0, 1, -1]),
        new("Armor_009_Head", "Hair_F_009_Havok", [2, 3, 0, 1, -1]),
        new("Armor_009_Head", "Hair_G_009_Havok", [2, 3, 0, 1, -1]),
        new("Armor_009_Head", "Hair_H_009_Havok", [2, 3, 0, 1, -1]),
        new("Armor_009_Head", "Hair_I_009_Havok", [2, 3, 0, 1, -1]),
        new("Armor_009_Head", "Hair_J_009_Havok", [2, 3, 0, 1, -1]),
        new("Armor_012_Head_A", "Add_Hair_A_Hacok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_012_Head_A", "Hair_B_A_012_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_012_Head_A", "Hair_E_A_012_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_012_Head_A", "Hair_F_A_012_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_012_Head_A", "Hair_H_A_012_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_014_Head", "Hair_Acs_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_014_Head", "Hair_H_Havok", [2, 3, 0, 1, -1]),
        new("Armor_014_Upper", "Carabiner_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_014_Upper", "Poach_B_Havok", [2, 3, 0, 1, -1]),
        new("Armor_014_Upper", "Poach_C_Havok", [2, 3, 0, 1, -1]),
        new("Armor_017_Head", "Hair_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_020_Head", "Hair_3_Havok", [2, 3, 0, 1, -1]),
        new("Armor_030_Head", "Hair_030_A_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_030_Head", "Hair_030_B_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_046_Head", "Beard_046_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_179_Head", "Hair_1_179_Havok", [2, 3, 0, 1, -1]),
        new("Armor_179_Head", "Hair_2_179_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_179_Head", "Hair_3_179_Havok", [2, 3, 0, 1, -1]),
        new("Armor_179_Head", "Hair_B_179_Havok", [2, 3, 0, 1, -1]),
        new("Armor_179_Head", "Hair_D_179_Havok", [2, 3, 0, 1, -1]),
        new("Armor_182_Head", "Hair_2_Havok", [2, 3, 0, 4, 4, 1, -1]),
        new("Armor_182_Head", "Hair_Back_Havok", [2, 3, 0, 4, 4, 1, -1])
    ];

    public static bool TryGetReverseConstraintExecution(string? sourcePath, string clothName, out int[] execution)
    {
        var sourceFile = NormalizeActorFileName(Path.GetFileNameWithoutExtension(sourcePath ?? string.Empty));
        var normalizedName = Normalize(clothName);
        var profile = ReverseConstraintExecutionProfiles.FirstOrDefault(entry =>
            string.Equals(entry.SourceFile, sourceFile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.ClothName, normalizedName, StringComparison.OrdinalIgnoreCase));
        execution = profile?.Execution.ToArray() ?? Array.Empty<int>();
        return profile is not null;
    }
    public static bool TryGetReverseAampProfile(
        string? sourcePath,
        string clothName,
        out string baseBone,
        out string preset)
    {
        var sourceFile = NormalizeActorFileName(Path.GetFileNameWithoutExtension(sourcePath ?? string.Empty));
        var normalizedName = Normalize(clothName);
        var profile = ReverseAampProfiles.FirstOrDefault(entry =>
            string.Equals(NormalizeActorFileName(entry.SourceFile), sourceFile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.ClothName, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            baseBone = string.Empty;
            preset = string.Empty;
            return false;
        }

        baseBone = profile.BaseBone;
        preset = profile.Preset;
        return true;
    }

    public static bool TryGet(string? sourcePath, string clothName, out float scale, out string basis)
    {
        var normalizedName = Normalize(clothName);
        var sourceFile = Path.GetFileNameWithoutExtension(sourcePath ?? string.Empty);
        var exact = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.SourceFile, sourceFile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.ClothName, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            scale = exact.Scale;
            basis = $"{exact.Basis ?? "Verified match"}: {exact.SourceFile}";
            return true;
        }

        var sameName = Profiles
            .Where(profile => string.Equals(profile.ClothName, normalizedName, StringComparison.OrdinalIgnoreCase))
            .Select(profile => profile.Scale)
            .DistinctBy(value => MathF.Round(value, 3))
            .ToArray();
        if (sameName.Length == 1)
        {
            scale = sameName[0];
            basis = "Verified cloth-name match";
            return true;
        }

        // Some TotK exports carry a generic SimulationMesh_ prefix and an
        // actor suffix, so an exact name cannot identify an otherwise clear
        // cloth family. Only use a family when every known match agrees on
        // its scale; generic families such as Hair intentionally fall back
        // to the structural matcher instead.
        var family = GetVerifiedFamily(normalizedName);
        if (family.Length > 0)
        {
            var familyScales = Profiles
                .Where(profile => profile.ClothName.Contains(family, StringComparison.OrdinalIgnoreCase))
                .Select(profile => profile.Scale)
                .DistinctBy(value => MathF.Round(value, 3))
                .ToArray();
            if (familyScales.Length == 1)
            {
                scale = familyScales[0];
                // A family can offer a useful particle-mass hint, but it is
                // not proof that link/bend coefficients use the same BotW
                // conversion. Callers keep those coefficients independent.
                basis = $"Mass-only cloth family hint: {family}";
                return true;
            }
        }

        scale = 1.0f;
        basis = "Topology fallback";
        return false;
    }

    /// <summary>
    /// Finds a measured pair for the reverse HKCL-to-BPHCL path. HKCL files
    /// omit TotK's optional RaulSkin segment, so actor filenames are compared
    /// after normalizing that segment.
    /// </summary>
    /// <summary>
    /// Returns the native TotK cloth name for verified paired cases where the
    /// BotW counterpart uses a different authoring name.
    /// </summary>
    public static string GetReverseClothName(string? sourcePath, string clothName)
    {
        var sourceFile = NormalizeActorFileName(Path.GetFileNameWithoutExtension(sourcePath ?? string.Empty));
        var normalizedName = Normalize(clothName);
        if (string.Equals(sourceFile, "Armor_180_Upper", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalizedName, "Mant_Havok", StringComparison.OrdinalIgnoreCase))
        {
            return "SimulationMesh_Mant";
        }

        return normalizedName;
    }
    public static bool TryGetReverseScales(
        string? sourcePath,
        string clothName,
        out float massScale,
        out float constraintScale)
    {
        var normalizedName = Normalize(clothName);
        var sourceFile = NormalizeActorFileName(Path.GetFileNameWithoutExtension(sourcePath ?? string.Empty));
        var exact = Profiles.FirstOrDefault(profile =>
            string.Equals(NormalizeActorFileName(profile.SourceFile), sourceFile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.ClothName, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (exact is null)
        {
            massScale = 1.0f;
            constraintScale = 1.0f;
            return false;
        }

        massScale = exact.Scale;
        constraintScale = exact.ConstraintScale ??
            (UsesLegacyMassConstraintScale(exact.SourceFile, normalizedName) ? exact.Scale : 1.0f);
        return true;
    }
    /// <summary>
    /// Returns true only for a measured BPHCL/HKCL counterpart from the same
    /// source file. A similarly named or merely similar-shaped cloth is not
    /// enough evidence to reinterpret solver coefficients.
    /// </summary>
    public static bool HasExactSourceProfile(string? sourcePath, string clothName)
    {
        var normalizedName = Normalize(clothName);
        var sourceFile = Path.GetFileNameWithoutExtension(sourcePath ?? string.Empty);
        return Profiles.Any(profile =>
            string.Equals(profile.SourceFile, sourceFile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.ClothName, normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// TotK normally enables terrain collision for every dynamic particle.
    /// These are the only paired vanilla exceptions observed so far.
    /// </summary>
    public static bool ShouldEnableReverseLandscapeCollision(string? sourcePath, string clothName)
    {
        var sourceFile = NormalizeActorFileName(Path.GetFileNameWithoutExtension(sourcePath ?? string.Empty));
        var normalizedName = Normalize(clothName);
        return !((string.Equals(sourceFile, "Armor_009_Head", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(normalizedName, "Hair_2_009_Havok", StringComparison.OrdinalIgnoreCase)) ||
                 (string.Equals(sourceFile, "Armor_179_Head", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(normalizedName, "Hair_2_179_Havok", StringComparison.OrdinalIgnoreCase)) ||
                 (string.Equals(sourceFile, "Armor_182_Head", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(normalizedName, "Hair_2_Havok", StringComparison.OrdinalIgnoreCase)));
    }

    public static bool ShouldScaleConstraintCoefficients(string? sourcePath, string clothName)
    {
        return TryGetConstraintScale(sourcePath, clothName, out var scale) &&
               MathF.Abs(scale - 1.0f) > 0.000001f;
    }

    /// <summary>
    /// Gets the independently measured multiplier for soft link/bend values.
    /// A mass match alone is not enough evidence to rescale those coefficients.
    /// </summary>
    public static bool TryGetConstraintScale(string? sourcePath, string clothName, out float scale)
    {
        var normalizedName = Normalize(clothName);
        var sourceFile = Path.GetFileNameWithoutExtension(sourcePath ?? string.Empty);
        var exact = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.SourceFile, sourceFile, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.ClothName, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (exact == null)
        {
            scale = 1.0f;
            return false;
        }

        if (exact.ConstraintScale is { } measuredScale)
        {
            scale = measuredScale;
            return true;
        }

        // Preserve the previous verified-profile behavior until a profile has
        // an independently measured constraint value.
        scale = UsesLegacyMassConstraintScale(sourceFile, normalizedName)
            ? exact.Scale
            : 1.0f;
        return true;
    }

    private static bool UsesLegacyMassConstraintScale(string sourceFile, string clothName)
    {
        if (sourceFile.Contains("RaulSkin_Upper", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(sourceFile, "Armor_1086_Head", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(clothName, "HairLong_1086_SimulationMesh", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    // Constraint differences are deliberately narrower than mass profiles.
    // These rules are repeated in real vanilla BPHCL/HKCL counterparts; an
    // unrecognized cloth retains the direct source values.
    public static bool ShouldOmitConstraintSet(string clothName, string className)
    {
        if (!string.Equals(className, "hclBendStiffnessConstraintSet", StringComparison.Ordinal))
            return false;

        var normalized = Normalize(clothName);
        return normalized.Contains("Muffler", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "Belt_A_Havok", StringComparison.OrdinalIgnoreCase);
    }

    public static double ApplyConstraintFieldProfile(
        string? sourcePath,
        string clothName,
        string className,
        string fieldName,
        double value)
    {
        var normalized = Normalize(clothName);
        if (string.Equals(className, "hclBendLinkConstraintSet", StringComparison.Ordinal) &&
            string.Equals(fieldName, "bendMinLength", StringComparison.Ordinal) &&
            (RequiresHair2BendMinimumScale(sourcePath, normalized) ||
             normalized.Contains("Muffler", StringComparison.OrdinalIgnoreCase)))
        {
            // This differs by source file. The matched Hair_2 variants below
            // encode their bend minimum 25% larger in TotK, while 009, 179,
            // and 182 already store BotW-compatible values.
            return value * 0.8;
        }

        if (string.Equals(normalized, "Hair_1_009_Havok", StringComparison.OrdinalIgnoreCase))
        {
            // This is a measured one-to-one profile, not a family rule.
            // The paired files keep the same topology but use a small set of
            // independent BotW rest/range coefficients.
            return (className, fieldName) switch
            {
                ("hclBendLinkConstraintSet", "bendMinLength") => value * 0.7532889,
                ("hclBendLinkConstraintSet", "stretchMaxLength") => value * 0.9416111,
                ("hclLocalRangeConstraintSet", "maximumDistance") => value * 0.9692514,
                ("hclStandardLinkConstraintSet", "restLength") => value * 0.9511670,
                ("hclStretchLinkConstraintSet", "restLength") => value * 0.9021802,
                _ => value
            };
        }

        if (string.Equals(normalized, "Belt_A_Havok", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(className, "hclStretchLinkConstraintSet", StringComparison.Ordinal) &&
            string.Equals(fieldName, "stiffness", StringComparison.Ordinal))
        {
            // The verified Belt_A pair uses a 0.3 hard-stretch stiffness.
            return value * 0.3;
        }

        return value;
    }

    /// <summary>
    /// Reverses the small, field-specific adjustments made while translating
    /// native BPHCL constraints into BotW HKCL constraints. This is used only
    /// by the standalone HKCL-to-BPHCL path for a known matched source file.
    /// </summary>
    public static double ReverseConstraintFieldProfile(
        string? sourcePath,
        string clothName,
        string className,
        string fieldName,
        double value)
    {
        var normalized = Normalize(clothName);
        if (string.Equals(className, "hclBendLinkConstraintSet", StringComparison.Ordinal) &&
            string.Equals(fieldName, "bendMinLength", StringComparison.Ordinal) &&
            (RequiresHair2BendMinimumScale(sourcePath, normalized) ||
             normalized.Contains("Muffler", StringComparison.OrdinalIgnoreCase)))
        {
            return value / 0.8;
        }

        if (string.Equals(normalized, "Hair_1_009_Havok", StringComparison.OrdinalIgnoreCase))
        {
            return (className, fieldName) switch
            {
                ("hclBendLinkConstraintSet", "bendMinLength") => value / 0.7532889,
                ("hclBendLinkConstraintSet", "stretchMaxLength") => value / 0.9416111,
                ("hclLocalRangeConstraintSet", "maximumDistance") => value / 0.9692514,
                ("hclStandardLinkConstraintSet", "restLength") => value / 0.9511670,
                ("hclStretchLinkConstraintSet", "restLength") => value / 0.9021802,
                _ => value
            };
        }

        if (string.Equals(normalized, "Belt_A_Havok", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(className, "hclStretchLinkConstraintSet", StringComparison.Ordinal) &&
            string.Equals(fieldName, "stiffness", StringComparison.Ordinal))
        {
            return value / 0.3;
        }

        return value;
    }

    public static bool ShouldDeriveMissingBendStiffness(string? sourcePath, string clothName)
    {
        var normalized = Normalize(clothName);
        return string.Equals(normalized, "Belt_A_Havok", StringComparison.OrdinalIgnoreCase) ||
            RequiresHair2BendMinimumScale(sourcePath, normalized) ||
            normalized.Contains("Muffler", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresHair2BendMinimumScale(string? sourcePath, string clothName)
    {
        if (!clothName.StartsWith("Hair_2", StringComparison.OrdinalIgnoreCase))
            return false;

        var sourceFile = Path.GetFileNameWithoutExtension(sourcePath ?? string.Empty);
        return sourceFile is "Armor_008_Head" or "Armor_012_Head_A" or "Armor_014_Head" or
            "Armor_020_Head" or "Armor_048_Head";
    }

    private static bool IsHair2Family(string clothName) =>
        clothName.StartsWith("Hair_2", StringComparison.OrdinalIgnoreCase) ||
        clothName.Contains("_Hair_2", StringComparison.OrdinalIgnoreCase);

    private static string GetVerifiedFamily(string clothName)
    {
        foreach (var family in new[]
                 {
                     "Earring", "Muffler", "Apron", "Tunic", "Skirt", "Belt", "Pony",
                     "Dread", "Beard", "Caudal", "Carabiner", "Headrope", "Rope", "Knot", "Hat", "Sode"
                 })
        {
            if (clothName.Contains(family, StringComparison.OrdinalIgnoreCase))
                return family;
        }

        return string.Empty;
    }

    private static string Normalize(string value) => value.StartsWith("Link:", StringComparison.OrdinalIgnoreCase)
        ? value[5..]
        : value;

    private static string NormalizeActorFileName(string value) =>
        value.Replace("_RaulSkin_", "_", StringComparison.OrdinalIgnoreCase);

    private sealed record BphclConversionProfile(
        string SourceFile,
        string ClothName,
        float Scale,
        float? ConstraintScale = null,
        string? Basis = null);

    private sealed record ReverseAampProfile(string SourceFile, string ClothName, string BaseBone, string Preset);

    private sealed record ReverseConstraintExecutionProfile(string SourceFile, string ClothName, int[] Execution);
}

