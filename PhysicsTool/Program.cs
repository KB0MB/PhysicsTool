using System;
using System.Numerics;
using System.Windows.Forms;
using HKX2;

namespace HKCLTool;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-reflection", StringComparison.OrdinalIgnoreCase))
            {
                var layout = NativeBphclReflectionCatalog.GetTypeLayout(args[1]);
                Console.WriteLine($"type|{layout.TypeIndex}|{layout.Name}|size=0x{layout.Size:X}");
                foreach (var member in layout.Members.OrderBy(member => member.Offset))
                {
                    Console.WriteLine($"member|0x{member.Offset:X2}|{member.Name}|{member.TypeIndex}:{member.TypeName}");
                }

                return;
            }            if (args.Length == 4 && string.Equals(args[0], "--dump-bphcl-closure-members", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var clothIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                var typeName = args[3];
                var cloth = document.Cloths.ElementAtOrDefault(clothIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
                var skeleton = document.Skeletons.ElementAtOrDefault(clothIndex)
                    ?? throw new InvalidDataException("The cloth has no paired skeleton.");
                var layout = NativeBphclReflectionCatalog.GetTypeLayout(typeName);
                var members = layout.Members.OrderBy(member => member.Offset).ToArray();
                var data = document.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
                var closure = document.CollectItemClosure(new[] { cloth.ItemIndex, skeleton.ItemIndex });
                foreach (var itemIndex in closure.Where(index => document.GetItem(index).TypeIndex == layout.TypeIndex).OrderBy(index => document.GetItem(index).DataOffset))
                {
                    var item = document.GetItem(itemIndex);
                    for (uint element = 0; element < item.Count; element++)
                    {
                        Console.WriteLine($"object|item={itemIndex}|element={element}|data=0x{item.DataOffset + element * layout.Size:X}");
                        for (var memberIndex = 0; memberIndex < members.Length; memberIndex++)
                        {
                            var member = members[memberIndex];
                            var nextOffset = memberIndex + 1 < members.Length ? members[memberIndex + 1].Offset : layout.Size;
                            var length = checked((int)(nextOffset - member.Offset));
                            var fieldOffset = item.DataOffset + element * layout.Size + member.Offset;
                            var start = data.PayloadOffset + checked((int)fieldOffset);
                            var bytes = Convert.ToHexString(document.Bytes.AsSpan(start, length));
                            var reference = document.TryGetReferencedItem(fieldOffset, out var targetItem)
                                ? $"->ITEM {targetItem}:{document.GetTypeName(document.GetItem(targetItem).TypeIndex)}"
                                : string.Empty;
                            Console.WriteLine($"member|0x{member.Offset:X2}|{member.Name}|{member.TypeName}|{bytes}{reference}");
                        }
                    }
                }

                return;
            }            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-buffer-layouts", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var data = document.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
                foreach (var cloth in document.Cloths)
                {
                    foreach (var buffer in cloth.BufferDefinitions)
                    {
                        const uint bufferLayoutOffset = 56;
                        var start = data.PayloadOffset + checked((int)(buffer.DataOffset + bufferLayoutOffset));
                        var bytes = Convert.ToHexString(document.Bytes.AsSpan(start, 32));
                        Console.WriteLine($"bufferLayout|cloth={cloth.Index}:{cloth.Name}|buffer={buffer.Index}:{buffer.ClassName}:{buffer.BufferName}|type={buffer.Type}|vertices={buffer.VertexCount}|triangles={buffer.TriangleCount}|{bytes}");
                    }
                }

                return;
            }            if (args.Length == 2 && string.Equals(args[0], "--find-bphcl-bone-space-skins", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var path in Directory.EnumerateFiles(args[1], "*.bphcl", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var document = NativeBphclDocument.Open(path);
                    foreach (var cloth in document.Cloths)
                    {
                        var hasBoneSpaceSkin = cloth.Operators.Any(@operator =>
                            string.Equals(@operator.ClassName, "hclBoneSpaceSkinPOperator", StringComparison.Ordinal));
                        if (hasBoneSpaceSkin)
                            Console.WriteLine($"{path}|{cloth.Index}|{cloth.Name}");
                    }
                }
                return;
            }            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-roots", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                foreach (var variant in document.RootVariants)
                    Console.WriteLine($"root|{variant.Index}|name={variant.Name}|class={variant.ClassName}|item={variant.ObjectItemIndex}");
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-items", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                foreach (var (item, index) in document.Items.Take(24).Select((item, index) => (item, index)))
                {
                    Console.WriteLine($"item|{index}|flags=0x{item.Flags:X8}|type={item.TypeIndex}:{document.GetTypeName(item.TypeIndex)}|data=0x{item.DataOffset:X}|count={item.Count}");
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--compare-bphcl-type-catalog", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var mismatches = 0;
                var usedIndexes = document.Items
                    .Select(item => item.TypeIndex)
                    .Concat(document.InternalPatches.Select(patch => patch.TypeIndex))
                    .Distinct()
                    .OrderBy(index => index)
                    .ToArray();

                Console.WriteLine($"typeCatalog|documentTypes={document.TypeNames.Count - 1}|usedIndexes={usedIndexes.Length}");
                foreach (var typeIndex in usedIndexes)
                {
                    var nativeName = document.GetTypeName(typeIndex) ?? $"<missing:{typeIndex}>";
                    var embeddedName = NativeBphclReflectionCatalog.GetTypeName(typeIndex);
                    var nativeSize = document.TypeDefinitions.FirstOrDefault(definition => definition.TypeIndex == typeIndex)?.Size ?? 0;
                    var embeddedSize = NativeBphclReflectionCatalog.GetTypeSize(typeIndex);
                    if (string.Equals(nativeName, embeddedName, StringComparison.Ordinal) && nativeSize == embeddedSize)
                        continue;

                    mismatches++;
                    Console.WriteLine($"mismatch|index={typeIndex}|native={nativeName}|nativeSize=0x{nativeSize:X}|embedded={embeddedName}|embeddedSize=0x{embeddedSize:X}");
                }

                foreach (var typeIndex in usedIndexes)
                {
                    var native = document.TypeDefinitions.FirstOrDefault(definition => definition.TypeIndex == typeIndex);
                    if (native is null)
                        continue;

                    var embedded = NativeBphclReflectionCatalog.GetTypeLayout(typeIndex);
                    var nativeMembers = native.Members
                        .Select(member => $"{member.Name}@0x{member.Offset:X}:{document.GetTypeName(member.TypeIndex)}")
                        .OrderBy(member => member, StringComparer.Ordinal)
                        .ToArray();
                    var embeddedMembers = embedded.Members
                        .Select(member => $"{member.Name}@0x{member.Offset:X}:{member.TypeName}")
                        .OrderBy(member => member, StringComparer.Ordinal)
                        .ToArray();
                    if (nativeMembers.SequenceEqual(embeddedMembers, StringComparer.Ordinal))
                        continue;

                    mismatches++;
                    Console.WriteLine($"layoutMismatch|index={typeIndex}|type={document.GetTypeName(typeIndex)}|native=[{string.Join(',', nativeMembers)}]|embedded=[{string.Join(',', embeddedMembers)}]");
                }

                Console.WriteLine($"typeCatalog|mismatches={mismatches}");
                return;
            }            if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-root-closure", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var rootIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                var root = document.RootVariants.ElementAtOrDefault(rootIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(rootIndex));
                var closure = document.CollectItemClosure(new[] { root.ObjectItemIndex });
                var ranges = document.GetItemRanges();
                var patchesByOffset = document.InternalPatches
                    .SelectMany(patch => patch.Offsets.Select(offset => (offset, patch.TypeIndex)))
                    .ToDictionary(entry => entry.offset, entry => entry.TypeIndex);
                Console.WriteLine($"rootClosure|{root.Index}:{root.Name}:{root.ClassName}|items={closure.Count}");
                foreach (var itemIndex in closure.OrderBy(index => document.GetItem(index).DataOffset).ThenBy(index => index))
                {
                    var item = document.GetItem(itemIndex);
                    var range = ranges[itemIndex];
                    var pointerFields = patchesByOffset
                        .Where(entry => entry.Key >= range.Start && entry.Key < range.End)
                        .OrderBy(entry => entry.Key)
                        .Select(entry =>
                        {
                            var target = document.TryGetReferencedItem(entry.Key, out var targetIndex) ? targetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";
                            return $"0x{entry.Key - item.DataOffset:X}:{document.GetTypeName(entry.Value)}->{target}";
                        });
                    Console.WriteLine($"item|{itemIndex}|{document.GetTypeName(item.TypeIndex)}|flags=0x{item.Flags:X8}:{document.GetTypeName(item.Flags)}|data=0x{item.DataOffset:X}|count={item.Count}|range=0x{range.Start:X}-0x{range.End:X}|ptr=[{string.Join(',', pointerFields)}]");
                }
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-closure", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var clothIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                var cloth = document.Cloths.ElementAtOrDefault(clothIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
                var skeleton = document.Skeletons.ElementAtOrDefault(clothIndex)
                    ?? throw new InvalidDataException("The cloth has no paired skeleton.");
                var closure = document.CollectItemClosure(new[] { cloth.ItemIndex, skeleton.ItemIndex });

                Console.WriteLine($"closure|cloth={clothIndex}:{cloth.Name}|items={closure.Count}|patches={document.GetPatchesForItems(closure).Sum(patch => patch.Offsets.Count)}");
                foreach (var group in closure
                             .GroupBy(index => document.GetTypeName(document.GetItem(index).TypeIndex) ?? $"type_{document.GetItem(index).TypeIndex}")
                             .OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    Console.WriteLine($"  {group.Key}|items={group.Count()}|elements={group.Sum(index => (long)document.GetItem(index).Count)}|pointers={group.Sum(index => document.GetPatchesForItems(new[] { index }).Sum(patch => patch.Offsets.Count))}");
                }

                return;
            }
            if (args.Length == 5 && string.Equals(args[0], "--compare-bphcl-closure-bytes", StringComparison.OrdinalIgnoreCase))
            {
                var native = NativeBphclDocument.Open(args[1]);
                var nativeClothIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                var generated = NativeBphclDocument.Open(args[3]);
                var generatedClothIndex = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
                var nativeCloth = native.Cloths.ElementAtOrDefault(nativeClothIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(nativeClothIndex));
                var generatedCloth = generated.Cloths.ElementAtOrDefault(generatedClothIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(generatedClothIndex));
                var nativeSkeleton = native.Skeletons.ElementAtOrDefault(nativeClothIndex)
                    ?? throw new InvalidDataException("Native cloth has no paired skeleton.");
                var generatedSkeleton = generated.Skeletons.ElementAtOrDefault(generatedClothIndex)
                    ?? throw new InvalidDataException("Generated cloth has no paired skeleton.");

                static HashSet<uint> PointerBytes(NativeBphclDocument document) => document.InternalPatches
                    .SelectMany(patch => patch.Offsets)
                    .SelectMany(offset => Enumerable.Range(0, 4).Select(byteOffset => offset + (uint)byteOffset))
                    .ToHashSet();
                static string ItemKey(NativeBphclDocument document, NativeBphclItem item) =>
                    $"{document.GetTypeName(item.TypeIndex)}|count={item.Count}";

                var nativeRanges = native.GetItemRanges();
                var generatedRanges = generated.GetItemRanges();
                var nativePointers = PointerBytes(native);
                var generatedPointers = PointerBytes(generated);
                var nativeGroups = native.CollectItemClosure(new[] { nativeCloth.ItemIndex, nativeSkeleton.ItemIndex })
                    .Select(index => (index, item: native.GetItem(index)))
                    .Where(entry => !string.Equals(native.GetTypeName(entry.item.TypeIndex), "char", StringComparison.Ordinal))
                    .GroupBy(entry => ItemKey(native, entry.item), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.item.DataOffset).ToArray(), StringComparer.Ordinal);
                var generatedGroups = generated.CollectItemClosure(new[] { generatedCloth.ItemIndex, generatedSkeleton.ItemIndex })
                    .Select(index => (index, item: generated.GetItem(index)))
                    .Where(entry => !string.Equals(generated.GetTypeName(entry.item.TypeIndex), "char", StringComparison.Ordinal))
                    .GroupBy(entry => ItemKey(generated, entry.item), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.item.DataOffset).ToArray(), StringComparer.Ordinal);

                Console.WriteLine($"compare|native={nativeCloth.Name}|generated={generatedCloth.Name}");
                foreach (var key in nativeGroups.Keys.Union(generatedGroups.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
                {
                    nativeGroups.TryGetValue(key, out var nativeItems);
                    generatedGroups.TryGetValue(key, out var generatedItems);
                    nativeItems ??= Array.Empty<(int index, NativeBphclItem item)>();
                    generatedItems ??= Array.Empty<(int index, NativeBphclItem item)>();
                    if (nativeItems.Length != generatedItems.Length)
                    {
                        Console.WriteLine($"count|{key}|native={nativeItems.Length}|generated={generatedItems.Length}");
                        continue;
                    }

                    for (var ordinal = 0; ordinal < nativeItems.Length; ordinal++)
                    {
                        var nativeItem = nativeItems[ordinal].item;
                        var generatedItem = generatedItems[ordinal].item;
                        // ITEM offsets are allocation boundaries, not object boundaries. Compare
                        // only the reflected object payload so alignment and following arrays do
                        // not masquerade as semantic differences.
                        var nativeElementSize = NativeBphclReflectionCatalog.GetTypeSize(nativeItem.TypeIndex);
                        var generatedElementSize = NativeBphclReflectionCatalog.GetTypeSize(generatedItem.TypeIndex);
                        var nativeLength = checked((int)(nativeItem.Count * nativeElementSize));
                        var generatedLength = checked((int)(generatedItem.Count * generatedElementSize));
                        var comparedLength = Math.Min(nativeLength, generatedLength);
                        var differences = new List<string>();
                        for (var offset = 0; offset < comparedLength; offset++)
                        {
                            var nativeOffset = nativeItem.DataOffset + (uint)offset;
                            var generatedOffset = generatedItem.DataOffset + (uint)offset;
                            if (nativePointers.Contains(nativeOffset) || generatedPointers.Contains(generatedOffset))
                                continue;
                            var nativeByte = native.Bytes[native.DataSection!.PayloadOffset + checked((int)nativeOffset)];
                            var generatedByte = generated.Bytes[generated.DataSection!.PayloadOffset + checked((int)generatedOffset)];
                            if (nativeByte == generatedByte)
                                continue;
                            differences.Add($"0x{offset:X}:{nativeByte:X2}/{generatedByte:X2}");
                            if (differences.Count == 12)
                                break;
                        }
                        if (nativeLength != generatedLength || differences.Count > 0)
                        {
                            Console.WriteLine($"diff|{key}|ordinal={ordinal}|bytes={nativeLength}/{generatedLength}|{string.Join(',', differences)}");
                        }
                    }
                }
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-closure-layout", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var clothIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                var cloth = document.Cloths.ElementAtOrDefault(clothIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
                var skeleton = document.Skeletons.ElementAtOrDefault(clothIndex)
                    ?? throw new InvalidDataException("The cloth has no paired skeleton.");
                var closure = document.CollectItemClosure(new[] { cloth.ItemIndex, skeleton.ItemIndex });
                var ranges = document.GetItemRanges();
                var patchesByOffset = document.InternalPatches
                    .SelectMany(patch => patch.Offsets.Select(offset => (offset, patch.TypeIndex)))
                    .ToDictionary(entry => entry.offset, entry => entry.TypeIndex);

                Console.WriteLine($"layout|cloth={clothIndex}:{cloth.Name}|items={closure.Count}");
                foreach (var itemIndex in closure
                             .OrderBy(index => document.GetItem(index).DataOffset)
                             .ThenBy(index => index))
                {
                    var item = document.GetItem(itemIndex);
                    var range = ranges[itemIndex];
                    var pointerFields = patchesByOffset
                        .Where(entry => entry.Key >= range.Start && entry.Key < range.End)
                        .OrderBy(entry => entry.Key)
                        .Select(entry =>
                        {
                            var target = document.TryGetReferencedItem(entry.Key, out var targetIndex)
                                ? targetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                : "?";
                            return $"0x{entry.Key - item.DataOffset:X}:{document.GetTypeName(entry.Value)}->{target}";
                        });
                    Console.WriteLine(
                        $"item|{itemIndex}|{document.GetTypeName(item.TypeIndex)}|flags=0x{item.Flags:X8}:{document.GetTypeName(item.Flags)}|" +
                        $"data=0x{item.DataOffset:X}|count={item.Count}|range=0x{range.Start:X}-0x{range.End:X}|" +
                        $"ptr=[{string.Join(',', pointerFields)}]");
                }

                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-operator-access", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var clothIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                var cloth = document.Cloths.ElementAtOrDefault(clothIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
                var dataSection = document.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");

                uint ReadUInt32(uint offset) => BitConverter.ToUInt32(document.Bytes, dataSection.PayloadOffset + checked((int)offset));
                byte ReadByte(uint offset) => document.Bytes[dataSection.PayloadOffset + checked((int)offset)];
                string ReadString(uint offset)
                {
                    if (!document.TryGetReferencedItem(offset, out var stringItemIndex))
                        return string.Empty;
                    var item = document.GetItem(stringItemIndex);
                    var start = dataSection.PayloadOffset + checked((int)item.DataOffset);
                    var end = Array.IndexOf(document.Bytes, (byte)0, start);
                    return end < start ? string.Empty : System.Text.Encoding.UTF8.GetString(document.Bytes, start, end - start);
                }
                string DescribeBuffers(uint fieldOffset)
                {
                    if (!document.TryGetReferencedItem(fieldOffset, out var storageIndex))
                        return "[]";
                    var storage = document.GetItem(storageIndex);
                    var values = Enumerable.Range(0, checked((int)storage.Count)).Select(index =>
                    {
                        var offset = storage.DataOffset + checked((uint)(index * 16));
                        return $"{ReadUInt32(offset)}:{ReadByte(offset + 4)}/{ReadByte(offset + 5)}/{ReadByte(offset + 6)}/{ReadByte(offset + 7)}:tri{ReadByte(offset + 8)}:shadow{ReadUInt32(offset + 12)}";
                    });
                    return $"[{string.Join(';', values)}]";
                }
                string DescribeTransformSets(uint fieldOffset)
                {
                    if (!document.TryGetReferencedItem(fieldOffset, out var storageIndex))
                        return "[]";
                    var storage = document.GetItem(storageIndex);
                    var values = Enumerable.Range(0, checked((int)storage.Count)).Select(index =>
                    {
                        var offset = storage.DataOffset + checked((uint)(index * 32));
                        return $"{ReadUInt32(offset)}:{ReadByte(offset + 8)}/{ReadByte(offset + 9)}:trackers{ReadUInt32(offset + 24)}";
                    });
                    return $"[{string.Join(';', values)}]";
                }

                Console.WriteLine($"operators|cloth={clothIndex}:{cloth.Name}");
                foreach (var op in cloth.Operators)
                {
                    var item = document.GetItem(op.ItemIndex);
                    Console.WriteLine(
                        $"op|{op.Index}|{op.ClassName}|item={op.ItemIndex}|id={ReadUInt32(item.DataOffset + 32)}|type={ReadUInt32(item.DataOffset + 36)}|" +
                        $"name={ReadString(item.DataOffset + 24)}|buffers={DescribeBuffers(item.DataOffset + 40)}|" +
                        $"transforms={DescribeTransformSets(item.DataOffset + 56)}");
                }
                return;
            }            if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-operator-trackers", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var clothIndex = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                var cloth = document.Cloths.ElementAtOrDefault(clothIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
                var data = document.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");

                uint ReadUInt32(uint offset) => BitConverter.ToUInt32(document.Bytes, data.PayloadOffset + checked((int)offset));
                string DescribeBitField(uint offset)
                {
                    var numBits = ReadUInt32(offset + 16);
                    if (!document.TryGetReferencedItem(offset, out var wordsItemIndex))
                        return $"{numBits}:<null>";
                    var words = document.GetItem(wordsItemIndex);
                    var values = Enumerable.Range(0, checked((int)words.Count))
                        .Select(index => ReadUInt32(words.DataOffset + checked((uint)(index * 4))))
                        .Select(value => value.ToString("X8"));
                    return $"{numBits}:0x{string.Join(",", values)}";
                }

                foreach (var @operator in cloth.Operators)
                {
                    var item = document.GetItem(@operator.ItemIndex);
                    var accessesOffset = item.DataOffset + 56;
                    if (!document.TryGetReferencedItem(accessesOffset, out var accessItemIndex))
                    {
                        Console.WriteLine($"op={@operator.Index}:{@operator.ClassName}|transforms=[]");
                        continue;
                    }

                    var accesses = document.GetItem(accessItemIndex);
                    for (uint accessIndex = 0; accessIndex < accesses.Count; accessIndex++)
                    {
                        var accessOffset = accesses.DataOffset + accessIndex * 32u;
                        var fieldCount = ReadUInt32(accessOffset + 24);
                        if (!document.TryGetReferencedItem(accessOffset + 16, out var trackerItemIndex))
                        {
                            Console.WriteLine($"op={@operator.Index}:{@operator.ClassName}|set={ReadUInt32(accessOffset)}|fieldCount={fieldCount}|trackers=<null>");
                            continue;
                        }

                        var trackers = document.GetItem(trackerItemIndex);
                        var values = Enumerable.Range(0, checked((int)trackers.Count))
                            .Select(index =>
                            {
                                var trackerOffset = trackers.DataOffset + checked((uint)(index * 72));
                                return $"r{DescribeBitField(trackerOffset)};b{DescribeBitField(trackerOffset + 24)};w{DescribeBitField(trackerOffset + 48)}";
                            });
                        Console.WriteLine(
                            $"op={@operator.Index}:{@operator.ClassName}|set={ReadUInt32(accessOffset)}|" +
                            $"fieldCount={fieldCount}|storageCount={trackers.Count}|trackers=[{string.Join("|", values)}]");
                    }
                }
                return;
            }
            if (args.Length == 4 && string.Equals(args[0], "--append-bphcl-cloths", StringComparison.OrdinalIgnoreCase))
            {
                var target = NativeBphclDocument.Open(args[1]);
                var source = NativeBphclDocument.Open(args[2]);
                var merged = target;

                for (var clothIndex = 0; clothIndex < source.Cloths.Count; clothIndex++)
                {
                    var mergedBytes = NativeBphclMerge.MergeCompleteCloth(merged, source, clothIndex);
                    merged = NativeBphclDocument.Parse(mergedBytes);
                }

                File.WriteAllBytes(args[3], merged.Bytes);
                Console.WriteLine($"BPHCL-APPEND|baseCloths={target.Cloths.Count}|importedCloths={source.Cloths.Count}|totalCloths={merged.Cloths.Count}|output={args[3]}");
                return;
            }
            if (args.Length == 6 && string.Equals(args[0], "--replace-bphcl-cloth", StringComparison.OrdinalIgnoreCase))
            {
                var target = NativeBphclDocument.Open(args[1]);
                var source = NativeBphclDocument.Open(args[2]);
                var targetClothIndex = int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
                var sourceClothIndex = int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture);
                NativeBphclWriter.SaveReplacedCloth(target, source, targetClothIndex, sourceClothIndex, args[3]);
                var result = NativeBphclDocument.Open(args[3]);
                Console.WriteLine($"BPHCL-REPLACE|target={targetClothIndex}|source={sourceClothIndex}|cloths={result.Cloths.Count}|output={args[3]}");
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--compare-bphcl-fresh-runtime", StringComparison.OrdinalIgnoreCase))
            {
                CompareBphclFreshRuntime(NativeBphclDocument.Open(args[1]), NativeBphclDocument.Open(args[2]));
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--inspect-skin-layout", StringComparison.OrdinalIgnoreCase))
            {
                InspectSkinLayout(args[1], args[2]);
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--export-hkcl-bphcl", StringComparison.OrdinalIgnoreCase))
            {
                var source = new HkclService();
                source.Load(args[1]);
                var graph = HkclToBphclStandaloneGraphBuilder.Build(source);
                File.WriteAllBytes(args[2], NativeBphclStandaloneGraphWriter.Write(graph));
                Console.WriteLine($"HKCL->BPHCL|cloths={graph.Cloths.Count}|output={args[2]}");
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--rebuild-native-bphcl-standalone", StringComparison.OrdinalIgnoreCase))
            {
                var source = new HkclService();
                source.Load(args[1]);
                var graph = HkclToBphclStandaloneGraphBuilder.BuildNativeReconstruction(source);
                File.WriteAllBytes(args[2], NativeBphclStandaloneGraphWriter.Write(graph));
                Console.WriteLine($"BPHCL-NATIVE-REBUILD|cloths={graph.Cloths.Count}|output={args[2]}");
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-conversion-matches", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(ConversionMatchAnalyzer.Format(ConversionMatchAnalyzer.Analyze(args[1])));
                return;
            }

            if (args.Length == 3 && string.Equals(args[0], "--export-two-key-sidecar-tests", StringComparison.OrdinalIgnoreCase))
            {
                if (!Path.GetExtension(args[1]).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("--export-two-key-sidecar-tests expects a .bphhb source file.");

                var stem = Path.GetFileNameWithoutExtension(args[1]);
                var results = BphyssbBridge.ExportTwoKeyCurveTests(BphhbBridge.Load(args[1]), args[2], stem);
                foreach (var result in results)
                    Console.WriteLine($"BPHHB-TWO-KEY-TEST|output={result.Path}|curves={result.CurveCount}");
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--export-sidecar-orientation-tests", StringComparison.OrdinalIgnoreCase))
            {
                if (!Path.GetExtension(args[1]).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("--export-sidecar-orientation-tests expects a .bphhb source file.");

                var stem = Path.GetFileNameWithoutExtension(args[1]);
                var results = BphyssbBridge.ExportSupportBoneOrientationTests(BphhbBridge.Load(args[1]), args[2], stem, "Head_B_Assist");
                foreach (var result in results)
                    Console.WriteLine($"BPHHB-ORIENTATION-TEST|output={result.Path}|curves={result.CurveCount}");
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--convert-sidecar", StringComparison.OrdinalIgnoreCase))
            {
                var extension = Path.GetExtension(args[1]);
                if (extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
                {
                    var result = BphyssbBridge.Export(BphhbBridge.Load(args[1]), args[2]);
                    Console.WriteLine($"BPHHB->BPHYSSB|bones={result.BoneCount}|curves={result.CurveCount}|main={result.MainBoneCount}|support={result.SupportBoneCount}");
                }
                else if (extension.Equals(".bphyssb", StringComparison.OrdinalIgnoreCase))
                {
                    var result = BphhbBridge.Export(BphyssbBridge.Load(args[1]), args[2]);
                    var graph = result.Graph!;
                    Console.WriteLine($"BPHYSSB->BPHHB|bones={graph.Bones.Count}|curves={graph.CurveCount}|drivers={graph.Drivers.Count}|driven={graph.Driven.Count}|outputs={graph.Outputs.Count}");
                }
                else
                {
                    throw new InvalidOperationException("--convert-sidecar expects a .bphhb or .bphyssb source file.");
                }

                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-sidecar", StringComparison.OrdinalIgnoreCase))
            {
                var extension = Path.GetExtension(args[1]);
                if (extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
                {
                    var helper = BphhbBridge.Load(args[1]);
                    var graph = helper.Graph ?? throw new InvalidOperationException("BPHHB graph could not be decoded.");
                    Console.WriteLine($"BPHHB|bones={graph.Bones.Count}|drivers={graph.Drivers.Count}|driven={graph.Driven.Count}|poses={graph.PoseDriven.Count}|outputs={graph.Outputs.Count}|curves={graph.CurveCount}");
                    foreach (var (bone, index) in graph.Bones.Select((bone, index) => (bone, index)))
                        Console.WriteLine($"bone|{index}|{bone}");
                    foreach (var driver in graph.Drivers)
                        Console.WriteLine($"driver|bone={driver.BoneId}|base={driver.BaseBoneId}|translation={driver.Translation}|rotation={driver.Rotation}");
                    foreach (var driven in graph.Driven)
                        Console.WriteLine($"driven|bone={driven.BoneId}|translate={driven.TranslateDrivenId}|rotate={driven.RotateDrivenId}");
                }
                else if (extension.Equals(".bphyssb", StringComparison.OrdinalIgnoreCase))
                {
                    var support = BphyssbBridge.Load(args[1]);
                    var graph = support.Graph ?? throw new InvalidOperationException("BPHYSSB graph could not be decoded.");
                    Console.WriteLine($"BPHYSSB|bones={graph.Bones.Count}|main={graph.MainBones.Count}|support={graph.SupportBones.Count}|single={graph.OutputSingleCount}|double={graph.OutputDoubleCount}|curves={graph.CurveCount}");
                    foreach (var (bone, index) in graph.Bones.Select((bone, index) => (bone, index)))
                        Console.WriteLine($"bone|{index}|{bone}");
                    foreach (var main in graph.MainBones)
                        Console.WriteLine($"main|bone={main.BoneId}|space={main.SpaceId}|translation={main.Translation}|rotation={main.Rotation}");
                    foreach (var driven in graph.SupportBones)
                        Console.WriteLine($"support|bone={driven.BoneId}|space={driven.SpaceId}|translation={driven.Translation}|rotation={driven.Rotation}");
                }
                else
                    throw new InvalidOperationException("--inspect-sidecar expects a .bphhb or .bphyssb file.");
                return;
            }

            if (args.Length == 3 && string.Equals(args[0], "--compare-sidecar-wiring", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in SidecarWiringDiagnostics.Compare(args[1], args[2]))
                    Console.WriteLine(line);
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--audit-sidecar-curve-layouts", StringComparison.OrdinalIgnoreCase))
            {
                var root = args[1];
                var extension = Directory.EnumerateFiles(root, "*.bphhb", SearchOption.AllDirectories).Any()
                    ? ".bphhb"
                    : ".bphyssb";
                var layouts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var path in Directory.EnumerateFiles(root, $"*{extension}", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    AampReadArchive archive = extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase)
                        ? BphhbBridge.Load(path).Archive ?? throw new InvalidDataException($"Could not decode {path}.")
                        : BphyssbBridge.Load(path).Archive ?? throw new InvalidDataException($"Could not decode {path}.");
                    var data = archive.Root.Children.FirstOrDefault(child => child.FindChild("connection_curve_list") != null)
                        ?? throw new InvalidDataException($"{path} has no connection_curve_list.");
                    var curves = data.FindChild("connection_curve_list")!;
                    var keyCounts = curves.Objects.Select(curve =>
                    {
                        if (extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
                            return curve.Parameters.FirstOrDefault(parameter => parameter.Hash == 139113150u)?.Bytes is { Length: >= 4 } bytes
                                ? BitConverter.ToInt32(bytes, 0)
                                : -1;
                        return curve.Parameters.Count(parameter => parameter.Hash is 1692570776u or 333816846u or 2330785204u);
                    });
                    var layout = string.Join(",", keyCounts.GroupBy(count => count).OrderBy(group => group.Key).Select(group => $"{group.Key}x{group.Count()}"));
                    if (!layouts.TryGetValue(layout, out var paths))
                        layouts.Add(layout, paths = new List<string>());
                    paths.Add(path);
                }
                foreach (var layout in layouts.OrderByDescending(entry => entry.Value.Count).ThenBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    Console.WriteLine($"layout|{layout.Key}|files={layout.Value.Count}");
                    foreach (var path in layout.Value.Take(20))
                        Console.WriteLine($"  {Path.GetFileName(path)}");
                }
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--compare-sidecar-curve-values", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in SidecarWiringDiagnostics.CompareCurveValues(args[1], args[2]))
                    Console.WriteLine(line);
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-sidecar-curves", StringComparison.OrdinalIgnoreCase))
            {
                var extension = Path.GetExtension(args[1]);
                AampReadArchive archive = extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase)
                    ? BphhbBridge.Load(args[1]).Archive ?? throw new InvalidDataException("BPHHB graph could not be decoded.")
                    : extension.Equals(".bphyssb", StringComparison.OrdinalIgnoreCase)
                        ? BphyssbBridge.Load(args[1]).Archive ?? throw new InvalidDataException("BPHYSSB graph could not be decoded.")
                        : throw new InvalidOperationException("--inspect-sidecar-curves expects a .bphhb or .bphyssb file.");
                var data = archive.Root.Children.FirstOrDefault(child => child.FindChild("connection_curve_list") != null)
                    ?? throw new InvalidDataException("The sidecar contains no connection_curve_list.");
                var curves = data.FindChild("connection_curve_list")!;
                Console.WriteLine($"format={archive.Type};curves={curves.Objects.Count}");
                foreach (var (curve, index) in curves.Objects.Select((curve, index) => (curve, index)))
                {
                    var fields = string.Join(",", curve.Parameters.Select(parameter =>
                    {
                        var value = parameter.Type switch
                        {
                            0 or 2 => parameter.Bytes.Length >= 4 ? BitConverter.ToInt32(parameter.Bytes, 0).ToString(System.Globalization.CultureInfo.InvariantCulture) : "<short>",
                            1 => parameter.Bytes.Length >= 4 ? BitConverter.ToSingle(parameter.Bytes, 0).ToString("G9", System.Globalization.CultureInfo.InvariantCulture) : "<short>",
                            5 => parameter.Bytes.Length >= 16
                                ? $"<{BitConverter.ToSingle(parameter.Bytes, 0):G9};{BitConverter.ToSingle(parameter.Bytes, 4):G9};{BitConverter.ToSingle(parameter.Bytes, 8):G9};{BitConverter.ToSingle(parameter.Bytes, 12):G9}>"
                                : "<short-vector>",
                            _ => $"type{parameter.Type}"
                        };
                        return $"0x{parameter.Hash:X8}={value}";
                    }));
                    Console.WriteLine($"{index}|{fields}");
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-sidecar-wiring", StringComparison.OrdinalIgnoreCase))
            {
                var extension = Path.GetExtension(args[1]);
                AampReadArchive archive = extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase)
                    ? BphhbBridge.Load(args[1]).Archive ?? throw new InvalidDataException("BPHHB graph could not be decoded.")
                    : extension.Equals(".bphyssb", StringComparison.OrdinalIgnoreCase)
                        ? BphyssbBridge.Load(args[1]).Archive ?? throw new InvalidDataException("BPHYSSB graph could not be decoded.")
                        : throw new InvalidOperationException("--inspect-sidecar-wiring expects a .bphhb or .bphyssb file.");

                var listNames = extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "output_list", "driver_bone_list", "driven_bone_list", "pose_driven_list" }
                    : new[] { "output_single_list", "output_double_list", "main_bone_list", "support_bone_list" };
                var data = archive.Root.Children.FirstOrDefault(child =>
                    listNames.Any(name => child.FindChild(name) != null))
                    ?? throw new InvalidDataException("The sidecar contains no readable output wiring lists.");

                Console.WriteLine($"format={archive.Type}");
                foreach (var name in listNames)
                {
                    var list = data.FindChild(name);
                    if (list == null)
                        continue;
                    Console.WriteLine($"{name}|count={list.Objects.Count}");
                    foreach (var (item, index) in list.Objects.Select((item, index) => (item, index)))
                    {
                        var fields = string.Join(",", item.Parameters.Select(parameter => parameter.Type switch
                        {
                            1 => $"0x{parameter.Hash:X8}={parameter.AsSingle():G8}",
                            2 => $"0x{parameter.Hash:X8}={parameter.AsInt32()}",
                            _ => $"0x{parameter.Hash:X8}:type{parameter.Type}"
                        }));
                        Console.WriteLine($"  {index}|{fields}");
                    }
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-layout", StringComparison.OrdinalIgnoreCase))
            {
                var layout = NativeBphclReflectionCatalog.GetTypeLayout(args[1]);
                Console.WriteLine($"{layout.Name} | type={layout.TypeIndex} | size=0x{layout.Size:X}");
                foreach (var member in layout.Members)
                    Console.WriteLine($"+0x{member.Offset:X4} | {member.Name} | {member.TypeName} ({member.TypeIndex})");
                return;
            }


            if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-member-type", StringComparison.OrdinalIgnoreCase))
            {
                var layout = NativeBphclReflectionCatalog.GetTypeLayout(args[1]);
                var offsetText = args[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[2][2..] : args[2];
                var offset = Convert.ToUInt32(offsetText, 16);
                var typeIndex = NativeBphclReflectionCatalog.GetMemberTypeIndex(layout.TypeIndex, offset);
                if (typeIndex is null)
                    throw new InvalidDataException($"{layout.Name} has no reflected member at 0x{offset:X}.");
                Console.WriteLine($"{layout.Name}+0x{offset:X}|type={typeIndex}|name={NativeBphclReflectionCatalog.GetTypeName(typeIndex.Value)}|size=0x{NativeBphclReflectionCatalog.GetTypeSize(typeIndex.Value):X}");
                return;
            }
            if (args.Length == 1 && string.Equals(args[0], "--inspect-bphcl-type-templates", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var typeName in new[] { "hkArray", "T*", "hkRefPtr", "hkRefVariant" })
                {
                    foreach (var typeIndex in NativeBphclReflectionCatalog.GetTypeIndexes(typeName))
                    {
                        var templates = string.Join(",", NativeBphclReflectionCatalog.GetTypeTemplates(typeIndex)
                            .Select(value => $"{value.NameIndex}>{value.Value}:{NativeBphclReflectionCatalog.GetTypeName(value.Value)}"));
                        Console.WriteLine($"{typeIndex}|{typeName}|templates=[{templates}]");
                    }
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--audit-bphcl-array-types", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var findings = BphclSchemaAudit.ValidateArrayStorageTypes(document);
                Console.WriteLine($"BPHCL array storage audit: {findings.Count} mismatch(es).");
                foreach (var finding in findings)
                    Console.WriteLine(finding);
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-array-headers", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var payloadOffset = document.DataSection?.PayloadOffset
                    ?? throw new InvalidDataException("BPHCL has no DATA section.");
                var headers = new List<(int ItemIndex, string TypeName, string MemberName, uint Count, uint CapacityAndFlags)>();
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

                    var stride = layout.Size;
                    if (stride == 0 || item.Count == 0)
                        continue;
                    foreach (var member in layout.Members.Where(member => member.TypeName == "hkArray"))
                    {
                        for (uint elementIndex = 0; elementIndex < item.Count; elementIndex++)
                        {
                            var fieldOffset = item.DataOffset + elementIndex * stride + member.Offset;
                            if (fieldOffset > document.DataSection!.PayloadSize - 16)
                                continue;
                            headers.Add((
                                itemIndex,
                                typeName,
                                member.Name,
                                BitConverter.ToUInt32(document.Bytes, payloadOffset + checked((int)(fieldOffset + 8))),
                                BitConverter.ToUInt32(document.Bytes, payloadOffset + checked((int)(fieldOffset + 12)))));
                        }
                    }
                }

                foreach (var group in headers.GroupBy(header => (header.Count, header.CapacityAndFlags)).OrderBy(group => group.Key.Count).ThenBy(group => group.Key.CapacityAndFlags))
                    Console.WriteLine($"arrayHeader|count={group.Key.Count}|capacityAndFlags=0x{group.Key.CapacityAndFlags:X8}|occurrences={group.Count()}");
                foreach (var header in headers.Where(header => header.Count != 0 || header.CapacityAndFlags != 0).Take(32))
                    Console.WriteLine($"nonDefault|item={header.ItemIndex}|{header.TypeName}.{header.MemberName}|count={header.Count}|capacityAndFlags=0x{header.CapacityAndFlags:X8}");
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--audit-bphcl-reference-types", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var findings = BphclSchemaAudit.ValidateDirectReferencePatchTypes(document);
                Console.WriteLine($"BPHCL direct reference audit: {findings.Count} mismatch(es).");
                foreach (var finding in findings)
                    Console.WriteLine(finding);
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-container-storage", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var payloadOffset = document.DataSection?.PayloadOffset
                    ?? throw new InvalidDataException("BPHCL has no DATA section.");
                uint ReadUInt32(uint offset) => BitConverter.ToUInt32(document.Bytes, payloadOffset + checked((int)offset));
                foreach (var variant in document.RootVariants)
                {
                    if (!string.Equals(variant.ClassName, "hclClothContainer", StringComparison.Ordinal) &&
                        !string.Equals(variant.ClassName, "hkaAnimationContainer", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var layout = NativeBphclReflectionCatalog.GetTypeLayout(variant.ClassName);
                    foreach (var member in layout.Members.Where(member => member.TypeName == "hkArray"))
                    {
                        var fieldOffset = variant.ObjectDataOffset + member.Offset;
                        if (!document.TryGetReferencedItem(fieldOffset, out var storageItemIndex))
                        {
                            Console.WriteLine($"{variant.ClassName}.{member.Name}|storage=<null>");
                            continue;
                        }

                        var storage = document.GetItem(storageItemIndex);
                        var expected = NativeBphclReflectionCatalog.GetArrayElementType(member.TypeIndex);
                        var patch = document.InternalPatches.FirstOrDefault(entry => entry.Offsets.Contains(storage.DataOffset));
                        var patchType = patch?.TypeIndex;
                        var declaredCount = ReadUInt32(fieldOffset + 8);
                        var capacityAndFlags = ReadUInt32(fieldOffset + 12);
                        Console.WriteLine(
                            $"{variant.ClassName}.{member.Name}|item={storageItemIndex}|count={storage.Count}|declaredCount={declaredCount}|capacityAndFlags=0x{capacityAndFlags:X8}|" +
                            $"actual={storage.TypeIndex}:{document.GetTypeName(storage.TypeIndex)}|" +
                            $"expected={expected}:{(expected.HasValue ? NativeBphclReflectionCatalog.GetTypeName(expected.Value) : "<none>")}|" +
                            $"entryPatch={patchType}:{(patchType.HasValue ? document.GetTypeName(patchType.Value) : "<none>")}");
                    }
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-cloth-tail", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var payloadOffset = document.DataSection?.PayloadOffset
                    ?? throw new InvalidDataException("BPHCL has no DATA section.");
                uint ReadUInt32(uint offset) => BitConverter.ToUInt32(document.Bytes, payloadOffset + checked((int)offset));

                foreach (var cloth in document.Cloths)
                {
                    var item = document.GetItem(cloth.ItemIndex);
                    var offset = item.DataOffset;
                    var transitions = ReadUInt32(offset + 0x78);
                    var actions = ReadUInt32(offset + 0x88);
                    var generatedAtRuntime = document.Bytes[payloadOffset + checked((int)(offset + 0x90))];
                    var targetPlatform = ReadUInt32(offset + 0x94);
                    Console.WriteLine(
                        $"{cloth.Index}|{cloth.Name}|transitions={transitions}|actions={actions}|" +
                        $"generatedAtRuntime={generatedAtRuntime}|targetPlatform=0x{targetPlatform:X8}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-state-graphs", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                if (document.DataSection is null)
                    throw new InvalidDataException("BPHCL has no DATA section.");

                uint ReadUInt32(uint offset) => BitConverter.ToUInt32(document.Bytes, document.DataSection.PayloadOffset + checked((int)offset));
                IReadOnlyList<int> ReadIntArray(uint fieldOffset)
                {
                    if (!document.TryGetReferencedItem(fieldOffset, out var arrayItemIndex))
                        return Array.Empty<int>();
                    var array = document.GetItem(arrayItemIndex);
                    return Enumerable.Range(0, checked((int)array.Count))
                        .Select(index => unchecked((int)ReadUInt32(array.DataOffset + checked((uint)(index * 4)))))
                        .ToArray();
                }

                IReadOnlyList<IReadOnlyList<int>> ReadNestedIntArrays(uint fieldOffset)
                {
                    if (!document.TryGetReferencedItem(fieldOffset, out var arrayItemIndex))
                        return Array.Empty<IReadOnlyList<int>>();

                    var array = document.GetItem(arrayItemIndex);
                    const uint nestedArraySize = 16;
                    return Enumerable.Range(0, checked((int)array.Count))
                        .Select(index => ReadIntArray(array.DataOffset + checked((uint)index) * nestedArraySize))
                        .ToArray();
                }

                static string FormatNestedIntArrays(IReadOnlyList<IReadOnlyList<int>> arrays) =>
                    $"[{string.Join(',', arrays.Select(values => $"[{string.Join(',', values)}]"))}]";

                foreach (var cloth in document.Cloths)
                {
                    foreach (var state in cloth.States)
                    {
                        var stateItem = document.GetItem(state.ItemIndex);
                        var graphFieldOffset = stateItem.DataOffset + 96;
                        if (!document.TryGetReferencedItem(graphFieldOffset, out var graphItemIndex))
                        {
                            Console.WriteLine($"cloth={cloth.Index}:{cloth.Name}|state={state.Index}:{state.Name}|graph=<null>");
                            continue;
                        }

                        var graph = document.GetItem(graphItemIndex);
                        var branchFieldOffset = graph.DataOffset + 24;
                        var roots = ReadIntArray(graph.DataOffset + 40);
                        var globalChildren = ReadNestedIntArrays(graph.DataOffset + 56);
                        var globalParents = ReadNestedIntArrays(graph.DataOffset + 72);
                        if (!document.TryGetReferencedItem(branchFieldOffset, out var branchesItemIndex))
                        {
                            Console.WriteLine($"cloth={cloth.Index}:{cloth.Name}|state={state.Index}:{state.Name}|graph={graphItemIndex}|branches=<null>|roots=[{string.Join(',', roots)}]");
                            continue;
                        }

                        var branches = document.GetItem(branchesItemIndex);
                        Console.WriteLine($"cloth={cloth.Index}:{cloth.Name}|state={state.Index}:{state.Name}|graph={graphItemIndex}|branches={branches.Count}|roots=[{string.Join(',', roots)}]|children={FormatNestedIntArrays(globalChildren)}|parents={FormatNestedIntArrays(globalParents)}");
                        for (uint branchIndex = 0; branchIndex < branches.Count; branchIndex++)
                        {
                            var branchOffset = branches.DataOffset + branchIndex * 56u;
                            var branchId = unchecked((int)ReadUInt32(branchOffset));
                            var operators = ReadIntArray(branchOffset + 8u);
                            var parents = ReadIntArray(branchOffset + 24u);
                            var children = ReadIntArray(branchOffset + 40u);
                            Console.WriteLine($"  branch={branchId}|ops=[{string.Join(',', operators)}]|parents=[{string.Join(',', parents)}]|children=[{string.Join(',', children)}]");
                        }
                    }
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-state-accesses", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                static string FormatBits(NativeBphclBitField bits) =>
                    $"{bits.NumBits}:0x{string.Join(",", bits.Words.Select(word => word.ToString("X8")))}";

                foreach (var cloth in document.Cloths)
                {
                    var operatorIndices = cloth.Operators
                        .Select((@operator, index) => (@operator.ItemIndex, index))
                        .ToDictionary(entry => entry.ItemIndex, entry => entry.index);

                    foreach (var state in cloth.States)
                    {
                        var operators = string.Join(",", state.OperatorItemIndices.Select(itemIndex =>
                            operatorIndices.TryGetValue(itemIndex, out var index) ? index.ToString() : $"item{itemIndex}"));
                        var buffers = string.Join(";", state.BufferAccesses.Select(access =>
                            $"{access.BufferIndex}:{access.ComponentFlags0}/{access.ComponentFlags1}/" +
                            $"{access.ComponentFlags2}/{access.ComponentFlags3}:tri{(access.TrianglesRead ? 1 : 0)}:shadow{access.ShadowBufferIndex}"));
                        var transforms = string.Join(";", state.TransformSetAccesses.Select(access =>
                            $"set{access.TransformSetIndex}:{access.ComponentFlags0}/{access.ComponentFlags1}:" +
                            $"track[{string.Join("|", access.TransformTrackers.Select(tracker =>
                                $"r{FormatBits(tracker.Read)},b{FormatBits(tracker.ReadBeforeWrite)},w{FormatBits(tracker.Written)}"))}]"));
                        Console.WriteLine(
                            $"cloth={cloth.Index}:{cloth.Name}|state={state.Index}:{state.Name}|" +
                            $"ops=[{operators}]|buffers=[{buffers}]|transforms=[{transforms}]|" +
                            $"sims=[{string.Join(",", state.SimClothItemIndices)}]");
                    }
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--dump-bphcl-operator-items", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                foreach (var cloth in document.Cloths)
                {
                    Console.WriteLine($"cloth|{cloth.Index}|{cloth.Name}|item={cloth.ItemIndex}|offset={cloth.DataOffset}");
                    foreach (var op in cloth.Operators)
                    {
                        var configs = string.Join(";", op.SimulateConfigs.Select(config =>
                            $"[{string.Join(",", config.ConstraintExecution)}]"));
                        Console.WriteLine($"  op|{op.Index}|{op.ClassName}|item={op.ItemIndex}|offset={op.DataOffset}|configs={configs}");
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-sim-runtime", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                foreach (var cloth in document.Cloths)
                {
                    foreach (var simulation in cloth.SimCloths)
                    {
                        var runtime = simulation.Runtime;
                        var transfer = runtime.TransferMotionData;
                        var landscape = runtime.LandscapeCollisionData;
                        var dynamicParticles = simulation.Particles.Where(particle => !particle.Fixed).ToArray();
                        var dynamicMass = dynamicParticles.Sum(particle => particle.Mass);
                        Console.WriteLine(
                            $"{cloth.Index}|{cloth.Name}|sim={simulation.Index}|" +
                            $"particles={simulation.Particles.Count}:dynamic={dynamicParticles.Length}:dynamicMass={dynamicMass:G8}|" +
                            $"normals={runtime.DoNormals}|radius={runtime.MaxParticleRadius:G8}|mass={runtime.TotalMass:G8}|" +
                            $"transfer={runtime.TransferMotionEnabled}:{transfer.TransformSetIndex}/{transfer.TransformIndex}:" +
                            $"T{transfer.TransferTranslationMotion}:{transfer.MinTranslationSpeed:G8}-{transfer.MaxTranslationSpeed:G8}:" +
                            $"R{transfer.TransferRotationMotion}:{transfer.MinRotationSpeed:G8}-{transfer.MaxRotationSpeed:G8}|" +
                            $"landscape={runtime.LandscapeCollisionEnabled}:{runtime.NumLandscapeCollidableParticles}:" +
                            $"{landscape.LandscapeRadius:G8},{landscape.CollisionTolerance:G8}|" +
                            $"pinch={runtime.PinchDetectionEnabled}:flags{runtime.PerParticlePinchDetectionEnabledFlags.Count}:" +
                            $"colliders{runtime.CollidablePinchingData.Count}:range{runtime.MinPinchedParticleIndex}-{runtime.MaxPinchedParticleIndex}|" +
                            $"pairs={runtime.MaxCollisionPairs}|flips={simulation.TriangleFlips.Count}|" +
                            $"pinchData={string.Join(',', runtime.CollidablePinchingData.Select(value => $"{value.Enabled}/{value.Priority}/{value.Radius:G6}"))}");
                    }
                }
                return;
            }

            if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-patch-owner", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var offsetText = args[2].StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? args[2][2..] : args[2];
                var offset = Convert.ToUInt32(offsetText, 16);
                var patch = document.InternalPatches.FirstOrDefault(group => group.Offsets.Contains(offset));
                var owner = document.GetItemRanges()
                    .Select(entry => (ItemIndex: entry.Key, Range: entry.Value))
                    .FirstOrDefault(entry => offset >= entry.Range.Start && offset < entry.Range.End);
                Console.WriteLine($"offset=0x{offset:X}|patch={patch?.TypeIndex}:{(patch is null ? "<none>" : document.GetTypeName(patch.TypeIndex))}|owner={owner.ItemIndex}:{document.GetTypeName(document.GetItem(owner.ItemIndex).TypeIndex)}|relative=0x{offset - owner.Range.Start:X}");
                return;
            }
            if (args.Length == 3 && string.Equals(args[0], "--inspect-bphcl-patch-type", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                var typeIndex = uint.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                var ranges = document.GetItemRanges();
                var group = document.InternalPatches.SingleOrDefault(candidate => candidate.TypeIndex == typeIndex)
                    ?? throw new InvalidDataException($"BPHCL has no PTCH group for type {typeIndex}.");
                Console.WriteLine($"patchType|{typeIndex}:{document.GetTypeName(typeIndex)}|count={group.Offsets.Count}");
                foreach (var offset in group.Offsets)
                {
                    var owner = ranges
                        .Select(entry => (ItemIndex: entry.Key, Range: entry.Value))
                        .FirstOrDefault(entry => offset >= entry.Range.Start && offset < entry.Range.End);
                    var item = document.GetItem(owner.ItemIndex);
                    var target = document.TryGetReferencedItem(offset, out var targetIndex) ? targetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?";
                    Console.WriteLine($"offset=0x{offset:X}|owner={owner.ItemIndex}:{document.GetTypeName(item.TypeIndex)}+0x{offset - item.DataOffset:X}|target={target}");
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-patches", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                Console.WriteLine($"PTCH|groups={document.InternalPatches.Count}|fixups={document.InternalPatches.Sum(group => group.Offsets.Count)}");
                foreach (var group in document.InternalPatches.OrderBy(group => group.TypeIndex))
                {
                    var typeName = document.GetTypeName(group.TypeIndex) ?? $"<type {group.TypeIndex}>";
                    var first = group.Offsets.Count == 0 ? 0 : group.Offsets.Min();
                    var last = group.Offsets.Count == 0 ? 0 : group.Offsets.Max();
                    Console.WriteLine($"{group.TypeIndex}|{typeName}|count={group.Offsets.Count}|range=0x{first:X}-0x{last:X}");
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-aamp", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                if (document.Header.ParameterSize == 0)
                    throw new InvalidDataException("BPHCL does not contain an embedded AAMP block.");

                var offset = checked((int)document.Header.ParameterOffset);
                var size = checked((int)document.Header.ParameterSize);
                var archiveSize = BitConverter.ToInt32(document.Bytes, offset + 0x0c);
                if (archiveSize <= 0 || archiveSize > size)
                    throw new InvalidDataException("BPHCL declares an invalid embedded AAMP size.");
                var archive = BotwAampReader.Read(document.Bytes.AsSpan(offset, archiveSize).ToArray());
                var knownLists = new[] { "cloth_mesh_list", "collidable_list" };
                var knownParameters = new[]
                {
                    "Name", "BaseBone", "BoneCorrection", "BoneCorrectionAxisOrder",
                    "Twist", "TwistSwingAxis", "TwistAngleCoef", "TwistMaxAngle",
                    "KeepBoneLength", "Preset", "ForReplace"
                };

                static string DescribeValue(AampReadParameter parameter) => parameter.Type switch
                {
                    0 => parameter.AsBoolean().ToString(),
                    1 => parameter.AsSingle().ToString("G9"),
                    2 => parameter.AsInt32().ToString(),
                    7 or 8 or 15 or 20 => parameter.AsString(),
                    _ => $"type={parameter.Type}, bytes={Convert.ToHexString(parameter.Bytes)}"
                };

                Console.WriteLine($"AAMP|type={archive.Type}|size={size}");
                foreach (var list in archive.Root.Children)
                {
                    var listName = knownLists.FirstOrDefault(name => BotwAampWriter.Crc32(name) == list.Hash)
                        ?? $"0x{list.Hash:X8}";
                    Console.WriteLine($"{listName}|objects={list.Objects.Count}");
                    foreach (var (item, index) in list.Objects.Select((item, index) => (item, index)))
                    {
                        var fields = string.Join(", ", item.Parameters.Select(parameter =>
                        {
                            var name = knownParameters.FirstOrDefault(candidate => BotwAampWriter.Crc32(candidate) == parameter.Hash)
                                ?? $"0x{parameter.Hash:X8}";
                            return $"{name}={DescribeValue(parameter)}";
                        }));
                        Console.WriteLine($"  {index}|{fields}");
                    }
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-skins", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                foreach (var cloth in document.Cloths)
                {
                    var operators = string.Join(",", cloth.Operators.Select(op => op.ClassName));
                    var objectSkins = string.Join(",", cloth.ObjectSpaceSkins.Select(skin => $"op{skin.OperatorIndex}:buf{skin.OutputBufferIndex}:set{skin.TransformSetIndex}:subset{skin.TransformSubset.Count}:mat{skin.BoneFromSkinMeshTransforms.Count}:local{skin.LocalPs.Count}/{skin.LocalUnpackedPs.Count}"));
                    var boneSkins = string.Join(",", cloth.BoneSpaceSkins.Select(skin => $"op{skin.OperatorIndex}:buf{skin.OutputBufferIndex}:set{skin.TransformSetIndex}:subset{skin.TransformSubset.Count}:local{skin.LocalPs.Count}/{skin.LocalUnpackedPs.Count}"));
                    var deformers = string.Join(",", cloth.SimpleMeshBoneDeformers.Select(deformer => $"op{deformer.OperatorIndex}:in{deformer.InputBufferIndex}:set{deformer.OutputTransformSetIndex}:pairs{deformer.TriangleBonePairs.Count}:local{deformer.LocalBoneTransforms.Count}"));
                    var states = string.Join(";", cloth.States.Select(state => $"#{state.Index}:{state.Name}:ops[{string.Join(',', state.OperatorItemIndices)}]:buf[{string.Join(',', state.BufferAccesses.Select(access => $"{access.BufferIndex}/{access.ShadowBufferIndex}:{access.ComponentFlags0},{access.ComponentFlags1},{access.ComponentFlags2},{access.ComponentFlags3}:tri{access.TrianglesRead}"))}]:set[{string.Join(',', state.TransformSetAccesses.Select(access => $"{access.TransformSetIndex}:{access.ComponentFlags0},{access.ComponentFlags1}:track{access.TransformTrackers.Count}"))}]:sim[{string.Join(',', state.SimClothItemIndices)}]"));
                    Console.WriteLine($"{cloth.Index}|{cloth.Name}|ops={operators}|objectSkins={objectSkins}|boneSkins={boneSkins}|deformers={deformers}|states={states}");
                }
                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-skeleton", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                foreach (var skeleton in document.Skeletons)
                {
                    Console.WriteLine($"skeleton[{skeleton.Index}]|{skeleton.Name}|bones={skeleton.Bones.Count}");
                    foreach (var bone in skeleton.Bones)
                        Console.WriteLine($"  {bone.Index}|parent={bone.ParentIndex}|{bone.Name}");
                }
                return;
            }
            if (args.Length == 1 && string.Equals(args[0], "--inspect-hkx-simulate-operator", StringComparison.OrdinalIgnoreCase))
            {
                var type = typeof(hclSimulateOperator);
                Console.WriteLine(type.FullName);
                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                    Console.WriteLine($"field | {field.FieldType.Name} | {field.Name}");
                foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                    Console.WriteLine($"property | {property.PropertyType.Name} | {property.Name}");
                return;
            }

            if (args.Length == 1 && string.Equals(args[0], "--inspect-hkx-local-range", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var type in typeof(hclLocalRangeConstraintSet).Assembly.GetTypes()
                             .Where(type => type.Name.Contains("LocalRangeConstraintSet", StringComparison.Ordinal)))
                {
                    Console.WriteLine(type.FullName);
                    foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                        Console.WriteLine($"field | {field.FieldType.Name} | {field.Name}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-hkx-type", StringComparison.OrdinalIgnoreCase))
            {
                var matches = typeof(hclSimulateOperator).Assembly.GetTypes()
                    .Where(type => type.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase))
                    .OrderBy(type => type.FullName, StringComparer.Ordinal);

                foreach (var type in matches)
                {
                    Console.WriteLine(type.FullName);
                    foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                        Console.WriteLine($"field | {field.FieldType.Name} | {field.Name}");
                    foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                        Console.WriteLine($"property | {property.PropertyType.Name} | {property.Name}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-collider-map", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                foreach (var (cloth, clothIndex) in document.Cloths.Select((cloth, index) => (cloth, index)))
                {
                    var skeleton = document.Skeletons.ElementAtOrDefault(clothIndex);
                    if (skeleton == null)
                        continue;

                    foreach (var simulation in cloth.SimCloths)
                    {
                        for (var slot = 0; slot < simulation.CollidableItemIndices.Count; slot++)
                        {
                            if (slot >= simulation.CollidableTransformMap.TransformIndices.Count ||
                                slot >= simulation.CollidableTransformMap.Offsets.Count)
                                continue;

                            var collider = document.Colliders.FirstOrDefault(value => value.ItemIndex == simulation.CollidableItemIndices[slot]);
                            if (collider == null)
                                continue;

                            var boneIndex = checked((int)simulation.CollidableTransformMap.TransformIndices[slot]);
                            var boneWorld = GetBphclBoneWorld(skeleton, boneIndex);
                            var offset = simulation.CollidableTransformMap.Offsets[slot];
                            var stored = new Matrix4x4(
                                collider.AxisX.X, collider.AxisX.Y, collider.AxisX.Z, collider.AxisX.W,
                                collider.AxisY.X, collider.AxisY.Y, collider.AxisY.Z, collider.AxisY.W,
                                collider.AxisZ.X, collider.AxisZ.Y, collider.AxisZ.Z, collider.AxisZ.W,
                                collider.Translation.X, collider.Translation.Y, collider.Translation.Z, collider.Translation.W);
                            var offsetThenBone = offset * boneWorld;
                            var boneThenOffset = boneWorld * offset;

                            Console.WriteLine(
                                $"{clothIndex}|{cloth.Name}|slot={slot}|item={simulation.CollidableItemIndices[slot]}|{collider.Name}|bone={boneIndex}|" +
                                $"offset*bone={MatrixDistance(stored, offsetThenBone):G7}|" +
                                $"bone*offset={MatrixDistance(stored, boneThenOffset):G7}|" +
                                $"stored={stored.M41:G7},{stored.M42:G7},{stored.M43:G7}|" +
                                $"left={offsetThenBone.M41:G7},{offsetThenBone.M42:G7},{offsetThenBone.M43:G7}|" +
                                $"right={boneThenOffset.M41:G7},{boneThenOffset.M42:G7},{boneThenOffset.M43:G7}");
                        }
                    }
                }
                return;
            }

            if (args.Length == 3 && string.Equals(args[0], "--compare-bphcl-skeletons", StringComparison.OrdinalIgnoreCase))
            {
                var native = NativeBphclDocument.Open(args[1]);
                var fresh = NativeBphclDocument.Open(args[2]);
                var skeletonCount = Math.Min(native.Skeletons.Count, fresh.Skeletons.Count);
                for (var skeletonIndex = 0; skeletonIndex < skeletonCount; skeletonIndex++)
                {
                    var nativeSkeleton = native.Skeletons[skeletonIndex];
                    var freshSkeleton = fresh.Skeletons[skeletonIndex];
                    var freshByName = freshSkeleton.Bones
                        .GroupBy(bone => bone.Name, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                    foreach (var nativeBone in nativeSkeleton.Bones)
                    {
                        if (!freshByName.TryGetValue(nativeBone.Name, out var freshBone))
                        {
                            Console.WriteLine($"skeleton={skeletonIndex}|{nativeSkeleton.Name}|bone={nativeBone.Index}|{nativeBone.Name}|missing=fresh");
                            continue;
                        }

                        var translation = Vector4.Distance(nativeBone.Translation, freshBone.Translation);
                        var rotation = MathF.Min(
                            Vector4.Distance(nativeBone.Rotation, freshBone.Rotation),
                            Vector4.Distance(nativeBone.Rotation, -freshBone.Rotation));
                        var parentMatches = nativeBone.ParentIndex == freshBone.ParentIndex;
                        if (!parentMatches || translation > 0.000001f || rotation > 0.000001f)
                        {
                            Console.WriteLine(
                                $"skeleton={skeletonIndex}|{nativeSkeleton.Name}|bone={nativeBone.Index}|{nativeBone.Name}|" +
                                $"fresh={freshBone.Index}|parent={nativeBone.ParentIndex}/{freshBone.ParentIndex}|" +
                                $"translation={translation:G9}|rotation={rotation:G9}|" +
                                $"nativeT={nativeBone.Translation.X:G7},{nativeBone.Translation.Y:G7},{nativeBone.Translation.Z:G7},{nativeBone.Translation.W:G7}|" +
                                $"freshT={freshBone.Translation.X:G7},{freshBone.Translation.Y:G7},{freshBone.Translation.Z:G7},{freshBone.Translation.W:G7}|" +
                                $"nativeR={nativeBone.Rotation.X:G7},{nativeBone.Rotation.Y:G7},{nativeBone.Rotation.Z:G7},{nativeBone.Rotation.W:G7}|" +
                                $"freshR={freshBone.Rotation.X:G7},{freshBone.Rotation.Y:G7},{freshBone.Rotation.Z:G7},{freshBone.Rotation.W:G7}");
                        }
                    }
                }

                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphcl-bend-rest", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                foreach (var cloth in document.Cloths)
                {
                    foreach (var simulation in cloth.SimCloths)
                    {
                        foreach (var set in simulation.ConstraintSets.Where(set =>
                                     string.Equals(set.ClassName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal)))
                        {
                            var maximum = set.Values.TryGetValue("maxRestPoseHeightSq", out var value)
                                ? value
                                : 0.0;
                            var radii = simulation.Particles.Select(particle => particle.Radius).ToArray();
                            var triangles = simulation.TriangleIndices
                                .Chunk(3)
                                .Where(triangle => triangle.Length == 3 && triangle.All(index => index < simulation.Particles.Count))
                                .Select(triangle =>
                                {
                                    var a = new Vector3(simulation.Particles[triangle[0]].Position.X, simulation.Particles[triangle[0]].Position.Y, simulation.Particles[triangle[0]].Position.Z);
                                    var b = new Vector3(simulation.Particles[triangle[1]].Position.X, simulation.Particles[triangle[1]].Position.Y, simulation.Particles[triangle[1]].Position.Z);
                                    var c = new Vector3(simulation.Particles[triangle[2]].Position.X, simulation.Particles[triangle[2]].Position.Y, simulation.Particles[triangle[2]].Position.Z);
                                    return Vector3.Cross(b - a, c - a).Length() * 0.5f;
                                })
                                .ToArray();
                            Console.WriteLine(
                                $"cloth={cloth.Name}|simulation={simulation.Name}|set={set.Index}|maxRestPoseHeightSq={maximum:G9}|" +
                                $"radius={radii.Min():G9}-{radii.Max():G9}|triangleArea={triangles.DefaultIfEmpty().Min():G9}-{triangles.DefaultIfEmpty().Max():G9}");
                            foreach (var link in set.Links)
                            {
                                if (link.ParticleA is not int particleA || link.ParticleB is not int particleB ||
                                    !link.Values.TryGetValue("particleC", out var particleCValue) ||
                                    !link.Values.TryGetValue("particleD", out var particleDValue))
                                {
                                    continue;
                                }

                                var particleC = checked((int)particleCValue);
                                var particleD = checked((int)particleDValue);
                                if (particleA < 0 || particleB < 0 || particleC < 0 || particleD < 0 ||
                                    particleA >= simulation.Particles.Count || particleB >= simulation.Particles.Count ||
                                    particleC >= simulation.Particles.Count || particleD >= simulation.Particles.Count)
                                {
                                    continue;
                                }

                                var a = new Vector3(simulation.Particles[particleA].Position.X, simulation.Particles[particleA].Position.Y, simulation.Particles[particleA].Position.Z);
                                var b = new Vector3(simulation.Particles[particleB].Position.X, simulation.Particles[particleB].Position.Y, simulation.Particles[particleB].Position.Z);
                                var c = new Vector3(simulation.Particles[particleC].Position.X, simulation.Particles[particleC].Position.Y, simulation.Particles[particleC].Position.Z);
                                var d = new Vector3(simulation.Particles[particleD].Position.X, simulation.Particles[particleD].Position.Y, simulation.Particles[particleD].Position.Z);
                                var edge = b - a;
                                var edgeLengthSquared = edge.LengthSquared();
                                var heightCSquared = edgeLengthSquared > 0.000000001f
                                    ? Vector3.Cross(c - a, edge).LengthSquared() / edgeLengthSquared
                                    : 0.0f;
                                var heightDSquared = edgeLengthSquared > 0.000000001f
                                    ? Vector3.Cross(d - a, edge).LengthSquared() / edgeLengthSquared
                                    : 0.0f;

                                static string Format(Vector3 position) =>
                                    $"{position.X:G9},{position.Y:G9},{position.Z:G9}";
                                var oppositeEdge = d - c;
                                var edgeDot = Vector3.Dot(edge, oppositeEdge);
                                var oppositeLengthSquared = oppositeEdge.LengthSquared();
                                var denominator = edgeLengthSquared * oppositeLengthSquared - edgeDot * edgeDot;
                                var ac = a - c;
                                var closestA = denominator > 0.0000000001f
                                    ? (edgeDot * Vector3.Dot(oppositeEdge, ac) - oppositeLengthSquared * Vector3.Dot(edge, ac)) / denominator
                                    : 0.5f;
                                var closestC = denominator > 0.0000000001f
                                    ? (edgeLengthSquared * Vector3.Dot(oppositeEdge, ac) - edgeDot * Vector3.Dot(edge, ac)) / denominator
                                    : 0.5f;
                                var closestOnAb = a + closestA * edge;
                                var closestOnCd = c + closestC * oppositeEdge;
                                var cross = Vector3.Cross(edge, oppositeEdge);
                                var crossLength = cross.Length();
                                var signedDistance = crossLength > 0.000000001f
                                    ? Vector3.Dot(closestOnCd - closestOnAb, cross / crossLength)
                                    : 0.0f;
                                var normalC = Vector3.Normalize(Vector3.Cross(edge, c - a));
                                var normalD = Vector3.Normalize(Vector3.Cross(d - a, edge));
                                var sine = Vector3.Dot(Vector3.Cross(normalC, normalD), Vector3.Normalize(edge));
                                var cosine = Vector3.Dot(normalC, normalD);
                                var angle = MathF.Atan2(sine, cosine);
                                var values = link.Values;
                                Console.WriteLine(
                                    $"  link={link.Index}|A={particleA}|B={particleB}|C={particleC}|D={particleD}|" +
                                    $"heightC2={heightCSquared:G9}|heightD2={heightDSquared:G9}|" +
                                    $"CD2={Vector3.DistanceSquared(c, d):G9}|AB2={edgeLengthSquared:G9}|" +
                                    $"positions=A({Format(a)}) B({Format(b)}) C({Format(c)}) D({Format(d)})|" +
                                    $"weights={values.GetValueOrDefault("weightA"):G9},{values.GetValueOrDefault("weightB"):G9}," +
                                    $"{values.GetValueOrDefault("weightC"):G9},{values.GetValueOrDefault("weightD"):G9}|" +
                                    $"closest={1.0f - closestA:G9},{closestA:G9},{-(1.0f - closestC):G9},{-closestC:G9}|" +
                                    $"curvature={values.GetValueOrDefault("restCurvature"):G9}|" +
                                    $"distance={signedDistance:G9}|angle={angle:G9}|sin={sine:G9}|cos={cosine:G9}|" +
                                    $"stiffness={values.GetValueOrDefault("bendStiffness"):G9}");
                            }
                        }
                    }
                }

                return;
            }
            if (args.Length == 2 && string.Equals(args[0], "--dump-bphcl-constraints", StringComparison.OrdinalIgnoreCase))
            {
                var document = NativeBphclDocument.Open(args[1]);
                foreach (var cloth in document.Cloths)
                {
                    foreach (var simulation in cloth.SimCloths)
                    {
                        Console.WriteLine($"cloth={cloth.Name} | simulation={simulation.Name}");
                        foreach (var set in simulation.ConstraintSets)
                        {
                            Console.WriteLine($"  {set.Index}: {set.ClassName} | bucket={(set.IsAntiPinch ? "anti-pinch" : "static")} | id={set.ConstraintId} | type={set.ConstraintType} | set=[{string.Join(", ", set.Values.Select(value => $"{value.Key}={value.Value:G8}"))}]");
                            DumpRecords("links", set.Links);
                            DumpRecords("local", set.LocalConstraints);
                            DumpRecords("local stiffness", set.LocalStiffnessConstraints);
                            DumpRecords("transition", set.TransitionParticles);
                        }
                    }
                }
                return;

                static void DumpRecords(string label, IReadOnlyList<NativeBphclConstraintLink> records)
                {
                    if (records.Count == 0)
                        return;
                    foreach (var record in records)
                    {
                        var particles = record.ParticleA.HasValue || record.ParticleB.HasValue
                            ? $" A={record.ParticleA?.ToString() ?? "-"} B={record.ParticleB?.ToString() ?? "-"}"
                            : string.Empty;
                        Console.WriteLine($"    {label}[{record.Index}]{particles} | {string.Join(", ", record.Values.Select(value => $"{value.Key}={value.Value:G8}"))}");
                    }
                }
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-conversion-constraints", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(ConversionMatchAnalyzer.FormatConstraints(ConversionMatchAnalyzer.AnalyzeConstraints(args[1])));
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--audit-collision-masks", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(ConversionMatchAnalyzer.AnalyzeCollisionMasks(args[1]));
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--audit-fresh-conversions", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(ConversionMatchAnalyzer.FormatFreshExports(ConversionMatchAnalyzer.AnalyzeFreshExports(args[1])));
                return;
            }

            if (args.Length == 4 && string.Equals(args[0], "--diff-fresh-cloth", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(ConversionMatchAnalyzer.DescribeFreshClothComparison(args[1], args[2], args[3]));
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-constraint-sets", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                {
                    foreach (var set in service.GetConstraintSetSummaries(clothIndex))
                        Console.WriteLine($"{clothIndex}|{service.GetClothName(clothIndex)}|{set.Index}|{set.ClassName}|{set.Name}|id={set.ConstraintId}|type={set.ConstraintType}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-bphysics", StringComparison.OrdinalIgnoreCase))
            {
                var document = BphysicsService.Load(args[1]);
                Console.WriteLine($"HKCL={document.HkclPath};SubWind={document.SubWindDirection.X:G7},{document.SubWindDirection.Y:G7},{document.SubWindDirection.Z:G7};Frequency={document.SubWindFrequency:G7};Speed={document.SubWindSpeed:G7}");
                foreach (var cloth in document.Cloths)
                {
                    Console.WriteLine($"{cloth.Name}|{cloth.BaseBone}|{cloth.WindFrequency:G7}|{cloth.WindDrag:G7}|{cloth.WindMinSpeed:G7}|{cloth.WindMaxSpeed:G7}|{cloth.SubWindFactorMain:G7}|{cloth.SubWindFactorAdd:G7}|{cloth.WindEnabled}|{cloth.WritebackToLocal}");
                }

                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-conversion-scales", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                foreach (var scale in service.GetBphclConversionScaleSuggestions())
                    Console.WriteLine($"{scale.ClothIndex}|{scale.ClothName}|mass={scale.DefaultMassScale:G7}|constraints={scale.DefaultConstraintScale:G7}|{scale.SuggestionBasis}");
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-fresh-conversion", StringComparison.OrdinalIgnoreCase))
            {
                var source = new HkclService();
                source.Load(args[1]);
                var fresh = source.CreateFreshHkclFromCurrentBphclDocument();
                foreach (var cloth in fresh.GetClothSummaries().Select((summary, index) => (summary, index)))
                {
                    var dynamicParticles = fresh.GetParticleRows(cloth.index)
                        .Where(particle => !particle.Fixed)
                        .Select(particle => $"#{particle.Index}:mass={particle.Mass:G7},invMass={particle.InverseMass:G7}");
                    Console.WriteLine($"{cloth.index}|{cloth.summary}|dynamic=[{string.Join(';', dynamicParticles)}]");

                    foreach (var config in fresh.GetClothRuntimeData(cloth.index).Operators
                                 .SelectMany(@operator => @operator.SimulateConfigs))
                    {
                        Console.WriteLine(
                            $"{cloth.index}|{cloth.summary}|solver=" +
                            $"sub={config.SubSteps},iter={config.NumberOfSolveIterations}," +
                            $"adaptive={config.AdaptConstraintStiffness},allColliders={config.UseAllInstanceCollidables}," +
                            $"selected=[{string.Join(',', config.InstanceCollidablesUsed)}]," +
                            $"execution=[{string.Join(',', config.ConstraintExecution)}]");
                    }

                    var seenConstraints = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var particle in fresh.GetParticleRows(cloth.index))
                    {
                        foreach (var relation in fresh.GetParticleRelationships(cloth.index, particle.Index)
                                     .Where(relation => relation.IsEditable &&
                                                        (relation.Stiffness.HasValue || relation.BendStiffness.HasValue || relation.StretchStiffness.HasValue)))
                        {
                            var key = $"{relation.ConstraintSetIndex}:{relation.LinkIndex}";
                            if (!seenConstraints.Add(key))
                                continue;

                            Console.WriteLine(
                                $"{cloth.index}|{cloth.summary}|constraint={relation.Name}#{relation.LinkIndex}|" +
                                $"stiff={relation.Stiffness?.ToString("G7") ?? "-"}|" +
                                $"bend={relation.BendStiffness?.ToString("G7") ?? "-"}|" +
                                $"stretch={relation.StretchStiffness?.ToString("G7") ?? "-"}");
                        }
                    }
                }

                return;
            }

            if (args.Length == 3 && string.Equals(args[0], "--export-fresh-hkcl", StringComparison.OrdinalIgnoreCase))
            {
                var source = new HkclService();
                source.Load(args[1]);
                var fresh = source.CreateFreshHkclFromCurrentBphclDocument();
                fresh.SaveHkcl(args[2], HkclPlatform.WiiU);
                Console.WriteLine(args[2]);
                return;
            }

            if (args.Length == 3 && string.Equals(args[0], "--export-raw-json", StringComparison.OrdinalIgnoreCase))
            {
                var source = new HkclService();
                source.Load(args[1]);
                source.ExportReadableJson(args[2]);
                Console.WriteLine(args[2]);
                return;
            }

            if (args.Length == 3 && string.Equals(args[0], "--export-hkcl-to-bphcl", StringComparison.OrdinalIgnoreCase))
            {
                var source = new HkclService();
                source.Load(args[1]);
                if (source.IsBphcl || source.IsHelperBoneSidecar)
                    throw new InvalidOperationException("The source must be an HKCL file.");

                var graph = HkclToBphclStandaloneGraphBuilder.Build(source);
                File.WriteAllBytes(args[2], NativeBphclStandaloneGraphWriter.Write(graph));
                var verification = NativeBphclDocument.Open(args[2]);
                if (verification.Cloths.Count != graph.Cloths.Count)
                    throw new InvalidDataException("Generated BPHCL cloth count does not match the source graph.");

                Console.WriteLine($"Exported {verification.Cloths.Count} cloths to {args[2]}");
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-runtime", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                foreach (var (summary, clothIndex) in service.GetClothSummaries().Select((summary, index) => (summary, index)))
                {
                    foreach (var @operator in service.GetClothRuntimeData(clothIndex).Operators)
                    {
                        if (@operator.VertexParticlePairs.Count == 0 && @operator.SimulateConfigs.Count == 0)
                            continue;

                        var anchors = string.Join(',', @operator.VertexParticlePairs
                            .Select(pair => $"{pair.VertexIndex}:{pair.ParticleIndex}"));
                        var configurations = string.Join(';', @operator.SimulateConfigs.Select(config =>
                            $"sub={config.SubSteps},iter={config.NumberOfSolveIterations},adaptive={config.AdaptConstraintStiffness},all={config.UseAllInstanceCollidables},colliders=[{string.Join(',', config.InstanceCollidablesUsed)}],constraints=[{string.Join(',', config.ConstraintExecution)}]"));
                        Console.WriteLine(
                            $"{clothIndex}|{summary}|op={@operator.Index}:{@operator.ClassName}|sim={@operator.SimulationClothIndex}|ref={@operator.ReferenceBufferIndex}|anchors=[{anchors}]|{configurations}");
                    }
                }

                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-runtime-full", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                foreach (var (summary, clothIndex) in service.GetClothSummaries().Select((summary, index) => (summary, index)))
                {
                    var runtime = service.GetClothRuntimeData(clothIndex);
                    Console.WriteLine($"{clothIndex}|{summary}");
                    Console.WriteLine($"  buffers={string.Join(';', runtime.Buffers.Select(buffer => $"#{buffer.Index}:{buffer.ClassName}:v{buffer.VertexCount}:t{buffer.TriangleCount}"))}");
                    Console.WriteLine($"  ops={string.Join(';', runtime.Operators.Select(op => $"#{op.Index}:{op.ClassName}:in{op.InputBufferIndex}:out{op.OutputBufferIndex}:set{op.TransformSetIndex}:sim{op.SimulationClothIndex}:ref{op.ReferenceBufferIndex}:vp{op.VertexParticlePairs.Count}:cfg{op.SimulateConfigs.Count}"))}");
                    Console.WriteLine($"  states={string.Join(';', runtime.States.Select(state => $"#{state.Index}:{state.Name}:ops[{string.Join(',', state.OperatorIndices)}]:buf[{string.Join(',', state.BufferIndices)}]:set[{string.Join(',', state.TransformSetIndices)}]:sim[{string.Join(',', state.SimulationIndices)}]"))}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-simulation-settings", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                var cloths = service.GetClothSummaries();
                for (var clothIndex = 0; clothIndex < cloths.Count; clothIndex++)
                {
                    var settings = service.GetSimulationSettings(clothIndex);
                    Console.WriteLine(
                        $"{clothIndex}|{cloths[clothIndex]}|gravity={settings.GravityX:G7},{settings.GravityY:G7},{settings.GravityZ:G7}|" +
                        $"damping={settings.DampingPerSecond:G7}|collisionTolerance={settings.CollisionTolerance:G7}|" +
                        $"translationTransfer={settings.TransferTranslationMotion}:{settings.MinTranslationSpeed:G7}-{settings.MaxTranslationSpeed:G7}:" +
                        $"{settings.MinTranslationBlend:G7}-{settings.MaxTranslationBlend:G7}|" +
                        $"rotationTransfer={settings.TransferRotationMotion}:{settings.MinRotationSpeed:G7}-{settings.MaxRotationSpeed:G7}:" +
                        $"{settings.MinRotationBlend:G7}-{settings.MaxRotationBlend:G7}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-plane-colliders", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                var cloths = service.GetClothSummaries();
                for (var clothIndex = 0; clothIndex < cloths.Count; clothIndex++)
                {
                    var options = service.GetParticleColliderOptions(clothIndex);
                    var particles = service.GetParticleRows(clothIndex);
                    foreach (var plane in service.GetColliderRows(clothIndex).Where(row => row.IsPlane))
                    {
                        var option = options.FirstOrDefault(candidate => candidate.ColliderIndex == plane.Index);
                        var affected = option == null || option.BitIndex >= 31
                            ? Array.Empty<int>()
                            : particles.Where(particle => (particle.CollisionMask & (1u << option.BitIndex)) != 0)
                                .Select(particle => particle.Index)
                                .ToArray();
                        Console.WriteLine(
                            $"{clothIndex}|{service.GetClothName(clothIndex)}|plane={plane.Index}:{plane.Name}|" +
                            $"slot={(option == null ? "none" : option.BitIndex)}|affected=[{string.Join(',', affected)}]|" +
                            $"point={plane.StartX:G6},{plane.StartY:G6},{plane.StartZ:G6}|" +
                            $"normal={plane.PlaneNormalX:G6},{plane.PlaneNormalY:G6},{plane.PlaneNormalZ:G6}");
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-collider-masks", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                var cloths = service.GetClothSummaries();
                for (var clothIndex = 0; clothIndex < cloths.Count; clothIndex++)
                {
                    var particles = service.GetParticleRows(clothIndex);
                    foreach (var option in service.GetParticleColliderOptions(clothIndex))
                    {
                        var affected = option.BitIndex >= 31
                            ? Array.Empty<int>()
                            : particles.Where(particle => (particle.CollisionMask & (1u << option.BitIndex)) != 0)
                                .Select(particle => particle.Index)
                                .ToArray();
                        Console.WriteLine(
                            $"{clothIndex}|{service.GetClothName(clothIndex)}|slot={option.BitIndex}|" +
                            $"collider={option.ColliderIndex}:{option.Name}|affected=[{string.Join(',', affected)}]");
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-collision-state", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                {
                    foreach (var line in service.GetCollisionDiagnostics(clothIndex))
                        Console.WriteLine($"{clothIndex}|{line}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-collider-shapes", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                {
                    foreach (var collider in service.GetColliderRows(clothIndex))
                    {
                        Console.WriteLine(
                            $"{clothIndex}|{service.GetClothName(clothIndex)}|{collider.Index}|{collider.Name}|" +
                            $"{collider.ShapeType}|bone={collider.BoneIndex}|radius={collider.Radius:G8}|" +
                            $"start={collider.StartX:G8},{collider.StartY:G8},{collider.StartZ:G8}|" +
                            $"end={collider.EndX:G8},{collider.EndY:G8},{collider.EndZ:G8}");
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-local-ranges", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                {
                    var particleCount = service.GetParticleRows(clothIndex).Count;
                    for (var particleIndex = 0; particleIndex < particleCount; particleIndex++)
                    {
                        foreach (var relationship in service.GetParticleRelationships(clothIndex, particleIndex)
                                     .Where(row => row.MaximumDistance.HasValue))
                        {
                            Console.WriteLine(
                                $"{clothIndex}|{service.GetClothName(clothIndex)}|particle={particleIndex}|" +
                                $"{relationship.Kind}:{relationship.Name}|max={relationship.MaximumDistance!.Value:G8}");
                        }
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-particle-relationships", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                {
                    var particleCount = service.GetParticleRows(clothIndex).Count;
                    for (var particleIndex = 0; particleIndex < particleCount; particleIndex++)
                    {
                        foreach (var relationship in service.GetParticleRelationships(clothIndex, particleIndex))
                        {
                            Console.WriteLine(
                                $"{clothIndex}|{service.GetClothName(clothIndex)}|particle={particleIndex}|" +
                                $"{relationship.Kind}:{relationship.Name}|other={relationship.Particles}|{relationship.Details}");
                        }
                    }
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-collider-bindings", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                foreach (var cloth in service.GetClothSummaries().Select((summary, index) => (summary, index)))
                {
                    foreach (var line in service.GetColliderBindingDiagnostics(cloth.index))
                        Console.WriteLine($"{cloth.index}|{cloth.summary}|{line}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--validate-collider-roundtrip", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                for (var clothIndex = 0; clothIndex < service.GetClothSummaries().Count; clothIndex++)
                    service.UpdateColliderRows(service.GetColliderRows(clothIndex));

                foreach (var cloth in service.GetClothSummaries().Select((summary, index) => (summary, index)))
                {
                    foreach (var line in service.GetColliderBindingDiagnostics(cloth.index))
                        Console.WriteLine($"{cloth.index}|{cloth.summary}|{line}");
                }
                return;
            }

            if (args.Length == 2 && string.Equals(args[0], "--inspect-runtime-graph", StringComparison.OrdinalIgnoreCase))
            {
                var service = new HkclService();
                service.Load(args[1]);
                foreach (var (summary, clothIndex) in service.GetClothSummaries().Select((summary, index) => (summary, index)))
                {
                    var runtime = service.GetClothRuntimeData(clothIndex);
                    Console.WriteLine($"cloth|{clothIndex}|{summary}|buffers={runtime.Buffers.Count}|sets={runtime.TransformSets.Count}|ops={runtime.Operators.Count}|states={runtime.States.Count}");
                    foreach (var buffer in runtime.Buffers)
                        Console.WriteLine($"  buffer|{buffer.Index}|{buffer.ClassName}|mesh={buffer.MeshName}|name={buffer.BufferName}|type={buffer.Type}/{buffer.SubType}|vertices={buffer.VertexCount}|triangles={buffer.TriangleCount}");
                    foreach (var transformSet in runtime.TransformSets)
                        Console.WriteLine($"  set|{transformSet.Index}|{transformSet.Name}|type={transformSet.Type}|transforms={transformSet.TransformCount}");
                    foreach (var op in runtime.Operators)
                    {
                        Console.WriteLine($"  op|{op.Index}|{op.ClassName}|in={op.InputBufferIndex}|out={op.OutputBufferIndex}|set={op.TransformSetIndex}|sim={op.SimulationClothIndex}|ref={op.ReferenceBufferIndex}|vertices={op.VertexCount}|pairs={string.Join(',', op.VertexParticlePairs.Select(pair => $"{pair.VertexIndex}>{pair.ParticleIndex}"))}");
                        foreach (var config in op.SimulateConfigs.Select((config, index) => (config, index)))
                            Console.WriteLine($"    simconfig|{config.index}|constraints=[{string.Join(',', config.config.ConstraintExecution)}]|colliders=[{string.Join(',', config.config.InstanceCollidablesUsed)}]|substeps={config.config.SubSteps}|iterations={config.config.NumberOfSolveIterations}|all={config.config.UseAllInstanceCollidables}|adaptive={config.config.AdaptConstraintStiffness}");
                    }
                    foreach (var state in runtime.States)
                    {
                        var buffers = string.Join(',', state.BufferAccesses?.Select(access => $"{access.BufferIndex}/{access.ShadowBufferIndex}:{access.ComponentFlags0},{access.ComponentFlags1},{access.ComponentFlags2},{access.ComponentFlags3}:tri{access.TrianglesRead}") ?? Array.Empty<string>());
                        var sets = string.Join(',', state.TransformSetAccesses?.Select(access => $"{access.TransformSetIndex}:{access.ComponentFlags0},{access.ComponentFlags1}:track{access.TransformTrackers.Count}") ?? Array.Empty<string>());
                        Console.WriteLine($"  state|{state.Index}|{state.Name}|ops=[{string.Join(',', state.OperatorIndices)}]|buffers=[{buffers}]|sets=[{sets}]|sim=[{string.Join(',', state.SimulationIndices)}]");
                    }
                }
                return;
            }
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            if (args.Length > 0)
            {
                var errorPath = Path.Combine(Path.GetTempPath(), "PhysicsTool-command-error.txt");
                File.WriteAllText(errorPath, ex.ToString());
                Console.Error.WriteLine(ex);
                Environment.ExitCode = 1;
                return;
            }

            MessageBox.Show(
                ex.ToString(),
                "PhysicsTool startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    // Diagnostic only: exposes the actual scratch-vertex packing used by the
    // source file. This is intentionally separate from conversion logic.
    private static void InspectSkinLayout(string filePath, string clothSelector)
    {
        var service = new HkclService();
        service.Load(filePath);
        var cloths = service.GetClothSummaries();
        var isIndex = int.TryParse(clothSelector, out var clothIndex);
        if (!isIndex)
        {
            clothIndex = Enumerable.Range(0, cloths.Count).FirstOrDefault(index =>
                cloths[index].Contains(clothSelector, StringComparison.OrdinalIgnoreCase));
        }

        if ((uint)clothIndex >= (uint)cloths.Count ||
            (!isIndex && !cloths[clothIndex].Contains(clothSelector, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"No cloth matches '{clothSelector}'.");
        }

        var runtime = service.GetClothRuntimeData(clothIndex);
        Console.WriteLine($"cloth|{clothIndex}|{cloths[clothIndex]}");
        Console.WriteLine($"buffers|{string.Join(';', runtime.Buffers.Select(buffer => $"{buffer.Index}:{buffer.VertexCount}:{buffer.BufferName}"))}");
        foreach (var op in runtime.Operators.Where(@operator => @operator.VertexParticlePairs.Count > 0))
        {
            Console.WriteLine($"move|op={op.Index}|pairs={string.Join(',', op.VertexParticlePairs.Select(pair => $"{pair.VertexIndex}>{pair.ParticleIndex}"))}");
        }

        foreach (var skin in service.GetSkinOperatorData(clothIndex).OrderBy(skin => skin.OperatorIndex))
        {
            Console.WriteLine($"skin|op={skin.OperatorIndex}|{skin.ClassName}|range={skin.StartVertexIndex}-{skin.EndVertexIndex}|local={skin.LocalPositionBlocks.Count}/{skin.LocalUnpackedPositionBlocks.Count}");
            foreach (var blend in skin.BlendArrays.Where(blend => blend.Blocks.Count > 0))
            {
                for (var blockIndex = 0; blockIndex < blend.Blocks.Count; blockIndex++)
                {
                    var block = blend.Blocks[blockIndex];
                    Console.WriteLine($"blend|{blend.MemberName}|block={blockIndex}|vertices={string.Join(',', block.VertexIndices)}|bones={string.Join(',', block.BoneIndices)}|weights={string.Join(',', block.BoneWeights)}");
                }
            }
        }
    }
    private static void CompareBphclFreshRuntime(NativeBphclDocument native, NativeBphclDocument fresh)
    {
        static string F(float value) => value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
        static string V(Vector4 value) => $"{F(value.X)},{F(value.Y)},{F(value.Z)},{F(value.W)}";
        static string M(Matrix4x4 matrix) => string.Join(",", new[]
        {
            F(matrix.M11), F(matrix.M12), F(matrix.M13), F(matrix.M14),
            F(matrix.M21), F(matrix.M22), F(matrix.M23), F(matrix.M24),
            F(matrix.M31), F(matrix.M32), F(matrix.M33), F(matrix.M34),
            F(matrix.M41), F(matrix.M42), F(matrix.M43), F(matrix.M44)
        });
        static string Numbers(IReadOnlyDictionary<string, double> values) => string.Join(",", values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value.ToString("G12", System.Globalization.CultureInfo.InvariantCulture)}"));
        static string Links(IReadOnlyList<NativeBphclConstraintLink> links) => string.Join(";", links.Select(link =>
            $"{link.ParticleA?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}/" +
            $"{link.ParticleB?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}:" +
            Numbers(link.Values)));
        static string Constraint(NativeBphclConstraintSet set) =>
            $"{set.ClassName}|{set.ConstraintId}/{set.ConstraintType}|values[{Numbers(set.Values)}]|" +
            $"links[{Links(set.Links)}]|local[{Links(set.LocalConstraints)}]|" +
            $"localStiff[{Links(set.LocalStiffnessConstraints)}]|transition[{Links(set.TransitionParticles)}]";
        static string Runtime(NativeBphclSimCloth sim) =>
            $"info={V(sim.SimulationInfo.Gravity)}:{F(sim.SimulationInfo.GlobalDampingPerSecond)}|" +
            $"particles=[{string.Join(";", sim.Particles.Select(p => $"{V(p.Position)}:{p.Fixed}:{F(p.Mass)}:{F(p.InverseMass)}:{F(p.Radius)}:{F(p.Friction)}"))}]|" +
            $"tri=[{string.Join(',', sim.TriangleIndices)}]|flips=[{string.Join(',', sim.TriangleFlips)}]|masks=[{string.Join(',', sim.StaticCollisionMasks)}]|" +
            $"runtime={sim.Runtime.DoNormals}:{F(sim.Runtime.MaxParticleRadius)}:{F(sim.Runtime.TotalMass)}:" +
            $"transfer={sim.Runtime.TransferMotionEnabled}/{sim.Runtime.TransferMotionData.TransformSetIndex}/{sim.Runtime.TransferMotionData.TransformIndex}/" +
            $"T{sim.Runtime.TransferMotionData.TransferTranslationMotion}:{F(sim.Runtime.TransferMotionData.MinTranslationSpeed)}:{F(sim.Runtime.TransferMotionData.MaxTranslationSpeed)}:" +
            $"R{sim.Runtime.TransferMotionData.TransferRotationMotion}:{F(sim.Runtime.TransferMotionData.MinRotationSpeed)}:{F(sim.Runtime.TransferMotionData.MaxRotationSpeed)}|" +
            $"land={sim.Runtime.LandscapeCollisionEnabled}:{sim.Runtime.NumLandscapeCollidableParticles}:{F(sim.Runtime.LandscapeCollisionData.CollisionTolerance)}|" +
            $"pinch={sim.Runtime.PinchDetectionEnabled}:{string.Join(',', sim.Runtime.PerParticlePinchDetectionEnabledFlags.Select(value => value ? '1' : '0'))}:" +
            $"{string.Join(';', sim.Runtime.CollidablePinchingData.Select(value => $"{value.Enabled}/{value.Priority}/{F(value.Radius)}"))}|" +
            $"map={sim.CollidableTransformMap.TransformSetIndex}/[{string.Join(',', sim.CollidableTransformMap.TransformIndices)}]/" +
            $"[{string.Join(';', sim.CollidableTransformMap.Offsets.Select(M))}]|" +
            $"constraintIds=[{string.Join(',', sim.SimulationOperatorIds)}]|sets=[{string.Join("||", sim.ConstraintSets.Select(Constraint))}]";
        static string Operator(NativeBphclOperatorLayout op) =>
            $"{op.ClassName}:in={op.InputBufferIndex}:out={op.OutputBufferIndex}:set={op.TransformSetIndex}:sim={op.SimulationClothIndex}:ref={op.ReferenceBufferIndex}:" +
            $"vertices={op.VertexCount}:{op.StartVertexIn}:{op.StartVertexOut}:pairs=[{string.Join(',', op.VertexParticlePairs.Select(pair => $"{pair.VertexIndex}>{pair.ParticleIndex}"))}]:" +
            $"configs=[{string.Join(';', op.SimulateConfigs.Select(config => $"{string.Join(',', config.ConstraintExecution)}/{string.Join(',', config.InstanceCollidablesUsed)}/{config.SubSteps}/{config.NumberOfSolveIterations}/{config.UseAllInstanceCollidables}/{config.AdaptConstraintStiffness}"))}]";
        static string State(NativeBphclClothState state)
        {
            var buffers = string.Join(";", state.BufferAccesses.Select(access =>
                $"{access.BufferIndex}/{access.ComponentFlags0}/{access.ComponentFlags1}/{access.ComponentFlags2}/{access.ComponentFlags3}/{access.TrianglesRead}/{access.ShadowBufferIndex}"));
            var sets = string.Join(";", state.TransformSetAccesses.Select(access =>
            {
                var trackers = string.Join("|", access.TransformTrackers.Select(tracker =>
                    $"r{string.Join(',', tracker.Read.Words)}:{tracker.Read.NumBits};" +
                    $"b{string.Join(',', tracker.ReadBeforeWrite.Words)}:{tracker.ReadBeforeWrite.NumBits};" +
                    $"w{string.Join(',', tracker.Written.Words)}:{tracker.Written.NumBits}"));
                return $"{access.TransformSetIndex}/{access.ComponentFlags0}/{access.ComponentFlags1}/{trackers}";
            }));
            return $"{state.Name}|ops={string.Join(',', state.OperatorItemIndices)}|buffers={buffers}|sets={sets}";
        }

        var freshByName = fresh.Cloths.ToDictionary(cloth => cloth.Name, StringComparer.Ordinal);
        foreach (var nativeCloth in native.Cloths)
        {
            if (!freshByName.TryGetValue(nativeCloth.Name, out var freshCloth))
            {
                Console.WriteLine($"missing|cloth={nativeCloth.Name}");
                continue;
            }

            var nativeIndex = nativeCloth.Index;
            var freshIndex = freshCloth.Index;
            var nativeSkeleton = native.Skeletons.ElementAtOrDefault(nativeIndex);
            var freshSkeleton = fresh.Skeletons.ElementAtOrDefault(freshIndex);
            Console.WriteLine($"cloth|{nativeCloth.Name}|native={nativeIndex}|fresh={freshIndex}");
            CompareValue("buffers", string.Join(';', nativeCloth.BufferDefinitions.Select(buffer => $"{buffer.ClassName}/{buffer.MeshName}/{buffer.BufferName}/{buffer.Type}/{buffer.SubType}/{buffer.VertexCount}/{buffer.TriangleCount}")), string.Join(';', freshCloth.BufferDefinitions.Select(buffer => $"{buffer.ClassName}/{buffer.MeshName}/{buffer.BufferName}/{buffer.Type}/{buffer.SubType}/{buffer.VertexCount}/{buffer.TriangleCount}")));
            CompareValue("sets", string.Join(';', nativeCloth.TransformSetDefinitions.Select(set => $"{set.Name}/{set.Type}/{set.TransformCount}")), string.Join(';', freshCloth.TransformSetDefinitions.Select(set => $"{set.Name}/{set.Type}/{set.TransformCount}")));
            CompareValue("operators", string.Join("|", nativeCloth.Operators.Select(Operator)), string.Join("|", freshCloth.Operators.Select(Operator)));
            CompareValue("states", string.Join("||", nativeCloth.States.Select(State)), string.Join("||", freshCloth.States.Select(State)));
            CompareValue("simulations", string.Join("###", nativeCloth.SimCloths.Select(Runtime)), string.Join("###", freshCloth.SimCloths.Select(Runtime)));
            CompareValue("skeleton", nativeSkeleton is null ? "<missing>" : string.Join(';', nativeSkeleton.Bones.Select(bone => $"{bone.Name}/{bone.ParentIndex}/{V(bone.Translation)}/{V(bone.Rotation)}/{V(bone.Scale)}")), freshSkeleton is null ? "<missing>" : string.Join(';', freshSkeleton.Bones.Select(bone => $"{bone.Name}/{bone.ParentIndex}/{V(bone.Translation)}/{V(bone.Rotation)}/{V(bone.Scale)}")));
        }

        foreach (var extra in fresh.Cloths.Where(cloth => !native.Cloths.Any(nativeCloth => string.Equals(nativeCloth.Name, cloth.Name, StringComparison.Ordinal))))
            Console.WriteLine($"extra|cloth={extra.Name}");

        static void CompareValue(string section, string nativeValue, string freshValue)
        {
            if (string.Equals(nativeValue, freshValue, StringComparison.Ordinal))
                Console.WriteLine($"  same|{section}");
            else
                Console.WriteLine($"  diff|{section}|native={nativeValue}|fresh={freshValue}");
        }
    }
    private static Matrix4x4 GetBphclBoneWorld(NativeBphclSkeleton skeleton, int boneIndex)
    {
        var bones = skeleton.Bones.ToDictionary(bone => bone.Index);
        var chain = new Stack<NativeBphclBone>();
        var visited = new HashSet<int>();
        var current = boneIndex;
        while (current >= 0 && visited.Add(current) && bones.TryGetValue(current, out var bone))
        {
            chain.Push(bone);
            current = bone.ParentIndex;
        }

        var world = Matrix4x4.Identity;
        while (chain.Count > 0)
        {
            var bone = chain.Pop();
            var rotation = new Quaternion(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W);
            rotation = rotation.LengthSquared() < 0.000001f ? Quaternion.Identity : Quaternion.Normalize(rotation);
            world = Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(bone.Translation.X, bone.Translation.Y, bone.Translation.Z)
                * world;
        }

        return world;
    }

    private static float MatrixDistance(Matrix4x4 left, Matrix4x4 right)
    {
        var maximum = 0.0f;
        for (var row = 0; row < 4; row++)
        for (var column = 0; column < 4; column++)
            maximum = Math.Max(maximum, MathF.Abs(left[row, column] - right[row, column]));
        return maximum;
    }
}


