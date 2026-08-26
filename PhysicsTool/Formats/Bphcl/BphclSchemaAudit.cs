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
                var expectedElementName = NativeBphclReflectionCatalog.GetTypeName(expectedElementType.Value);
                var actualElementName = document.GetTypeName(actualElementType);
                if (actualElementType == expectedElementType.Value ||
                    string.Equals(actualElementName, expectedElementName, StringComparison.Ordinal))
                {
                    // Retail TYPE tables may contain equivalent aliases for the
                    // same reflected array element class at different indices.
                    continue;
                }

                findings.Add(
                    $"ITEM {itemIndex} {typeName}.{member.Name}: expected " +
                    $"{expectedElementType.Value}:{expectedElementName}, " +
                    $"got {actualElementType}:{actualElementName} (storage ITEM {storageItemIndex}).");
            }
        }

        return findings;
    }

    public static IReadOnlyList<string> ValidateDirectReferencePatchTypes(NativeBphclDocument document)
    {
        var findings = new List<string>();
        var ranges = document.GetItemRanges();
        var arrayTypes = document.TypeNames
            .Select((name, index) => (name, index))
            .Where(entry => string.Equals(entry.name, "hkArray", StringComparison.Ordinal))
            .Select(entry => checked((uint)entry.index))
            .ToHashSet();

        uint? GetMemberTypeIndex(uint ownerTypeIndex, uint memberOffset)
        {
            var visited = new HashSet<uint>();
            var currentTypeIndex = ownerTypeIndex;
            while (currentTypeIndex != 0 && visited.Add(currentTypeIndex))
            {
                var definition = document.TypeDefinitions.FirstOrDefault(
                    candidate => candidate.TypeIndex == currentTypeIndex);
                if (definition is null)
                    return null;

                var member = definition.Members.FirstOrDefault(
                    candidate => candidate.Offset == memberOffset);
                if (member is not null)
                    return member.TypeIndex;

                currentTypeIndex = definition.ParentTypeIndex;
            }

            return null;
        }

        foreach (var patch in document.InternalPatches)
        {
            foreach (var offset in patch.Offsets)
            {
                if (offset == 0)
                {
                    if (!arrayTypes.Contains(patch.TypeIndex))
                    {
                        findings.Add(
                            "DATA+0x000000 root variants: expected a file-local hkArray type, " +
                            $"got {patch.TypeIndex}:{document.GetTypeName(patch.TypeIndex)}.");
                    }
                    continue;
                }

                var candidates = ranges
                    .Where(entry => offset >= entry.Value.Start && offset < entry.Value.End)
                    .Select(entry =>
                    {
                        var item = document.GetItem(entry.Key);
                        var typeSize = document.TypeDefinitions.FirstOrDefault(
                            candidate => candidate.TypeIndex == item.TypeIndex)?.Size ?? 0;
                        if (typeSize == 0 || offset < item.DataOffset)
                            return (ItemIndex: entry.Key, ExpectedType: (uint?)null);

                        var relative = offset - item.DataOffset;
                        var expected = GetMemberTypeIndex(item.TypeIndex, relative % typeSize);
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
                        $"ITEM {candidate.ItemIndex} expects {candidate.ExpectedType}:{document.GetTypeName(candidate.ExpectedType!.Value)}"));
                findings.Add(
                    $"DATA+0x{offset:X}: PTCH says {patch.TypeIndex}:{document.GetTypeName(patch.TypeIndex)}, " +
                    labels + ".");
            }
        }

        return findings;
    }
}