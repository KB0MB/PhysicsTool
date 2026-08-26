using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HKCLTool;

public sealed class BphyssbDocumentSummary
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
    public IReadOnlyList<string> SupportBoneNames { get; init; } = Array.Empty<string>();
    public BphyssbGraphSummary? Graph { get; init; }
    internal AampReadArchive? Archive { get; init; }
}

public sealed class BphyssbGraphSummary
{
    public IReadOnlyList<string> Bones { get; init; } = Array.Empty<string>();
    public IReadOnlyList<BphyssbPoseBone> MainBones { get; init; } = Array.Empty<BphyssbPoseBone>();
    public IReadOnlyList<BphyssbPoseBone> SupportBones { get; init; } = Array.Empty<BphyssbPoseBone>();
    public int CurveCount { get; init; }
    public int OutputSingleCount { get; init; }
    public int OutputDoubleCount { get; init; }
}

public sealed record BphyssbPoseBone(int BoneId, int SpaceId, Vector3 Translation, Vector4 Rotation);

/// <summary>
/// Converts the editable TotK helper-bone graph into BotW's separate support-bone
/// AAMP layout. The two formats share curves and base poses, but their output
/// tables and reference packing are different, so this is intentionally kept
/// outside the BPHHB writer.
/// </summary>
public static class BphyssbBridge
{
    private const uint ParamRootHash = 2767637356u;
    private const uint SupportBoneHeaderHash = 2417381405u;
    private const uint SupportBoneDataHash = 2663467289u;
    private const uint BoneListHash = 3240679892u;
    private const uint ConnectionCurveListHash = 1471120930u;
    private const uint OutputSingleListHash = 2123203909u;
    private const uint OutputDoubleListHash = 2452354690u;
    private const uint MainBoneListHash = 3785260662u;
    private const uint SupportBoneListHash = 2012889698u;

    private const uint HeaderStepHash = 423031007u;
    private const uint BoneCountHash = 1498594108u;
    private const uint UnknownHeaderCountHash = 3864144338u;
    private const uint CurveCountHash = 3617964918u;
    private const uint SingleOutputCountHash = 745487375u;
    private const uint DoubleOutputCountHash = 3924745678u;
    private const uint MainBoneCountHash = 2154867273u;
    private const uint SupportBoneCountHash = 3991154691u;

    private const uint NameHash = 1579384326u;
    private const uint SourceDriverBoneIdHash = 249727330u;
    private const uint SourceAttributeHash = 75375380u;
    private const uint SourceKey0Hash = 1692570776u;
    private const uint SourceKey1Hash = 333816846u;
    private const uint SourceKey2Hash = 2330785204u;
    private const uint SourceKey3Hash = 4260087074u;
    private const uint SourceKeyCountHash = 139113150u;
    private const uint SourceConnection0Hash = 2390703269u;
    private const uint SourceConnection1Hash = 918772672u;
    private const uint SourceBoneIdHash = 3407736066u;
    private const uint SourceBaseBoneIdHash = 4279981158u;
    private const uint SourceBaseTranslationHash = 4181521146u;
    private const uint SourceBaseRotationHash = 1609157221u;
    private const uint SourceAimAxisHash = 3442436866u;
    private const uint SourceUpAxisHash = 907422927u;
    private const uint SourceRollIdHash = 2869652774u;
    private const uint SourceBendHIdHash = 3079588026u;
    private const uint SourceBendVIdHash = 122218518u;
    private const uint SourceTranslateXIdHash = 3595547632u;
    private const uint SourceTranslateYIdHash = 1861473429u;
    private const uint SourceTranslateZIdHash = 2084993915u;

    private const uint TargetBoneAttributeHash = 554147540u;
    private const uint TargetConstantInHash = 1411838321u;
    private const uint TargetConstantOutHash = 260541147u;
    private const uint TargetConnectionHash = 704082790u;
    private const uint TargetWeightHash = 130897217u;
    private const uint TargetConnection0Hash = 2693310104u;
    private const uint TargetWeight0Hash = 2822815546u;
    private const uint TargetConnection1Hash = 3616511502u;
    private const uint TargetWeight1Hash = 3746009004u;
    private const uint TargetBoneHash = 893828983u;
    private const uint TargetAimHash = 828945678u;
    private const uint TargetUpHash = 1133833840u;
    private const uint TargetSpaceHash = 695386426u;
    private const uint TargetBendHHash = 318199082u;
    private const uint TargetBendVHash = 3908593737u;
    private const uint TargetRollHash = 783626958u;
    private const uint TargetTranslateXHash = 2040119991u;
    private const uint TargetTranslateYHash = 245297697u;
    private const uint TargetTranslateZHash = 2543297435u;

    private const int HeaderSize = 0x30;

