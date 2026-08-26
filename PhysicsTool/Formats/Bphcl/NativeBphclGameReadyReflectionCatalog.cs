using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HKCLTool;

public static class NativeBphclGameReadyReflectionCatalog
{
	private sealed class Catalog
	{
		private readonly IReadOnlyList<string> _typeNames;

		private readonly IReadOnlyList<TypeDefinition> _definitions;

		private readonly IReadOnlyDictionary<uint, IReadOnlyList<(uint NameIndex, uint Value)>> _templates;

		public byte[] TypeSection { get; }

		public Catalog(byte[] typeSection, IReadOnlyList<string> typeNames, IReadOnlyList<TypeDefinition> definitions, IReadOnlyDictionary<uint, IReadOnlyList<(uint NameIndex, uint Value)>> templates)
		{
			TypeSection = typeSection;
			_typeNames = typeNames;
			_definitions = definitions;
			_templates = templates;
		}

		public IReadOnlyList<uint> GetTypeIndexes(string typeName)
		{
			return (from entry in _typeNames.Select((string name, int index) => (name: name, index: index))
				where entry.index > 0 && string.Equals(entry.name, typeName, StringComparison.Ordinal)
				select checked((uint)entry.index)).ToArray();
		}

		public IReadOnlyList<(uint NameIndex, uint Value)> GetTypeTemplates(uint typeIndex)
		{
			if (!_templates.TryGetValue(typeIndex, out IReadOnlyList<(uint, uint)> value))
			{
				return Array.Empty<(uint, uint)>();
			}
			return value;
		}

		public uint? GetArrayElementType(uint arrayTypeIndex)
		{
			if (!string.Equals(GetTypeName(arrayTypeIndex), "hkArray", StringComparison.Ordinal))
			{
				return null;
			}
			return (from value in GetTypeTemplates(arrayTypeIndex)
				where value.NameIndex == 2
				select value).Select<(uint, uint), uint?>((Func<(uint, uint), uint?>)(((uint NameIndex, uint Value) value) => value.Value)).FirstOrDefault();
		}

		public uint GetTypeIndex(string typeName, int occurrence = 0)
		{
			uint[] array = (from entry in _typeNames.Select((string name, int index) => (name: name, index: index))
				where entry.index > 0 && string.Equals(entry.name, typeName, StringComparison.Ordinal)
				select checked((uint)entry.index)).ToArray();
			if ((uint)occurrence >= (uint)array.Length)
			{
				throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no occurrence {occurrence} of '{typeName}'.");
			}
			return array[occurrence];
		}

		public uint? GetMemberOffset(string className, string memberName)
		{
			TypeDefinition definition = _definitions.FirstOrDefault((TypeDefinition entry) => string.Equals(entry.Name, className, StringComparison.Ordinal));
			HashSet<uint> hashSet = new HashSet<uint>();
			while ((object)definition != null && hashSet.Add(definition.TypeIndex))
			{
				TypeMember typeMember = definition.Members.FirstOrDefault((TypeMember candidate) => string.Equals(candidate.Name, memberName, StringComparison.Ordinal));
				if ((object)typeMember != null)
				{
					return typeMember.Offset;
				}
				definition = _definitions.FirstOrDefault((TypeDefinition candidate) => candidate.TypeIndex == definition.ParentTypeIndex);
			}
			return null;
		}

		public uint GetTypeSize(uint typeIndex)
		{
			return _definitions.FirstOrDefault((TypeDefinition entry) => entry.TypeIndex == typeIndex)?.Size ?? 0;
		}

		public uint GetTypeAlignment(uint typeIndex)
		{
			return _definitions.FirstOrDefault((TypeDefinition entry) => entry.TypeIndex == typeIndex)?.Alignment ?? 1;
		}

		public uint? GetMemberTypeIndex(uint typeIndex, uint memberOffset)
		{
			return FindMemberType(typeIndex, memberOffset, new HashSet<(uint, uint)>());
		}

