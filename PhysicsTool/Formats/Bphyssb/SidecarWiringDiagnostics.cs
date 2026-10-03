using System.Globalization;

namespace HKCLTool;

/// <summary>
/// Compares a paired TotK helper-bone file with its BotW support-bone counterpart.
/// It is diagnostic-only: it makes the output routing visible without modifying either file.
/// </summary>
internal static class SidecarWiringDiagnostics
{
    private const uint SourceBoneHash = 3407736066u;
    private const uint SourceDriverBoneHash = 249727330u;
    private const uint SourceAttributeHash = 75375380u;
    private const uint SourceKey0Hash = 1692570776u;
    private const uint SourceKey1Hash = 333816846u;
    private const uint SourceKey2Hash = 2330785204u;
    private const uint SourceConnection0Hash = 2390703269u;
    private const uint SourceConnection1Hash = 918772672u;
    private const uint TargetBoneHash = 893828983u;
    private const uint TargetBoneAttributeHash = 554147540u;
    private const uint TargetConnectionHash = 704082790u;
    private const uint TargetConnection0Hash = 2693310104u;
    private const uint TargetConnection1Hash = 3616511502u;

    private static readonly Field[] SourceFields =
    {
        new("roll", 2869652774u),
        new("bendH", 3079588026u),
        new("bendV", 122218518u),
        new("translateX", 3595547632u),
        new("translateY", 1861473429u),
        new("translateZ", 2084993915u)
    };

    private static readonly Field[] TargetFields =
    {
        new("bendH", 318199082u),
        new("bendV", 3908593737u),
        new("roll", 783626958u),
        new("translateX", 2040119991u),
        new("translateY", 245297697u),
        new("translateZ", 2543297435u)
    };

    public static IReadOnlyList<string> Compare(string bphhbPath, string bphyssbPath)
    {
        var helper = BphhbBridge.Load(bphhbPath);
        var support = BphyssbBridge.Load(bphyssbPath);
        var helperArchive = helper.Archive ?? throw new InvalidDataException("BPHHB graph could not be decoded.");
        var supportArchive = support.Archive ?? throw new InvalidDataException("BPHYSSB graph could not be decoded.");

        var helperData = FindData(helperArchive, "bone_list", "connection_curve_list", "output_list", "driven_bone_list", "pose_driven_list");
        var supportData = FindData(supportArchive, "bone_list", "connection_curve_list", "output_single_list", "support_bone_list");
        var helperBones = RequireList(helperData, "bone_list");
        var helperCurves = RequireList(helperData, "connection_curve_list");
        var helperOutputs = RequireList(helperData, "output_list");
        var helperDrivers = RequireList(helperData, "driver_bone_list");
        var helperDriven = RequireList(helperData, "driven_bone_list");
        var helperPoses = RequireList(helperData, "pose_driven_list");
        var supportBones = RequireList(supportData, "bone_list");
        var supportCurves = RequireList(supportData, "connection_curve_list");
        var supportSingles = RequireList(supportData, "output_single_list");
        var supportDoubles = supportData.FindChild("output_double_list");
        var supportDrivers = RequireList(supportData, "main_bone_list");
        var supportDriven = RequireList(supportData, "support_bone_list");

        var curveMap = MatchCurves(helperCurves, helperDrivers, helperBones, supportCurves, supportDrivers, supportBones);
        var helperOutputCurves = helperOutputs.Objects.Select(item => ReadSourceOutput(item, curveMap)).ToArray();
        var supportOutputCurves = ReadTargetOutputs(supportSingles, supportDoubles);
        var supportByBone = supportDriven.Objects
            .Select((item, index) => (Item: item, Index: index, BoneId: ReadInt(item, TargetBoneHash)))
            .Where(entry => entry.BoneId >= 0 && entry.BoneId < supportBones.Objects.Count)
            .GroupBy(entry => Normalize(ReadName(supportBones.Objects[entry.BoneId], entry.BoneId)))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var lines = new List<string>
        {
            $"helperOutputs={helperOutputs.Objects.Count};supportSingles={supportSingles.Objects.Count};supportDoubles={supportDoubles?.Objects.Count ?? 0};curveMap={curveMap.Count}/{helperCurves.Objects.Count}",
            "Curves are matched by driver bone, channel, and their three key payloads before output fields are compared."
        };

        var comparable = Math.Min(helperDriven.Objects.Count, helperPoses.Objects.Count);
        for (var index = 0; index < comparable; index++)
        {
            var helperBoneId = ReadInt(helperDriven.Objects[index], SourceBoneHash);
            if (helperBoneId < 0 || helperBoneId >= helperBones.Objects.Count)
                continue;

            var boneName = ReadName(helperBones.Objects[helperBoneId], helperBoneId);
            if (!supportByBone.TryGetValue(Normalize(boneName), out var target))
            {
                lines.Add($"bone={boneName}|support=missing");
                continue;
            }

            var targetFields = TargetFields.Select(field => new FieldOutput(
                field.Name,
                ResolveTargetOutput(ReadInt(target.Item, field.Hash), supportOutputCurves))).ToArray();
            lines.Add($"bone={boneName}|helperRecord={index}|supportRecord={target.Index}");
            foreach (var sourceField in SourceFields)
            {
                var sourceOutput = ResolveSourceOutput(ReadInt(helperPoses.Objects[index], sourceField.Hash), helperOutputCurves);
                if (sourceOutput.IsEmpty)
                    continue;

                var matches = targetFields
                    .Where(targetField => targetField.Output.Curves.SequenceEqual(sourceOutput.Curves))
                    .Select(targetField => targetField.Name)
                    .ToArray();
                lines.Add($"  {sourceField.Name}={sourceOutput.Display}|target={string.Join(',', matches.DefaultIfEmpty("none"))}");
            }
        }

        return lines;
    }

