using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HKCLTool;

internal sealed class NativeBphclGameReadyTypeTable
{
	private sealed record TypeTable(NativeBphclSection Section, IReadOnlyList<string> TypeStrings, IReadOnlyList<string> FieldStrings, IReadOnlyList<NamedType> NamedTypes, IReadOnlyList<TypeBody> Bodies, IReadOnlyList<TypeHash> Hashes, byte[] OriginalBytes)
	{
		public string? GetTypeName(uint typeIndex)
		{
			if (typeIndex == 0 || typeIndex > NamedTypes.Count)
			{
				return null;
			}
			return TypeStrings[checked((int)NamedTypes[(int)typeIndex - 1].StringIndex)];
		}

		public IReadOnlyList<string> GetTypeNames()
		{
			return Enumerable.Range(0, NamedTypes.Count + 1).Select(delegate(int index)
			{
				string text;
				if (index != 0)
				{
					text = GetTypeName((uint)index);
					if (text == null)
					{
						return string.Empty;
					}
				}
				else
				{
					text = string.Empty;
				}
				return text;
			}).ToArray();
		}

		public IReadOnlyList<byte[]> BuildSubsetChildren(IReadOnlyList<string> typeStrings, IReadOnlyList<string> fieldStrings, IReadOnlyList<NamedType> namedTypes, IReadOnlyList<TypeBody> bodies, IReadOnlyList<TypeHash> hashes)
		{
			List<byte[]> list = new List<byte[]>();
			foreach (NativeBphclSection child in Section.Children)
			{
				byte[] array;
				switch (child.Signature)
				{
				case "TPTR":
					array = new byte[checked((namedTypes.Count + 1) * 8)];
					break;
				case "TSTR":
				case "TST1":
					array = WriteStrings(typeStrings);
					break;
				case "FSTR":
				case "FST1":
					array = WriteStrings(fieldStrings);
					break;
				case "TNAM":
				case "TNA1":
					array = WriteNamedTypes(namedTypes);
					break;
				case "TBDY":
				case "TBOD":
					array = WriteBodies(bodies);
					break;
				case "THSH":
					array = WriteHashes(hashes);
					break;
				default:
					array = OriginalBytes.AsSpan(child.PayloadOffset, child.PayloadSize).ToArray();
					break;
				}
				byte[] array2 = array;
				list.Add(BuildSection(child.Signature, child.ChunkKind, array2));
			}
			return list;
		}

		public IReadOnlyList<byte[]> BuildChildren(IReadOnlyList<string> typeStrings, IReadOnlyList<string> fieldStrings, IReadOnlyList<NamedType> namedTypes, IReadOnlyList<TypeBody> bodies, IReadOnlyList<TypeHash> hashes)
		{
			List<byte[]> list = new List<byte[]>();
			int addedTypeCount = checked(namedTypes.Count - NamedTypes.Count);
			foreach (NativeBphclSection child in Section.Children)
			{
				byte[] array;
				switch (child.Signature)
				{
				case "TPTR":
					array = ExpandPointerTable(OriginalBytes.AsSpan(child.PayloadOffset, child.PayloadSize), addedTypeCount);
					break;
				case "TSTR":
				case "TST1":
					array = WriteStrings(typeStrings);
					break;
				case "FSTR":
				case "FST1":
					array = WriteStrings(fieldStrings);
					break;
				case "TNAM":
				case "TNA1":
					array = WriteNamedTypes(namedTypes);
					break;
				case "TBDY":
				case "TBOD":
					array = WriteBodies(bodies);
					break;
				case "THSH":
					array = WriteHashes(hashes);
					break;
				default:
					array = OriginalBytes.AsSpan(child.PayloadOffset, child.PayloadSize).ToArray();
					break;
				}
				byte[] array2 = array;
				list.Add(BuildSection(child.Signature, child.ChunkKind, array2));
			}
			return list;
		}

		private static byte[] ExpandPointerTable(ReadOnlySpan<byte> original, int addedTypeCount)
		{
			if (addedTypeCount <= 0)
			{
				return original.ToArray();
			}
			byte[] array = new byte[checked(original.Length + addedTypeCount * 8)];
			original.CopyTo(array);
			return array;
		}
	}

	private sealed class StringTable
	{
		private readonly List<string> _values;

		private readonly Dictionary<string, uint> _indices;

		public IReadOnlyList<string> Values => _values;

		public IReadOnlyDictionary<uint, uint> IdentityMap => Enumerable.Range(0, _values.Count).ToDictionary((int index) => (uint)index, (int index) => (uint)index);

		public StringTable(IReadOnlyList<string> values)
		{
			_values = values.ToList();
			_indices = new Dictionary<string, uint>(StringComparer.Ordinal);
			for (int i = 0; i < _values.Count; i++)
			{
				_indices.TryAdd(_values[i], (uint)i);
			}
		}

		public IReadOnlyDictionary<uint, uint> BuildMap(IReadOnlyList<string> source)
		{
			Dictionary<uint, uint> dictionary = new Dictionary<uint, uint>();
			for (int i = 0; i < source.Count; i++)
			{
				if (!_indices.TryGetValue(source[i], out var value))
				{
					value = checked((uint)_values.Count);
					_values.Add(source[i]);
					_indices.Add(source[i], value);
				}
				dictionary[(uint)i] = value;
			}
			return dictionary;
		}
	}

	private sealed record NamedType(uint StringIndex, IReadOnlyList<TypeTemplate> Templates)
	{
		public NamedType Remap(
			IReadOnlyDictionary<uint, uint> types,
			IReadOnlyDictionary<uint, uint> strings,
			IReadOnlyList<string> sourceStrings)
		{
			return new NamedType(strings[StringIndex], Templates.Select((TypeTemplate template) => template.Remap(types, strings, sourceStrings)).ToArray());
		}
	}

	private sealed record TypeTemplate(uint StringIndex, uint TypeIndex)
	{
		public TypeTemplate Remap(
			IReadOnlyDictionary<uint, uint> types,
			IReadOnlyDictionary<uint, uint> strings,
			IReadOnlyList<string> sourceStrings)
		{
			string parameterName = sourceStrings[checked((int)StringIndex)];
			uint value = parameterName.StartsWith("t", StringComparison.Ordinal)
				? RemapTypeReference(TypeIndex, types)
				: TypeIndex;
			return new TypeTemplate(strings[StringIndex], value);
		}
	}

