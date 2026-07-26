using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace HKCLTool;

// Conservative native BPHCL cloth merge. It imports an entire source DATA
// graph, remaps every ITEM reference through PTCH, then replaces the target
// root arrays with newly allocated reference blocks. No spare slots or Python
// object model are involved.
internal static class NativeBphclMerge
{
    // First-stage cleanup: remove colliders that no active cloth references
    // from the live root array and embedded AAMP list. Their DATA/ITEM
    // allocations deliberately remain in place until full relocation has an
    // in-game validation path.
    public static byte[] PruneUnreferencedColliders(NativeBphclDocument target)
    {
        var targetData = target.DataSection
            ?? throw new InvalidDataException("Target BPHCL has no DATA section.");
        var clothContainer = target.FindRootVariants("hclClothContainer").SingleOrDefault()
            ?? throw new InvalidDataException("Target BPHCL has no hclClothContainer root variant.");
        var referencedItems = target.Cloths
            .SelectMany(cloth => cloth.SimCloths)
            .SelectMany(simulation => simulation.CollidableItemIndices)
            .ToHashSet();
        var retainedColliders = target.Colliders
            .Where(collider => referencedItems.Contains(collider.ItemIndex))
            .ToArray();
        if (retainedColliders.Length == target.Colliders.Count)
            return target.Bytes;

        var replacementAamp = NativeAampClothMerger.KeepColliderEntries(
            target,
            retainedColliders.Select(collider => collider.Name));
        var data = target.Bytes
            .AsSpan(targetData.PayloadOffset, targetData.PayloadSize)
            .ToArray()
            .ToList();
        var items = target.Items.ToList();
        var patches = CreateMutablePatchGroups(target.InternalPatches);
        var colliderArray = target.GetReferenceArray(clothContainer.ObjectDataOffset + 24);
        ReplaceReferenceArray(
            target,
            data,
            items,
            patches,
            colliderArray,
            retainedColliders.Select(collider => collider.ItemIndex));

        return NativeBphclTagFileBuilder.Rebuild(
            target,
            CollectionsMarshal.AsSpan(data),
            items,
            patches.Select(group => new NativeBphclPatch(group.TypeIndex, group.Offsets)).ToArray(),
            replacementAamp: replacementAamp);
    }

    public static byte[] DeleteCloth(NativeBphclDocument target, int clothIndex)
    {
        if ((uint)clothIndex >= (uint)target.Cloths.Count)
            throw new ArgumentOutOfRangeException(nameof(clothIndex));

        var deletedCloth = target.Cloths[clothIndex];
        var replacementAamp = target.Aamp.RemoveClothEntry(target.Bytes, deletedCloth.Name);
        var targetData = target.DataSection
            ?? throw new InvalidDataException("Target BPHCL has no DATA section.");
        var data = target.Bytes.AsSpan(targetData.PayloadOffset, targetData.PayloadSize).ToArray().ToList();
        var items = target.Items.ToList();
        var patches = CreateMutablePatchGroups(target.InternalPatches);
        var clothContainer = target.FindRootVariants("hclClothContainer").SingleOrDefault()
            ?? throw new InvalidDataException("Target BPHCL has no hclClothContainer root variant.");

        var clothArray = target.GetReferenceArray(clothContainer.ObjectDataOffset + 40);
        var clothEntries = target.GetReferenceItemIndices(clothArray.FieldOffset).ToList();
        clothEntries.RemoveAt(clothIndex);
        ReplaceReferenceArray(target, data, items, patches, clothArray, clothEntries);

        // Cloth and skeleton entries are paired by index in the known BPHCL
        // containers.
        var animationContainer = target.FindRootVariants("hkaAnimationContainer").SingleOrDefault();
        if (animationContainer is not null)
        {
            var skeletonArray = target.GetReferenceArray(animationContainer.ObjectDataOffset + 24);
            var skeletonEntries = target.GetReferenceItemIndices(skeletonArray.FieldOffset).ToList();
            if (clothIndex < skeletonEntries.Count)
            {
                skeletonEntries.RemoveAt(clothIndex);
                ReplaceReferenceArray(target, data, items, patches, skeletonArray, skeletonEntries);
            }
        }

        var deleted = NativeBphclTagFileBuilder.Rebuild(
            target,
            CollectionsMarshal.AsSpan(data),
            items,
            patches.Select(group => new NativeBphclPatch(group.TypeIndex, group.Offsets)).ToArray(),
            replacementAamp: replacementAamp);

        // The first safe cleanup layer updates only live root references and
        // AAMP metadata. Keep the original DATA/ITEM allocations untouched.
        return PruneUnreferencedColliders(NativeBphclDocument.Parse(deleted));
    }