		private uint? FindMemberType(uint typeIndex, uint memberOffset, ISet<(uint TypeIndex, uint Offset)> visited)
		{
			if (!visited.Add((typeIndex, memberOffset)))
			{
				return null;
			}
			TypeDefinition typeDefinition = _definitions.FirstOrDefault((TypeDefinition entry) => entry.TypeIndex == typeIndex);
			if ((object)typeDefinition == null)
			{
				return null;
			}
			TypeMember typeMember = typeDefinition.Members.FirstOrDefault((TypeMember candidate) => candidate.Offset == memberOffset);
			if ((object)typeMember != null)
			{
				return typeMember.TypeIndex;
			}
			foreach (TypeMember item in typeDefinition.Members.OrderByDescending((TypeMember candidate) => candidate.Offset))
			{
				uint typeSize = GetTypeSize(item.TypeIndex);
				if (typeSize != 0 && memberOffset >= item.Offset && memberOffset < item.Offset + typeSize)
				{
					uint? num = FindMemberType(item.TypeIndex, memberOffset - item.Offset, visited);
					if (num.HasValue)
					{
						return num.Value;
					}
				}
			}
			if (typeDefinition.ParentTypeIndex == 0)
			{
				return null;
			}
			uint typeSize2 = GetTypeSize(typeDefinition.ParentTypeIndex);
			uint num2 = ((typeDefinition.Members.Count == 0) ? typeSize2 : typeDefinition.Members.Min((TypeMember member) => member.Offset));
			uint num3 = ((num2 >= typeSize2) ? (num2 - typeSize2) : 0u);
			if (memberOffset >= num3 && memberOffset < num3 + typeSize2)
			{
				return FindMemberType(typeDefinition.ParentTypeIndex, memberOffset - num3, visited);
			}
			return FindMemberType(typeDefinition.ParentTypeIndex, memberOffset, visited);
		}

		public NativeBphclTypeLayout GetTypeLayout(string typeName)
		{
			TypeDefinition typeDefinition = _definitions.FirstOrDefault((TypeDefinition entry) => string.Equals(entry.Name, typeName, StringComparison.Ordinal));
			if ((object)typeDefinition == null)
			{
				throw new InvalidDataException("Built-in BPHCL TYPE catalog has no reflected layout for '" + typeName + "'.");
			}
			return CreateTypeLayout(typeDefinition);
		}

		public NativeBphclTypeLayout GetTypeLayout(uint typeIndex)
		{
			TypeDefinition typeDefinition = _definitions.FirstOrDefault((TypeDefinition entry) => entry.TypeIndex == typeIndex);
			if ((object)typeDefinition == null)
			{
				throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no reflected layout for type {typeIndex}.");
			}
			return CreateTypeLayout(typeDefinition);
		}

		public NativeBphclTypeLayout GetTypeLayoutIncludingBaseTypes(uint typeIndex)
		{
			TypeDefinition typeDefinition = _definitions.FirstOrDefault((TypeDefinition entry) => entry.TypeIndex == typeIndex);
			if ((object)typeDefinition == null)
			{
				throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no reflected layout for type {typeIndex}.");
			}
			List<TypeMember> list = new List<TypeMember>();
			CollectInheritedMembers(typeDefinition, list, new HashSet<uint>());
			return new NativeBphclTypeLayout(typeDefinition.TypeIndex, typeDefinition.Name, typeDefinition.Size, (from member in list.OrderBy((TypeMember member) => member.Offset).ThenBy<TypeMember, string>((TypeMember member) => member.Name, StringComparer.Ordinal)
				select new NativeBphclTypeLayoutMember(member.Name, member.Offset, member.TypeIndex, GetTypeName(member.TypeIndex))).ToArray());
		}

		private void CollectInheritedMembers(TypeDefinition definition, ICollection<TypeMember> members, ISet<uint> visited)
		{
			if (!visited.Add(definition.TypeIndex))
			{
				return;
			}
			if (definition.ParentTypeIndex != 0)
			{
				TypeDefinition typeDefinition = _definitions.FirstOrDefault((TypeDefinition entry) => entry.TypeIndex == definition.ParentTypeIndex);
				if ((object)typeDefinition != null)
				{
					CollectInheritedMembers(typeDefinition, members, visited);
				}
			}
			foreach (TypeMember member in definition.Members)
			{
				members.Add(member);
			}
		}