	private sealed record TypeMember(uint NameIndex, uint Flags, byte? Reserve, uint Offset, uint TypeIndex)
	{
		public TypeMember Remap(IReadOnlyDictionary<uint, uint> types, IReadOnlyDictionary<uint, uint> strings)
		{
			return new TypeMember(strings[NameIndex], Flags, Reserve, Offset, RemapTypeReference(TypeIndex, types));
		}
	}

	private sealed record TypeInterface(uint TypeIndex, uint Flags)
	{
		public TypeInterface Remap(IReadOnlyDictionary<uint, uint> types)
		{
			return new TypeInterface(RemapTypeReference(TypeIndex, types), Flags);
		}
	}

	private sealed record TypeBody(uint TypeIndex, uint ParentTypeIndex, uint Flags, uint? Format, uint? SubtypeIndex, uint? Version, uint? Size, uint? Alignment, uint? UnknownFlags, uint EncodedMemberCount, IReadOnlyList<TypeMember> Members, uint? InterfaceCount, IReadOnlyList<TypeInterface> Interfaces, uint? AttributeIndex)
	{
		public TypeBody Remap(IReadOnlyDictionary<uint, uint> types, IReadOnlyDictionary<uint, uint> strings)
		{
			uint typeIndex = RemapTypeReference(TypeIndex, types);
			uint parentTypeIndex = RemapTypeReference(ParentTypeIndex, types);
			uint flags = Flags;
			uint? format = Format;
			uint? subtypeIndex = SubtypeIndex;
			uint? subtypeIndex2;
			if (subtypeIndex.HasValue)
			{
				uint valueOrDefault = subtypeIndex.GetValueOrDefault();
				subtypeIndex2 = RemapTypeReference(valueOrDefault, types);
			}
			else
			{
				subtypeIndex2 = null;
			}
			return new TypeBody(typeIndex, parentTypeIndex, flags, format, subtypeIndex2, Version, Size, Alignment, UnknownFlags, EncodedMemberCount, Members.Select((TypeMember member) => member.Remap(types, strings)).ToArray(), InterfaceCount, Interfaces.Select((TypeInterface @interface) => @interface.Remap(types)).ToArray(), AttributeIndex);
		}
	}

	private sealed record TypeHash(uint TypeIndex, uint Hash)
	{
		public TypeHash Remap(IReadOnlyDictionary<uint, uint> types)
		{
			return new TypeHash(RemapTypeReference(TypeIndex, types), Hash);
		}
	}

	public IReadOnlyDictionary<uint, uint> TargetToMerged { get; }

	public IReadOnlyDictionary<uint, uint> SourceToMerged { get; }

	public byte[]? ReplacementSection { get; }

	private NativeBphclGameReadyTypeTable(IReadOnlyDictionary<uint, uint> targetToMerged, IReadOnlyDictionary<uint, uint> sourceToMerged, byte[]? replacementSection)
	{
		TargetToMerged = targetToMerged;
		SourceToMerged = sourceToMerged;
		ReplacementSection = replacementSection;
	}

	public static NativeBphclGameReadyTypeTable Create(NativeBphclDocument target, NativeBphclDocument source, IEnumerable<uint>? requiredSourceTypes = null)
	{
		uint[] requiredSourceTypes2 = requiredSourceTypes?.Append(0u).Distinct().ToArray() ?? (from index in Enumerable.Range(0, source.TypeNames.Count)
			select (uint)index).ToArray();
		if (TryUseTargetTypeSection(target, source, requiredSourceTypes2, out NativeBphclGameReadyTypeTable table))
		{
			return table;
		}
		return CreateExperimentalUnion(Parse(target), Parse(source), requiredSourceTypes2);
	}

	public static IReadOnlyList<string> FindMissingRequiredTypes(NativeBphclDocument target, NativeBphclDocument source, IEnumerable<uint> requiredSourceTypes)
	{
		IReadOnlyDictionary<uint, string> readOnlyDictionary = BuildTypeKeys(Parse(target));
		IReadOnlyDictionary<uint, string> readOnlyDictionary2 = BuildTypeKeys(Parse(source));
		HashSet<string> hashSet = readOnlyDictionary.Values.ToHashSet<string>(StringComparer.Ordinal);
		HashSet<string> hashSet2 = target.TypeNames.Skip(1).ToHashSet<string>(StringComparer.Ordinal);
		List<string> list = new List<string>();
		foreach (uint item in requiredSourceTypes.Where((uint type) => type != 0).Distinct())
		{
			if (!readOnlyDictionary2.TryGetValue(item, out var value) || !hashSet.Contains(value))
			{
				string text = source.GetTypeName(item) ?? $"type {item}";
				list.Add(hashSet2.Contains(text) ? (text + " (definition differs)") : text);
			}
		}
		return list.Distinct<string>(StringComparer.Ordinal).OrderBy<string, string>((string name) => name, StringComparer.Ordinal).ToArray();
	}

	public static NativeBphclGameReadyTypeTable CreateEmbeddedCatalogUnion(NativeBphclDocument source, IEnumerable<uint> requiredSourceTypes)
	{
		uint[] requiredSourceTypes2 = requiredSourceTypes.Append(0u).Distinct().ToArray();
		return CreateExperimentalUnion(Parse(NativeBphclGameReadyReflectionCatalog.TypeSection), Parse(source), requiredSourceTypes2);
	}

	public static NativeBphclGameReadyTypeTable CreateCompactEmbeddedCatalog(
		IEnumerable<uint> requiredCatalogTypes,
		IEnumerable<uint>? concreteCatalogTypes = null)
	{
		return CreateCompactEmbeddedCatalogCore(requiredCatalogTypes, concreteCatalogTypes);
	}