    public static byte[] MergeCompleteCloth(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int sourceClothIndex) =>
        ImportCompleteCloth(target, source, sourceClothIndex, replaceTargetClothIndex: null, reuseCompatibleColliders: true);

    // Duplication is intentionally different from cross-file import: a
    // duplicate must own its colliders so future edits cannot move the source
    // cloth's body collision objects as a side effect.
    public static byte[] DuplicateCompleteCloth(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int sourceClothIndex) =>
        ImportCompleteCloth(target, source, sourceClothIndex, replaceTargetClothIndex: null, reuseCompatibleColliders: false);

    public static byte[] ReplaceCompleteCloth(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int targetClothIndex,
        int sourceClothIndex) =>
        ImportCompleteCloth(target, source, sourceClothIndex, targetClothIndex, reuseCompatibleColliders: true);

    private static byte[] ImportCompleteCloth(
        NativeBphclDocument target,
        NativeBphclDocument source,
        int sourceClothIndex,
        int? replaceTargetClothIndex,
        bool reuseCompatibleColliders)
    {
        if ((uint)sourceClothIndex >= (uint)source.Cloths.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceClothIndex));
        if ((uint)sourceClothIndex >= (uint)source.Skeletons.Count)
        {
            throw new InvalidDataException(
                "The imported BPHCL does not have a skeleton at the matching cloth index.");
        }

        // Match a donor's shared body-bone namespace to the target before
        // collecting its closure. This only changes names with a verified
        // Link: counterpart in the target and leaves authored cloth bones
        // intact.
        source = NativeBphclWriter.RetargetSourceSkeletonNamespaces(
            target,
            source,
            sourceClothIndex);

