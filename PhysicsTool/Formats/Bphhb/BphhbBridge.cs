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
    internal static BphhbDocumentSummary EditConnections(BphhbDocumentSummary document, Action<BotwAampWriter.AampListNode> edit) =>
        Rebuild(document, root => edit(RequireDataRoot(root)));
    private const int HeaderSize = 0x30;
    private const uint BoneIdHash = 3407736066u;
    private const uint BaseBoneIdHash = 4279981158u;
    private const uint DriverListHash = 3993686733u;
    private const uint DrivenListHash = 1256621249u;
    private const uint PoseDrivenListHash = 1386686749u;
    private const uint NameHash = 1579384326u;
    private const uint CurveDriverHash = 249727330u;
    private const uint CurveAttributeHash = 75375380u;
    private const uint CurveKeyCountHash = 139113150u;
    private const uint CurveKey0Hash = 1692570776u;
    private const uint CurveKey1Hash = 333816846u;
    private const uint CurveKey2Hash = 2330785204u;
    private const uint OutputConnection0Hash = 2390703269u;
    private const uint OutputConnection1Hash = 918772672u;
    private const uint BaseTranslationHash = 4181521146u;
    private const uint BaseRotationHash = 1609157221u;
    private const uint AimAxisHash = 3442436866u;
    private const uint UpAxisHash = 907422927u;
    private const uint RollOutputHash = 2869652774u;
    private const uint BendHOutputHash = 3079588026u;
    private const uint BendVOutputHash = 122218518u;
    private const uint TranslateXOutputHash = 3595547632u;
    private const uint TranslateYOutputHash = 1861473429u;
    private const uint TranslateZOutputHash = 2084993915u;

    private const uint SupportHeaderStepHash = 423031007u;
    private const uint SupportBoneAttributeHash = 554147540u;
    private const uint SupportConnectionHash = 704082790u;
    private const uint SupportConnection0Hash = 2693310104u;
    private const uint SupportConnection1Hash = 3616511502u;
    private const uint SupportBoneHash = 893828983u;
    private const uint SupportAimHash = 828945678u;
    private const uint SupportUpHash = 1133833840u;
    private const uint SupportSpaceHash = 695386426u;
    private const uint SupportBendHHash = 318199082u;
    private const uint SupportBendVHash = 3908593737u;
    private const uint SupportRollHash = 783626958u;
    private const uint SupportTranslateXHash = 2040119991u;
    private const uint SupportTranslateYHash = 245297697u;
    private const uint SupportTranslateZHash = 2543297435u;

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

        AtomicFile.WriteAllBytes(outputPath, document.Bytes);
        return Load(outputPath);
    }

    /// <summary>
    /// Rebuilds a TotK helper-bone graph from BotW's native support-bone graph.
    /// Explicit reference spaces are retained. Parent spaces require the
    /// model skeleton; unresolved references are never inferred from names.
    /// </summary>
    public static BphhbDocumentSummary Export(BphyssbDocumentSummary source, string outputPath)
        => ExportWithParents(source, outputPath, null);

    public static IReadOnlyList<string> GetConversionWarnings(BphyssbDocumentSummary source)
    {
        var data = source.Archive?.Root.Children.FirstOrDefault(child => child.FindChild("connection_curve_list") != null);
        var curves = data?.FindChild("connection_curve_list")?.Objects;
        if (curves == null) return Array.Empty<string>();
        var extrapolated = curves.Count(curve =>
            (!(curve.Find("constant_in")?.AsBoolean() ?? true) && curve.Find(CurveKey0Hash)?.AsVector4().Z != 0f) ||
            (!(curve.Find("constant_out")?.AsBoolean() ?? true) && curve.Find(CurveKey2Hash)?.AsVector4().Z != 0f));
        return extrapolated == 0 ? Array.Empty<string>() : new[] {
            $"{extrapolated} support curve(s) extrapolate beyond their authored key range. BPHHB preview clamps outside that range; those extreme poses need in-game verification." };
    }

    public static BphhbDocumentSummary ExportWithParents(
        BphyssbDocumentSummary source,
        string outputPath,
        IReadOnlyDictionary<string, string>? parentBones)
    {
        if (!Path.GetExtension(outputPath).Equals(".bphhb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("BPHHB exports must use the .bphhb extension.");

        var bytes = BuildFromBphyssb(source, parentBones);
        var validated = LoadBytes(bytes, outputPath);
        if (validated.Graph == null)
            throw new InvalidDataException("The converted BPHHB could not be decoded.");
        AtomicFile.WriteAllBytes(outputPath, bytes);
        var result = Load(outputPath);
        if (result.Graph == null)
            throw new InvalidDataException("The converted BPHHB could not be decoded after it was written.");
        return result;
    }

    private static byte[] BuildFromBphyssb(BphyssbDocumentSummary source, IReadOnlyDictionary<string, string>? parentBones)
    {
        var archive = source.Archive
            ?? throw new InvalidOperationException("This BPHYSSB file could not be decoded into an editable AAMP graph.");
        if (!string.Equals(archive.Type, "physsb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected a physsb AAMP archive, found '{archive.Type}'.");

        var sourceData = archive.Root.Children.FirstOrDefault(child =>
            child.FindChild(BotwAampWriter.Crc32("bone_list")) != null &&
            child.FindChild(BotwAampWriter.Crc32("connection_curve_list")) != null &&
            child.FindChild(BotwAampWriter.Crc32("main_bone_list")) != null &&
            child.FindChild(BotwAampWriter.Crc32("support_bone_list")) != null)
            ?? throw new InvalidDataException("BPHYSSB does not contain a complete support-bone graph.");

        var sourceBones = RequireSourceList(sourceData, "bone_list");
        var sourceCurves = RequireSourceList(sourceData, "connection_curve_list");
        var sourceSingles = sourceData.FindChild(BotwAampWriter.Crc32("output_single_list"));
        var sourceDoubles = sourceData.FindChild(BotwAampWriter.Crc32("output_double_list"));
        var sourceMain = RequireSourceList(sourceData, "main_bone_list");
        var sourceSupport = RequireSourceList(sourceData, "support_bone_list");
        var sourceBoneNames = sourceBones.Objects
            .Select((item, index) => item.Find(NameHash)?.AsString()
                ?? throw new InvalidDataException($"BPHYSSB bone #{index} is missing its name."))
            .ToList();

        var root = new BotwAampWriter.AampListNode("param_root");
        var header = new BotwAampWriter.AampObjectNode("helper_bone_header");
        var sourceHeader = archive.Root.Objects.FirstOrDefault();
        if (sourceHeader?.Find(SupportHeaderStepHash) is { } step)
            header.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(step.Hash, step.Type, step.Bytes));
        else
            header.Parameters.Add(RawFloat(SupportHeaderStepHash, 0.1f));
        root.Objects.Add(header);

        // A native BPHHB always carries these nine child tables in this order.
        // Together with param_root and helper_bone_data they produce the
        // 11-list layout expected by TotK, even when some tables are empty.
        var data = new BotwAampWriter.AampListNode("helper_bone_data");
        root.Children.Add(data);
        var bones = new BotwAampWriter.AampListNode("bone_list");
        var curves = new BotwAampWriter.AampListNode("connection_curve_list");
        var outputs = new BotwAampWriter.AampListNode("output_list");
        var drivers = new BotwAampWriter.AampListNode("driver_bone_list");
        var driven = new BotwAampWriter.AampListNode("driven_bone_list");
        var poses = new BotwAampWriter.AampListNode("pose_driven_list");
        var positions = new BotwAampWriter.AampListNode("position_driven_list");
        var rotations = new BotwAampWriter.AampListNode("rotate_driven_list");
        var aims = new BotwAampWriter.AampListNode("aim_driven_list");
        data.Children.Add(bones);
        data.Children.Add(curves);
        data.Children.Add(outputs);
        data.Children.Add(drivers);
        data.Children.Add(driven);
        data.Children.Add(poses);
        data.Children.Add(positions);
        data.Children.Add(rotations);
        data.Children.Add(aims);

        foreach (var sourceBone in sourceBones.Objects)
        {
            var targetBone = new BotwAampWriter.AampObjectNode($"bone_{bones.Objects.Count}");
            CopyAampParameter(targetBone, sourceBone, NameHash);
            bones.Objects.Add(targetBone);
        }

        int GetOrAddBone(string name)
        {
            static string LocalName(string value)
            {
                var separator = value.LastIndexOf(':');
                return separator >= 0 ? value[(separator + 1)..] : value;
            }

            var existing = sourceBoneNames.FindIndex(item => string.Equals(item, name, StringComparison.Ordinal));
            if (existing >= 0)
                return existing;

            var localName = LocalName(name);
            var localMatches = sourceBoneNames
                .Select((item, index) => (Name: LocalName(item), Index: index))
                .Where(item => string.Equals(item.Name, localName, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (localMatches.Length == 1)
                return localMatches[0].Index;
            if (localMatches.Length > 1)
                throw new InvalidDataException($"Reference bone '{name}' matches multiple namespaces; an exact bone name is required.");

            if (!name.Contains(':'))
            {
                var prefixes = sourceBoneNames.Where(item => item.Contains(':'))
                    .Select(item => item[..(item.LastIndexOf(':') + 1)])
                    .Distinct(StringComparer.Ordinal).Take(2).ToArray();
                if (prefixes.Length > 1)
                    throw new InvalidDataException($"Reference bone '{name}' has no namespace in a multi-namespace skeleton.");
                if (prefixes.Length == 1)
                    name = prefixes[0] + name;
            }

            var targetBone = new BotwAampWriter.AampObjectNode($"bone_{bones.Objects.Count}");
            targetBone.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(
                NameHash,
                15,
                Encoding.UTF8.GetBytes(name + "\0")));
            sourceBoneNames.Add(name);
            bones.Objects.Add(targetBone);
            return bones.Objects.Count - 1;
        }

        int ResolveBase(AampReadObject record, int boneId)
        {
            var space = ReadOptionalAampInt(record, SupportSpaceHash);
            if (space >= 0 && space < sourceBones.Objects.Count && space != boneId)
                return space;

            var boneName = boneId >= 0 && boneId < sourceBoneNames.Count
                ? sourceBoneNames[boneId]
                : string.Empty;
            if (space == -1 && parentBones != null)
            {
                static string Local(string name) => name[(name.LastIndexOf(':') + 1)..];
                string? parent = null;
                if (!parentBones.TryGetValue(boneName, out parent))
                {
                    var matches = parentBones.Where(pair => string.Equals(Local(pair.Key), Local(boneName), StringComparison.Ordinal)).Take(2).ToArray();
                    if (matches.Length == 1)
                        parent = matches[0].Value;
                }
                if (!string.IsNullOrWhiteSpace(parent) && !string.Equals(Local(parent), Local(boneName), StringComparison.Ordinal))
                    return GetOrAddBone(parent);
            }
            throw new InvalidDataException(space == -1
                ? $"Cannot resolve the parent of '{boneName}'. Load its matching model before exporting to BPHHB."
                : $"Bone '{boneName}' uses unsupported or invalid reference space {space}; BPHHB export cannot safely replace it.");
        }

        for (var index = 0; index < sourceMain.Objects.Count; index++)
        {
            var sourceDriver = sourceMain.Objects[index];
            var targetDriver = new BotwAampWriter.AampObjectNode($"driver_bone_{index}");
            var boneId = ReadRequiredAampInt(sourceDriver, SupportBoneHash, "main_bone_list");
            targetDriver.Parameters.Add(RawInt(BoneIdHash, boneId));
            CopyAampParameter(targetDriver, sourceDriver, BaseTranslationHash);
            CopyAampParameter(targetDriver, sourceDriver, BaseRotationHash);
            CopyAampParameter(targetDriver, sourceDriver, SupportAimHash, AimAxisHash);
            CopyAampParameter(targetDriver, sourceDriver, SupportUpHash, UpAxisHash);
            targetDriver.Parameters.Add(RawInt(BaseBoneIdHash, ResolveBase(sourceDriver, boneId)));
            drivers.Objects.Add(targetDriver);
        }

        for (var index = 0; index < sourceCurves.Objects.Count; index++)
        {
            var sourceCurve = sourceCurves.Objects[index];
            var packed = ReadRequiredAampInt(sourceCurve, SupportBoneAttributeHash, "connection_curve_list");
            var driverIndex = (int)((uint)packed >> 16);
            if (driverIndex < 0 || driverIndex >= sourceMain.Objects.Count)
                throw new InvalidDataException($"BPHYSSB curve #{index} references driver #{driverIndex}, but only {sourceMain.Objects.Count} driver records exist.");

            var targetCurve = new BotwAampWriter.AampObjectNode($"connection_curve_{index}");
            targetCurve.Parameters.Add(RawInt(CurveDriverHash, driverIndex));
            targetCurve.Parameters.Add(RawInt(CurveAttributeHash, UnpackBoneAttribute(packed & 0xFFFF, index)));
            targetCurve.Parameters.Add(RawInt(CurveKeyCountHash, 3));
            CopyAampParameter(targetCurve, sourceCurve, CurveKey0Hash);
            CopyAampParameter(targetCurve, sourceCurve, CurveKey1Hash);
            CopyAampParameter(targetCurve, sourceCurve, CurveKey2Hash);
            curves.Objects.Add(targetCurve);
        }

        var outputMap = new Dictionary<int, int>();
        var weightedCurves = new Dictionary<(int Curve, float Weight), int>();

        int MapWeightedCurve(AampReadObject output, uint connectionHash, string weightName)
        {
            var curveIndex = DecodeCurveReference(ReadRequiredAampInt(output, connectionHash, "output"));
            if (curveIndex < 0) return -1;
            if (curveIndex >= sourceCurves.Objects.Count)
                throw new InvalidDataException($"BPHYSSB output references missing curve #{curveIndex}.");
            var weight = output.Find(weightName)?.AsSingle() ?? 0f;
            if (!float.IsFinite(weight)) throw new InvalidDataException("BPHYSSB output has a non-finite weight.");
            if (weight == 1f) return curveIndex;
            if (weightedCurves.TryGetValue((curveIndex, weight), out var existing)) return existing;

            // BPHHB sums connections without a weight field. Bake each output's
            // weight into its own Hermite values/tangents, not the shared source curve.
            var original = sourceCurves.Objects[curveIndex];
            var packed = ReadRequiredAampInt(original, SupportBoneAttributeHash, "connection_curve_list");
            var target = new BotwAampWriter.AampObjectNode($"connection_curve_{curves.Objects.Count}");
            target.Parameters.Add(RawInt(CurveDriverHash, (int)((uint)packed >> 16)));
            target.Parameters.Add(RawInt(CurveAttributeHash, UnpackBoneAttribute(packed & 0xFFFF, curveIndex)));
            target.Parameters.Add(RawInt(CurveKeyCountHash, 3));
            foreach (var hash in new[] { CurveKey0Hash, CurveKey1Hash, CurveKey2Hash })
            {
                var key = original.Find(hash)?.AsVector4() ?? throw new InvalidDataException("Support curve is missing a key.");
                var values = new[] { key.X, key.Y * weight, key.Z * weight, key.W * weight };
                if (values.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Weighted support curve is non-finite.");
                target.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(hash, 5, values.SelectMany(BitConverter.GetBytes).ToArray()));
            }
            var index = curves.Objects.Count;
            curves.Objects.Add(target);
            weightedCurves.Add((curveIndex, weight), index);
            return index;
        }

        int MapActiveOutput(int handle)
        {
            if (handle < 0)
                return -1;
            if (outputMap.TryGetValue(handle, out var existing))
                return existing;

            int connection0;
            int connection1;
            if (((uint)handle & 0xFFFF0000u) == 0x00010000u)
            {
                var index = handle & 0xFFFF;
                if (sourceDoubles == null || index >= sourceDoubles.Objects.Count)
                    throw new InvalidDataException($"BPHYSSB references missing double output #{index}.");
                var sourceOutput = sourceDoubles.Objects[index];
                connection0 = MapWeightedCurve(sourceOutput, SupportConnection0Hash, "weight_0");
                connection1 = MapWeightedCurve(sourceOutput, SupportConnection1Hash, "weight_1");
            }
            else
            {
                if (sourceSingles == null || handle >= sourceSingles.Objects.Count)
                    throw new InvalidDataException($"BPHYSSB references missing single output #{handle}.");
                var sourceOutput = sourceSingles.Objects[handle];
                connection0 = MapWeightedCurve(sourceOutput, SupportConnectionHash, "weight");
                connection1 = -1;
            }

            if (connection0 < 0 && connection1 < 0)
                return -1;
            if (connection0 >= curves.Objects.Count || connection1 >= curves.Objects.Count)
                throw new InvalidDataException($"BPHYSSB output 0x{handle:X8} references a curve outside the converted curve list.");

            var targetOutput = new BotwAampWriter.AampObjectNode($"output_{outputs.Objects.Count}");
            if (connection0 >= 0)
                targetOutput.Parameters.Add(RawInt(OutputConnection0Hash, connection0));
            else
                targetOutput.Parameters.Add(RawInt(OutputConnection0Hash, connection1));
            if (connection0 >= 0 && connection1 >= 0)
                targetOutput.Parameters.Add(RawInt(OutputConnection1Hash, connection1));

            var targetIndex = outputs.Objects.Count;
            outputs.Objects.Add(targetOutput);
            outputMap.Add(handle, targetIndex);
            return targetIndex;
        }

        for (var index = 0; index < sourceSupport.Objects.Count; index++)
        {
            var sourceBone = sourceSupport.Objects[index];
            var boneId = ReadRequiredAampInt(sourceBone, SupportBoneHash, "support_bone_list");
            var rollOutput = MapActiveOutput(ReadOptionalAampInt(sourceBone, SupportRollHash));
            var bendHOutput = MapActiveOutput(ReadOptionalAampInt(sourceBone, SupportBendHHash));
            var bendVOutput = MapActiveOutput(ReadOptionalAampInt(sourceBone, SupportBendVHash));
            var translateXOutput = MapActiveOutput(ReadOptionalAampInt(sourceBone, SupportTranslateXHash));
            var translateYOutput = MapActiveOutput(ReadOptionalAampInt(sourceBone, SupportTranslateYHash));
            var translateZOutput = MapActiveOutput(ReadOptionalAampInt(sourceBone, SupportTranslateZHash));
            var hasRotation = rollOutput >= 0 || bendHOutput >= 0 || bendVOutput >= 0;
            var hasTranslation = translateXOutput >= 0 || translateYOutput >= 0 || translateZOutput >= 0;

            var targetDriven = new BotwAampWriter.AampObjectNode($"driven_bone_{index}");
            targetDriven.Parameters.Add(RawInt(BoneIdHash, boneId));
            if (hasTranslation)
            {
                targetDriven.Parameters.Add(RawInt(BotwAampWriter.Crc32("translate_driven_type"), 0));
                targetDriven.Parameters.Add(RawInt(BotwAampWriter.Crc32("translate_driven_id"), index));
            }
            if (hasRotation)
            {
                targetDriven.Parameters.Add(RawInt(BotwAampWriter.Crc32("rotate_driven_type"), 0));
                targetDriven.Parameters.Add(RawInt(BotwAampWriter.Crc32("rotate_driven_id"), index));
            }
            driven.Objects.Add(targetDriven);

            var targetPose = new BotwAampWriter.AampObjectNode($"pose_driven_{index}");
            CopyAampParameter(targetPose, sourceBone, BaseTranslationHash);
            CopyAampParameter(targetPose, sourceBone, BaseRotationHash);
            CopyAampParameter(targetPose, sourceBone, SupportAimHash, AimAxisHash);
            CopyAampParameter(targetPose, sourceBone, SupportUpHash, UpAxisHash);
            targetPose.Parameters.Add(RawInt(BaseBoneIdHash, ResolveBase(sourceBone, boneId)));
            AddPoseOutput(targetPose, rollOutput, RollOutputHash, SupportRollHash);
            AddPoseOutput(targetPose, bendHOutput, BendHOutputHash, SupportBendHHash);
            AddPoseOutput(targetPose, bendVOutput, BendVOutputHash, SupportBendVHash);
            AddPoseOutput(targetPose, translateXOutput, TranslateXOutputHash, SupportTranslateXHash);
            AddPoseOutput(targetPose, translateYOutput, TranslateYOutputHash, SupportTranslateYHash);
            AddPoseOutput(targetPose, translateZOutput, TranslateZOutputHash, SupportTranslateZHash);
            poses.Objects.Add(targetPose);
        }

        AddHeaderCount(header, "bone_num", bones.Objects.Count);
        AddHeaderCount(header, "connection_curve_num", curves.Objects.Count);
        AddHeaderCount(header, "output_num", outputs.Objects.Count);
        AddHeaderCount(header, "driver_bone_num", drivers.Objects.Count);
        AddHeaderCount(header, "driven_bone_num", driven.Objects.Count);
        AddHeaderCount(header, "pose_driven_num", poses.Objects.Count);
        AddHeaderCount(header, "position_driven_num", positions.Objects.Count);
        AddHeaderCount(header, "rotate_driven_num", rotations.Objects.Count);
        AddHeaderCount(header, "aim_driven_num", aims.Objects.Count);

        return BotwAampWriter.WriteArchive(
            "phhb",
            root,
            source.ArchiveVersion,
            source.FormatVersion,
            source.ParameterIoVersion);
    }

    private static void AddPoseOutput(
        BotwAampWriter.AampObjectNode pose,
        int output,
        uint outputHash,
        uint defaultHash)
    {
        pose.Parameters.Add(output >= 0
            ? RawInt(outputHash, output)
            : RawInt(defaultHash, 0));
    }
    private static AampReadList RequireSourceList(AampReadList parent, string name) =>
        parent.FindChild(BotwAampWriter.Crc32(name))
        ?? throw new InvalidDataException($"BPHYSSB is missing '{name}'.");

    private static int UnpackBoneAttribute(int channel, int curveIndex) => channel switch
    {
        2 => 0,
        0 => 1,
        1 => 2,
        3 => 3,
        4 => 4,
        5 => 5,
        _ => throw new InvalidDataException($"BPHYSSB curve #{curveIndex} uses unsupported channel {channel}.")
    };

    private static int DecodeCurveReference(int reference)
    {
        if (reference < 0)
            return -1;
        if (((uint)reference & 0xFFFF0000u) == 0x00010000u)
            return reference & 0xFFFF;
        throw new InvalidDataException("This support output uses a linear or unsupported connection. BPHHB export cannot replace it with an unrelated curve.");
    }
    private static int ReadRequiredAampInt(AampReadObject record, uint hash, string owner)
    {
        var value = record.Find(hash);
        if (value is not { Type: 2 })
            throw new InvalidDataException($"BPHYSSB '{owner}' is missing integer parameter 0x{hash:X8}.");
        return value.AsInt32();
    }

    private static int ReadOptionalAampInt(AampReadObject record, uint hash) =>
        record.Find(hash) is { Type: 2 } value ? value.AsInt32() : -1;

    private static void CopyAampParameter(BotwAampWriter.AampObjectNode target, AampReadObject source, uint sourceHash, uint? targetHash = null)
    {
        var value = source.Find(sourceHash)
            ?? throw new InvalidDataException($"BPHYSSB record is missing parameter 0x{sourceHash:X8}.");
        target.Parameters.Add(BotwAampWriter.AampParameterNode.Raw(targetHash ?? value.Hash, value.Type, value.Bytes));
    }

    private static void AddHeaderCount(BotwAampWriter.AampObjectNode header, string name, int value) =>
        header.Parameters.Add(RawInt(BotwAampWriter.Crc32(name), value));

    private static BotwAampWriter.AampParameterNode RawInt(uint hash, int value) =>
        BotwAampWriter.AampParameterNode.Raw(hash, 2, BitConverter.GetBytes(value));

    private static BotwAampWriter.AampParameterNode RawFloat(uint hash, float value) =>
        BotwAampWriter.AampParameterNode.Raw(hash, 1, BitConverter.GetBytes(value));
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
                foreach (var record in driven.Objects.Where(o => GetInt(o, BoneIdHash) == boneIndex))
                {
                    var poseObject = SidecarConnectionEditing.Pose(data, boneIndex, true, isolate: true);
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
                    if (pose == null) throw new InvalidDataException("The helper has no pose table.");
                    var sourcePoseIndex = SidecarConnectionEditing.SinglePose(entry.item, pose.Objects.Count);

                    var targetPoseIndex = pose.Objects.Count;
                    var clonedDriven = CloneObject(entry.item, $"driven_bone_{driven.Objects.Count}");
                    SetInt(clonedDriven, BoneIdHash, newBoneIndex);
                    RemapPoseDrivenId(clonedDriven, BotwAampWriter.Crc32("translate_driven_id"), targetPoseIndex);
                    RemapPoseDrivenId(clonedDriven, BotwAampWriter.Crc32("rotate_driven_id"), targetPoseIndex);
                    RemapPoseDrivenId(clonedDriven, BotwAampWriter.Crc32("aim_driven_id"), targetPoseIndex);

                    var clonedPose = CloneObject(pose.Objects[sourcePoseIndex], $"pose_driven_{targetPoseIndex}");
                    foreach (var outputHash in PoseOutputHashes)
                        CloneDrivenOutput(clonedPose, outputHash, outputs, curves, outputMap, curveMap, sourceBoneIndex, newBoneIndex);

                    driven.Objects.Add(clonedDriven);
                    pose.Objects.Add(clonedPose);
                }
            }
        });
    }

    /// <summary>
    /// Creates a new affected bone from a known-good driven graph. Existing
    /// driver records are retained; pose outputs and curves are cloned so the
    /// new support can be edited independently later.
    /// </summary>
    public static (BphhbDocumentSummary Document, int TargetBoneIndex) CreateSupportFromBone(
        BphhbDocumentSummary document,
        int sourceBoneIndex,
        string targetBoneName)
    {
        var targetBoneIndex = -1;
        var rebuilt = Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var bones = RequireList(data, "bone_list");
            var driven = RequireList(data, "driven_bone_list");
            var poses = RequireList(data, "pose_driven_list");
            var outputs = RequireList(data, "output_list");
            var curves = RequireList(data, "connection_curve_list");
            if (sourceBoneIndex < 0 || sourceBoneIndex >= bones.Objects.Count)
                throw new ArgumentOutOfRangeException(nameof(sourceBoneIndex));

            var sourceRecords = driven.Objects
                .Select((item, index) => (item, index))
                .Where(entry => GetInt(entry.item, BoneIdHash) == sourceBoneIndex)
                .ToArray();
            if (sourceRecords.Length == 0)
                throw new InvalidOperationException("The selected bone has no driven support graph to clone.");

            targetBoneIndex = bones.Objects.FindIndex(item =>
                string.Equals(GetString(item, "name"), targetBoneName, StringComparison.Ordinal));
            if (targetBoneIndex < 0)
            {
                targetBoneIndex = bones.Objects.Count;
                var targetBone = CloneObject(bones.Objects[sourceBoneIndex], $"bone_{targetBoneIndex}");
                SetString(targetBone, "name", targetBoneName);
                bones.Objects.Add(targetBone);
            }
            else if (targetBoneIndex == sourceBoneIndex)
            {
                throw new InvalidOperationException("Choose a different target bone name.");
            }

            if (driven.Objects.Any(item => GetInt(item, BoneIdHash) == targetBoneIndex))
                throw new InvalidOperationException($"{targetBoneName} already has support data in this file.");

            var outputMap = new Dictionary<int, int>();
            var curveMap = new Dictionary<int, int>();
            foreach (var sourceRecord in sourceRecords)
            {
                var sourcePoseIndex = SidecarConnectionEditing.SinglePose(sourceRecord.item, poses.Objects.Count);

                var targetPoseIndex = poses.Objects.Count;
                var clonedDriven = CloneObject(sourceRecord.item, $"driven_bone_{driven.Objects.Count}");
                SetInt(clonedDriven, BoneIdHash, targetBoneIndex);
                RemapPoseDrivenId(clonedDriven, BotwAampWriter.Crc32("translate_driven_id"), targetPoseIndex);
                RemapPoseDrivenId(clonedDriven, BotwAampWriter.Crc32("rotate_driven_id"), targetPoseIndex);
                RemapPoseDrivenId(clonedDriven, BotwAampWriter.Crc32("aim_driven_id"), targetPoseIndex);

                var clonedPose = CloneObject(poses.Objects[sourcePoseIndex], $"pose_driven_{targetPoseIndex}");
                foreach (var outputHash in PoseOutputHashes)
                    CloneDrivenOutput(clonedPose, outputHash, outputs, curves, outputMap, curveMap, int.MinValue, targetBoneIndex);

                driven.Objects.Add(clonedDriven);
                poses.Objects.Add(clonedPose);
            }
        });

        return (rebuilt, targetBoneIndex);
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
            // Curves refer to driver-record indices, not entries in bone_list.
            // Reordering the bone table does not reorder driver records.
        });
    }

    public static IReadOnlyList<SidecarDriverGroup> GetDriverGroups(BphhbDocumentSummary document) =>
        BuildDriverGroups(document).Select(group => group.Summary).ToArray();

    /// <summary>
    /// Imports one connected helper-bone driver graph. Bone names are matched
    /// against the target, while driver, curve, output, driven, and pose indices
    /// are rebuilt in the target's native tables.
    /// </summary>
    public static BphhbDocumentSummary MergeDriverGroup(
        BphhbDocumentSummary target,
        BphhbDocumentSummary source,
        int groupIndex)
    {
        var sourceGroups = BuildDriverGroups(source);
        var group = sourceGroups.ElementAtOrDefault(groupIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(groupIndex));
        var sourceArchive = source.Archive
            ?? throw new InvalidOperationException("The reference BPHHB graph could not be decoded.");
        var sourceData = sourceArchive.Root.Children.SingleOrDefault()
            ?? throw new InvalidDataException("The reference BPHHB has no helper-bone data root.");

        return Rebuild(target, root =>
        {
            var targetData = RequireDataRoot(root);
            var targetBones = RequireList(targetData, "bone_list");
            var targetCurves = RequireList(targetData, "connection_curve_list");
            var targetOutputs = RequireList(targetData, "output_list");
            var targetDrivers = RequireList(targetData, "driver_bone_list");
            var targetDriven = RequireList(targetData, "driven_bone_list");
            var targetPoses = RequireList(targetData, "pose_driven_list");

            var sourceBones = sourceData.FindChild("bone_list")
                ?? throw new InvalidDataException("The reference BPHHB has no bone_list.");
            var sourceCurves = sourceData.FindChild("connection_curve_list")
                ?? throw new InvalidDataException("The reference BPHHB has no connection_curve_list.");
            var sourceOutputs = sourceData.FindChild("output_list")
                ?? throw new InvalidDataException("The reference BPHHB has no output_list.");
            var sourceDrivers = sourceData.FindChild("driver_bone_list")
                ?? throw new InvalidDataException("The reference BPHHB has no driver_bone_list.");
            var sourceDriven = sourceData.FindChild("driven_bone_list")
                ?? throw new InvalidDataException("The reference BPHHB has no driven_bone_list.");
            var sourcePoses = sourceData.FindChild("pose_driven_list")
                ?? throw new InvalidDataException("The reference BPHHB has no pose_driven_list.");

            var boneMap = new Dictionary<int, int>();
            int MapBone(int sourceBoneIndex)
            {
                if (sourceBoneIndex < 0)
                    return -1;
                if (boneMap.TryGetValue(sourceBoneIndex, out var mapped))
                    return mapped;
                if (sourceBoneIndex >= sourceBones.Objects.Count)
                    throw new InvalidDataException($"The reference driver group uses missing bone #{sourceBoneIndex}.");

                var sourceBone = sourceBones.Objects[sourceBoneIndex];
                var name = sourceBone.Find(NameHash)?.AsString() ?? $"bone_{sourceBoneIndex}";
                for (var index = 0; index < targetBones.Objects.Count; index++)
                {
                    if (string.Equals(GetString(targetBones.Objects[index], "name"), name, StringComparison.Ordinal))
                    {
                        boneMap[sourceBoneIndex] = index;
                        return index;
                    }
                }

                var targetIndex = targetBones.Objects.Count;
                targetBones.Objects.Add(CloneObject(sourceBone, $"bone_{targetIndex}"));
                boneMap[sourceBoneIndex] = targetIndex;
                return targetIndex;
            }

            foreach (var boneIndex in group.RequiredBoneIndices)
                MapBone(boneIndex);

            var driverMap = new Dictionary<int, int>();
            foreach (var sourceDriverIndex in group.DriverIndices)
            {
                var sourceDriver = sourceDrivers.Objects[sourceDriverIndex];
                var sourceBoneIndex = sourceDriver.Find(BoneIdHash)?.AsInt32() ?? -1;
                var targetBoneIndex = MapBone(sourceBoneIndex);
                var existing = targetDrivers.Objects.FindIndex(item => GetInt(item, BoneIdHash) == targetBoneIndex);
                if (existing >= 0)
                {
                    driverMap[sourceDriverIndex] = existing;
                    continue;
                }

                var cloned = CloneObject(sourceDriver, $"driver_bone_{targetDrivers.Objects.Count}");
                SetInt(cloned, BoneIdHash, targetBoneIndex);
                SetInt(cloned, BaseBoneIdHash, MapBone(sourceDriver.Find(BaseBoneIdHash)?.AsInt32() ?? -1));
                driverMap[sourceDriverIndex] = targetDrivers.Objects.Count;
                targetDrivers.Objects.Add(cloned);
            }

            var curveMap = new Dictionary<int, int>();
            foreach (var sourceCurveIndex in group.CurveIndices)
            {
                var cloned = CloneObject(sourceCurves.Objects[sourceCurveIndex], $"connection_curve_{targetCurves.Objects.Count}");
                var sourceDriverIndex = sourceCurves.Objects[sourceCurveIndex].Find(CurveDriverHash)?.AsInt32() ?? -1;
                if (!driverMap.TryGetValue(sourceDriverIndex, out var targetDriverIndex))
                    throw new InvalidDataException($"Curve #{sourceCurveIndex} escapes the selected driver group.");
                SetInt(cloned, CurveDriverHash, targetDriverIndex);
                curveMap[sourceCurveIndex] = targetCurves.Objects.Count;
                targetCurves.Objects.Add(cloned);
            }

            var outputMap = new Dictionary<int, int>();
            foreach (var sourceOutputIndex in group.OutputIndices)
            {
                var cloned = CloneObject(sourceOutputs.Objects[sourceOutputIndex], $"output_{targetOutputs.Objects.Count}");
                RemapRequiredIndex(cloned, OutputConnection0Hash, curveMap, "curve");
                RemapOptionalIndex(cloned, OutputConnection1Hash, curveMap);
                outputMap[sourceOutputIndex] = targetOutputs.Objects.Count;
                targetOutputs.Objects.Add(cloned);
            }

            foreach (var sourceDrivenIndex in group.DrivenIndices)
            {
                if (sourceDrivenIndex >= sourceDriven.Objects.Count) throw new InvalidDataException("Invalid driven record.");
                var sourcePoseIndex = SidecarConnectionEditing.SinglePose(CloneObject(sourceDriven.Objects[sourceDrivenIndex]), sourcePoses.Objects.Count);

                var targetPoseIndex = targetPoses.Objects.Count;
                var clonedDriven = CloneObject(sourceDriven.Objects[sourceDrivenIndex], $"driven_bone_{targetDriven.Objects.Count}");
                SetInt(clonedDriven, BoneIdHash, MapBone(sourceDriven.Objects[sourceDrivenIndex].Find(BoneIdHash)?.AsInt32() ?? -1));
                RemapPoseDrivenId(clonedDriven, BotwAampWriter.Crc32("translate_driven_id"), targetPoseIndex);
                RemapPoseDrivenId(clonedDriven, BotwAampWriter.Crc32("rotate_driven_id"), targetPoseIndex);
                RemapPoseDrivenId(clonedDriven, BotwAampWriter.Crc32("aim_driven_id"), targetPoseIndex);

                var sourcePose = sourcePoses.Objects[sourcePoseIndex];
                var clonedPose = CloneObject(sourcePose, $"pose_driven_{targetPoseIndex}");
                SetInt(clonedPose, BaseBoneIdHash, MapBone(sourcePose.Find(BaseBoneIdHash)?.AsInt32() ?? -1));
                foreach (var outputHash in PoseOutputHashes)
                    RemapOptionalIndex(clonedPose, outputHash, outputMap);

                targetDriven.Objects.Add(clonedDriven);
                targetPoses.Objects.Add(clonedPose);
            }
        });
    }

    public static BphhbDocumentSummary RemoveDriverGroup(BphhbDocumentSummary document, int groupIndex)
    {
        var groups = BuildDriverGroups(document);
        if ((uint)groupIndex >= (uint)groups.Count)
            throw new ArgumentOutOfRangeException(nameof(groupIndex));
        var group = groups[groupIndex];
        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            foreach (var name in new[] { "position_driven_list", "rotate_driven_list", "aim_driven_list" })
                if (FindList(data, BotwAampWriter.Crc32(name)) is { Objects.Count: > 0 })
                    throw new InvalidOperationException("Group deletion currently supports pose-driven BPHHB graphs only. This file contains another driven table; no changes were applied.");
            var drivers = RequireList(data, "driver_bone_list");
            var curves = RequireList(data, "connection_curve_list");
            var outputs = RequireList(data, "output_list");
            var driven = RequireList(data, "driven_bone_list");
            var poses = RequireList(data, "pose_driven_list");
            var removedOutputs = group.OutputIndices.ToHashSet();
            var removedPoses = Enumerable.Range(0, poses.Objects.Count)
                .Where(i => PoseOutputHashes.Any(hash => removedOutputs.Contains(GetInt(poses.Objects[i], hash))))
                .ToHashSet();
            foreach (var index in group.DrivenIndices)
                removedPoses.Add(SidecarConnectionEditing.SinglePose(driven.Objects[index], poses.Objects.Count));
            var channels = new[] { "translate", "rotate", "aim" };
            var removedDriven = new HashSet<int>();
            for (var i = 0; i < driven.Objects.Count; i++)
            {
                var record = driven.Objects[i];
                var poseIds = new List<int>();
                foreach (var channel in channels)
                {
                    var id = GetInt(record, BotwAampWriter.Crc32(channel + "_driven_id"));
                    if (id < 0) continue;
                    var type = GetInt(record, BotwAampWriter.Crc32(channel + "_driven_type"));
                    if (type is not (-1 or 0))
                        throw new InvalidOperationException("Group deletion currently supports pose-driven BPHHB graphs only. This file contains another driven type; no changes were applied.");
                    poseIds.Add(id);
                }
                if (poseIds.Any(removedPoses.Contains))
                {
                    if (poseIds.Any(id => !removedPoses.Contains(id)))
                        throw new InvalidOperationException("This driven bone shares channels with another group. Deletion was cancelled to preserve that group.");
                    removedDriven.Add(i);
                }
            }
            if (group.DrivenIndices.Any(index => !removedDriven.Contains(index)))
                throw new InvalidDataException("A driven bone has no explicit pose binding. Deletion was cancelled to preserve its references.");
            var driverMap = SidecarGroupRemoval.Compact(drivers, "driver_bone", group.DriverIndices.Contains);
            var curveMap = SidecarGroupRemoval.Compact(curves, "connection_curve", group.CurveIndices.Contains);
            var outputMap = SidecarGroupRemoval.Compact(outputs, "output", removedOutputs.Contains);
            var poseMap = SidecarGroupRemoval.Compact(poses, "pose_driven", removedPoses.Contains);
            SidecarGroupRemoval.Compact(driven, "driven_bone", removedDriven.Contains);
            foreach (var curve in curves.Objects)
                SetInt(curve, CurveDriverHash, SidecarGroupRemoval.Remap(GetInt(curve, CurveDriverHash), driverMap));
            foreach (var output in outputs.Objects)
                foreach (var hash in new[] { OutputConnection0Hash, OutputConnection1Hash })
                    if (FindParameter(output, hash) != null)
                        SetInt(output, hash, SidecarGroupRemoval.Remap(GetInt(output, hash), curveMap));
            foreach (var pose in poses.Objects)
                foreach (var hash in PoseOutputHashes)
                    if (FindParameter(pose, hash) != null)
                        SetInt(pose, hash, SidecarGroupRemoval.Remap(GetInt(pose, hash), outputMap));
            foreach (var record in driven.Objects)
                foreach (var channel in channels)
                {
                    var hash = BotwAampWriter.Crc32(channel + "_driven_id");
                    if (GetInt(record, hash) >= 0)
                        SetInt(record, hash, SidecarGroupRemoval.Remap(GetInt(record, hash), poseMap));
                }
        });
    }

    private static IReadOnlyList<BphhbDriverGroupData> BuildDriverGroups(BphhbDocumentSummary document)
    {
        var archive = document.Archive
            ?? throw new InvalidOperationException("This BPHHB file could not be decoded into driver groups.");
        var data = archive.Root.Children.SingleOrDefault()
            ?? throw new InvalidDataException("BPHHB has no helper-bone data root.");
        var bones = data.FindChild("bone_list") ?? throw new InvalidDataException("BPHHB has no bone_list.");
        var curves = data.FindChild("connection_curve_list") ?? throw new InvalidDataException("BPHHB has no connection_curve_list.");
        var outputs = data.FindChild("output_list") ?? throw new InvalidDataException("BPHHB has no output_list.");
        var drivers = data.FindChild("driver_bone_list") ?? throw new InvalidDataException("BPHHB has no driver_bone_list.");
        var driven = data.FindChild("driven_bone_list") ?? throw new InvalidDataException("BPHHB has no driven_bone_list.");
        var poses = data.FindChild("pose_driven_list") ?? throw new InvalidDataException("BPHHB has no pose_driven_list.");
        if (drivers.Objects.Count == 0)
            return Array.Empty<BphhbDriverGroupData>();

        var translateDrivenTypeHash = BotwAampWriter.Crc32("translate_driven_type");
        var translateDrivenIdHash = BotwAampWriter.Crc32("translate_driven_id");
        var rotateDrivenTypeHash = BotwAampWriter.Crc32("rotate_driven_type");
        var rotateDrivenIdHash = BotwAampWriter.Crc32("rotate_driven_id");
        var aimDrivenTypeHash = BotwAampWriter.Crc32("aim_driven_type");
        var aimDrivenIdHash = BotwAampWriter.Crc32("aim_driven_id");
        int PoseIndexForDriven(int drivenIndex)
        {
            if ((uint)drivenIndex >= (uint)driven.Objects.Count)
                return -1;
            var record = driven.Objects[drivenIndex];
            foreach (var (typeHash, idHash) in new[]
            {
                (translateDrivenTypeHash, translateDrivenIdHash),
                (rotateDrivenTypeHash, rotateDrivenIdHash),
                (aimDrivenTypeHash, aimDrivenIdHash)
            })
            {
                var id = record.Find(idHash)?.AsInt32() ?? -1;
                var type = record.Find(typeHash)?.AsInt32() ?? -1;
                if (id >= 0 && id < poses.Objects.Count && type is -1 or 0)
                    return id;
            }
            return drivenIndex < poses.Objects.Count ? drivenIndex : -1;
        }

        var parents = Enumerable.Range(0, drivers.Objects.Count).ToArray();
        int Find(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }
        void Union(int left, int right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (leftRoot != rightRoot)
                parents[rightRoot] = leftRoot;
        }

        var curveDrivers = curves.Objects
            .Select(item => item.Find(CurveDriverHash)?.AsInt32() ?? -1)
            .ToArray();
        foreach (var output in outputs.Objects)
        {
            var linkedDrivers = PoseOutputConnections(output)
                .Where(index => index >= 0 && index < curveDrivers.Length)
                .Select(index => curveDrivers[index])
                .Where(index => index >= 0 && index < drivers.Objects.Count)
                .Distinct()
                .ToArray();
            for (var index = 1; index < linkedDrivers.Length; index++)
                Union(linkedDrivers[0], linkedDrivers[index]);
        }

        // Different channels on one driven pose are still one motion graph,
        // even when each channel owns a separate output record.
        foreach (var pose in poses.Objects)
        {
            var linkedDrivers = PoseOutputHashes
                .Select(hash => pose.Find(hash)?.AsInt32() ?? -1)
                .Where(index => index >= 0 && index < outputs.Objects.Count)
                .SelectMany(index => PoseOutputConnections(outputs.Objects[index]))
                .Where(index => index >= 0 && index < curveDrivers.Length)
                .Select(index => curveDrivers[index])
                .Where(index => index >= 0 && index < drivers.Objects.Count)
                .Distinct()
                .ToArray();
            for (var index = 1; index < linkedDrivers.Length; index++)
                Union(linkedDrivers[0], linkedDrivers[index]);
        }
        var boneNames = bones.Objects
            .Select((item, index) => item.Find(NameHash)?.AsString() ?? $"bone_{index}")
            .ToArray();
        return Enumerable.Range(0, drivers.Objects.Count)
            .GroupBy(Find)
            .Select(group => group.OrderBy(index => index).ToArray())
            .OrderBy(group => group[0])
            .Select((driverIndices, groupIndex) =>
            {
                var driverSet = driverIndices.ToHashSet();
                var curveIndices = curveDrivers
                    .Select((driverIndex, curveIndex) => (driverIndex, curveIndex))
                    .Where(item => driverSet.Contains(item.driverIndex))
                    .Select(item => item.curveIndex)
                    .ToArray();
                var curveSet = curveIndices.ToHashSet();
                var outputIndices = outputs.Objects
                    .Select((item, outputIndex) => (item, outputIndex))
                    .Where(item => PoseOutputConnections(item.item).Any(curveSet.Contains))
                    .Select(item => item.outputIndex)
                    .ToArray();
                var outputSet = outputIndices.ToHashSet();
                var drivenIndices = Enumerable.Range(0, driven.Objects.Count)
                    .Where(index => PoseIndexForDriven(index) is var poseIndex && poseIndex >= 0 && PoseOutputHashes
                        .Select(hash => poses.Objects[poseIndex].Find(hash)?.AsInt32() ?? -1)
                        .Any(outputSet.Contains) || groupIndex == 0 && PoseIndexForDriven(index) is var unconnectedPose && unconnectedPose >= 0 &&
                            PoseOutputHashes.All(hash => (poses.Objects[unconnectedPose].Find(hash)?.AsInt32() ?? -1) < 0))
                    .ToArray();
                var driverBoneIndices = driverIndices
                    .Select(index => drivers.Objects[index].Find(BoneIdHash)?.AsInt32() ?? -1)
                    .Where(index => index >= 0)
                    .Distinct()
                    .ToArray();
                var drivenBoneIndices = drivenIndices
                    .Select(index => driven.Objects[index].Find(BoneIdHash)?.AsInt32() ?? -1)
                    .Where(index => index >= 0)
                    .Distinct()
                    .ToArray();
                var requiredBones = driverIndices
                    .SelectMany(index => new[]
                    {
                        drivers.Objects[index].Find(BoneIdHash)?.AsInt32() ?? -1,
                        drivers.Objects[index].Find(BaseBoneIdHash)?.AsInt32() ?? -1
                    })
                    .Concat(drivenIndices.SelectMany(index =>
                    {
                        var poseIndex = PoseIndexForDriven(index);
                        return new[]
                        {
                            driven.Objects[index].Find(BoneIdHash)?.AsInt32() ?? -1,
                            poseIndex >= 0 ? poses.Objects[poseIndex].Find(BaseBoneIdHash)?.AsInt32() ?? -1 : -1
                        };
                    }))
                    .Where(index => index >= 0)
                    .Distinct()
                    .ToArray();
                var summary = new SidecarDriverGroup(
                    groupIndex,
                    driverIndices,
                    driverBoneIndices,
                    drivenBoneIndices,
                    driverBoneIndices.Select(index => BoneName(boneNames, index)).ToArray(),
                    drivenBoneIndices.Select(index => BoneName(boneNames, index)).ToArray(),
                    requiredBones,
                    requiredBones.Select(index => BoneName(boneNames, index)).ToArray(),
                    curveIndices.Length,
                    outputIndices.Length);
                return new BphhbDriverGroupData(summary, driverIndices, curveIndices, outputIndices, drivenIndices, requiredBones);
            })
            .ToArray();
    }

    private static readonly uint[] PoseOutputHashes =
    {
        RollOutputHash,
        BendHOutputHash,
        BendVOutputHash,
        TranslateXOutputHash,
        TranslateYOutputHash,
        TranslateZOutputHash
    };

    private static IEnumerable<int> PoseOutputConnections(AampReadObject output)
    {
        yield return output.Find(OutputConnection0Hash)?.AsInt32() ?? -1;
        yield return output.Find(OutputConnection1Hash)?.AsInt32() ?? -1;
    }

    private static string BoneName(IReadOnlyList<string> names, int index) =>
        index >= 0 && index < names.Count ? names[index] : $"bone_{index}";

    private static void RemapPoseDrivenId(BotwAampWriter.AampObjectNode item, uint hash, int targetPoseIndex)
    {
        if (GetInt(item, hash) >= 0)
            SetInt(item, hash, targetPoseIndex);
    }

    private static void RemapRequiredIndex(
        BotwAampWriter.AampObjectNode item,
        uint hash,
        IReadOnlyDictionary<int, int> remap,
        string label)
    {
        var sourceIndex = GetInt(item, hash);
        if (sourceIndex < 0 || !remap.TryGetValue(sourceIndex, out var targetIndex))
            throw new InvalidDataException($"The selected driver group references a {label} outside its dependency closure.");
        SetInt(item, hash, targetIndex);
    }

    private static void RemapOptionalIndex(
        BotwAampWriter.AampObjectNode item,
        uint hash,
        IReadOnlyDictionary<int, int> remap)
    {
        var sourceIndex = GetInt(item, hash);
        if (sourceIndex >= 0 && remap.TryGetValue(sourceIndex, out var targetIndex))
            SetInt(item, hash, targetIndex);
    }

    private sealed record BphhbDriverGroupData(
        SidecarDriverGroup Summary,
        IReadOnlyList<int> DriverIndices,
        IReadOnlyList<int> CurveIndices,
        IReadOnlyList<int> OutputIndices,
        IReadOnlyList<int> DrivenIndices,
        IReadOnlyList<int> RequiredBoneIndices);
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

    /// <summary>
    /// Reflects every input and affected pose in one connected driver group.
    /// Curves and outputs remain attached because both endpoint frames are
    /// reflected together.
    /// </summary>
    public static BphhbDocumentSummary MirrorDriverGroupAcrossX(
        BphhbDocumentSummary document,
        int groupIndex)
    {
        var group = BuildDriverGroups(document).ElementAtOrDefault(groupIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(groupIndex));
        var groupBoneIndices = group.Summary.DriverBoneIndices
            .Concat(group.Summary.DrivenBoneIndices)
            .Distinct()
            .ToArray();

        return Rebuild(document, root =>
        {
            var data = RequireDataRoot(root);
            var bones = RequireList(data, "bone_list");
            foreach (var boneIndex in groupBoneIndices)
            {
                if (boneIndex < 0 || boneIndex >= bones.Objects.Count)
                    continue;
                var bone = bones.Objects[boneIndex];
                var name = GetString(bone, "name");
                if (!string.IsNullOrWhiteSpace(name))
                    SetString(bone, "name", SwapSideTokens(name));
            }

            var drivers = RequireList(data, "driver_bone_list");
            MirrorTransformRecords(group.DriverIndices
                .Where(index => index >= 0 && index < drivers.Objects.Count)
                .Select(index => drivers.Objects[index]));

            var poses = RequireList(data, "pose_driven_list");
            var driven = RequireList(data, "driven_bone_list");
            MirrorTransformRecords(group.DrivenIndices
                .Select(index => SidecarConnectionEditing.SinglePose(driven.Objects[index], poses.Objects.Count)).Distinct()
                .Select(index => poses.Objects[index]));
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
                if (driven.Objects.Any(o => GetInt(o, BoneIdHash) == boneIndex))
                    MirrorTransformRecords(new[] { SidecarConnectionEditing.Pose(data, boneIndex, true, isolate: true) });
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

    internal static BphhbDocumentSummary UpdateCurveKeys(BphhbDocumentSummary document, int index, IReadOnlyList<Vector4> keys) =>
        Rebuild(document, root => SidecarCurveEditing.Write(RequireList(RequireDataRoot(root), "connection_curve_list"), index, keys));

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

    private static BotwAampWriter.AampObjectNode CloneObject(AampReadObject source, string replacementName)
    {
        var result = new BotwAampWriter.AampObjectNode(replacementName);
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

            MirrorDirectionAcrossX(record, "aim_axis");
            MirrorDirectionAcrossX(record, "up_axis");
        }
    }

    private static void MirrorDirectionAcrossX(BotwAampWriter.AampObjectNode record, string name)
    {
        var direction = GetVector3(record, name);
        if (!direction.HasValue)
            return;

        var mirrored = direction.Value;
        mirrored.X = -mirrored.X;
        SetVector3(record, name, mirrored);
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
