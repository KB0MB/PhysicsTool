namespace HKCLTool;

/// <summary>
/// A per-cloth solver scale used while converting BPHCL particle and
/// constraint data into BotW HKCL values.
/// </summary>
public sealed record BphclConversionScale(
    int ClothIndex,
    string ClothName,
    float DefaultScale,
    string SuggestionBasis);
