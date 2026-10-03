using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace HKCLTool;

internal static class NativeBphclGameReadyMerge
{
	private readonly record struct ImportedRange(uint OldStart, uint OldEnd, uint NewStart);

	private sealed class MutablePatchGroup
	{
		public uint TypeIndex { get; }

		public List<uint> Offsets { get; }

		public MutablePatchGroup(uint typeIndex, List<uint> offsets)
		{
			TypeIndex = typeIndex;
			Offsets = offsets;
		}
	}

	public static byte[] PruneUnreferencedColliders(NativeBphclDocument target)
	{
		NativeBphclSection nativeBphclSection = target.DataSection ?? throw new InvalidDataException("Target BPHCL has no DATA section.");
		NativeBphclNamedVariant nativeBphclNamedVariant = target.FindRootVariants("hclClothContainer").SingleOrDefault() ?? throw new InvalidDataException("Target BPHCL has no hclClothContainer root variant.");
		HashSet<int> referencedItems = target.Cloths.SelectMany((NativeBphclCloth cloth) => cloth.SimCloths).SelectMany((NativeBphclSimCloth simulation) => simulation.CollidableItemIndices).ToHashSet();
		NativeBphclCollider[] array = target.Colliders.Where((NativeBphclCollider collider) => referencedItems.Contains(collider.ItemIndex)).ToArray();
		if (array.Length == target.Colliders.Count)
		{
			return target.Bytes;
		}
		byte[] replacementAamp = NativeAampClothMerger.KeepColliderEntries(target, array.Select((NativeBphclCollider collider) => collider.Name));
		List<byte> list = target.Bytes.AsSpan(nativeBphclSection.PayloadOffset, nativeBphclSection.PayloadSize).ToArray().ToList();
		List<NativeBphclItem> items = target.Items.ToList();
		List<MutablePatchGroup> list2 = CreateMutablePatchGroups(target.InternalPatches);
		NativeBphclReferenceArray referenceArray = target.GetReferenceArray(nativeBphclNamedVariant.ObjectDataOffset + 24);
		ReplaceReferenceArray(target, list, items, list2, referenceArray, array.Select((NativeBphclCollider collider) => collider.ItemIndex));
		return NativeBphclTagFileBuilder.Rebuild(target, CollectionsMarshal.AsSpan(list), items, list2.Select((MutablePatchGroup group) => new NativeBphclPatch(group.TypeIndex, group.Offsets)).ToArray(), null, replacementAamp);
	}

	public static byte[] DeleteCloth(NativeBphclDocument target, int clothIndex)
	{
		if ((uint)clothIndex >= (uint)target.Cloths.Count)
		{
			throw new ArgumentOutOfRangeException("clothIndex");
		}
		NativeBphclCloth nativeBphclCloth = target.Cloths[clothIndex];
		byte[] replacementAamp = target.Aamp.RemoveClothEntry(target.Bytes, nativeBphclCloth.Name);
		NativeBphclSection nativeBphclSection = target.DataSection ?? throw new InvalidDataException("Target BPHCL has no DATA section.");
		List<byte> list = target.Bytes.AsSpan(nativeBphclSection.PayloadOffset, nativeBphclSection.PayloadSize).ToArray().ToList();
		List<NativeBphclItem> items = target.Items.ToList();
		List<MutablePatchGroup> list2 = CreateMutablePatchGroups(target.InternalPatches);
		NativeBphclNamedVariant nativeBphclNamedVariant = target.FindRootVariants("hclClothContainer").SingleOrDefault() ?? throw new InvalidDataException("Target BPHCL has no hclClothContainer root variant.");
		NativeBphclReferenceArray referenceArray = target.GetReferenceArray(nativeBphclNamedVariant.ObjectDataOffset + 40);
		List<int> list3 = target.GetReferenceItemIndices(referenceArray.FieldOffset).ToList();
		list3.RemoveAt(clothIndex);
		ReplaceReferenceArray(target, list, items, list2, referenceArray, list3);
		NativeBphclNamedVariant nativeBphclNamedVariant2 = target.FindRootVariants("hkaAnimationContainer").SingleOrDefault();
		if ((object)nativeBphclNamedVariant2 != null)
		{
			NativeBphclReferenceArray referenceArray2 = target.GetReferenceArray(nativeBphclNamedVariant2.ObjectDataOffset + 24);
			List<int> list4 = target.GetReferenceItemIndices(referenceArray2.FieldOffset).ToList();
			if (clothIndex < list4.Count)
			{
				list4.RemoveAt(clothIndex);
				ReplaceReferenceArray(target, list, items, list2, referenceArray2, list4);
			}
		}
		return PruneUnreferencedColliders(NativeBphclDocument.Parse(NativeBphclTagFileBuilder.Rebuild(target, CollectionsMarshal.AsSpan(list), items, list2.Select((MutablePatchGroup group) => new NativeBphclPatch(group.TypeIndex, group.Offsets)).ToArray(), null, replacementAamp)));
	}

