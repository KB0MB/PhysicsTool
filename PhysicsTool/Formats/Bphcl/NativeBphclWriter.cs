using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace HKCLTool;

// Native BPHCL writer for edits that stay within existing allocations. Array
// growth and relocation rebuilding will live in a later allocator layer.
public static class NativeBphclWriter
{
    // Some TotK actor files namespace their shared model bones as Link:Bone,
    // while a donor cloth uses plain Bone names. Retarget only names for which
    // the target already exposes an exact Link: counterpart; authored cloth
    // bones (for example Necklace_1) remain untouched.
    internal static NativeBphclDocument RetargetSourceSkeletonNamespaces(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int sourceSkeletonIndex)
    {
        if ((uint)sourceSkeletonIndex >= (uint)source.Skeletons.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceSkeletonIndex));

        var targetBoneNames = target.Skeletons
            .SelectMany(skeleton => skeleton.Bones)
            .Select(bone => bone.Name)
            .ToHashSet(StringComparer.Ordinal);
        var sourceSkeleton = source.Skeletons[sourceSkeletonIndex];
        var replacements = sourceSkeleton.Bones
            .Where(bone => !bone.Name.StartsWith("Link:", StringComparison.Ordinal) &&
                           targetBoneNames.Contains("Link:" + bone.Name))
            .ToDictionary(bone => bone.Index, bone => "Link:" + bone.Name);