        var sourceCloth = source.Cloths[sourceClothIndex];
        if (replaceTargetClothIndex is int replacementIndex)
        {
            if ((uint)replacementIndex >= (uint)target.Cloths.Count)
                throw new ArgumentOutOfRangeException(nameof(replaceTargetClothIndex));
            if (!string.Equals(target.Cloths[replacementIndex].Name, sourceCloth.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A native BPHCL replacement must keep the same cloth mesh name. " +
                    "Use a retargeting conversion for a differently named cloth.");
            }
        }
        else if (target.Cloths.Any(cloth => string.Equals(
                     cloth.Name,
                     sourceCloth.Name,
                     StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                $"The target already contains a cloth named '{sourceCloth.Name}'. " +
                "Remove or rename that target cloth before importing another one with the same mesh name.");
        }

        var targetData = target.DataSection
            ?? throw new InvalidDataException("Target BPHCL has no DATA section.");
        var sourceItemIndices = source.CollectItemClosure(new[]
        {
            sourceCloth.ItemIndex,
            source.Skeletons[sourceClothIndex].ItemIndex
        });
        // Import only the selected cloth's reachable object graph. Importing
        // every donor ITEM made a one-cloth merge carry unrelated Phives,
        // root containers, and their metadata into the target archive.
        var sourcePatches = source.GetPatchesForItems(sourceItemIndices);
        var replacementAamp = replaceTargetClothIndex is null
            ? NativeAampClothMerger.AppendClothEntry(target, source, sourceCloth.Name)
            : NativeAampClothMerger.GetOriginalArchive(target);
        var requiredSourceTypes = sourceItemIndices
            .Select(itemIndex => source.GetItem(itemIndex).TypeIndex)
            .Concat(sourcePatches.Select(patch => patch.TypeIndex))
            .Distinct()
            .ToArray();
        var typeTable = NativeBphclTypeTable.Create(target, source, requiredSourceTypes);
        var typeMap = typeTable.SourceToMerged;
        var liveSourceItems = sourceItemIndices.ToHashSet();
        var reusedColliderItems = reuseCompatibleColliders
            ? FindReusableColliders(target, source, liveSourceItems)
            : new Dictionary<int, int>();
        replacementAamp = NativeAampClothMerger.AppendColliderEntries(
            replacementAamp,
            source,
            source.Colliders
                .Where(collider => liveSourceItems.Contains(collider.ItemIndex) &&
                                   !reusedColliderItems.ContainsKey(collider.ItemIndex))
                .Select(collider => collider.Name));

        var data = target.Bytes.AsSpan(targetData.PayloadOffset, targetData.PayloadSize).ToArray().ToList();
        var importedRanges = CopySourceClosureData(source, sourceItemIndices, data);

        var items = target.Items
            .Select(item => RemapTargetItem(item, typeTable.TargetToMerged))
            .ToList();
        var importedItemMap = new Dictionary<int, int>();
        foreach (var sourceItemIndex in sourceItemIndices)
        {
            var sourceItem = source.GetItem(sourceItemIndex);
            var targetTypeIndex = RemapTypeIndex(sourceItem.TypeIndex, typeMap);
            var flags = (sourceItem.Flags & 0xff00_0000u) | targetTypeIndex;
            var sourceRange = importedRanges[sourceItem.DataOffset];
            importedItemMap.Add(sourceItemIndex, items.Count);
            items.Add(new NativeBphclItem(
                flags,
                targetTypeIndex,
                checked(sourceRange.NewStart + sourceItem.DataOffset - sourceRange.OldStart),
                sourceItem.Count));
        }

        RemapImportedPointers(
            data,
            source,
            sourcePatches,
            importedRanges.Values,
            importedItemMap,
            reusedColliderItems);

        var patches = CreateMutablePatchGroups(target.InternalPatches, typeTable.TargetToMerged);
        foreach (var sourcePatch in sourcePatches)
        {
            var targetTypeIndex = RemapTypeIndex(sourcePatch.TypeIndex, typeMap);
            foreach (var offset in sourcePatch.Offsets)
            {
                var range = FindImportedRange(importedRanges.Values, offset);
                AddPatch(patches, targetTypeIndex, checked(range.NewStart + offset - range.OldStart));
            }
        }

        var clothContainer = target.FindRootVariants("hclClothContainer").SingleOrDefault()
            ?? throw new InvalidDataException("Target BPHCL has no hclClothContainer root variant.");
        var animationContainer = target.FindRootVariants("hkaAnimationContainer").SingleOrDefault()
            ?? throw new InvalidDataException("Target BPHCL has no hkaAnimationContainer root variant.");

        // hclClothContainer: collidables at +0x18, cloth data at +0x28.
        // hkaAnimationContainer: skeletons at +0x18. These offsets are
        // validated by the native reader for both current BPHCL samples.
        AppendReferenceArray(
            target,
            data,
            items,
            patches,
            target.GetReferenceArray(clothContainer.ObjectDataOffset + 24),
            source.Colliders
                .Where(collider => liveSourceItems.Contains(collider.ItemIndex) &&
                                   !reusedColliderItems.ContainsKey(collider.ItemIndex))
                .Select(collider => importedItemMap[collider.ItemIndex]));
        var clothArray = target.GetReferenceArray(clothContainer.ObjectDataOffset + 40);
        var skeletonArray = target.GetReferenceArray(animationContainer.ObjectDataOffset + 24);
        if (replaceTargetClothIndex is int targetIndex)
        {
            var clothEntries = target.GetReferenceItemIndices(clothArray.FieldOffset).ToList();
            var skeletonEntries = target.GetReferenceItemIndices(skeletonArray.FieldOffset).ToList();
            if (targetIndex >= skeletonEntries.Count)
            {
                throw new InvalidDataException(
                    "The target BPHCL does not have a skeleton at the cloth slot being replaced.");
            }

            clothEntries[targetIndex] = importedItemMap[sourceCloth.ItemIndex];
            skeletonEntries[targetIndex] = importedItemMap[source.Skeletons[sourceClothIndex].ItemIndex];
            ReplaceReferenceArray(target, data, items, patches, clothArray, clothEntries);
            ReplaceReferenceArray(target, data, items, patches, skeletonArray, skeletonEntries);
        }
        else
        {
            AppendReferenceArray(
                target,
                data,
                items,
                patches,
                clothArray,
                new[] { importedItemMap[sourceCloth.ItemIndex] });
            AppendReferenceArray(
                target,
                data,
                items,
                patches,
                skeletonArray,
                new[] { importedItemMap[source.Skeletons[sourceClothIndex].ItemIndex] });
        }

        return NativeBphclTagFileBuilder.Rebuild(
            target,
            CollectionsMarshal.AsSpan(data),
            items,
            patches.Select(group => new NativeBphclPatch(group.TypeIndex, group.Offsets)).ToArray(),
            typeTable.ReplacementSection,
            replacementAamp);
    }

    private static NativeBphclItem RemapTargetItem(NativeBphclItem item, IReadOnlyDictionary<uint, uint> typeMap)
    {
        var typeIndex = RemapTypeIndex(item.TypeIndex, typeMap);
        return new NativeBphclItem((item.Flags & 0xff00_0000u) | typeIndex, typeIndex, item.DataOffset, item.Count);
    }

