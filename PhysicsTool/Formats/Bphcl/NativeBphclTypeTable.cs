using System.Buffers.Binary;
using System.Text;

namespace HKCLTool;

// The TAG0 TYPE table is independent of cloth data, but ITEM and PTCH records
// use its numeric type IDs. This helper merges source-only reflected types into
// a target table and returns the two remapping tables needed by native merge.
internal sealed class NativeBphclTypeTable
{
    private NativeBphclTypeTable(
        IReadOnlyDictionary<uint, uint> targetToMerged,
        IReadOnlyDictionary<uint, uint> sourceToMerged,
        byte[]? replacementSection)
    {
        TargetToMerged = targetToMerged;
        SourceToMerged = sourceToMerged;
        ReplacementSection = replacementSection;
    }

    public IReadOnlyDictionary<uint, uint> TargetToMerged { get; }
    public IReadOnlyDictionary<uint, uint> SourceToMerged { get; }
    public byte[]? ReplacementSection { get; }

    // Native cross-file merging must keep the target's original TYPE table.
    // Havok's reflection records are not safely interchangeable just because
    // their visible class names match. A donor is allowed only when every
    // imported ITEM/PTCH type has an equivalent target definition.
    public static NativeBphclTypeTable Create(
        NativeBphclDocument target,
        NativeBphclDocument source,
        IEnumerable<uint>? requiredSourceTypes = null)
    {
        var required = requiredSourceTypes?.Append(0u).Distinct().ToArray()
            ?? Enumerable.Range(0, source.TypeNames.Count).Select(index => (uint)index).ToArray();
        if (TryUseTargetTypeSection(target, source, required, out var targetBackedTable))
            return targetBackedTable;

        // The caller has already identified an incompatible donor graph.
        // Rebuild only its needed reflection closure rather than blindly
        // copying the entire donor TYPE table into the target.
        return CreateExperimentalUnion(target, source, required);
    }

    public static IReadOnlyList<string> FindMissingRequiredTypes(
        NativeBphclDocument target,
        NativeBphclDocument source,
        IEnumerable<uint> requiredSourceTypes)
    {
        var targetKeys = BuildTypeKeys(Parse(target));
        var sourceKeys = BuildTypeKeys(Parse(source));
        var targetByKey = targetKeys.Values.ToHashSet(StringComparer.Ordinal);
        var targetNames = target.TypeNames.Skip(1).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var sourceType in requiredSourceTypes.Where(type => type != 0).Distinct())
        {
            if (sourceKeys.TryGetValue(sourceType, out var key) && targetByKey.Contains(key))
                continue;

            var name = source.GetTypeName(sourceType) ?? $"type {sourceType}";
            missing.Add(targetNames.Contains(name)
                ? $"{name} (definition differs)"
                : name);
        }
        return missing.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static NativeBphclTypeTable CreateExperimentalUnion(
        NativeBphclDocument target,
        NativeBphclDocument source,
        IReadOnlyCollection<uint> requiredSourceTypes)
    {
        var targetTable = Parse(target);
        var sourceTable = Parse(source);
        var required = ExpandTypeDependencies(sourceTable, requiredSourceTypes);
        var targetKeys = BuildTypeKeys(targetTable);
        var sourceKeys = BuildTypeKeys(sourceTable);
        var targetByKey = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var entry in targetKeys)
            targetByKey.TryAdd(entry.Value, entry.Key);