        return replacements.Count == 0
            ? source
            : NativeBphclDocument.Parse(RewriteSkeletonBoneNames(source, sourceSkeleton, replacements));
    }

    // Exercises the native section writer without changing semantic data. It
    // is the first round-trip gate before DATA/ITEM growth is allowed.
    public static void SaveRebuiltCopy(NativeBphclDocument document, string outputPath)
    {
        var data = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var bytes = NativeBphclTagFileBuilder.Rebuild(
            document,
            document.Bytes.AsSpan(data.PayloadOffset, data.PayloadSize),
            document.Items,
            document.InternalPatches);
        File.WriteAllBytes(outputPath, bytes);
    }

    // Diagnostic gate for cross-file imports. This writes a union of the
    // target/source Havok TYPE metadata without changing any target object,
    // pointer, cloth-array, or AAMP data.
    public static void SaveWithMergedTypeMetadata(
        NativeBphclDocument target,
        NativeBphclDocument source,
        string outputPath)
    {
        var data = target.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var typeTable = NativeBphclTypeTable.Create(target, source);
        var items = target.Items
            .Select(item => RemapItemType(item, typeTable.TargetToMerged))
            .ToArray();
        var patches = target.InternalPatches
            .Select(patch => new NativeBphclPatch(
                RemapTypeIndex(patch.TypeIndex, typeTable.TargetToMerged),
                patch.Offsets))
            .ToArray();
        var bytes = NativeBphclTagFileBuilder.Rebuild(
            target,
            target.Bytes.AsSpan(data.PayloadOffset, data.PayloadSize),
            items,
            patches,
            typeTable.ReplacementSection);
        _ = NativeBphclDocument.Parse(bytes);
        File.WriteAllBytes(outputPath, bytes);
    }

    // Imports one complete cloth package through the native DATA/ITEM/PTCH
    // path. The source and target must expose compatible Havok type names.
    public static void SaveMergedCloth(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int sourceClothIndex,
        string outputPath)
    {
        // Preserve the target's existing ITEM indices. The importer appends
        // the donor graph and remaps its pointers. Full live-graph relocation
        // remains a separate, not-yet game-validated pass.
        var merged = NativeBphclMerge.MergeCompleteCloth(target, source, sourceClothIndex);
        _ = NativeBphclDocument.Parse(merged);
        File.WriteAllBytes(outputPath, merged);
    }

    // Replaces a cloth package in its existing container slot. This is useful
    // for controlled tests and later for deliberate cloth replacement: it
    // keeps cloth/skeleton/AAMP ordering stable instead of appending a second
    // entry with the same logical simulation-mesh name.
    public static void SaveReplacedCloth(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int targetClothIndex,
        int sourceClothIndex,
        string outputPath)
    {
        var replaced = NativeBphclMerge.ReplaceCompleteCloth(
            target,
            source,
            targetClothIndex,
            sourceClothIndex);
        _ = NativeBphclDocument.Parse(replaced);
        File.WriteAllBytes(outputPath, replaced);
    }

    public static void SaveWithoutCloth(NativeBphclDocument target, int clothIndex, string outputPath)
    {
        var deleted = NativeBphclMerge.DeleteCloth(target, clothIndex);
        // Re-open the logical deletion before compaction so root arrays, AAMP
        // metadata, and collider pruning all participate in the live closure.
        // Compact keeps the implicit hkRootLevelContainer at DATA+0 / ITEM 1.
        var compacted = NativeBphclCompactor.Compact(NativeBphclDocument.Parse(deleted));
        _ = NativeBphclDocument.Parse(compacted);
        File.WriteAllBytes(outputPath, compacted);
    }

    // Optional cleanup pass used only after a delete/merge has been
    // validated. It trims live collider references and AAMP metadata without
    // relocating arbitrary DATA allocations.
    public static void SaveWithUnreferencedCollidersPruned(
        NativeBphclDocument target,
        string outputPath)
    {
        var pruned = NativeBphclMerge.PruneUnreferencedColliders(target);
        _ = NativeBphclDocument.Parse(pruned);
        File.WriteAllBytes(outputPath, pruned);
    }

    // Retargeting experiment: keep the skeleton's structure and transforms,
    // but replace the Link: namespace used by some BPHCL skeletons with the
    // underlying model-bone name. Existing pointer patches remain valid.
    public static void SaveWithoutLinkBonePrefixes(NativeBphclDocument document, string outputPath)
    {
        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, dataSection.PayloadSize)
            .ToArray()
            .ToList();
        var items = document.Items.ToList();

        foreach (var skeleton in document.Skeletons)
        {
            if (!document.TryGetReferencedItem(skeleton.DataOffset + 48, out var boneArrayItemIndex))
                throw new InvalidDataException($"BPHCL skeleton '{skeleton.Name}' has no readable bone array.");
            var boneArrayItem = document.GetItem(boneArrayItemIndex);

            for (var index = 0; index < skeleton.Bones.Count && index < boneArrayItem.Count; index++)
            {
                var bone = skeleton.Bones[index];
                if (!bone.Name.StartsWith("Link:", StringComparison.Ordinal))
                    continue;

                var boneOffset = checked(boneArrayItem.DataOffset + (uint)(index * 16));
                if (!document.TryGetReferencedItem(boneOffset, out var oldNameItemIndex))
                    throw new InvalidDataException($"BPHCL bone '{bone.Name}' has no readable name pointer.");

                var oldNameItem = document.GetItem(oldNameItemIndex);
                var newNameBytes = Encoding.UTF8.GetBytes(bone.Name[5..]);
                Align(data, 8);
                var newNameOffset = checked((uint)data.Count);
                data.AddRange(newNameBytes);
                data.Add(0);

                var newNameItemIndex = items.Count;
                items.Add(oldNameItem with
                {
                    DataOffset = newNameOffset,
                    Count = checked((uint)(newNameBytes.Length + 1))
                });
                BinaryPrimitives.WriteUInt32LittleEndian(
                    CollectionsMarshal.AsSpan(data).Slice(checked((int)boneOffset), 4),
                    checked((uint)newNameItemIndex));
            }
        }

        var rebuilt = NativeBphclTagFileBuilder.Rebuild(
            document,
            CollectionsMarshal.AsSpan(data),
            items,
            document.InternalPatches);
        File.WriteAllBytes(outputPath, rebuilt);
    }

    private static byte[] RewriteSkeletonBoneNames(
        NativeBphclDocument document,
        NativeBphclSkeleton skeleton,
        IReadOnlyDictionary<int, string> replacements)
    {
        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, dataSection.PayloadSize)
            .ToArray()
            .ToList();
        var items = document.Items.ToList();

        if (!document.TryGetReferencedItem(skeleton.DataOffset + 48, out var boneArrayItemIndex))
            throw new InvalidDataException($"BPHCL skeleton '{skeleton.Name}' has no readable bone array.");
        var boneArrayItem = document.GetItem(boneArrayItemIndex);

        foreach (var bone in skeleton.Bones)
        {
            if (!replacements.TryGetValue(bone.Index, out var replacementName))
                continue;
            if (bone.Index >= boneArrayItem.Count)
                throw new InvalidDataException($"BPHCL bone '{bone.Name}' lies outside its bone array.");

            var boneOffset = checked(boneArrayItem.DataOffset + (uint)(bone.Index * 16));
            if (!document.TryGetReferencedItem(boneOffset, out var oldNameItemIndex))
                throw new InvalidDataException($"BPHCL bone '{bone.Name}' has no readable name pointer.");

            var oldNameItem = document.GetItem(oldNameItemIndex);
            var newNameBytes = Encoding.UTF8.GetBytes(replacementName);
            Align(data, 8);
            var newNameOffset = checked((uint)data.Count);
            data.AddRange(newNameBytes);
            data.Add(0);

            var newNameItemIndex = items.Count;
            items.Add(oldNameItem with
            {
                DataOffset = newNameOffset,
                Count = checked((uint)(newNameBytes.Length + 1))
            });
            BinaryPrimitives.WriteUInt32LittleEndian(
                CollectionsMarshal.AsSpan(data).Slice(checked((int)boneOffset), 4),
                checked((uint)newNameItemIndex));
        }

        return NativeBphclTagFileBuilder.Rebuild(
            document,
            CollectionsMarshal.AsSpan(data),
            items,
            document.InternalPatches);
    }

    // Cloth names are pointer-backed UTF-8 strings. Add a fresh string ITEM
    // and repoint the cloth so renames are not limited by the old name length.
    public static void SaveRenamedCloth(
        NativeBphclDocument document,
        int clothIndex,
        string name,
        string outputPath)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A cloth name cannot be empty.", nameof(name));
        if (name.IndexOf('\0') >= 0)
            throw new ArgumentException("A cloth name cannot contain a null character.", nameof(name));

        var cloth = document.Cloths.ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var replacementAamp = NativeAampClothMerger.RenameClothEntry(document, cloth.Name, name);

        // hclClothData begins with hkReferencedObject (24 bytes), followed by
        // its name pointer. The existing pointer has an INDX relocation entry.
        var namePointerOffset = checked(cloth.DataOffset + 24u);
        if (!document.TryGetReferencedItem(namePointerOffset, out var oldStringItemIndex))
            throw new InvalidDataException("BPHCL cloth name pointer could not be resolved.");

        var oldStringItem = document.GetItem(oldStringItemIndex);
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, dataSection.PayloadSize)
            .ToArray()
            .ToList();
        Align(data, 8);
        var newStringOffset = checked((uint)data.Count);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        data.AddRange(nameBytes);
        data.Add(0);

        var items = document.Items.ToList();
        var newStringItemIndex = items.Count;
        items.Add(oldStringItem with
        {
            DataOffset = newStringOffset,
            Count = checked((uint)(nameBytes.Length + 1))
        });

        BinaryPrimitives.WriteUInt32LittleEndian(
            CollectionsMarshal.AsSpan(data).Slice(checked((int)namePointerOffset), 4),
            checked((uint)newStringItemIndex));

        var rebuilt = NativeBphclTagFileBuilder.Rebuild(
            document,
            CollectionsMarshal.AsSpan(data),
            items,
            document.InternalPatches,
            replacementAamp: replacementAamp);
        // Keep the original object layout. Compaction is a separate,
        // game-validation task and must never be a side effect of renaming.
        File.WriteAllBytes(outputPath, rebuilt);
    }

    public static void SaveCompactedCopy(NativeBphclDocument document, string outputPath)
    {
        File.WriteAllBytes(outputPath, NativeBphclCompactor.Compact(document));
    }

    public static void SaveReindexedDiagnosticCopy(NativeBphclDocument document, string outputPath)
    {
        File.WriteAllBytes(outputPath, NativeBphclCompactor.ReindexAllItemsForDiagnostic(document));
    }

    public static void SaveInactiveItemsReindexedDiagnosticCopy(NativeBphclDocument document, string outputPath)
    {
        File.WriteAllBytes(outputPath, NativeBphclCompactor.ReindexInactiveItemsForDiagnostic(document));
    }

    public static void SaveIndexStableCompactedCopy(NativeBphclDocument document, string outputPath)
    {
        File.WriteAllBytes(outputPath, NativeBphclCompactor.CompactPreservingItemIndices(document));
    }

    public static void SaveParticleEdit(
        NativeBphclDocument document,
        string outputPath,
        int clothIndex,
        int simulationIndex,
        int particleIndex,
        NativeBphclParticleEdit edit)
    {
        var particle = document.Cloths
            .ElementAtOrDefault(clothIndex)?.SimCloths
            .ElementAtOrDefault(simulationIndex)?.Particles
            .ElementAtOrDefault(particleIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(particleIndex));

        if (particle.PhysicsDataOffset == 0)
            throw new InvalidDataException("This BPHCL particle has no writable physics-data offset.");

        var bytes = (byte[])document.Bytes.Clone();
        var dataStart = document.DataSection?.PayloadOffset
            ?? throw new InvalidDataException("BPHCL has no DATA section.");

        if (edit.Position is { } position)
        {
            if (particle.PositionDataOffset == 0)
                throw new InvalidDataException("This BPHCL particle has no writable pose offset.");
            WriteVector4(bytes, checked(dataStart + (int)particle.PositionDataOffset), position);
        }

        if (edit.Mass is { } mass)
        {
            if (mass < 0)
                throw new ArgumentOutOfRangeException(nameof(edit), "Particle mass cannot be negative.");
            WriteSingle(bytes, checked(dataStart + (int)particle.PhysicsDataOffset), mass);
            WriteSingle(bytes, checked(dataStart + (int)particle.PhysicsDataOffset + 4), mass == 0 ? 0 : 1f / mass);
        }
        if (edit.Radius is { } radius)
        {
            if (radius < 0)
                throw new ArgumentOutOfRangeException(nameof(edit), "Particle radius cannot be negative.");
            WriteSingle(bytes, checked(dataStart + (int)particle.PhysicsDataOffset + 8), radius);
        }
        if (edit.Friction is { } friction)
            WriteSingle(bytes, checked(dataStart + (int)particle.PhysicsDataOffset + 12), friction);

        File.WriteAllBytes(outputPath, bytes);
    }

    private static void WriteVector4(byte[] bytes, int offset, System.Numerics.Vector4 value)
    {
        WriteSingle(bytes, offset, value.X);
        WriteSingle(bytes, offset + 4, value.Y);
        WriteSingle(bytes, offset + 8, value.Z);
        WriteSingle(bytes, offset + 12, value.W);
    }

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));

    private static void Align(List<byte> data, int alignment)
    {
        while (data.Count % alignment != 0)
            data.Add(0);
    }

    private static NativeBphclItem RemapItemType(
        NativeBphclItem item,
        IReadOnlyDictionary<uint, uint> typeMap)
    {
        var typeIndex = RemapTypeIndex(item.TypeIndex, typeMap);
        return item with { Flags = (item.Flags & 0xff00_0000u) | typeIndex, TypeIndex = typeIndex };
    }

    private static uint RemapTypeIndex(uint typeIndex, IReadOnlyDictionary<uint, uint> typeMap)
    {
        if (!typeMap.TryGetValue(typeIndex, out var remapped))
            throw new InvalidDataException($"BPHCL TYPE map has no entry for type {typeIndex}.");
        return remapped;
    }
}
