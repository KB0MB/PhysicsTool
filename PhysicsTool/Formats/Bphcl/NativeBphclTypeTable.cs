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

    internal static uint GetArrayElementType(NativeBphclDocument document, uint arrayType)
    {
        var table = Parse(document);
        var named = table.NamedTypes[checked((int)arrayType - 1)];
        return named.Templates.FirstOrDefault(t => t.StringIndex < table.TypeStrings.Count &&
            table.TypeStrings[(int)t.StringIndex] == "tT")?.TypeIndex
            ?? throw new InvalidDataException("The native array has no element type metadata.");
    }

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
        return CreateExperimentalUnion(Parse(target), Parse(source), required);
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

    public static IReadOnlyList<string> DescribeNamedTypes(
        NativeBphclDocument document,
        string typeName)
    {
        var table = Parse(document);
        var lines = new List<string>();
        for (uint typeIndex = 1; typeIndex <= table.NamedTypes.Count; typeIndex++)
        {
            if (!string.Equals(table.GetTypeName(typeIndex), typeName, StringComparison.Ordinal))
                continue;

            var named = table.NamedTypes[checked((int)typeIndex - 1)];
            var body = table.Bodies.FirstOrDefault(entry => entry.TypeIndex == typeIndex);
            var templates = named.Templates.Count == 0
                ? "none"
                : string.Join(",", named.Templates.Select(template =>
                    $"{table.TypeStrings[checked((int)template.StringIndex)]}={template.TypeIndex}:{table.GetTypeName(template.TypeIndex)}"));
            lines.Add($"named|{typeIndex}|body={(body is null ? 0 : 1)}|templates={templates}");
        }

        return lines;
    }

    public static IReadOnlyList<string> DescribeTypeIndex(
        NativeBphclDocument document,
        uint typeIndex)
    {
        var table = Parse(document);
        if (typeIndex == 0 || typeIndex > table.NamedTypes.Count)
            return [$"typeIndex|{typeIndex}|missing"];

        var named = table.NamedTypes[checked((int)typeIndex - 1)];
        var body = table.Bodies.FirstOrDefault(entry => entry.TypeIndex == typeIndex);
        var templates = named.Templates.Count == 0
            ? "none"
            : string.Join(",", named.Templates.Select(template =>
                $"{table.TypeStrings[checked((int)template.StringIndex)]}={template.TypeIndex}:{table.GetTypeName(template.TypeIndex)}"));
        var hashes = string.Join(",",
            table.Hashes.Where(entry => entry.TypeIndex == typeIndex)
                .Select(entry => $"0x{entry.Hash:X8}"));
        var lines = new List<string>
        {
            $"typeIndex|{typeIndex}|name={table.GetTypeName(typeIndex)}|body={(body is null ? 0 : 1)}|templates={templates}|hashes={(hashes.Length == 0 ? "none" : hashes)}"
        };
        if (body is not null)
        {
            lines.Add($"body|flags=0x{body.Flags:X}|size=0x{body.Size:X}|align={body.Alignment}|parent={body.ParentTypeIndex}:{table.GetTypeName(body.ParentTypeIndex)}");
            lines.AddRange(body.Members.OrderBy(member => member.Offset).Select(member =>
                $"member|0x{member.Offset:X2}|{table.FieldStrings[checked((int)member.NameIndex)]}|flags=0x{member.Flags:X}|{member.TypeIndex}:{table.GetTypeName(member.TypeIndex)}"));
        }

        return lines;
    }

    public static IReadOnlyList<string> DescribeTypeKeys(
        NativeBphclDocument document,
        string typeName)
    {
        var table = Parse(document);
        var keys = BuildTypeKeys(table);
        return keys
            .Where(entry => string.Equals(table.GetTypeName(entry.Key), typeName, StringComparison.Ordinal))
            .OrderBy(entry => entry.Key)
            .Select(entry => $"typeKey|{entry.Key}|{entry.Value}")
            .ToArray();
    }

    internal static NativeBphclTypeTable AddEmbeddedTypes(NativeBphclDocument target, IEnumerable<uint> catalogTypes) =>
        CreateExperimentalUnion(Parse(target), Parse(NativeBphclReflectionCatalog.TypeSection), catalogTypes.Append(0u).Distinct().ToArray());

    public static NativeBphclTypeTable CreateEmbeddedCatalogUnion(
        NativeBphclDocument source,
        IEnumerable<uint> requiredSourceTypes)
    {
        var required = requiredSourceTypes.Append(0u).Distinct().ToArray();
        return CreateExperimentalUnion(Parse(NativeBphclReflectionCatalog.TypeSection), Parse(source), required);
    }

    // Fresh HKCL -> BPHCL output must not declare every class available in the
    // bundled catalog. Native files include only the reflected type closure
    // used by their ITEM/PTCH records, with ids compacted to that local table.
    public static NativeBphclTypeTable CreateCompactEmbeddedCatalog(
        IEnumerable<uint> requiredCatalogTypes,
        IEnumerable<uint>? concreteCatalogTypes = null)
    {
        var roots = requiredCatalogTypes.Append(0u).Distinct().ToArray();
        var concreteRoots = (concreteCatalogTypes ?? requiredCatalogTypes)
            .Append(0u)
            .Distinct()
            .ToArray();
        var catalog = AddBodylessBlendAliases(
            Parse(NativeBphclReflectionCatalog.TypeSection),
            concreteRoots,
            "hclObjectSpaceDeformer");
        catalog = AddBodylessBlendAliases(catalog, concreteRoots, "hclBoneSpaceDeformer");
        var canonicalTypes = BuildBlendAliases(catalog, concreteRoots);
        var expanded = ExpandTypeDependencies(catalog, roots, canonicalTypes).ToArray();
        var requiredSet = expanded.ToHashSet();
        var compactOrder = expanded.ToDictionary(index => index, index => index);

        // A concrete blend entry can replace an earlier bodyless alias. Keep
        // the canonical reflected type in that alias's original slot instead
        // of appending it near the end of the compact table. Some Havok cloth
        // runtimes use byte-sized blend type ids even though TAG0 stores wider
        // indices, so moving an active FourBlend type above 255 makes an
        // otherwise valid deformer inert.
        foreach (var (alias, canonical) in canonicalTypes)
        {
            if (!requiredSet.Contains(canonical))
                continue;

            var blendName = catalog.GetTypeName(canonical);
            var earliestNamedSlot = Enumerable.Range(1, catalog.NamedTypes.Count)
                .Select(index => checked((uint)index))
                .Where(index => string.Equals(
                    catalog.GetTypeName(index),
                    blendName,
                    StringComparison.Ordinal))
                .DefaultIfEmpty(canonical)
                .Min();
            compactOrder[canonical] = Math.Min(
                compactOrder[canonical],
                Math.Min(alias, earliestNamedSlot));
        }

        var reflectedTypes = catalog.Bodies.Select(body => body.TypeIndex).ToHashSet();
        foreach (var concrete in expanded.Where(index => reflectedTypes.Contains(index)))
        {
            var blendName = catalog.GetTypeName(concrete);
            if (string.IsNullOrEmpty(blendName) ||
                !blendName.Contains("Deformer::", StringComparison.Ordinal) ||
                !blendName.EndsWith("BlendEntryBlock", StringComparison.Ordinal))
            {
                continue;
            }

            var earliestNamedSlot = Enumerable.Range(1, catalog.NamedTypes.Count)
                .Select(index => checked((uint)index))
                .Where(index => string.Equals(
                    catalog.GetTypeName(index),
                    blendName,
                    StringComparison.Ordinal))
                .DefaultIfEmpty(concrete)
                .Min();
            // Keep active packed blend blocks inside the runtime's byte-sized type-id range.
            compactOrder[concrete] = Math.Min(
                Math.Min(compactOrder[concrete], earliestNamedSlot), 192u);
        }

        var required = expanded
            .OrderBy(index => catalog.GetTypeName(index)?.EndsWith("BlendEntryBlock", StringComparison.Ordinal) == true ? 0 : 1)
            .ThenBy(index => compactOrder[index])
            .ThenBy(index => index)
            .ToArray();
        requiredSet = required.ToHashSet();

        var remap = new Dictionary<uint, uint> { [0] = 0 };
        for (var index = 0; index < required.Length; index++)
            remap[required[index]] = checked((uint)index + 1);

        foreach (var (alias, canonical) in canonicalTypes)
            if (remap.TryGetValue(canonical, out var mapped))
                remap[alias] = mapped;

        var sourceNamedTypes = required
            .Select(index => catalog.NamedTypes[checked((int)index - 1)])
            .ToArray();
        var sourceBodies = catalog.Bodies
            .Where(body => requiredSet.Contains(body.TypeIndex))
            .ToArray();
        var (typeStrings, typeStringMap) = BuildCompactStringTable(
            catalog.TypeStrings,
            sourceNamedTypes.SelectMany(named =>
                new[] { named.StringIndex }.Concat(named.Templates.Select(template => template.StringIndex))));
        var (fieldStrings, fieldStringMap) = BuildCompactStringTable(
            catalog.FieldStrings,
            sourceBodies.SelectMany(body => body.Members.Select(member => member.NameIndex)));
        var namedTypes = sourceNamedTypes
            .Select(named => named.Remap(remap, typeStringMap, catalog.TypeStrings))
            .ToArray();
        var bodies = sourceBodies
            .Select(body => body.Remap(remap, fieldStringMap))
            .ToArray();
        var hashes = catalog.Hashes
            .Where(hash => requiredSet.Contains(hash.TypeIndex))
            .Select(hash => hash.Remap(remap))
            .ToList();

        var knownObjectBlendArrayHashes = new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["hclObjectSpaceDeformer::FourBlendEntryBlock"] = 0xCF50639E,
            ["hclObjectSpaceDeformer::ThreeBlendEntryBlock"] = 0xA234E80F,
            ["hclObjectSpaceDeformer::TwoBlendEntryBlock"] = 0x72714023,
            ["hclObjectSpaceDeformer::OneBlendEntryBlock"] = 0xCDDEE230
        };
        foreach (var sourceType in required)
        {
            if (!string.Equals(catalog.GetTypeName(sourceType), "hkArray", StringComparison.Ordinal))
                continue;

            var named = catalog.NamedTypes[checked((int)sourceType - 1)];
            var elementTemplate = named.Templates.FirstOrDefault(template =>
                template.StringIndex < catalog.TypeStrings.Count &&
                string.Equals(catalog.TypeStrings[checked((int)template.StringIndex)], "tT", StringComparison.Ordinal));
            if (elementTemplate is null ||
                !knownObjectBlendArrayHashes.TryGetValue(catalog.GetTypeName(elementTemplate.TypeIndex), out var expectedHash))
                continue;

            var mappedType = remap[sourceType];
            hashes.RemoveAll(hash => hash.TypeIndex == mappedType);
            hashes.Add(new TypeHash(mappedType, expectedHash));
        }
        hashes.Sort((left, right) => left.TypeIndex.CompareTo(right.TypeIndex));

        var section = BuildSection(
            catalog.Section,
            catalog.BuildSubsetChildren(
                typeStrings,
                fieldStrings,
                namedTypes,
                bodies,
                hashes));
        return new NativeBphclTypeTable(remap, remap, section);
    }
    private static NativeBphclTypeTable CreateExperimentalUnion(
        TypeTable targetTable,
        TypeTable sourceTable,
        IReadOnlyCollection<uint> requiredSourceTypes)
    {
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

            sourceToMerged[sourceIndex] = nextTypeIndex++;
            addedSourceTypes.Add(sourceIndex);
        }

        var targetToMerged = Enumerable.Range(0, targetTable.NamedTypes.Count + 1)
            .ToDictionary(index => (uint)index, index => (uint)index);
        if (addedSourceTypes.Count == 0)
            return new NativeBphclTypeTable(targetToMerged, sourceToMerged, null);

        var typeStrings = new StringTable(targetTable.TypeStrings);
        var fieldStrings = new StringTable(targetTable.FieldStrings);
        var mergedNamedTypes = targetTable.NamedTypes
            .Select(named => named.Remap(targetToMerged, typeStrings.IdentityMap, targetTable.TypeStrings))
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
            var body = sourceTable.Bodies.FirstOrDefault(entry => entry.TypeIndex == sourceTypeIndex);

            mergedNamedTypes.Add(named.Remap(sourceToMerged, sourceTypeStringMap, sourceTable.TypeStrings));
            // Forward/packed blend aliases legitimately have a name without
            // a layout body. Preserve that distinction instead of inventing a
            // layout or binding to a same-named concrete type in the target.
            if (body != null) mergedBodies.Add(body.Remap(sourceToMerged, sourceFieldStringMap));
            foreach (var hash in sourceTable.Hashes.Where(entry => entry.TypeIndex == sourceTypeIndex))
                mergedHashes.Add(hash.Remap(sourceToMerged));
        }

        var replacement = BuildSection(
            targetTable.Section,
            targetTable.BuildChildren(typeStrings.Values, fieldStrings.Values, mergedNamedTypes, mergedBodies, mergedHashes));
        return new NativeBphclTypeTable(targetToMerged, sourceToMerged, replacement);
    }

    private static (IReadOnlyList<string> Values, IReadOnlyDictionary<uint, uint> Map) BuildCompactStringTable(
        IReadOnlyList<string> source,
        IEnumerable<uint> requiredIndices)
    {
        var values = new List<string>();
        var map = new Dictionary<uint, uint>();
        foreach (var sourceIndex in requiredIndices.Distinct().OrderBy(index => index))
        {
            if (sourceIndex >= source.Count)
                throw new InvalidDataException("BPHCL TYPE record references a missing string index.");

            map[sourceIndex] = checked((uint)values.Count);
            values.Add(source[checked((int)sourceIndex)]);
        }

        return (values, map);
    }

    private static TypeTable AddBodylessBlendAliases(
        TypeTable source,
        IReadOnlyCollection<uint> roots,
        string deformerType)
    {
        var namedTypes = source.NamedTypes.ToList();
        var bodies = source.Bodies.ToList();
        var deformer = bodies.FirstOrDefault(body =>
            string.Equals(source.GetTypeName(body.TypeIndex), deformerType, StringComparison.Ordinal));
        if (deformer is null)
            return source;

        var deformerMembers = deformer.Members.ToArray();

        foreach (var (memberName, blockType) in new[]
        {
            ("fourBlendEntries", $"{deformerType}::FourBlendEntryBlock"),
            ("threeBlendEntries", $"{deformerType}::ThreeBlendEntryBlock"),
            ("twoBlendEntries", $"{deformerType}::TwoBlendEntryBlock"),
            ("oneBlendEntries", $"{deformerType}::OneBlendEntryBlock")
        })
        {
            var reflected = bodies
                .Select(body => body.TypeIndex)
                .FirstOrDefault(type => string.Equals(source.GetTypeName(type), blockType, StringComparison.Ordinal));
            if (reflected == 0 || roots.Contains(reflected))
                continue;

            var memberIndex = Array.FindIndex(deformerMembers, member =>
                member.NameIndex < source.FieldStrings.Count &&
                string.Equals(source.FieldStrings[checked((int)member.NameIndex)], memberName, StringComparison.Ordinal));
            if (memberIndex < 0)
                throw new InvalidDataException($"{deformerType} has no reflected member '{memberName}'.");

            var arrayIndex = deformerMembers[memberIndex].TypeIndex;
            var arrayType = namedTypes[checked((int)arrayIndex - 1)];
            var bodylessBlockIndex = Enumerable.Range(1, namedTypes.Count)
                .Select(index => (uint)index)
                .FirstOrDefault(type => type != reflected &&
                    string.Equals(source.GetTypeName(type), blockType, StringComparison.Ordinal) &&
                    bodies.All(body => body.TypeIndex != type));
            if (bodylessBlockIndex == 0)
            {
                bodylessBlockIndex = checked((uint)namedTypes.Count + 1);
                namedTypes.Add(namedTypes[checked((int)reflected - 1)]);
            }

            namedTypes[checked((int)arrayIndex - 1)] = arrayType with
            {
                Templates = arrayType.Templates
                    .Select(template => template.TypeIndex == reflected
                        ? template with { TypeIndex = bodylessBlockIndex }
                        : template)
                    .ToArray()
            };

            var arrayBodyIndex = bodies.FindIndex(body => body.TypeIndex == arrayIndex);
            if (arrayBodyIndex >= 0 && bodies[arrayBodyIndex].SubtypeIndex == reflected)
            {
                bodies[arrayBodyIndex] = bodies[arrayBodyIndex] with
                {
                    SubtypeIndex = bodylessBlockIndex
                };
            }
        }

        return source with
        {
            NamedTypes = namedTypes,
            Bodies = bodies
        };
    }

    private static IReadOnlyDictionary<uint, uint> BuildBlendAliases(
        TypeTable source,
        IReadOnlyCollection<uint> concreteRoots)
    {
        var aliases = new Dictionary<uint, uint>();
        foreach (var blendType in new[]
        {
            "hclObjectSpaceDeformer::FourBlendEntryBlock",
            "hclObjectSpaceDeformer::ThreeBlendEntryBlock",
            "hclObjectSpaceDeformer::TwoBlendEntryBlock",
            "hclObjectSpaceDeformer::OneBlendEntryBlock",
            "hclBoneSpaceDeformer::FourBlendEntryBlock",
            "hclBoneSpaceDeformer::ThreeBlendEntryBlock",
            "hclBoneSpaceDeformer::TwoBlendEntryBlock",
            "hclBoneSpaceDeformer::OneBlendEntryBlock"
        })
        {
            var reflected = source.Bodies
                .Select(body => body.TypeIndex)
                .Where(type => string.Equals(source.GetTypeName(type), blendType, StringComparison.Ordinal))
                .Distinct()
                .ToArray();
            if (reflected.Length != 1)
                continue;

            var concrete = reflected[0];
            var concreteType = source.NamedTypes[checked((int)concrete - 1)];
            var bodyless = Enumerable.Range(1, source.NamedTypes.Count)
                .Select(index => (uint)index)
                .Where(typeIndex =>
                    typeIndex != concrete &&
                    string.Equals(source.GetTypeName(typeIndex), blendType, StringComparison.Ordinal) &&
                    source.Bodies.All(body => body.TypeIndex != typeIndex))
                .FirstOrDefault(typeIndex =>
                {
                    var aliasType = source.NamedTypes[checked((int)typeIndex - 1)];
                    return aliasType.Templates.Count == concreteType.Templates.Count &&
                        aliasType.Templates.Zip(concreteType.Templates).All(pair =>
                            pair.First.StringIndex == pair.Second.StringIndex &&
                            pair.First.TypeIndex == pair.Second.TypeIndex);
                });
            if (bodyless == 0)
                continue;

            if (concreteRoots.Contains(concrete) || concreteRoots.Contains(bodyless))
                aliases[bodyless] = concrete;
            else
                aliases[concrete] = bodyless;
        }
        return aliases;
    }

    private static IReadOnlyList<uint> ExpandTypeDependencies(
        TypeTable source,
        IEnumerable<uint> roots,
        IReadOnlyDictionary<uint, uint>? canonicalTypes = null)
    {
        var required = new HashSet<uint>();
        var queue = new Queue<uint>();
        foreach (var root in roots)
            Enqueue(root);
        while (queue.TryDequeue(out var typeIndex))
        {
            if (!required.Add(typeIndex))
                continue;

            var named = source.NamedTypes[checked((int)typeIndex - 1)];
            foreach (var template in named.Templates)
            {
                var parameterName = source.TypeStrings[checked((int)template.StringIndex)];
                if (parameterName.StartsWith("t", StringComparison.Ordinal))
                    Enqueue(template.TypeIndex);
            }

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
            if (canonicalTypes is not null && canonicalTypes.TryGetValue(typeIndex, out var canonical))
                typeIndex = canonical;
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
                return parameterName.StartsWith("t", StringComparison.Ordinal)
                    ? $"{parameterName}={Describe(template.TypeIndex)}"
                    : $"{parameterName}={template.TypeIndex}";
            }));
            active.Remove(index);

            var body = table.Bodies.FirstOrDefault(entry => entry.TypeIndex == index);
            var baseKey = templates.Length == 0 ? typeName : $"{typeName}<{templates}>";
            if (body is null)
            {
                // Some reflected helper/forward types deliberately have a
                // name record but no layout body. Match their declared identity
                // separately from a same-named type with a concrete layout.
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
        return Parse(section, document.Bytes);
    }

    private static TypeTable Parse(byte[] typeSection)
    {
        var section = NativeBphclTagFileReader.ReadStandaloneSection(typeSection, "TYPE");
        return Parse(section, typeSection);
    }

    private static TypeTable Parse(NativeBphclSection section, byte[] bytes)
    {
        var typeStrings = ReadStrings(section.Children.FirstOrDefault(child => child.Signature is "TST1" or "TSTR"), bytes);
        var fieldStrings = ReadStrings(section.Children.FirstOrDefault(child => child.Signature is "FST1" or "FSTR"), bytes);
        var named = ReadNamedTypes(section.Children.FirstOrDefault(child => child.Signature is "TNA1" or "TNAM"), bytes);
        var bodies = ReadBodies(section.Children.FirstOrDefault(child => child.Signature is "TBDY" or "TBOD"), bytes);
        var hashes = ReadHashes(section.Children.FirstOrDefault(child => child.Signature == "THSH"), bytes);
        return new TypeTable(section, typeStrings, fieldStrings, named, bodies, hashes, bytes);
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
        public string? GetTypeName(uint typeIndex) =>
            typeIndex > 0 && typeIndex <= NamedTypes.Count
                ? TypeStrings[checked((int)NamedTypes[checked((int)typeIndex - 1)].StringIndex)]
                : null;

        public IReadOnlyList<string> GetTypeNames() =>
            Enumerable.Range(0, NamedTypes.Count + 1)
                .Select(index => index == 0 ? string.Empty : GetTypeName((uint)index) ?? string.Empty)
                .ToArray();

        public IReadOnlyList<byte[]> BuildSubsetChildren(
            IReadOnlyList<string> typeStrings,
            IReadOnlyList<string> fieldStrings,
            IReadOnlyList<NamedType> namedTypes,
            IReadOnlyList<TypeBody> bodies,
            IReadOnlyList<TypeHash> hashes)
        {
            var result = new List<byte[]>();
            foreach (var child in Section.Children)
            {
                var payload = child.Signature switch
                {
                    // TPTR is an all-zero, eight-byte slot table in retail
                    // files. It includes the null type-zero slot as well as
                    // one entry per named reflected type.
                    "TPTR" => new byte[checked((namedTypes.Count + 1) * 8)],
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
        public NamedType Remap(
            IReadOnlyDictionary<uint, uint> types,
            IReadOnlyDictionary<uint, uint> strings,
            IReadOnlyList<string> sourceStrings) =>
            new(strings[StringIndex], Templates.Select(template => template.Remap(types, strings, sourceStrings)).ToArray());
    }

    private sealed record TypeTemplate(uint StringIndex, uint TypeIndex)
    {
        public TypeTemplate Remap(
            IReadOnlyDictionary<uint, uint> types,
            IReadOnlyDictionary<uint, uint> strings,
            IReadOnlyList<string> sourceStrings)
        {
            var parameterName = sourceStrings[checked((int)StringIndex)];
            var value = parameterName.StartsWith("t", StringComparison.Ordinal)
                ? RemapTypeReference(TypeIndex, types)
                : TypeIndex;
            return new TypeTemplate(strings[StringIndex], value);
        }
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
