using System.Globalization;
using System.Text;

namespace HKCLTool;

// Diagnostic-only survey of native BPHCL reflection members. The survey uses
// each file's own TYPE table so repeated generic names keep their local layout.
public static class BphclCorpusFieldSurvey
{
    public static void WriteFromManifest(string manifestPath, string outputPath)
    {
        var paths = File.ReadAllLines(manifestPath)
            .Select(path => path.Trim())
            .Where(path => path.Length > 0 && !path.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var fields = new Dictionary<FieldKey, FieldStats>();
        var parsedFiles = 0;
        var failedFiles = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                SurveyFile(NativeBphclDocument.Open(path), path, fields);
                parsedFiles++;
            }
            catch (Exception exception)
            {
                failedFiles.Add($"{path}\t{exception.GetType().Name}: {exception.Message}");
            }
        }

        var lines = new List<string>
        {
            $"# files={parsedFiles}; failed={failedFiles.Count}; fields={fields.Count}",
            "Type\tOffset\tMember\tMemberType\tOccurrences\tNonZero\tReferences\tDistinctSamples\tExamples"
        };
        lines.AddRange(fields
            .OrderBy(entry => entry.Key.OwnerType, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.Offset)
            .ThenBy(entry => entry.Key.Member, StringComparer.Ordinal)
            .Select(entry => string.Join('\t',
                entry.Key.OwnerType,
                $"0x{entry.Key.Offset:X}",
                entry.Key.Member,
                entry.Key.MemberType,
                entry.Value.Occurrences.ToString(CultureInfo.InvariantCulture),
                entry.Value.NonZero.ToString(CultureInfo.InvariantCulture),
                entry.Value.References.ToString(CultureInfo.InvariantCulture),
                entry.Value.Samples.Count.ToString(CultureInfo.InvariantCulture),
                string.Join(" | ", entry.Value.Samples.Take(6)))));

        if (failedFiles.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("# Failures");
            lines.AddRange(failedFiles.Select(failure => $"# {failure}"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllLines(outputPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void SurveyFile(
        NativeBphclDocument document,
        string path,
        IDictionary<FieldKey, FieldStats> fields)
    {
        var data = document.DataSection
            ?? throw new InvalidDataException("BPHCL has no DATA section.");
        var definitions = document.TypeDefinitions
            .GroupBy(definition => definition.TypeIndex)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var item in document.Items.Skip(1))
        {
            if (!definitions.TryGetValue(item.TypeIndex, out var definition) || definition.Size is not > 0)
                continue;

            var members = GetMembers(definition, definitions)
                .GroupBy(member => member.Offset)
                .Select(group => group.Last())
                .OrderBy(member => member.Offset)
                .ToArray();
            if (members.Length == 0)
                continue;

            var objectSize = definition.Size.Value;
            for (uint elementIndex = 0; elementIndex < item.Count; elementIndex++)
            {
                var objectOffset = checked(item.DataOffset + elementIndex * objectSize);
                var ownerType = document.GetTypeName(item.TypeIndex) ?? $"type#{item.TypeIndex}";
                SurveyMembers(
                    document,
                    path,
                    data.PayloadOffset,
                    definitions,
                    fields,
                    ownerType,
                    objectOffset,
                    objectSize,
                    members,
                    prefix: string.Empty,
                    depth: 0);            }
        }
    }


    private static void SurveyMembers(
        NativeBphclDocument document,
        string path,
        int payloadOffset,
        IReadOnlyDictionary<uint, NativeBphclTypeDefinition> definitions,
        IDictionary<FieldKey, FieldStats> fields,
        string ownerType,
        uint objectOffset,
        uint objectSize,
        IReadOnlyList<NativeBphclTypeMember> members,
        string prefix,
        int depth)
    {
        for (var memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            var member = members[memberIndex];
            var nextOffset = memberIndex + 1 < members.Count
                ? members[memberIndex + 1].Offset
                : objectSize;
            if (nextOffset <= member.Offset)
                continue;

            var length = checked((int)(nextOffset - member.Offset));
            var fieldOffset = checked(objectOffset + member.Offset);
            var byteOffset = checked(payloadOffset + (int)fieldOffset);
            if (byteOffset < payloadOffset || byteOffset > document.Bytes.Length - length)
                continue;

            var memberType = document.GetTypeName(member.TypeIndex) ?? $"type#{member.TypeIndex}";
            var memberName = prefix.Length == 0 ? member.Name : $"{prefix}.{member.Name}";
            var key = new FieldKey(ownerType, member.Offset, memberName, memberType);
            if (!fields.TryGetValue(key, out var stats))
            {
                stats = new FieldStats();
                fields.Add(key, stats);
            }

            stats.Occurrences++;
            var bytes = document.Bytes.AsSpan(byteOffset, length);
            var isNonZero = bytes.IndexOfAnyExcept((byte)0) >= 0;
            if (isNonZero)
                stats.NonZero++;
            var isReference = document.TryGetReferencedItem(fieldOffset, out _);
            if (isReference)
                stats.References++;

            if (stats.Samples.Count < 24 && isNonZero)
            {
                var previewLength = Math.Min(length, 24);
                var sample = $"{Path.GetFileName(path)}:{Convert.ToHexString(bytes[..previewLength])}";
                stats.Samples.Add(sample);
            }

            if (depth >= 4 || isReference ||
                !definitions.TryGetValue(member.TypeIndex, out var nestedDefinition) ||
                nestedDefinition.Size is not > 0 ||
                nestedDefinition.Size.Value > length)
            {
                continue;
            }

            var nestedMembers = GetMembers(nestedDefinition, definitions)
                .GroupBy(nested => nested.Offset)
                .Select(group => group.Last())
                .OrderBy(nested => nested.Offset)
                .ToArray();
            if (nestedMembers.Length == 0)
                continue;

            SurveyMembers(
                document,
                path,
                payloadOffset,
                definitions,
                fields,
                ownerType,
                fieldOffset,
                nestedDefinition.Size.Value,
                nestedMembers,
                memberName,
                depth + 1);
        }
    }

    private static IReadOnlyList<NativeBphclTypeMember> GetMembers(
        NativeBphclTypeDefinition definition,
        IReadOnlyDictionary<uint, NativeBphclTypeDefinition> definitions)
    {
        var chain = new Stack<NativeBphclTypeDefinition>();
        var visited = new HashSet<uint>();
        var current = definition;
        while (current.TypeIndex != 0 && visited.Add(current.TypeIndex))
        {
            chain.Push(current);
            if (!definitions.TryGetValue(current.ParentTypeIndex, out current!))
                break;
        }

        return chain.SelectMany(entry => entry.Members).ToArray();
    }

    private sealed record FieldKey(string OwnerType, uint Offset, string Member, string MemberType);


    private sealed class FieldStats
    {
        public long Occurrences { get; set; }
        public long NonZero { get; set; }
        public long References { get; set; }
        public HashSet<string> Samples { get; } = new(StringComparer.Ordinal);
    }
}