    /// <summary>
    /// Compares the raw authored keys for curves that share a driver bone and channel.
    /// This intentionally does not require the values to match first: it is used to
    /// distinguish a TotK value change from a genuinely new graph connection.
    /// </summary>
    public static IReadOnlyList<string> CompareCurveValues(string bphhbPath, string bphyssbPath)
    {
        var helperArchive = BphhbBridge.Load(bphhbPath).Archive
            ?? throw new InvalidDataException("BPHHB graph could not be decoded.");
        var supportArchive = BphyssbBridge.Load(bphyssbPath).Archive
            ?? throw new InvalidDataException("BPHYSSB graph could not be decoded.");
        var helperData = FindData(helperArchive, "bone_list", "connection_curve_list", "driver_bone_list");
        var supportData = FindData(supportArchive, "bone_list", "connection_curve_list", "main_bone_list");
        var helperBones = RequireList(helperData, "bone_list");
        var helperDrivers = RequireList(helperData, "driver_bone_list");
        var helperCurves = RequireList(helperData, "connection_curve_list");
        var supportBones = RequireList(supportData, "bone_list");
        var supportDrivers = RequireList(supportData, "main_bone_list");
        var supportCurves = RequireList(supportData, "connection_curve_list");

        var helperDriverNames = helperDrivers.Objects
            .Select(item => ResolveBoneName(item, SourceBoneHash, helperBones)).ToArray();
        var supportDriverNames = supportDrivers.Objects
            .Select(item => ResolveBoneName(item, TargetBoneHash, supportBones)).ToArray();
        var helperGroups = helperCurves.Objects.Select((curve, index) => (Curve: curve, Index: index))
            .GroupBy(entry => GetSourceCurveIdentity(entry.Curve, helperDriverNames))
            .Where(group => group.Key != null)
            .ToDictionary(group => group.Key!, group => group.OrderBy(entry => entry.Index).ToArray(), StringComparer.Ordinal);
        var supportGroups = supportCurves.Objects.Select((curve, index) => (Curve: curve, Index: index))
            .GroupBy(entry => GetTargetCurveIdentity(entry.Curve, supportDriverNames))
            .Where(group => group.Key != null)
            .ToDictionary(group => group.Key!, group => group.OrderBy(entry => entry.Index).ToArray(), StringComparer.Ordinal);

        var lines = new List<string>
        {
            $"helperCurves={helperCurves.Objects.Count};supportCurves={supportCurves.Objects.Count}",
            "Rows are paired by normalized driver bone, mapped channel, and order within that group."
        };
        foreach (var identity in helperGroups.Keys.Union(supportGroups.Keys, StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal))
        {
            helperGroups.TryGetValue(identity, out var helperEntries);
            supportGroups.TryGetValue(identity, out var supportEntries);
            helperEntries ??= Array.Empty<(AampReadObject Curve, int Index)>();
            supportEntries ??= Array.Empty<(AampReadObject Curve, int Index)>();
            var common = Math.Min(helperEntries.Length, supportEntries.Length);
            for (var index = 0; index < common; index++)
            {
                var differences = CompareCurveKeys(helperEntries[index].Curve, supportEntries[index].Curve);
                if (differences.Count > 0)
                    lines.Add($"curve={identity}|helper={helperEntries[index].Index}|support={supportEntries[index].Index}|{string.Join(";", differences)}");
            }
            if (helperEntries.Length != supportEntries.Length)
                lines.Add($"curve={identity}|helperCount={helperEntries.Length}|supportCount={supportEntries.Length}");
        }
        return lines;
    }

