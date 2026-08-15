using System.Buffers.Binary;
using System.Reflection;
using System.Text;

namespace HKCLTool;

// A built-in reflection catalog for standalone BPHCL output. The resource is
// TYPE metadata only: it contains no actor, cloth, particle, collider, AAMP,
// or other donor data. Keeping this schema in the application removes the
// runtime requirement for a BPHCL template while preserving native IDs.
public static class NativeBphclReflectionCatalog
{
    private const string ResourceName = "PhysicsTool.Formats.Bphcl.Resources.NativeBphclTypeCatalog.bin";
    private static readonly Lazy<Catalog> CurrentCatalog = new(Load);

    public static byte[] TypeSection => CurrentCatalog.Value.TypeSection.ToArray();

    public static uint GetTypeIndex(string typeName) => CurrentCatalog.Value.GetTypeIndex(typeName);

    public static uint GetTypeIndex(string typeName, int occurrence) => CurrentCatalog.Value.GetTypeIndex(typeName, occurrence);

    public static IReadOnlyList<uint> GetTypeIndexes(string typeName) => CurrentCatalog.Value.GetTypeIndexes(typeName);

    public static IReadOnlyList<(uint NameIndex, uint Value)> GetTypeTemplates(uint typeIndex) =>
        CurrentCatalog.Value.GetTypeTemplates(typeIndex);

    public static uint? GetArrayElementType(uint arrayTypeIndex) =>
        CurrentCatalog.Value.GetArrayElementType(arrayTypeIndex);

    public static uint? GetMemberOffset(string className, string memberName) =>
        CurrentCatalog.Value.GetMemberOffset(className, memberName);

    public static NativeBphclTypeLayout GetTypeLayout(string typeName) =>
        CurrentCatalog.Value.GetTypeLayout(typeName);

    public static NativeBphclTypeLayout GetTypeLayout(uint typeIndex) =>
        CurrentCatalog.Value.GetTypeLayout(typeIndex);

    public static NativeBphclTypeLayout GetTypeLayoutIncludingBaseTypes(uint typeIndex) =>
        CurrentCatalog.Value.GetTypeLayoutIncludingBaseTypes(typeIndex);

    public static string GetTypeName(uint typeIndex) => CurrentCatalog.Value.GetTypeName(typeIndex);

    public static uint GetTypeSize(uint typeIndex) => CurrentCatalog.Value.GetTypeSize(typeIndex);

    public static uint? GetMemberTypeIndex(uint typeIndex, uint memberOffset) =>
        CurrentCatalog.Value.GetMemberTypeIndex(typeIndex, memberOffset);

