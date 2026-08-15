namespace HKCLTool;

/// <summary>
/// Per-cloth defaults used while converting BPHCL particle data into BotW
/// HKCL values. An unpaired source may use a structural estimate for mass,
/// while its link and bend coefficients remain at their direct source values.
/// The two values are intentionally independent.
/// </summary>
public sealed record BphclConversionScale(
    int ClothIndex,
    string ClothName,
    int DynamicParticleCount,
    float SourceDynamicMass,
    float DefaultMassScale,
    float DefaultConstraintScale,
    float SuggestedBotwDynamicMass,
    float SuggestedMaxInverseMass,
    string SuggestionBasis);