		private NativeBphclTypeLayout CreateTypeLayout(TypeDefinition definition)
		{
			return new NativeBphclTypeLayout(definition.TypeIndex, definition.Name, definition.Size, definition.Members.Select((TypeMember member) => new NativeBphclTypeLayoutMember(member.Name, member.Offset, member.TypeIndex, GetTypeName(member.TypeIndex))).ToArray());
		}

		public string GetTypeName(uint typeIndex)
		{
			if (typeIndex >= _typeNames.Count)
			{
				return $"type {typeIndex}";
			}
			return _typeNames[checked((int)typeIndex)];
		}
	}

	private sealed record TypeDefinition(uint TypeIndex, string Name, uint ParentTypeIndex, uint Size, uint Alignment, IReadOnlyList<TypeMember> Members);

	private sealed record TypeMember(string Name, uint Offset, uint TypeIndex);

	// Keep the recovered August 18 writer independent from later reflection
	// catalog revisions. Its DATA layout depends on this exact catalog.
	private const string ResourceName = "PhysicsTool.Formats.Bphcl.Resources.NativeBphclGameReadyTypeCatalog.bin";

	private static readonly Lazy<Catalog> CurrentCatalog = new Lazy<Catalog>(Load);

	public static byte[] TypeSection => CurrentCatalog.Value.TypeSection.ToArray();

	public static uint GetTypeIndex(string typeName)
	{
		return CurrentCatalog.Value.GetTypeIndex(typeName);
	}

	public static uint GetTypeIndex(string typeName, int occurrence)
	{
		return CurrentCatalog.Value.GetTypeIndex(typeName, occurrence);
	}

	public static IReadOnlyList<uint> GetTypeIndexes(string typeName)
	{
		return CurrentCatalog.Value.GetTypeIndexes(typeName);
	}

	public static IReadOnlyList<(uint NameIndex, uint Value)> GetTypeTemplates(uint typeIndex)
	{
		return CurrentCatalog.Value.GetTypeTemplates(typeIndex);
	}

	public static uint? GetArrayElementType(uint arrayTypeIndex)
	{
		return CurrentCatalog.Value.GetArrayElementType(arrayTypeIndex);
	}

	public static uint? GetMemberOffset(string className, string memberName)
	{
		return CurrentCatalog.Value.GetMemberOffset(className, memberName);
	}

	public static NativeBphclTypeLayout GetTypeLayout(string typeName)
	{
		return CurrentCatalog.Value.GetTypeLayout(typeName);
	}

	public static NativeBphclTypeLayout GetTypeLayout(uint typeIndex)
	{
		return CurrentCatalog.Value.GetTypeLayout(typeIndex);
	}

	public static NativeBphclTypeLayout GetTypeLayoutIncludingBaseTypes(uint typeIndex)
	{
		return CurrentCatalog.Value.GetTypeLayoutIncludingBaseTypes(typeIndex);
	}

	public static string GetTypeName(uint typeIndex)
	{
		return CurrentCatalog.Value.GetTypeName(typeIndex);
	}

	public static uint GetTypeSize(uint typeIndex)
	{
		return CurrentCatalog.Value.GetTypeSize(typeIndex);
	}

	public static uint GetTypeAlignment(uint typeIndex)
	{
		return CurrentCatalog.Value.GetTypeAlignment(typeIndex);
	}

	public static uint? GetMemberTypeIndex(uint typeIndex, uint memberOffset)
	{
		return CurrentCatalog.Value.GetMemberTypeIndex(typeIndex, memberOffset);
	}