	public static byte[] MergeCompleteCloth(NativeBphclDocument target, NativeBphclDocument source, int sourceClothIndex)
	{
		return ImportCompleteCloth(target, source, sourceClothIndex, null, reuseCompatibleColliders: true);
	}

	public static byte[] DuplicateCompleteCloth(NativeBphclDocument target, NativeBphclDocument source, int sourceClothIndex)
	{
		return ImportCompleteCloth(target, source, sourceClothIndex, null, reuseCompatibleColliders: false);
	}

	public static byte[] ReplaceCompleteCloth(NativeBphclDocument target, NativeBphclDocument source, int targetClothIndex, int sourceClothIndex)
	{
		return ImportCompleteCloth(target, source, sourceClothIndex, targetClothIndex, reuseCompatibleColliders: true);
	}

	private static byte[] ImportCompleteCloth(NativeBphclDocument target, NativeBphclDocument source, int sourceClothIndex, int? replaceTargetClothIndex, bool reuseCompatibleColliders)
	{
		if ((uint)sourceClothIndex >= (uint)source.Cloths.Count)
		{
			throw new ArgumentOutOfRangeException("sourceClothIndex");
		}
		if ((uint)sourceClothIndex >= (uint)source.Skeletons.Count)
		{
			throw new InvalidDataException("The imported BPHCL does not have a skeleton at the matching cloth index.");
		}
		source = NativeBphclWriter.RetargetSourceSkeletonNamespaces(target, source, sourceClothIndex);
		NativeBphclCloth sourceCloth = source.Cloths[sourceClothIndex];
		if (replaceTargetClothIndex.HasValue)
		{
			int valueOrDefault = replaceTargetClothIndex.GetValueOrDefault();
			if ((uint)valueOrDefault >= (uint)target.Cloths.Count)
			{
				throw new ArgumentOutOfRangeException("replaceTargetClothIndex");
			}
			if (!string.Equals(target.Cloths[valueOrDefault].Name, sourceCloth.Name, StringComparison.Ordinal))
			{
				throw new InvalidDataException("A native BPHCL replacement must keep the same cloth mesh name. Use a retargeting conversion for a differently named cloth.");
			}
		}
		else if (target.Cloths.Any((NativeBphclCloth cloth) => string.Equals(cloth.Name, sourceCloth.Name, StringComparison.Ordinal)))
		{
			throw new InvalidDataException("The target already contains a cloth named '" + sourceCloth.Name + "'. Remove or rename that target cloth before importing another one with the same mesh name.");
		}
		NativeBphclSection nativeBphclSection = target.DataSection ?? throw new InvalidDataException("Target BPHCL has no DATA section.");
		IReadOnlyList<int> readOnlyList = source.CollectItemClosure(new int[2]
		{
			sourceCloth.ItemIndex,
			source.Skeletons[sourceClothIndex].ItemIndex
		});
		IReadOnlyList<NativeBphclPatch> patchesForItems = source.GetPatchesForItems(readOnlyList);
		byte[] targetAamp = ((!replaceTargetClothIndex.HasValue) ? NativeAampClothMerger.AppendClothEntry(target, source, sourceCloth.Name) : NativeAampClothMerger.GetOriginalArchive(target));
		uint[] requiredSourceTypes = readOnlyList.Select((int itemIndex) => source.GetItem(itemIndex).TypeIndex).Concat(patchesForItems.Select((NativeBphclPatch patch) => patch.TypeIndex)).Distinct()
			.ToArray();
		NativeBphclGameReadyTypeTable typeTable = NativeBphclGameReadyTypeTable.Create(target, source, requiredSourceTypes);
		IReadOnlyDictionary<uint, uint> sourceToMerged = typeTable.SourceToMerged;
		HashSet<int> liveSourceItems = readOnlyList.ToHashSet();
		IReadOnlyDictionary<int, int> readOnlyDictionary2;
		if (!reuseCompatibleColliders)
		{
			IReadOnlyDictionary<int, int> readOnlyDictionary = new Dictionary<int, int>();
			readOnlyDictionary2 = readOnlyDictionary;
		}
		else
		{
			readOnlyDictionary2 = FindReusableColliders(target, source, liveSourceItems);
		}
		IReadOnlyDictionary<int, int> reusedColliderItems = readOnlyDictionary2;
		targetAamp = NativeAampClothMerger.AppendColliderEntries(targetAamp, source, from collider in source.Colliders
			where liveSourceItems.Contains(collider.ItemIndex) && !reusedColliderItems.ContainsKey(collider.ItemIndex)
			select collider.Name);
		List<byte> list = target.Bytes.AsSpan(nativeBphclSection.PayloadOffset, nativeBphclSection.PayloadSize).ToArray().ToList();
		Dictionary<uint, ImportedRange> dictionary = CopySourceClosureData(source, readOnlyList, list);
		List<NativeBphclItem> list2 = target.Items.Select((NativeBphclItem item2) => RemapTargetItem(item2, typeTable.TargetToMerged)).ToList();
		Dictionary<int, int> importedItemMap = new Dictionary<int, int>();
		List<MutablePatchGroup> list3;
		NativeBphclNamedVariant nativeBphclNamedVariant;
		NativeBphclNamedVariant nativeBphclNamedVariant2;
		int[] array;
		checked
		{
			foreach (int item2 in readOnlyList)
			{
				NativeBphclItem item = source.GetItem(item2);
				uint num = RemapTypeIndex(item.TypeIndex, sourceToMerged);
				uint flags = (item.Flags & 0xFF000000u) | num;
				ImportedRange importedRange = dictionary[item.DataOffset];
				importedItemMap.Add(item2, list2.Count);
				list2.Add(new NativeBphclItem(flags, num, importedRange.NewStart + item.DataOffset - importedRange.OldStart, item.Count));
			}
			RemapImportedPointers(list, source, patchesForItems, dictionary.Values, importedItemMap, reusedColliderItems);
			list3 = CreateMutablePatchGroups(target.InternalPatches, typeTable.TargetToMerged);
			foreach (NativeBphclPatch item3 in patchesForItems)
			{
				uint typeIndex = RemapTypeIndex(item3.TypeIndex, sourceToMerged);
				foreach (uint offset in item3.Offsets)
				{
					ImportedRange importedRange2 = FindImportedRange(dictionary.Values, offset);
					AddPatch(list3, typeIndex, importedRange2.NewStart + offset - importedRange2.OldStart);
				}
			}
			nativeBphclNamedVariant = target.FindRootVariants("hclClothContainer").SingleOrDefault() ?? throw new InvalidDataException("Target BPHCL has no hclClothContainer root variant.");
			nativeBphclNamedVariant2 = target.FindRootVariants("hkaAnimationContainer").SingleOrDefault() ?? throw new InvalidDataException("Target BPHCL has no hkaAnimationContainer root variant.");
			array = (from collider in source.Colliders
				where liveSourceItems.Contains(collider.ItemIndex) && !reusedColliderItems.ContainsKey(collider.ItemIndex)
				select importedItemMap[collider.ItemIndex]).ToArray();
		}
		if (array.Length != 0)
		{
			AppendReferenceArray(target, list, list2, list3, target.GetReferenceArray(nativeBphclNamedVariant.ObjectDataOffset + 24), array);
		}
		NativeBphclReferenceArray referenceArray = target.GetReferenceArray(nativeBphclNamedVariant.ObjectDataOffset + 40);
		NativeBphclReferenceArray referenceArray2 = target.GetReferenceArray(nativeBphclNamedVariant2.ObjectDataOffset + 24);
		if (replaceTargetClothIndex.HasValue)
		{
			int valueOrDefault2 = replaceTargetClothIndex.GetValueOrDefault();
			List<int> list4 = target.GetReferenceItemIndices(referenceArray.FieldOffset).ToList();
			List<int> list5 = target.GetReferenceItemIndices(referenceArray2.FieldOffset).ToList();
			if (valueOrDefault2 >= list5.Count)
			{
				throw new InvalidDataException("The target BPHCL does not have a skeleton at the cloth slot being replaced.");
			}
			list4[valueOrDefault2] = importedItemMap[sourceCloth.ItemIndex];
			list5[valueOrDefault2] = importedItemMap[source.Skeletons[sourceClothIndex].ItemIndex];
			ReplaceReferenceArray(target, list, list2, list3, referenceArray, list4);
			ReplaceReferenceArray(target, list, list2, list3, referenceArray2, list5);
		}
		else
		{
			AppendReferenceArray(target, list, list2, list3, referenceArray, new int[1] { importedItemMap[sourceCloth.ItemIndex] });
			AppendReferenceArray(target, list, list2, list3, referenceArray2, new int[1] { importedItemMap[source.Skeletons[sourceClothIndex].ItemIndex] });
		}
		return NativeBphclTagFileBuilder.Rebuild(target, CollectionsMarshal.AsSpan(list), list2, list3.Select((MutablePatchGroup group) => new NativeBphclPatch(group.TypeIndex, group.Offsets)).ToArray(), typeTable.ReplacementSection, targetAamp);
	}