	private static NativeBphclGameReadyTypeTable CreateCompactEmbeddedCatalogCore(
		IEnumerable<uint> requiredCatalogTypes,
		IEnumerable<uint>? concreteCatalogTypes)
	{
		TypeTable catalog = Parse(NativeBphclGameReadyReflectionCatalog.TypeSection);
		uint[] roots = requiredCatalogTypes.Append(0u).Distinct().ToArray();
		uint[] concreteRoots = (concreteCatalogTypes ?? requiredCatalogTypes).Append(0u).Distinct().ToArray();
		IReadOnlyDictionary<uint, uint> canonicalTypes = BuildBlendAliases(catalog, concreteRoots);
		uint[] expanded = ExpandTypeDependencies(catalog, roots, canonicalTypes).ToArray();
		HashSet<uint> requiredSet = expanded.ToHashSet();
		Dictionary<uint, uint> compactOrder = expanded.ToDictionary(index => index, index => index);

		// Bodyless blend aliases precede the concrete reflected blocks in Havok's
		// catalog. Keep active packed blends in that early region because this
		// runtime path consumes byte-sized type ids.
		foreach ((uint alias, uint canonical) in canonicalTypes)
		{
			if (!requiredSet.Contains(canonical))
				continue;

			string blendName = catalog.GetTypeName(canonical);
			uint earliestNamedSlot = Enumerable.Range(1, catalog.NamedTypes.Count)
				.Select(index => checked((uint)index))
				.Where(index => string.Equals(catalog.GetTypeName(index), blendName, StringComparison.Ordinal))
				.DefaultIfEmpty(canonical)
				.Min();
			compactOrder[canonical] = Math.Min(compactOrder[canonical], Math.Min(alias, earliestNamedSlot));
		}

		HashSet<uint> reflectedTypes = catalog.Bodies.Select(body => body.TypeIndex).ToHashSet();
		uint[] activeBlendTypes = expanded
			.Where(reflectedTypes.Contains)
			.Where(index =>
			{
				string typeName = catalog.GetTypeName(index);
				return !string.IsNullOrEmpty(typeName) &&
					typeName.Contains("Deformer::", StringComparison.Ordinal) &&
					typeName.EndsWith("BlendEntryBlock", StringComparison.Ordinal);
			})
			.ToArray();
		foreach (uint concrete in activeBlendTypes)
		{
			string typeName = catalog.GetTypeName(concrete);
			uint earliestNamedSlot = Enumerable.Range(1, catalog.NamedTypes.Count)
				.Select(index => checked((uint)index))
				.Where(index => string.Equals(catalog.GetTypeName(index), typeName, StringComparison.Ordinal))
				.DefaultIfEmpty(concrete)
				.Min();
			compactOrder[concrete] = Math.Min(Math.Min(compactOrder[concrete], earliestNamedSlot), 192u);
		}

		// Havok follows the blend block's member descriptors while unpacking the
		// skin. Keep that complete dependency family in the byte-addressable TYPE
		// range too, not only the outer BlendEntryBlock body.
		HashSet<uint> activeBlendFamily = ExpandTypeDependencies(catalog, activeBlendTypes, canonicalTypes).ToHashSet();
		uint[] array = expanded
			.OrderBy(index => activeBlendTypes.Contains(index) ? 0 : activeBlendFamily.Contains(index) ? 1 : 2)
			.ThenBy(index => compactOrder[index])
			.ThenBy(index => index)
			.ToArray();
		Dictionary<uint, uint> remap = new Dictionary<uint, uint> { [0u] = 0u };
		for (int num = 0; num < array.Length; num++)
		{
			remap[array[num]] = checked((uint)num + 1);
		}
		foreach (KeyValuePair<uint, uint> canonicalType in canonicalTypes)
		{
			if (remap.TryGetValue(canonicalType.Value, out uint mappedType))
			{
				remap[canonicalType.Key] = mappedType;
			}
		}
		NamedType[] source = array.Select((uint index) => catalog.NamedTypes[checked((int)index - 1)]).ToArray();
		TypeBody[] source2 = catalog.Bodies.Where((TypeBody body) => remap.ContainsKey(body.TypeIndex)).ToArray();
		(IReadOnlyList<string>, IReadOnlyDictionary<uint, uint>) tuple = BuildCompactStringTable(catalog.TypeStrings, source.SelectMany((NamedType named) => new uint[1] { named.StringIndex }.Concat(named.Templates.Select((TypeTemplate template) => template.StringIndex))));
		IReadOnlyList<string> item = tuple.Item1;
		IReadOnlyDictionary<uint, uint> typeStringMap = tuple.Item2;
		tuple = BuildCompactStringTable(catalog.FieldStrings, source2.SelectMany((TypeBody body) => body.Members.Select((TypeMember member) => member.NameIndex)));
		IReadOnlyList<string> item2 = tuple.Item1;
		IReadOnlyDictionary<uint, uint> fieldStringMap = tuple.Item2;
		NamedType[] namedTypes = source.Select((NamedType named) => named.Remap(remap, typeStringMap, catalog.TypeStrings)).ToArray();
		TypeBody[] bodies = source2.Select((TypeBody body) => body.Remap(remap, fieldStringMap)).ToArray();
		List<TypeHash> hashes = (from hash in catalog.Hashes
			where remap.ContainsKey(hash.TypeIndex)
			select hash.Remap(remap)).ToList();
		Dictionary<string, uint> knownObjectBlendArrayHashes = new Dictionary<string, uint>(StringComparer.Ordinal)
		{
			["hclObjectSpaceDeformer::FourBlendEntryBlock"] = 0xCF50639E,
			["hclObjectSpaceDeformer::ThreeBlendEntryBlock"] = 0xA234E80F,
			["hclObjectSpaceDeformer::TwoBlendEntryBlock"] = 0x72714023,
			["hclObjectSpaceDeformer::OneBlendEntryBlock"] = 0xCDDEE230
		};
		foreach (uint sourceType in array)
		{
			if (!string.Equals(catalog.GetTypeName(sourceType), "hkArray", StringComparison.Ordinal))
			{
				continue;
			}
			NamedType named = catalog.NamedTypes[checked((int)sourceType - 1)];
			TypeTemplate? elementTemplate = named.Templates.FirstOrDefault((TypeTemplate template) => template.StringIndex < catalog.TypeStrings.Count && string.Equals(catalog.TypeStrings[checked((int)template.StringIndex)], "tT", StringComparison.Ordinal));
			if (elementTemplate == null || !knownObjectBlendArrayHashes.TryGetValue(catalog.GetTypeName(elementTemplate.TypeIndex), out uint expectedHash))
			{
				continue;
			}
			uint mappedType = remap[sourceType];
			hashes.RemoveAll((TypeHash hash) => hash.TypeIndex == mappedType);
			hashes.Add(new TypeHash(mappedType, expectedHash));
		}
		hashes.Sort((TypeHash left, TypeHash right) => left.TypeIndex.CompareTo(right.TypeIndex));
		byte[] replacementSection = BuildSection(catalog.Section, catalog.BuildSubsetChildren(item, item2, namedTypes, bodies, hashes.ToArray()));
		return new NativeBphclGameReadyTypeTable(remap, remap, replacementSection);
	}

