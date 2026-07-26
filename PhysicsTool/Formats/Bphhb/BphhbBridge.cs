using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HKCLTool;

/// <summary>
/// Native, format-specific entry point for TotK helper-bone AAMP files.
/// BPHHB drives skeleton helper bones and is deliberately separate from BPHCL.
/// </summary>
public sealed class BphhbDocumentSummary
{
    internal byte[] Bytes { get; init; } = Array.Empty<byte>();
    public string SourcePath { get; init; } = string.Empty;
    public int FileSize { get; init; }
    public uint ArchiveVersion { get; init; }
    public uint FormatVersion { get; init; }
    public uint ParameterIoVersion { get; init; }
    public uint ListCount { get; init; }
    public uint ObjectCount { get; init; }
    public uint ParameterCount { get; init; }
    public uint DataSize { get; init; }
    public uint StringPoolSize { get; init; }
    public IReadOnlyList<string> HelperBoneNames { get; init; } = Array.Empty<string>();
    public BphhbGraphSummary? Graph { get; init; }
    internal AampReadArchive? Archive { get; init; }
}

/// <summary>Named TotK helper-bone relationships decoded from the phhb AAMP.</summary>
public sealed class BphhbGraphSummary
{
    public IReadOnlyList<string> Bones { get; init; } = Array.Empty<string>();
    public IReadOnlyList<BphhbDriverBone> Drivers { get; init; } = Array.Empty<BphhbDriverBone>();
    public IReadOnlyList<BphhbDrivenBone> Driven { get; init; } = Array.Empty<BphhbDrivenBone>();
    public IReadOnlyList<BphhbPoseDrivenBone> PoseDriven { get; init; } = Array.Empty<BphhbPoseDrivenBone>();
    public IReadOnlyList<BphhbOutputLink> Outputs { get; init; } = Array.Empty<BphhbOutputLink>();
    public int CurveCount { get; init; }
}

public sealed record BphhbDriverBone(int BoneId, int BaseBoneId, Vector3 Translation, Vector4 Rotation);
public sealed record BphhbDrivenBone(int BoneId, int TranslateDrivenId, int RotateDrivenId);
public sealed record BphhbPoseDrivenBone(Vector3 Translation, Vector4 Rotation, int BaseBoneId);
public sealed record BphhbOutputLink(int Connection0, int? Connection1);

public static class BphhbBridge
{
    private const int HeaderSize = 0x30;
    private const uint BoneIdHash = 3407736066u;
    private const uint BaseBoneIdHash = 4279981158u;
    private const uint DriverListHash = 3993686733u;
    private const uint DrivenListHash = 1256621249u;
    private const uint PoseDrivenListHash = 1386686749u;