        // Type names are not sufficient: multiple hkArray/T* entries can
        // have different templates. Match their complete reflection key,
        // then append only genuinely absent definitions.
        var sourceToMerged = new Dictionary<uint, uint> { [0] = 0 };
        var addedSourceTypes = new List<uint>();
        var nextTypeIndex = checked((uint)targetTable.NamedTypes.Count + 1);
        foreach (var sourceIndex in required.Where(index => index != 0).OrderBy(index => index))
        {
            if (sourceKeys.TryGetValue(sourceIndex, out var key) &&
                targetByKey.TryGetValue(key, out var targetIndex))
            {
                sourceToMerged[sourceIndex] = targetIndex;
                continue;
            }

            // A few Havok helper types are named but intentionally omit a
            // TBDY layout record. They cannot be appended as independent
            // reflection entries. Map a uniquely named counterpart already
            // present in the target instead (for example the packed
            // BoneSpaceDeformer blend blocks).
            if (!sourceTable.Bodies.Any(body => body.TypeIndex == sourceIndex))
            {
                var sourceName = source.GetTypeName(sourceIndex);
                var candidates = target.TypeNames
                    .Select((name, index) => (name, index))
                    .Where(entry => entry.index > 0 && string.Equals(entry.name, sourceName, StringComparison.Ordinal))
                    .Select(entry => (uint)entry.index)
                    .ToArray();
                if (candidates.Length == 1)
                {
                    sourceToMerged[sourceIndex] = candidates[0];
                    continue;
                }
            }

            sourceToMerged[sourceIndex] = nextTypeIndex++;
            addedSourceTypes.Add(sourceIndex);
        }

        var targetToMerged = Enumerable.Range(0, target.TypeNames.Count)
            .ToDictionary(index => (uint)index, index => (uint)index);
        if (addedSourceTypes.Count == 0)
            return new NativeBphclTypeTable(targetToMerged, sourceToMerged, null);

        var typeStrings = new StringTable(targetTable.TypeStrings);
        var fieldStrings = new StringTable(targetTable.FieldStrings);
        var mergedNamedTypes = targetTable.NamedTypes
            .Select(named => named.Remap(targetToMerged, typeStrings.IdentityMap))
            .ToList();
        var mergedBodies = targetTable.Bodies
            .Select(body => body.Remap(targetToMerged, fieldStrings.IdentityMap))
            .ToList();
        var mergedHashes = targetTable.Hashes
            .Select(hash => hash.Remap(targetToMerged))
            .ToList();

        var sourceTypeStringMap = typeStrings.BuildMap(sourceTable.TypeStrings);
        var sourceFieldStringMap = fieldStrings.BuildMap(sourceTable.FieldStrings);
        foreach (var sourceTypeIndex in addedSourceTypes)
        {
            var named = sourceTable.NamedTypes.ElementAtOrDefault(checked((int)sourceTypeIndex - 1))
                ?? throw new InvalidDataException($"Source BPHCL TYPE has no name record for type {sourceTypeIndex}.");
            var body = sourceTable.Bodies.FirstOrDefault(entry => entry.TypeIndex == sourceTypeIndex)
                ?? throw new InvalidDataException($"Source BPHCL TYPE has no body record for type {sourceTypeIndex}.");

            mergedNamedTypes.Add(named.Remap(sourceToMerged, sourceTypeStringMap));
            mergedBodies.Add(body.Remap(sourceToMerged, sourceFieldStringMap));
            foreach (var hash in sourceTable.Hashes.Where(entry => entry.TypeIndex == sourceTypeIndex))
                mergedHashes.Add(hash.Remap(sourceToMerged));
        }

