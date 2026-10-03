namespace HKCLTool;

public sealed record ClothConstraintKind(string Name, string ClassName, int ParticleCount, string Description, bool CanCreate = true)
{
    public override string ToString() => CanCreate ? Name : Name + " (inspect only)";
}

public static class ClothConstraintCatalog
{
    public static IReadOnlyList<ClothConstraintKind> Kinds { get; } = new[]
    {
        new ClothConstraintKind("Distance link", "hclStandardLinkConstraintSet", 2,
            "Keeps two particles near a rest length. Both free endpoints move according to inverse mass. Native stiffness is mass-scaled; it is not a universal 0-1 spring strength."),
        new ClothConstraintKind("Stretch limit (A anchors B)", "hclStretchLinkConstraintSet", 2,
            "Limits the maximum separation. A is the anchor and only B is corrected. Reverse A/B to change the driven endpoint. Compression is allowed."),
        new ClothConstraintKind("Bend / stretch range", "hclBendLinkConstraintSet", 2,
            "Keeps separation between a minimum and maximum length, with independent compression and stretch stiffness. Often connects non-neighboring particles to resist folding."),
        new ClothConstraintKind("Local range", "hclLocalRangeConstraintSet", 1,
            "Limits a particle around an animated reference vertex. Needs a valid reference mesh buffer, vertex mapping, and optional normals; these must not be guessed.", false),
        new ClothConstraintKind("Surface bending", "hclBendStiffnessConstraintSet", 4,
            "Resists changes in curvature across a four-particle surface neighborhood. Requires ordered topology, rest curvature, and four geometric weights.", false),
        new ClothConstraintKind("Bone plane", "hclBonePlanesConstraintSet", 1,
            "Constrains a particle against a plane in an animated bone space. Requires a transform-set binding and a normalized local plane equation.", false),
        new ClothConstraintKind("Animation transition", "hclTransitionConstraintSet", 1,
            "Controls timed blending between animation and simulation. Requires a reference vertex and a compatible runtime state/transition setup.", false),
        new ClothConstraintKind("Volume preservation", "hclVolumeConstraint", 0,
            "Preserves a multi-particle volume frame using weighted frame and apply records. It is not a two-particle link and requires a valid volume setup.", false),
        new ClothConstraintKind("Compressible link", "hclCompressibleLinkConstraintSet", 2,
            "A link with separate rest and compression lengths. Its reflected record is known, but authoring and preview behavior are not yet validated.", false),
        new ClothConstraintKind("Anti-pinch recovery", "hclAntiPinchConstraintSet", 0,
            "A specialized collision-recovery constraint family. It requires runtime pinch detection and cannot safely be authored as an ordinary particle link.", false)
    };
}

public sealed record ClothLinkOptions(string ClassName, float Length, float Stiffness,
    float MinimumLength = 0, float StretchStiffness = 0);