	private static NativeBphclGameReadyTypeTable CreateExperimentalUnion(TypeTable targetTable, TypeTable sourceTable, IReadOnlyCollection<uint> requiredSourceTypes)
	{
		IReadOnlyList<uint> source = ExpandTypeDependencies(sourceTable, requiredSourceTypes);
		IReadOnlyDictionary<uint, string> readOnlyDictionary = BuildTypeKeys(targetTable);
		IReadOnlyDictionary<uint, string> readOnlyDictionary2 = BuildTypeKeys(sourceTable);
		Dictionary<string, uint> dictionary = new Dictionary<string, uint>(StringComparer.Ordinal);
		foreach (KeyValuePair<uint, string> item in readOnlyDictionary)
		{
			dictionary.TryAdd(item.Value, item.Key);
		}
		Dictionary<uint, uint> dictionary2 = new Dictionary<uint, uint> { [0u] = 0u };
		List<uint> list = new List<uint>();
		uint num = checked((uint)targetTable.NamedTypes.Count + 1);
		foreach (uint sourceIndex in from index in source
			where index != 0
			orderby index
			select index)
		{
			if (readOnlyDictionary2.TryGetValue(sourceIndex, out var value) && dictionary.TryGetValue(value, out var value2))
			{
				dictionary2[sourceIndex] = value2;
				continue;
			}
			if (!sourceTable.Bodies.Any((TypeBody body) => body.TypeIndex == sourceIndex))
			{
				string sourceName = sourceTable.GetTypeName(sourceIndex);
				uint[] array = (from entry in targetTable.GetTypeNames().Select((string name, int index) => (name: name, index: index))
					where entry.index > 0 && string.Equals(entry.name, sourceName, StringComparison.Ordinal)
					select (uint)entry.index).ToArray();
				if (array.Length == 1)
				{
					dictionary2[sourceIndex] = array[0];
					continue;
				}
			}
			dictionary2[sourceIndex] = num++;
			list.Add(sourceIndex);
		}
		Dictionary<uint, uint> targetToMerged = Enumerable.Range(0, targetTable.NamedTypes.Count + 1).ToDictionary((int index) => (uint)index, (int index) => (uint)index);
		if (list.Count == 0)
		{
			return new NativeBphclGameReadyTypeTable(targetToMerged, dictionary2, null);
		}
		StringTable typeStrings = new StringTable(targetTable.TypeStrings);
		StringTable fieldStrings = new StringTable(targetTable.FieldStrings);
		List<NamedType> list2 = targetTable.NamedTypes.Select((NamedType named) => named.Remap(targetToMerged, typeStrings.IdentityMap, targetTable.TypeStrings)).ToList();
		List<TypeBody> list3 = targetTable.Bodies.Select((TypeBody body) => body.Remap(targetToMerged, fieldStrings.IdentityMap)).ToList();
		List<TypeHash> list4 = targetTable.Hashes.Select((TypeHash hash) => hash.Remap(targetToMerged)).ToList();
		IReadOnlyDictionary<uint, uint> strings = typeStrings.BuildMap(sourceTable.TypeStrings);
		IReadOnlyDictionary<uint, uint> strings2 = fieldStrings.BuildMap(sourceTable.FieldStrings);
		foreach (uint sourceTypeIndex in list)
		{
			NamedType namedType = sourceTable.NamedTypes.ElementAtOrDefault(checked((int)sourceTypeIndex - 1)) ?? throw new InvalidDataException($"Source BPHCL TYPE has no name record for type {sourceTypeIndex}.");
			TypeBody typeBody = sourceTable.Bodies.FirstOrDefault((TypeBody entry) => entry.TypeIndex == sourceTypeIndex) ?? throw new InvalidDataException($"Source BPHCL TYPE has no body record for type {sourceTypeIndex}.");
			list2.Add(namedType.Remap(dictionary2, strings, sourceTable.TypeStrings));
			list3.Add(typeBody.Remap(dictionary2, strings2));
			foreach (TypeHash item2 in sourceTable.Hashes.Where((TypeHash entry) => entry.TypeIndex == sourceTypeIndex))
			{
				list4.Add(item2.Remap(dictionary2));
			}
		}
		byte[] replacementSection = BuildSection(targetTable.Section, targetTable.BuildChildren(typeStrings.Values, fieldStrings.Values, list2, list3, list4));
		return new NativeBphclGameReadyTypeTable(targetToMerged, dictionary2, replacementSection);
	}

	private static (IReadOnlyList<string> Values, IReadOnlyDictionary<uint, uint> Map) BuildCompactStringTable(IReadOnlyList<string> source, IEnumerable<uint> requiredIndices)
	{
		List<string> list = new List<string>();
		Dictionary<uint, uint> dictionary = new Dictionary<uint, uint>();
		checked
		{
			foreach (uint item in from index in requiredIndices.Distinct()
				orderby index
				select index)
			{
				if (item >= source.Count)
				{
					throw new InvalidDataException("BPHCL TYPE record references a missing string index.");
				}
				dictionary[item] = (uint)list.Count;
				list.Add(source[(int)item]);
			}
			return (Values: list, Map: dictionary);
		}
	}

