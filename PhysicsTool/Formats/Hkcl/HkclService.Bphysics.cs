using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    /// <summary>
    /// Creates a BotW runtime-sidecar model from the active HKCL or BPHCL. The sidecar
    /// contains attachment/wind settings, not the Havok simulation itself.
    /// </summary>
    public BphysicsDocument CreateBphysicsDocument(string hkclGamePath)
    {
        if (!HasDocument || (IsReadOnlyExternal && !IsBphcl))
            throw new InvalidOperationException("BPHYSICS export requires an HKCL or BPHCL cloth document.");

        var document = new BphysicsDocument { HkclPath = hkclGamePath };
        if (BphysicsRuntimeProfileCatalog.TryGetDocumentWind(hkclGamePath, out var subWindFrequency, out var subWindSpeed))
        {
            document.SubWindFrequency = subWindFrequency;
            document.SubWindSpeed = subWindSpeed;
        }
        for (var index = 0; index < GetClothSummaries().Count; index++)
        {
            var clothName = StripRuntimePrefix(GetClothName(index));
            var boneNames = GetBoneRows(index).Select(b => StripRuntimePrefix(b.Name))
                .Where(name => !string.IsNullOrWhiteSpace(name)).ToHashSet(StringComparer.Ordinal);
            document.Cloths.Add(
                BphysicsRuntimeProfileCatalog.TryGetCloth(hkclGamePath, clothName, out var verifiedProfile)
                    ? verifiedProfile
                    : CreateFallbackBphysicsProfile(clothName, boneNames, GetSimulationRuntimeData(index).TotalMass));
        }

        return document;
    }

    private static BphysicsClothProfile CreateFallbackBphysicsProfile(string clothName, ISet<string> boneNames, float totalMass)
    {
        if (!float.IsFinite(totalMass) || totalMass < 0)
            throw new InvalidOperationException($"Cloth '{clothName}' has an invalid total mass. Correct its mass before exporting wind settings.");

        // Generic defaults use the armor survey in docs/BPHYSICS_WIND_DEFAULTS.md.
        // Keep the existing accessory-specific tuning and editable export values.
        var baseBone = ChooseBphysicsBaseBone(clothName, boneNames);
        var profile = new BphysicsClothProfile(clothName, baseBone);

        if (clothName.Contains("Earring", StringComparison.OrdinalIgnoreCase))
            profile = profile with { WindFrequency = 3.62f, WindDrag = 5.0f, WindMaxSpeed = 8.0f, SubWindFactorMain = 0.0f };
        else if (clothName.Contains("Belt", StringComparison.OrdinalIgnoreCase) ||
            clothName.Contains("Tunic", StringComparison.OrdinalIgnoreCase) ||
            clothName.Contains("Apron", StringComparison.OrdinalIgnoreCase))
            profile = profile with { WindFrequency = 3.5f, WindDrag = 8.0f, WindMaxSpeed = 10.0f };
        else if (clothName.Contains("Hair", StringComparison.OrdinalIgnoreCase))
            profile = profile with { WindFrequency = 5.0f, WindDrag = 8.0f, WindMaxSpeed = 12.0f };

        // BotW divides drag by total cloth mass. The paired vanilla armor median
        // is 4; cap at the old category default so heavier cloths are not amplified.
        const float armorMedianDragPerMass = 4.0f;
        var drag = (float)Math.Min(profile.WindDrag, (double)totalMass * armorMedianDragPerMass);
        return profile with { WindDrag = drag, WindEnabled = totalMass > 0 };
    }

    private static string ChooseBphysicsBaseBone(string clothName, ISet<string> bones)
    {
        var candidates = clothName.Contains("Hair", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Hair_Root", "Head", "Spine_2", "Waist" }
            : clothName.Contains("Belt", StringComparison.OrdinalIgnoreCase) || clothName.Contains("Tunic", StringComparison.OrdinalIgnoreCase)
                ? new[] { "Waist", "Spine_2", "Head", "Hair_Root" }
                : new[] { "Head", "Spine_2", "Waist", "Hair_Root" };

        return candidates.FirstOrDefault(bones.Contains) ?? bones.FirstOrDefault() ?? "Head";
    }

    private static string StripRuntimePrefix(string value) => value.StartsWith("Link:", StringComparison.OrdinalIgnoreCase)
        ? value[5..]
        : value;
}