        var replacement = BuildSection(
            targetTable.Section,
            targetTable.BuildChildren(typeStrings.Values, fieldStrings.Values, mergedNamedTypes, mergedBodies, mergedHashes));
        return new NativeBphclTypeTable(targetToMerged, sourceToMerged, replacement);
    }

    private static IReadOnlyList<uint> ExpandTypeDependencies(
        TypeTable source,
        IEnumerable<uint> roots)
    {
        var required = new HashSet<uint>();
        var queue = new Queue<uint>(roots.Where(type => type > 0 && type <= source.NamedTypes.Count));
        while (queue.TryDequeue(out var typeIndex))
        {
            if (!required.Add(typeIndex))
                continue;

            var named = source.NamedTypes[checked((int)typeIndex - 1)];
            foreach (var template in named.Templates)
                Enqueue(template.TypeIndex);

            var body = source.Bodies.FirstOrDefault(entry => entry.TypeIndex == typeIndex);
            if (body is null)
                continue;
            Enqueue(body.ParentTypeIndex);
            if (body.SubtypeIndex is uint subtype)
                Enqueue(subtype);
            foreach (var member in body.Members)
                Enqueue(member.TypeIndex);
            foreach (var @interface in body.Interfaces)
                Enqueue(@interface.TypeIndex);
        }
        return required.OrderBy(type => type).ToArray();

        void Enqueue(uint typeIndex)
        {
            if (typeIndex > 0 && typeIndex <= source.NamedTypes.Count && !required.Contains(typeIndex))
                queue.Enqueue(typeIndex);
        }
    }

    private static bool TryUseTargetTypeSection(
        NativeBphclDocument target,
        NativeBphclDocument source,
        IReadOnlyCollection<uint> requiredSourceTypes,
        out NativeBphclTypeTable table)
    {
        var targetKeys = BuildTypeKeys(Parse(target));
        var sourceKeys = BuildTypeKeys(Parse(source));
        var targetByKey = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var entry in targetKeys)
            targetByKey.TryAdd(entry.Value, entry.Key);

        var sourceToTarget = new Dictionary<uint, uint> { [0] = 0 };
        foreach (var sourceType in requiredSourceTypes)
        {
            if (sourceType == 0)
                continue;
            if (!sourceKeys.TryGetValue(sourceType, out var key) ||
                !targetByKey.TryGetValue(key, out var targetType))
            {
                table = null!;
                return false;
            }
            sourceToTarget[sourceType] = targetType;
        }

        var targetIdentity = Enumerable.Range(0, target.TypeNames.Count)
            .ToDictionary(index => (uint)index, index => (uint)index);
        table = new NativeBphclTypeTable(targetIdentity, sourceToTarget, replacementSection: null);
        return true;
    }

    private static bool TryUseSourceTypeSection(
        NativeBphclDocument target,
        NativeBphclDocument source,
        out NativeBphclTypeTable table)
    {
        var targetTable = Parse(target);
        var sourceTable = Parse(source);
        var sourceKeys = BuildTypeKeys(sourceTable);
        var targetKeys = BuildTypeKeys(targetTable);
        var sourceByKey = new Dictionary<string, Queue<uint>>(StringComparer.Ordinal);
        for (var index = 1; index < source.TypeNames.Count; index++)
        {
            var key = sourceKeys[(uint)index];
            if (!sourceByKey.TryGetValue(key, out var queue))
            {
                queue = new Queue<uint>();
                sourceByKey.Add(key, queue);
            }
            queue.Enqueue((uint)index);
        }

        var targetToSource = new Dictionary<uint, uint> { [0] = 0 };
        for (var index = 1; index < target.TypeNames.Count; index++)
        {
            var key = targetKeys[(uint)index];
            if (!sourceByKey.TryGetValue(key, out var queue) || queue.Count == 0)
            {
                table = null!;
                return false;
            }
            targetToSource[(uint)index] = queue.Dequeue();
        }

        var sourceToSource = Enumerable.Range(0, source.TypeNames.Count)
            .ToDictionary(index => (uint)index, index => (uint)index);
        var sourceTypeSection = source.TypeSection
            ?? throw new InvalidDataException("Source BPHCL has no TYPE section.");
        var rawTypeSection = source.Bytes
            .AsSpan(sourceTypeSection.Offset, sourceTypeSection.Size)
            .ToArray();
        table = new NativeBphclTypeTable(targetToSource, sourceToSource, rawTypeSection);
        return true;
    }

    // Names alone are not enough: the TYPE table repeats generic forms such
    // as hkArray and T*. More importantly, two games/files can use the same
    // class name with a different reflected member layout. ITEM/PTCH records
    // are interpreted through that layout, so type equivalence includes the
    // full reflection body as well as generic template arguments.
    private static IReadOnlyDictionary<uint, string> BuildTypeKeys(TypeTable table)
    {
        var keys = new Dictionary<uint, string>();
        var active = new HashSet<uint>();

        string Describe(uint index)
        {
            if (index == 0)
                return "void";
            if (keys.TryGetValue(index, out var existing))
                return existing;
            if (index > table.NamedTypes.Count)
                return $"external:{index}";

            var named = table.NamedTypes[checked((int)index - 1)];
            var typeName = named.StringIndex < table.TypeStrings.Count
                ? table.TypeStrings[checked((int)named.StringIndex)]
                : $"invalid-name:{named.StringIndex}";
            if (!active.Add(index))
                return $"cycle:{typeName}";

            var templates = string.Join(",", named.Templates.Select(template =>
            {
                var parameterName = template.StringIndex < table.TypeStrings.Count
                    ? table.TypeStrings[checked((int)template.StringIndex)]
                    : $"invalid-parameter:{template.StringIndex}";
                return $"{parameterName}={Describe(template.TypeIndex)}";
            }));
            active.Remove(index);

            var body = table.Bodies.FirstOrDefault(entry => entry.TypeIndex == index);
            var baseKey = templates.Length == 0 ? typeName : $"{typeName}<{templates}>";
            if (body is null)
            {
                // Some reflected helper/forward types deliberately have a
                // name record but no layout body. They cannot be copied as
                // standalone TYPE records, so match their declared identity.
                keys[index] = baseKey;
                return baseKey;
            }

            var layout = string.Join(";", new[]
                {
                    $"parent={Describe(body.ParentTypeIndex)}",
                    $"flags={body.Flags:X}",
                    $"format={body.Format}",
                    $"subtype={(body.SubtypeIndex is uint subtype ? Describe(subtype) : "-")}",
                    $"version={body.Version}",
                    $"size={body.Size}",
                    $"alignment={body.Alignment}",
                    $"unknown={body.UnknownFlags}",
                    "members=[" + string.Join(",", body.Members.Select(member =>
                    {
                        var memberName = member.NameIndex < table.FieldStrings.Count
                            ? table.FieldStrings[checked((int)member.NameIndex)]
                            : $"invalid-field:{member.NameIndex}";
                        return $"{memberName}@{member.Offset:X}:{Describe(member.TypeIndex)}:{member.Flags:X}:{member.Reserve}";
                    })) + "]",
                    "interfaces=[" + string.Join(",", body.Interfaces.Select(@interface =>
                        $"{Describe(@interface.TypeIndex)}:{@interface.Flags:X}")) + "]",
                    $"attribute={body.AttributeIndex}"
                });
            var key = baseKey + "{" + layout + "}";
            keys[index] = key;
            return key;
        }

        for (var index = 1u; index <= table.NamedTypes.Count; index++)
            _ = Describe(index);
        return keys;
    }

    private static TypeTable Parse(NativeBphclDocument document)
    {
        var section = document.TypeSection ?? throw new InvalidDataException("BPHCL has no TYPE section.");
        var typeStrings = ReadStrings(section.Children.FirstOrDefault(child => child.Signature is "TST1" or "TSTR"), document.Bytes);
        var fieldStrings = ReadStrings(section.Children.FirstOrDefault(child => child.Signature is "FST1" or "FSTR"), document.Bytes);
        var named = ReadNamedTypes(section.Children.FirstOrDefault(child => child.Signature is "TNA1" or "TNAM"), document.Bytes);
        var bodies = ReadBodies(section.Children.FirstOrDefault(child => child.Signature is "TBDY" or "TBOD"), document.Bytes);
        var hashes = ReadHashes(section.Children.FirstOrDefault(child => child.Signature == "THSH"), document.Bytes);
        return new TypeTable(section, typeStrings, fieldStrings, named, bodies, hashes, document.Bytes);
    }

    private sealed record TypeTable(
        NativeBphclSection Section,
        IReadOnlyList<string> TypeStrings,
        IReadOnlyList<string> FieldStrings,
        IReadOnlyList<NamedType> NamedTypes,
        IReadOnlyList<TypeBody> Bodies,
        IReadOnlyList<TypeHash> Hashes,
        byte[] OriginalBytes)
    {
        public IReadOnlyList<byte[]> BuildChildren(
            IReadOnlyList<string> typeStrings,
            IReadOnlyList<string> fieldStrings,
            IReadOnlyList<NamedType> namedTypes,
            IReadOnlyList<TypeBody> bodies,
            IReadOnlyList<TypeHash> hashes)
        {
            var result = new List<byte[]>();
            var addedTypeCount = checked(namedTypes.Count - NamedTypes.Count);
            foreach (var child in Section.Children)
            {
                var payload = child.Signature switch
                {
                    // TPTR has one eight-byte entry per named type. It is
                    // opaque for now, but new reflection entries still need
                    // matching empty slots so the table stays index-aligned.
                    "TPTR" => ExpandPointerTable(OriginalBytes.AsSpan(child.PayloadOffset, child.PayloadSize), addedTypeCount),
                    "TST1" or "TSTR" => WriteStrings(typeStrings),
                    "FST1" or "FSTR" => WriteStrings(fieldStrings),
                    "TNA1" or "TNAM" => WriteNamedTypes(namedTypes),
                    "TBDY" or "TBOD" => WriteBodies(bodies),
                    "THSH" => WriteHashes(hashes),
                    _ => OriginalBytes.AsSpan(child.PayloadOffset, child.PayloadSize).ToArray()
                };
                result.Add(BuildSection(child.Signature, child.ChunkKind, payload));
            }
            return result;
        }

        private static byte[] ExpandPointerTable(ReadOnlySpan<byte> original, int addedTypeCount)
        {
            if (addedTypeCount <= 0)
                return original.ToArray();
            var expanded = new byte[checked(original.Length + addedTypeCount * 8)];
            original.CopyTo(expanded);
            return expanded;
        }
    }

    private sealed class StringTable
    {
        private readonly List<string> _values;
        private readonly Dictionary<string, uint> _indices;

        public StringTable(IReadOnlyList<string> values)
        {
            _values = values.ToList();
            _indices = new Dictionary<string, uint>(StringComparer.Ordinal);
            for (var index = 0; index < _values.Count; index++)
                _indices.TryAdd(_values[index], (uint)index);
        }

        public IReadOnlyList<string> Values => _values;
        public IReadOnlyDictionary<uint, uint> IdentityMap => Enumerable.Range(0, _values.Count)
            .ToDictionary(index => (uint)index, index => (uint)index);

        public IReadOnlyDictionary<uint, uint> BuildMap(IReadOnlyList<string> source)
        {
            var map = new Dictionary<uint, uint>();
            for (var index = 0; index < source.Count; index++)
            {
                if (!_indices.TryGetValue(source[index], out var targetIndex))
                {
                    targetIndex = checked((uint)_values.Count);
                    _values.Add(source[index]);
                    _indices.Add(source[index], targetIndex);
                }
                map[(uint)index] = targetIndex;
            }
            return map;
        }
    }

    private sealed record NamedType(uint StringIndex, IReadOnlyList<TypeTemplate> Templates)
    {
        public NamedType Remap(IReadOnlyDictionary<uint, uint> types, IReadOnlyDictionary<uint, uint> strings) =>
            new(strings[StringIndex], Templates.Select(template => template.Remap(types, strings)).ToArray());
    }

    private sealed record TypeTemplate(uint StringIndex, uint TypeIndex)
    {
        public TypeTemplate Remap(IReadOnlyDictionary<uint, uint> types, IReadOnlyDictionary<uint, uint> strings) =>
            new(strings[StringIndex], RemapTypeReference(TypeIndex, types));
    }

    private sealed record TypeMember(uint NameIndex, uint Flags, byte? Reserve, uint Offset, uint TypeIndex)
    {
        public TypeMember Remap(IReadOnlyDictionary<uint, uint> types, IReadOnlyDictionary<uint, uint> strings) =>
            new(strings[NameIndex], Flags, Reserve, Offset, RemapTypeReference(TypeIndex, types));
    }

    private sealed record TypeInterface(uint TypeIndex, uint Flags)
    {
        public TypeInterface Remap(IReadOnlyDictionary<uint, uint> types) => new(RemapTypeReference(TypeIndex, types), Flags);
    }

    private sealed record TypeBody(
        uint TypeIndex,
        uint ParentTypeIndex,
        uint Flags,
        uint? Format,
        uint? SubtypeIndex,
        uint? Version,
        uint? Size,
        uint? Alignment,
        uint? UnknownFlags,
        uint EncodedMemberCount,
        IReadOnlyList<TypeMember> Members,
        uint? InterfaceCount,
        IReadOnlyList<TypeInterface> Interfaces,
        uint? AttributeIndex)
    {
        public TypeBody Remap(IReadOnlyDictionary<uint, uint> types, IReadOnlyDictionary<uint, uint> strings) =>
            new(
                RemapTypeReference(TypeIndex, types),
                RemapTypeReference(ParentTypeIndex, types),
                Flags,
                Format,
                SubtypeIndex is uint subtype ? RemapTypeReference(subtype, types) : null,
                Version,
                Size,
                Alignment,
                UnknownFlags,
                EncodedMemberCount,
                Members.Select(member => member.Remap(types, strings)).ToArray(),
                InterfaceCount,
                Interfaces.Select(@interface => @interface.Remap(types)).ToArray(),
                AttributeIndex);
    }

    private sealed record TypeHash(uint TypeIndex, uint Hash)
    {
        public TypeHash Remap(IReadOnlyDictionary<uint, uint> types) => new(RemapTypeReference(TypeIndex, types), Hash);
    }

    // TotK's TYPE metadata uses out-of-range values such as 0x7fffffff as
    // sentinels. They are metadata markers, not Havok type-table references.
    private static uint RemapTypeReference(uint value, IReadOnlyDictionary<uint, uint> types) =>
        types.TryGetValue(value, out var mapped) ? mapped : value;

    private static IReadOnlyList<string> ReadStrings(NativeBphclSection? section, byte[] bytes)
    {
        if (section == null)
            throw new InvalidDataException("BPHCL TYPE is missing a required string table.");
        var strings = new List<string>();
        var start = section.PayloadOffset;
        var end = section.Offset + section.Size;
        for (var cursor = start; cursor < end; cursor++)
        {
            if (bytes[cursor] != 0)
                continue;
            strings.Add(Encoding.UTF8.GetString(bytes, start, cursor - start));
            start = cursor + 1;
        }
        if (start < end)
            strings.Add(Encoding.UTF8.GetString(bytes, start, end - start));
        return strings;
    }

    private static IReadOnlyList<NamedType> ReadNamedTypes(NativeBphclSection? section, byte[] bytes)
    {
        if (section == null)
            throw new InvalidDataException("BPHCL TYPE is missing TNA1.");
        var cursor = section.PayloadOffset;
        var end = section.Offset + section.Size;
        var count = checked((int)ReadVarUInt(bytes, ref cursor, end));
        var result = new List<NamedType>(Math.Max(0, count - 1));
        for (var index = 1; index < count; index++)
        {
            var stringIndex = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            var templateCount = checked((int)ReadVarUInt(bytes, ref cursor, end));
            var templates = new List<TypeTemplate>(templateCount);
            for (var template = 0; template < templateCount; template++)
            {
                templates.Add(new TypeTemplate(
                    checked((uint)ReadVarUInt(bytes, ref cursor, end)),
                    checked((uint)ReadVarUInt(bytes, ref cursor, end))));
            }
            result.Add(new NamedType(stringIndex, templates));
        }
        return result;
    }

    private static IReadOnlyList<TypeBody> ReadBodies(NativeBphclSection? section, byte[] bytes)
    {
        if (section == null)
            throw new InvalidDataException("BPHCL TYPE is missing TBDY.");
        var cursor = section.PayloadOffset;
        var end = section.Offset + section.Size;
        var result = new List<TypeBody>();
        while (cursor < end)
        {
            var typeIndex = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            if (typeIndex == 0)
            {
                result.Add(new TypeBody(0, 0, 0, null, null, null, null, null, null, 0, Array.Empty<TypeMember>(), null, Array.Empty<TypeInterface>(), null));
                continue;
            }

            var parent = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            var flags = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            uint? format = (flags & 0x01) != 0 ? checked((uint)ReadVarUInt(bytes, ref cursor, end)) : null;
            uint? subtype = (flags & 0x02) != 0 ? checked((uint)ReadVarUInt(bytes, ref cursor, end)) : null;
            uint? version = (flags & 0x04) != 0 ? checked((uint)ReadVarUInt(bytes, ref cursor, end)) : null;
            uint? size = null;
            uint? alignment = null;
            if ((flags & 0x08) != 0)
            {
                size = checked((uint)ReadVarUInt(bytes, ref cursor, end));
                alignment = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            }
            uint? unknownFlags = (flags & 0x10) != 0 ? checked((uint)ReadVarUInt(bytes, ref cursor, end)) : null;
            var encodedMemberCount = (flags & 0x20) != 0 ? checked((uint)ReadVarUInt(bytes, ref cursor, end)) : 0u;
            var members = new List<TypeMember>(checked((int)(encodedMemberCount & 0xffff)));
            for (var index = 0; index < (encodedMemberCount & 0xffff); index++)
            {
                var name = checked((uint)ReadVarUInt(bytes, ref cursor, end));
                var memberFlags = checked((uint)ReadVarUInt(bytes, ref cursor, end));
                byte? reserve = null;
                if ((memberFlags & 0x80) != 0)
                {
                    if (cursor >= end)
                        throw new InvalidDataException("Truncated BPHCL TYPE member padding.");
                    reserve = bytes[cursor++];
                }
                members.Add(new TypeMember(
                    name,
                    memberFlags,
                    reserve,
                    checked((uint)ReadVarUInt(bytes, ref cursor, end)),
                    checked((uint)ReadVarUInt(bytes, ref cursor, end))));
            }
            uint? interfaceCount = null;
            var interfaces = new List<TypeInterface>();
            if ((flags & 0x40) != 0)
            {
                interfaceCount = checked((uint)ReadVarUInt(bytes, ref cursor, end));
                for (var index = 0; index < interfaceCount; index++)
                {
                    interfaces.Add(new TypeInterface(
                        checked((uint)ReadVarUInt(bytes, ref cursor, end)),
                        checked((uint)ReadVarUInt(bytes, ref cursor, end))));
                }
            }
            uint? attribute = (flags & 0x80) != 0 ? checked((uint)ReadVarUInt(bytes, ref cursor, end)) : null;
            result.Add(new TypeBody(typeIndex, parent, flags, format, subtype, version, size, alignment, unknownFlags, encodedMemberCount, members, interfaceCount, interfaces, attribute));
        }
        return result;
    }

    private static IReadOnlyList<TypeHash> ReadHashes(NativeBphclSection? section, byte[] bytes)
    {
        if (section == null)
            return Array.Empty<TypeHash>();
        var cursor = section.PayloadOffset;
        var end = section.Offset + section.Size;
        var count = checked((int)ReadVarUInt(bytes, ref cursor, end));
        var result = new List<TypeHash>(count);
        for (var index = 0; index < count; index++)
        {
            var type = checked((uint)ReadVarUInt(bytes, ref cursor, end));
            if (cursor > end - 4)
                throw new InvalidDataException("Truncated BPHCL THSH entry.");
            result.Add(new TypeHash(type, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor, 4))));
            cursor += 4;
        }
        return result;
    }

    private static byte[] WriteStrings(IReadOnlyList<string> strings)
    {
        using var stream = new MemoryStream();
        foreach (var value in strings)
        {
            stream.Write(Encoding.UTF8.GetBytes(value));
            stream.WriteByte(0);
        }
        return stream.ToArray();
    }

    private static byte[] WriteNamedTypes(IReadOnlyList<NamedType> types)
    {
        using var stream = new MemoryStream();
        WriteVarUInt(stream, checked((uint)types.Count + 1));
        foreach (var type in types)
        {
            WriteVarUInt(stream, type.StringIndex);
            WriteVarUInt(stream, checked((uint)type.Templates.Count));
            foreach (var template in type.Templates)
            {
                WriteVarUInt(stream, template.StringIndex);
                WriteVarUInt(stream, template.TypeIndex);
            }
        }
        return stream.ToArray();
    }

    private static byte[] WriteBodies(IReadOnlyList<TypeBody> bodies)
    {
        using var stream = new MemoryStream();
        foreach (var body in bodies)
        {
            WriteVarUInt(stream, body.TypeIndex);
            if (body.TypeIndex == 0)
                continue;
            WriteVarUInt(stream, body.ParentTypeIndex);
            WriteVarUInt(stream, body.Flags);
            if ((body.Flags & 0x01) != 0) WriteVarUInt(stream, body.Format!.Value);
            if ((body.Flags & 0x02) != 0) WriteVarUInt(stream, body.SubtypeIndex!.Value);
            if ((body.Flags & 0x04) != 0) WriteVarUInt(stream, body.Version!.Value);
            if ((body.Flags & 0x08) != 0)
            {
                WriteVarUInt(stream, body.Size!.Value);
                WriteVarUInt(stream, body.Alignment!.Value);
            }
            if ((body.Flags & 0x10) != 0) WriteVarUInt(stream, body.UnknownFlags!.Value);
            if ((body.Flags & 0x20) != 0)
            {
                WriteVarUInt(stream, body.EncodedMemberCount);
                foreach (var member in body.Members)
                {
                    WriteVarUInt(stream, member.NameIndex);
                    WriteVarUInt(stream, member.Flags);
                    if (member.Reserve is byte reserve) stream.WriteByte(reserve);
                    WriteVarUInt(stream, member.Offset);
                    WriteVarUInt(stream, member.TypeIndex);
                }
            }
            if ((body.Flags & 0x40) != 0)
            {
                WriteVarUInt(stream, body.InterfaceCount!.Value);
                foreach (var @interface in body.Interfaces)
                {
                    WriteVarUInt(stream, @interface.TypeIndex);
                    WriteVarUInt(stream, @interface.Flags);
                }
            }
            if ((body.Flags & 0x80) != 0) WriteVarUInt(stream, body.AttributeIndex!.Value);
        }
        return stream.ToArray();
    }

    private static byte[] WriteHashes(IReadOnlyList<TypeHash> hashes)
    {
        using var stream = new MemoryStream();
        var bytes = new byte[4];
        WriteVarUInt(stream, checked((uint)hashes.Count));
        foreach (var hash in hashes)
        {
            WriteVarUInt(stream, hash.TypeIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, hash.Hash);
            stream.Write(bytes);
        }
        return stream.ToArray();
    }

    private static byte[] BuildSection(NativeBphclSection section, IReadOnlyList<byte[]> children)
    {
        using var stream = new MemoryStream();
        foreach (var child in children)
            stream.Write(child);
        return BuildSection(section.Signature, section.ChunkKind, stream.ToArray());
    }

    private static byte[] BuildSection(string signature, byte chunkKind, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[checked(payload.Length + 8)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), ((uint)chunkKind << 30) | (uint)bytes.Length);
        Encoding.ASCII.GetBytes(signature, bytes.AsSpan(4, 4));
        payload.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private static ulong ReadVarUInt(byte[] bytes, ref int cursor, int end)
    {
        if (cursor >= end)
            throw new InvalidDataException("Truncated BPHCL VarUInt.");
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
            _ => throw new InvalidDataException($"Unsupported BPHCL VarUInt marker 0x{marker:X}.")
        };
        if (cursor > end - count)
            throw new InvalidDataException("Truncated BPHCL VarUInt payload.");
        var mask = count switch { 1 => 0x3f, 2 => 0x1f, _ => 0x07 };
        ulong value = (ulong)(first & mask);
        for (var index = 0; index < count; index++)
            value = (value << 8) | bytes[cursor++];
        return value;
    }

    private static void WriteVarUInt(Stream stream, uint value)
    {
        if (value <= 0x7f)
        {
            stream.WriteByte((byte)value);
            return;
        }
        if (value <= 0x3fff)
        {
            stream.WriteByte((byte)(0x80 | (value >> 8)));
            stream.WriteByte((byte)value);
            return;
        }
        if (value <= 0x1f_ffff)
        {
            stream.WriteByte((byte)(0xc0 | (value >> 16)));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
            return;
        }
        if (value <= 0x7ff_ffff)
        {
            stream.WriteByte((byte)(0xe0 | (value >> 24)));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
            return;
        }
        // Marker 0x1d carries four following bytes. TotK uses this form for
        // sentinel values such as 0x7fffffff inside otherwise ordinary TYPE
        // records.
        stream.WriteByte(0xe8);
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