	private static IReadOnlyDictionary<uint, uint> BuildBlendAliases(TypeTable source, IReadOnlyCollection<uint> concreteRoots)
	{
		Dictionary<uint, uint> aliases = new Dictionary<uint, uint>();
		string[] blendTypes = new string[8]
		{
			"hclObjectSpaceDeformer::FourBlendEntryBlock",
			"hclObjectSpaceDeformer::ThreeBlendEntryBlock",
			"hclObjectSpaceDeformer::TwoBlendEntryBlock",
			"hclObjectSpaceDeformer::OneBlendEntryBlock",
			"hclBoneSpaceDeformer::FourBlendEntryBlock",
			"hclBoneSpaceDeformer::ThreeBlendEntryBlock",
			"hclBoneSpaceDeformer::TwoBlendEntryBlock",
			"hclBoneSpaceDeformer::OneBlendEntryBlock"
		};
		foreach (string blendType in blendTypes)
		{
			uint[] reflected = source.Bodies.Select((TypeBody body) => body.TypeIndex).Where((uint type) => string.Equals(source.GetTypeName(type), blendType, StringComparison.Ordinal)).Distinct().ToArray();
			if (reflected.Length != 1)
			{
				continue;
			}
			uint concrete = reflected[0];
			NamedType concreteType = source.NamedTypes[checked((int)concrete - 1)];
			uint bodyless = Enumerable.Range(1, source.NamedTypes.Count).Select((int index) => (uint)index).Where((uint typeIndex) => typeIndex != concrete && string.Equals(source.GetTypeName(typeIndex), blendType, StringComparison.Ordinal) && source.Bodies.All((TypeBody body) => body.TypeIndex != typeIndex)).FirstOrDefault(delegate(uint typeIndex)
			{
				NamedType aliasType = source.NamedTypes[checked((int)typeIndex - 1)];
				return aliasType.Templates.Count == concreteType.Templates.Count && aliasType.Templates.Zip(concreteType.Templates).All((ValueTuple<TypeTemplate, TypeTemplate> pair) => pair.Item1.StringIndex == pair.Item2.StringIndex && pair.Item1.TypeIndex == pair.Item2.TypeIndex);
			});
			if (bodyless == 0)
			{
				continue;
			}
			if (concreteRoots.Contains(concrete))
			{
				aliases[bodyless] = concrete;
			}
			else
			{
				aliases[concrete] = bodyless;
			}
		}
		return aliases;
	}

	private static IReadOnlyList<uint> ExpandTypeDependencies(TypeTable source, IEnumerable<uint> roots, IReadOnlyDictionary<uint, uint>? canonicalTypes = null)
	{
		HashSet<uint> required = new HashSet<uint>();
		Queue<uint> queue = new Queue<uint>();
		foreach (uint root in roots)
		{
			Enqueue(root);
		}
		while (true)
		{
			if (!queue.TryDequeue(out var typeIndex))
			{
				break;
			}
			if (!required.Add(typeIndex))
			{
				continue;
			}
			foreach (TypeTemplate template in source.NamedTypes[checked((int)typeIndex - 1)].Templates)
			{
				string parameterName = source.TypeStrings[checked((int)template.StringIndex)];
				if (parameterName.StartsWith("t", StringComparison.Ordinal))
				{
					Enqueue(template.TypeIndex);
				}
			}
			TypeBody typeBody = source.Bodies.FirstOrDefault((TypeBody entry) => entry.TypeIndex == typeIndex);
			if ((object)typeBody == null)
			{
				continue;
			}
			Enqueue(typeBody.ParentTypeIndex);
			uint? subtypeIndex = typeBody.SubtypeIndex;
			if (subtypeIndex.HasValue)
			{
				uint valueOrDefault = subtypeIndex.GetValueOrDefault();
				Enqueue(valueOrDefault);
			}
			foreach (TypeMember member in typeBody.Members)
			{
				Enqueue(member.TypeIndex);
			}
			foreach (TypeInterface @interface in typeBody.Interfaces)
			{
				Enqueue(@interface.TypeIndex);
			}
		}
		return required.OrderBy((uint type) => type).ToArray();
		void Enqueue(uint num)
		{
			if (canonicalTypes != null && canonicalTypes.TryGetValue(num, out uint canonical))
			{
				num = canonical;
			}
			if (num != 0 && num <= source.NamedTypes.Count && !required.Contains(num))
			{
				queue.Enqueue(num);
			}
		}
	}

	private static bool TryUseTargetTypeSection(NativeBphclDocument target, NativeBphclDocument source, IReadOnlyCollection<uint> requiredSourceTypes, out NativeBphclGameReadyTypeTable table)
	{
		IReadOnlyDictionary<uint, string> readOnlyDictionary = BuildTypeKeys(Parse(target));
		IReadOnlyDictionary<uint, string> readOnlyDictionary2 = BuildTypeKeys(Parse(source));
		Dictionary<string, uint> dictionary = new Dictionary<string, uint>(StringComparer.Ordinal);
		foreach (KeyValuePair<uint, string> item in readOnlyDictionary)
		{
			dictionary.TryAdd(item.Value, item.Key);
		}
		Dictionary<uint, uint> dictionary2 = new Dictionary<uint, uint> { [0u] = 0u };
		foreach (uint requiredSourceType in requiredSourceTypes)
		{
			if (requiredSourceType != 0)
			{
				if (!readOnlyDictionary2.TryGetValue(requiredSourceType, out var value) || !dictionary.TryGetValue(value, out var value2))
				{
					table = null;
					return false;
				}
				dictionary2[requiredSourceType] = value2;
			}
		}
		Dictionary<uint, uint> targetToMerged = Enumerable.Range(0, target.TypeNames.Count).ToDictionary((int index) => (uint)index, (int index) => (uint)index);
		table = new NativeBphclGameReadyTypeTable(targetToMerged, dictionary2, null);
		return true;
	}

	private static bool TryUseSourceTypeSection(NativeBphclDocument target, NativeBphclDocument source, out NativeBphclGameReadyTypeTable table)
	{
		TypeTable table2 = Parse(target);
		IReadOnlyDictionary<uint, string> readOnlyDictionary = BuildTypeKeys(Parse(source));
		IReadOnlyDictionary<uint, string> readOnlyDictionary2 = BuildTypeKeys(table2);
		Dictionary<string, Queue<uint>> dictionary = new Dictionary<string, Queue<uint>>(StringComparer.Ordinal);
		for (int i = 1; i < source.TypeNames.Count; i++)
		{
			string key = readOnlyDictionary[(uint)i];
			if (!dictionary.TryGetValue(key, out var value))
			{
				value = new Queue<uint>();
				dictionary.Add(key, value);
			}
			value.Enqueue((uint)i);
		}
		Dictionary<uint, uint> dictionary2 = new Dictionary<uint, uint> { [0u] = 0u };
		for (int j = 1; j < target.TypeNames.Count; j++)
		{
			string key2 = readOnlyDictionary2[(uint)j];
			if (!dictionary.TryGetValue(key2, out var value2) || value2.Count == 0)
			{
				table = null;
				return false;
			}
			dictionary2[(uint)j] = value2.Dequeue();
		}
		Dictionary<uint, uint> sourceToMerged = Enumerable.Range(0, source.TypeNames.Count).ToDictionary((int index) => (uint)index, (int index) => (uint)index);
		NativeBphclSection nativeBphclSection = source.TypeSection ?? throw new InvalidDataException("Source BPHCL has no TYPE section.");
		byte[] replacementSection = source.Bytes.AsSpan(nativeBphclSection.Offset, nativeBphclSection.Size).ToArray();
		table = new NativeBphclGameReadyTypeTable(dictionary2, sourceToMerged, replacementSection);
		return true;
	}