    private static string? GetSourceCurveIdentity(AampReadObject curve, IReadOnlyList<string> driverNames)
    {
        var driverIndex = ReadInt(curve, SourceDriverBoneHash);
        var attribute = ReadInt(curve, SourceAttributeHash);
        return driverIndex >= 0 && driverIndex < driverNames.Count && attribute >= 0
            ? $"{Normalize(driverNames[driverIndex])}|{MapSourceChannel(attribute)}"
            : null;
    }

    private static string? GetTargetCurveIdentity(AampReadObject curve, IReadOnlyList<string> driverNames)
    {
        var packed = ReadInt(curve, TargetBoneAttributeHash);
        if (packed < 0)
            return null;
        var raw = unchecked((uint)packed);
        var driverIndex = (int)(raw >> 16);
        return driverIndex >= 0 && driverIndex < driverNames.Count
            ? $"{Normalize(driverNames[driverIndex])}|{raw & 0xFFFF}"
            : null;
    }

    private static IReadOnlyList<string> CompareCurveKeys(AampReadObject helper, AampReadObject support)
    {
        var differences = new List<string>();
        foreach (var (name, hash) in new[] { ("key0", SourceKey0Hash), ("key1", SourceKey1Hash), ("key2", SourceKey2Hash) })
        {
            var left = GetParameterFingerprint(helper, hash);
            var right = GetParameterFingerprint(support, hash);
            if (!string.Equals(left, right, StringComparison.Ordinal))
                differences.Add($"{name}:changed");
        }
        var helperKeyCount = helper.Parameters.Count(parameter => parameter.Hash is SourceKey0Hash or SourceKey1Hash or SourceKey2Hash or 4260087074u);
        if (helperKeyCount != 3)
            differences.Add($"totkKeys={helperKeyCount}");
        return differences;
    }
    private static IReadOnlyDictionary<int, int> MatchCurves(
        AampReadList helperCurves,
        AampReadList helperDrivers,
        AampReadList helperBones,
        AampReadList supportCurves,
        AampReadList supportDrivers,
        AampReadList supportBones)
    {
        var helperDriverNames = helperDrivers.Objects
            .Select(item => ResolveBoneName(item, SourceBoneHash, helperBones))
            .ToArray();
        var supportDriverNames = supportDrivers.Objects
            .Select(item => ResolveBoneName(item, TargetBoneHash, supportBones))
            .ToArray();

        var targetBySignature = supportCurves.Objects
            .Select((curve, index) => (Index: index, Signature: GetTargetCurveSignature(curve, supportDriverNames)))
            .Where(item => item.Signature != null)
            .GroupBy(item => item.Signature!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => new Queue<int>(group.Select(item => item.Index)), StringComparer.Ordinal);

        var result = new Dictionary<int, int>();
        for (var index = 0; index < helperCurves.Objects.Count; index++)
        {
            var signature = GetSourceCurveSignature(helperCurves.Objects[index], helperDriverNames);
            if (signature != null && targetBySignature.TryGetValue(signature, out var candidates) && candidates.Count > 0)
                result[index] = candidates.Dequeue();
        }

        return result;
    }

    private static string? GetSourceCurveSignature(AampReadObject curve, IReadOnlyList<string> driverNames)
    {
        var driverIndex = ReadInt(curve, SourceDriverBoneHash);
        var attribute = ReadInt(curve, SourceAttributeHash);
        if (driverIndex < 0 || driverIndex >= driverNames.Count || attribute < 0)
            return null;

        return BuildCurveSignature(driverNames[driverIndex], MapSourceChannel(attribute), curve);
    }

    private static string? GetTargetCurveSignature(AampReadObject curve, IReadOnlyList<string> driverNames)
    {
        var packed = ReadInt(curve, TargetBoneAttributeHash);
        if (packed < 0)
            return null;

        var raw = unchecked((uint)packed);
        var driverIndex = (int)(raw >> 16);
        var channel = (int)(raw & 0xFFFF);
        if (driverIndex < 0 || driverIndex >= driverNames.Count)
            return null;

        return BuildCurveSignature(driverNames[driverIndex], channel, curve);
    }

    private static string BuildCurveSignature(string driverName, int channel, AampReadObject curve) =>
        $"{Normalize(driverName)}|{channel}|{GetParameterFingerprint(curve, SourceKey0Hash)}|{GetParameterFingerprint(curve, SourceKey1Hash)}|{GetParameterFingerprint(curve, SourceKey2Hash)}";