	private static Catalog Load()
	{
		using Stream stream = typeof(NativeBphclGameReadyReflectionCatalog).Assembly.GetManifestResourceStream(ResourceName) ?? throw new InvalidOperationException($"Missing built-in BPHCL reflection catalog resource '{ResourceName}'.");
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		byte[] array = memoryStream.ToArray();
		if (array.Length < 8 || !array.AsSpan(4, 4).SequenceEqual("TYPE"u8))
		{
			throw new InvalidDataException("Built-in BPHCL reflection catalog has an invalid TYPE header.");
		}
		IReadOnlyList<(string Signature, byte[] Bytes)> children = ReadChildren(array);
		IReadOnlyList<string> strings = ReadStrings(GetChild("TST1", "TSTR"));
		IReadOnlyList<string> fieldNames = ReadStrings(GetChild("FST1", "FSTR"));
		ReadOnlySpan<byte> bytes = GetChild("TNA1", "TNAM");
		IReadOnlyList<string> typeNames = ReadTypeNames(bytes, strings);
		IReadOnlyDictionary<uint, IReadOnlyList<(uint, uint)>> templates = ReadTypeTemplates(bytes);
		IReadOnlyList<TypeDefinition> definitions = ReadDefinitions(GetChild("TBDY", "TBOD"), typeNames, fieldNames);
		return new Catalog(array, typeNames, definitions, templates);
		ReadOnlySpan<byte> GetChild(string first, string second)
		{
			(string, byte[]) tuple = children.FirstOrDefault<(string, byte[])>(delegate((string Signature, byte[] Bytes) entry)
			{
				var (text, _) = entry;
				return text == first || text == second;
			});
			if (tuple.Item2 == null)
			{
				throw new InvalidDataException("Built-in BPHCL TYPE catalog is missing " + first + ".");
			}
			return tuple.Item2;
		}
	}

	private static IReadOnlyList<(string Signature, byte[] Bytes)> ReadChildren(byte[] typeSection)
	{
		List<(string, byte[])> list = new List<(string, byte[])>();
		int num;
		for (int i = 8; i < typeSection.Length; i += num)
		{
			if (i > typeSection.Length - 8)
			{
				throw new InvalidDataException("Built-in BPHCL TYPE catalog has a truncated child section.");
			}
			num = checked((int)(BinaryPrimitives.ReadUInt32BigEndian(typeSection.AsSpan(i, 4)) & 0x3FFFFFFF));
			if (num < 8 || i > typeSection.Length - num)
			{
				throw new InvalidDataException("Built-in BPHCL TYPE catalog has an invalid child size.");
			}
			string item = Encoding.ASCII.GetString(typeSection, i + 4, 4);
			list.Add((item, typeSection.AsSpan(i + 8, num - 8).ToArray()));
		}
		return list;
	}