	private static IReadOnlyDictionary<uint, string> BuildTypeKeys(TypeTable table)
	{
		Dictionary<uint, string> keys = new Dictionary<uint, string>();
		HashSet<uint> active = new HashSet<uint>();
		for (uint num = 1u; num <= table.NamedTypes.Count; num++)
		{
			Describe(num);
		}
		return keys;
		string Describe(uint index)
		{
			if (index == 0)
			{
				return "void";
			}
			if (keys.TryGetValue(index, out string value))
			{
				return value;
			}
			if (index > table.NamedTypes.Count)
			{
				return $"external:{index}";
			}
			checked
			{
				NamedType namedType = table.NamedTypes[(int)index - 1];
				string text = ((namedType.StringIndex < table.TypeStrings.Count) ? table.TypeStrings[(int)namedType.StringIndex] : $"invalid-name:{namedType.StringIndex}");
				if (!active.Add(index))
				{
					return "cycle:" + text;
				}
				string text2 = string.Join(",", namedType.Templates.Select((TypeTemplate template) => ((template.StringIndex < table.TypeStrings.Count) ? table.TypeStrings[(int)template.StringIndex] : $"invalid-parameter:{template.StringIndex}") + "=" + Describe(template.TypeIndex)));
				active.Remove(index);
				TypeBody typeBody = table.Bodies.FirstOrDefault((TypeBody entry) => entry.TypeIndex == index);
				string text3 = ((text2.Length == 0) ? text : (text + "<" + text2 + ">"));
				if ((object)typeBody == null)
				{
					keys[index] = text3;
					return text3;
				}
				string[] obj = new string[11]
				{
					"parent=" + Describe(typeBody.ParentTypeIndex),
					$"flags={typeBody.Flags:X}",
					$"format={typeBody.Format}",
					null,
					null,
					null,
					null,
					null,
					null,
					null,
					null
				};
				uint? subtypeIndex = typeBody.SubtypeIndex;
				object obj2;
				if (subtypeIndex.HasValue)
				{
					uint valueOrDefault = subtypeIndex.GetValueOrDefault();
					obj2 = Describe(valueOrDefault);
				}
				else
				{
					obj2 = "-";
				}
				obj[3] = "subtype=" + (string?)obj2;
				obj[4] = $"version={typeBody.Version}";
				obj[5] = $"size={typeBody.Size}";
				obj[6] = $"alignment={typeBody.Alignment}";
				obj[7] = $"unknown={typeBody.UnknownFlags}";
				obj[8] = "members=[" + string.Join(",", typeBody.Members.Select(delegate(TypeMember member)
				{
					string value2 = ((member.NameIndex < table.FieldStrings.Count) ? table.FieldStrings[(int)member.NameIndex] : $"invalid-field:{member.NameIndex}");
					return $"{value2}@{member.Offset:X}:{Describe(member.TypeIndex)}:{member.Flags:X}:{member.Reserve}";
				})) + "]";
				obj[9] = "interfaces=[" + string.Join(",", typeBody.Interfaces.Select((TypeInterface @interface) => $"{Describe(@interface.TypeIndex)}:{@interface.Flags:X}")) + "]";
				obj[10] = $"attribute={typeBody.AttributeIndex}";
				string text4 = string.Join(";", obj);
				string text5 = text3 + "{" + text4 + "}";
				keys[index] = text5;
				return text5;
			}
		}
	}

	private static TypeTable Parse(NativeBphclDocument document)
	{
		return Parse(document.TypeSection ?? throw new InvalidDataException("BPHCL has no TYPE section."), document.Bytes);
	}

	private static TypeTable Parse(byte[] typeSection)
	{
		return Parse(NativeBphclTagFileReader.ReadStandaloneSection(typeSection, "TYPE"), typeSection);
	}

	private static TypeTable Parse(NativeBphclSection section, byte[] bytes)
	{
		IReadOnlyList<string> typeStrings = ReadStrings(section.Children.FirstOrDefault(delegate(NativeBphclSection child)
		{
			string signature = child.Signature;
			return (signature == "TST1" || signature == "TSTR") ? true : false;
		}), bytes);
		IReadOnlyList<string> fieldStrings = ReadStrings(section.Children.FirstOrDefault(delegate(NativeBphclSection child)
		{
			string signature = child.Signature;
			return (signature == "FST1" || signature == "FSTR") ? true : false;
		}), bytes);
		IReadOnlyList<NamedType> namedTypes = ReadNamedTypes(section.Children.FirstOrDefault(delegate(NativeBphclSection child)
		{
			string signature = child.Signature;
			return (signature == "TNA1" || signature == "TNAM") ? true : false;
		}), bytes);
		IReadOnlyList<TypeBody> bodies = ReadBodies(section.Children.FirstOrDefault(delegate(NativeBphclSection child)
		{
			string signature = child.Signature;
			return (signature == "TBDY" || signature == "TBOD") ? true : false;
		}), bytes);
		IReadOnlyList<TypeHash> hashes = ReadHashes(section.Children.FirstOrDefault((NativeBphclSection child) => child.Signature == "THSH"), bytes);
		return new TypeTable(section, typeStrings, fieldStrings, namedTypes, bodies, hashes, bytes);
	}

	private static uint RemapTypeReference(uint value, IReadOnlyDictionary<uint, uint> types)
	{
		if (!types.TryGetValue(value, out var value2))
		{
			return value;
		}
		return value2;
	}

	private static IReadOnlyList<string> ReadStrings(NativeBphclSection? section, byte[] bytes)
	{
		if (section == null)
		{
			throw new InvalidDataException("BPHCL TYPE is missing a required string table.");
		}
		List<string> list = new List<string>();
		int num = section.PayloadOffset;
		int num2 = section.Offset + section.Size;
		for (int i = num; i < num2; i++)
		{
			if (bytes[i] == 0)
			{
				list.Add(Encoding.UTF8.GetString(bytes, num, i - num));
				num = i + 1;
			}
		}
		if (num < num2)
		{
			list.Add(Encoding.UTF8.GetString(bytes, num, num2 - num));
		}
		return list;
	}