    private static uint RemapTypeIndex(uint sourceTypeIndex, IReadOnlyDictionary<uint, uint> typeMap)
    {
        if (!typeMap.TryGetValue(sourceTypeIndex, out var targetTypeIndex))
            throw new InvalidDataException($"No target BPHCL type mapping exists for source type {sourceTypeIndex}.");
        return targetTypeIndex;
    }

    private static void RemapImportedPointers(
        List<byte> data,
        NativeBphclDocument source,
        IReadOnlyList<NativeBphclPatch> sourcePatches,
        IEnumerable<ImportedRange> importedRanges,
        IReadOnlyDictionary<int, int> importedItemMap,
        IReadOnlyDictionary<int, int> reusedItemMap)
    {
        foreach (var patch in sourcePatches)
        {
            foreach (var sourceOffset in patch.Offsets)
            {
                var range = FindImportedRange(importedRanges, sourceOffset);
                var offset = checked((int)(range.NewStart + sourceOffset - range.OldStart));
                if (offset < 0 || offset > data.Count - 4)
                    throw new InvalidDataException("Imported BPHCL relocation offset exceeds DATA.");

                var sourceData = source.DataSection
                    ?? throw new InvalidDataException("Source BPHCL has no DATA section.");
                var sourceItemIndex = BinaryPrimitives.ReadUInt32LittleEndian(
                    source.Bytes.AsSpan(sourceData.PayloadOffset + checked((int)sourceOffset), 4));
                if (sourceItemIndex >= (uint)source.Items.Count)
                {
                    throw new InvalidDataException(
                        $"Selected BPHCL graph points at ITEM {sourceItemIndex}, which was not included in the import closure.");
                }

                var sourceItemKey = checked((int)sourceItemIndex);
                if (!reusedItemMap.TryGetValue(sourceItemKey, out var targetItemIndex) &&
                    !importedItemMap.TryGetValue(sourceItemKey, out targetItemIndex))
                {
                    throw new InvalidDataException(
                        $"Selected BPHCL graph points at ITEM {sourceItemIndex}, which was not included in the import closure.");
                }
                BinaryPrimitives.WriteUInt32LittleEndian(
                    CollectionsMarshal.AsSpan(data).Slice(offset, 4),
                    checked((uint)targetItemIndex));
            }
        }
    }

    private static Dictionary<uint, ImportedRange> CopySourceClosureData(
        NativeBphclDocument source,
        IEnumerable<int> sourceItemIndices,
        List<byte> destination)
    {
        var sourceData = source.DataSection
            ?? throw new InvalidDataException("Source BPHCL has no DATA section.");
        var ranges = source.GetItemRanges();
        var copied = new Dictionary<uint, ImportedRange>();

        foreach (var sourceItemIndex in sourceItemIndices.OrderBy(index => ranges[index].Start))
        {
            var range = ranges[sourceItemIndex];
            if (copied.ContainsKey(range.Start))
                continue;
            if (range.End < range.Start || range.End > sourceData.PayloadSize)
                throw new InvalidDataException("Selected BPHCL graph exceeds the donor DATA section.");

            Align(destination, 8);
            var newStart = checked((uint)destination.Count);
            destination.AddRange(source.Bytes.AsSpan(
                sourceData.PayloadOffset + checked((int)range.Start),
                checked((int)(range.End - range.Start))).ToArray());
            copied.Add(range.Start, new ImportedRange(range.Start, range.End, newStart));
        }

        return copied;
    }

    private static ImportedRange FindImportedRange(IEnumerable<ImportedRange> ranges, uint offset)
    {
        foreach (var range in ranges)
        {
            if (offset >= range.OldStart && offset < range.OldEnd)
                return range;
        }
        throw new InvalidDataException($"BPHCL pointer at DATA+0x{offset:X} is outside the selected import graph.");
    }

    private static IReadOnlyDictionary<int, int> FindReusableColliders(
        NativeBphclDocument target,
        NativeBphclDocument source,
        IReadOnlySet<int> liveSourceItems)
    {
        var result = new Dictionary<int, int>();
        var claimedTargetItems = new HashSet<int>();

        foreach (var sourceCollider in source.Colliders.Where(collider => liveSourceItems.Contains(collider.ItemIndex)))
        {
            var candidates = target.Colliders
                .Where(targetCollider => !claimedTargetItems.Contains(targetCollider.ItemIndex) &&
                                         CollidersMatch(sourceCollider, targetCollider))
                .ToArray();
            if (candidates.Length != 1)
                continue;

            result.Add(sourceCollider.ItemIndex, candidates[0].ItemIndex);
            claimedTargetItems.Add(candidates[0].ItemIndex);
        }

        return result;
    }

