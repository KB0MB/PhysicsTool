using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace HKCLTool;

internal static class NativeBphclGameReadyCompactor
{
	private readonly record struct CompactRange(uint OldStart, uint OldEnd, uint NewStart);

	public static byte[] Compact(NativeBphclDocument document)
	{
		return Compact(document, preserveItemIndices: false);
	}

	public static byte[] ReindexAllItemsForDiagnostic(NativeBphclDocument document)
	{
		EnsureNoExternalPatches(document);
		if (document.Items.Count == 0 || document.GetItem(0).TypeIndex != 0)
		{
			throw new InvalidDataException("BPHCL ITEM 0 is expected to be the null-pointer sentinel.");
		}
		Dictionary<int, int> dictionary = new Dictionary<int, int>(document.Items.Count) { [0] = 0 };
		for (int i = 1; i < document.Items.Count; i++)
		{
			dictionary[i] = document.Items.Count - i;
		}
		NativeBphclItem[] array = new NativeBphclItem[document.Items.Count];
		array[0] = document.GetItem(0);
		for (int j = 1; j < document.Items.Count; j++)
		{
			array[dictionary[j]] = document.GetItem(j);
		}
		NativeBphclSection nativeBphclSection = document.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
		byte[] array2 = document.Bytes.AsSpan(nativeBphclSection.PayloadOffset, nativeBphclSection.PayloadSize).ToArray();
		List<NativeBphclPatch> list = new List<NativeBphclPatch>(document.InternalPatches.Count);
		checked
		{
			foreach (NativeBphclPatch internalPatch in document.InternalPatches)
			{
				foreach (uint offset in internalPatch.Offsets)
				{
					uint num = BinaryPrimitives.ReadUInt32LittleEndian(array2.AsSpan((int)offset, 4));
					if (num >= document.Items.Count || !dictionary.TryGetValue((int)num, out var value))
					{
						throw new InvalidDataException($"BPHCL PTCH at DATA+0x{offset:X} has an invalid ITEM target.");
					}
					BinaryPrimitives.WriteUInt32LittleEndian(array2.AsSpan((int)offset, 4), (uint)value);
				}
				list.Add(internalPatch);
			}
			return NativeBphclTagFileBuilder.Rebuild(document, array2, array, list);
		}
	}

	public static byte[] ReindexInactiveItemsForDiagnostic(NativeBphclDocument document)
	{
		EnsureNoExternalPatches(document);
		if (document.Items.Count == 0 || document.GetItem(0).TypeIndex != 0)
		{
			throw new InvalidDataException("BPHCL ITEM 0 is expected to be the null-pointer sentinel.");
		}
		if (!document.TryGetReferencedItem(0u, out var itemIndex))
		{
			throw new InvalidDataException("BPHCL root named-variant array could not be resolved.");
		}
		int rootContainerItemIndex = GetRootContainerItemIndex(document);
		HashSet<int> liveItems = document.CollectItemClosure(new int[2] { rootContainerItemIndex, itemIndex }.Concat(document.RootVariants.Select((NativeBphclNamedVariant variant) => variant.ObjectItemIndex))).ToHashSet();
		int[] array = (from index in Enumerable.Range(1, document.Items.Count - 1)
			where !liveItems.Contains(index)
			select index).ToArray();
		if (array.Length < 2)
		{
			throw new InvalidDataException("BPHCL has fewer than two inactive ITEM records to shuffle.");
		}
		Dictionary<int, int> dictionary = Enumerable.Range(0, document.Items.Count).ToDictionary((int index) => index, (int index) => index);
		for (int num = 0; num < array.Length; num++)
		{
			dictionary[array[num]] = array[array.Length - 1 - num];
		}
		NativeBphclItem[] array2 = document.Items.ToArray();
		int[] array3 = array;
		foreach (int num3 in array3)
		{
			array2[dictionary[num3]] = document.GetItem(num3);
		}
		NativeBphclSection nativeBphclSection = document.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
		byte[] array4 = document.Bytes.AsSpan(nativeBphclSection.PayloadOffset, nativeBphclSection.PayloadSize).ToArray();
		checked
		{
			foreach (NativeBphclPatch internalPatch in document.InternalPatches)
			{
				foreach (uint offset in internalPatch.Offsets)
				{
					uint num4 = BinaryPrimitives.ReadUInt32LittleEndian(array4.AsSpan((int)offset, 4));
					if (num4 >= document.Items.Count)
					{
						throw new InvalidDataException($"BPHCL PTCH at DATA+0x{offset:X} has an invalid ITEM target.");
					}
					BinaryPrimitives.WriteUInt32LittleEndian(array4.AsSpan((int)offset, 4), (uint)dictionary[(int)num4]);
				}
			}
			return NativeBphclTagFileBuilder.Rebuild(document, array4, array2, document.InternalPatches);
		}
	}

