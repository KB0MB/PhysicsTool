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
        new("Armor_048_Head", "Dread_Havok", 2f),
        new("Armor_048_Head", "Hair_2_Havok", 30.0000019f),
        new("Armor_048_Head", "Pony_Havok", 2.50000024f),
        new("Armor_048_Upper", "AccSpine_Havok", 3f),
        new("Armor_048_Upper", "Skirt_Havok", 2f),
        new("Armor_179_Head", "Hair_1_179_Havok", 6.42588186f),
        new("Armor_179_Head", "Hair_2_179_Havok", 30f),
        new("Armor_179_Head", "Hair_3_179_Havok", 12f),
        new("Armor_179_Head", "Hair_B_179_Havok", 4.02698898f),
        new("Armor_179_Head", "Hair_D_179_Havok", 3f),
        new("Armor_179_Head", "Hat_179_Havok", 16f),
        new("Armor_179_Upper", "Sode_179_Havok", 29.9999981f),
        new("Armor_182_Head", "Hair_2_Havok", 30.0000019f),
        new("Armor_182_Head", "Hair_Back_Havok", 7.50394678f)
    ];

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
            basis = $"Verified match: {exact.SourceFile}";
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

        scale = 1.0f;
        basis = "Topology fallback";
        return false;
    }

    private static string Normalize(string value) => value.StartsWith("Link:", StringComparison.OrdinalIgnoreCase)
        ? value[5..]
        : value;

    private sealed record BphclConversionProfile(string SourceFile, string ClothName, float Scale);
}