    private static Catalog Load()
    {
        var assembly = typeof(NativeBphclReflectionCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing built-in BPHCL reflection catalog resource '{ResourceName}'.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var typeSection = memory.ToArray();
        if (typeSection.Length < 8 || !typeSection.AsSpan(4, 4).SequenceEqual("TYPE"u8))
            throw new InvalidDataException("Built-in BPHCL reflection catalog has an invalid TYPE header.");

        var children = ReadChildren(typeSection);
        var typeStrings = ReadStrings(GetChild("TST1", "TSTR"));
        var fieldStrings = ReadStrings(GetChild("FST1", "FSTR"));
        var typeNameBytes = GetChild("TNA1", "TNAM");
        var names = ReadTypeNames(typeNameBytes, typeStrings);
        var templates = ReadTypeTemplates(typeNameBytes);
        var definitions = ReadDefinitions(GetChild("TBDY", "TBOD"), names, fieldStrings);
        return new Catalog(typeSection, names, definitions, templates);

        ReadOnlySpan<byte> GetChild(string first, string second)
        {
            var child = children.FirstOrDefault(entry => entry.Signature is var signature && (signature == first || signature == second));
            if (child.Bytes is null)
                throw new InvalidDataException($"Built-in BPHCL TYPE catalog is missing {first}.");
            return child.Bytes;
        }
    }

    private static IReadOnlyList<(string Signature, byte[] Bytes)> ReadChildren(byte[] typeSection)
    {
        var result = new List<(string Signature, byte[] Bytes)>();
        var cursor = 8;
        while (cursor < typeSection.Length)
        {
            if (cursor > typeSection.Length - 8)
                throw new InvalidDataException("Built-in BPHCL TYPE catalog has a truncated child section.");
            var size = checked((int)(BinaryPrimitives.ReadUInt32BigEndian(typeSection.AsSpan(cursor, 4)) & 0x3fff_ffffu));
            if (size < 8 || cursor > typeSection.Length - size)
                throw new InvalidDataException("Built-in BPHCL TYPE catalog has an invalid child size.");
            var signature = Encoding.ASCII.GetString(typeSection, cursor + 4, 4);
            result.Add((signature, typeSection.AsSpan(cursor + 8, size - 8).ToArray()));
            cursor += size;
        }
        return result;
    }

    private static IReadOnlyList<string> ReadStrings(ReadOnlySpan<byte> bytes)
    {
        var values = new List<string>();
        var start = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != 0)
                continue;
            values.Add(Encoding.UTF8.GetString(bytes[start..index]));
            start = index + 1;
        }
        if (start < bytes.Length)
            values.Add(Encoding.UTF8.GetString(bytes[start..]));
        return values;
    }

    private static IReadOnlyList<string> ReadTypeNames(ReadOnlySpan<byte> bytes, IReadOnlyList<string> strings)
    {
        var cursor = 0;
        var count = checked((int)ReadVarUInt(bytes, ref cursor));
        var result = new List<string>(count) { string.Empty };
        for (var typeIndex = 1; typeIndex < count; typeIndex++)
        {
            var nameIndex = checked((int)ReadVarUInt(bytes, ref cursor));
            var templateCount = checked((int)ReadVarUInt(bytes, ref cursor));
            result.Add(nameIndex >= 0 && nameIndex < strings.Count ? strings[nameIndex] : $"<invalid type string {nameIndex}>");
            for (var templateIndex = 0; templateIndex < templateCount; templateIndex++)
            {
                _ = ReadVarUInt(bytes, ref cursor);
                _ = ReadVarUInt(bytes, ref cursor);
            }
        }
        return result;
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<(uint NameIndex, uint Value)>> ReadTypeTemplates(ReadOnlySpan<byte> bytes)
    {
        var cursor = 0;
        var count = checked((int)ReadVarUInt(bytes, ref cursor));
        var result = new Dictionary<uint, IReadOnlyList<(uint NameIndex, uint Value)>>();
        for (var typeIndex = 1; typeIndex < count; typeIndex++)
        {
            _ = ReadVarUInt(bytes, ref cursor); // type-name string index
            var templateCount = checked((int)ReadVarUInt(bytes, ref cursor));
            var values = new (uint NameIndex, uint Value)[templateCount];
            for (var templateIndex = 0; templateIndex < templateCount; templateIndex++)
            {
                values[templateIndex] = (
                    checked((uint)ReadVarUInt(bytes, ref cursor)),
                    checked((uint)ReadVarUInt(bytes, ref cursor)));
            }
            result[checked((uint)typeIndex)] = values;
        }
        return result;
    }

    private static IReadOnlyList<TypeDefinition> ReadDefinitions(
        ReadOnlySpan<byte> bytes,
        IReadOnlyList<string> typeNames,
        IReadOnlyList<string> fieldNames)
    {
        var cursor = 0;
        var definitions = new List<TypeDefinition>();
        while (cursor < bytes.Length)
        {
            var typeIndex = checked((uint)ReadVarUInt(bytes, ref cursor));
            if (typeIndex == 0)
            {
                definitions.Add(new TypeDefinition(0, string.Empty, 0, 0, Array.Empty<TypeMember>()));
                continue;
            }

            var parentTypeIndex = checked((uint)ReadVarUInt(bytes, ref cursor));
            var flags = checked((uint)ReadVarUInt(bytes, ref cursor));
            if ((flags & 0x01) != 0) _ = ReadVarUInt(bytes, ref cursor);
            if ((flags & 0x02) != 0) _ = ReadVarUInt(bytes, ref cursor);
            if ((flags & 0x04) != 0) _ = ReadVarUInt(bytes, ref cursor);
            uint size = 0;
            if ((flags & 0x08) != 0)
            {
                size = checked((uint)ReadVarUInt(bytes, ref cursor));
                _ = ReadVarUInt(bytes, ref cursor);
            }
            if ((flags & 0x10) != 0) _ = ReadVarUInt(bytes, ref cursor);

            var members = new List<TypeMember>();
            if ((flags & 0x20) != 0)
            {
                var count = checked((int)(ReadVarUInt(bytes, ref cursor) & 0xffff));
                for (var index = 0; index < count; index++)
                {
                    var nameIndex = checked((int)ReadVarUInt(bytes, ref cursor));
                    var memberFlags = checked((uint)ReadVarUInt(bytes, ref cursor));
                    if ((memberFlags & 0x80) != 0)
                        cursor++;
                    var offset = checked((uint)ReadVarUInt(bytes, ref cursor));
                    var memberTypeIndex = checked((uint)ReadVarUInt(bytes, ref cursor));
                    members.Add(new TypeMember(
                        nameIndex >= 0 && nameIndex < fieldNames.Count ? fieldNames[nameIndex] : $"<invalid field {nameIndex}>",
                        offset,
                        memberTypeIndex));
                }
            }
            if ((flags & 0x40) != 0)
            {
                var count = checked((int)ReadVarUInt(bytes, ref cursor));
                for (var index = 0; index < count; index++)
                {
                    _ = ReadVarUInt(bytes, ref cursor);
                    _ = ReadVarUInt(bytes, ref cursor);
                }
            }
            if ((flags & 0x80) != 0)
                _ = ReadVarUInt(bytes, ref cursor);

            var name = typeIndex < typeNames.Count ? typeNames[checked((int)typeIndex)] : $"type {typeIndex}";
            definitions.Add(new TypeDefinition(typeIndex, name, parentTypeIndex, size, members));
        }
        return definitions;
    }

    private static ulong ReadVarUInt(ReadOnlySpan<byte> bytes, ref int cursor)
    {
        if ((uint)cursor >= (uint)bytes.Length)
            throw new InvalidDataException("Truncated BPHCL reflection VarUInt.");
        var first = bytes[cursor++];
        if ((first & 0x80) == 0)
            return first;
        var marker = first >> 3;
        var count = marker switch
        {
            >= 0x10 and <= 0x17 => 1,
            >= 0x18 and <= 0x1b => 2,
            0x1c => 3,
            0x1d => 4,
            _ => throw new InvalidDataException($"Unsupported BPHCL reflection VarUInt marker 0x{marker:X}.")
        };
        if (cursor > bytes.Length - count)
            throw new InvalidDataException("Truncated BPHCL reflection VarUInt payload.");
        ulong value = (ulong)(first & (count switch { 1 => 0x3f, 2 => 0x1f, _ => 0x07 }));
        for (var index = 0; index < count; index++)
            value = (value << 8) | bytes[cursor++];
        return value;
    }

    private sealed class Catalog
    {
        private readonly IReadOnlyList<string> _typeNames;
        private readonly IReadOnlyList<TypeDefinition> _definitions;
        private readonly IReadOnlyDictionary<uint, IReadOnlyList<(uint NameIndex, uint Value)>> _templates;

        public Catalog(
            byte[] typeSection,
            IReadOnlyList<string> typeNames,
            IReadOnlyList<TypeDefinition> definitions,
            IReadOnlyDictionary<uint, IReadOnlyList<(uint NameIndex, uint Value)>> templates)
        {
            TypeSection = typeSection;
            _typeNames = typeNames;
            _definitions = definitions;
            _templates = templates;
        }

        public byte[] TypeSection { get; }

        public IReadOnlyList<uint> GetTypeIndexes(string typeName) =>
            _typeNames
                .Select((name, index) => (name, index))
                .Where(entry => entry.index > 0 && string.Equals(entry.name, typeName, StringComparison.Ordinal))
                .Select(entry => checked((uint)entry.index))
                .ToArray();

        public IReadOnlyList<(uint NameIndex, uint Value)> GetTypeTemplates(uint typeIndex) =>
            _templates.TryGetValue(typeIndex, out var values) ? values : Array.Empty<(uint NameIndex, uint Value)>();

        public uint? GetArrayElementType(uint arrayTypeIndex)
        {
            if (!string.Equals(GetTypeName(arrayTypeIndex), "hkArray", StringComparison.Ordinal))
                return null;
            return GetTypeTemplates(arrayTypeIndex)
                .Where(value => value.NameIndex == 2)
                .Select(value => (uint?)value.Value)
                .FirstOrDefault();
        }

        public uint GetTypeIndex(string typeName, int occurrence = 0)
        {
            var matches = _typeNames
                .Select((name, index) => (name, index))
                .Where(entry => entry.index > 0 && string.Equals(entry.name, typeName, StringComparison.Ordinal))
                .Select(entry => checked((uint)entry.index))
                .ToArray();
            if ((uint)occurrence >= (uint)matches.Length)
                throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no occurrence {occurrence} of '{typeName}'.");
            return matches[occurrence];
        }

        public uint? GetMemberOffset(string className, string memberName)
        {
            var definition = _definitions.FirstOrDefault(entry => string.Equals(entry.Name, className, StringComparison.Ordinal));
            var visited = new HashSet<uint>();
            while (definition is not null && visited.Add(definition.TypeIndex))
            {
                var member = definition.Members.FirstOrDefault(candidate => string.Equals(candidate.Name, memberName, StringComparison.Ordinal));
                if (member is not null)
                    return member.Offset;
                definition = _definitions.FirstOrDefault(candidate => candidate.TypeIndex == definition.ParentTypeIndex);
            }
            return null;
        }

        public uint GetTypeSize(uint typeIndex) =>
            _definitions.FirstOrDefault(entry => entry.TypeIndex == typeIndex)?.Size ?? 0;

        public uint? GetMemberTypeIndex(uint typeIndex, uint memberOffset) =>
            FindMemberType(typeIndex, memberOffset, new HashSet<(uint TypeIndex, uint Offset)>());

        private uint? FindMemberType(uint typeIndex, uint memberOffset, ISet<(uint TypeIndex, uint Offset)> visited)
        {
            if (!visited.Add((typeIndex, memberOffset)))
                return null;

            var definition = _definitions.FirstOrDefault(entry => entry.TypeIndex == typeIndex);
            if (definition is null)
                return null;

            var direct = definition.Members.FirstOrDefault(candidate => candidate.Offset == memberOffset);
            if (direct is not null)
                return direct.TypeIndex;

            // Several Havok objects embed small records such as
            // CollidableTransformMap. Relocation entries point into those
            // records, so resolve their member type recursively instead of
            // falling back to the containing class type.
            foreach (var member in definition.Members.OrderByDescending(candidate => candidate.Offset))
            {
                var memberSize = GetTypeSize(member.TypeIndex);
                if (memberSize == 0 || memberOffset < member.Offset || memberOffset >= member.Offset + memberSize)
                    continue;

                var nested = FindMemberType(member.TypeIndex, memberOffset - member.Offset, visited);
                if (nested.HasValue)
                    return nested.Value;
            }

            if (definition.ParentTypeIndex == 0)
                return null;

            // Derived Havok classes place their parent record immediately
            // before their own first declared field. Use that physical offset
            // when resolving a pointer inside inherited deformer data.
            var parentSize = GetTypeSize(definition.ParentTypeIndex);
            var firstOwnMember = definition.Members.Count == 0
                ? parentSize
                : definition.Members.Min(member => member.Offset);
            var parentOffset = firstOwnMember >= parentSize
                ? firstOwnMember - parentSize
                : 0;
            if (memberOffset >= parentOffset && memberOffset < parentOffset + parentSize)
                return FindMemberType(definition.ParentTypeIndex, memberOffset - parentOffset, visited);

            return FindMemberType(definition.ParentTypeIndex, memberOffset, visited);
        }

        public NativeBphclTypeLayout GetTypeLayout(string typeName)
        {
            var definition = _definitions.FirstOrDefault(entry => string.Equals(entry.Name, typeName, StringComparison.Ordinal));
            if (definition is null)
                throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no reflected layout for '{typeName}'.");
            return CreateTypeLayout(definition);
        }

        public NativeBphclTypeLayout GetTypeLayout(uint typeIndex)
        {
            var definition = _definitions.FirstOrDefault(entry => entry.TypeIndex == typeIndex);
            if (definition is null)
                throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no reflected layout for type {typeIndex}.");
            return CreateTypeLayout(definition);
        }

        // Havok derived classes serialize their base records at the beginning
        // of the same object. The direct layout is useful for inspection;
        // writer passes that must visit embedded fields need the full record.
        public NativeBphclTypeLayout GetTypeLayoutIncludingBaseTypes(uint typeIndex)
        {
            var definition = _definitions.FirstOrDefault(entry => entry.TypeIndex == typeIndex);
            if (definition is null)
                throw new InvalidDataException($"Built-in BPHCL TYPE catalog has no reflected layout for type {typeIndex}.");

            var members = new List<TypeMember>();
            CollectInheritedMembers(definition, members, new HashSet<uint>());
            return new NativeBphclTypeLayout(
                definition.TypeIndex,
                definition.Name,
                definition.Size,
                members
                    .OrderBy(member => member.Offset)
                    .ThenBy(member => member.Name, StringComparer.Ordinal)
                    .Select(member => new NativeBphclTypeLayoutMember(
                        member.Name,
                        member.Offset,
                        member.TypeIndex,
                        GetTypeName(member.TypeIndex)))
                    .ToArray());
        }

        private void CollectInheritedMembers(TypeDefinition definition, ICollection<TypeMember> members, ISet<uint> visited)
        {
            if (!visited.Add(definition.TypeIndex))
                return;

            if (definition.ParentTypeIndex != 0)
            {
                var parent = _definitions.FirstOrDefault(entry => entry.TypeIndex == definition.ParentTypeIndex);
                if (parent is not null)
                    CollectInheritedMembers(parent, members, visited);
            }

            foreach (var member in definition.Members)
                members.Add(member);
        }

        private NativeBphclTypeLayout CreateTypeLayout(TypeDefinition definition) =>
            new(
                definition.TypeIndex,
                definition.Name,
                definition.Size,
                definition.Members.Select(member => new NativeBphclTypeLayoutMember(
                    member.Name,
                    member.Offset,
                    member.TypeIndex,
                    GetTypeName(member.TypeIndex))).ToArray());

        public string GetTypeName(uint typeIndex) =>
            typeIndex < _typeNames.Count ? _typeNames[checked((int)typeIndex)] : $"type {typeIndex}";
    }

    private sealed record TypeDefinition(uint TypeIndex, string Name, uint ParentTypeIndex, uint Size, IReadOnlyList<TypeMember> Members);
    private sealed record TypeMember(string Name, uint Offset, uint TypeIndex);
}

public sealed record NativeBphclTypeLayout(
    uint TypeIndex,
    string Name,
    uint Size,
    IReadOnlyList<NativeBphclTypeLayoutMember> Members);

public sealed record NativeBphclTypeLayoutMember(
    string Name,
    uint Offset,
    uint TypeIndex,
    string TypeName);
