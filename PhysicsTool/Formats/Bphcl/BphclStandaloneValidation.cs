namespace HKCLTool;

// Verifies the invariants that matter to a fresh BPHCL writer. Parsing alone
// is not enough: Havok can accept a file whose root tables are incomplete.
public static class BphclStandaloneValidation
{
    public static void Validate(NativeBphclDocument document, StandaloneBphclDocumentGraph graph, bool prunedUnusedColliders = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(graph);

        if (!string.Equals(document.TagFile.Signature, "TAG0", StringComparison.Ordinal) ||
            document.DataSection is null || document.TypeSection is null || document.IndexSection is null ||
            !document.TagFile.Children.Any(section => string.Equals(section.Signature, "SDKV", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Generated BPHCL is missing one or more required TAG0 sections.");
        }

        if (document.Items.Count < 2 || document.Items[0] != new NativeBphclItem(0, 0, 0, 0))
            throw new InvalidDataException("Generated BPHCL does not contain the native ITEM null sentinel.");

        var expectedClothNames = graph.Cloths.Select(cloth => cloth.Name).ToHashSet(StringComparer.Ordinal);
        var actualClothNames = document.Cloths.Select(cloth => cloth.Name).ToHashSet(StringComparer.Ordinal);
        if (!expectedClothNames.SetEquals(actualClothNames))
            throw new InvalidDataException("Generated BPHCL cloth names do not match the source graph.");

        if (document.Skeletons.Count != graph.Cloths.Count)
            throw new InvalidDataException("Generated BPHCL skeleton count does not match the cloth graph.");

        var expectedColliderCount = prunedUnusedColliders
            ? graph.Cloths.SelectMany(cloth => cloth.Colliders).Select(collider => collider.Index).Distinct().Count()
            : graph.OuterColliders.Count;
        if (document.Colliders.Count != expectedColliderCount)
        {
            throw new InvalidDataException(
                $"Generated BPHCL outer collider count ({document.Colliders.Count}) does not match the expected live graph ({expectedColliderCount}).");
        }

        var expectedAampNames = graph.Cloths.Select(cloth => cloth.Name).ToArray();
        if (!document.IsAampReadable || !document.AampClothEntryNames.SequenceEqual(expectedAampNames, StringComparer.Ordinal))
            throw new InvalidDataException("Generated BPHCL AAMP cloth metadata does not match its cloth graph.");

        // Every ITEM must retain the alignment declared by its reflected type.
        // The game-ready merge and compaction passes are allowed to reorder
        // allocations, but never to weaken this Havok loader invariant.
        var alignmentByType = document.TypeDefinitions
            .GroupBy(definition => definition.TypeIndex)
            .ToDictionary(group => group.Key, group => group.First().Alignment ?? 1u);
        var misalignedItem = document.Items
            .Select((item, index) => (item, index, alignment: alignmentByType.GetValueOrDefault(item.TypeIndex, 1u)))
            .FirstOrDefault(entry => entry.alignment > 1 && entry.item.DataOffset % entry.alignment != 0);
        if (misalignedItem.alignment > 1)
        {
            throw new InvalidDataException(
                $"Generated BPHCL ITEM {misalignedItem.index} ({document.GetTypeName(misalignedItem.item.TypeIndex)}) " +
                $"is not aligned to its reflected {misalignedItem.alignment}-byte boundary.");
        }


        var arrayIssues = BphclSchemaAudit.ValidateArrayStorageTypes(document);
        if (arrayIssues.Count > 0)
            throw new InvalidDataException("Generated BPHCL has invalid hkArray storage: " + arrayIssues[0]);

        // Sim-cloth arrays point to collider ITEM records, not their display indexes.
        var outerColliderItemToIndex = document.Colliders.ToDictionary(collider => collider.ItemIndex, collider => collider.Index);
        foreach (var cloth in document.Cloths)
        {
            if (graph.Cloths.FirstOrDefault(candidate => string.Equals(candidate.Name, cloth.Name, StringComparison.Ordinal)) is not { } sourceCloth)
                throw new InvalidDataException($"Generated BPHCL cloth '{cloth.Name}' has no source graph entry.");

            if (sourceCloth.GeneratedSimulateConfig is { } expectedConfig)
            {
                var configs = cloth.Operators.Where(op => op.ClassName == "hclSimulateOperator").SelectMany(op => op.SimulateConfigs).ToArray();
                if (configs.Length != 1 || !configs[0].ConstraintExecution.SequenceEqual(expectedConfig.ConstraintExecution) ||
                    configs[0].SubSteps != expectedConfig.SubSteps || configs[0].NumberOfSolveIterations != expectedConfig.NumberOfSolveIterations ||
                    configs[0].AdaptConstraintStiffness != expectedConfig.AdaptConstraintStiffness ||
                    configs[0].UseAllInstanceCollidables != expectedConfig.UseAllInstanceCollidables ||
                    !configs[0].InstanceCollidablesUsed.SequenceEqual(expectedConfig.InstanceCollidablesUsed))
                    throw new InvalidDataException($"Generated BPHCL cloth '{cloth.Name}' changed its solver configuration.");
                var particles = cloth.SimCloths.SelectMany(sim => sim.Particles).ToArray();
                if (particles.Length != sourceCloth.Particles.Count || particles.Where((p, i) =>
                    p.Mass != sourceCloth.Particles[i].Mass || p.InverseMass != sourceCloth.Particles[i].InverseMass ||
                    p.Fixed != sourceCloth.Particles[i].Fixed).Any())
                    throw new InvalidDataException($"Generated BPHCL cloth '{cloth.Name}' changed its particle masses or fixed membership.");
            }

            foreach (var simulation in cloth.SimCloths)
            {
                var expectedTotalMass = simulation.Particles
                    .Where(particle => !particle.Fixed)
                    .Sum(particle => particle.Mass);
                if (!float.IsFinite(simulation.Runtime.TotalMass) ||
                    MathF.Abs(simulation.Runtime.TotalMass - expectedTotalMass) > 0.00001f)
                {
                    throw new InvalidDataException(
                        $"Generated BPHCL cloth '{cloth.Name}' has an inconsistent simulation total mass.");
                }
            }
            // ITEM/container order is allowed to differ from the source's
            // editor identity. Validate the semantic per-instance collider
            // sequence by name, which is what masks and map slots resolve to.
            var expectedColliderNames = sourceCloth.Colliders
                .Select(collider => collider.Name)
                .ToArray();
            var actualColliderNames = cloth.SimCloths
                .SelectMany(simCloth => simCloth.CollidableItemIndices)
                .Select(itemIndex => outerColliderItemToIndex.TryGetValue(itemIndex, out var colliderIndex)
                    ? document.Colliders[colliderIndex].Name
                    : throw new InvalidDataException(
                        $"Generated BPHCL cloth '{cloth.Name}' references collider ITEM {itemIndex}, which is absent from the outer container."))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (!actualColliderNames.SequenceEqual(expectedColliderNames, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Generated BPHCL cloth '{cloth.Name}' did not preserve its per-instance collider order. " +
                    $"Expected [{string.Join(",", expectedColliderNames)}], got [{string.Join(",", actualColliderNames)}].");
            }
        }
    }
}
