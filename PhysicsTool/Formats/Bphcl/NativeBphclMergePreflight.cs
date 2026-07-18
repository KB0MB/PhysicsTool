using System.Text;

namespace HKCLTool;

// Describes exactly what the conservative BPHCL merge will import. The UI
// uses this same graph/type analysis as the writer, so it cannot promise a
// merge that the native serializer will later reject.
internal sealed record NativeBphclMergePreflight(
    int SourceClothIndex,
    string ClothName,
    int SimulationCount,
    int ParticleCount,
    int BoneCount,
    int ConstraintSetCount,
    IReadOnlyList<string> ColliderNames,
    IReadOnlyList<string> MissingTypes,
    bool NameAlreadyExists,
    bool HasPairedSkeleton)
{
    public bool IsSafe => HasPairedSkeleton && !NameAlreadyExists && MissingTypes.Count == 0;
    public bool CanAttemptExperimentalTypeUnion => HasPairedSkeleton && !NameAlreadyExists;

    public static NativeBphclMergePreflight Analyze(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int sourceClothIndex)
    {
        var cloth = source.Cloths.ElementAtOrDefault(sourceClothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(sourceClothIndex));
        var skeleton = source.Skeletons.ElementAtOrDefault(sourceClothIndex);
        if (skeleton == null)
        {
            return new NativeBphclMergePreflight(
                sourceClothIndex,
                cloth.Name,
                cloth.SimClothCount,
                0,
                0,
                0,
                Array.Empty<string>(),
                Array.Empty<string>(),
                false,
                false);
        }

        var itemIndices = source.CollectItemClosure(new[] { cloth.ItemIndex, skeleton.ItemIndex });
        var patches = source.GetPatchesForItems(itemIndices);
        var requiredTypeIndices = itemIndices
            .Select(itemIndex => source.GetItem(itemIndex).TypeIndex)
            .Concat(patches.Select(patch => patch.TypeIndex))
            .Distinct()
            .ToArray();
        var usedColliderItems = cloth.SimCloths
            .SelectMany(simCloth => simCloth.CollidableItemIndices)
            .ToHashSet();
        var colliders = source.Colliders
            .Where(collider => usedColliderItems.Contains(collider.ItemIndex))
            .Select(collider => $"{collider.Name} ({collider.Shape.TypeName})")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return new NativeBphclMergePreflight(
            sourceClothIndex,
            cloth.Name,
            cloth.SimClothCount,
            cloth.SimCloths.Sum(simCloth => simCloth.Particles.Count),
            skeleton.BoneCount,
            cloth.SimCloths.Sum(simCloth => simCloth.ConstraintSetCount),
            colliders,
            NativeBphclTypeTable.FindMissingRequiredTypes(target, source, requiredTypeIndices),
            target.Cloths.Any(targetCloth => string.Equals(targetCloth.Name, cloth.Name, StringComparison.Ordinal)),
            true);
    }

    public string ToDisplayText()
    {
        var lines = new List<string>
        {
            "BPHCL complete-cloth merge preflight",
            string.Empty,
            $"Donor cloth: {ClothName}",
            $"Simulation cloths: {SimulationCount}",
            $"Particles: {ParticleCount}",
            $"Bones: {BoneCount}",
            $"Constraint sets: {ConstraintSetCount}",
            $"Referenced colliders: {ColliderNames.Count}",
            string.Empty
        };

        if (!HasPairedSkeleton)
            lines.Add("Blocked: the donor has no skeleton at this cloth slot.");
        if (NameAlreadyExists)
            lines.Add("Blocked: the target already has a cloth with this mesh name. Rename or remove it first.");
        if (MissingTypes.Count > 0)
            lines.Add("Additional TYPE layouts required: " + string.Join(", ", MissingTypes));

        if (ColliderNames.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Imported colliders:");
            lines.AddRange(ColliderNames.Select(name => "  " + name));
        }

        lines.Add(string.Empty);
        lines.Add(IsSafe
            ? "Ready: all required Havok TYPE layouts already exist in the target."
            : CanAttemptExperimentalTypeUnion
                ? "Beta path: source-only reflection layouts will be added and imported type indexes remapped. Back up the target and test in-game."
                : "No file will be written until the missing cloth pairing or duplicate-name issue is resolved.");
        return string.Join(Environment.NewLine, lines);
    }
}