	private static IReadOnlyList<NamedType> ReadNamedTypes(NativeBphclSection? section, byte[] bytes)
	{
		if (section == null)
		{
			throw new InvalidDataException("BPHCL TYPE is missing TNA1.");
		}
		int cursor = section.PayloadOffset;
		int end = section.Offset + section.Size;
		int num = checked((int)ReadVarUInt(bytes, ref cursor, end));
		List<NamedType> list = new List<NamedType>(Math.Max(0, num - 1));
		for (int i = 1; i < num; i++)
		{
			checked
			{
				uint stringIndex = (uint)ReadVarUInt(bytes, ref cursor, end);
				int num2 = (int)ReadVarUInt(bytes, ref cursor, end);
				List<TypeTemplate> list2 = new List<TypeTemplate>(num2);
				for (int j = 0; j < num2; j = unchecked(j + 1))
				{
					list2.Add(new TypeTemplate((uint)ReadVarUInt(bytes, ref cursor, end), (uint)ReadVarUInt(bytes, ref cursor, end)));
				}
				list.Add(new NamedType(stringIndex, list2));
			}
		}
		return list;
	}

	private static IReadOnlyList<TypeBody> ReadBodies(NativeBphclSection? section, byte[] bytes)
	{
		if (section == null)
		{
			throw new InvalidDataException("BPHCL TYPE is missing TBDY.");
		}
		int cursor = section.PayloadOffset;
		int num = section.Offset + section.Size;
		List<TypeBody> list = new List<TypeBody>();
		checked
		{
			while (cursor < num)
			{
				uint num2 = (uint)ReadVarUInt(bytes, ref cursor, num);
				if (num2 == 0)
				{
					list.Add(new TypeBody(0u, 0u, 0u, null, null, null, null, null, null, 0u, Array.Empty<TypeMember>(), null, Array.Empty<TypeInterface>(), null));
					continue;
				}
				uint parentTypeIndex = (uint)ReadVarUInt(bytes, ref cursor, num);
				uint num3 = (uint)ReadVarUInt(bytes, ref cursor, num);
				uint? format = (((num3 & 1) != 0) ? new uint?((uint)ReadVarUInt(bytes, ref cursor, num)) : ((uint?)null));
				uint? subtypeIndex = (((num3 & 2) != 0) ? new uint?((uint)ReadVarUInt(bytes, ref cursor, num)) : ((uint?)null));
				uint? version = (((num3 & 4) != 0) ? new uint?((uint)ReadVarUInt(bytes, ref cursor, num)) : ((uint?)null));
				uint? size = null;
				uint? alignment = null;
				if ((num3 & 8) != 0)
				{
					size = (uint)ReadVarUInt(bytes, ref cursor, num);
					alignment = (uint)ReadVarUInt(bytes, ref cursor, num);
				}
				uint? unknownFlags = (((num3 & 0x10) != 0) ? new uint?((uint)ReadVarUInt(bytes, ref cursor, num)) : ((uint?)null));
				uint num4 = (((num3 & 0x20) != 0) ? ((uint)ReadVarUInt(bytes, ref cursor, num)) : 0u);
				List<TypeMember> list2 = new List<TypeMember>((int)(num4 & 0xFFFF));
				for (int i = 0; i < (num4 & 0xFFFF); i = unchecked(i + 1))
				{
					uint nameIndex = (uint)ReadVarUInt(bytes, ref cursor, num);
					uint num5 = (uint)ReadVarUInt(bytes, ref cursor, num);
					byte? reserve = null;
					if ((num5 & 0x80) != 0)
					{
						if (cursor >= num)
						{
							throw new InvalidDataException("Truncated BPHCL TYPE member padding.");
						}
						reserve = bytes[unchecked(cursor++)];
					}
					list2.Add(new TypeMember(nameIndex, num5, reserve, (uint)ReadVarUInt(bytes, ref cursor, num), (uint)ReadVarUInt(bytes, ref cursor, num)));
				}
				uint? num6 = null;
				List<TypeInterface> list3 = new List<TypeInterface>();
				if ((num3 & 0x40) != 0)
				{
					num6 = (uint)ReadVarUInt(bytes, ref cursor, num);
					for (int j = 0; j < num6; j = unchecked(j + 1))
					{
						list3.Add(new TypeInterface((uint)ReadVarUInt(bytes, ref cursor, num), (uint)ReadVarUInt(bytes, ref cursor, num)));
					}
				}
				uint? attributeIndex = (((num3 & 0x80) != 0) ? new uint?((uint)ReadVarUInt(bytes, ref cursor, num)) : ((uint?)null));
				list.Add(new TypeBody(num2, parentTypeIndex, num3, format, subtypeIndex, version, size, alignment, unknownFlags, num4, list2, num6, list3, attributeIndex));
			}
			return list;
		}
	}