	public static byte[] CompactPreservingItemIndices(NativeBphclDocument document)
	{
		return Compact(document, preserveItemIndices: true);
	}

	private static byte[] Compact(NativeBphclDocument document, bool preserveItemIndices)
	{
		EnsureNoExternalPatches(document);
		if (document.Items.Count == 0 || document.GetItem(0).TypeIndex != 0)
		{
			throw new InvalidDataException("BPHCL ITEM 0 is expected to be the null-pointer sentinel.");
		}
		if (!document.TryGetReferencedItem(0u, out var itemIndex))
		{
			throw new InvalidDataException("BPHCL root named-variant array could not be resolved.");
		}
		List<int> list = new List<int>
		{
			GetRootContainerItemIndex(document),
			itemIndex
		};
		list.AddRange(document.RootVariants.Select((NativeBphclNamedVariant variant) => variant.ObjectItemIndex));
		int[] array = (from index in document.CollectItemClosure(list)
			where index != 0
			select index).ToArray();
		if (array.Length == 0)
		{
			throw new InvalidDataException("BPHCL compaction found no live non-null ITEM records.");
		}
		IReadOnlyList<NativeBphclPatch> patchesForItems = document.GetPatchesForItems(array);
		Dictionary<int, (uint Start, uint End)> ranges = document.GetItemRanges();
		NativeBphclSection nativeBphclSection = document.DataSection ?? throw new InvalidDataException("BPHCL has no DATA section.");
		uint firstLiveStart = array.Min((int index) => ranges[index].Start);
		Dictionary<uint, uint> alignmentByType = document.TypeDefinitions
			.GroupBy((NativeBphclTypeDefinition definition) => definition.TypeIndex)
			.ToDictionary(
				(IGrouping<uint, NativeBphclTypeDefinition> group) => group.Key,
				(IGrouping<uint, NativeBphclTypeDefinition> group) => group.First().Alignment ?? 1u);
		Dictionary<int, int> dictionary = new Dictionary<int, int>(array.Length + 1) { [0] = 0 };
		int[] array2 = array;
		foreach (int num2 in array2)
		{
			dictionary.Add(num2, preserveItemIndices ? num2 : dictionary.Count);
		}
		Dictionary<uint, CompactRange> dictionary2 = new Dictionary<uint, CompactRange>();
		List<byte> list2 = document.Bytes.AsSpan(nativeBphclSection.PayloadOffset, checked((int)firstLiveStart)).ToArray().ToList();
		foreach (IGrouping<uint, int> itemGroup in array
			.GroupBy((int index) => ranges[index].Start)
			.OrderBy((IGrouping<uint, int> group) => group.Key))
		{
			int item2 = itemGroup.First();
			(uint, uint) tuple = ranges[item2];
			if (!dictionary2.ContainsKey(tuple.Item1))
			{
				if (tuple.Item2 < tuple.Item1 || tuple.Item2 > nativeBphclSection.PayloadSize)
				{
					throw new InvalidDataException("BPHCL live ITEM range exceeds DATA.");
				}
				uint newStart;
				int length;
				checked
				{
					uint requiredAlignment = itemGroup
						.Select((int index) => alignmentByType.GetValueOrDefault(document.GetItem(index).TypeIndex, 1u))
						.DefaultIfEmpty(1u)
						.Max();
					Align(list2, (int)requiredAlignment);
					newStart = (uint)list2.Count;
					length = (int)(tuple.Item2 - tuple.Item1);
				}
				list2.AddRange(document.Bytes.AsSpan(nativeBphclSection.PayloadOffset + checked((int)tuple.Item1), length).ToArray());
				dictionary2.Add(tuple.Item1, new CompactRange(tuple.Item1, tuple.Item2, newStart));
			}
		}
		NativeBphclItem[] array3 = (preserveItemIndices ? Enumerable.Repeat(new NativeBphclItem(0u, 0u, 0u, 0u), document.Items.Count).ToArray() : new NativeBphclItem[array.Length + 1]);
		array3[0] = document.GetItem(0);
		array2 = array;
		checked
		{
			foreach (int num3 in array2)
			{
				NativeBphclItem item = document.GetItem(num3);
				CompactRange compactRange = dictionary2[ranges[num3].Start];
				uint dataOffset = compactRange.NewStart + item.DataOffset - compactRange.OldStart;
				array3[dictionary[num3]] = item with
				{
					DataOffset = dataOffset
				};
			}
			List<NativeBphclPatch> list3 = new List<NativeBphclPatch>();
			foreach (NativeBphclPatch item3 in patchesForItems)
			{
				List<uint> list4 = new List<uint>(item3.Offsets.Count);
				foreach (uint offset in item3.Offsets)
				{
					CompactRange compactRange2 = FindRange(dictionary2.Values, offset);
					uint num4 = compactRange2.NewStart + offset - compactRange2.OldStart;
					uint num5;
					unchecked
					{
						num5 = BinaryPrimitives.ReadUInt32LittleEndian(document.Bytes.AsSpan(nativeBphclSection.PayloadOffset + checked((int)offset), 4));
					}
					if (num5 >= document.Items.Count || !dictionary.TryGetValue((int)num5, out var value))
					{
						throw new InvalidDataException($"Live BPHCL pointer at DATA+0x{offset:X} reaches an item outside the compacted graph.");
					}
					BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(list2).Slice((int)num4, 4), (uint)value);
					list4.Add(num4);
				}
				list3.Add(new NativeBphclPatch(item3.TypeIndex, list4));
			}
			foreach (NativeBphclPatch internalPatch in document.InternalPatches)
			{
				List<uint> list5 = new List<uint>();
				foreach (uint item4 in internalPatch.Offsets.Where((uint offset) => offset < firstLiveStart))
				{
					uint num6;
					unchecked
					{
						num6 = BinaryPrimitives.ReadUInt32LittleEndian(document.Bytes.AsSpan(nativeBphclSection.PayloadOffset + checked((int)item4), 4));
					}
					if (num6 >= document.Items.Count || !dictionary.TryGetValue((int)num6, out var value2))
					{
						throw new InvalidDataException($"BPHCL root pointer at DATA+0x{item4:X} reaches an item outside the compacted graph.");
					}
					BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(list2).Slice((int)item4, 4), (uint)value2);
					list5.Add(item4);
				}
				if (list5.Count > 0)
				{
					list3.Add(new NativeBphclPatch(internalPatch.TypeIndex, list5));
				}
			}
			return NativeBphclTagFileBuilder.Rebuild(document, CollectionsMarshal.AsSpan(list2), array3, list3);
		}
	}

	private static CompactRange FindRange(IEnumerable<CompactRange> ranges, uint offset)
	{
		foreach (CompactRange range in ranges)
		{
			if (offset >= range.OldStart && offset < range.OldEnd)
			{
				return range;
			}
		}
		throw new InvalidDataException($"BPHCL patch at DATA+0x{offset:X} is outside the compacted graph.");
	}

	private static int GetRootContainerItemIndex(NativeBphclDocument document)
	{
		int[] array = (from entry in document.Items.Select((NativeBphclItem item, int index) => (item: item, index: index))
			where entry.index != 0 && entry.item.DataOffset == 0 && string.Equals(document.GetTypeName(entry.item.TypeIndex), "hkRootLevelContainer", StringComparison.Ordinal)
			select entry.index).ToArray();
		if (array.Length != 1)
		{
			throw new InvalidDataException($"BPHCL expected one implicit hkRootLevelContainer ITEM at DATA+0, found {array.Length}.");
		}
		return array[0];
	}

	private static void EnsureNoExternalPatches(NativeBphclDocument document)
	{
		NativeBphclSection nativeBphclSection = document.IndexSection?.Children.FirstOrDefault((NativeBphclSection section) => section.Signature == "PTCH");
		if (nativeBphclSection == null)
		{
			throw new InvalidDataException("BPHCL INDX has no PTCH section.");
		}
		int num = nativeBphclSection.PayloadOffset;
		int num2 = checked(nativeBphclSection.Offset + nativeBphclSection.Size);
		while (num + 4 <= num2)
		{
			uint num3 = BinaryPrimitives.ReadUInt32LittleEndian(document.Bytes.AsSpan(num, 4));
			num += 4;
			if (num3 == 0)
			{
				if (num != num2)
				{
					throw new NotSupportedException("BPHCL compaction does not yet support external PTCH records.");
				}
				return;
			}
			if (num + 4 > num2)
			{
				throw new InvalidDataException("Truncated BPHCL PTCH count.");
			}
			uint num4 = BinaryPrimitives.ReadUInt32LittleEndian(document.Bytes.AsSpan(num, 4));
			num = checked(num + 4 + (int)num4 * 4);
			if (num > num2)
			{
				throw new InvalidDataException("BPHCL PTCH entry exceeds its section.");
			}
		}
		if (num != num2)
		{
			throw new InvalidDataException("BPHCL PTCH ends in a truncated patch group.");
		}
	}

	private static void Align(List<byte> data, int alignment)
	{
		while (data.Count % alignment != 0)
		{
			data.Add(0);
		}
	}


}