	private static NativeBphclItem RemapTargetItem(NativeBphclItem item, IReadOnlyDictionary<uint, uint> typeMap)
	{
		uint num = RemapTypeIndex(item.TypeIndex, typeMap);
		return new NativeBphclItem((item.Flags & 0xFF000000u) | num, num, item.DataOffset, item.Count);
	}

	private static uint RemapTypeIndex(uint sourceTypeIndex, IReadOnlyDictionary<uint, uint> typeMap)
	{
		if (!typeMap.TryGetValue(sourceTypeIndex, out var value))
		{
			throw new InvalidDataException($"No target BPHCL type mapping exists for source type {sourceTypeIndex}.");
		}
		return value;
	}

	private static void RemapImportedPointers(List<byte> data, NativeBphclDocument source, IReadOnlyList<NativeBphclPatch> sourcePatches, IEnumerable<ImportedRange> importedRanges, IReadOnlyDictionary<int, int> importedItemMap, IReadOnlyDictionary<int, int> reusedItemMap)
	{
		foreach (NativeBphclPatch sourcePatch in sourcePatches)
		{
			foreach (uint offset in sourcePatch.Offsets)
			{
				ImportedRange importedRange = FindImportedRange(importedRanges, offset);
				int num = checked((int)(importedRange.NewStart + offset - importedRange.OldStart));
				if (num < 0 || num > data.Count - 4)
				{
					throw new InvalidDataException("Imported BPHCL relocation offset exceeds DATA.");
				}
				NativeBphclSection nativeBphclSection = source.DataSection ?? throw new InvalidDataException("Source BPHCL has no DATA section.");
				uint num2 = BinaryPrimitives.ReadUInt32LittleEndian(source.Bytes.AsSpan(nativeBphclSection.PayloadOffset + checked((int)offset), 4));
				if (num2 >= (uint)source.Items.Count)
				{
					throw new InvalidDataException($"Selected BPHCL graph points at ITEM {num2}, which was not included in the import closure.");
				}
				checked
				{
					int key = (int)num2;
					if (!reusedItemMap.TryGetValue(key, out var value) && !importedItemMap.TryGetValue(key, out value))
					{
						throw new InvalidDataException($"Selected BPHCL graph points at ITEM {num2}, which was not included in the import closure.");
					}
					BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(data).Slice(num, 4), (uint)value);
				}
			}
		}
	}

	private static Dictionary<uint, ImportedRange> CopySourceClosureData(NativeBphclDocument source, IEnumerable<int> sourceItemIndices, List<byte> destination)
	{
		NativeBphclSection nativeBphclSection = source.DataSection ?? throw new InvalidDataException("Source BPHCL has no DATA section.");
		Dictionary<int, (uint Start, uint End)> ranges = source.GetItemRanges();
		Dictionary<uint, ImportedRange> dictionary = new Dictionary<uint, ImportedRange>();
		foreach (int item in sourceItemIndices.OrderBy((int index) => ranges[index].Start))
		{
			(uint, uint) tuple = ranges[item];
			if (!dictionary.ContainsKey(tuple.Item1))
			{
				if (tuple.Item2 < tuple.Item1 || tuple.Item2 > nativeBphclSection.PayloadSize)
				{
					throw new InvalidDataException("Selected BPHCL graph exceeds the donor DATA section.");
				}
				// The proven August replacement layout aligns copied DATA ranges to 8 bytes.
				// Using reflected TYPE alignment here produces readable but static cloths.
				Align(destination, 8);
				uint newStart = checked((uint)destination.Count);
				destination.AddRange(source.Bytes.AsSpan(nativeBphclSection.PayloadOffset + checked((int)tuple.Item1), checked((int)(tuple.Item2 - tuple.Item1))).ToArray());
				dictionary.Add(tuple.Item1, new ImportedRange(tuple.Item1, tuple.Item2, newStart));
			}
		}
		return dictionary;
	}

	private static ImportedRange FindImportedRange(IEnumerable<ImportedRange> ranges, uint offset)
	{
		foreach (ImportedRange range in ranges)
		{
			if (offset >= range.OldStart && offset < range.OldEnd)
			{
				return range;
			}
		}
		throw new InvalidDataException($"BPHCL pointer at DATA+0x{offset:X} is outside the selected import graph.");
	}

	private static IReadOnlyDictionary<int, int> FindReusableColliders(NativeBphclDocument target, NativeBphclDocument source, IReadOnlySet<int> liveSourceItems)
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		HashSet<int> claimedTargetItems = new HashSet<int>();
		foreach (NativeBphclCollider sourceCollider in source.Colliders.Where((NativeBphclCollider collider) => liveSourceItems.Contains(collider.ItemIndex)))
		{
			NativeBphclCollider[] array = target.Colliders.Where((NativeBphclCollider targetCollider) => !claimedTargetItems.Contains(targetCollider.ItemIndex) && CollidersMatch(sourceCollider, targetCollider)).ToArray();
			if (array.Length == 1)
			{
				dictionary.Add(sourceCollider.ItemIndex, array[0].ItemIndex);
				claimedTargetItems.Add(array[0].ItemIndex);
			}
		}
		return dictionary;
	}

	private static bool CollidersMatch(NativeBphclCollider source, NativeBphclCollider target)
	{
		if (string.Equals(source.Name, target.Name, StringComparison.Ordinal) && string.Equals(source.Shape.TypeName, target.Shape.TypeName, StringComparison.Ordinal) && source.Shape.Kind == target.Shape.Kind && source.PinchDetectionEnabled == target.PinchDetectionEnabled && source.PinchDetectionPriority == target.PinchDetectionPriority && source.Enabled == target.Enabled && ApproximatelyEqual(source.PinchDetectionRadius, target.PinchDetectionRadius) && ApproximatelyEqual(source.Translation.X, target.Translation.X) && ApproximatelyEqual(source.Translation.Y, target.Translation.Y))
		{
			return ApproximatelyEqual(source.Translation.Z, target.Translation.Z);
		}
		return false;
	}

	private static bool ApproximatelyEqual(float left, float right)
	{
		return MathF.Abs(left - right) <= 1E-05f;
	}

	private static List<MutablePatchGroup> CreateMutablePatchGroups(IReadOnlyList<NativeBphclPatch> source, IReadOnlyDictionary<uint, uint>? typeMap = null)
	{
		return source.Select((NativeBphclPatch patch) => new MutablePatchGroup((typeMap == null) ? patch.TypeIndex : RemapTypeIndex(patch.TypeIndex, typeMap), patch.Offsets.ToList())).ToList();
	}

	private static void AddPatch(List<MutablePatchGroup> groups, uint typeIndex, uint offset)
	{
		MutablePatchGroup mutablePatchGroup = groups.FirstOrDefault((MutablePatchGroup existing) => existing.TypeIndex == typeIndex);
		if (mutablePatchGroup == null)
		{
			mutablePatchGroup = new MutablePatchGroup(typeIndex, new List<uint>());
			groups.Add(mutablePatchGroup);
		}
		mutablePatchGroup.Offsets.Add(offset);
	}

	private static void AppendReferenceArray(NativeBphclDocument target, List<byte> data, List<NativeBphclItem> items, List<MutablePatchGroup> patches, NativeBphclReferenceArray original, IEnumerable<int> addedItemIndices)
	{
		int[] array = addedItemIndices.ToArray();
		if (array.Length != 0)
		{
			ReplaceReferenceArray(target, data, items, patches, original, target.GetReferenceItemIndices(original.FieldOffset).Concat(array));
		}
	}

	private static void ReplaceReferenceArray(NativeBphclDocument target, List<byte> data, List<NativeBphclItem> items, List<MutablePatchGroup> patches, NativeBphclReferenceArray original, IEnumerable<int> itemIndices)
	{
		int[] array = itemIndices.ToArray();
		if (array.Length > 268435455)
		{
			throw new InvalidDataException("BPHCL reference array is too large to write.");
		}
		Align(data, 8);
		checked
		{
			uint num = (uint)data.Count;
			int[] array2 = array;
			foreach (int num2 in array2)
			{
				if (num2 < 0)
				{
					throw new InvalidDataException("BPHCL ITEM index cannot be represented in a pointer field.");
				}
				AppendUInt32(data, (uint)num2);
				AppendUInt32(data, 0u);
			}
			int count = items.Count;
			NativeBphclItem nativeBphclItem = items[original.StorageItemIndex];
			items.Add(new NativeBphclItem(nativeBphclItem.Flags, nativeBphclItem.TypeIndex, num, (uint)array.Length));
			WriteUInt32(data, (int)original.FieldOffset, (uint)count);
			WriteUInt32(data, (int)original.FieldOffset + 8, (uint)array.Length);
			uint num3 = ReadUInt32(data, (int)original.FieldOffset + 12);
			WriteUInt32(data, (int)original.FieldOffset + 12, (num3 & 0xC0000000u) | (uint)array.Length);
			for (int j = 0; j < array.Length; j = unchecked(j + 1))
			{
				AddPatch(patches, original.EntryPatchTypeIndex, num + (uint)(j * 8));
			}
		}
	}

	private static void Align(List<byte> data, int alignment)
	{
		while (data.Count % alignment != 0)
		{
			data.Add(0);
		}
	}

	private static uint ReadUInt32(List<byte> bytes, int offset)
	{
		return BinaryPrimitives.ReadUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, 4));
	}

	private static void WriteUInt32(List<byte> bytes, int offset, uint value)
	{
		BinaryPrimitives.WriteUInt32LittleEndian(CollectionsMarshal.AsSpan(bytes).Slice(offset, 4), value);
	}

	private static void AppendUInt32(List<byte> bytes, uint value)
	{
		bytes.Add((byte)value);
		bytes.Add((byte)(value >> 8));
		bytes.Add((byte)(value >> 16));
		bytes.Add((byte)(value >> 24));
	}
}

