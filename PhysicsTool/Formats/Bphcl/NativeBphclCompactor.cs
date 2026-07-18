using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace HKCLTool;

// Removes unreachable DATA/ITEM records after native edits. This is kept as
// an explicit pass because it changes layout substantially, unlike the safe
// allocation-preserving Save/Merge/Delete operations.
internal static class NativeBphclCompactor
{
    public static byte[] Compact(NativeBphclDocument document) => Compact(document, preserveItemIndices: false);

    // Keeps every allocation but deliberately changes non-null ITEM identities.
    // This is a diagnostic control: if it fails in-game, ITEM indices have a
    // dependency outside the currently understood PTCH relocation graph.
    public static byte[] ReindexAllItemsForDiagnostic(NativeBphclDocument document)
    {
        EnsureNoExternalPatches(document);
        if (document.Items.Count == 0 || document.GetItem(0).TypeIndex != 0)
            throw new InvalidDataException("BPHCL ITEM 0 is expected to be the null-pointer sentinel.");

        var itemIndexMap = new Dictionary<int, int>(document.Items.Count) { [0] = 0 };
        for (var oldIndex = 1; oldIndex < document.Items.Count; oldIndex++)
            itemIndexMap[oldIndex] = document.Items.Count - oldIndex;

        var items = new NativeBphclItem[document.Items.Count];
        items[0] = document.GetItem(0);
        for (var oldIndex = 1; oldIndex < document.Items.Count; oldIndex++)
            items[itemIndexMap[oldIndex]] = document.GetItem(oldIndex);

        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, checked((int)dataSection.PayloadSize))
            .ToArray();
        var patches = new List<NativeBphclPatch>(document.InternalPatches.Count);
        foreach (var patch in document.InternalPatches)
        {
            foreach (var offset in patch.Offsets)
            {
                var oldItemIndex = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(checked((int)offset), 4));
                if (oldItemIndex >= document.Items.Count || !itemIndexMap.TryGetValue(checked((int)oldItemIndex), out var newItemIndex))
                    throw new InvalidDataException($"BPHCL PTCH at DATA+0x{offset:X} has an invalid ITEM target.");
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(checked((int)offset), 4), checked((uint)newItemIndex));
            }
            patches.Add(patch);
        }

        return NativeBphclTagFileBuilder.Rebuild(document, data, items, patches);
    }

    // Diagnostic control: leave the active root graph's ITEM identities alone
    // and shuffle only inactive records. This determines whether the loader
    // treats the complete ITEM table as runtime-significant.
    public static byte[] ReindexInactiveItemsForDiagnostic(NativeBphclDocument document)
    {
        EnsureNoExternalPatches(document);
        if (document.Items.Count == 0 || document.GetItem(0).TypeIndex != 0)
            throw new InvalidDataException("BPHCL ITEM 0 is expected to be the null-pointer sentinel.");
        if (!document.TryGetReferencedItem(0, out var namedVariantItemIndex))
            throw new InvalidDataException("BPHCL root named-variant array could not be resolved.");

        var rootContainerItemIndex = GetRootContainerItemIndex(document);
        var liveItems = document.CollectItemClosure(
                new[] { rootContainerItemIndex, namedVariantItemIndex }
                    .Concat(document.RootVariants.Select(variant => variant.ObjectItemIndex)))
            .ToHashSet();
        var inactiveItems = Enumerable.Range(1, document.Items.Count - 1)
            .Where(index => !liveItems.Contains(index))
            .ToArray();
        if (inactiveItems.Length < 2)
            throw new InvalidDataException("BPHCL has fewer than two inactive ITEM records to shuffle.");

        var itemIndexMap = Enumerable.Range(0, document.Items.Count)
            .ToDictionary(index => index, index => index);
        for (var position = 0; position < inactiveItems.Length; position++)
            itemIndexMap[inactiveItems[position]] = inactiveItems[inactiveItems.Length - 1 - position];

        var items = document.Items.ToArray();
        foreach (var oldIndex in inactiveItems)
            items[itemIndexMap[oldIndex]] = document.GetItem(oldIndex);

        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, checked((int)dataSection.PayloadSize))
            .ToArray();
        foreach (var patch in document.InternalPatches)
        {
            foreach (var offset in patch.Offsets)
            {
                var oldItemIndex = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(checked((int)offset), 4));
                if (oldItemIndex >= document.Items.Count)
                    throw new InvalidDataException($"BPHCL PTCH at DATA+0x{offset:X} has an invalid ITEM target.");
                BinaryPrimitives.WriteUInt32LittleEndian(
                    data.AsSpan(checked((int)offset), 4),
                    checked((uint)itemIndexMap[checked((int)oldItemIndex)]));
            }
        }

        return NativeBphclTagFileBuilder.Rebuild(document, data, items, document.InternalPatches);
    }

    // Diagnostic mode: retain original ITEM identities while moving only the
    // reachable DATA ranges. This isolates hidden index dependencies from
    // ordinary pointer relocation without becoming a normal save path.
    public static byte[] CompactPreservingItemIndices(NativeBphclDocument document) =>
        Compact(document, preserveItemIndices: true);

    private static byte[] Compact(NativeBphclDocument document, bool preserveItemIndices)
    {
        EnsureNoExternalPatches(document);
        if (document.Items.Count == 0 || document.GetItem(0).TypeIndex != 0)
            throw new InvalidDataException("BPHCL ITEM 0 is expected to be the null-pointer sentinel.");
        if (!document.TryGetReferencedItem(0, out var namedVariantItemIndex))
            throw new InvalidDataException("BPHCL root named-variant array could not be resolved.");

        // hkRootLevelContainer is the implicit object at DATA+0. The loader
        // starts there directly; it is not itself the target of a PTCH entry.
        // Keep its ITEM record as a real root alongside its named-variant data.
        var roots = new List<int> { GetRootContainerItemIndex(document), namedVariantItemIndex };
        roots.AddRange(document.RootVariants.Select(variant => variant.ObjectItemIndex));
        // ITEM 0 is a real on-disk sentinel: zero in an unrelocated pointer
        // field means null. Never assign a compacted object to that index.
        var liveItemIndices = document.CollectItemClosure(roots)
            .Where(index => index != 0)
            .ToArray();
        if (liveItemIndices.Length == 0)
            throw new InvalidDataException("BPHCL compaction found no live non-null ITEM records.");

        var livePatches = document.GetPatchesForItems(liveItemIndices);
        var ranges = document.GetItemRanges();
        var dataSection = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var firstLiveStart = liveItemIndices.Min(index => ranges[index].Start);

        var itemIndexMap = new Dictionary<int, int>(liveItemIndices.Length + 1)
        {
            [0] = 0
        };
        foreach (var oldItemIndex in liveItemIndices)
            itemIndexMap.Add(oldItemIndex, preserveItemIndices ? oldItemIndex : itemIndexMap.Count);

        var rangeCopies = new Dictionary<uint, CompactRange>();
        // DATA begins with a small root pointer prefix rather than an ITEM.
        // Preserve it so the loader can still find hkRootLevelContainer's
        // named-variant array after every ITEM is renumbered.
        var data = document.Bytes
            .AsSpan(dataSection.PayloadOffset, checked((int)firstLiveStart))
            .ToArray()
            .ToList();
        foreach (var oldItemIndex in liveItemIndices.OrderBy(index => ranges[index].Start))
        {
            var range = ranges[oldItemIndex];
            if (rangeCopies.ContainsKey(range.Start))
                continue;

            if (range.End < range.Start || range.End > dataSection.PayloadSize)
                throw new InvalidDataException("BPHCL live ITEM range exceeds DATA.");
            // Havok's vector and transform allocations rely on their original
            // 16-byte alignment. Preserve each range's alignment residue
            // instead of packing all retained data to a generic boundary.
            AlignToResidue(data, 16, checked((int)(range.Start % 16)));
            var newStart = checked((uint)data.Count);
            var byteCount = checked((int)(range.End - range.Start));
            data.AddRange(document.Bytes.AsSpan(dataSection.PayloadOffset + checked((int)range.Start), byteCount).ToArray());
            rangeCopies.Add(range.Start, new CompactRange(range.Start, range.End, newStart));
        }

        var compactItems = preserveItemIndices
            ? Enumerable.Repeat(new NativeBphclItem(0, 0, 0, 0), document.Items.Count).ToArray()
            : new NativeBphclItem[liveItemIndices.Length + 1];
        compactItems[0] = document.GetItem(0);
        foreach (var oldItemIndex in liveItemIndices)
        {
            var oldItem = document.GetItem(oldItemIndex);
            var copiedRange = rangeCopies[ranges[oldItemIndex].Start];
            var newDataOffset = checked(copiedRange.NewStart + oldItem.DataOffset - copiedRange.OldStart);
            compactItems[itemIndexMap[oldItemIndex]] = oldItem with { DataOffset = newDataOffset };
        }

        var compactPatches = new List<NativeBphclPatch>();
        foreach (var patch in livePatches)
        {
            var offsets = new List<uint>(patch.Offsets.Count);
            foreach (var oldPointerOffset in patch.Offsets)
            {
                var copiedRange = FindRange(rangeCopies.Values, oldPointerOffset);
                var newPointerOffset = checked(copiedRange.NewStart + oldPointerOffset - copiedRange.OldStart);
                var originalItemIndex = BinaryPrimitives.ReadUInt32LittleEndian(
                    document.Bytes.AsSpan(dataSection.PayloadOffset + checked((int)oldPointerOffset), 4));
                if (originalItemIndex >= document.Items.Count ||
                    !itemIndexMap.TryGetValue(checked((int)originalItemIndex), out var newItemIndex))
                {
                    throw new InvalidDataException(
                        $"Live BPHCL pointer at DATA+0x{oldPointerOffset:X} reaches an item outside the compacted graph.");
                }

                BinaryPrimitives.WriteUInt32LittleEndian(
                    CollectionsMarshal.AsSpan(data).Slice(checked((int)newPointerOffset), 4),
                    checked((uint)newItemIndex));
                offsets.Add(newPointerOffset);
            }
            compactPatches.Add(new NativeBphclPatch(patch.TypeIndex, offsets));
        }

        // The prefix pointer(s) are not inside an ITEM range, so they are not
        // part of GetPatchesForItems. They still point into the live graph and
        // must be remapped to the compact ITEM indices.
        foreach (var patch in document.InternalPatches)
        {
            var offsets = new List<uint>();
            foreach (var oldPointerOffset in patch.Offsets.Where(offset => offset < firstLiveStart))
            {
                var originalItemIndex = BinaryPrimitives.ReadUInt32LittleEndian(
                    document.Bytes.AsSpan(dataSection.PayloadOffset + checked((int)oldPointerOffset), 4));
                if (originalItemIndex >= document.Items.Count ||
                    !itemIndexMap.TryGetValue(checked((int)originalItemIndex), out var newItemIndex))
                {
                    throw new InvalidDataException(
                        $"BPHCL root pointer at DATA+0x{oldPointerOffset:X} reaches an item outside the compacted graph.");
                }
                BinaryPrimitives.WriteUInt32LittleEndian(
                    CollectionsMarshal.AsSpan(data).Slice(checked((int)oldPointerOffset), 4),
                    checked((uint)newItemIndex));
                offsets.Add(oldPointerOffset);
            }
            if (offsets.Count > 0)
                compactPatches.Add(new NativeBphclPatch(patch.TypeIndex, offsets));
        }

        return NativeBphclTagFileBuilder.Rebuild(document, CollectionsMarshal.AsSpan(data), compactItems, compactPatches);
    }

    private static CompactRange FindRange(IEnumerable<CompactRange> ranges, uint offset)
    {
        foreach (var range in ranges)
        {
            if (offset >= range.OldStart && offset < range.OldEnd)
                return range;
        }
        throw new InvalidDataException($"BPHCL patch at DATA+0x{offset:X} is outside the compacted graph.");
    }

    private static int GetRootContainerItemIndex(NativeBphclDocument document)
    {
        var matches = document.Items
            .Select((item, index) => (item, index))
            .Where(entry => entry.index != 0 &&
                            entry.item.DataOffset == 0 &&
                            string.Equals(document.GetTypeName(entry.item.TypeIndex), "hkRootLevelContainer", StringComparison.Ordinal))
            .Select(entry => entry.index)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException(
                $"BPHCL expected one implicit hkRootLevelContainer ITEM at DATA+0, found {matches.Length}.");
    }

    private static void EnsureNoExternalPatches(NativeBphclDocument document)
    {
        var patch = document.IndexSection?.Children.FirstOrDefault(section => section.Signature == "PTCH");
        if (patch is null)
            throw new InvalidDataException("BPHCL INDX has no PTCH section.");

        var cursor = patch.PayloadOffset;
        var end = checked(patch.Offset + patch.Size);
        while (cursor + 4 <= end)
        {
            var typeIndex = BinaryPrimitives.ReadUInt32LittleEndian(document.Bytes.AsSpan(cursor, 4));
            cursor += 4;
            if (typeIndex == 0)
            {
                if (cursor != end)
                    throw new NotSupportedException("BPHCL compaction does not yet support external PTCH records.");
                return;
            }
            if (cursor + 4 > end)
                throw new InvalidDataException("Truncated BPHCL PTCH count.");
            var count = BinaryPrimitives.ReadUInt32LittleEndian(document.Bytes.AsSpan(cursor, 4));
            cursor = checked(cursor + 4 + checked((int)count * 4));
            if (cursor > end)
                throw new InvalidDataException("BPHCL PTCH entry exceeds its section.");
        }
        if (cursor != end)
            throw new InvalidDataException("BPHCL PTCH ends in a truncated patch group.");
    }

    private static void Align(List<byte> data, int alignment)
    {
        while (data.Count % alignment != 0)
            data.Add(0);
    }

    private static void AlignToResidue(List<byte> data, int alignment, int residue)
    {
        while (data.Count % alignment != residue)
            data.Add(0);
    }

    private readonly record struct CompactRange(uint OldStart, uint OldEnd, uint NewStart);
}