    private static string GetParameterFingerprint(AampReadObject item, uint hash) =>
        item.Find(hash) is { } parameter
            ? $"{parameter.Type}:{Convert.ToBase64String(parameter.Bytes)}"
            : "-";

    private static string ResolveBoneName(AampReadObject item, uint boneHash, AampReadList bones)
    {
        var boneId = ReadInt(item, boneHash);
        return boneId >= 0 && boneId < bones.Objects.Count
            ? ReadName(bones.Objects[boneId], boneId)
            : $"bone_{boneId}";
    }

    private static int MapSourceChannel(int attribute) => attribute switch
    {
        0 => 2,
        1 => 0,
        2 => 1,
        3 => 3,
        4 => 4,
        5 => 5,
        _ => attribute
    };

    private static AampReadList FindData(AampReadArchive archive, params string[] requiredLists) =>
        archive.Root.Children.FirstOrDefault(child => requiredLists.All(name => child.FindChild(name) != null))
        ?? throw new InvalidDataException("The AAMP sidecar does not contain the expected helper/support graph.");

    private static AampReadList RequireList(AampReadList parent, string name) =>
        parent.FindChild(name) ?? throw new InvalidDataException($"The AAMP sidecar is missing '{name}'.");

    private static IReadOnlyDictionary<int, OutputCurves> ReadTargetOutputs(AampReadList singles, AampReadList? doubles)
    {
        var result = new Dictionary<int, OutputCurves>();
        for (var index = 0; index < singles.Objects.Count; index++)
        {
            var curve = DecodeCurveReference(ReadInt(singles.Objects[index], TargetConnectionHash));
            result[index] = curve < 0 ? OutputCurves.Empty : new OutputCurves(curve);
        }

        if (doubles != null)
        {
            for (var index = 0; index < doubles.Objects.Count; index++)
            {
                var first = DecodeCurveReference(ReadInt(doubles.Objects[index], TargetConnection0Hash));
                var second = DecodeCurveReference(ReadInt(doubles.Objects[index], TargetConnection1Hash));
                result[EncodeDoubleReference(index)] = new OutputCurves(first, second);
            }
        }

        return result;
    }

    private static OutputCurves ReadSourceOutput(AampReadObject output, IReadOnlyDictionary<int, int> curveMap) => new(
        MapCurve(ReadInt(output, SourceConnection0Hash), curveMap),
        MapCurve(ReadInt(output, SourceConnection1Hash), curveMap));

    private static int MapCurve(int sourceCurve, IReadOnlyDictionary<int, int> curveMap) =>
        sourceCurve >= 0 && curveMap.TryGetValue(sourceCurve, out var targetCurve)
            ? targetCurve
            : -1;

    private static OutputCurves ResolveSourceOutput(int outputIndex, IReadOnlyList<OutputCurves> outputs) =>
        outputIndex >= 0 && outputIndex < outputs.Count ? outputs[outputIndex] : OutputCurves.Empty;

    private static OutputCurves ResolveTargetOutput(int outputReference, IReadOnlyDictionary<int, OutputCurves> outputs) =>
        outputReference >= 0 && outputs.TryGetValue(outputReference, out var output) ? output : OutputCurves.Empty;

    private static int DecodeCurveReference(int reference) =>
        reference < 0 ? -1 : ((uint)reference & 0xFFFF0000u) == 0x00010000u ? reference & 0xFFFF : reference;

    private static int EncodeDoubleReference(int index) => 0x00010000 | index;

    private static int ReadInt(AampReadObject item, uint hash) =>
        item.Find(hash) is { Type: 2 } value ? value.AsInt32() : -1;

    private static string ReadName(AampReadObject item, int fallbackIndex) =>
        item.Find("name")?.AsString() ?? $"bone_{fallbackIndex}";

    private static string Normalize(string name) =>
        name.StartsWith("Link:", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;

    private sealed record Field(string Name, uint Hash);
    private sealed record FieldOutput(string Name, OutputCurves Output);

    private readonly record struct OutputCurves(int First, int Second = -1)
    {
        public static OutputCurves Empty { get; } = new(-1);
        public bool IsEmpty => First < 0 && Second < 0;
        public int[] Curves => new[] { First, Second }.Where(value => value >= 0).OrderBy(value => value).ToArray();
        public string Display => IsEmpty
            ? "none"
            : $"[{string.Join(',', Curves.Select(value => value.ToString(CultureInfo.InvariantCulture)))}]";
    }
}