    public static BphhbDocumentSummary Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return LoadBytes(bytes, Path.GetFullPath(path));
    }

    internal static BphhbDocumentSummary LoadBytes(byte[] bytes, string sourcePath)
    {
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException("BPHHB is shorter than an AAMP header.");
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "AAMP")
            throw new InvalidDataException("BPHHB must begin with the AAMP signature.");

        var archiveVersion = ReadUInt32(bytes, 0x04);
        var formatVersion = ReadUInt32(bytes, 0x08);
        var declaredSize = ReadUInt32(bytes, 0x0C);
        var parameterIoVersion = ReadUInt32(bytes, 0x10);
        var parameterIoOffset = ReadUInt32(bytes, 0x14);
        var listCount = ReadUInt32(bytes, 0x18);
        var objectCount = ReadUInt32(bytes, 0x1C);
        var parameterCount = ReadUInt32(bytes, 0x20);
        var dataSize = ReadUInt32(bytes, 0x24);
        var stringPoolSize = ReadUInt32(bytes, 0x28);

        if (declaredSize != bytes.Length)
            throw new InvalidDataException($"AAMP header declares {declaredSize} bytes, but the file contains {bytes.Length} bytes.");
        if (HeaderSize + parameterIoOffset >= bytes.Length)
            throw new InvalidDataException("AAMP parameter root offset lies outside the file.");
        if (stringPoolSize > bytes.Length)
            throw new InvalidDataException("AAMP string pool lies outside the file.");

        var type = ReadZeroTerminatedUtf8(bytes, HeaderSize, Math.Min(bytes.Length, HeaderSize + (int)parameterIoOffset));
        if (!string.Equals(type, "phhb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected an AAMP helper-bone file (type phhb), found '{type}'.");

        AampReadArchive? archive = null;
        BphhbGraphSummary? graph = null;
        try
        {
            archive = BotwAampReader.Read(bytes);
            graph = ReadGraph(archive);
        }
        catch (InvalidDataException)
        {
            // Keep the basic inspector available for a future AAMP type we
            // do not yet decode. Editing remains disabled without the graph.
        }

        return new BphhbDocumentSummary
        {
            Bytes = bytes,
            SourcePath = sourcePath,
            FileSize = bytes.Length,
            ArchiveVersion = archiveVersion,
            FormatVersion = formatVersion,
            ParameterIoVersion = parameterIoVersion,
            ListCount = listCount,
            ObjectCount = objectCount,
            ParameterCount = parameterCount,
            DataSize = dataSize,
            StringPoolSize = stringPoolSize,
            HelperBoneNames = graph?.Bones.Count > 0 ? graph.Bones : ReadStringPool(bytes, stringPoolSize),
            Graph = graph,
            Archive = archive
        };
    }

    public static BphhbDocumentSummary Save(BphhbDocumentSummary document, string outputPath)
    {
        if (!Path.GetExtension(outputPath).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BPHHB documents must be saved with the .bphhb extension.");

        File.WriteAllBytes(outputPath, document.Bytes);
        return Load(outputPath);
    }

    /// <summary>
    /// Updates the named helper-bone record and its matching base pose records.
    /// BPHHB carries only the helper graph, not the complete actor skeleton.
    /// </summary>
    public static BphhbDocumentSummary UpdateBone(
        BphhbDocumentSummary document,
        int boneIndex,
        string name,
        int baseBoneIndex,
        Vector3 translation,
        Vector4 rotation)
    {
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var boneList = RequireList(data, "bone_list");
            if (boneIndex < 0 || boneIndex >= boneList.Objects.Count)
                throw new ArgumentOutOfRangeException(nameof(boneIndex));

            SetString(boneList.Objects[boneIndex], "name", name);

            var drivers = FindList(data, DriverListHash);
            foreach (var driver in drivers?.Objects.Where(item => GetInt(item, BoneIdHash) == boneIndex) ?? Enumerable.Empty<BotwAampWriter.AampObjectNode>())
            {
                SetInt(driver, BaseBoneIdHash, baseBoneIndex);
                SetVector3(driver, "base_translate", translation);
                SetVector4(driver, "base_rotate", rotation);
            }

            var driven = FindList(data, DrivenListHash);
            var pose = FindList(data, PoseDrivenListHash);
            if (driven != null && pose != null)
            {
                for (var index = 0; index < Math.Min(driven.Objects.Count, pose.Objects.Count); index++)
                {
                    if (GetInt(driven.Objects[index], BoneIdHash) != boneIndex)
                        continue;

                    var poseObject = pose.Objects[index];
                    SetInt(poseObject, BaseBoneIdHash, baseBoneIndex);
                    SetVector3(poseObject, "base_translate", translation);
                    SetVector4(poseObject, "base_rotate", rotation);
                }
            }
        });
    }

    /// <summary>
    /// Adds a new helper-bone graph by cloning an existing one. A blank BPHHB
    /// node would have no curve/driver context and is substantially less useful.
    /// </summary>
    public static BphhbDocumentSummary DuplicateBone(BphhbDocumentSummary document, int sourceBoneIndex, string? requestedName = null)
    {
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var boneList = RequireList(data, "bone_list");
            if (sourceBoneIndex < 0 || sourceBoneIndex >= boneList.Objects.Count)
                throw new ArgumentOutOfRangeException(nameof(sourceBoneIndex));

            var newBoneIndex = boneList.Objects.Count;
            var sourceName = GetString(boneList.Objects[sourceBoneIndex], "name") ?? $"HelperBone_{sourceBoneIndex}";
            var newName = string.IsNullOrWhiteSpace(requestedName)
                ? GetUniqueBoneName(boneList, sourceName)
                : requestedName.Trim();

            var newBone = CloneObject(boneList.Objects[sourceBoneIndex], $"bone_{newBoneIndex}");
            SetString(newBone, "name", newName);
            boneList.Objects.Add(newBone);

            var drivers = FindList(data, DriverListHash);
            CloneBoneRecord(drivers, sourceBoneIndex, newBoneIndex, BoneIdHash, "driver_bone");

            var driven = FindList(data, DrivenListHash);
            var pose = FindList(data, PoseDrivenListHash);
            var outputs = FindList(data, BotwAampWriter.Crc32("output_list"));
            var curves = FindList(data, BotwAampWriter.Crc32("connection_curve_list"));
            var outputMap = new Dictionary<int, int>();
            var curveMap = new Dictionary<int, int>();
            if (driven != null)
            {
                var sourceDriven = driven.Objects
                    .Select((item, index) => (item, index))
                    .Where(entry => GetInt(entry.item, BoneIdHash) == sourceBoneIndex)
                    .ToArray();
                foreach (var entry in sourceDriven)
                {
                    var clonedDriven = CloneObject(entry.item, $"driven_bone_{driven.Objects.Count}");
                    SetInt(clonedDriven, BoneIdHash, newBoneIndex);
                    CloneDrivenOutput(clonedDriven, BotwAampWriter.Crc32("translate_driven_id"), outputs, curves, outputMap, curveMap, sourceBoneIndex, newBoneIndex);
                    CloneDrivenOutput(clonedDriven, BotwAampWriter.Crc32("rotate_driven_id"), outputs, curves, outputMap, curveMap, sourceBoneIndex, newBoneIndex);
                    driven.Objects.Add(clonedDriven);

                    if (pose != null && entry.index < pose.Objects.Count)
                        pose.Objects.Add(CloneObject(pose.Objects[entry.index], $"pose_driven_{pose.Objects.Count}"));
                }
            }
        });
    }

    /// <summary>
    /// Moves a helper-bone list entry and rewrites every bone-index reference
    /// that defines the helper graph. Output and curve IDs stay stable because
    /// they address their own lists, not the bone list.
    /// </summary>
    public static BphhbDocumentSummary MoveBone(BphhbDocumentSummary document, int sourceBoneIndex, int destinationBoneIndex)
    {
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var boneList = RequireList(data, "bone_list");
            if (sourceBoneIndex < 0 || sourceBoneIndex >= boneList.Objects.Count ||
                destinationBoneIndex < 0 || destinationBoneIndex >= boneList.Objects.Count)
                throw new ArgumentOutOfRangeException(nameof(destinationBoneIndex));
            if (sourceBoneIndex == destinationBoneIndex)
                return;

            var originalOrder = Enumerable.Range(0, boneList.Objects.Count).ToList();
            var movedIndex = originalOrder[sourceBoneIndex];
            originalOrder.RemoveAt(sourceBoneIndex);
            originalOrder.Insert(destinationBoneIndex, movedIndex);

            var remap = new int[originalOrder.Count];
            for (var newIndex = 0; newIndex < originalOrder.Count; newIndex++)
                remap[originalOrder[newIndex]] = newIndex;

            var movedBone = boneList.Objects[sourceBoneIndex];
            boneList.Objects.RemoveAt(sourceBoneIndex);
            boneList.Objects.Insert(destinationBoneIndex, movedBone);
            NormalizeIndexedObjectNames(boneList, "bone");

            RemapBoneIds(FindList(data, DriverListHash)?.Objects, remap, BoneIdHash, BaseBoneIdHash);
            RemapBoneIds(FindList(data, DrivenListHash)?.Objects, remap, BoneIdHash);
            RemapBoneIds(FindList(data, PoseDrivenListHash)?.Objects, remap, BaseBoneIdHash);
            RemapBoneIds(FindList(data, BotwAampWriter.Crc32("connection_curve_list"))?.Objects, remap, BotwAampWriter.Crc32("driver_bone_id"));
        });
    }

    public static BphhbDocumentSummary AddBone(BphhbDocumentSummary document, int preferredSourceIndex)
    {
        var sourceIndex = document.Graph?.Bones.Count > 0
            ? Math.Clamp(preferredSourceIndex, 0, document.Graph.Bones.Count - 1)
            : throw new InvalidOperationException("This helper-bone file has no source bone to clone.");
        return DuplicateBone(document, sourceIndex, "HelperBone_" + (document.Graph!.Bones.Count).ToString());
    }

    /// <summary>Reflects the helper graph in its own X axis and swaps L/R name tokens.</summary>
    public static BphhbDocumentSummary MirrorBonesAcrossX(BphhbDocumentSummary document)
    {
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var boneList = RequireList(data, "bone_list");
            foreach (var bone in boneList.Objects)
            {
                var name = GetString(bone, "name");
                if (!string.IsNullOrWhiteSpace(name))
                    SetString(bone, "name", SwapSideTokens(name));
            }

            MirrorTransformRecords(FindList(data, DriverListHash)?.Objects ?? Enumerable.Empty<BotwAampWriter.AampObjectNode>());
            MirrorTransformRecords(FindList(data, PoseDrivenListHash)?.Objects ?? Enumerable.Empty<BotwAampWriter.AampObjectNode>());
        });
    }

    /// <summary>Reflects one selected helper-bone base pose across its X axis.</summary>
    public static BphhbDocumentSummary MirrorBoneAcrossX(BphhbDocumentSummary document, int boneIndex)
    {
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var boneList = RequireList(data, "bone_list");
            if (boneIndex < 0 || boneIndex >= boneList.Objects.Count)
                throw new ArgumentOutOfRangeException(nameof(boneIndex));

            var bone = boneList.Objects[boneIndex];
            var name = GetString(bone, "name");
            if (!string.IsNullOrWhiteSpace(name))
                SetString(bone, "name", SwapSideTokens(name));

            MirrorTransformRecords(FindList(data, DriverListHash)?.Objects
                .Where(item => GetInt(item, BoneIdHash) == boneIndex)
                ?? Enumerable.Empty<BotwAampWriter.AampObjectNode>());

            var driven = FindList(data, DrivenListHash);
            var pose = FindList(data, PoseDrivenListHash);
            if (driven != null && pose != null)
            {
                for (var index = 0; index < Math.Min(driven.Objects.Count, pose.Objects.Count); index++)
                {
                    if (GetInt(driven.Objects[index], BoneIdHash) == boneIndex)
                        MirrorTransformRecords(new[] { pose.Objects[index] });
                }
            }
        });
    }

    public static JObject ExportSummary(BphhbDocumentSummary summary) => new()
    {
        ["format"] = "BPHHBTool.Readable",
        ["version"] = 1,
        ["notes"] = "BPHHB helper-bone AAMP is edited through its native graph. Only helper-bone names and base-pose records are currently authorable.",
        ["bphhb"] = new JObject
        {
            ["sourcePath"] = summary.SourcePath,
            ["fileSize"] = summary.FileSize,
            ["archiveVersion"] = summary.ArchiveVersion,
            ["parameterIoVersion"] = summary.ParameterIoVersion,
            ["listCount"] = summary.ListCount,
            ["objectCount"] = summary.ObjectCount,
            ["parameterCount"] = summary.ParameterCount,
            ["dataSize"] = summary.DataSize,
            ["stringPoolSize"] = summary.StringPoolSize,
            ["helperBones"] = new JArray(summary.HelperBoneNames),
            ["graph"] = summary.Graph == null ? null : new JObject
            {
                ["curveCount"] = summary.Graph.CurveCount,
                ["drivers"] = new JArray(summary.Graph.Drivers.Select(driver => new JObject
                {
                    ["boneId"] = driver.BoneId,
                    ["bone"] = GetBoneName(summary.Graph.Bones, driver.BoneId),
                    ["baseBoneId"] = driver.BaseBoneId,
                    ["baseBone"] = GetBoneName(summary.Graph.Bones, driver.BaseBoneId)
                })),
                ["driven"] = new JArray(summary.Graph.Driven.Select(driven => new JObject
                {
                    ["boneId"] = driven.BoneId,
                    ["bone"] = GetBoneName(summary.Graph.Bones, driven.BoneId),
                    ["translateDrivenId"] = driven.TranslateDrivenId,
                    ["rotateDrivenId"] = driven.RotateDrivenId
                })),
                ["poseDriven"] = new JArray(summary.Graph.PoseDriven.Select(pose => new JObject
                {
                    ["baseBoneId"] = pose.BaseBoneId,
                    ["baseBone"] = GetBoneName(summary.Graph.Bones, pose.BaseBoneId),
                    ["translation"] = new JArray(pose.Translation.X, pose.Translation.Y, pose.Translation.Z),
                    ["rotation"] = new JArray(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W)
                })),
                ["outputs"] = new JArray(summary.Graph.Outputs.Select(output => new JObject
                {
                    ["connection0"] = output.Connection0,
                    ["connection1"] = output.Connection1
                }))
            }
        }
    };

    private static BphhbGraphSummary? ReadGraph(AampReadArchive archive)
    {
        if (!string.Equals(archive.Type, "phhb", StringComparison.OrdinalIgnoreCase))
            return null;

        var data = archive.Root.Children.SingleOrDefault();
        if (data == null)
            return null;

        var boneList = data.FindChild("bone_list");
            var curveList = data.FindChild("connection_curve_list");
            var outputList = data.FindChild(1224233410u);
            var driverList = data.FindChild(3993686733u);
            var drivenList = data.FindChild(1256621249u);
        if (boneList == null || curveList == null || outputList == null || driverList == null || drivenList == null)
            return null;

        var bones = boneList.Objects
                .Select(item => item.Find("name")?.AsString() ?? string.Empty)
                .ToArray();
        var drivers = driverList.Objects.Select(item => new BphhbDriverBone(
                item.Find(3407736066u)?.AsInt32() ?? -1,
                item.Find(4279981158u)?.AsInt32() ?? -1,
                item.Find("base_translate")?.AsVector3() ?? Vector3.Zero,
                item.Find("base_rotate")?.AsVector4() ?? Vector4.UnitW)).ToArray();
        var driven = drivenList.Objects.Select(item => new BphhbDrivenBone(
                item.Find(3407736066u)?.AsInt32() ?? -1,
                item.Find(2041202863u)?.AsInt32() ?? -1,
                item.Find(4080663648u)?.AsInt32() ?? -1)).ToArray();
        var poseList = data.FindChild(1386686749u);
        var poseDriven = poseList?.Objects.Select(item => new BphhbPoseDrivenBone(
                item.Find("base_translate")?.AsVector3() ?? Vector3.Zero,
                item.Find("base_rotate")?.AsVector4() ?? Vector4.UnitW,
                item.Find(4279981158u)?.AsInt32() ?? -1)).ToArray() ?? Array.Empty<BphhbPoseDrivenBone>();
        var outputs = outputList.Objects.Select(item => new BphhbOutputLink(
                item.Find(2390703269u)?.AsInt32() ?? -1,
                item.Find(918772672u)?.AsInt32())).ToArray();

        return new BphhbGraphSummary
        {
            Bones = bones,
            CurveCount = curveList.Objects.Count,
            Drivers = drivers,
            Driven = driven,
            PoseDriven = poseDriven,
            Outputs = outputs
        };
    }

    private static BphhbDocumentSummary Rebuild(BphhbDocumentSummary document, Action<BotwAampWriter.AampListNode> edit)
    {
        var archive = document.Archive
            ?? throw new InvalidOperationException("This BPHHB file could not be decoded into an editable AAMP graph.");
        var root = CloneList(archive.Root);
        edit(root);
        SynchronizeHeaderCounts(root);
        var bytes = BotwAampWriter.WriteArchive(
            archive.Type,
            root,
            document.ArchiveVersion,
            document.FormatVersion,
            document.ParameterIoVersion);
        return LoadBytes(bytes, document.SourcePath);
    }

    private static BotwAampWriter.AampListNode CloneList(AampReadList source)
    {
        var result = new BotwAampWriter.AampListNode(string.Empty, source.Hash);
        foreach (var child in source.Children)
            result.Children.Add(CloneList(child));
        foreach (var item in source.Objects)
            result.Objects.Add(CloneObject(item));
        return result;
    }

    private static BotwAampWriter.AampObjectNode CloneObject(AampReadObject source)
    {
        var result = new BotwAampWriter.AampObjectNode(string.Empty, source.Hash);
        foreach (var parameter in source.Parameters)
            result.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(parameter.Hash, parameter.Type, parameter.Bytes));
        return result;
    }

    private static BotwAampWriter.AampObjectNode CloneObject(BotwAampWriter.AampObjectNode source, string? replacementName = null)
    {
        var result = replacementName == null
            ? new BotwAampWriter.AampObjectNode(string.Empty, source.NameHash)
            : new BotwAampWriter.AampObjectNode(replacementName);
        foreach (var parameter in source.Parameters)
            result.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(
                parameter.NameHash ?? BotwAampWriter.Crc32(parameter.Name),
                parameter.Type,
                parameter.RawBytes ?? parameter.ToBinary()));
        return result;
    }

    private static BotwAampWriter.AampListNode RequireDataRoot(BotwAampWriter.AampListNode root) =>
        root.Children.Count == 1
            ? root.Children[0]
            : throw new InvalidOperationException("The BPHHB archive does not contain one editable data root.");

    private static BotwAampWriter.AampListNode RequireList(BotwAampWriter.AampListNode parent, string name) =>
        FindList(parent, BotwAampWriter.Crc32(name))
        ?? throw new InvalidOperationException($"The BPHHB graph does not contain '{name}'.");

    private static BotwAampWriter.AampListNode? FindList(BotwAampWriter.AampListNode parent, uint hash) =>
        parent.Children.FirstOrDefault(child => child.NameHash == hash);

    // The top-level phhb object carries redundant list totals. They must agree
    // with the actual lists after a duplicate, otherwise game-side readers can
    // walk past the intended graph.
    private static void SynchronizeHeaderCounts(BotwAampWriter.AampListNode root)
    {
        if (root.Objects.Count == 0 || root.Children.Count != 1)
            return;

        var header = root.Objects[0];
        var data = root.Children[0];
        SetExistingInt(header, "bone_num", FindList(data, BotwAampWriter.Crc32("bone_list"))?.Objects.Count ?? 0);
        SetExistingInt(header, "connection_curve_num", FindList(data, BotwAampWriter.Crc32("connection_curve_list"))?.Objects.Count ?? 0);
        SetExistingInt(header, "output_num", FindList(data, BotwAampWriter.Crc32("output_list"))?.Objects.Count ?? 0);
        SetExistingInt(header, "driver_bone_num", FindList(data, DriverListHash)?.Objects.Count ?? 0);
        SetExistingInt(header, "driven_bone_num", FindList(data, DrivenListHash)?.Objects.Count ?? 0);
        SetExistingInt(header, "pose_driven_num", FindList(data, PoseDrivenListHash)?.Objects.Count ?? 0);
        SetExistingInt(header, "position_driven_num", FindList(data, BotwAampWriter.Crc32("position_driven_list"))?.Objects.Count ?? 0);
        SetExistingInt(header, "rotate_driven_num", FindList(data, BotwAampWriter.Crc32("rotate_driven_list"))?.Objects.Count ?? 0);
        SetExistingInt(header, "aim_driven_num", FindList(data, BotwAampWriter.Crc32("aim_driven_list"))?.Objects.Count ?? 0);
    }

    private static void CloneBoneRecord(
        BotwAampWriter.AampListNode? list,
        int sourceBoneIndex,
        int newBoneIndex,
        uint boneHash,
        string objectPrefix)
    {
        if (list == null)
            return;
        foreach (var item in list.Objects.Where(item => GetInt(item, boneHash) == sourceBoneIndex).ToArray())
        {
            var cloned = CloneObject(item, $"{objectPrefix}_{list.Objects.Count}");
            SetInt(cloned, boneHash, newBoneIndex);
            list.Objects.Add(cloned);
        }
    }

    private static void CloneDrivenOutput(
        BotwAampWriter.AampObjectNode driven,
        uint drivenIdHash,
        BotwAampWriter.AampListNode? outputs,
        BotwAampWriter.AampListNode? curves,
        IDictionary<int, int> outputMap,
        IDictionary<int, int> curveMap,
        int sourceBoneIndex,
        int newBoneIndex)
    {
        var sourceOutputIndex = GetInt(driven, drivenIdHash);
        if (sourceOutputIndex < 0 || outputs == null || sourceOutputIndex >= outputs.Objects.Count)
            return;

        if (!outputMap.TryGetValue(sourceOutputIndex, out var newOutputIndex))
        {
            var sourceOutput = outputs.Objects[sourceOutputIndex];
            var clonedOutput = CloneObject(sourceOutput, $"output_{outputs.Objects.Count}");
            newOutputIndex = outputs.Objects.Count;
            CloneOutputCurve(clonedOutput, BotwAampWriter.Crc32("connection_0_id"), curves, curveMap, sourceBoneIndex, newBoneIndex);
            CloneOutputCurve(clonedOutput, BotwAampWriter.Crc32("connection_1_id"), curves, curveMap, sourceBoneIndex, newBoneIndex);
            outputs.Objects.Add(clonedOutput);
            outputMap.Add(sourceOutputIndex, newOutputIndex);
        }

        SetInt(driven, drivenIdHash, newOutputIndex);
    }

    private static void CloneOutputCurve(
        BotwAampWriter.AampObjectNode output,
        uint connectionIdHash,
        BotwAampWriter.AampListNode? curves,
        IDictionary<int, int> curveMap,
        int sourceBoneIndex,
        int newBoneIndex)
    {
        var sourceCurveIndex = GetInt(output, connectionIdHash);
        if (sourceCurveIndex < 0 || curves == null || sourceCurveIndex >= curves.Objects.Count)
            return;

        if (!curveMap.TryGetValue(sourceCurveIndex, out var newCurveIndex))
        {
            var clonedCurve = CloneObject(curves.Objects[sourceCurveIndex], $"connection_curve_{curves.Objects.Count}");
            var driverBoneIdHash = BotwAampWriter.Crc32("driver_bone_id");
            if (GetInt(clonedCurve, driverBoneIdHash) == sourceBoneIndex)
                SetInt(clonedCurve, driverBoneIdHash, newBoneIndex);
            newCurveIndex = curves.Objects.Count;
            curves.Objects.Add(clonedCurve);
            curveMap.Add(sourceCurveIndex, newCurveIndex);
        }

        SetInt(output, connectionIdHash, newCurveIndex);
    }

    private static void RemapBoneIds(
        IEnumerable<BotwAampWriter.AampObjectNode>? objects,
        IReadOnlyList<int> remap,
        params uint[] parameterHashes)
    {
        if (objects == null)
            return;

        foreach (var item in objects)
        {
            foreach (var parameterHash in parameterHashes)
            {
                var oldIndex = GetInt(item, parameterHash);
                if (oldIndex >= 0 && oldIndex < remap.Count)
                    SetInt(item, parameterHash, remap[oldIndex]);
            }
        }
    }

    // AAMP list-object hashes are not references, but vanilla phhb files name
    // their bone entries bone_0, bone_1, ... . Reassigning those labels after
    // a move keeps YAML/debug views aligned with the new positional indices.
    private static void NormalizeIndexedObjectNames(BotwAampWriter.AampListNode list, string prefix)
    {
        var renamed = list.Objects
            .Select((item, index) => CloneObject(item, $"{prefix}_{index}"))
            .ToArray();
        list.Objects.Clear();
        list.Objects.AddRange(renamed);
    }

    private static void MirrorTransformRecords(IEnumerable<BotwAampWriter.AampObjectNode> records)
    {
        foreach (var record in records)
        {
            var translation = GetVector3(record, "base_translate");
            if (translation.HasValue)
            {
                var mirrored = translation.Value;
                mirrored.X = -mirrored.X;
                SetVector3(record, "base_translate", mirrored);
            }

            var rotation = GetVector4(record, "base_rotate");
            if (rotation.HasValue)
                SetVector4(record, "base_rotate", MirrorQuaternionAcrossX(rotation.Value));
        }
    }

    private static Vector4 MirrorQuaternionAcrossX(Vector4 rotation)
    {
        var quaternion = Quaternion.Normalize(new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W));
        var reflection = Matrix4x4.CreateScale(-1f, 1f, 1f);
        var mirroredMatrix = reflection * Matrix4x4.CreateFromQuaternion(quaternion) * reflection;
        if (Matrix4x4.Decompose(mirroredMatrix, out _, out var mirrored, out _))
            return new Vector4(mirrored.X, mirrored.Y, mirrored.Z, mirrored.W);

        return new Vector4(rotation.X, -rotation.Y, -rotation.Z, rotation.W);
    }

    private static string GetUniqueBoneName(BotwAampWriter.AampListNode boneList, string sourceName)
    {
        var used = boneList.Objects
            .Select(item => GetString(item, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        for (var number = 1; ; number++)
        {
            var candidate = sourceName + "_" + number;
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    private static int? FindParameterIndex(BotwAampWriter.AampObjectNode item, uint hash) =>
        item.Parameters.FindIndex(parameter => (parameter.NameHash ?? BotwAampWriter.Crc32(parameter.Name)) == hash) is var index && index >= 0
            ? index
            : null;

    private static BotwAampWriter.AampParameterNode? FindParameter(BotwAampWriter.AampObjectNode item, uint hash)
    {
        var index = FindParameterIndex(item, hash);
        return index is { } value ? item.Parameters[value] : null;
    }

    private static int GetInt(BotwAampWriter.AampObjectNode item, uint hash) =>
        FindParameter(item, hash) is { RawBytes: { Length: >= 4 } bytes } ? BitConverter.ToInt32(bytes, 0) : -1;

    private static string? GetString(BotwAampWriter.AampObjectNode item, string name) =>
        FindParameter(item, BotwAampWriter.Crc32(name)) is { RawBytes: { } bytes }
            ? Encoding.UTF8.GetString(bytes).TrimEnd('\0')
            : null;

    private static Vector3? GetVector3(BotwAampWriter.AampObjectNode item, string name)
    {
        var bytes = FindParameter(item, BotwAampWriter.Crc32(name))?.RawBytes;
        return bytes is { Length: >= 12 }
            ? new Vector3(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8))
            : null;
    }

    private static Vector4? GetVector4(BotwAampWriter.AampObjectNode item, string name)
    {
        var bytes = FindParameter(item, BotwAampWriter.Crc32(name))?.RawBytes;
        return bytes is { Length: >= 16 }
            ? new Vector4(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8), BitConverter.ToSingle(bytes, 12))
            : null;
    }

    private static void SetInt(BotwAampWriter.AampObjectNode item, uint hash, int value) =>
        SetRaw(item, hash, 2, BitConverter.GetBytes(value));

    private static void SetExistingInt(BotwAampWriter.AampObjectNode item, string name, int value)
    {
        var hash = BotwAampWriter.Crc32(name);
        if (FindParameterIndex(item, hash) != null)
            SetInt(item, hash, value);
    }

    private static void SetString(BotwAampWriter.AampObjectNode item, string name, string value) =>
        SetRaw(item, BotwAampWriter.Crc32(name), 15, Encoding.UTF8.GetBytes(value + "\0"));

    private static void SetVector3(BotwAampWriter.AampObjectNode item, string name, Vector3 value)
    {
        var bytes = new byte[12];
        BitConverter.GetBytes(value.X).CopyTo(bytes, 0);
        BitConverter.GetBytes(value.Y).CopyTo(bytes, 4);
        BitConverter.GetBytes(value.Z).CopyTo(bytes, 8);
        SetRaw(item, BotwAampWriter.Crc32(name), 4, bytes);
    }

    private static void SetVector4(BotwAampWriter.AampObjectNode item, string name, Vector4 value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value.X).CopyTo(bytes, 0);
        BitConverter.GetBytes(value.Y).CopyTo(bytes, 4);
        BitConverter.GetBytes(value.Z).CopyTo(bytes, 8);
        BitConverter.GetBytes(value.W).CopyTo(bytes, 12);
        SetRaw(item, BotwAampWriter.Crc32(name), 5, bytes);
    }

    private static void SetRaw(BotwAampWriter.AampObjectNode item, uint hash, byte fallbackType, byte[] bytes)
    {
        var index = FindParameterIndex(item, hash);
        var type = index is { } existing ? item.Parameters[existing].Type : fallbackType;
        var replacement = BotwAampWriter.AampParameterNode.Raw(hash, type, bytes);
        if (index is { } value)
            item.Parameters[value] = replacement;
        else
            item.Parameters.Add(replacement);
    }

    private static string SwapSideTokens(string name)
    {
        const string marker = "\u0001";
        return System.Text.RegularExpressions.Regex.Replace(
            name,
            @"(?:(?<=^)|(?<=[_:\-.]))([LR])(?=$|[_:\-.])",
            match => match.Groups[1].Value == "L" ? marker : "L",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            .Replace(marker, "R", StringComparison.Ordinal);
    }

    private static string? GetBoneName(IReadOnlyList<string> bones, int index) => index >= 0 && index < bones.Count
        ? bones[index]
        : null;

    private static uint ReadUInt32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);

    private static string ReadZeroTerminatedUtf8(byte[] bytes, int start, int endExclusive)
    {
        var end = start;
        while (end < endExclusive && bytes[end] != 0)
            end++;
        return Encoding.UTF8.GetString(bytes, start, end - start);
    }

    private static IReadOnlyList<string> ReadStringPool(byte[] bytes, uint stringPoolSize)
    {
        if (stringPoolSize == 0)
            return Array.Empty<string>();

        var start = checked(bytes.Length - (int)stringPoolSize);
        var values = new List<string>();
        var cursor = start;
        while (cursor < bytes.Length)
        {
            var next = cursor;
            while (next < bytes.Length && bytes[next] != 0)
                next++;

            if (next > cursor)
            {
                var value = Encoding.UTF8.GetString(bytes, cursor, next - cursor);
                if (value.All(character => !char.IsControl(character)))
                    values.Add(value);
            }

            cursor = next + 1;
        }

        return values.Distinct(StringComparer.Ordinal).ToArray();
    }
}