    private static bool CollidersMatch(NativeBphclCollider source, NativeBphclCollider target) =>
        string.Equals(source.Name, target.Name, StringComparison.Ordinal) &&
        string.Equals(source.Shape.TypeName, target.Shape.TypeName, StringComparison.Ordinal) &&
        source.Shape.Kind == target.Shape.Kind &&
        source.Enabled == target.Enabled &&
        ApproximatelyEqual(source.Translation.X, target.Translation.X) &&
        ApproximatelyEqual(source.Translation.Y, target.Translation.Y) &&
        ApproximatelyEqual(source.Translation.Z, target.Translation.Z);

    private static bool ApproximatelyEqual(float left, float right) => MathF.Abs(left - right) <= 0.00001f;

    private static List<MutablePatchGroup> CreateMutablePatchGroups(
        IReadOnlyList<NativeBphclPatch> source,
        IReadOnlyDictionary<uint, uint>? typeMap = null) =>
        source.Select(patch => new MutablePatchGroup(
            typeMap == null ? patch.TypeIndex : RemapTypeIndex(patch.TypeIndex, typeMap),
            patch.Offsets.ToList())).ToList();

    private static void AddPatch(List<MutablePatchGroup> groups, uint typeIndex, uint offset)
    {
        var group = groups.FirstOrDefault(existing => existing.TypeIndex == typeIndex);
        if (group is null)
        {
            group = new MutablePatchGroup(typeIndex, new List<uint>());
            groups.Add(group);
        }
        group.Offsets.Add(offset);
    }

    private static void AppendReferenceArray(
        NativeBphclDocument target,
        List<byte> data,
        List<NativeBphclItem> items,
        List<MutablePatchGroup> patches,
        NativeBphclReferenceArray original,
        IEnumerable<int> addedItemIndices)
    {
        var additions = addedItemIndices.ToArray();
        if (additions.Length == 0)
            return;
        ReplaceReferenceArray(
            target,
            data,
            items,
            patches,
            original,
            target.GetReferenceItemIndices(original.FieldOffset).Concat(additions));
    }

    private static void ReplaceReferenceArray(
        NativeBphclDocument target,
        List<byte> data,
        List<NativeBphclItem> items,
        List<MutablePatchGroup> patches,
        NativeBphclReferenceArray original,
        IEnumerable<int> itemIndices)
    {
        var entries = itemIndices.ToArray();
        if (entries.Length > int.MaxValue / 8)
            throw new InvalidDataException("BPHCL reference array is too large to write.");

        Align(data, 8);
        var newDataOffset = checked((uint)data.Count);
        foreach (var itemIndex in entries)
        {
            if (itemIndex < 0)
                throw new InvalidDataException("BPHCL ITEM index cannot be represented in a pointer field.");
            AppendUInt32(data, checked((uint)itemIndex));
            AppendUInt32(data, 0);
        }

        var newStorageItemIndex = items.Count;
        var remappedStorageItem = items[original.StorageItemIndex];
        items.Add(new NativeBphclItem(
            remappedStorageItem.Flags,
            remappedStorageItem.TypeIndex,
            newDataOffset,
            checked((uint)entries.Length)));

        // The original field already has its PTCH record. Its raw ITEM index
        // and hkArray length are the only values that change in place.
        WriteUInt32(data, checked((int)original.FieldOffset), checked((uint)newStorageItemIndex));
        WriteUInt32(data, checked((int)original.FieldOffset + 8), checked((uint)entries.Length));
        var oldCapacityAndFlags = ReadUInt32(data, checked((int)original.FieldOffset + 12));
        WriteUInt32(
            data,
            checked((int)original.FieldOffset + 12),
            (oldCapacityAndFlags & 0xc000_0000u) | checked((uint)entries.Length));

        for (var index = 0; index < entries.Length; index++)
            AddPatch(patches, original.EntryPatchTypeIndex, checked(newDataOffset + (uint)(index * 8)));
    }

    private static void Align(List<byte> data, int alignment)
    {
        while (data.Count % alignment != 0)
            data.Add(0);
    }

    private static uint ReadUInt32(List<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, 4));

    private static void WriteUInt32(List<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, 4), value);

    private static void AppendUInt32(List<byte> bytes, uint value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 24));
    }

    private readonly record struct ImportedRange(uint OldStart, uint OldEnd, uint NewStart);

    private sealed class MutablePatchGroup
    {
        public MutablePatchGroup(uint typeIndex, List<uint> offsets)
        {
            TypeIndex = typeIndex;
            Offsets = offsets;
        }

        public uint TypeIndex { get; }
        public List<uint> Offsets { get; }
    }
}