    public static BphyssbDocumentSummary Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return LoadBytes(bytes, Path.GetFullPath(path));
    }

    internal static BphyssbDocumentSummary LoadBytes(byte[] bytes, string sourcePath)
    {
        if (bytes.Length < HeaderSize)
            throw new InvalidDataException("BPHYSSB is shorter than an AAMP header.");
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "AAMP")
            throw new InvalidDataException("BPHYSSB must begin with the AAMP signature.");

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

        var type = ReadZeroTerminatedUtf8(bytes, HeaderSize, Math.Min(bytes.Length, HeaderSize + (int)parameterIoOffset));
        if (!string.Equals(type, "physsb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected an AAMP support-bone file (type physsb), found '{type}'.");

        AampReadArchive? archive = null;
        BphyssbGraphSummary? graph = null;
        try
        {
            archive = BotwAampReader.Read(bytes);
            graph = ReadGraph(archive);
        }
        catch (InvalidDataException)
        {
            // Leave the basic file summary available even if a future support
            // bone variant contains tables we do not decode yet.
        }

        return new BphyssbDocumentSummary
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
            SupportBoneNames = graph?.Bones.Count > 0 ? graph.Bones : ReadStringPool(bytes, stringPoolSize),
            Graph = graph,
            Archive = archive
        };
    }

    public static BphyssbDocumentSummary Save(BphyssbDocumentSummary document, string outputPath)
    {
        if (!Path.GetExtension(outputPath).Equals(".bphyssb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BPHYSSB documents must be saved with the .bphyssb extension.");

        File.WriteAllBytes(outputPath, document.Bytes);
        return Load(outputPath);
    }

    public static BphyssbDocumentSummary UpdateBone(
        BphyssbDocumentSummary document,
        int boneIndex,
        string name,
        int spaceIndex,
        Vector3 translation,
        Vector4 rotation)
    {
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var boneList = RequireList(data, BoneListHash, "bone_list");
            if (boneIndex < 0 || boneIndex >= boneList.Objects.Count)
                throw new ArgumentOutOfRangeException(nameof(boneIndex));

            SetString(boneList.Objects[boneIndex], NameHash, name);
            UpdatePoseRecords(FindList(data, MainBoneListHash)?.Objects, boneIndex, spaceIndex, translation, rotation);
            UpdatePoseRecords(FindList(data, SupportBoneListHash)?.Objects, boneIndex, spaceIndex, translation, rotation);
        });
    }

    public static BphyssbDocumentSummary DuplicateBone(BphyssbDocumentSummary document, int sourceBoneIndex, string? requestedName = null)
    {
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var boneList = RequireList(data, BoneListHash, "bone_list");
            if (sourceBoneIndex < 0 || sourceBoneIndex >= boneList.Objects.Count)
                throw new ArgumentOutOfRangeException(nameof(sourceBoneIndex));

            var newBoneIndex = boneList.Objects.Count;
            var sourceName = GetString(boneList.Objects[sourceBoneIndex], NameHash) ?? $"SupportBone_{sourceBoneIndex}";
            var newName = string.IsNullOrWhiteSpace(requestedName)
                ? GetUniqueBoneName(boneList, sourceName)
                : requestedName.Trim();

            var newBone = CloneObject(boneList.Objects[sourceBoneIndex], $"bone_{newBoneIndex}");
            SetString(newBone, NameHash, newName);
            boneList.Objects.Add(newBone);

            ClonePoseRecords(FindList(data, MainBoneListHash), sourceBoneIndex, newBoneIndex, "main_bone");
            ClonePoseRecords(FindList(data, SupportBoneListHash), sourceBoneIndex, newBoneIndex, "support_bone");
        });
    }

    public static BphyssbDocumentSummary AddBone(BphyssbDocumentSummary document, int preferredSourceIndex)
    {
        var sourceIndex = document.Graph?.Bones.Count > 0
            ? Math.Clamp(preferredSourceIndex, 0, document.Graph.Bones.Count - 1)
            : throw new InvalidOperationException("This support-bone file has no source bone to clone.");
        return DuplicateBone(document, sourceIndex, "SupportBone_" + document.Graph!.Bones.Count.ToString());
    }

    public static BphyssbDocumentSummary MirrorBoneAcrossX(BphyssbDocumentSummary document, int boneIndex)
    {
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var boneList = RequireList(data, BoneListHash, "bone_list");
            if (boneIndex < 0 || boneIndex >= boneList.Objects.Count)
                throw new ArgumentOutOfRangeException(nameof(boneIndex));

            var bone = boneList.Objects[boneIndex];
            var name = GetString(bone, NameHash);
            if (!string.IsNullOrWhiteSpace(name))
                SetString(bone, NameHash, SwapSideTokens(name));

            MirrorTransformRecords(FindList(data, MainBoneListHash)?.Objects
                .Where(item => GetInt(item, TargetBoneHash) == boneIndex)
                ?? Enumerable.Empty<BotwAampWriter.AampObjectNode>());
            MirrorTransformRecords(FindList(data, SupportBoneListHash)?.Objects
                .Where(item => GetInt(item, TargetBoneHash) == boneIndex)
                ?? Enumerable.Empty<BotwAampWriter.AampObjectNode>());
        });
    }

    public static BphyssbDocumentSummary MoveBone(BphyssbDocumentSummary document, int sourceBoneIndex, int destinationBoneIndex)
    {
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var boneList = RequireList(data, BoneListHash, "bone_list");
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

            RemapBoneIds(FindList(data, MainBoneListHash)?.Objects, remap, TargetBoneHash, TargetSpaceHash);
            RemapBoneIds(FindList(data, SupportBoneListHash)?.Objects, remap, TargetBoneHash, TargetSpaceHash);
        });
    }

    public static BphyssbExportSummary Export(BphhbDocumentSummary source, string outputPath)
    {
        if (!Path.GetExtension(outputPath).Equals(".bphyssb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BotW support-bone exports must use the .bphyssb extension.");

        var bytes = Build(source);
        File.WriteAllBytes(outputPath, bytes);
        return Validate(outputPath);
    }

    /// <summary>
    /// Produces controlled BotW support-bone variants for investigating a TotK
    /// two-key curve. This is diagnostic only; normal conversion remains unchanged.
    /// </summary>
    public static IReadOnlyList<BphyssbExportSummary> ExportTwoKeyCurveTests(
        BphhbDocumentSummary source,
        string outputDirectory,
        string outputStem)
    {
        Directory.CreateDirectory(outputDirectory);
        var baseDocument = LoadBytes(Build(source), Path.Combine(outputDirectory, outputStem + "_base.bphyssb"));
        var variants = new (string Name, Func<Vector4, Vector4, Vector4> Endpoint, bool OmitKey2)[]
        {
            ("hold-key1", (_, key1) => key1, false),
            ("return-key0", (key0, _) => key0, false),
            ("linear", (key0, key1) => key1 + (key1 - key0), false),
            ("omit-key2", (_, _) => Vector4.Zero, true)
        };

        var results = new List<BphyssbExportSummary>(variants.Length);
        foreach (var variant in variants)
        {
            var rebuilt = Rebuild(baseDocument, root =>
            {
                var curves = RequireList(RequireDataRoot(root), ConnectionCurveListHash, "connection_curve_list");
                foreach (var curve in curves.Objects)
                {
                    var key0 = GetVector4(curve, SourceKey0Hash);
                    var key1 = GetVector4(curve, SourceKey1Hash);
                    if (key0 is null || key1 is null)
                        continue;

                    var key2Index = FindParameterIndex(curve, SourceKey2Hash);
                    if (variant.OmitKey2)
                    {
                        if (key2Index is { } index)
                            curve.Parameters.RemoveAt(index);
                    }
                    else
                    {
                        SetVector4(curve, SourceKey2Hash, variant.Endpoint(key0.Value, key1.Value));
                    }
                }
            });

            var outputPath = Path.Combine(outputDirectory, $"{outputStem}_{variant.Name}.bphyssb");
            File.WriteAllBytes(outputPath, rebuilt.Bytes);
            results.Add(Validate(outputPath));
        }

        return results;
    }

    /// <summary>
    /// Expands TotK two-key curves whose range ends at zero into BotW's
    /// ordered negative/zero/positive three-key representation.
    /// Diagnostic only; normal conversion remains unchanged.
    /// </summary>
    public static BphyssbExportSummary ExportSymmetricTwoKeyCurveTest(
        BphhbDocumentSummary source,
        string outputPath)
    {
        var baseDocument = LoadBytes(Build(source), outputPath);
        var rebuilt = Rebuild(baseDocument, root =>
        {
            var curves = RequireList(RequireDataRoot(root), ConnectionCurveListHash, "connection_curve_list");
            foreach (var curve in curves.Objects)
            {
                var key0 = GetVector4(curve, SourceKey0Hash);
                var key1 = GetVector4(curve, SourceKey1Hash);
                if (key0 is null || key1 is null)
                    continue;

                var first = key0.Value;
                var second = key1.Value;
                const float epsilon = 1e-5f;

                if (MathF.Abs(first.X) <= epsilon && MathF.Abs(first.Y) <= epsilon && second.X > epsilon)
                {
                    var slope = ResolveCurveSlope(first, second);
                    SetVector4(curve, SourceKey0Hash, new Vector4(-second.X, -second.Y, 0f, slope));
                    SetVector4(curve, SourceKey1Hash, new Vector4(0f, 0f, slope, slope));
                    SetVector4(curve, SourceKey2Hash, new Vector4(second.X, second.Y, slope, 0f));
                }
                else if (MathF.Abs(second.X) <= epsilon && MathF.Abs(second.Y) <= epsilon && first.X < -epsilon)
                {
                    var slope = ResolveCurveSlope(first, second);
                    SetVector4(curve, SourceKey0Hash, new Vector4(first.X, first.Y, 0f, slope));
                    SetVector4(curve, SourceKey1Hash, new Vector4(0f, 0f, slope, slope));
                    SetVector4(curve, SourceKey2Hash, new Vector4(-first.X, -first.Y, slope, 0f));
                }
                else
                {
                    throw new InvalidDataException(
                        $"Two-key curve does not have a zero endpoint: key0={first}, key1={second}.");
                }
            }
        });

        File.WriteAllBytes(outputPath, rebuilt.Bytes);
        return Validate(outputPath);
    }

    private static float ResolveCurveSlope(Vector4 first, Vector4 second)
    {
        var deltaX = second.X - first.X;
        if (MathF.Abs(deltaX) <= 1e-6f)
            throw new InvalidDataException($"Two-key curve has no input range: key0={first}, key1={second}.");

        return (second.Y - first.Y) / deltaX;
    }

    /// <summary>
    /// Forces one support bone's vertical-bend output to a visible constant.
    /// This distinguishes an inactive output from an incorrectly shaped curve.
    /// Diagnostic only.
    /// </summary>
    public static BphyssbExportSummary ExportConstantSupportCurveTest(
        BphhbDocumentSummary source,
        string outputPath,
        string supportBoneName,
        float bendRadians)
    {
        var baseDocument = LoadBytes(Build(source), outputPath);
        var rebuilt = Rebuild(baseDocument, root =>
        {
            var data = RequireDataRoot(root);
            var bones = RequireList(data, BoneListHash, "bone_list");
            var supports = RequireList(data, SupportBoneListHash, "support_bone_list");
            var outputs = RequireList(data, OutputSingleListHash, "output_single_list");
            var curves = RequireList(data, ConnectionCurveListHash, "connection_curve_list");
            var target = supports.Objects.FirstOrDefault(record =>
            {
                var boneId = GetInt(record, TargetBoneHash);
                return boneId >= 0 && boneId < bones.Objects.Count &&
                    string.Equals(GetString(bones.Objects[boneId], NameHash), supportBoneName, StringComparison.OrdinalIgnoreCase);
            }) ?? throw new InvalidDataException($"Support bone '{supportBoneName}' was not found.");

            var outputIndex = GetInt(target, TargetBendVHash);
            if (outputIndex < 0 || outputIndex >= outputs.Objects.Count)
                throw new InvalidDataException($"Support bone '{supportBoneName}' has no valid vertical-bend output.");

            var packedCurve = GetInt(outputs.Objects[outputIndex], TargetConnectionHash);
            var curveIndex = packedCurve & 0xFFFF;
            if (curveIndex < 0 || curveIndex >= curves.Objects.Count)
                throw new InvalidDataException($"Support bone '{supportBoneName}' references an invalid curve.");

            var curve = curves.Objects[curveIndex];
            SetVector4(curve, SourceKey0Hash, new Vector4(-MathF.PI, bendRadians, 0f, 0f));
            SetVector4(curve, SourceKey1Hash, new Vector4(0f, bendRadians, 0f, 0f));
            SetVector4(curve, SourceKey2Hash, new Vector4(MathF.PI, bendRadians, 0f, 0f));
        });

        File.WriteAllBytes(outputPath, rebuilt.Bytes);
        return Validate(outputPath);
    }
    /// <summary>
    /// Replaces one support bone's vertical-bend response with the native
    /// Armor 182 Head-to-Hair_Back_C response. Diagnostic only.
    /// </summary>
    public static BphyssbExportSummary ExportArmor182HeadCurveTest(
        BphhbDocumentSummary source,
        string outputPath,
        string supportBoneName)
    {
        var baseDocument = LoadBytes(Build(source), outputPath);
        var rebuilt = Rebuild(baseDocument, root =>
        {
            var data = RequireDataRoot(root);
            var bones = RequireList(data, BoneListHash, "bone_list");
            var supports = RequireList(data, SupportBoneListHash, "support_bone_list");
            var outputs = RequireList(data, OutputSingleListHash, "output_single_list");
            var curves = RequireList(data, ConnectionCurveListHash, "connection_curve_list");
            var target = supports.Objects.FirstOrDefault(record =>
            {
                var boneId = GetInt(record, TargetBoneHash);
                return boneId >= 0 && boneId < bones.Objects.Count &&
                    string.Equals(GetString(bones.Objects[boneId], NameHash), supportBoneName, StringComparison.OrdinalIgnoreCase);
            }) ?? throw new InvalidDataException($"Support bone '{supportBoneName}' was not found.");

            var outputIndex = GetInt(target, TargetBendVHash);
            if (outputIndex < 0 || outputIndex >= outputs.Objects.Count)
                throw new InvalidDataException($"Support bone '{supportBoneName}' has no valid vertical-bend output.");

            var packedCurve = GetInt(outputs.Objects[outputIndex], TargetConnectionHash);
            var curveIndex = packedCurve & 0xFFFF;
            if (curveIndex < 0 || curveIndex >= curves.Objects.Count)
                throw new InvalidDataException($"Support bone '{supportBoneName}' references an invalid curve.");

            // Native Armor_182_Head.bphyssb, Hair_Back_C_1 vertical bend.
            var curve = curves.Objects[curveIndex];
            SetVector4(curve, SourceKey0Hash, new Vector4(-1.04719806f, 1.04719806f, 0f, -1f));
            SetVector4(curve, SourceKey1Hash, new Vector4(0f, 0f, -1f, -0.777778029f));
            SetVector4(curve, SourceKey2Hash, new Vector4(0.785398006f, -0.610864997f, 0f, 0f));
        });

        File.WriteAllBytes(outputPath, rebuilt.Bytes);
        return Validate(outputPath);
    }
    /// <summary>
    /// Emits narrowly-scoped orientation experiments for a named support bone.
    /// These are diagnostic exports and never alter normal BPHHB conversion.
    /// </summary>
    public static IReadOnlyList<BphyssbExportSummary> ExportSupportBoneOrientationTests(
        BphhbDocumentSummary source,
        string outputDirectory,
        string outputStem,
        string supportBoneName)
    {
        Directory.CreateDirectory(outputDirectory);
        var baseDocument = LoadBytes(Build(source), Path.Combine(outputDirectory, outputStem + "_base.bphyssb"));
        var variants = new[] { "inverse-frame", "invert-rotation-curve" };
        var results = new List<BphyssbExportSummary>(variants.Length);
        foreach (var variant in variants)
        {
            var rebuilt = Rebuild(baseDocument, root =>
            {
                var data = RequireDataRoot(root);
                var bones = RequireList(data, BoneListHash, "bone_list");
                var supports = RequireList(data, SupportBoneListHash, "support_bone_list");
                var curves = RequireList(data, ConnectionCurveListHash, "connection_curve_list");
                var target = supports.Objects.FirstOrDefault(record =>
                {
                    var boneId = GetInt(record, TargetBoneHash);
                    return boneId >= 0 && boneId < bones.Objects.Count &&
                        string.Equals(GetString(bones.Objects[boneId], NameHash), supportBoneName, StringComparison.OrdinalIgnoreCase);
                }) ?? throw new InvalidDataException($"Support bone '{supportBoneName}' was not found.");

                if (variant == "inverse-frame")
                {
                    var rotation = GetVector4(target, SourceBaseRotationHash)
                        ?? throw new InvalidDataException($"Support bone '{supportBoneName}' has no base rotation.");
                    var frame = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
                    if (frame.LengthSquared() < 1e-8f)
                        throw new InvalidDataException($"Support bone '{supportBoneName}' has an invalid base rotation.");
                    frame = Quaternion.Normalize(Quaternion.Inverse(frame));
                    SetVector4(target, SourceBaseRotationHash, new Vector4(frame.X, frame.Y, frame.Z, frame.W));
                    return;
                }

                var outputIndex = GetInt(target, TargetBendVHash);
                var outputs = RequireList(data, OutputSingleListHash, "output_single_list");
                if (outputIndex < 0 || outputIndex >= outputs.Objects.Count)
                    throw new InvalidDataException($"Support bone '{supportBoneName}' does not reference a single rotation output.");
                var packedCurve = GetInt(outputs.Objects[outputIndex], TargetConnectionHash);
                var curveIndex = packedCurve & 0xFFFF;
                if (curveIndex < 0 || curveIndex >= curves.Objects.Count)
                    throw new InvalidDataException($"Support bone '{supportBoneName}' references an invalid rotation curve.");
                foreach (var keyHash in new[] { SourceKey0Hash, SourceKey1Hash, SourceKey2Hash })
                {
                    if (GetVector4(curves.Objects[curveIndex], keyHash) is { } key)
                        SetVector4(curves.Objects[curveIndex], keyHash, -key);
                }
            });

            var outputPath = Path.Combine(outputDirectory, $"{outputStem}_{variant}.bphyssb");
            File.WriteAllBytes(outputPath, rebuilt.Bytes);
            results.Add(Validate(outputPath));
        }
        return results;
    }
    public static JObject ExportSummary(BphyssbDocumentSummary summary) => new()
    {
        ["format"] = "BPHYSSBTool.Readable",
        ["version"] = 1,
        ["notes"] = "BPHYSSB support-bone AAMP is edited through its native graph. Names and main/support base-pose records are currently authorable.",
        ["bphyssb"] = new JObject
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
            ["supportBones"] = new JArray(summary.SupportBoneNames),
            ["graph"] = summary.Graph == null ? null : new JObject
            {
                ["curveCount"] = summary.Graph.CurveCount,
                ["outputSingleCount"] = summary.Graph.OutputSingleCount,
                ["outputDoubleCount"] = summary.Graph.OutputDoubleCount,
                ["mainBones"] = new JArray(summary.Graph.MainBones.Select(bone => new JObject
                {
                    ["boneId"] = bone.BoneId,
                    ["bone"] = GetBoneName(summary.Graph.Bones, bone.BoneId),
                    ["spaceId"] = bone.SpaceId,
                    ["translation"] = new JArray(bone.Translation.X, bone.Translation.Y, bone.Translation.Z),
                    ["rotation"] = new JArray(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W)
                })),
                ["supportBones"] = new JArray(summary.Graph.SupportBones.Select(bone => new JObject
                {
                    ["boneId"] = bone.BoneId,
                    ["bone"] = GetBoneName(summary.Graph.Bones, bone.BoneId),
                    ["spaceId"] = bone.SpaceId,
                    ["translation"] = new JArray(bone.Translation.X, bone.Translation.Y, bone.Translation.Z),
                    ["rotation"] = new JArray(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W)
                }))
            }
        }
    };

    public static BphyssbExportSummary Validate(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var archive = BotwAampReader.Read(bytes);
        if (!string.Equals(archive.Type, "physsb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected a physsb AAMP archive, found '{archive.Type}'.");

        var data = archive.Root.Children.SingleOrDefault()
            ?? throw new InvalidDataException("BPHYSSB is missing its support-bone data list.");
        var boneList = RequireList(data, BoneListHash, "bone_list");
        var curveList = RequireList(data, ConnectionCurveListHash, "connection_curve_list");
        var mainList = RequireList(data, MainBoneListHash, "main_bone_list");
        var supportList = RequireList(data, SupportBoneListHash, "support_bone_list");

        return new BphyssbExportSummary(
            path,
            bytes.Length,
            boneList.Objects.Count,
            curveList.Objects.Count,
            mainList.Objects.Count,
            supportList.Objects.Count);
    }

    private static BphyssbGraphSummary? ReadGraph(AampReadArchive archive)
    {
        if (!string.Equals(archive.Type, "physsb", StringComparison.OrdinalIgnoreCase))
            return null;

        // A support-bone archive can contain unrelated top-level lists. Select
        // the one that actually owns the graph instead of assuming a singleton.
        var data = archive.Root.Children.FirstOrDefault(child =>
            child.FindChild(BoneListHash) != null &&
            child.FindChild(ConnectionCurveListHash) != null &&
            child.FindChild(MainBoneListHash) != null &&
            child.FindChild(SupportBoneListHash) != null);
        if (data == null)
            return null;

        var boneList = data.FindChild(BoneListHash);
        var curveList = data.FindChild(ConnectionCurveListHash);
        var outputSingleList = data.FindChild(OutputSingleListHash);
        var outputDoubleList = data.FindChild(OutputDoubleListHash);
        var mainList = data.FindChild(MainBoneListHash);
        var supportList = data.FindChild(SupportBoneListHash);
        if (boneList == null || curveList == null || mainList == null || supportList == null)
            return null;

        return new BphyssbGraphSummary
        {
            Bones = boneList.Objects
                .Select((item, index) => item.Find(NameHash)?.AsString() ?? $"bone_{index}")
                .ToArray(),
            CurveCount = curveList.Objects.Count,
            OutputSingleCount = outputSingleList?.Objects.Count ?? 0,
            OutputDoubleCount = outputDoubleList?.Objects.Count ?? 0,
            MainBones = mainList.Objects.Select(ReadPoseBone).ToArray(),
            SupportBones = supportList.Objects.Select(ReadPoseBone).ToArray()
        };
    }

    private static BphyssbPoseBone ReadPoseBone(AampReadObject item) => new(
        item.Find(TargetBoneHash)?.AsInt32() ?? -1,
        item.Find(TargetSpaceHash)?.AsInt32() ?? -1,
        item.Find(SourceBaseTranslationHash)?.AsVector3() ?? Vector3.Zero,
        item.Find(SourceBaseRotationHash)?.AsVector4() ?? Vector4.UnitW);

    private static BphyssbDocumentSummary Rebuild(BphyssbDocumentSummary document, Action<BotwAampWriter.AampListNode> edit)
    {
        var archive = document.Archive
            ?? throw new InvalidOperationException("This BPHYSSB file could not be decoded into an editable AAMP graph.");
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
            : throw new InvalidOperationException("The BPHYSSB archive does not contain one editable data root.");

    private static BotwAampWriter.AampListNode RequireList(BotwAampWriter.AampListNode parent, uint hash, string name) =>
        FindList(parent, hash)
        ?? throw new InvalidOperationException($"The BPHYSSB graph does not contain '{name}'.");

    private static BotwAampWriter.AampListNode? FindList(BotwAampWriter.AampListNode parent, uint hash) =>
        parent.Children.FirstOrDefault(child => child.NameHash == hash);

    private static void SynchronizeHeaderCounts(BotwAampWriter.AampListNode root)
    {
        if (root.Objects.Count == 0 || root.Children.Count != 1)
            return;

        var header = root.Objects[0];
        var data = root.Children[0];
        SetExistingInt(header, BoneCountHash, FindList(data, BoneListHash)?.Objects.Count ?? 0);
        SetExistingInt(header, CurveCountHash, FindList(data, ConnectionCurveListHash)?.Objects.Count ?? 0);
        SetExistingInt(header, SingleOutputCountHash, FindList(data, OutputSingleListHash)?.Objects.Count ?? 0);
        SetExistingInt(header, DoubleOutputCountHash, FindList(data, OutputDoubleListHash)?.Objects.Count ?? 0);
        SetExistingInt(header, MainBoneCountHash, FindList(data, MainBoneListHash)?.Objects.Count ?? 0);
        SetExistingInt(header, SupportBoneCountHash, FindList(data, SupportBoneListHash)?.Objects.Count ?? 0);
    }

    private static void UpdatePoseRecords(
        IEnumerable<BotwAampWriter.AampObjectNode>? records,
        int boneIndex,
        int spaceIndex,
        Vector3 translation,
        Vector4 rotation)
    {
        if (records == null)
            return;

        foreach (var record in records.Where(item => GetInt(item, TargetBoneHash) == boneIndex))
        {
            SetInt(record, TargetSpaceHash, spaceIndex);
            SetVector3(record, SourceBaseTranslationHash, translation);
            SetVector4(record, SourceBaseRotationHash, rotation);
        }
    }

    private static void ClonePoseRecords(BotwAampWriter.AampListNode? list, int sourceBoneIndex, int newBoneIndex, string objectPrefix)
    {
        if (list == null)
            return;

        foreach (var item in list.Objects.Where(item => GetInt(item, TargetBoneHash) == sourceBoneIndex).ToArray())
        {
            var cloned = CloneObject(item, $"{objectPrefix}_{list.Objects.Count}");
            SetInt(cloned, TargetBoneHash, newBoneIndex);
            list.Objects.Add(cloned);
        }
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
            var translation = GetVector3(record, SourceBaseTranslationHash);
            if (translation.HasValue)
            {
                var mirrored = translation.Value;
                mirrored.X = -mirrored.X;
                SetVector3(record, SourceBaseTranslationHash, mirrored);
            }

            var rotation = GetVector4(record, SourceBaseRotationHash);
            if (rotation.HasValue)
                SetVector4(record, SourceBaseRotationHash, MirrorQuaternionAcrossX(rotation.Value));
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
            .Select(item => GetString(item, NameHash))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        for (var number = 1; ; number++)
        {
            var candidate = sourceName + "_" + number;
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    private static byte[] Build(BphhbDocumentSummary source)
    {
        var archive = source.Archive
            ?? throw new InvalidOperationException("The BPHHB helper graph could not be decoded.");
        if (!string.Equals(archive.Type, "phhb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only TotK BPHHB helper-bone files can be converted to BPHYSSB.");

        var sourceHeader = archive.Root.Objects.SingleOrDefault()
            ?? throw new InvalidDataException("BPHHB is missing its helper-bone header.");
        var sourceData = archive.Root.Children.SingleOrDefault()
            ?? throw new InvalidDataException("BPHHB is missing its helper-bone data list.");
        var sourceBones = RequireList(sourceData, BoneListHash, "bone_list");
        var sourceCurves = RequireList(sourceData, ConnectionCurveListHash, "connection_curve_list");
        var sourceOutputs = RequireList(sourceData, 1224233410u, "output_list");
        var sourceDrivers = RequireList(sourceData, 3993686733u, "driver_bone_list");
        var sourceDriven = RequireList(sourceData, 1256621249u, "driven_bone_list");
        var sourcePoses = RequireList(sourceData, 1386686749u, "pose_driven_list");

        if (sourceDriven.Objects.Count != sourcePoses.Objects.Count)
        {
            throw new InvalidDataException(
                $"BPHHB has {sourceDriven.Objects.Count} driven records but {sourcePoses.Objects.Count} pose-driven records.");
        }

        var sourceBoneNames = sourceBones.Objects
            .Select((item, index) => ReadString(item, NameHash, $"bone_{index}"))
            .ToArray();
        var driverRecords = sourceDrivers.Objects
            .Select((item, index) => new SourceDriver(index, ReadRequiredInt(item, SourceBoneIdHash, "driver_bone_list"), item))
            .ToArray();
        var drivenRecords = sourceDriven.Objects
            .Select((item, index) => new SourceDriven(index, ReadRequiredInt(item, SourceBoneIdHash, "driven_bone_list"), item, sourcePoses.Objects[index]))
            .ToArray();

        var requiredBoneIds = new List<int>();
        // Native BPHYSSB files keep a compact bone table containing the driver
        // and driven bones only. BPHHB base_bone_id is helper-graph context,
        // not an extra BotW support-bone table entry.
        AddRequiredBones(requiredBoneIds, driverRecords.Select(record => record.BoneId));
        AddRequiredBones(requiredBoneIds, drivenRecords.Select(record => record.BoneId));
        foreach (var boneId in requiredBoneIds)
        {
            if (boneId < 0 || boneId >= sourceBoneNames.Length)
                throw new InvalidDataException($"BPHHB references helper bone #{boneId}, but its bone list contains {sourceBoneNames.Length} entries.");
        }

        var targetBoneIds = requiredBoneIds
            .Select((sourceBoneId, targetBoneId) => (sourceBoneId, targetBoneId))
            .ToDictionary(pair => pair.sourceBoneId, pair => pair.targetBoneId);
        var driverTargets = driverRecords
            .Select((record, targetIndex) => (record.SourceIndex, targetIndex))
            .ToDictionary(pair => pair.SourceIndex, pair => pair.targetIndex);

        var root = new BotwAampWriter.AampListNode("param_root", ParamRootHash);
        var data = new BotwAampWriter.AampListNode("support_bone_data", SupportBoneDataHash);
        root.Children.Add(data);

        var boneList = new BotwAampWriter.AampListNode("bone_list", BoneListHash);
        var curveList = new BotwAampWriter.AampListNode("connection_curve_list", ConnectionCurveListHash);
        var outputSingleList = new BotwAampWriter.AampListNode("output_single_list", OutputSingleListHash);
        var outputDoubleList = new BotwAampWriter.AampListNode("output_double_list", OutputDoubleListHash);
        var mainBoneList = new BotwAampWriter.AampListNode("main_bone_list", MainBoneListHash);
        var supportBoneList = new BotwAampWriter.AampListNode("support_bone_list", SupportBoneListHash);

        data.Children.Add(boneList);
        data.Children.Add(curveList);
        data.Children.Add(outputSingleList);
        data.Children.Add(outputDoubleList);
        data.Children.Add(mainBoneList);
        data.Children.Add(supportBoneList);

        foreach (var sourceBoneId in requiredBoneIds)
        {
            var targetBone = new BotwAampWriter.AampObjectNode($"bone_{boneList.Objects.Count}");
            targetBone.Parameters.Add(RawString(NameHash, 8, sourceBoneNames[sourceBoneId]));
            boneList.Objects.Add(targetBone);
        }

        for (var index = 0; index < sourceCurves.Objects.Count; index++)
        {
            var sourceCurve = sourceCurves.Objects[index];
            var sourceDriverIndex = ReadRequiredInt(sourceCurve, SourceDriverBoneIdHash, "connection_curve_list");
            if (!driverTargets.TryGetValue(sourceDriverIndex, out var targetDriverIndex))
                throw new InvalidDataException($"BPHHB curve #{index} references missing driver #{sourceDriverIndex}.");

            var sourceAttribute = ReadRequiredInt(sourceCurve, SourceAttributeHash, "connection_curve_list");
            var targetCurve = new BotwAampWriter.AampObjectNode($"connection_curve_{index}");
            targetCurve.Parameters.Add(RawInt(TargetBoneAttributeHash, PackBoneAttribute(targetDriverIndex, sourceAttribute)));
            targetCurve.Parameters.Add(RawBool(TargetConstantInHash, false));
            targetCurve.Parameters.Add(RawBool(TargetConstantOutHash, false));
            CopyCurveKeys(targetCurve, sourceCurve);
            curveList.Objects.Add(targetCurve);
        }

        var sourceOutputConnections = sourceOutputs.Objects
            .Select((sourceOutput, index) =>
            {
                var connection0 = ReadOptionalInt(sourceOutput, SourceConnection0Hash);
                var connection1 = ReadOptionalInt(sourceOutput, SourceConnection1Hash);
                ValidateCurveReference(connection0, curveList.Objects.Count, index);
                ValidateCurveReference(connection1, curveList.Objects.Count, index);
                return (Connection0: connection0, Connection1: connection1);
            })
            .ToArray();

        var outputHandles = new Dictionary<int, int>();

        int AddInactiveSingleOutput()
        {
            var targetIndex = outputSingleList.Objects.Count;
            var targetOutput = new BotwAampWriter.AampObjectNode($"output_single_{targetIndex}");
            targetOutput.Parameters.Add(RawInt(TargetConnectionHash, -1));
            targetOutput.Parameters.Add(RawFloat(TargetWeightHash, 0f));
            outputSingleList.Objects.Add(targetOutput);
            return targetIndex;
        }

        int GetOrCreateOutputHandle(int sourceOutputIndex)
        {
            if (outputHandles.TryGetValue(sourceOutputIndex, out var existingHandle))
                return existingHandle;
            if ((uint)sourceOutputIndex >= (uint)sourceOutputConnections.Length)
                throw new InvalidDataException($"BPHHB pose references missing output #{sourceOutputIndex}.");

            var (connection0, connection1) = sourceOutputConnections[sourceOutputIndex];
            int targetHandle;
            if (connection0 >= 0 && connection1 >= 0)
            {
                var targetIndex = outputDoubleList.Objects.Count;
                var targetOutput = new BotwAampWriter.AampObjectNode($"output_double_{targetIndex}");
                targetOutput.Parameters.Add(RawInt(TargetConnection0Hash, EncodeCurveReference(connection0)));
                targetOutput.Parameters.Add(RawFloat(TargetWeight0Hash, 1f));
                targetOutput.Parameters.Add(RawInt(TargetConnection1Hash, EncodeCurveReference(connection1)));
                targetOutput.Parameters.Add(RawFloat(TargetWeight1Hash, 1f));
                outputDoubleList.Objects.Add(targetOutput);
                targetHandle = EncodeDoubleOutputReference(targetIndex);
            }
            else if (connection0 >= 0 || connection1 >= 0)
            {
                var targetIndex = outputSingleList.Objects.Count;
                var targetOutput = new BotwAampWriter.AampObjectNode($"output_single_{targetIndex}");
                targetOutput.Parameters.Add(RawInt(TargetConnectionHash, EncodeCurveReference(Math.Max(connection0, connection1))));
                targetOutput.Parameters.Add(RawFloat(TargetWeightHash, 1f));
                outputSingleList.Objects.Add(targetOutput);
                targetHandle = targetIndex;
            }
            else
            {
                targetHandle = AddInactiveSingleOutput();
            }

            outputHandles[sourceOutputIndex] = targetHandle;
            return targetHandle;
        }

        int MapActiveOutput(AampReadObject pose, uint sourceIdHash)
        {
            var sourceOutputIndex = ReadOptionalInt(pose, sourceIdHash);
            return sourceOutputIndex >= 0 ? GetOrCreateOutputHandle(sourceOutputIndex) : -1;
        }

        int MapRotationOutput(AampReadObject pose, uint sourceIdHash)
        {
            var sourceOutputIndex = ReadOptionalInt(pose, sourceIdHash);
            return sourceOutputIndex >= 0
                ? GetOrCreateOutputHandle(sourceOutputIndex)
                : AddInactiveSingleOutput();
        }
        foreach (var record in driverRecords)
        {
            var targetMainBone = new BotwAampWriter.AampObjectNode($"main_bone_{mainBoneList.Objects.Count}");
            targetMainBone.Parameters.Add(RawInt(TargetBoneHash, targetBoneIds[record.BoneId]));
            CopyParameter(targetMainBone, record.Object, SourceBaseTranslationHash);
            CopyParameter(targetMainBone, record.Object, SourceBaseRotationHash);
            CopyParameter(targetMainBone, record.Object, SourceAimAxisHash, TargetAimHash);
            CopyParameter(targetMainBone, record.Object, SourceUpAxisHash, TargetUpHash);
            targetMainBone.Parameters.Add(RawInt(TargetSpaceHash, -1));
            mainBoneList.Objects.Add(targetMainBone);
        }

        foreach (var record in drivenRecords)
        {
            var targetSupportBone = new BotwAampWriter.AampObjectNode($"support_bone_{supportBoneList.Objects.Count}");
            targetSupportBone.Parameters.Add(RawInt(TargetBoneHash, targetBoneIds[record.BoneId]));
            CopyParameter(targetSupportBone, record.Pose, SourceBaseTranslationHash);
            CopyParameter(targetSupportBone, record.Pose, SourceBaseRotationHash);
            CopyParameter(targetSupportBone, record.Pose, SourceAimAxisHash, TargetAimHash);
            CopyParameter(targetSupportBone, record.Pose, SourceUpAxisHash, TargetUpHash);
            targetSupportBone.Parameters.Add(RawInt(TargetSpaceHash, -1));

            // BotW stores the three rotational outputs as positional channels.
            // Even an unused bend/roll channel owns a null single-output slot;
            // translation channels are sparse and remain -1 when unconnected.
            targetSupportBone.Parameters.Add(RawInt(TargetBendHHash, MapRotationOutput(record.Pose, SourceBendHIdHash)));
            targetSupportBone.Parameters.Add(RawInt(TargetBendVHash, MapRotationOutput(record.Pose, SourceBendVIdHash)));
            targetSupportBone.Parameters.Add(RawInt(TargetRollHash, MapRotationOutput(record.Pose, SourceRollIdHash)));
            targetSupportBone.Parameters.Add(RawInt(TargetTranslateXHash, MapActiveOutput(record.Pose, SourceTranslateXIdHash)));
            targetSupportBone.Parameters.Add(RawInt(TargetTranslateYHash, MapActiveOutput(record.Pose, SourceTranslateYIdHash)));
            targetSupportBone.Parameters.Add(RawInt(TargetTranslateZHash, MapActiveOutput(record.Pose, SourceTranslateZIdHash)));
            supportBoneList.Objects.Add(targetSupportBone);
        }

        // BotW omits this node when no output uses two curves.
        if (outputDoubleList.Objects.Count == 0)
            data.Children.Remove(outputDoubleList);

        var header = new BotwAampWriter.AampObjectNode("support_bone_header", SupportBoneHeaderHash);
        var sourceStep = sourceHeader.Find(HeaderStepHash);
        if (sourceStep != null)
            header.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(sourceStep.Hash, sourceStep.Type, sourceStep.Bytes));
        else
            header.Parameters.Add(RawFloat(HeaderStepHash, 0.1f));
        header.Parameters.Add(RawInt(BoneCountHash, boneList.Objects.Count));
        header.Parameters.Add(RawInt(UnknownHeaderCountHash, 0));
        header.Parameters.Add(RawInt(CurveCountHash, curveList.Objects.Count));
        header.Parameters.Add(RawInt(SingleOutputCountHash, outputSingleList.Objects.Count));
        header.Parameters.Add(RawInt(DoubleOutputCountHash, outputDoubleList.Objects.Count));
        header.Parameters.Add(RawInt(MainBoneCountHash, mainBoneList.Objects.Count));
        header.Parameters.Add(RawInt(SupportBoneCountHash, supportBoneList.Objects.Count));
        root.Objects.Add(header);

        return BotwAampWriter.WriteArchive(
            "physsb",
            root,
            source.ArchiveVersion,
            source.FormatVersion,
            source.ParameterIoVersion);
    }


    private static void AddRequiredBones(ICollection<int> destination, IEnumerable<int> sourceBoneIds)
    {
        foreach (var sourceBoneId in sourceBoneIds)
        {
            if (sourceBoneId >= 0 && !destination.Contains(sourceBoneId))
                destination.Add(sourceBoneId);
        }
    }

    private static int PackBoneAttribute(int driverIndex, int sourceAttribute)
    {
        if (driverIndex is < 0 or > ushort.MaxValue)
            throw new InvalidDataException($"BPHHB driver index {driverIndex} is outside the BPHYSSB packing range.");

        // phhb and physsb use compact channel IDs in different contexts.
        // Attributes 3-5 are translation X/Y/Z drivers. BPHYSSB stores the
        // corresponding axis in the same three compact channels as rotation.
        var channel = sourceAttribute switch
        {
            0 => 2,
            1 => 0,
            2 => 1,
            3 => 0,
            4 => 1,
            5 => 2,
            _ => throw new InvalidDataException($"Unsupported BPHHB curve attribute {sourceAttribute}.")
        };
        return (driverIndex << 16) | channel;
    }

    private static int EncodeCurveReference(int curveIndex) => curveIndex < 0 ? -1 : 0x0001_0000 | curveIndex;
    private static int EncodeDoubleOutputReference(int outputIndex) => outputIndex < 0 ? -1 : 0x0001_0000 | outputIndex;

    private static int MapOutputReference(
        AampReadObject pose,
        uint sourceIdHash,
        IReadOnlyDictionary<int, int> outputHandles)
    {
        var sourceOutput = ReadOptionalInt(pose, sourceIdHash);
        return sourceOutput >= 0 && outputHandles.TryGetValue(sourceOutput, out var targetOutput)
            ? targetOutput
            : -1;
    }

    private static void ValidateCurveReference(int curveIndex, int curveCount, int outputIndex)
    {
        if (curveIndex >= curveCount)
            throw new InvalidDataException($"BPHHB output #{outputIndex} references curve #{curveIndex}, but only {curveCount} curves exist.");
    }

    private static AampReadList RequireList(AampReadList parent, uint hash, string name) =>
        parent.FindChild(hash) ?? throw new InvalidDataException($"BPHHB is missing '{name}'.");

    private static void CopyParameter(BotwAampWriter.AampObjectNode target, AampReadObject source, uint sourceHash, uint? targetHash = null)
    {
        var parameter = source.Find(sourceHash)
            ?? throw new InvalidDataException($"BPHHB record is missing parameter 0x{sourceHash:X8}.");
        target.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(
            targetHash ?? parameter.Hash,
            parameter.Type,
            parameter.Bytes));
    }

    private static void CopyCurveKeys(BotwAampWriter.AampObjectNode target, AampReadObject source)
    {
        var keyCount = ReadOptionalInt(source, SourceKeyCountHash);

        // BotW stores three curve keys. TotK's four-key form folds its inner
        // tangents into the middle BotW key, as confirmed by native matches.
        if (keyCount == 4 &&
            source.Find(SourceKey0Hash) is { } key0 &&
            source.Find(SourceKey1Hash) is { } key1 &&
            source.Find(SourceKey3Hash) is { } key3)
        {
            var first = key1.AsVector4();
            var terminal = key3.AsVector4();
            target.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(SourceKey0Hash, key0.Type, key0.Bytes));
            target.Parameters.Add(RawVector4(SourceKey1Hash, new Vector4(first.X, first.Y, first.Z, terminal.Z)));
            target.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(SourceKey2Hash, key3.Type, key3.Bytes));
            return;
        }

        // Extend TotK's clamped two-key curves with a flat key on the missing
        // side instead of synthesizing a duplicate zero-valued endpoint.
        if (keyCount == 2 &&
            source.Find(SourceKey0Hash) is { } twoKey0 &&
            source.Find(SourceKey1Hash) is { } twoKey1)
        {
            var first = twoKey0.AsVector4();
            var second = twoKey1.AsVector4();
            const float epsilon = 1e-5f;
            var span = MathF.Max(MathF.Abs(second.X - first.X), 1e-3f);
            if (MathF.Abs(first.X) <= epsilon && second.X > epsilon)
            {
                target.Parameters.Add(RawVector4(SourceKey0Hash, new Vector4(first.X - span, first.Y, 0f, 0f)));
                target.Parameters.Add(RawVector4(SourceKey1Hash, new Vector4(first.X, first.Y, 0f, first.W)));
                target.Parameters.Add(RawVector4(SourceKey2Hash, second));
            }
            else
            {
                target.Parameters.Add(RawVector4(SourceKey0Hash, first));
                target.Parameters.Add(RawVector4(SourceKey1Hash, new Vector4(second.X, second.Y, second.Z, 0f)));
                target.Parameters.Add(RawVector4(SourceKey2Hash, new Vector4(second.X + span, second.Y, 0f, 0f)));
            }
            return;
        }

        CopyParameter(target, source, SourceKey0Hash);
        CopyParameter(target, source, SourceKey1Hash);
        CopyOptionalCurveKey(target, source, SourceKey2Hash);
    }

    private static void CopyOptionalCurveKey(BotwAampWriter.AampObjectNode target, AampReadObject source, uint sourceHash)
    {
        if (source.Find(sourceHash) is { } parameter)
        {
            target.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(
                parameter.Hash,
                parameter.Type,
                parameter.Bytes));
            return;
        }

        // Curve keys are hkVector4-style AAMP values. A scalar float has the
        // wrong byte width and changes the curve evaluator's interpretation.
        target.Parameters.Add(RawVector4(sourceHash, Vector4.Zero));
    }

    private static string ReadString(AampReadObject item, uint hash, string fallback) =>
        item.Find(hash)?.AsString() is { Length: > 0 } value ? value : fallback;

    private static int ReadRequiredInt(AampReadObject item, uint hash, string owner)
    {
        var parameter = item.Find(hash)
            ?? throw new InvalidDataException($"BPHHB '{owner}' record is missing parameter 0x{hash:X8}.");
        if (parameter.Type != 2)
            throw new InvalidDataException($"BPHHB '{owner}' parameter 0x{hash:X8} is not an integer link.");
        return parameter.AsInt32();
    }

    private static int ReadOptionalInt(AampReadObject item, uint hash) =>
        item.Find(hash) is { Type: 2 } parameter ? parameter.AsInt32() : -1;

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

    private static string? GetString(BotwAampWriter.AampObjectNode item, uint hash) =>
        FindParameter(item, hash) is { RawBytes: { } bytes }
            ? Encoding.UTF8.GetString(bytes).TrimEnd('\0')
            : null;

    private static Vector3? GetVector3(BotwAampWriter.AampObjectNode item, uint hash)
    {
        var bytes = FindParameter(item, hash)?.RawBytes;
        return bytes is { Length: >= 12 }
            ? new Vector3(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8))
            : null;
    }

    private static Vector4? GetVector4(BotwAampWriter.AampObjectNode item, uint hash)
    {
        var bytes = FindParameter(item, hash)?.RawBytes;
        return bytes is { Length: >= 16 }
            ? new Vector4(BitConverter.ToSingle(bytes, 0), BitConverter.ToSingle(bytes, 4), BitConverter.ToSingle(bytes, 8), BitConverter.ToSingle(bytes, 12))
            : null;
    }

    private static void SetInt(BotwAampWriter.AampObjectNode item, uint hash, int value) =>
        SetRaw(item, hash, 2, BitConverter.GetBytes(value));

    private static void SetExistingInt(BotwAampWriter.AampObjectNode item, uint hash, int value)
    {
        if (FindParameterIndex(item, hash) != null)
            SetInt(item, hash, value);
    }

    private static void SetString(BotwAampWriter.AampObjectNode item, uint hash, string value) =>
        SetRaw(item, hash, 15, Encoding.UTF8.GetBytes(value + "\0"));

    private static void SetVector3(BotwAampWriter.AampObjectNode item, uint hash, Vector3 value)
    {
        var bytes = new byte[12];
        BitConverter.GetBytes(value.X).CopyTo(bytes, 0);
        BitConverter.GetBytes(value.Y).CopyTo(bytes, 4);
        BitConverter.GetBytes(value.Z).CopyTo(bytes, 8);
        SetRaw(item, hash, 4, bytes);
    }

    private static void SetVector4(BotwAampWriter.AampObjectNode item, uint hash, Vector4 value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value.X).CopyTo(bytes, 0);
        BitConverter.GetBytes(value.Y).CopyTo(bytes, 4);
        BitConverter.GetBytes(value.Z).CopyTo(bytes, 8);
        BitConverter.GetBytes(value.W).CopyTo(bytes, 12);
        SetRaw(item, hash, 5, bytes);
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
                values.Add(Encoding.UTF8.GetString(bytes, cursor, next - cursor));
            cursor = next + 1;
        }

        return values;
    }

    private static BotwAampWriter.AampParameterNode RawInt(uint hash, int value) =>
        BotwAampWriter.AampParameterNode.Raw(hash, 2, BitConverter.GetBytes(value));

    private static BotwAampWriter.AampParameterNode RawFloat(uint hash, float value) =>
        BotwAampWriter.AampParameterNode.Raw(hash, 1, BitConverter.GetBytes(value));

    private static BotwAampWriter.AampParameterNode RawVector4(uint hash, Vector4 value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value.X).CopyTo(bytes, 0);
        BitConverter.GetBytes(value.Y).CopyTo(bytes, 4);
        BitConverter.GetBytes(value.Z).CopyTo(bytes, 8);
        BitConverter.GetBytes(value.W).CopyTo(bytes, 12);
        return BotwAampWriter.AampParameterNode.Raw(hash, 5, bytes);
    }

    private static BotwAampWriter.AampParameterNode RawBool(uint hash, bool value) =>
        BotwAampWriter.AampParameterNode.Raw(hash, 0, BitConverter.GetBytes(value ? 1 : 0));

    private static BotwAampWriter.AampParameterNode RawString(uint hash, byte type, string value) =>
        BotwAampWriter.AampParameterNode.Raw(hash, type, Encoding.UTF8.GetBytes(value + "\0"));

    private sealed record SourceDriver(int SourceIndex, int BoneId, AampReadObject Object);
    private sealed record SourceDriven(int SourceIndex, int BoneId, AampReadObject Object, AampReadObject Pose);
}

public sealed record BphyssbExportSummary(
    string Path,
    int FileSize,
    int BoneCount,
    int CurveCount,
    int MainBoneCount,
    int SupportBoneCount);