	private static IReadOnlyList<TypeHash> ReadHashes(NativeBphclSection? section, byte[] bytes)
	{
		if (section == null)
		{
			return Array.Empty<TypeHash>();
		}
		int cursor = section.PayloadOffset;
		int num = section.Offset + section.Size;
		int num2 = checked((int)ReadVarUInt(bytes, ref cursor, num));
		List<TypeHash> list = new List<TypeHash>(num2);
		for (int i = 0; i < num2; i++)
		{
			uint typeIndex = checked((uint)ReadVarUInt(bytes, ref cursor, num));
			if (cursor > num - 4)
			{
				throw new InvalidDataException("Truncated BPHCL THSH entry.");
			}
			list.Add(new TypeHash(typeIndex, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor, 4))));
			cursor += 4;
		}
		return list;
	}

	private static byte[] WriteStrings(IReadOnlyList<string> strings)
	{
		using MemoryStream memoryStream = new MemoryStream();
		foreach (string @string in strings)
		{
			memoryStream.Write(Encoding.UTF8.GetBytes(@string));
			memoryStream.WriteByte(0);
		}
		return memoryStream.ToArray();
	}

	private static byte[] WriteNamedTypes(IReadOnlyList<NamedType> types)
	{
		checked
		{
			using MemoryStream memoryStream = new MemoryStream();
			WriteVarUInt(memoryStream, (uint)types.Count + 1);
			foreach (NamedType type in types)
			{
				WriteVarUInt(memoryStream, type.StringIndex);
				WriteVarUInt(memoryStream, (uint)type.Templates.Count);
				foreach (TypeTemplate template in type.Templates)
				{
					WriteVarUInt(memoryStream, template.StringIndex);
					WriteVarUInt(memoryStream, template.TypeIndex);
				}
			}
			return memoryStream.ToArray();
		}
	}

	private static byte[] WriteBodies(IReadOnlyList<TypeBody> bodies)
	{
		using MemoryStream memoryStream = new MemoryStream();
		foreach (TypeBody body in bodies)
		{
			WriteVarUInt(memoryStream, body.TypeIndex);
			if (body.TypeIndex == 0)
			{
				continue;
			}
			WriteVarUInt(memoryStream, body.ParentTypeIndex);
			WriteVarUInt(memoryStream, body.Flags);
			if ((body.Flags & 1) != 0)
			{
				WriteVarUInt(memoryStream, body.Format.Value);
			}
			if ((body.Flags & 2) != 0)
			{
				WriteVarUInt(memoryStream, body.SubtypeIndex.Value);
			}
			if ((body.Flags & 4) != 0)
			{
				WriteVarUInt(memoryStream, body.Version.Value);
			}
			if ((body.Flags & 8) != 0)
			{
				WriteVarUInt(memoryStream, body.Size.Value);
				WriteVarUInt(memoryStream, body.Alignment.Value);
			}
			if ((body.Flags & 0x10) != 0)
			{
				WriteVarUInt(memoryStream, body.UnknownFlags.Value);
			}
			if ((body.Flags & 0x20) != 0)
			{
				WriteVarUInt(memoryStream, body.EncodedMemberCount);
				foreach (TypeMember member in body.Members)
				{
					WriteVarUInt(memoryStream, member.NameIndex);
					WriteVarUInt(memoryStream, member.Flags);
					byte? reserve = member.Reserve;
					if (reserve.HasValue)
					{
						byte valueOrDefault = reserve.GetValueOrDefault();
						memoryStream.WriteByte(valueOrDefault);
					}
					WriteVarUInt(memoryStream, member.Offset);
					WriteVarUInt(memoryStream, member.TypeIndex);
				}
			}
			if ((body.Flags & 0x40) != 0)
			{
				WriteVarUInt(memoryStream, body.InterfaceCount.Value);
				foreach (TypeInterface @interface in body.Interfaces)
				{
					WriteVarUInt(memoryStream, @interface.TypeIndex);
					WriteVarUInt(memoryStream, @interface.Flags);
				}
			}
			if ((body.Flags & 0x80) != 0)
			{
				WriteVarUInt(memoryStream, body.AttributeIndex.Value);
			}
		}
		return memoryStream.ToArray();
	}

	private static byte[] WriteHashes(IReadOnlyList<TypeHash> hashes)
	{
		using MemoryStream memoryStream = new MemoryStream();
		byte[] array = new byte[4];
		WriteVarUInt(memoryStream, checked((uint)hashes.Count));
		foreach (TypeHash hash in hashes)
		{
			WriteVarUInt(memoryStream, hash.TypeIndex);
			BinaryPrimitives.WriteUInt32LittleEndian(array, hash.Hash);
			memoryStream.Write(array);
		}
		return memoryStream.ToArray();
	}

	private static byte[] BuildSection(NativeBphclSection section, IReadOnlyList<byte[]> children)
	{
		using MemoryStream memoryStream = new MemoryStream();
		foreach (byte[] child in children)
		{
			memoryStream.Write(child);
		}
		return BuildSection(section.Signature, section.ChunkKind, memoryStream.ToArray());
	}

	private static byte[] BuildSection(string signature, byte chunkKind, ReadOnlySpan<byte> payload)
	{
		byte[] array = new byte[checked(payload.Length + 8)];
		BinaryPrimitives.WriteUInt32BigEndian(array.AsSpan(0, 4), (uint)((chunkKind << 30) | array.Length));
		Encoding.ASCII.GetBytes(signature, array.AsSpan(4, 4));
		payload.CopyTo(array.AsSpan(8));
		return array;
	}

	private static ulong ReadVarUInt(byte[] bytes, ref int cursor, int end)
	{
		if (cursor >= end)
		{
			throw new InvalidDataException("Truncated BPHCL VarUInt.");
		}
		byte b = bytes[cursor++];
		if ((b & 0x80) == 0)
		{
			return b;
		}
		int num = b >> 3;
		int num2;
		switch (num)
		{
		case 16:
		case 17:
		case 18:
		case 19:
		case 20:
		case 21:
		case 22:
		case 23:
			num2 = 1;
			break;
		case 24:
		case 25:
		case 26:
		case 27:
			num2 = 2;
			break;
		case 28:
			num2 = 3;
			break;
		case 29:
			num2 = 4;
			break;
		default:
			throw new InvalidDataException($"Unsupported BPHCL VarUInt marker 0x{num:X}.");
		}
		int num3 = num2;
		if (cursor > end - num3)
		{
			throw new InvalidDataException("Truncated BPHCL VarUInt payload.");
		}
		ulong num4 = (ulong)(b & (num3 switch
		{
			1 => 63, 
			2 => 31, 
			_ => 7, 
		}));
		for (int i = 0; i < num3; i++)
		{
			num4 = (num4 << 8) | bytes[cursor++];
		}
		return num4;
	}

	private static void WriteVarUInt(Stream stream, uint value)
	{
		if (value <= 127)
		{
			stream.WriteByte((byte)value);
		}
		else if (value <= 16383)
		{
			stream.WriteByte((byte)(0x80 | (value >> 8)));
			stream.WriteByte((byte)value);
		}
		else if (value <= 2097151)
		{
			stream.WriteByte((byte)(0xC0 | (value >> 16)));
			stream.WriteByte((byte)(value >> 8));
			stream.WriteByte((byte)value);
		}
		else if (value <= 134217727)
		{
			stream.WriteByte((byte)(0xE0 | (value >> 24)));
			stream.WriteByte((byte)(value >> 16));
			stream.WriteByte((byte)(value >> 8));
			stream.WriteByte((byte)value);
		}
		else
		{
			stream.WriteByte(232);
			stream.WriteByte((byte)(value >> 24));
			stream.WriteByte((byte)(value >> 16));
			stream.WriteByte((byte)(value >> 8));
			stream.WriteByte((byte)value);
		}
	}
}
