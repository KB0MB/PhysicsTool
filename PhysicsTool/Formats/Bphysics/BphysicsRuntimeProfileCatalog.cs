using System;
using System.IO;

namespace HKCLTool;

/// <summary>
/// Exact runtime-sidecar values recovered from paired vanilla BPHYSICS files.
/// The catalog only overrides the generic export defaults for a known HKCL
/// resource and cloth name; unknown files remain fully editable fallbacks.
/// </summary>
internal static class BphysicsRuntimeProfileCatalog
{
    public static bool TryGetDocumentWind(string hkclPath, out float frequency, out float speed)
    {
        switch (GetResourceName(hkclPath))
        {
            case "Armor_179_Head":
                frequency = 5.0f;
                speed = 50.0f;
                return true;
            case "Armor_182_Head":
                frequency = 0.2f;
                speed = 0.0f;
                return true;
            default:
                frequency = 5.0f;
                speed = 50.0f;
                return false;
        }
    }

    public static bool TryGetCloth(string hkclPath, string clothName, out BphysicsClothProfile profile)
    {
        var resource = GetResourceName(hkclPath);
        var name = StripLinkPrefix(clothName);
        profile = (resource, name) switch
        {
            ("Armor_179_Head", "Hat_179_Havok") => new(name, "Head", 3.0f, 4.0f, -5.0f, 10.0f),
            ("Armor_179_Head", "Hair_1_179_Havok") => new(name, "Head", 3.0f, 8.0f, -2.0f, 12.0f),
            ("Armor_179_Head", "Hair_3_179_Havok") => new(name, "Hair_Root", 2.63f, 5.0f, -2.0f, 10.0f),
            ("Armor_179_Head", "Hair_2_179_Havok") => new(name, "Hair_Root", 2.69f, 10.0f, -2.0f, 12.0f),
            ("Armor_179_Head", "Hair_D_179_Havok") => new(name, "Hair_Root", 5.12f, 8.0f, -2.0f, 12.0f),
            ("Armor_179_Head", "Hair_B_179_Havok") => new(name, "Hair_Root", 5.12f, 8.0f, -2.0f, 12.0f),
            ("Armor_182_Head", "Hair_Back_Havok") => new(name, "Head", 5.0f, 5.0f, -4.0f, 10.0f),
            ("Armor_182_Head", "Hair_2_Havok") => new(name, "Head", 5.0f, 5.0f, -4.0f, 10.0f),
            _ => null!
        };

        return profile != null;
    }

    private static string GetResourceName(string hkclPath) => Path.GetFileNameWithoutExtension(hkclPath.Replace('/', '\\'));

    private static string StripLinkPrefix(string value) => value.StartsWith("Link:", StringComparison.OrdinalIgnoreCase)
        ? value[5..]
        : value;
}