	private static IReadOnlyList<string> ReadStrings(ReadOnlySpan<byte> bytes)
	{
		List<string> list = new List<string>();
		int num = 0;
		for (int i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] == 0)
			{
				Encoding uTF = Encoding.UTF8;
				int num2 = num;
				list.Add(uTF.GetString(bytes.Slice(num2, i - num2)));
				num = i + 1;
			}
		}
		if (num < bytes.Length)
		{
			list.Add(Encoding.UTF8.GetString(bytes.Slice(num)));
		}
		return list;
	}

	private static IReadOnlyList<string> ReadTypeNames(ReadOnlySpan<byte> bytes, IReadOnlyList<string> strings)
	{
		int cursor = 0;
		int num = checked((int)ReadVarUInt(bytes, ref cursor));
		List<string> list = new List<string>(num) { string.Empty };
		for (int i = 1; i < num; i++)
		{
			int num3;
			checked
			{
				int num2 = (int)ReadVarUInt(bytes, ref cursor);
				num3 = (int)ReadVarUInt(bytes, ref cursor);
				list.Add((num2 >= 0 && num2 < strings.Count) ? strings[num2] : $"<invalid type string {num2}>");
			}
			for (int j = 0; j < num3; j++)
			{
				ReadVarUInt(bytes, ref cursor);
				ReadVarUInt(bytes, ref cursor);
			}
		}
		return list;
	}

	private static IReadOnlyDictionary<uint, IReadOnlyList<(uint NameIndex, uint Value)>> ReadTypeTemplates(ReadOnlySpan<byte> bytes)
	{
		int cursor = 0;
		checked
		{
			int num = (int)ReadVarUInt(bytes, ref cursor);
			Dictionary<uint, IReadOnlyList<(uint, uint)>> dictionary = new Dictionary<uint, IReadOnlyList<(uint, uint)>>();
			for (int i = 1; i < num; i = unchecked(i + 1))
			{
				ReadVarUInt(bytes, ref cursor);
				int num2 = (int)ReadVarUInt(bytes, ref cursor);
				(uint, uint)[] array = new(uint, uint)[num2];
				for (int j = 0; j < num2; j = unchecked(j + 1))
				{
					array[j] = ((uint)ReadVarUInt(bytes, ref cursor), (uint)ReadVarUInt(bytes, ref cursor));
				}
				dictionary[(uint)i] = array;
			}
			return dictionary;
		}
	}

	private static IReadOnlyList<TypeDefinition> ReadDefinitions(ReadOnlySpan<byte> bytes, IReadOnlyList<string> typeNames, IReadOnlyList<string> fieldNames)
	{
		int cursor = 0;
		List<TypeDefinition> list = new List<TypeDefinition>();
		checked
		{
			while (cursor < bytes.Length)
			{
				uint num = (uint)ReadVarUInt(bytes, ref cursor);
				if (num == 0)
				{
					list.Add(new TypeDefinition(0u, string.Empty, 0u, 0u, 1u, Array.Empty<TypeMember>()));
					continue;
				}
				uint parentTypeIndex = (uint)ReadVarUInt(bytes, ref cursor);
				uint num2 = (uint)ReadVarUInt(bytes, ref cursor);
				if ((num2 & 1) != 0)
				{
					ReadVarUInt(bytes, ref cursor);
				}
				if ((num2 & 2) != 0)
				{
					ReadVarUInt(bytes, ref cursor);
				}
				if ((num2 & 4) != 0)
				{
					ReadVarUInt(bytes, ref cursor);
				}
				uint size = 0u;
				uint alignment = 1u;
				if ((num2 & 8) != 0)
				{
					size = (uint)ReadVarUInt(bytes, ref cursor);
					alignment = (uint)ReadVarUInt(bytes, ref cursor);
				}
				if ((num2 & 0x10) != 0)
				{
					ReadVarUInt(bytes, ref cursor);
				}
				List<TypeMember> list2 = new List<TypeMember>();
				if ((num2 & 0x20) != 0)
				{
					int num3 = (int)(ReadVarUInt(bytes, ref cursor) & 0xFFFF);
					for (int i = 0; i < num3; i = unchecked(i + 1))
					{
						int num4 = (int)ReadVarUInt(bytes, ref cursor);
						if (((uint)ReadVarUInt(bytes, ref cursor) & 0x80) != 0)
						{
							cursor = unchecked(cursor + 1);
						}
						uint offset = (uint)ReadVarUInt(bytes, ref cursor);
						uint typeIndex = (uint)ReadVarUInt(bytes, ref cursor);
						list2.Add(new TypeMember((num4 >= 0 && num4 < fieldNames.Count) ? fieldNames[num4] : $"<invalid field {num4}>", offset, typeIndex));
					}
				}
				if ((num2 & 0x40) != 0)
				{
					int num5 = (int)ReadVarUInt(bytes, ref cursor);
					for (int j = 0; j < num5; j = unchecked(j + 1))
					{
						ReadVarUInt(bytes, ref cursor);
						ReadVarUInt(bytes, ref cursor);
					}
				}
				if ((num2 & 0x80) != 0)
				{
					ReadVarUInt(bytes, ref cursor);
				}
				string name = ((num < typeNames.Count) ? typeNames[(int)num] : $"type {num}");
				list.Add(new TypeDefinition(num, name, parentTypeIndex, size, alignment, list2));
			}
			return list;
		}
	}

	private static ulong ReadVarUInt(ReadOnlySpan<byte> bytes, ref int cursor)
	{
		if ((uint)cursor >= (uint)bytes.Length)
		{
			throw new InvalidDataException("Truncated BPHCL reflection VarUInt.");
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
			throw new InvalidDataException($"Unsupported BPHCL reflection VarUInt marker 0x{num:X}.");
		}
		int num3 = num2;
		if (cursor > bytes.Length - num3)
		{
			throw new InvalidDataException("Truncated BPHCL reflection VarUInt payload.");
		}
		num2 = b;
		ulong num4 = (ulong)(num2 & (num3 switch
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
}
