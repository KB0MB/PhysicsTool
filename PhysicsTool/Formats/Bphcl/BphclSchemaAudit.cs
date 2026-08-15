namespace HKCLTool;

// Compares every directly reflected hkArray field with the ITEM type used by
// its backing storage. The generic reader can tolerate a mismatched scalar
// size, while Havok's runtime loader may not.
internal static class BphclSchemaAudit
{
    public static IReadOnlyList<string> ValidateArrayStorageTypes(NativeBphclDocument document)
    {
        var findings = new List<string>();
        foreach (var (item, itemIndex) in document.Items.Select((item, index) => (item, index)))
        {
            var typeName = document.GetTypeName(item.TypeIndex);
            if (string.IsNullOrWhiteSpace(typeName))
                continue;

            NativeBphclTypeLayout layout;
            try
            {
                layout = NativeBphclReflectionCatalog.GetTypeLayout(typeName);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            foreach (var member in layout.Members)
            {
                if (!string.Equals(member.TypeName, "hkArray", StringComparison.Ordinal))
                    continue;

                var expectedElementType = NativeBphclReflectionCatalog.GetArrayElementType(member.TypeIndex);
                if (expectedElementType is null ||
                    !document.TryGetReferencedItem(item.DataOffset + member.Offset, out var storageItemIndex))
                {
                    continue;
                }

                var actualElementType = document.GetItem(storageItemIndex).TypeIndex;
                if (actualElementType == expectedElementType.Value)
                    continue;

                findings.Add(
                    $"ITEM {itemIndex} {typeName}.{member.Name}: expected " +
                    $"{expectedElementType.Value}:{NativeBphclReflectionCatalog.GetTypeName(expectedElementType.Value)}, " +
                    $"got {actualElementType}:{document.GetTypeName(actualElementType)} (storage ITEM {storageItemIndex}).");
            }
        }

        return findings;
    }

    public static IReadOnlyList<string> ValidateDirectReferencePatchTypes(NativeBphclDocument document)
    {
        var findings = new List<string>();
        var ranges = document.GetItemRanges();
        var arrayType = NativeBphclReflectionCatalog.GetTypeIndex("hkArray");

        foreach (var patch in document.InternalPatches)
        {
            foreach (var offset in patch.Offsets)
            {
                if (offset == 0)
                {
                    if (patch.TypeIndex != arrayType)
                    {
                        findings.Add(
                            $"DATA+0x000000 root variants: expected {arrayType}:hkArray, " +
                            $"got {patch.TypeIndex}:{document.GetTypeName(patch.TypeIndex)}.");
                    }
                    continue;
                }

                var candidates = ranges
                    .Where(entry => offset >= entry.Value.Start && offset < entry.Value.End)
                    .Select(entry =>
                    {
                        var item = document.GetItem(entry.Key);
                        var typeSize = NativeBphclReflectionCatalog.GetTypeSize(item.TypeIndex);
                        if (typeSize == 0 || offset < item.DataOffset)
                            return (ItemIndex: entry.Key, ExpectedType: (uint?)null);

                        var relative = offset - item.DataOffset;
                        var expected = NativeBphclReflectionCatalog.GetMemberTypeIndex(
                            item.TypeIndex,
                            relative % typeSize);
                        return (ItemIndex: entry.Key, ExpectedType: expected);
                    })
                    .Where(candidate => candidate.ExpectedType.HasValue)
                    .ToArray();

                if (candidates.Length == 0)
                    continue;

                if (candidates.Any(candidate => candidate.ExpectedType == patch.TypeIndex))
                    continue;

                var labels = string.Join(
                    ", ",
                    candidates.Select(candidate =>
                        $"ITEM {candidate.ItemIndex} expects {candidate.ExpectedType}:{NativeBphclReflectionCatalog.GetTypeName(candidate.ExpectedType!.Value)}"));
                findings.Add(
                    $"DATA+0x{offset:X}: PTCH says {patch.TypeIndex}:{document.GetTypeName(patch.TypeIndex)}, " +
                    labels + ".");
            }
        }

        return findings;
    }
}