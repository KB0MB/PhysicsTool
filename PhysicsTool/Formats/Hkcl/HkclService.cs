using System;
using System.Collections;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.IO.Compression;
using HKX2;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace HKCLTool;

public enum HkclPlatform
{
    WiiU,
    Switch
}

public sealed partial class HkclService
{
    private hkRootLevelContainer? _root;
    private BphclDocumentSummary? _bphcl;
    private BphhbDocumentSummary? _bphhb;
    private BphyssbDocumentSummary? _bphyssb;
    private bool _isHkrg;
    private string? _path;

    public bool HasDocument => _root != null || _bphcl != null || _bphhb != null || _bphyssb != null;
    public string? SourcePath => _path;
    public bool IsBphcl => _bphcl != null;
    public bool IsBphhb => _bphhb != null;
    public bool IsBphyssb => _bphyssb != null;
    public bool IsHelperBoneSidecar => IsBphhb || IsBphyssb;
    public bool IsHkrg => _isHkrg;
    public bool IsReadOnlyExternal => IsBphcl || IsHelperBoneSidecar || IsHkrg;
    // BPHCL particle records have known fixed-size native layouts. Their
    // position and basic physics values can be changed safely, while BPHCL
    // structural data remains read-only until its ITEM/PTCH writer is proven.
    public bool CanEditParticleValues => !IsHelperBoneSidecar;
    public bool CanEditConstraintValues => !IsHelperBoneSidecar;
    public string CurrentExtension => IsBphcl ? ".bphcl" : IsBphhb ? ".bphhb" : IsBphyssb ? ".bphyssb" : IsHkrg ? ".hkrg" : ".hkcl";

    /// <summary>
    /// Reads the Havok packfile layout flags without deserializing the document.
    /// BotW Wii U packfiles use a four-byte, big-endian layout; Switch packfiles
    /// use an eight-byte, little-endian layout.
    /// </summary>
    public static HkclPlatform DetectHkclPlatform(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[20];
        if (stream.Read(header) != header.Length)
            throw new InvalidDataException("The HKCL file is too small to contain a Havok packfile header.");

        const int layoutRulesOffset = 16;
        var pointerSize = header[layoutRulesOffset];
        var littleEndian = header[layoutRulesOffset + 1] != 0;

        if (pointerSize == 4 && !littleEndian)
            return HkclPlatform.WiiU;

        if (pointerSize == 8 && littleEndian)
            return HkclPlatform.Switch;

        throw new InvalidDataException(
            $"Unsupported HKCL layout (pointer size {pointerSize}, little endian {littleEndian}).");
    }

    public void Load(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".bphcl")
        {
            _bphcl = BphclBridge.Load(path);
            _bphhb = null;
            _bphyssb = null;
            _root = null;
            _isHkrg = false;
            _path = path;
            return;
        }

        if (extension == ".bphhb")
        {
            _bphhb = BphhbBridge.Load(path);
            _bphcl = null;
            _bphyssb = null;
            _root = null;
            _isHkrg = false;
            _path = path;
            return;
        }

        if (extension == ".bphyssb")
        {
            _bphyssb = BphyssbBridge.Load(path);
            _bphcl = null;
            _bphhb = null;
            _root = null;
            _isHkrg = false;
            _path = path;
            return;
        }

        _bphcl = null;
        _bphhb = null;
        _bphyssb = null;
        _isHkrg = extension == ".hkrg";
        _root = extension == ".json" ? LoadJson(path) : LoadHkcl(path);
        _path = path;
    }

    public void ExportReadableJson(string path)
    {
        if (_bphcl != null)
        {
            File.WriteAllText(path, BphclBridge.ExportSummary(_bphcl).ToString(Formatting.Indented));
            return;
        }

        if (_bphhb != null)
        {
            File.WriteAllText(path, BphhbBridge.ExportSummary(_bphhb).ToString(Formatting.Indented));
            return;
        }

        if (_bphyssb != null)
        {
            File.WriteAllText(path, BphyssbBridge.ExportSummary(_bphyssb).ToString(Formatting.Indented));
            return;
        }

        // Raw HKX2 JSON is the stable, directly reloadable representation for HKCL.
        // Keep it as the public export until the authoring schema is complete.
        File.WriteAllText(path, SerializeRaw(RequireRoot()));
    }

    public void SaveHkcl(string path, HkclPlatform platform)
    {
        if (_bphcl != null)
        {
            if (!Path.GetExtension(path).Equals(".bphcl", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("BPHCL documents must be saved with the .bphcl extension.");

            _bphcl = BphclBridge.Save(_bphcl.NativeDocument
                ?? throw new InvalidOperationException("The BPHCL native document is unavailable."), path);
            _path = path;
            return;
        }

        if (_bphhb != null)
        {
            _bphhb = BphhbBridge.Save(_bphhb, path);
            _path = path;
            return;
        }

        if (_bphyssb != null)
        {
            _bphyssb = BphyssbBridge.Save(_bphyssb, path);
            _path = path;
            return;
        }

        if (IsHkrg && !Path.GetExtension(path).Equals(".hkrg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HKRG documents must be saved with the .hkrg extension.");

        if (IsHkrg && !HkrgConstraintFramesAreFinite())
            RepairHkrgConstraintFramesForCurrentPose(updateMotorTargets: false);

        var root = RequireRoot();
        var header = platform == HkclPlatform.WiiU
            ? HKXHeader.BotwWiiu()
            : HKXHeader.BotwNx();

        using var stream = File.Create(path);
        var writer = new BinaryWriterEx(stream);
        var serializer = new PackFileSerializer();
        serializer.Serialize(root, writer, header);
    }

    /// <summary>
    /// Exports the currently loaded TotK helper-bone graph as BotW's native
    /// support-bone sidecar. This is deliberately separate from SaveHkcl: the
    /// formats are both AAMP, but they are different graph layouts.
    /// </summary>
    public BphyssbExportSummary ExportBphyssb(string path)
    {
        if (_bphhb == null)
            throw new InvalidOperationException("Open a BPHHB helper-bone file before exporting a BPHYSSB support-bone file.");

        return BphyssbBridge.Export(_bphhb, path);
    }

    /// <summary>
    /// Exports the currently loaded BotW support-bone graph as TotK's native
    /// helper-bone sidecar. This stays independent from cloth conversion.
    /// </summary>
    public BphhbDocumentSummary ExportBphhb(string path)
    {
        if (_bphyssb == null)
            throw new InvalidOperationException("Open a BPHYSSB support-bone file before exporting a BPHHB helper-bone file.");

        return BphhbBridge.Export(_bphyssb, path);
    }
    public string CaptureState()
    {
        if (_bphcl?.NativeDocument is { } bphcl)
            return "BPHCL:" + Convert.ToBase64String(bphcl.Bytes);

        if (_bphhb is { } bphhb)
            return "BPHHB:" + Convert.ToBase64String(bphhb.Bytes);

        if (_bphyssb is { } bphyssb)
            return "BPHYSSB:" + Convert.ToBase64String(bphyssb.Bytes);

        if (IsHkrg)
            return "HKRG:" + SerializeRaw(RequireRoot());

        if (IsReadOnlyExternal)
            throw new InvalidOperationException("External physics document state snapshots are not implemented yet.");

        return SerializeRaw(RequireRoot());
    }

    public void RestoreState(string state)
    {
        if (state.StartsWith("BPHCL:", StringComparison.Ordinal))
        {
            var document = NativeBphclDocument.Parse(Convert.FromBase64String(state[6..]));
            _bphcl = CreateBphclSummary(document, _path ?? "Physics.bphcl");
            _bphhb = null;
            _bphyssb = null;
            _root = null;
            _isHkrg = false;
            return;
        }

        if (state.StartsWith("BPHHB:", StringComparison.Ordinal))
        {
            _bphhb = BphhbBridge.LoadBytes(Convert.FromBase64String(state[6..]), _path ?? "Physics.bphhb");
            _bphcl = null;
            _bphyssb = null;
            _root = null;
            _isHkrg = false;
            return;
        }

        if (state.StartsWith("BPHYSSB:", StringComparison.Ordinal))
        {
            _bphyssb = BphyssbBridge.LoadBytes(Convert.FromBase64String(state[8..]), _path ?? "Physics.bphyssb");
            _bphcl = null;
            _bphhb = null;
            _root = null;
            _isHkrg = false;
            return;
        }

        if (state.StartsWith("HKRG:", StringComparison.Ordinal))
        {
            _bphcl = null;
            _bphhb = null;
            _bphyssb = null;
            _isHkrg = true;
            _root = DeserializeRoot(state[5..]);
            return;
        }

        _bphcl = null;
        _bphhb = null;
        _bphyssb = null;
        _isHkrg = false;
        _root = DeserializeRoot(state);
    }

    public string SuggestFileName(string extension)
    {
        var baseName = string.IsNullOrWhiteSpace(_path)
            ? "Physics"
            : Path.GetFileNameWithoutExtension(_path);

        return baseName + extension;
    }

    public string GetClothName(int clothIndex)
    {
        if (_bphcl != null)
            return _bphcl.Cloths.ElementAtOrDefault(clothIndex)?.Value<string>("name") ?? $"Cloth {clothIndex}";

        if (IsHkrg)
            return clothIndex == 0 ? "Ragdoll" : $"Ragdoll {clothIndex}";

        if (_root == null)
            return $"Cloth {clothIndex}";

        var cloth = GetClothDatas(_root).ElementAtOrDefault(clothIndex);
        return cloth == null ? $"Cloth {clothIndex}" : GetString(cloth, "name") ?? $"Cloth {clothIndex}";
    }

    public IReadOnlyList<string> GetClothSummaries()
    {
        if (_bphcl != null)
            return _bphcl.Cloths.Select((cloth, i) => $"{i}: {cloth.Value<string>("name") ?? $"Cloth {i}"}  |  particles: {cloth.Value<int?>("particleCount") ?? 0}  |  bones: {cloth["skeleton"]?["boneCount"]?.Value<int>() ?? 0}  |  BPHCL").ToList();

        if (_bphhb != null)
            return BphhbBridge.GetDriverGroups(_bphhb)
                .Select(group => $"{group.Index}: {group.DisplayName}  |  driven: {group.DrivenBoneNames.Count}  |  curves: {group.CurveCount}  |  BPHHB")
                .ToArray();

        if (_bphyssb != null)
            return BphyssbBridge.GetDriverGroups(_bphyssb)
                .Select(group => $"{group.Index}: {group.DisplayName}  |  driven: {group.DrivenBoneNames.Count}  |  curves: {group.CurveCount}  |  BPHYSSB")
                .ToArray();

        if (IsHkrg)
        {
            var ragdoll = GetHkrgRagdollInstance();
            var constraints = GetList(GetValue(ragdoll, "constraints"))?.Count ?? 0;
            return new[]
            {
                $"0: Ragdoll  |  rigid bodies: {GetHkrgRigidBodies().Count}  |  constraints: {constraints}  |  HKRG"
            };
        }

        if (_root == null)
            return Array.Empty<string>();

        var cloths = GetClothDatas(_root).ToList();
        var skeletons = GetSkeletons(_root).ToList();
        var result = new List<string>();

        for (var i = 0; i < cloths.Count; i++)
        {
            var name = GetString(cloths[i], "name") ?? $"Cloth {i}";
            var particleCount = GetParticleCount(cloths[i]);
            var boneCount = i < skeletons.Count ? GetList(GetValue(skeletons[i], "bones"))?.Count ?? 0 : 0;
            result.Add($"{i}: {name}  |  particles: {particleCount}  |  bones: {boneCount}");
        }

        return result;
    }

    public SidecarDriverGroup? GetSidecarDriverGroup(int groupIndex)
    {
        if (_bphhb != null)
            return BphhbBridge.GetDriverGroups(_bphhb).ElementAtOrDefault(groupIndex);
        if (_bphyssb != null)
            return BphyssbBridge.GetDriverGroups(_bphyssb).ElementAtOrDefault(groupIndex);
        return null;
    }

    public IReadOnlyList<SidecarDriverGroup> GetSidecarDriverGroups()
    {
        if (_bphhb != null)
            return BphhbBridge.GetDriverGroups(_bphhb);
        if (_bphyssb != null)
            return BphyssbBridge.GetDriverGroups(_bphyssb);
        return Array.Empty<SidecarDriverGroup>();
    }

    public SidecarPreviewEvaluation EvaluateSidecarDriverGroup(
        int groupIndex,
        IReadOnlyList<VisualModelBone> currentBones,
        IReadOnlyList<VisualModelBone> restBones)
    {
        if (_bphhb != null)
            return SidecarPreviewEvaluator.Evaluate(_bphhb, groupIndex, currentBones, restBones);
        if (_bphyssb != null)
            return SidecarPreviewEvaluator.Evaluate(_bphyssb, groupIndex, currentBones, restBones);
        return new SidecarPreviewEvaluation(
            Array.Empty<SidecarBonePreviewPose>(),
            Array.Empty<string>());
    }
    public IReadOnlyList<string> GetSkeletonBones(int clothIndex)
    {
        if (_bphcl != null)
        {
            var bphclSkeleton = _bphcl.Cloths.ElementAtOrDefault(clothIndex)?["skeleton"];
            var bphclBones = bphclSkeleton?["bones"] as JArray;
            if (bphclBones == null)
                return Array.Empty<string>();
            return bphclBones.Select(b =>
            {
                var index = b.Value<int?>("index") ?? 0;
                var name = b.Value<string>("name") ?? $"Bone {index}";
                var parent = b.Value<int?>("parentIndex") ?? -1;
                var parentName = parent >= 0 && parent < bphclBones.Count ? bphclBones[parent]?["name"]?.Value<string>() ?? "none" : "none";
                return $"{index}: {name}  |  parent: {parentName} ({parent})";
            }).ToList();
        }

        if (_bphhb != null)
            return DescribeSidecarGroupBones(BphhbBridge.GetDriverGroups(_bphhb), clothIndex);

        if (_bphyssb != null)
            return DescribeSidecarGroupBones(BphyssbBridge.GetDriverGroups(_bphyssb), clothIndex);

        if (IsHkrg)
        {
            var ragdollBones = GetHkrgBoneRows();
            return ragdollBones.Select(bone =>
            {
                var parentName = bone.ParentIndex >= 0 && bone.ParentIndex < ragdollBones.Count
                    ? ragdollBones[bone.ParentIndex].Name
                    : "none";
                return $"{bone.Index}: {bone.Name}  |  parent: {parentName} ({bone.ParentIndex})";
            }).ToArray();
        }

        if (_root == null)
            return Array.Empty<string>();

        var skeleton = GetSkeletons(_root).ElementAtOrDefault(clothIndex);
        if (skeleton == null)
            return Array.Empty<string>();

        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var result = new List<string>();

        for (var i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            var name = GetString(bone, "name") ?? $"Bone {i}";
            var parentIndex = ToInt(parents.ElementAtOrDefault(i), -1);
            var parentName = parentIndex >= 0 && parentIndex < bones.Count
                ? GetString(bones[parentIndex], "name") ?? parentIndex.ToString(CultureInfo.InvariantCulture)
                : "none";

            result.Add($"{i}: {name}  |  parent: {parentName} ({parentIndex})");
        }

        return result;
    }

    private static IReadOnlyList<string> DescribeSidecarGroupBones(
        IReadOnlyList<SidecarDriverGroup> groups,
        int groupIndex)
    {
        var group = groups.ElementAtOrDefault(groupIndex);
        if (group == null)
            return Array.Empty<string>();

        return group.DriverBoneIndices
            .Zip(group.DriverBoneNames, (index, name) => $"Driver: {index}: {name}  |  motion input")
            .Concat(group.DrivenBoneIndices.Zip(
                group.DrivenBoneNames,
                (index, name) => $"Driven: {index}: {name}  |  affected bone"))
            .ToArray();
    }
    private static string GetSidecarGroupDetails(
        IReadOnlyList<SidecarDriverGroup> groups,
        int groupIndex,
        string format,
        string role)
    {
        var group = groups.ElementAtOrDefault(groupIndex);
        if (group == null)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine($"{role} driver group {group.Index}: {group.DisplayName}");
        builder.AppendLine(new string('-', 64));
        builder.AppendLine($"Format: {format}");
        builder.AppendLine($"Driver records: {group.DriverIndices.Count}");
        builder.AppendLine($"Driven bones: {group.DrivenBoneNames.Count}");
        builder.AppendLine($"Curves: {group.CurveCount}");
        builder.AppendLine($"Outputs: {group.OutputCount}");
        builder.AppendLine();
        builder.AppendLine("Motion inputs");
        foreach (var name in group.DriverBoneNames)
            builder.AppendLine($"  {name}");
        builder.AppendLine();
        builder.AppendLine("Affected bones");
        foreach (var name in group.DrivenBoneNames)
            builder.AppendLine($"  {name}");
        return builder.ToString();
    }
    public string GetClothDetails(int clothIndex)
    {
        if (_bphcl != null)
        {
            var bphclCloth = _bphcl.Cloths.ElementAtOrDefault(clothIndex) as JObject;
            if (bphclCloth == null)
                return string.Empty;
            var bphclBuilder = new StringBuilder();
            bphclBuilder.AppendLine(bphclCloth.Value<string>("name") ?? $"Cloth {clothIndex}");
            bphclBuilder.AppendLine(new string('-', 64));
            bphclBuilder.AppendLine("Format: BPHCL / Phive TAG0");
            bphclBuilder.AppendLine($"Class: {bphclCloth.Value<string>("class")}");
            var nativeCloth = _bphcl.NativeDocument?.Cloths.ElementAtOrDefault(clothIndex);
            bphclBuilder.AppendLine($"Particles: {nativeCloth?.SimCloths.Sum(simulation => simulation.Particles.Count) ?? bphclCloth.Value<int?>("particleCount") ?? 0}");
            bphclBuilder.AppendLine($"Operators: {nativeCloth?.Operators.Count ?? 0}");
            bphclBuilder.AppendLine($"States: {nativeCloth?.States.Count ?? 0}");
            bphclBuilder.AppendLine($"Buffers: {nativeCloth?.BufferDefinitions.Count ?? 0}");
            bphclBuilder.AppendLine($"Transform sets: {nativeCloth?.TransformSetDefinitions.Count ?? 0}");
            if (nativeCloth != null)
            {
                bphclBuilder.AppendLine();
                bphclBuilder.AppendLine("Native simulations");
                foreach (var simulation in nativeCloth.SimCloths)
                {
                    var colliderCount = simulation.CollidableItemIndices.Count;
                    bphclBuilder.AppendLine($"  {simulation.Index}: {simulation.Name}");
                    bphclBuilder.AppendLine($"    Particles: {simulation.Particles.Count}  |  triangles: {simulation.TriangleCount}  |  constraint sets: {simulation.ConstraintSetCount}");
                    bphclBuilder.AppendLine($"    Colliders: {colliderCount}  |  gravity: ({simulation.SimulationInfo.Gravity.X:G4}, {simulation.SimulationInfo.Gravity.Y:G4}, {simulation.SimulationInfo.Gravity.Z:G4})  |  damping: {simulation.SimulationInfo.GlobalDampingPerSecond:G4}");
                }
            }
            bphclBuilder.AppendLine();
            bphclBuilder.AppendLine("File");
            bphclBuilder.AppendLine($"  Cloths: {_bphcl.ClothCount}");
            bphclBuilder.AppendLine($"  Colliders: {_bphcl.ColliderCount}");
            bphclBuilder.AppendLine($"  Skeletons: {_bphcl.SkeletonCount}");
            bphclBuilder.AppendLine($"  AAMP: {(_bphcl.AampPresent ? "present" : "missing")}");
            var aamp = _bphcl.Raw["aamp"] as JObject;
            if (aamp != null)
            {
                var registered = aamp.Value<bool?>("allTag0ClothsRegistered") ?? false;
                bphclBuilder.AppendLine($"  AAMP live cloth registrations: {(registered ? "complete" : "incomplete")}");
            }
            bphclBuilder.AppendLine();
            bphclBuilder.AppendLine("BPHCL editor: particle pose/mass/radius/friction and reflected constraint values are editable in-place. Triangle, skeleton, and collider inspection uses native BPHCL data; structural edits remain locked until their relocation writer is verified.");
            return bphclBuilder.ToString();
        }

        if (_bphhb != null)
            return GetSidecarGroupDetails(BphhbBridge.GetDriverGroups(_bphhb), clothIndex, "BPHHB", "Helper");

        if (_bphyssb != null)
            return GetSidecarGroupDetails(BphyssbBridge.GetDriverGroups(_bphyssb), clothIndex, "BPHYSSB", "Support");

        if (IsHkrg)
            return GetHkrgDetails();

        if (_root == null)
            return string.Empty;

        var cloth = GetClothDatas(_root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine(GetString(cloth, "name") ?? $"Cloth {clothIndex}");
        builder.AppendLine(new string('-', 64));
        builder.AppendLine($"Class: {cloth.GetType().Name}");
        builder.AppendLine($"Particles: {GetParticleCount(cloth)}");
        builder.AppendLine($"Constraint sets: {GetList(GetValue(cloth, "constraintSets"))?.Count ?? 0}");
        builder.AppendLine($"Operators: {GetList(GetValue(cloth, "operators"))?.Count ?? 0}");
        builder.AppendLine($"Transform sets: {GetList(GetValue(cloth, "transformSetDefinitions"))?.Count ?? 0}");
        builder.AppendLine($"Buffer definitions: {GetList(GetValue(cloth, "bufferDefinitions"))?.Count ?? 0}");
        builder.AppendLine();

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData != null)
        {
            builder.AppendLine("Simulation");
            builder.AppendLine($"  Gravity: {FormatSimpleValue(GetValue(simData, "gravity"))}");
            builder.AppendLine($"  Total mass: {FormatSimpleValue(GetValue(simData, "totalMass"))}");
            builder.AppendLine($"  Max particle radius: {FormatSimpleValue(GetValue(simData, "maxParticleRadius"))}");
            builder.AppendLine($"  Collision tolerance: {FormatSimpleValue(GetValue(simData, "collisionTolerance"))}");
            builder.AppendLine();
        }

        var skeleton = GetSkeletons(_root).ElementAtOrDefault(clothIndex);
        if (skeleton != null)
        {
            builder.AppendLine("Skeleton");
            builder.AppendLine($"  Name: {GetString(skeleton, "name") ?? "(unnamed)"}");
            builder.AppendLine($"  Bones: {GetList(GetValue(skeleton, "bones"))?.Count ?? 0}");
        }

        return builder.ToString();
    }

    public IReadOnlyList<ParticleEditRow> GetParticleRows(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<ParticleEditRow>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var simulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            return simulation?.Particles.Select(particle => new ParticleEditRow
            {
                Index = particle.Index,
                Fixed = particle.Fixed,
                X = particle.Position.X,
                Y = particle.Position.Y,
                Z = particle.Position.Z,
                W = particle.Position.W,
                Mass = particle.Mass,
                InverseMass = particle.InverseMass,
                Radius = particle.Radius,
                Friction = particle.Friction,
                CollisionMask = simulation.StaticCollisionMasks.Count > particle.Index
                    ? simulation.StaticCollisionMasks[particle.Index]
                    : GetBphclCollisionMask(
                        simulation,
                        particle,
                        particle.Index,
                        preserveSourceLandscapeCollisionBit: true)
            }).ToArray() ?? Array.Empty<ParticleEditRow>();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return Array.Empty<ParticleEditRow>();

        return GetParticleRowsForCloth(cloth);
    }

    public ClothSimulationSettings GetSimulationSettings(int clothIndex)
    {
        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var bphclSimulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            if (bphclSimulation == null)
                throw new ArgumentOutOfRangeException(nameof(clothIndex));

            return new ClothSimulationSettings
            {
                GravityX = bphclSimulation.SimulationInfo.Gravity.X,
                GravityY = bphclSimulation.SimulationInfo.Gravity.Y,
                GravityZ = bphclSimulation.SimulationInfo.Gravity.Z,
                DampingPerSecond = bphclSimulation.SimulationInfo.GlobalDampingPerSecond,
                CollisionTolerance = bphclSimulation.Runtime.LandscapeCollisionData.CollisionTolerance,
                TransferTranslationMotion = bphclSimulation.Runtime.TransferMotionData.TransferTranslationMotion,
                MinTranslationSpeed = bphclSimulation.Runtime.TransferMotionData.MinTranslationSpeed,
                MaxTranslationSpeed = bphclSimulation.Runtime.TransferMotionData.MaxTranslationSpeed,
                MinTranslationBlend = bphclSimulation.Runtime.TransferMotionData.MinTranslationBlend,
                MaxTranslationBlend = bphclSimulation.Runtime.TransferMotionData.MaxTranslationBlend,
                TransferRotationMotion = bphclSimulation.Runtime.TransferMotionData.TransferRotationMotion,
                MinRotationSpeed = bphclSimulation.Runtime.TransferMotionData.MinRotationSpeed,
                MaxRotationSpeed = bphclSimulation.Runtime.TransferMotionData.MaxRotationSpeed,
                MinRotationBlend = bphclSimulation.Runtime.TransferMotionData.MinRotationBlend,
                MaxRotationBlend = bphclSimulation.Runtime.TransferMotionData.MaxRotationBlend
            };
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"))
            ?? throw new InvalidOperationException("The selected cloth has no simulation data.");
        var simulationInfo = GetValue(simulation, "simulationInfo") ?? simulation;
        var transfer = GetValue(simulation, "transferMotionData");
        var gravity = GetValue(simulationInfo, "gravity") is Vector4 value
            ? value
            : new Vector4(0.0f, -9.81f, 0.0f, 0.0f);

        return new ClothSimulationSettings
        {
            GravityX = gravity.X,
            GravityY = gravity.Y,
            GravityZ = gravity.Z,
            DampingPerSecond = Convert.ToSingle(GetValue(simulationInfo, "globalDampingPerSecond") ?? 0.0f, CultureInfo.InvariantCulture),
            CollisionTolerance = Convert.ToSingle(GetValue(simulation, "collisionTolerance") ?? 0.0f, CultureInfo.InvariantCulture),
            TransferTranslationMotion = Convert.ToBoolean(GetValue(transfer, "transferTranslationMotion") ?? false, CultureInfo.InvariantCulture),
            MinTranslationSpeed = Convert.ToSingle(GetValue(transfer, "minTranslationSpeed") ?? 0.0f, CultureInfo.InvariantCulture),
            MaxTranslationSpeed = Convert.ToSingle(GetValue(transfer, "maxTranslationSpeed") ?? 0.0f, CultureInfo.InvariantCulture),
            MinTranslationBlend = Convert.ToSingle(GetValue(transfer, "minTranslationBlend") ?? 0.0f, CultureInfo.InvariantCulture),
            MaxTranslationBlend = Convert.ToSingle(GetValue(transfer, "maxTranslationBlend") ?? 0.0f, CultureInfo.InvariantCulture),
            TransferRotationMotion = Convert.ToBoolean(GetValue(transfer, "transferRotationMotion") ?? false, CultureInfo.InvariantCulture),
            MinRotationSpeed = Convert.ToSingle(GetValue(transfer, "minRotationSpeed") ?? 0.0f, CultureInfo.InvariantCulture),
            MaxRotationSpeed = Convert.ToSingle(GetValue(transfer, "maxRotationSpeed") ?? 0.0f, CultureInfo.InvariantCulture),
            MinRotationBlend = Convert.ToSingle(GetValue(transfer, "minRotationBlend") ?? 0.0f, CultureInfo.InvariantCulture),
            MaxRotationBlend = Convert.ToSingle(GetValue(transfer, "maxRotationBlend") ?? 0.0f, CultureInfo.InvariantCulture)
        };
    }

    // The standalone HKCL -> BPHCL writer needs the fields that sit beside
    // hclSimClothData's particle arrays. They are runtime initialization data,
    // not editor settings, so keep them as an explicit export snapshot.
    public NativeBphclSimulationRuntime GetSimulationRuntimeData(int clothIndex)
    {
        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var nativeSimulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            if (nativeSimulation == null)
                throw new ArgumentOutOfRangeException(nameof(clothIndex));
            return nativeSimulation.Runtime;
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"))
            ?? throw new InvalidOperationException("The selected cloth has no simulation data.");
        var transfer = GetValue(simulation, "transferMotionData");
        var particleRows = GetParticleRowsForCloth(cloth);
        var pinchFlags = (GetList(GetValue(simulation, "perParticlePinchDetectionEnabledFlags")) ?? Array.Empty<object>())
            .Select(ToRuntimeBool)
            .ToArray();
        var pinchingData = (GetList(GetValue(simulation, "collidablePinchingDatas")) ?? Array.Empty<object>())
            .Select(entry => new NativeBphclCollidablePinchingData(
                ToRuntimeBool(GetValue(entry, "pinchDetectionEnabled")),
                unchecked((sbyte)ToInt(GetValue(entry, "pinchDetectionPriority"), 0)),
                Convert.ToSingle(GetValue(entry, "pinchDetectionRadius") ?? 0.0f, CultureInfo.InvariantCulture)))
            .ToArray();

        var maximumParticleRadius = particleRows.Count == 0
            ? 0.0f
            : particleRows.Max(particle => particle.Radius);
        var totalMass = particleRows.Sum(particle => particle.Mass);
        // These fields are BPHCL runtime initialization data. BotW's matching
        // HKCL class exposes different placeholder defaults (for example a
        // 0.05 landscape radius and a 1-16 pinch range), so copying them
        // produces a non-native TotK solver block. The native BPHCL corpus
        // consistently uses this baseline; per-particle landscape masks are
        // generated separately by the standalone graph builder.
        const bool landscapeCollisionEnabled = false;
        var landscapeRuntime = new NativeBphclLandscapeCollisionData(
            LandscapeRadius: 0.0f,
            EnableStuckParticleDetection: false,
            StuckParticlesStretchFactorSq: 9.0f,
            PinchDetectionEnabled: false,
            PinchDetectionPriority: 0,
            PinchDetectionRadius: 0.01f,
            CollisionTolerance: 0.2f);
        return new NativeBphclSimulationRuntime(
            ToRuntimeBool(GetValue(simulation, "doNormals")),
            Convert.ToSingle(GetValue(simulation, "maxParticleRadius") ?? maximumParticleRadius, CultureInfo.InvariantCulture),
            Convert.ToSingle(GetValue(simulation, "totalMass") ?? totalMass, CultureInfo.InvariantCulture),
            new NativeBphclTransferMotionData(
                ToInt(GetValue(transfer, "transformSetIndex"), 0),
                ToInt(GetValue(transfer, "transformIndex"), 0),
                ToRuntimeBool(GetValue(transfer, "transferTranslationMotion")),
                Convert.ToSingle(GetValue(transfer, "minTranslationSpeed") ?? 0.0f, CultureInfo.InvariantCulture),
                Convert.ToSingle(GetValue(transfer, "maxTranslationSpeed") ?? 0.0f, CultureInfo.InvariantCulture),
                Convert.ToSingle(GetValue(transfer, "minTranslationBlend") ?? 0.0f, CultureInfo.InvariantCulture),
                Convert.ToSingle(GetValue(transfer, "maxTranslationBlend") ?? 0.0f, CultureInfo.InvariantCulture),
                ToRuntimeBool(GetValue(transfer, "transferRotationMotion")),
                Convert.ToSingle(GetValue(transfer, "minRotationSpeed") ?? 0.0f, CultureInfo.InvariantCulture),
                Convert.ToSingle(GetValue(transfer, "maxRotationSpeed") ?? 0.0f, CultureInfo.InvariantCulture),
                Convert.ToSingle(GetValue(transfer, "minRotationBlend") ?? 0.0f, CultureInfo.InvariantCulture),
                Convert.ToSingle(GetValue(transfer, "maxRotationBlend") ?? 0.0f, CultureInfo.InvariantCulture)),
            ToRuntimeBool(GetValue(simulation, "transferMotionEnabled")),
            landscapeCollisionEnabled,
            landscapeRuntime,
            0u,
            false,
            pinchFlags,
            pinchingData,
            0,
            0,
            0u);
    }

    // Exports the exact per-simulation collider attachment map. Collider
    // objects are global, but transform indices and offsets are simulation
    // state; recreating them from a displayed transform loses native bases.
    public IReadOnlyList<StandaloneBphclColliderBinding> GetStandaloneColliderBindings(int clothIndex)
    {
        if (_bphcl?.NativeDocument is { } native)
        {
            var nativeSimulation = native.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            if (nativeSimulation is null)
                return Array.Empty<StandaloneBphclColliderBinding>();

            var collidersByItem = native.Colliders.ToDictionary(collider => collider.ItemIndex, collider => collider.Index);
            return nativeSimulation.CollidableItemIndices
                .Select((itemIndex, slot) =>
                {
                    if (!collidersByItem.TryGetValue(itemIndex, out var colliderIndex) ||
                        slot >= nativeSimulation.CollidableTransformMap.TransformIndices.Count ||
                        slot >= nativeSimulation.CollidableTransformMap.Offsets.Count)
                    {
                        return null;
                    }

                    return new StandaloneBphclColliderBinding(
                        colliderIndex,
                        checked((int)nativeSimulation.CollidableTransformMap.TransformIndices[slot]),
                        nativeSimulation.CollidableTransformMap.Offsets[slot]);
                })
                .Where(binding => binding is not null)
                .Cast<StandaloneBphclColliderBinding>()
                .ToArray();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var colliders = GetCollidables(root);
        var hkclSimulation = GetFirst(GetValue(cloth, "simClothDatas"));
        var references = GetList(GetValue(hkclSimulation, "perInstanceCollidables")) ?? Array.Empty<object>();
        var map = GetValue(hkclSimulation, "collidableTransformMap");
        var indices = map == null ? null : GetList(GetValue(map, "transformIndices"));
        var offsets = map == null ? null : GetList(GetValue(map, "offsets"));
        var bindings = new List<StandaloneBphclColliderBinding>();

        for (var slot = 0; slot < references.Count; slot++)
        {
            var colliderIndex = IndexOfReference(colliders, references[slot]);
            if (colliderIndex < 0)
                continue;

            var boneIndex = indices != null && slot < indices.Count ? ToInt(indices[slot], -1) : -1;
            var offset = offsets != null && slot < offsets.Count && offsets[slot] is Matrix4x4 matrix
                ? matrix
                : Matrix4x4.Identity;
            if (boneIndex >= 0)
                bindings.Add(new StandaloneBphclColliderBinding(colliderIndex, boneIndex, offset));
        }

        return bindings;
    }
    public IReadOnlyList<byte> GetTriangleFlips(int clothIndex)
    {
        if (_bphcl?.NativeDocument is { } bphcl)
            return bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault()?.TriangleFlips ?? Array.Empty<byte>();

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"));
        return (GetList(GetValue(simulation, "triangleFlips")) ?? Array.Empty<object>())
            .Select(value => checked((byte)ToInt(value, 0)))
            .ToArray();
    }

    public void UpdateSimulationSettings(int clothIndex, ClothSimulationSettings settings)
    {
        if (_bphcl != null)
            throw new InvalidOperationException("BPHCL simulation settings are currently read-only. Convert to HKCL before editing them.");

        var numericValues = new[]
        {
            settings.GravityX, settings.GravityY, settings.GravityZ, settings.DampingPerSecond, settings.CollisionTolerance,
            settings.MinTranslationSpeed, settings.MaxTranslationSpeed, settings.MinTranslationBlend, settings.MaxTranslationBlend,
            settings.MinRotationSpeed, settings.MaxRotationSpeed, settings.MinRotationBlend, settings.MaxRotationBlend
        };
        if (numericValues.Any(value => !float.IsFinite(value)) || settings.DampingPerSecond < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Simulation values must be finite, and damping cannot be negative.");
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"))
            ?? throw new InvalidOperationException("The selected cloth has no simulation data.");
        var simulationInfo = GetValue(simulation, "simulationInfo") ?? simulation;
        var existingGravity = GetValue(simulationInfo, "gravity") is Vector4 gravity ? gravity : Vector4.Zero;

        SetValue(simulationInfo, "gravity", new Vector4(settings.GravityX, settings.GravityY, settings.GravityZ, existingGravity.W));
        SetValue(simulationInfo, "globalDampingPerSecond", settings.DampingPerSecond);
        SetValue(simulation, "collisionTolerance", settings.CollisionTolerance);
        var transfer = GetValue(simulation, "transferMotionData");
        if (transfer != null)
        {
            SetValue(transfer, "transferTranslationMotion", settings.TransferTranslationMotion);
            SetValue(transfer, "minTranslationSpeed", settings.MinTranslationSpeed);
            SetValue(transfer, "maxTranslationSpeed", settings.MaxTranslationSpeed);
            SetValue(transfer, "minTranslationBlend", settings.MinTranslationBlend);
            SetValue(transfer, "maxTranslationBlend", settings.MaxTranslationBlend);
            SetValue(transfer, "transferRotationMotion", settings.TransferRotationMotion);
            SetValue(transfer, "minRotationSpeed", settings.MinRotationSpeed);
            SetValue(transfer, "maxRotationSpeed", settings.MaxRotationSpeed);
            SetValue(transfer, "minRotationBlend", settings.MinRotationBlend);
            SetValue(transfer, "maxRotationBlend", settings.MaxRotationBlend);
        }
    }

    private static IReadOnlyList<ParticleEditRow> GetParticleRowsForCloth(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var particles = GetList(GetValue(simData, "particleDatas")) ?? Array.Empty<object>();
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetList(GetValue(pose, "positions")) ?? Array.Empty<object>();
        var fixedParticles = new HashSet<int>((GetList(GetValue(simData, "fixedParticles")) ?? Array.Empty<object>()).Select(x => ToInt(x, -1)));
        var collisionMasks = GetList(GetValue(simData, "staticCollisionMasks")) ?? Array.Empty<object>();
        var rows = new List<ParticleEditRow>();

        for (var i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];
            var position = positions.ElementAtOrDefault(i) is Vector4 vector ? vector : Vector4.Zero;
            rows.Add(new ParticleEditRow
            {
                Index = i,
                Fixed = fixedParticles.Contains(i),
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                W = position.W,
                Mass = Convert.ToSingle(GetValue(particle, "mass") ?? 0.0f, CultureInfo.InvariantCulture),
                InverseMass = Convert.ToSingle(GetValue(particle, "invMass") ?? 0.0f, CultureInfo.InvariantCulture),
                Radius = Convert.ToSingle(GetValue(particle, "radius") ?? 0.0f, CultureInfo.InvariantCulture),
                Friction = Convert.ToSingle(GetValue(particle, "friction") ?? 0.0f, CultureInfo.InvariantCulture),
                CollisionMask = ToUInt32(collisionMasks.ElementAtOrDefault(i), 0u)
            });
        }

        return rows;
    }

    public IReadOnlyList<ParticleRelationshipRow> GetParticleRelationships(int clothIndex, int particleIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<ParticleRelationshipRow>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var simulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            if (simulation is null || particleIndex < 0)
                return Array.Empty<ParticleRelationshipRow>();

            var nativeRows = new List<ParticleRelationshipRow>
            {
                new()
                {
                    Kind = "State",
                    Name = simulation.Particles.ElementAtOrDefault(particleIndex)?.Fixed == true ? "Fixed anchor" : "Dynamic particle",
                    Particles = particleIndex.ToString(CultureInfo.InvariantCulture),
                    Details = "Native BPHCL simulation particle."
                }
            };

            foreach (var constraintSet in simulation.ConstraintSets)
            {
                foreach (var link in constraintSet.Links.Where(link => link.ParticleA == particleIndex || link.ParticleB == particleIndex))
                {
                    nativeRows.Add(new ParticleRelationshipRow
                    {
                        Kind = "Link",
                        Name = constraintSet.Name,
                        Particles = $"{link.ParticleA}-{link.ParticleB}",
                        ConstraintSetIndex = constraintSet.Index,
                        LinkIndex = link.Index,
                        ParticleA = link.ParticleA ?? -1,
                        ParticleB = link.ParticleB ?? -1,
                        RestLength = GetNativeConstraintValue(link, "restLength"),
                        BendMinLength = GetNativeConstraintValue(link, "bendMinLength"),
                        StretchMaxLength = GetNativeConstraintValue(link, "stretchMaxLength"),
                        Stiffness = GetNativeConstraintValue(link, "stiffness"),
                        BendStiffness = GetNativeConstraintValue(link, "bendStiffness"),
                        StretchStiffness = GetNativeConstraintValue(link, "stretchStiffness"),
                        CompressionLength = GetNativeConstraintValue(link, "compressionLength"),
                        Details = string.Join("; ", link.Values.Select(value =>
                            $"{value.Key}={value.Value.ToString("G7", CultureInfo.InvariantCulture)}"))
                    });
                }

                foreach (var local in constraintSet.LocalConstraints.Where(link =>
                             link.ParticleA == particleIndex ||
                             link.Values.TryGetValue("particleIndex", out var value) && (int)value == particleIndex))
                {
                    nativeRows.Add(new ParticleRelationshipRow
                    {
                        Kind = "Range",
                        Name = constraintSet.Name,
                        Particles = particleIndex.ToString(CultureInfo.InvariantCulture),
                        ConstraintSetIndex = constraintSet.Index,
                        LocalConstraintIndex = local.Index,
                        ParticleA = particleIndex,
                        MaximumDistance = GetNativeConstraintValue(local, "maximumDistance")
                            ?? GetNativeConstraintValue(local, "maxDistance")
                            // TotK calls this local-range limit shapeRadius.
                            // Expose it through the shared editor field so the
                            // BPHCL UI and conversion diagnostics agree.
                            ?? GetNativeConstraintValue(local, "shapeRadius"),
                        Details = string.Join("; ", local.Values.Select(value =>
                            $"{value.Key}={value.Value.ToString("G7", CultureInfo.InvariantCulture)}"))
                    });
                }
            }

            for (var triangleIndex = 0; triangleIndex + 2 < simulation.TriangleIndices.Count; triangleIndex += 3)
            {
                var a = simulation.TriangleIndices[triangleIndex];
                var b = simulation.TriangleIndices[triangleIndex + 1];
                var c = simulation.TriangleIndices[triangleIndex + 2];
                if (a != particleIndex && b != particleIndex && c != particleIndex)
                    continue;

                nativeRows.Add(new ParticleRelationshipRow
                {
                    Kind = "Triangle",
                    Name = $"Surface #{triangleIndex / 3}",
                    Particles = $"{a}, {b}, {c}",
                    ParticleA = a,
                    ParticleB = b,
                    Details = "Native virtual-cloth surface triangle used by mesh/bone deformation."
                });
            }

            return nativeRows;
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null || particleIndex < 0)
            return Array.Empty<ParticleRelationshipRow>();

        var rows = new List<ParticleRelationshipRow>();
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var fixedParticles = new HashSet<int>((GetList(GetValue(simData, "fixedParticles")) ?? Array.Empty<object>()).Select(x => ToInt(x, -1)));

        rows.Add(new ParticleRelationshipRow
        {
            Kind = "State",
            Name = fixedParticles.Contains(particleIndex) ? "Fixed anchor" : "Dynamic particle",
            Particles = particleIndex.ToString(CultureInfo.InvariantCulture),
            Details = fixedParticles.Contains(particleIndex)
                ? "Pinned to animation/root motion; mass and inverse mass are normally 0."
                : "Simulated by cloth constraints; mass, radius, and links affect motion."
        });

        AddTriangleRelationships(rows, simData, particleIndex);
        AddConstraintRelationships(rows, simData, cloth, particleIndex);

        return rows;
    }

    public IReadOnlyList<ConstraintSetSummary> GetConstraintSetSummaries(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<ConstraintSetSummary>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            return bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault()?.ConstraintSets
                .Select(set => new ConstraintSetSummary(
                    set.Index,
                    set.ClassName,
                    set.Name,
                    set.ConstraintId,
                    set.ConstraintType))
                .ToArray()
                ?? Array.Empty<ConstraintSetSummary>();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return Array.Empty<ConstraintSetSummary>();

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        return constraints
            .Select((constraint, index) => new ConstraintSetSummary(
                index,
                constraint.GetType().Name,
                GetString(constraint, "name") ?? constraint.GetType().Name,
                ToUInt32(GetValue(constraint, "constraintId"), 0u),
                ToUInt32(GetValue(constraint, "constraintType"), 0u)))
            .ToArray();
    }

    // hclBendStiffnessConstraintSet has four-particle records that do not fit
    // the editor's ordinary two-particle link model. Preserve them verbatim
    // for native BPHCL reconstruction.
    public IReadOnlyList<BendStiffnessConstraintSetData> GetBendStiffnessConstraintSets(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<BendStiffnessConstraintSetData>();

        static float NativeValue(IReadOnlyDictionary<string, double> values, string name) =>
            values.TryGetValue(name, out var value) ? (float)value : 0.0f;

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var simulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            return simulation?.ConstraintSets
                .Where(set => string.Equals(set.ClassName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal))
                .Select(set => new BendStiffnessConstraintSetData(
                    set.Index,
                    set.Name,
                    set.ConstraintId,
                    set.ConstraintType,
                    NativeValue(set.Values, "clampBendStiffness") != 0.0f,
                    NativeValue(set.Values, "maxRestPoseHeightSq"),
                    NativeValue(set.Values, "useRestPoseConfig") != 0.0f,
                    set.Links.Select(link => new BendStiffnessConstraintData(
                        link.Index,
                        link.ParticleA ?? 0,
                        link.ParticleB ?? 0,
                        (int)NativeValue(link.Values, "particleC"),
                        (int)NativeValue(link.Values, "particleD"),
                        NativeValue(link.Values, "bendStiffness"),
                        NativeValue(link.Values, "restCurvature"),
                        NativeValue(link.Values, "weightA"),
                        NativeValue(link.Values, "weightB"),
                        NativeValue(link.Values, "weightC"),
                        NativeValue(link.Values, "weightD"))).ToArray()))
                .ToArray()
                ?? Array.Empty<BendStiffnessConstraintSetData>();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth is null)
            return Array.Empty<BendStiffnessConstraintSetData>();

        var simulationData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraints = GetList(GetValue(simulationData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        return constraints.Select((constraint, index) => (constraint, index))
            .Where(entry => string.Equals(entry.constraint.GetType().Name, "hclBendStiffnessConstraintSet", StringComparison.Ordinal))
            .Select(entry =>
            {
                var links = GetList(GetValue(entry.constraint, "links")) ?? Array.Empty<object>();
                return new BendStiffnessConstraintSetData(
                    entry.index,
                    GetString(entry.constraint, "name") ?? "hclBendStiffnessConstraintSet",
                    ToUInt32(GetValue(entry.constraint, "constraintId"), 0u),
                    ToUInt32(GetValue(entry.constraint, "constraintType"), 0u),
                    ToRuntimeBool(GetValue(entry.constraint, "clampBendStiffness")),
                    ReadConstraintFloat(entry.constraint, "maxRestPoseHeightSq") ?? 0.0f,
                    ToRuntimeBool(GetValue(entry.constraint, "useRestPoseConfig")),
                    links.Select((link, linkIndex) => new BendStiffnessConstraintData(
                        linkIndex,
                        ToInt(GetValue(link, "particleA"), 0),
                        ToInt(GetValue(link, "particleB"), 0),
                        ToInt(GetValue(link, "particleC"), 0),
                        ToInt(GetValue(link, "particleD"), 0),
                        ReadConstraintFloat(link, "bendStiffness") ?? 0.0f,
                        ReadConstraintFloat(link, "restCurvature") ?? 0.0f,
                        ReadConstraintFloat(link, "weightA") ?? 0.0f,
                        ReadConstraintFloat(link, "weightB") ?? 0.0f,
                        ReadConstraintFloat(link, "weightC") ?? 0.0f,
                        ReadConstraintFloat(link, "weightD") ?? 0.0f)).ToArray());
            })
            .ToArray();
    }
    // The transition set controls the animation-to-simulation handoff. It is
    // intentionally exposed as serializer data rather than UI relationships:
    // its records are per-particle timing data, not particle-particle links.
    public IReadOnlyList<TransitionConstraintSetData> GetTransitionConstraintSets(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<TransitionConstraintSetData>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            return bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault()?.ConstraintSets
                .Where(set => string.Equals(set.ClassName, "hclTransitionConstraintSet", StringComparison.Ordinal))
                .Select(set => new TransitionConstraintSetData(
                    set.Index,
                    set.Name,
                    set.ConstraintId,
                    set.ConstraintType,
                    GetNativeConstraintValue(set.Values, "toAnimPeriod") ?? 0.0f,
                    GetNativeConstraintValue(set.Values, "toAnimPlusDelayPeriod") ?? 0.0f,
                    GetNativeConstraintValue(set.Values, "toSimPeriod") ?? 0.0f,
                    GetNativeConstraintValue(set.Values, "toSimPlusDelayPeriod") ?? 0.0f,
                    (uint)(GetNativeConstraintValue(set.Values, "referenceMeshBufferIdx") ?? 0.0f),
                    set.TransitionParticles.Select(record => new TransitionConstraintParticleData(
                        record.Index,
                        (int)(GetNativeConstraintValue(record, "particleIndex") ?? 0.0f),
                        (int)(GetNativeConstraintValue(record, "referenceVertex") ?? 0.0f),
                        GetNativeConstraintValue(record, "toAnimDelay") ?? 0.0f,
                        GetNativeConstraintValue(record, "toSimDelay") ?? 0.0f,
                        GetNativeConstraintValue(record, "toSimMaxDistance") ?? 0.0f)).ToArray()))
                .ToArray()
                ?? Array.Empty<TransitionConstraintSetData>();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return Array.Empty<TransitionConstraintSetData>();

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        var result = new List<TransitionConstraintSetData>();
        for (var setIndex = 0; setIndex < constraints.Count; setIndex++)
        {
            var constraint = constraints[setIndex];
            if (!string.Equals(constraint.GetType().Name, "hclTransitionConstraintSet", StringComparison.Ordinal))
                continue;

            var records = GetList(GetValue(constraint, "perParticleData")) ?? Array.Empty<object>();
            result.Add(new TransitionConstraintSetData(
                setIndex,
                GetString(constraint, "name") ?? constraint.GetType().Name,
                ToUInt32(GetValue(constraint, "constraintId"), 0u),
                ToUInt32(GetValue(constraint, "constraintType"), 0u),
                ReadConstraintFloat(constraint, "toAnimPeriod") ?? 0.0f,
                ReadConstraintFloat(constraint, "toAnimPlusDelayPeriod") ?? 0.0f,
                ReadConstraintFloat(constraint, "toSimPeriod") ?? 0.0f,
                ReadConstraintFloat(constraint, "toSimPlusDelayPeriod") ?? 0.0f,
                ToUInt32(GetValue(constraint, "referenceMeshBufferIdx"), 0u),
                records.Select((record, recordIndex) => new TransitionConstraintParticleData(
                    recordIndex,
                    ToInt(GetValue(record, "particleIndex"), 0),
                    ToInt(GetValue(record, "referenceVertex"), 0),
                    ReadConstraintFloat(record, "toAnimDelay") ?? 0.0f,
                    ReadConstraintFloat(record, "toSimDelay") ?? 0.0f,
                    ReadConstraintFloat(record, "toSimMaxDistance") ?? 0.0f)).ToArray()));
        }

        return result;
    }

    // Local ranges are not particle-to-particle links. They pin a particle to
    // a radius around a specific reference-mesh vertex, so every field is
    // needed when rebuilding a native BPHCL constraint set.
    public IReadOnlyList<LocalRangeConstraintSetData> GetLocalRangeConstraintSets(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<LocalRangeConstraintSetData>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var simulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            return simulation?.ConstraintSets
                .Where(set => string.Equals(set.ClassName, "hclLocalRangeConstraintSet", StringComparison.Ordinal))
                .Select(set => new LocalRangeConstraintSetData(
                    set.Index,
                    set.Name,
                    set.ConstraintId,
                    set.ConstraintType,
                    (uint)(GetNativeConstraintValue(set.Values, "referenceMeshBufferIdx") ?? 0.0f),
                    GetNativeConstraintValue(set.Values, "stiffness") ?? 0.0f,
                    (int)(GetNativeConstraintValue(set.Values, "shapeType") ?? 0.0f),
                    (GetNativeConstraintValue(set.Values, "applyNormalComponent") ?? 0.0f) != 0.0f,
                    set.LocalConstraints.Select(record => new LocalRangeConstraintData(
                            record.Index,
                            (int)(GetNativeConstraintValue(record, "particleIndex") ?? 0.0f),
                            (int)(GetNativeConstraintValue(record, "referenceVertex") ?? 0.0f),
                            GetNativeConstraintValue(record, "shapeRadius")
                                ?? GetNativeConstraintValue(record, "maximumDistance")
                                ?? GetNativeConstraintValue(record, "maxDistance")
                                ?? 0.0f,
                            GetNativeConstraintValue(record, "maxNormalDistance") ?? float.MaxValue,
                            GetNativeConstraintValue(record, "minNormalDistance") ?? -float.MaxValue))
                        .Concat(set.LocalStiffnessConstraints.Select(record => new LocalRangeConstraintData(
                            record.Index,
                            (int)(GetNativeConstraintValue(record, "particleIndex") ?? 0.0f),
                            (int)(GetNativeConstraintValue(record, "referenceVertex") ?? 0.0f),
                            GetNativeConstraintValue(record, "shapeRadius")
                                ?? GetNativeConstraintValue(record, "maximumDistance")
                                ?? GetNativeConstraintValue(record, "maxDistance")
                                ?? 0.0f,
                            GetNativeConstraintValue(record, "maxNormalDistance") ?? float.MaxValue,
                            GetNativeConstraintValue(record, "minNormalDistance") ?? -float.MaxValue,
                            "hclLocalRangeConstraintSet::LocalStiffnessConstraint",
                            GetNativeConstraintValue(record, "stiffness"))))
                        .ToArray()))
                .ToArray()
                ?? Array.Empty<LocalRangeConstraintSetData>();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return Array.Empty<LocalRangeConstraintSetData>();

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraintSets = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        var result = new List<LocalRangeConstraintSetData>();
        for (var setIndex = 0; setIndex < constraintSets.Count; setIndex++)
        {
            var constraint = constraintSets[setIndex];
            if (!string.Equals(constraint.GetType().Name, "hclLocalRangeConstraintSet", StringComparison.Ordinal))
                continue;

            var records = GetList(GetValue(constraint, "localConstraints")) ?? Array.Empty<object>();
            result.Add(new LocalRangeConstraintSetData(
                setIndex,
                GetString(constraint, "name") ?? constraint.GetType().Name,
                ToUInt32(GetValue(constraint, "constraintId"), 0u),
                ToUInt32(GetValue(constraint, "constraintType"), 0u),
                ToUInt32(GetValue(constraint, "referenceMeshBufferIdx"), 0u),
                ReadConstraintFloat(constraint, "stiffness") ?? 0.0f,
                ToInt(GetValue(constraint, "shapeType"), 0),
                ToBool(GetValue(constraint, "applyNormalComponent")),
                records.Select((record, recordIndex) => new LocalRangeConstraintData(
                    recordIndex,
                    ToInt(GetValue(record, "particleIndex"), 0),
                    ToInt(GetValue(record, "referenceVertex"), 0),
                    ReadConstraintFloat(record, "maximumDistance")
                        ?? ReadConstraintFloat(record, "shapeRadius")
                        ?? ReadConstraintFloat(record, "maxDistance")
                        ?? 0.0f,
                    ReadConstraintFloat(record, "maxNormalDistance") ?? float.MaxValue,
                    ReadConstraintFloat(record, "minNormalDistance") ?? -float.MaxValue)).ToArray()));
        }

        return result;
    }

    // Bone planes constrain a particle against a plane in a transform-set
    // bone's local space. Keep them outside the link/range UI model because
    // their equation and transform index are a distinct record layout.
    public IReadOnlyList<BonePlaneConstraintSetData> GetBonePlaneConstraintSets(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<BonePlaneConstraintSetData>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var simulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            return simulation?.ConstraintSets
                .Where(set => string.Equals(set.ClassName, "hclBonePlanesConstraintSet", StringComparison.Ordinal))
                .Select(set => new BonePlaneConstraintSetData(
                    set.Index,
                    set.Name,
                    set.ConstraintId,
                    set.ConstraintType,
                    (uint)(GetNativeConstraintValue(set.Values, "transformSetIndex") ?? 0.0f),
                    set.Links.Select(plane => new BonePlaneConstraintData(
                        plane.Index,
                        plane.PlaneEquationBone ?? Vector4.Zero,
                        (int)(GetNativeConstraintValue(plane, "particleIndex") ?? 0.0f),
                        (int)(GetNativeConstraintValue(plane, "transformIndex") ?? 0.0f),
                        GetNativeConstraintValue(plane, "stiffness") ?? 0.0f)).ToArray()))
                .ToArray()
                ?? Array.Empty<BonePlaneConstraintSetData>();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return Array.Empty<BonePlaneConstraintSetData>();

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        var result = new List<BonePlaneConstraintSetData>();
        for (var setIndex = 0; setIndex < constraints.Count; setIndex++)
        {
            var constraint = constraints[setIndex];
            if (!string.Equals(constraint.GetType().Name, "hclBonePlanesConstraintSet", StringComparison.Ordinal))
                continue;

            var planes = GetList(GetValue(constraint, "bonePlanes")) ?? Array.Empty<object>();
            result.Add(new BonePlaneConstraintSetData(
                setIndex,
                GetString(constraint, "name") ?? constraint.GetType().Name,
                ToUInt32(GetValue(constraint, "constraintId"), 0u),
                ToUInt32(GetValue(constraint, "constraintType"), 0u),
                ToUInt32(GetValue(constraint, "transformSetIndex"), 0u),
                planes.Select((plane, planeIndex) => new BonePlaneConstraintData(
                    planeIndex,
                    GetValue(plane, "planeEquationBone") is Vector4 equation ? equation : Vector4.Zero,
                    ToInt(GetValue(plane, "particleIndex"), 0),
                    ToInt(GetValue(plane, "transformIndex"), 0),
                    ReadConstraintFloat(plane, "stiffness") ?? 0.0f)).ToArray()));
        }

        return result;
    }

    // Export-only view of the mesh-to-bone output stage. The editor preview
    // can infer a friendly particle binding, but standalone serialization
    // needs these original offsets and matrices verbatim.
    public IReadOnlyList<SimpleMeshBoneDeformData> GetSimpleMeshBoneDeformData(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<SimpleMeshBoneDeformData>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            return bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimpleMeshBoneDeformers
                .Select(deformer => new SimpleMeshBoneDeformData(
                    deformer.OperatorIndex,
                    checked((int)deformer.InputBufferIndex),
                    checked((int)deformer.OutputTransformSetIndex),
                    deformer.BoneAxis,
                    deformer.TriangleBonePairs.Select(pair => new SimpleMeshTriangleBonePairData(
                        pair.BoneOffset,
                        pair.TriangleOffset)).ToArray(),
                    deformer.LocalBoneTransforms.ToArray()))
                .ToArray()
                ?? Array.Empty<SimpleMeshBoneDeformData>();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return Array.Empty<SimpleMeshBoneDeformData>();

        var result = new List<SimpleMeshBoneDeformData>();
        foreach (var (op, operatorIndex) in (GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>())
                     .Select((op, index) => (op, index)))
        {
            if (!string.Equals(op.GetType().Name, "hclSimpleMeshBoneDeformOperator", StringComparison.Ordinal))
                continue;

            var pairs = (GetList(GetValue(op, "triangleBonePairs")) ?? Array.Empty<object>())
                .Select(pair => new SimpleMeshTriangleBonePairData(
                    checked((ushort)ToInt(GetValue(pair, "boneOffset"), 0)),
                    checked((ushort)ToInt(GetValue(pair, "triangleOffset"), 0))))
                .ToArray();
            var localTransforms = (GetList(GetValue(op, "localBoneTransforms")) ?? Array.Empty<object>())
                .OfType<Matrix4x4>()
                .ToArray();
            result.Add(new SimpleMeshBoneDeformData(
                operatorIndex,
                ToInt(GetValue(op, "inputBufferIdx"), 0),
                ToInt(GetValue(op, "outputTransformSetIdx"), 0),
                ToInt(GetValue(op, "boneAxis"), 0),
                pairs,
                localTransforms));
        }

        return result;
    }

    // Export-only representation of the animation-to-virtual-cloth stage.
    // This preserves the actual blend blocks and local positions used by the
    // HKCL, rather than inferring skinning from the later bone-output stage.
    public IReadOnlyList<SkinOperatorData> GetSkinOperatorData(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<SkinOperatorData>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var cloth = bphcl.Cloths.ElementAtOrDefault(clothIndex);
            if (cloth is null)
                return Array.Empty<SkinOperatorData>();

            return cloth.ObjectSpaceSkins.Select(skin => new SkinOperatorData(
                    skin.OperatorIndex,
                    "hclObjectSpaceSkinPOperator",
                    skin.OutputBufferIndex,
                    skin.TransformSetIndex,
                    skin.BoneFromSkinMeshTransforms,
                    skin.TransformSubset,
                    CreateObjectSpaceBlendArrays(skin.Deformer),
                    skin.Deformer.ControlBytes,
                    skin.Deformer.StartVertexIndex,
                    skin.Deformer.EndVertexIndex,
                    0,
                    skin.Deformer.PartialWrite,
                    skin.LocalPs.Select(block => new SkinPositionBlockData(
                        Array.Empty<Vector4>(),
                        block.Positions)).ToArray(),
                    skin.LocalUnpackedPs.Select(block => new SkinPositionBlockData(block.Positions)).ToArray()))
                .Concat(cloth.BoneSpaceSkins.Select(skin => new SkinOperatorData(
                    skin.OperatorIndex,
                    "hclBoneSpaceSkinPOperator",
                    skin.OutputBufferIndex,
                    skin.TransformSetIndex,
                    Array.Empty<Matrix4x4>(),
                    skin.TransformSubset,
                    CreateBoneSpaceBlendArrays(skin.Deformer),
                    skin.Deformer.ControlBytes,
                    skin.Deformer.StartVertexIndex,
                    skin.Deformer.EndVertexIndex,
                    skin.Deformer.BatchSizeSpu,
                    skin.Deformer.PartialWrite,
                    skin.LocalPs.Select(block => new SkinPositionBlockData(block.Positions)).ToArray(),
                    skin.LocalUnpackedPs.Select(block => new SkinPositionBlockData(block.Positions)).ToArray())))
                .ToArray();
        }

        var root = RequireRoot();
        var clothData = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (clothData is null)
            return Array.Empty<SkinOperatorData>();

        var result = new List<SkinOperatorData>();
        foreach (var (op, operatorIndex) in (GetList(GetValue(clothData, "operators")) ?? Array.Empty<object>())
                     .Select((op, index) => (op, index)))
        {
            var className = op.GetType().Name;
            if (className is not ("hclObjectSpaceSkinPOperator" or "hclBoneSpaceSkinPOperator"))
                continue;

            var objectSpace = string.Equals(className, "hclObjectSpaceSkinPOperator", StringComparison.Ordinal);
            var deformer = GetValue(op, objectSpace ? "objectSpaceDeformer" : "boneSpaceDeformer")
                ?? throw new InvalidDataException($"{className} has no deformer payload.");
            result.Add(new SkinOperatorData(
                operatorIndex,
                className,
                ToInt(GetValue(op, "outputBufferIndex"), 0),
                ToInt(GetValue(op, "transformSetIndex"), 0),
                objectSpace
                    ? (GetList(GetValue(op, "boneFromSkinMeshTransforms")) ?? Array.Empty<object>()).OfType<Matrix4x4>().ToArray()
                    : Array.Empty<Matrix4x4>(),
                (GetList(GetValue(op, "transformSubset")) ?? Array.Empty<object>())
                    .Select(value => checked((ushort)ToInt(value, 0))).ToArray(),
                objectSpace ? ReadObjectSpaceBlendArrays(deformer) : ReadBoneSpaceBlendArrays(deformer),
                (GetList(GetValue(deformer, "controlBytes")) ?? Array.Empty<object>())
                    .Select(value => checked((byte)ToInt(value, 0))).ToArray(),
                checked((ushort)ToInt(GetValue(deformer, "startVertexIndex"), 0)),
                checked((ushort)ToInt(GetValue(deformer, "endVertexIndex"), 0)),
                checked((ushort)ToInt(GetValue(deformer, "batchSizeSpu"), 0)),
                Convert.ToBoolean(GetValue(deformer, "partialWrite") ?? false, CultureInfo.InvariantCulture),
                ReadSkinPositionBlocks(GetList(GetValue(op, "localPs")) ?? Array.Empty<object>()),
                ReadSkinPositionBlocks(GetList(GetValue(op, "localUnpackedPs")) ?? Array.Empty<object>())));
        }

        return result;
    }

    private static IReadOnlyList<SkinBlendArrayData> ReadObjectSpaceBlendArrays(object deformer) =>
        new[]
        {
            ReadSkinBlendArray(deformer, "eightBlendEntries", 8, 16, 128),
            ReadSkinBlendArray(deformer, "sevenBlendEntries", 7, 16, 112),
            ReadSkinBlendArray(deformer, "sixBlendEntries", 6, 16, 96),
            ReadSkinBlendArray(deformer, "fiveBlendEntries", 5, 16, 80),
            ReadSkinBlendArray(deformer, "fourBlendEntries", 4, 16, 64),
            ReadSkinBlendArray(deformer, "threeBlendEntries", 3, 16, 48),
            ReadSkinBlendArray(deformer, "twoBlendEntries", 2, 16, 32),
            ReadSkinBlendArray(deformer, "oneBlendEntries", 1, 16, 16)
        };

    private static IReadOnlyList<SkinBlendArrayData> ReadBoneSpaceBlendArrays(object deformer) =>
        new[]
        {
            ReadSkinBlendArray(deformer, "fourBlendEntries", 4, 4, 16, hasExplicitWeights: false),
            ReadSkinBlendArray(deformer, "threeBlendEntries", 3, 5, 15, hasExplicitWeights: false),
            ReadSkinBlendArray(deformer, "twoBlendEntries", 2, 8, 16, hasExplicitWeights: false),
            ReadSkinBlendArray(deformer, "oneBlendEntries", 1, 16, 16, hasExplicitWeights: false)
        };

    private static SkinBlendArrayData ReadSkinBlendArray(
        object deformer,
        string memberName,
        int blendCount,
        int vertexCount,
        int boneCount,
        bool hasExplicitWeights = true)
    {
        var blocks = (GetList(GetValue(deformer, memberName)) ?? Array.Empty<object>())
            .Select(block => new SkinBlendBlockData(
                ReadSkinUInt16Fields(block, "vertexIndices", vertexCount),
                ReadSkinUInt16Fields(block, "boneIndices", boneCount),
                hasExplicitWeights && blendCount > 1
                    ? ReadSkinByteFields(block, "boneWeights", boneCount)
                    : Array.Empty<byte>()))
            .ToArray();
        return new SkinBlendArrayData(memberName, blendCount, vertexCount, boneCount, blocks);
    }

    private static IReadOnlyList<SkinPositionBlockData> ReadSkinPositionBlocks(IReadOnlyList<object> blocks)
    {
        var result = new List<SkinPositionBlockData>(blocks.Count);
        foreach (var block in blocks)
        {
            var positions = new Vector4[16];
            for (var index = 0; index < positions.Length; index++)
            {
                if (GetValue(block, $"localPosition_{index}") is not Vector4 position)
                {
                    throw new InvalidDataException(
                        $"The skinning position block '{block.GetType().Name}' has no Vector4 localPosition_{index} field.");
                }
                positions[index] = position;
            }
            result.Add(new SkinPositionBlockData(positions));
        }
        return result;
    }

    private static IReadOnlyList<ushort> ReadSkinUInt16Fields(object block, string prefix, int count)
    {
        var result = new ushort[count];
        for (var index = 0; index < count; index++)
            result[index] = checked((ushort)ToInt(GetValue(block, $"{prefix}_{index}"), 0));
        return result;
    }

    private static IReadOnlyList<byte> ReadSkinByteFields(object block, string prefix, int count)
    {
        var result = new byte[count];
        for (var index = 0; index < count; index++)
            result[index] = checked((byte)ToInt(GetValue(block, $"{prefix}_{index}"), 0));
        return result;
    }

    private static IReadOnlyList<SkinBlendArrayData> CreateObjectSpaceBlendArrays(NativeBphclObjectSpaceDeformer deformer) =>
        new[]
        {
            CreatePackedSkinBlendArray("eightBlendEntries", 8, 16, 128, deformer.EightBlendEntries),
            CreatePackedSkinBlendArray("sevenBlendEntries", 7, 16, 112, deformer.SevenBlendEntries),
            CreatePackedSkinBlendArray("sixBlendEntries", 6, 16, 96, deformer.SixBlendEntries),
            CreatePackedSkinBlendArray("fiveBlendEntries", 5, 16, 80, deformer.FiveBlendEntries),
            CreatePackedSkinBlendArray("fourBlendEntries", 4, 16, 64, deformer.FourBlendEntries),
            CreatePackedSkinBlendArray("threeBlendEntries", 3, 16, 48, deformer.ThreeBlendEntries),
            CreatePackedSkinBlendArray("twoBlendEntries", 2, 16, 32, deformer.TwoBlendEntries),
            CreatePackedSkinBlendArray("oneBlendEntries", 1, 16, 16, deformer.OneBlendEntries)
        };

    private static IReadOnlyList<SkinBlendArrayData> CreateBoneSpaceBlendArrays(NativeBphclBoneSpaceDeformer deformer) =>
        new[]
        {
            CreatePackedSkinBlendArray("fourBlendEntries", 4, 4, 16, deformer.FourBlendEntries, hasExplicitWeights: false),
            CreatePackedSkinBlendArray("threeBlendEntries", 3, 5, 15, deformer.ThreeBlendEntries, hasExplicitWeights: false),
            CreatePackedSkinBlendArray("twoBlendEntries", 2, 8, 16, deformer.TwoBlendEntries, hasExplicitWeights: false),
            CreatePackedSkinBlendArray("oneBlendEntries", 1, 16, 16, deformer.OneBlendEntries, hasExplicitWeights: false)
        };

    private static SkinBlendArrayData CreatePackedSkinBlendArray(
        string memberName,
        int blendCount,
        int vertexCount,
        int boneCount,
        IReadOnlyList<NativeBphclPackedArray> arrays,
        bool hasExplicitWeights = true)
    {
        var blocks = new List<SkinBlendBlockData>();
        foreach (var array in arrays)
        {
            var required = checked((vertexCount + boneCount) * sizeof(ushort) +
                (hasExplicitWeights && blendCount > 1 ? boneCount : 0));
            if (array.ElementSize < required)
            {
                throw new InvalidDataException(
                    $"Packed skin array '{array.ElementType}' is {array.ElementSize} bytes, but {memberName} requires {required} bytes.");
            }
            for (var blockIndex = 0; blockIndex < array.Count; blockIndex++)
            {
                var baseOffset = checked(blockIndex * array.ElementSize);
                var vertices = new ushort[vertexCount];
                var bones = new ushort[boneCount];
                var weights = hasExplicitWeights && blendCount > 1 ? new byte[boneCount] : Array.Empty<byte>();
                for (var index = 0; index < vertexCount; index++)
                    vertices[index] = ReadPackedUInt16(array.Bytes, baseOffset + index * sizeof(ushort));
                var boneOffset = baseOffset + vertexCount * sizeof(ushort);
                for (var index = 0; index < boneCount; index++)
                    bones[index] = ReadPackedUInt16(array.Bytes, boneOffset + index * sizeof(ushort));
                if (hasExplicitWeights && blendCount > 1)
                {
                    var weightOffset = boneOffset + boneCount * sizeof(ushort);
                    for (var index = 0; index < boneCount; index++)
                        weights[index] = array.Bytes[weightOffset + index];
                }
                blocks.Add(new SkinBlendBlockData(vertices, bones, weights));
            }
        }
        return new SkinBlendArrayData(memberName, blendCount, vertexCount, boneCount, blocks);
    }

    private static ushort ReadPackedUInt16(IReadOnlyList<byte> bytes, int offset)
    {
        if (offset < 0 || offset > bytes.Count - sizeof(ushort))
            throw new InvalidDataException("Packed skinning block ends before a UInt16 field.");
        return BinaryPrimitives.ReadUInt16LittleEndian(new[] { bytes[offset], bytes[offset + 1] });
    }

    // Export-only snapshot of the source execution graph. The standalone
    // BPHCL writer uses it to retain the same buffers, operator ordering, and
    // state routing as the original HKCL rather than inferring a generic
    // three-buffer pipeline.
    public ClothRuntimeData GetClothRuntimeData(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return new ClothRuntimeData(
                Array.Empty<ClothBufferDefinitionData>(),
                Array.Empty<ClothTransformSetDefinitionData>(),
                Array.Empty<ClothOperatorData>(),
                Array.Empty<ClothStateRuntimeData>());

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var cloth = bphcl.Cloths.ElementAtOrDefault(clothIndex);
            if (cloth is null)
                return new ClothRuntimeData(
                    Array.Empty<ClothBufferDefinitionData>(),
                    Array.Empty<ClothTransformSetDefinitionData>(),
                    Array.Empty<ClothOperatorData>(),
                    Array.Empty<ClothStateRuntimeData>());

            return new ClothRuntimeData(
                cloth.BufferDefinitions.Select(buffer => new ClothBufferDefinitionData(
                    buffer.Index,
                    buffer.ClassName,
                    buffer.MeshName,
                    buffer.BufferName,
                    buffer.Type,
                    buffer.SubType,
                    checked((int)buffer.VertexCount),
                    checked((int)buffer.TriangleCount),
                    Array.Empty<ushort>())).ToArray(),
                cloth.TransformSetDefinitions.Select(transformSet => new ClothTransformSetDefinitionData(
                    transformSet.Index,
                    transformSet.Name,
                    transformSet.Type,
                    checked((int)transformSet.TransformCount))).ToArray(),
                cloth.Operators.Select(@operator => new ClothOperatorData(
                    @operator.Index,
                    @operator.ClassName,
                    @operator.InputBufferIndex,
                    @operator.OutputBufferIndex,
                    @operator.TransformSetIndex,
                    @operator.VertexCount,
                    @operator.StartVertexIn,
                    @operator.StartVertexOut,
                    @operator.VertexInputFromVertexOutput,
                    false,
                    false,
                    @operator.VertexParticlePairs.Select(pair => new ClothVertexParticlePairData(
                        pair.VertexIndex,
                        pair.ParticleIndex)).ToArray(),
                    @operator.SimulationClothIndex,
                    @operator.ReferenceBufferIndex,
                    @operator.SimulateConfigs.Select(config => new ClothSimulateConfigData(
                        config.ConstraintExecution,
                        config.InstanceCollidablesUsed,
                        config.SubSteps,
                        config.NumberOfSolveIterations,
                        config.UseAllInstanceCollidables,
                        config.AdaptConstraintStiffness)).ToArray(),
                    @operator.TransformSetAccesses.Select(access => new ClothStateTransformSetAccessData(
                        checked((int)access.TransformSetIndex),
                        access.ComponentFlags0,
                        access.ComponentFlags1,
                        access.TransformTrackers.Select(tracker => new ClothStateTransformTrackerData(
                            new ClothStateBitFieldData(tracker.Read.Words, tracker.Read.NumBits),
                            new ClothStateBitFieldData(tracker.ReadBeforeWrite.Words, tracker.ReadBeforeWrite.NumBits),
                            new ClothStateBitFieldData(tracker.Written.Words, tracker.Written.NumBits))).ToArray())).ToArray(),
                    @operator.Name)).ToArray(),
                cloth.States.Select(state => new ClothStateRuntimeData(
                    state.Index,
                    state.Name,
                    state.OperatorItemIndices,
                    state.BufferAccesses.Select(access => checked((int)access.BufferIndex)).ToArray(),
                    state.TransformSetAccesses.Select(access => checked((int)access.TransformSetIndex)).ToArray(),
                    state.SimClothItemIndices,
                    state.BufferAccesses.Select(access => new ClothStateBufferAccessData(
                        checked((int)access.BufferIndex),
                        access.ComponentFlags0,
                        access.ComponentFlags1,
                        access.ComponentFlags2,
                        access.ComponentFlags3,
                        access.TrianglesRead,
                        checked((int)access.ShadowBufferIndex))).ToArray(),
                    state.TransformSetAccesses.Select(access => new ClothStateTransformSetAccessData(
                        checked((int)access.TransformSetIndex),
                        access.ComponentFlags0,
                        access.ComponentFlags1,
                        access.TransformTrackers.Select(tracker => new ClothStateTransformTrackerData(
                            new ClothStateBitFieldData(tracker.Read.Words, tracker.Read.NumBits),
                            new ClothStateBitFieldData(tracker.ReadBeforeWrite.Words, tracker.ReadBeforeWrite.NumBits),
                            new ClothStateBitFieldData(tracker.Written.Words, tracker.Written.NumBits))).ToArray())).ToArray())).ToArray());
        }

        var root = RequireRoot();
        var clothData = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (clothData is null)
            return new ClothRuntimeData(
                Array.Empty<ClothBufferDefinitionData>(),
                Array.Empty<ClothTransformSetDefinitionData>(),
                Array.Empty<ClothOperatorData>(),
                Array.Empty<ClothStateRuntimeData>());

        var buffers = (GetList(GetValue(clothData, "bufferDefinitions")) ?? Array.Empty<object>())
            .Select((buffer, index) => new ClothBufferDefinitionData(
                index,
                buffer.GetType().Name,
                GetString(buffer, "meshName") ?? string.Empty,
                GetString(buffer, "name") ?? $"Buffer {index}",
                ToInt(GetValue(buffer, "type"), 0),
                ToInt(GetValue(buffer, "subType"), 0),
                ToInt(GetValue(buffer, "numVertices"), 0),
                ToInt(GetValue(buffer, "numTriangles"), 0),
                (GetList(GetValue(buffer, "triangleIndices")) ?? Array.Empty<object>())
                    .Select(value => checked((ushort)ToInt(value, 0)))
                    .ToArray()))
            .ToArray();

        var operators = (GetList(GetValue(clothData, "operators")) ?? Array.Empty<object>())
            .Select((@operator, index) =>
            {
                var className = @operator.GetType().Name;
                var simulateConfigs = className == "hclSimulateOperator"
                    ? new[]
                    {
                        new ClothSimulateConfigData(
                            ReadRuntimeIntList(@operator, "constraintExecution"),
                            ReadRuntimeIntList(@operator, "instanceCollidablesUsed"),
                            checked((byte)ToInt(GetValue(@operator, "subSteps"), 1)),
                            checked((byte)ToInt(GetValue(@operator, "numberOfSolveIterations"), 1)),
                            ToRuntimeBool(GetValue(@operator, "useAllInstanceCollidables")),
                            ToRuntimeBool(GetValue(@operator, "adaptConstraintStiffness")))
                    }
                    : Array.Empty<ClothSimulateConfigData>();
                var pairs = (GetList(GetValue(@operator, "vertexParticlePairs")) ?? Array.Empty<object>())
                    .Select(pair => new ClothVertexParticlePairData(
                        checked((ushort)ToInt(GetValue(pair, "vertexIndex"), 0)),
                        checked((ushort)ToInt(GetValue(pair, "particleIndex"), 0))))
                    .ToArray();
                return new ClothOperatorData(
                    index,
                    className,
                    ReadRuntimeOptionalInt(@operator, "inputBufferIdx"),
                    ReadRuntimeOptionalInt(@operator, "outputBufferIdx", "outputBufferIndex"),
                    ReadRuntimeOptionalInt(@operator, "outputTransformSetIdx", "transformSetIndex"),
                    ReadRuntimeOptionalInt(@operator, "numberOfVertices"),
                    ReadRuntimeOptionalInt(@operator, "startVertexIn"),
                    ReadRuntimeOptionalInt(@operator, "startVertexOut"),
                    (GetList(GetValue(@operator, "vertexInputFromVertexOutput")) ?? Array.Empty<object>())
                        .Select(value => checked((short)ToInt(value, 0))).ToArray(),
                    ToRuntimeBool(GetValue(@operator, "gatherNormals")),
                    ToRuntimeBool(GetValue(@operator, "partialGather")),
                    pairs,
                    ReadRuntimeOptionalInt(@operator, "simClothIndex"),
                    ReadRuntimeOptionalInt(@operator, "refBufferIdx"),
                    simulateConfigs,
                    null,
                    GetString(@operator, "name"));
            })
            .ToArray();

        var transformSets = (GetList(GetValue(clothData, "transformSetDefinitions")) ?? Array.Empty<object>())
            .Select((transformSet, index) => new ClothTransformSetDefinitionData(
                index,
                GetString(transformSet, "name") ?? $"Transform set {index}",
                ToInt(GetValue(transformSet, "type"), 0),
                ToInt(GetValue(transformSet, "numTransforms"), 0)))
            .ToArray();

        var states = (GetList(GetValue(clothData, "clothStateDatas")) ?? Array.Empty<object>())
            .Select((state, index) =>
            {
                var bufferAccesses = ReadRuntimeStateBufferAccesses(state);
                var transformSetAccesses = ReadRuntimeStateTransformSetAccesses(state);
                return new ClothStateRuntimeData(
                    index,
                    GetString(state, "name") ?? $"State {index}",
                    ReadRuntimeIntList(state, "operators"),
                    bufferAccesses.Select(access => access.BufferIndex).ToArray(),
                    transformSetAccesses.Select(access => access.TransformSetIndex).ToArray(),
                    ReadRuntimeIntList(state, "usedSimCloths"),
                    bufferAccesses,
                    transformSetAccesses);
            })
            .ToArray();

        return new ClothRuntimeData(buffers, transformSets, operators, states);
    }

    private static IReadOnlyList<int> ReadRuntimeIntList(object source, string memberName) =>
        (GetList(GetValue(source, memberName)) ?? Array.Empty<object>())
            .Select(value => ToInt(value, -1))
            .Where(value => value >= 0)
            .ToArray();

    private static IReadOnlyList<ClothStateBufferAccessData> ReadRuntimeStateBufferAccesses(object state)
    {
        return (GetList(GetValue(state, "usedBuffers")) ?? Array.Empty<object>())
            .Select(access =>
            {
                var usage = GetValue(access, "bufferUsage");
                var bufferIndex = ToInt(GetValue(access, "bufferIndex"), -1);
                return new ClothStateBufferAccessData(
                    bufferIndex,
                    ToRuntimeByte(GetValue(usage, "perComponentFlags_0")),
                    ToRuntimeByte(GetValue(usage, "perComponentFlags_1")),
                    ToRuntimeByte(GetValue(usage, "perComponentFlags_2")),
                    ToRuntimeByte(GetValue(usage, "perComponentFlags_3")),
                    ToRuntimeBool(GetValue(usage, "trianglesRead")),
                    ToInt(GetValue(access, "shadowBufferIndex"), bufferIndex));
            })
            .Where(access => access.BufferIndex >= 0)
            .ToArray();
    }

    private static IReadOnlyList<ClothStateTransformSetAccessData> ReadRuntimeStateTransformSetAccesses(object state)
    {
        return (GetList(GetValue(state, "usedTransformSets")) ?? Array.Empty<object>())
            .Select(access =>
            {
                var usage = GetValue(access, "transformSetUsage");
                var trackers = (GetList(GetValue(usage, "perComponentTransformTrackers")) ?? Array.Empty<object>())
                    .Select(tracker => new ClothStateTransformTrackerData(
                        ReadRuntimeStateBitField(GetValue(tracker, "read")),
                        ReadRuntimeStateBitField(GetValue(tracker, "readBeforeWrite")),
                        ReadRuntimeStateBitField(GetValue(tracker, "written"))))
                    .ToArray();
                return new ClothStateTransformSetAccessData(
                    ToInt(GetValue(access, "transformSetIndex"), -1),
                    ToRuntimeByte(GetValue(usage, "perComponentFlags_0")),
                    ToRuntimeByte(GetValue(usage, "perComponentFlags_1")),
                    trackers);
            })
            .Where(access => access.TransformSetIndex >= 0)
            .ToArray();
    }

    private static ClothStateBitFieldData ReadRuntimeStateBitField(object? bitField)
    {
        var storage = GetValue(bitField, "storage");
        var words = (GetList(GetValue(storage, "words")) ?? Array.Empty<object>())
            .Select(value => ToRuntimeUInt32(value))
            .ToArray();
        return new ClothStateBitFieldData(words, ToRuntimeUInt32(GetValue(storage, "numBits")));
    }

    private static byte ToRuntimeByte(object? value)
    {
        var number = ToInt(value, 0);
        return checked((byte)Math.Clamp(number, byte.MinValue, byte.MaxValue));
    }

    private static uint ToRuntimeUInt32(object? value)
    {
        return value switch
        {
            uint unsignedValue => unsignedValue,
            int signedValue => unchecked((uint)signedValue),
            _ => unchecked((uint)ToInt(value, 0))
        };
    }
    private static int? ReadRuntimeOptionalInt(object source, params string[] memberNames)
    {
        foreach (var memberName in memberNames)
        {
            var value = GetValue(source, memberName);
            if (value is not null)
                return ToInt(value, 0);
        }
        return null;
    }

    private static bool ToRuntimeBool(object? value)
    {
        if (value is null)
            return false;
        return value is bool boolean
            ? boolean
            : ToInt(value, 0) != 0;
    }

    // Keeps the reverse-conversion preflight honest: a standalone writer may
    // only claim runtime support for operator classes it can serialize.
    public IReadOnlyList<string> GetOperatorClassNames(int clothIndex)
    {
        if (_bphhb != null)
            return Array.Empty<string>();
        if (_bphcl?.NativeDocument is { } bphcl)
            return bphcl.Cloths.ElementAtOrDefault(clothIndex)?.Operators
                .Select(@operator => @operator.ClassName)
                .ToArray()
                ?? Array.Empty<string>();

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        return cloth == null
            ? Array.Empty<string>()
            : (GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>())
                .Select(@operator => @operator.GetType().Name)
                .ToArray();
    }

    public void UpdateParticleRelationshipRows(int clothIndex, IEnumerable<ParticleRelationshipRow> rows)
    {
        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var edits = new List<NativeBphclConstraintEdit>();
            foreach (var row in rows.Where(row => row.IsEditable))
            {
                var values = new Dictionary<string, float>(StringComparer.Ordinal);
                AddNativeConstraintEditValue(values, "restLength", row.RestLength);
                AddNativeConstraintEditValue(values, "bendMinLength", row.BendMinLength);
                AddNativeConstraintEditValue(values, "stretchMaxLength", row.StretchMaxLength);
                AddNativeConstraintEditValue(values, "maximumDistance", row.MaximumDistance);
                AddNativeConstraintEditValue(values, "maxDistance", row.MaximumDistance);
                AddNativeConstraintEditValue(values, "shapeRadius", row.MaximumDistance);
                AddNativeConstraintEditValue(values, "stiffness", row.Stiffness);
                AddNativeConstraintEditValue(values, "bendStiffness", row.BendStiffness);
                AddNativeConstraintEditValue(values, "stretchStiffness", row.StretchStiffness);
                if (values.Count > 0)
                {
                    edits.Add(new NativeBphclConstraintEdit(
                        row.ConstraintSetIndex,
                        row.LinkIndex >= 0 ? row.LinkIndex : row.LocalConstraintIndex,
                        row.LocalConstraintIndex >= 0,
                        values));
                }
            }

            if (edits.Count > 0)
            {
                var updated = NativeBphclWriter.ApplyConstraintEdits(bphcl, clothIndex, 0, edits);
                _bphcl = CreateBphclSummary(updated, _bphcl.SourcePath);
            }
            return;
        }

        if (IsHelperBoneSidecar)
            throw new InvalidOperationException("Helper-bone files do not contain cloth constraints.");

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraintSetSource = GetValue(simData, "staticConstraintSets")
            ?? GetValue(cloth, "constraintSets");
        var constraintSets = GetMutableList(constraintSetSource, "constraint sets");

        foreach (var row in rows.Where(row => row.IsEditable))
        {
            if (row.ConstraintSetIndex < 0 || row.ConstraintSetIndex >= constraintSets.Count)
                continue;

            var constraintSet = constraintSets[row.ConstraintSetIndex];
            if (row.LinkIndex >= 0)
            {
                var links = GetList(GetValue(constraintSet, "links"));
                if (links == null || row.LinkIndex >= links.Count)
                    continue;

                var link = links[row.LinkIndex];
                ApplyConstraintFloat(link, "restLength", row.RestLength);
                ApplyConstraintFloat(link, "bendMinLength", row.BendMinLength);
                ApplyConstraintFloat(link, "stretchMaxLength", row.StretchMaxLength);
                ApplyConstraintFloat(link, "stiffness", row.Stiffness);
                ApplyConstraintFloat(link, "bendStiffness", row.BendStiffness);
                ApplyConstraintFloat(link, "stretchStiffness", row.StretchStiffness);
            }
            else if (row.LocalConstraintIndex >= 0)
            {
                var localConstraints = GetList(GetValue(constraintSet, "localConstraints"));
                if (localConstraints == null || row.LocalConstraintIndex >= localConstraints.Count)
                    continue;

                var local = localConstraints[row.LocalConstraintIndex];
                ApplyConstraintFloat(local, "maximumDistance", row.MaximumDistance);
                ApplyConstraintFloat(local, "maxDistance", row.MaximumDistance);
                ApplyConstraintFloat(local, "shapeRadius", row.MaximumDistance);
            }
        }
    }

    public void DeleteParticleRelationship(int clothIndex, ParticleRelationshipRow row)
    {
        if (_bphcl != null || IsHelperBoneSidecar)
            throw new InvalidOperationException("Constraint editing is currently available for HKCL files only.");
        if (!row.IsEditable)
            throw new InvalidOperationException("That relationship is structural information and cannot be removed here.");

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraintSetSource = GetValue(simData, "staticConstraintSets")
            ?? GetValue(cloth, "constraintSets");
        var constraintSets = GetMutableList(constraintSetSource, "constraint sets");
        if (row.ConstraintSetIndex < 0 || row.ConstraintSetIndex >= constraintSets.Count)
            throw new InvalidOperationException("The selected relationship no longer exists.");

        var constraintSet = constraintSets[row.ConstraintSetIndex];
        if (row.LinkIndex >= 0)
        {
            var links = GetMutableList(GetValue(constraintSet, "links"), "links");
            if (row.LinkIndex >= links.Count)
                throw new InvalidOperationException("The selected constraint link no longer exists.");
            links.RemoveAt(row.LinkIndex);
            return;
        }

        var localConstraints = GetMutableList(GetValue(constraintSet, "localConstraints"), "localConstraints");
        if (row.LocalConstraintIndex >= localConstraints.Count)
            throw new InvalidOperationException("The selected local constraint no longer exists.");
        localConstraints.RemoveAt(row.LocalConstraintIndex);
    }

    public IReadOnlyList<BoneEditRow> GetBoneRows(int clothIndex)
    {
        if (_bphhb != null)
            return FilterSidecarBoneRows(GetBphhbBoneRows(_bphhb), BphhbBridge.GetDriverGroups(_bphhb), clothIndex);

        if (_bphyssb != null)
            return FilterSidecarBoneRows(GetBphyssbBoneRows(_bphyssb), BphyssbBridge.GetDriverGroups(_bphyssb), clothIndex);

        if (IsHkrg)
            return clothIndex == 0 ? GetHkrgBoneRows() : Array.Empty<BoneEditRow>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var nativeSkeleton = bphcl.Skeletons.ElementAtOrDefault(clothIndex);
            return nativeSkeleton?.Bones.Select(bone => new BoneEditRow
            {
                Index = bone.Index,
                Name = bone.Name,
                ParentIndex = bone.ParentIndex,
                X = bone.Translation.X,
                Y = bone.Translation.Y,
                Z = bone.Translation.Z,
                RotationX = bone.Rotation.X,
                RotationY = bone.Rotation.Y,
                RotationZ = bone.Rotation.Z,
                RotationW = bone.Rotation.W,
                TranslationW = bone.Translation.W,
                ScaleX = bone.Scale.X,
                ScaleY = bone.Scale.Y,
                ScaleZ = bone.Scale.Z,
                ScaleW = bone.Scale.W
            }).ToArray() ?? Array.Empty<BoneEditRow>();
        }

        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex);
        if (skeleton == null)
            return Array.Empty<BoneEditRow>();

        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();
        var result = new List<BoneEditRow>();

        for (var i = 0; i < bones.Count; i++)
        {
            var pose = poses.ElementAtOrDefault(i) is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
            result.Add(new BoneEditRow
            {
                Index = i,
                Name = GetString(bones[i], "name") ?? $"Bone {i}",
                ParentIndex = ToInt(parents.ElementAtOrDefault(i), -1),
                X = pose.M11,
                Y = pose.M12,
                Z = pose.M13,
                TranslationW = pose.M14,
                RotationX = pose.M21,
                RotationY = pose.M22,
                RotationZ = pose.M23,
                RotationW = pose.M24,
                ScaleX = pose.M31,
                ScaleY = pose.M32,
                ScaleZ = pose.M33,
                ScaleW = pose.M34
            });
        }

        return result;
    }

    private static IReadOnlyList<BoneEditRow> FilterSidecarBoneRows(
        IReadOnlyList<BoneEditRow> rows,
        IReadOnlyList<SidecarDriverGroup> groups,
        int groupIndex)
    {
        var group = groups.ElementAtOrDefault(groupIndex);
        if (group == null)
            return Array.Empty<BoneEditRow>();
        var used = group.DriverBoneIndices.Concat(group.DrivenBoneIndices).ToHashSet();
        return rows.Where(row => used.Contains(row.Index)).ToArray();
    }
    private static IReadOnlyList<BoneEditRow> GetBphhbBoneRows(BphhbDocumentSummary helper)
    {
        var graph = helper.Graph;
        if (graph == null)
        {
            return helper.HelperBoneNames.Select((name, index) => new BoneEditRow
            {
                Index = index,
                Name = name,
                ParentIndex = -1
            }).ToArray();
        }

        var rows = graph.Bones.Select((name, index) => new BoneEditRow
        {
            Index = index,
            Name = name,
            ParentIndex = -1
        }).ToArray();

        foreach (var driver in graph.Drivers)
        {
            if (driver.BoneId < 0 || driver.BoneId >= rows.Length)
                continue;

            var row = rows[driver.BoneId];
            row.ParentIndex = driver.BaseBoneId;
            row.X = driver.Translation.X;
            row.Y = driver.Translation.Y;
            row.Z = driver.Translation.Z;
            row.RotationX = driver.Rotation.X;
            row.RotationY = driver.Rotation.Y;
            row.RotationZ = driver.Rotation.Z;
            row.RotationW = driver.Rotation.W;
        }

        // TotK stores a pose-driven record beside each driven-bone record.
        // The base transform is therefore the best available viewport anchor
        // until the complete actor skeleton is supplied alongside the helper.
        for (var index = 0; index < Math.Min(graph.Driven.Count, graph.PoseDriven.Count); index++)
        {
            var driven = graph.Driven[index];
            if (driven.BoneId < 0 || driven.BoneId >= rows.Length)
                continue;

            var pose = graph.PoseDriven[index];
            var row = rows[driven.BoneId];
            row.ParentIndex = pose.BaseBoneId;
            row.X = pose.Translation.X;
            row.Y = pose.Translation.Y;
            row.Z = pose.Translation.Z;
            row.RotationX = pose.Rotation.X;
            row.RotationY = pose.Rotation.Y;
            row.RotationZ = pose.Rotation.Z;
            row.RotationW = pose.Rotation.W;
        }

        return rows;
    }

    private static IReadOnlyList<BoneEditRow> GetBphyssbBoneRows(BphyssbDocumentSummary support)
    {
        var graph = support.Graph;
        if (graph == null)
        {
            return support.SupportBoneNames.Select((name, index) => new BoneEditRow
            {
                Index = index,
                Name = name,
                ParentIndex = -1
            }).ToArray();
        }

        var rows = graph.Bones.Select((name, index) => new BoneEditRow
        {
            Index = index,
            Name = name,
            ParentIndex = -1
        }).ToArray();

        foreach (var main in graph.MainBones)
            ApplyBphyssbPoseRow(rows, main);

        foreach (var supportBone in graph.SupportBones)
            ApplyBphyssbPoseRow(rows, supportBone);

        return rows;
    }

    private static void ApplyBphyssbPoseRow(BoneEditRow[] rows, BphyssbPoseBone pose)
    {
        if (pose.BoneId < 0 || pose.BoneId >= rows.Length)
            return;

        var row = rows[pose.BoneId];
        row.ParentIndex = pose.SpaceId;
        row.X = pose.Translation.X;
        row.Y = pose.Translation.Y;
        row.Z = pose.Translation.Z;
        row.RotationX = pose.Rotation.X;
        row.RotationY = pose.Rotation.Y;
        row.RotationZ = pose.Rotation.Z;
        row.RotationW = pose.Rotation.W;
    }

    public void UpdateBoneRows(int clothIndex, IEnumerable<BoneEditRow> rows)
    {
        if (_bphhb is { } helper)
        {
            foreach (var row in rows)
            {
                helper = BphhbBridge.UpdateBone(
                    helper,
                    row.Index,
                    row.Name,
                    row.ParentIndex,
                    new Vector3(row.X, row.Y, row.Z),
                    new Vector4(row.RotationX, row.RotationY, row.RotationZ, row.RotationW));
            }

            _bphhb = helper;
            return;
        }

        if (_bphyssb is { } support)
        {
            foreach (var row in rows)
            {
                support = BphyssbBridge.UpdateBone(
                    support,
                    row.Index,
                    row.Name,
                    row.ParentIndex,
                    new Vector3(row.X, row.Y, row.Z),
                    new Vector4(row.RotationX, row.RotationY, row.RotationZ, row.RotationW));
            }

            _bphyssb = support;
            return;
        }

        if (IsHkrg)
        {
            if (clothIndex == 0)
                UpdateHkrgBoneRows(rows);
            return;
        }

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var submittedRows = rows.ToList();
            var nativeSkeleton = bphcl.Skeletons.ElementAtOrDefault(clothIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
            var nameEdits = submittedRows
                .Where(row => row.Index >= 0 && row.Index < nativeSkeleton.Bones.Count)
                .Where(row => !string.Equals(row.Name, nativeSkeleton.Bones[row.Index].Name, StringComparison.Ordinal))
                .ToDictionary(row => row.Index, row => row.Name);
            if (nameEdits.Count > 0)
            {
                bphcl = NativeBphclWriter.ApplyBoneNameEdits(bphcl, clothIndex, nameEdits);
                nativeSkeleton = bphcl.Skeletons.ElementAtOrDefault(clothIndex)
                    ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
            }

            var edits = new Dictionary<int, NativeBphclBoneEdit>();
            foreach (var row in submittedRows)
            {
                var original = nativeSkeleton.Bones.ElementAtOrDefault(row.Index);
                if (original == null)
                    continue;
                var translation = new Vector4(row.X, row.Y, row.Z, original.Translation.W);
                var rotation = new Vector4(row.RotationX, row.RotationY, row.RotationZ, row.RotationW);
                var translationChanged = Vector4.DistanceSquared(translation, original.Translation) > 0.0000000001f;
                var rotationChanged = Vector4.DistanceSquared(rotation, original.Rotation) > 0.0000000001f;
                if (translationChanged || rotationChanged)
                    edits[row.Index] = new NativeBphclBoneEdit(translationChanged ? translation : null, rotationChanged ? rotation : null);
            }

            if (edits.Count > 0)
                bphcl = NativeBphclWriter.ApplyBoneEdits(bphcl, clothIndex, edits);

            if (nameEdits.Count > 0 || edits.Count > 0)
                _bphcl = CreateBphclSummary(bphcl, _bphcl.SourcePath);
            return;
        }

        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetValue(skeleton, "parentIndices") as IList;
        var poses = GetValue(skeleton, "referencePose") as IList;

        foreach (var row in rows)
        {
            if (row.Index < 0 || row.Index >= bones.Count)
                continue;

            SetValue(bones[row.Index], "name", row.Name);
            if (parents != null && row.Index < parents.Count)
                SetListItem(parents, row.Index, row.ParentIndex);

            if (poses != null && row.Index < poses.Count)
            {
                var existing = poses[row.Index] is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
                SetListItem(poses, row.Index, new Matrix4x4(
                    row.X, row.Y, row.Z, existing.M14,
                    row.RotationX, row.RotationY, row.RotationZ, row.RotationW,
                    row.ScaleX, row.ScaleY, row.ScaleZ, existing.M34,
                    existing.M41, existing.M42, existing.M43, existing.M44));
            }
        }
    }

    // Returns the complete outer HKCL collider table. Fresh BPHCL needs this
    // separately from each simulation's per-instance list: native files can
    // retain unused colliders in the root container.
    public IReadOnlyList<ColliderEditRow> GetAllColliderRows()
    {
        if (_bphhb != null || IsHkrg)
            return Array.Empty<ColliderEditRow>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            return bphcl.Colliders.Select(collider => new ColliderEditRow
            {
                Index = collider.Index,
                Name = collider.Name,
                ShapeType = GetColliderShapeLabel(collider.Shape.TypeName),
                BoneIndex = -1,
                BoneName = string.Empty,
                StartX = TransformNativeColliderPoint(collider, collider.Shape.Start).X,
                StartY = TransformNativeColliderPoint(collider, collider.Shape.Start).Y,
                StartZ = TransformNativeColliderPoint(collider, collider.Shape.Start).Z,
                EndX = TransformNativeColliderPoint(collider, collider.Shape.End).X,
                EndY = TransformNativeColliderPoint(collider, collider.Shape.End).Y,
                EndZ = TransformNativeColliderPoint(collider, collider.Shape.End).Z,
                Radius = collider.Shape.Radius,
                PlaneNormalX = collider.Shape.PlaneEquation.X,
                PlaneNormalY = collider.Shape.PlaneEquation.Y,
                PlaneNormalZ = collider.Shape.PlaneEquation.Z,
                Transform = GetNativeColliderTransform(collider)
            }).ToArray();
        }

        var root = RequireRoot();
        var collidables = GetCollidables(root);
        return collidables
            .Select((collidable, index) =>
            {
                var shape = GetValue(collidable, "shape");
                if (shape == null)
                    return null;

                var shapeType = shape.GetType().Name;
                var planeEquation = shapeType == "hclPlaneShape" && GetValue(shape, "planeEquation") is Vector4 plane
                    ? plane
                    : Vector4.Zero;
                var start = shapeType switch
                {
                    "hclSphereShape" => GetValue(GetValue(shape, "sphere"), "pos") is Vector4 sphere ? sphere : Vector4.Zero,
                    "hclTaperedCapsuleShape" => GetValue(shape, "small") is Vector4 small ? small : Vector4.Zero,
                    "hclPlaneShape" => new Vector4(GetPlanePoint(planeEquation), 0.0f),
                    _ => GetValue(shape, "start") is Vector4 startVector ? startVector : Vector4.Zero
                };
                var end = shapeType switch
                {
                    "hclSphereShape" => start,
                    "hclTaperedCapsuleShape" => GetValue(shape, "big") is Vector4 big ? big : start,
                    _ => GetValue(shape, "end") is Vector4 endVector ? endVector : start
                };
                var transform = GetValue(collidable, "transform") is Matrix4x4 matrix
                    ? matrix
                    : Matrix4x4.Identity;
                var normal = shapeType == "hclPlaneShape"
                    ? NormalizePlaneNormal(TransformHkclColliderDirection(transform, new Vector3(planeEquation.X, planeEquation.Y, planeEquation.Z)))
                    : Vector3.UnitY;
                return new ColliderEditRow
                {
                    Index = index,
                    ClothIndex = -1,
                    Name = GetString(collidable, "name") ?? $"Collider {index}",
                    ShapeType = GetColliderShapeLabel(shapeType),
                    BoneIndex = ToInt(GetValue(collidable, "transformIndex"), -1),
                    BoneName = string.Empty,
                    StartX = TransformHkclColliderPoint(transform, start).X,
                    StartY = TransformHkclColliderPoint(transform, start).Y,
                    StartZ = TransformHkclColliderPoint(transform, start).Z,
                    EndX = TransformHkclColliderPoint(transform, end).X,
                    EndY = TransformHkclColliderPoint(transform, end).Y,
                    EndZ = TransformHkclColliderPoint(transform, end).Z,
                    Radius = shapeType switch
                    {
                        "hclSphereShape" => start.W,
                        "hclTaperedCapsuleShape" => Convert.ToSingle(GetValue(shape, "smallRadius") ?? 0.0f, CultureInfo.InvariantCulture),
                        _ => Convert.ToSingle(GetValue(shape, "radius") ?? 0.0f, CultureInfo.InvariantCulture)
                    },
                    PlaneNormalX = normal.X,
                    PlaneNormalY = normal.Y,
                    PlaneNormalZ = normal.Z,
                    Transform = transform
                };
            })
            .OfType<ColliderEditRow>()
            .ToArray();
    }
    public IReadOnlyList<ColliderEditRow> GetColliderRows(int clothIndex)
    {
        if (_bphhb != null)
            return Array.Empty<ColliderEditRow>();

        if (IsHkrg)
            return clothIndex == 0 ? GetHkrgRigidBodyRows() : Array.Empty<ColliderEditRow>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var nativeBones = bphcl.Skeletons.ElementAtOrDefault(clothIndex)?.Bones ?? Array.Empty<NativeBphclBone>();
            var referencedColliderItems = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths
                .SelectMany(simulation => simulation.CollidableItemIndices)
                .ToHashSet() ?? new HashSet<int>();
            return bphcl.Colliders
                .Where(collider => referencedColliderItems.Contains(collider.ItemIndex))
                .Select(collider =>
            {
                var boneIndex = ResolveColliderBoneIndex(collider.Name, nativeBones.Select(bone => (bone.Index, bone.Name)), -1);
                var isPlane = string.Equals(collider.Shape.TypeName, "hclPlaneShape", StringComparison.Ordinal);
                var localStart = isPlane
                    ? new Vector4(GetPlanePoint(collider.Shape.PlaneEquation), 0.0f)
                    : collider.Shape.Start;
                var planeNormal = isPlane
                    ? NormalizePlaneNormal(TransformNativeColliderDirection(collider, new Vector3(
                        collider.Shape.PlaneEquation.X,
                        collider.Shape.PlaneEquation.Y,
                        collider.Shape.PlaneEquation.Z)))
                    : Vector3.UnitY;
                return new ColliderEditRow
                {
                    Index = collider.Index,
                    Name = collider.Name,
                    ShapeType = GetColliderShapeLabel(collider.Shape.TypeName),
                    BoneIndex = boneIndex,
                    BoneName = nativeBones.FirstOrDefault(bone => bone.Index == boneIndex)?.Name ?? string.Empty,
                    StartX = TransformNativeColliderPoint(collider, localStart).X,
                    StartY = TransformNativeColliderPoint(collider, localStart).Y,
                    StartZ = TransformNativeColliderPoint(collider, localStart).Z,
                    EndX = TransformNativeColliderPoint(collider, isPlane ? localStart : collider.Shape.End).X,
                    EndY = TransformNativeColliderPoint(collider, isPlane ? localStart : collider.Shape.End).Y,
                    EndZ = TransformNativeColliderPoint(collider, isPlane ? localStart : collider.Shape.End).Z,
                    Radius = collider.Shape.Radius,
                    PlaneNormalX = planeNormal.X,
                    PlaneNormalY = planeNormal.Y,
                    PlaneNormalZ = planeNormal.Z,
                    Transform = GetNativeColliderTransform(collider)
                };
            }).ToArray();
        }

        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex);
        var bones = skeleton == null
            ? Array.Empty<object>()
            : GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var referencedCollidables = EnumerateReferencedCollidables(GetClothDatas(root).ElementAtOrDefault(clothIndex)!)
            .Distinct(ReferenceEquality.Instance)
            .ToList();
        var collidables = GetCollidables(root);
        var result = new List<ColliderEditRow>();

        foreach (var collidable in referencedCollidables)
        {
            var i = IndexOfReference(collidables, collidable);
            if (i < 0)
                continue;
            var shape = GetValue(collidable, "shape");
            if (shape == null)
                continue;
            var shapeType = shape.GetType().Name;
            var planeEquation = shapeType == "hclPlaneShape" && GetValue(shape, "planeEquation") is Vector4 plane
                ? plane
                : Vector4.Zero;
            var start = shapeType switch
            {
                "hclSphereShape" => GetValue(GetValue(shape, "sphere"), "pos") is Vector4 sphere ? sphere : Vector4.Zero,
                "hclTaperedCapsuleShape" => GetValue(shape, "small") is Vector4 small ? small : Vector4.Zero,
                "hclPlaneShape" => new Vector4(GetPlanePoint(planeEquation), 0.0f),
                _ => GetValue(shape, "start") is Vector4 startVector ? startVector : Vector4.Zero
            };
            var end = shapeType switch
            {
                "hclSphereShape" => start,
                "hclTaperedCapsuleShape" => GetValue(shape, "big") is Vector4 big ? big : start,
                _ => GetValue(shape, "end") is Vector4 endVector ? endVector : start
            };
            var name = GetString(collidable, "name") ?? $"Collider {i}";
            var storedTransform = GetValue(collidable, "transform") is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
            var runtimeBinding = GetCollidableRuntimeBinding(root, clothIndex, collidable, storedTransform);
            var boneIndex = runtimeBinding.HasMapEntry
                ? runtimeBinding.BoneIndex
                : ToInt(GetValue(collidable, "transformIndex"), -1);
            // hclCollidable owns the authored shape orientation. The transform
            // map drives its attachment translation at runtime, but its basis
            // can intentionally differ from the collider's own basis.
            var transform = storedTransform;
            var worldPlaneNormal = shapeType == "hclPlaneShape"
                ? NormalizePlaneNormal(TransformHkclColliderDirection(transform, new Vector3(planeEquation.X, planeEquation.Y, planeEquation.Z)))
                : Vector3.UnitY;
            result.Add(new ColliderEditRow
            {
                Index = i,
                ClothIndex = clothIndex,
                Name = name,
                ShapeType = GetColliderShapeLabel(shapeType),
                BoneIndex = boneIndex,
                BoneName = boneIndex >= 0 && boneIndex < bones.Count ? GetString(bones[boneIndex], "name") ?? string.Empty : string.Empty,
                StartX = TransformHkclColliderPoint(transform, start).X,
                StartY = TransformHkclColliderPoint(transform, start).Y,
                StartZ = TransformHkclColliderPoint(transform, start).Z,
                EndX = TransformHkclColliderPoint(transform, end).X,
                EndY = TransformHkclColliderPoint(transform, end).Y,
                EndZ = TransformHkclColliderPoint(transform, end).Z,
                Radius = shapeType switch
                {
                    "hclSphereShape" => start.W,
                    "hclTaperedCapsuleShape" => Convert.ToSingle(GetValue(shape, "smallRadius") ?? 0.0f, CultureInfo.InvariantCulture),
                    _ => Convert.ToSingle(GetValue(shape, "radius") ?? 0.0f, CultureInfo.InvariantCulture)
                },
                PlaneNormalX = worldPlaneNormal.X,
                PlaneNormalY = worldPlaneNormal.Y,
                PlaneNormalZ = worldPlaneNormal.Z,
                Transform = transform
            });
        }

        return result;
    }

    public int CreateSupportFromBone(int sourceBoneIndex, string targetBoneName)
    {
        if (string.IsNullOrWhiteSpace(targetBoneName))
            throw new ArgumentException("A target bone name is required.", nameof(targetBoneName));

        if (_bphhb != null)
        {
            (_bphhb, var targetBoneIndex) = BphhbBridge.CreateSupportFromBone(
                _bphhb,
                sourceBoneIndex,
                targetBoneName.Trim());
            return targetBoneIndex;
        }

        if (_bphyssb != null)
        {
            (_bphyssb, var targetBoneIndex) = BphyssbBridge.CreateSupportFromBone(
                _bphyssb,
                sourceBoneIndex,
                targetBoneName.Trim());
            return targetBoneIndex;
        }

        throw new InvalidOperationException("A helper/support-bone file is not open.");
    }
    public void DuplicateHelperBone(int boneIndex)
    {
        if (_bphhb != null)
        {
            _bphhb = BphhbBridge.DuplicateBone(_bphhb, boneIndex);
            return;
        }

        if (_bphyssb != null)
        {
            _bphyssb = BphyssbBridge.DuplicateBone(_bphyssb, boneIndex);
            return;
        }

        throw new InvalidOperationException("A helper/support-bone file is not open.");
    }

    public void AddHelperBone(int preferredSourceIndex)
    {
        if (_bphhb != null)
        {
            _bphhb = BphhbBridge.AddBone(_bphhb, preferredSourceIndex);
            return;
        }

        if (_bphyssb != null)
        {
            _bphyssb = BphyssbBridge.AddBone(_bphyssb, preferredSourceIndex);
            return;
        }

        throw new InvalidOperationException("A helper/support-bone file is not open.");
    }

    public void MirrorHelperGroupAcrossX(int groupIndex)
    {
        if (_bphhb != null)
        {
            _bphhb = BphhbBridge.MirrorDriverGroupAcrossX(_bphhb, groupIndex);
            return;
        }

        if (_bphyssb != null)
        {
            _bphyssb = BphyssbBridge.MirrorDriverGroupAcrossX(_bphyssb, groupIndex);
            return;
        }

        throw new InvalidOperationException("A helper/support-bone file is not open.");
    }
    public void MirrorHelperBoneAcrossX(int boneIndex)
    {
        if (_bphhb != null)
        {
            _bphhb = BphhbBridge.MirrorBoneAcrossX(_bphhb, boneIndex);
            return;
        }

        if (_bphyssb != null)
        {
            _bphyssb = BphyssbBridge.MirrorBoneAcrossX(_bphyssb, boneIndex);
            return;
        }

        throw new InvalidOperationException("A helper/support-bone file is not open.");
    }

    public void MoveHelperBone(int sourceBoneIndex, int destinationBoneIndex)
    {
        if (_bphhb != null)
        {
            _bphhb = BphhbBridge.MoveBone(_bphhb, sourceBoneIndex, destinationBoneIndex);
            return;
        }

        if (_bphyssb != null)
        {
            _bphyssb = BphyssbBridge.MoveBone(_bphyssb, sourceBoneIndex, destinationBoneIndex);
            return;
        }

        throw new InvalidOperationException("A helper/support-bone file is not open.");
    }

    public IReadOnlyList<string> GetColliderBindingDiagnostics(int clothIndex)
    {
        if (_root == null || _bphcl != null || IsHelperBoneSidecar)
            return Array.Empty<string>();

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex);
        if (cloth == null || skeleton == null)
            return Array.Empty<string>();

        var allCollidables = GetCollidables(root);
        var lines = new List<string>();
        foreach (var simulation in GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
        {
            var references = GetList(GetValue(simulation, "perInstanceCollidables")) ?? Array.Empty<object>();
            var map = GetValue(simulation, "collidableTransformMap");
            var indices = map == null ? null : GetList(GetValue(map, "transformIndices"));
            var offsets = map == null ? null : GetList(GetValue(map, "offsets"));

            for (var slot = 0; slot < references.Count; slot++)
            {
                var collidable = references[slot];
                var globalIndex = IndexOfReference(allCollidables, collidable);
                var name = GetString(collidable, "name") ?? $"Collider {globalIndex}";
                var stored = GetValue(collidable, "transform") is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
                var boneIndex = indices != null && slot < indices.Count ? ToInt(indices[slot], -1) : -1;
                var offset = offsets != null && slot < offsets.Count && offsets[slot] is Matrix4x4 value
                    ? value
                    : Matrix4x4.Identity;
                var mapped = boneIndex >= 0
                    ? offset * GetHkclBoneWorldTransform(skeleton, boneIndex)
                    : stored;
                var recomputedOffset = boneIndex >= 0 && Matrix4x4.Invert(GetHkclBoneWorldTransform(skeleton, boneIndex), out var inverseBone)
                    ? stored * inverseBone
                    : offset;
                var difference = Vector3.Distance(
                    new Vector3(stored.M41, stored.M42, stored.M43),
                    new Vector3(mapped.M41, mapped.M42, mapped.M43));
                var offsetDifference = MaxMatrixDifference(offset, recomputedOffset);
                var shape = GetValue(collidable, "shape");
                var start = GetValue(shape, "start") is Vector4 startValue ? startValue : Vector4.Zero;
                var end = GetValue(shape, "end") is Vector4 endValue ? endValue : start;
                var direction = GetValue(shape, "dir") is Vector4 directionValue ? directionValue : Vector4.Zero;
                var lengthSquared = Vector3.DistanceSquared(
                    new Vector3(start.X, start.Y, start.Z),
                    new Vector3(end.X, end.Y, end.Z));
                var expectedDirection = lengthSquared > 0.000001f
                    ? Vector3.Normalize(new Vector3(end.X - start.X, end.Y - start.Y, end.Z - start.Z))
                    : Vector3.Zero;
                var expectedInverseLength = lengthSquared > 0.000001f ? 1.0f / lengthSquared : 0.0f;
                var storedInverseLength = Convert.ToSingle(GetValue(shape, "capLenSqrdInv") ?? 0.0f, CultureInfo.InvariantCulture);
                lines.Add(
                    $"slot={slot}|global={globalIndex}|{name}|bone={boneIndex}|" +
                    $"stored={stored.M41:G7},{stored.M42:G7},{stored.M43:G7}|" +
                    $"offset={offset.M41:G7},{offset.M42:G7},{offset.M43:G7}|" +
                    $"mapped={mapped.M41:G7},{mapped.M42:G7},{mapped.M43:G7}|" +
                    $"translationDelta={difference:G7}|offsetMatrixDelta={offsetDifference:G7}|" +
                    $"localLengthSq={lengthSquared:G7}|capInv={storedInverseLength:G7}/{expectedInverseLength:G7}|" +
                    $"dirDelta={Vector3.Distance(new Vector3(direction.X, direction.Y, direction.Z), expectedDirection):G7}");
            }
        }

        return lines;
    }

    public void UpdateColliderRows(IEnumerable<ColliderEditRow> rows)
    {
        if (IsHkrg)
        {
            UpdateHkrgRigidBodyRows(rows);
            return;
        }

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var submittedRows = rows.ToList();
            var nameEdits = submittedRows
                .Where(row => row.Index >= 0 && row.Index < bphcl.Colliders.Count)
                .Where(row => !string.Equals(row.Name, bphcl.Colliders[row.Index].Name, StringComparison.Ordinal))
                .ToDictionary(row => row.Index, row => row.Name);
            if (nameEdits.Count > 0)
                bphcl = NativeBphclWriter.ApplyColliderNameEdits(bphcl, nameEdits);

            var edits = new Dictionary<int, NativeBphclColliderEdit>();
            foreach (var row in submittedRows)
            {
                var collider = bphcl.Colliders.ElementAtOrDefault(row.Index);
                if (collider == null)
                    continue;

                var transform = row.Transform;
                var axisX = new Vector4(transform.M11, transform.M12, transform.M13, collider.AxisX.W);
                var axisY = new Vector4(transform.M21, transform.M22, transform.M23, collider.AxisY.W);
                var axisZ = new Vector4(transform.M31, transform.M32, transform.M33, collider.AxisZ.W);
                var translation = new Vector4(transform.M41, transform.M42, transform.M43, collider.Translation.W);
                var start = InverseTransformNativeColliderPoint(transform, new Vector3(row.StartX, row.StartY, row.StartZ));
                var end = InverseTransformNativeColliderPoint(transform, new Vector3(row.EndX, row.EndY, row.EndZ));
                edits[row.Index] = new NativeBphclColliderEdit(
                    axisX,
                    axisY,
                    axisZ,
                    translation,
                    new Vector4(start, collider.Shape.Start.W),
                    new Vector4(end, collider.Shape.End.W),
                    row.Radius);
            }

            if (edits.Count > 0)
                bphcl = NativeBphclWriter.ApplyColliderEdits(bphcl, edits);

            if (nameEdits.Count > 0 || edits.Count > 0)
                _bphcl = CreateBphclSummary(bphcl, _bphcl.SourcePath);
            return;
        }

        var root = RequireRoot();
        var collidables = GetCollidables(root);
        foreach (var row in rows)
        {
            if (row.Index < 0 || row.Index >= collidables.Count)
                continue;

            var collidable = collidables[row.Index];
            var previousBoneIndex = ToInt(GetValue(collidable, "transformIndex"), -1);
            if (row.BoneIndex >= 0 && row.BoneIndex != previousBoneIndex && !string.IsNullOrWhiteSpace(row.BoneName))
                row.Name = GetUniqueColliderName(collidables, row.Index, row.BoneName);

            SetValue(collidable, "name", row.Name);
            SetValue(collidable, "transformIndex", row.BoneIndex);
            SetValue(collidable, "transform", row.Transform);
            if (row.BoneIndex >= 0)
                SynchronizeCollidableTransformMaps(root, row, collidable);
            else
                ClearCollidableTransformMapBinding(root, row, collidable);

            var shape = GetValue(collidable, "shape");
            if (shape == null)
                continue;

            var start = InverseTransformHkclColliderPoint(row.Transform, new Vector3(row.StartX, row.StartY, row.StartZ));
            var end = InverseTransformHkclColliderPoint(row.Transform, new Vector3(row.EndX, row.EndY, row.EndZ));
            switch (shape.GetType().Name)
            {
                case "hclSphereShape":
                {
                    var sphere = GetValue(shape, "sphere");
                    SetValue(sphere, "pos", new Vector4(start.X, start.Y, start.Z, row.Radius));
                    break;
                }
                case "hclTaperedCapsuleShape":
                    SetValue(shape, "small", start);
                    SetValue(shape, "big", end);
                    SetValue(shape, "smallRadius", row.Radius);
                    UpdateCapsuleDerivedValues(shape, start, end);
                    break;
                case "hclPlaneShape":
                    break;
                default:
                    SetValue(shape, "start", start);
                    SetValue(shape, "end", end);
                    SetValue(shape, "radius", row.Radius);
                    UpdateCapsuleDerivedValues(shape, start, end);
                    break;
            }
        }
    }

    private static string GetUniqueColliderName(IReadOnlyList<object> collidables, int currentIndex, string boneName)
    {
        var baseName = $"Collidable_{boneName}";
        var existingNames = collidables
            .Where((_, index) => index != currentIndex)
            .Select(collidable => GetString(collidable, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(baseName))
            return baseName;

        for (var suffix = 1; ; suffix++)
        {
            var candidate = $"{baseName}_{suffix}";
            if (!existingNames.Contains(candidate))
                return candidate;
        }
    }

    private static string GetColliderShapeLabel(string shapeType) => shapeType switch
    {
        "hclSphereShape" => "Sphere",
        "hclPlaneShape" => "Plane",
        "hclTaperedCapsuleShape" => "Tapered capsule",
        _ => "Capsule"
    };

    private static Vector3 GetPlanePoint(Vector4 equation)
    {
        var normal = new Vector3(equation.X, equation.Y, equation.Z);
        var lengthSquared = normal.LengthSquared();
        return lengthSquared < 0.000001f ? Vector3.Zero : normal * (-equation.W / lengthSquared);
    }

    private static Vector3 NormalizePlaneNormal(Vector3 normal) =>
        normal.LengthSquared() < 0.000001f ? Vector3.UnitY : Vector3.Normalize(normal);

    public ParticlePreviewData GetParticlePreview(int clothIndex)
    {
        var preview = new ParticlePreviewData();
        if (IsHelperBoneSidecar)
        {
            var helperBones = GetBoneRows(Math.Max(0, clothIndex));
            foreach (var bone in helperBones)
            {
                preview.Bones.Add(new BonePreviewPoint
                {
                    Index = bone.Index,
                    ParentIndex = bone.ParentIndex,
                    Name = bone.Name,
                    Position = new Vector3(bone.X, bone.Y, bone.Z),
                    AxisX = Vector3.UnitX,
                    AxisY = Vector3.UnitY,
                    AxisZ = Vector3.UnitZ
                });
            }

            var rootBone = preview.Bones.FirstOrDefault(bone => bone.ParentIndex < 0)
                ?? preview.Bones.FirstOrDefault();
            if (rootBone != null)
            {
                preview.ViewRoot = rootBone.Position;
                preview.HasViewRoot = true;
            }

            return preview;
        }

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var nativeCloth = bphcl.Cloths.ElementAtOrDefault(clothIndex);
            var simulation = nativeCloth?.SimCloths.FirstOrDefault();
            if (simulation is null)
                return preview;

            foreach (var particle in simulation.Particles)
            {
                preview.Particles.Add(new ParticlePreviewPoint
                {
                    Index = particle.Index,
                    Fixed = particle.Fixed,
                    Position = new Vector3(particle.Position.X, particle.Position.Y, particle.Position.Z),
                    Radius = particle.Radius,
                    Mass = particle.Mass,
                    InverseMass = particle.InverseMass,
                    Friction = particle.Friction,
                    CollisionMask = simulation.StaticCollisionMasks.Count > particle.Index
                        ? simulation.StaticCollisionMasks[particle.Index]
                        : GetBphclCollisionMask(
                            simulation,
                            particle,
                            particle.Index,
                            preserveSourceLandscapeCollisionBit: true)
                });
            }

            preview.Gravity = new Vector3(
                simulation.SimulationInfo.Gravity.X,
                simulation.SimulationInfo.Gravity.Y,
                simulation.SimulationInfo.Gravity.Z);
            preview.DampingPerSecond = simulation.SimulationInfo.GlobalDampingPerSecond;
            preview.CollisionTolerance = simulation.Runtime.LandscapeCollisionData.CollisionTolerance;
            var nativeTransferMotion = simulation.Runtime.TransferMotionData;
            preview.TransferMotionEnabled = simulation.Runtime.TransferMotionEnabled;
            preview.TransferMotionTransformSetIndex = nativeTransferMotion.TransformSetIndex;
            preview.TransferMotionTransformIndex = nativeTransferMotion.TransformIndex;
            preview.TransferTranslationMotion = nativeTransferMotion.TransferTranslationMotion;
            preview.MinTranslationSpeed = nativeTransferMotion.MinTranslationSpeed;
            preview.MaxTranslationSpeed = nativeTransferMotion.MaxTranslationSpeed;
            preview.MinTranslationBlend = nativeTransferMotion.MinTranslationBlend;
            preview.MaxTranslationBlend = nativeTransferMotion.MaxTranslationBlend;
            preview.TransferRotationMotion = nativeTransferMotion.TransferRotationMotion;
            preview.MinRotationSpeed = nativeTransferMotion.MinRotationSpeed;
            preview.MaxRotationSpeed = nativeTransferMotion.MaxRotationSpeed;
            preview.MinRotationBlend = nativeTransferMotion.MinRotationBlend;
            preview.MaxRotationBlend = nativeTransferMotion.MaxRotationBlend;

            for (var triangleIndex = 0; triangleIndex + 2 < simulation.TriangleIndices.Count; triangleIndex += 3)
            {
                preview.Triangles.Add(new ParticlePreviewTriangle
                {
                    ParticleA = simulation.TriangleIndices[triangleIndex],
                    ParticleB = simulation.TriangleIndices[triangleIndex + 1],
                    ParticleC = simulation.TriangleIndices[triangleIndex + 2]
                });
            }

            foreach (var constraintSet in simulation.ConstraintSets)
            {
                preview.ConstraintSets.Add(new ParticlePreviewConstraintSet
                {
                    Index = constraintSet.Index,
                    Kind = constraintSet.ClassName,
                    Name = constraintSet.Name
                });
                foreach (var link in constraintSet.Links.Where(link => link.ParticleA.HasValue && link.ParticleB.HasValue))
                {
                    if (constraintSet.ClassName.Contains("BendStiffness", StringComparison.Ordinal))
                    {
                        var particleC = GetNativeConstraintValue(link, "particleC");
                        var particleD = GetNativeConstraintValue(link, "particleD");
                        if (particleC.HasValue && particleD.HasValue)
                        {
                            preview.BendStiffnessConstraints.Add(new ParticlePreviewBendStiffness
                            {
                                ConstraintSetIndex = constraintSet.Index,
                                ParticleA = link.ParticleA!.Value,
                                ParticleB = link.ParticleB!.Value,
                                ParticleC = checked((int)particleC.Value),
                                ParticleD = checked((int)particleD.Value),
                                WeightA = GetNativeConstraintValue(link, "weightA") ?? 0.0f,
                                WeightB = GetNativeConstraintValue(link, "weightB") ?? 0.0f,
                                WeightC = GetNativeConstraintValue(link, "weightC") ?? 0.0f,
                                WeightD = GetNativeConstraintValue(link, "weightD") ?? 0.0f,
                                BendStiffness = GetNativeConstraintValue(link, "bendStiffness") ?? 0.0f,
                                RestCurvature = GetNativeConstraintValue(link, "restCurvature") ?? 0.0f
                            });
                        }
                        continue;
                    }

                    preview.Links.Add(new ParticlePreviewLink
                    {
                        ConstraintSetIndex = constraintSet.Index,
                        ParticleA = link.ParticleA!.Value,
                        ParticleB = link.ParticleB!.Value,
                        Kind = constraintSet.ClassName,
                        RestLength = GetNativeConstraintValue(link, "restLength"),
                        BendMinLength = GetNativeConstraintValue(link, "bendMinLength"),
                        StretchMaxLength = GetNativeConstraintValue(link, "stretchMaxLength"),
                        Stiffness = GetNativeConstraintValue(link, "stiffness"),
                        BendStiffness = GetNativeConstraintValue(link, "bendStiffness"),
                        StretchStiffness = GetNativeConstraintValue(link, "stretchStiffness")
                    });
                }

                foreach (var local in constraintSet.LocalConstraints.Concat(constraintSet.LocalStiffnessConstraints))
                {
                    var particleIndex = GetNativeConstraintValue(local, "particleIndex") ?? local.ParticleA;
                    var maximumDistance = GetNativeConstraintValue(local, "maximumDistance")
                        ?? GetNativeConstraintValue(local, "maxDistance")
                        ?? GetNativeConstraintValue(local, "shapeRadius");
                    if (!particleIndex.HasValue || !maximumDistance.HasValue || maximumDistance.Value < 0.0f)
                        continue;

                    preview.LocalRanges.Add(new ParticlePreviewLocalRange
                    {
                        ConstraintSetIndex = constraintSet.Index,
                        ParticleIndex = checked((int)particleIndex.Value),
                        ReferenceVertex = checked((int)(GetNativeConstraintValue(local, "referenceVertex") ?? -1.0f)),
                        ReferenceBufferIndex = checked((int)(GetNativeConstraintValue(constraintSet.Values, "referenceMeshBufferIdx") ?? 0.0f)),
                        MaximumDistance = maximumDistance.Value,
                        MinNormalDistance = GetNativeConstraintValue(local, "minNormalDistance") ?? float.NegativeInfinity,
                        MaxNormalDistance = GetNativeConstraintValue(local, "maxNormalDistance") ?? float.PositiveInfinity,
                        Stiffness = GetNativeConstraintValue(local, "stiffness")
                            ?? GetNativeConstraintValue(constraintSet.Values, "stiffness")
                            ?? 1.0f,
                        ApplyNormalComponent = (GetNativeConstraintValue(constraintSet.Values, "applyNormalComponent") ?? 0.0f) != 0.0f
                    });
                }
            }

            AddNativeBphclPreviewSkeleton(preview, bphcl.Skeletons.ElementAtOrDefault(clothIndex));
            AddPreviewParticleAttachments(preview, clothIndex);
            AddNativeBphclPreviewBoneBindings(preview, nativeCloth);
            AddNativeBphclPreviewColliders(preview, bphcl, clothIndex);
            AddPreviewSimulationConfig(preview, clothIndex);
            return preview;
        }

        var root = RequireRoot();
        if (IsHkrg)
        {
            if (clothIndex == 0)
                AddHkrgPreview(preview);
            return preview;
        }

        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return preview;

        foreach (var row in GetParticleRows(clothIndex))
        {
            preview.Particles.Add(new ParticlePreviewPoint
            {
                Index = row.Index,
                Fixed = row.Fixed,
                Position = new Vector3(row.X, row.Y, row.Z),
                Radius = row.Radius,
                Mass = row.Mass,
                InverseMass = row.InverseMass,
                Friction = row.Friction,
                CollisionMask = row.CollisionMask
            });
        }

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var simulationInfo = GetValue(simData, "simulationInfo") ?? simData;
        if (GetValue(simulationInfo, "gravity") is Vector4 gravity)
            preview.Gravity = new Vector3(gravity.X, gravity.Y, gravity.Z);
        preview.DampingPerSecond = Convert.ToSingle(GetValue(simulationInfo, "globalDampingPerSecond") ?? 0.0f, CultureInfo.InvariantCulture);
        preview.CollisionTolerance = Convert.ToSingle(GetValue(simulationInfo, "collisionTolerance") ?? 0.0f, CultureInfo.InvariantCulture);
        var hkclTransferMotion = GetValue(simData, "transferMotionData");
        preview.TransferMotionEnabled = ToBool(GetValue(simData, "transferMotionEnabled"));
        preview.TransferMotionTransformSetIndex = ToInt(GetValue(hkclTransferMotion, "transformSetIndex"), 0);
        preview.TransferMotionTransformIndex = ToInt(GetValue(hkclTransferMotion, "transformIndex"), 0);
        preview.TransferTranslationMotion = ToBool(GetValue(hkclTransferMotion, "transferTranslationMotion"));
        preview.MinTranslationSpeed = Convert.ToSingle(GetValue(hkclTransferMotion, "minTranslationSpeed") ?? 0.0f, CultureInfo.InvariantCulture);
        preview.MaxTranslationSpeed = Convert.ToSingle(GetValue(hkclTransferMotion, "maxTranslationSpeed") ?? 0.0f, CultureInfo.InvariantCulture);
        preview.MinTranslationBlend = Convert.ToSingle(GetValue(hkclTransferMotion, "minTranslationBlend") ?? 0.0f, CultureInfo.InvariantCulture);
        preview.MaxTranslationBlend = Convert.ToSingle(GetValue(hkclTransferMotion, "maxTranslationBlend") ?? 0.0f, CultureInfo.InvariantCulture);
        preview.TransferRotationMotion = ToBool(GetValue(hkclTransferMotion, "transferRotationMotion"));
        preview.MinRotationSpeed = Convert.ToSingle(GetValue(hkclTransferMotion, "minRotationSpeed") ?? 0.0f, CultureInfo.InvariantCulture);
        preview.MaxRotationSpeed = Convert.ToSingle(GetValue(hkclTransferMotion, "maxRotationSpeed") ?? 0.0f, CultureInfo.InvariantCulture);
        preview.MinRotationBlend = Convert.ToSingle(GetValue(hkclTransferMotion, "minRotationBlend") ?? 0.0f, CultureInfo.InvariantCulture);
        preview.MaxRotationBlend = Convert.ToSingle(GetValue(hkclTransferMotion, "maxRotationBlend") ?? 0.0f, CultureInfo.InvariantCulture);
        AddPreviewTriangles(preview, simData);
        AddPreviewLinks(preview, simData, cloth);
        AddPreviewSkeleton(preview, root, clothIndex);
        AddPreviewParticleAttachments(preview, clothIndex);
        AddPreviewBoneBindings(preview, cloth);
        AddPreviewColliders(preview, root, cloth);
        AddPreviewSimulationConfig(preview, clothIndex);
        return preview;
    }

    public void UpdateParticleRows(int clothIndex, IEnumerable<ParticleEditRow> rows)
    {
        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var simulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault()
                ?? throw new InvalidOperationException("The selected BPHCL cloth has no editable simulation particle data.");
            var edits = new Dictionary<int, NativeBphclParticleEdit>();
            foreach (var row in rows)
            {
                var original = simulation.Particles.ElementAtOrDefault(row.Index);
                if (original is null)
                    continue;

                var position = new Vector4(row.X, row.Y, row.Z, row.W);
                var positionChanged = Vector4.DistanceSquared(position, original.Position) > 0.0000000001f;
                var massChanged = !NearlyEqual(row.Mass, original.Mass);
                var radiusChanged = !NearlyEqual(row.Radius, original.Radius);
                var frictionChanged = !NearlyEqual(row.Friction, original.Friction);
                if (!positionChanged && !massChanged && !radiusChanged && !frictionChanged)
                    continue;

                edits[row.Index] = new NativeBphclParticleEdit(
                    positionChanged ? position : null,
                    massChanged ? row.Mass : null,
                    radiusChanged ? row.Radius : null,
                    frictionChanged ? row.Friction : null);
            }

            if (edits.Count > 0)
            {
                var updated = NativeBphclWriter.ApplyParticleEdits(bphcl, clothIndex, 0, edits);
                _bphcl = CreateBphclSummary(updated, _bphcl.SourcePath);
            }
            return;
        }

        if (IsHelperBoneSidecar)
            throw new InvalidOperationException("Helper-bone files do not contain cloth particles.");

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        ApplyParticleRows(cloth, rows);
    }

    /// <summary>
    /// Keeps virtual-surface normals consistent after a global reflection.
    /// Particle-link constraints are distance based and deliberately remain
    /// untouched; only triangle winding changes under an X mirror.
    /// </summary>
    public void FlipTriangleWinding(int clothIndex)
    {
        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var updated = NativeBphclWriter.FlipTriangleWinding(bphcl, clothIndex);
            _bphcl = CreateBphclSummary(updated, _bphcl.SourcePath);
            return;
        }

        if (IsHelperBoneSidecar)
            throw new InvalidOperationException("Helper-bone files do not contain virtual cloth triangles.");

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simulations = GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>();
        foreach (var simulation in simulations)
        {
            if (GetValue(simulation, "triangleIndices") is not IList triangles)
                continue;

            for (var offset = 0; offset + 2 < triangles.Count; offset += 3)
            {
                var b = triangles[offset + 1];
                triangles[offset + 1] = triangles[offset + 2];
                triangles[offset + 2] = b;
            }
        }
    }

    /// <summary>
    /// Mirrors the BPHCL matrix payloads that initialize and write back a
    /// cloth mesh. This complements particle/skeleton mirroring; it is not
    /// relevant to HKCL's managed object graph.
    /// </summary>
    public NativeBphclDocument? CaptureBphclNativeDocument() => _bphcl?.NativeDocument;

    public void MirrorBphclClothGeometryAcrossX(int clothIndex, NativeBphclDocument? sourceDocument = null)
    {
        if (_bphcl?.NativeDocument is not { } bphcl)
            return;

        var updated = NativeBphclWriter.MirrorClothGeometryAcrossX(bphcl, clothIndex, sourceDocument);
        _bphcl = CreateBphclSummary(updated, _bphcl.SourcePath);
    }

    private static bool NearlyEqual(float left, float right) => MathF.Abs(left - right) <= 0.000001f;

    private static void AddNativeConstraintEditValue(Dictionary<string, float> values, string name, float? value)
    {
        if (value is { } finiteValue && float.IsFinite(finiteValue))
            values[name] = finiteValue;
    }

    private static float? GetNativeConstraintValue(NativeBphclConstraintLink link, string name)
    {
        return GetNativeConstraintValue(link.Values, name);
    }

    private static float? GetNativeConstraintValue(IReadOnlyDictionary<string, double> values, string name)
    {
        var value = values.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrEmpty(value.Key) && double.IsFinite(value.Value) ? (float)value.Value : null;
    }

    private static BphclDocumentSummary CreateBphclSummary(NativeBphclDocument document, string sourcePath) =>
        BphclBridge.CreateSummary(document, sourcePath);

    // Particle locations are the simulation's rest pose. When an author moves a
    // particle, link constraints must describe that new pose or the cloth will
    // immediately try to pull itself back to its old shape.
    public void SynchronizeParticleConstraintGeometry(
        int clothIndex,
        IEnumerable<ParticleEditRow> previousRows,
        IEnumerable<ParticleEditRow> currentRows)
    {
        if (IsReadOnlyExternal)
            return;

        var previous = previousRows.ToDictionary(row => row.Index, ToPosition);
        var current = currentRows.ToDictionary(row => row.Index, ToPosition);
        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraintSetSource = GetValue(simData, "staticConstraintSets")
            ?? GetValue(cloth, "constraintSets");
        var constraintSets = GetList(constraintSetSource) ?? Array.Empty<object>();

        foreach (var constraintSet in constraintSets)
        {
            var links = GetList(GetValue(constraintSet, "links"));
            if (links == null)
                continue;

            foreach (var link in links)
            {
                var particleA = ToInt(GetValue(link, "particleA"), -1);
                var particleB = ToInt(GetValue(link, "particleB"), -1);
                if (!previous.TryGetValue(particleA, out var oldA) ||
                    !previous.TryGetValue(particleB, out var oldB) ||
                    !current.TryGetValue(particleA, out var newA) ||
                    !current.TryGetValue(particleB, out var newB))
                {
                    continue;
                }

                var oldLength = Vector3.Distance(oldA, oldB);
                var newLength = Vector3.Distance(newA, newB);
                if (oldLength <= 0.000001f || newLength <= 0.000001f)
                    continue;

                var ratio = newLength / oldLength;
                ApplyConstraintFloat(link, "restLength", newLength);
                ScaleConstraintFloat(link, "bendMinLength", ratio);
                ScaleConstraintFloat(link, "stretchMaxLength", ratio);
            }
        }

        // The simulation pose is only half of a cloth's rest state. Files that
        // use MoveParticles / Transition also regenerate source vertices from
        // skinning every frame. Rebase the matching source vertices so a
        // deliberate particle edit is not pulled back to its old position.
        RebaseParticleAnimationReferences(clothIndex, cloth, previous, current);
    }

    private void RebaseParticleAnimationReferences(
        int clothIndex,
        object cloth,
        IReadOnlyDictionary<int, Vector3> previous,
        IReadOnlyDictionary<int, Vector3> current)
    {
        var particleDeltas = current
            .Where(pair => previous.TryGetValue(pair.Key, out var before) &&
                           Vector3.DistanceSquared(before, pair.Value) > 0.0000000001f)
            .ToDictionary(pair => pair.Key, pair => pair.Value - previous[pair.Key]);
        if (particleDeltas.Count == 0)
            return;

        // Several source records may point at the same reference vertex. They
        // normally carry the same particle delta; average defensively so that a
        // shared vertex is only rebased once.
        var vertexDeltas = new Dictionary<(int BufferIndex, int VertexIndex), List<Vector3>>();
        void AddVertexDelta(int bufferIndex, int vertexIndex, int particleIndex)
        {
            if (vertexIndex < 0 || !particleDeltas.TryGetValue(particleIndex, out var delta))
                return;

            var key = (bufferIndex, vertexIndex);
            if (!vertexDeltas.TryGetValue(key, out var values))
            {
                values = new List<Vector3>();
                vertexDeltas.Add(key, values);
            }
            values.Add(delta);
        }

        var operators = GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>();
        foreach (var @operator in operators.Where(@operator =>
                     string.Equals(@operator.GetType().Name, "hclMoveParticlesOperator", StringComparison.Ordinal)))
        {
            var bufferIndex = ToInt(GetValue(@operator, "refBufferIdx"), 0);
            foreach (var pair in GetList(GetValue(@operator, "vertexParticlePairs")) ?? Array.Empty<object>())
            {
                AddVertexDelta(
                    bufferIndex,
                    ToInt(GetValue(pair, "vertexIndex"), -1),
                    ToInt(GetValue(pair, "particleIndex"), -1));
            }
        }

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraintSets = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        foreach (var set in constraintSets.Where(set =>
                     string.Equals(set.GetType().Name, "hclTransitionConstraintSet", StringComparison.Ordinal)))
        {
            var bufferIndex = ToInt(GetValue(set, "referenceMeshBufferIdx"), 0);
            foreach (var record in GetList(GetValue(set, "perParticleData")) ?? Array.Empty<object>())
            {
                AddVertexDelta(
                    bufferIndex,
                    ToInt(GetValue(record, "referenceVertex"), -1),
                    ToInt(GetValue(record, "particleIndex"), -1));
            }
        }

        if (vertexDeltas.Count == 0)
            return;

        foreach (var skin in operators.Where(@operator =>
                     string.Equals(@operator.GetType().Name, "hclObjectSpaceSkinPOperator", StringComparison.Ordinal)))
        {
            var outputBufferIndex = ToInt(GetValue(skin, "outputBufferIndex"), -1);
            var perVertex = vertexDeltas
                .Where(pair => pair.Key.BufferIndex == outputBufferIndex)
                .ToDictionary(
                    pair => pair.Key.VertexIndex,
                    pair => pair.Value.Aggregate(Vector3.Zero, (sum, value) => sum + value) / pair.Value.Count);
            if (perVertex.Count > 0)
                RebaseObjectSpaceSkinVertices(skin, perVertex);
        }

        // Bone-space skin operators store their reference vertices in each
        // influencing bone's local coordinates. This is used by compact cloths
        // such as the Nose Earring. Updating only the simulation particles
        // leaves the packed local positions behind, so the animation pipeline
        // can restore the previous pose on the next frame.
        var boneFrames = GetParticlePreview(clothIndex).Bones
            .GroupBy(bone => bone.Index)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var skin in operators.Where(@operator =>
                     string.Equals(@operator.GetType().Name, "hclBoneSpaceSkinPOperator", StringComparison.Ordinal)))
        {
            var outputBufferIndex = ToInt(GetValue(skin, "outputBufferIndex"), -1);
            var perVertex = vertexDeltas
                .Where(pair => pair.Key.BufferIndex == outputBufferIndex)
                .ToDictionary(
                    pair => pair.Key.VertexIndex,
                    pair => pair.Value.Aggregate(Vector3.Zero, (sum, value) => sum + value) / pair.Value.Count);
            if (perVertex.Count > 0)
                RebaseBoneSpaceSkinVertices(skin, perVertex, boneFrames);
        }
    }

    private static void RebaseObjectSpaceSkinVertices(object skinOperator, IReadOnlyDictionary<int, Vector3> vertexDeltas)
    {
        var deformer = GetValue(skinOperator, "objectSpaceDeformer");
        var positionBlocks = GetList(GetValue(skinOperator, "localUnpackedPs"));
        if (deformer == null || positionBlocks == null || positionBlocks.Count == 0)
            return;

        // Unpacked position blocks follow the packed blend blocks in Havok's
        // blend-count order. vertexIndices_N identifies which output vertex is
        // stored in localPosition_N.
        var blendMembers = new[]
        {
            "eightBlendEntries", "sevenBlendEntries", "sixBlendEntries", "fiveBlendEntries",
            "fourBlendEntries", "threeBlendEntries", "twoBlendEntries", "oneBlendEntries"
        };
        var positionBlockIndex = 0;
        foreach (var member in blendMembers)
        {
            var blendBlocks = GetList(GetValue(deformer, member)) ?? Array.Empty<object>();
            foreach (var blendBlock in blendBlocks)
            {
                if (positionBlockIndex >= positionBlocks.Count)
                    return;

                var positionBlock = positionBlocks[positionBlockIndex++];
                var rebasedVertices = new HashSet<int>();
                for (var slot = 0; slot < 16; slot++)
                {
                    var vertexIndex = ToInt(GetValue(blendBlock, $"vertexIndices_{slot}"), -1);
                    if (vertexIndex < 0 || !rebasedVertices.Add(vertexIndex) ||
                        !vertexDeltas.TryGetValue(vertexIndex, out var delta) ||
                        GetValue(positionBlock, $"localPosition_{slot}") is not Vector4 position)
                    {
                        continue;
                    }

                    SetValue(
                        positionBlock,
                        $"localPosition_{slot}",
                        new Vector4(position.X + delta.X, position.Y + delta.Y, position.Z + delta.Z, position.W));
                }
            }
        }
    }

    private static void RebaseBoneSpaceSkinVertices(
        object skinOperator,
        IReadOnlyDictionary<int, Vector3> vertexDeltas,
        IReadOnlyDictionary<int, BonePreviewPoint> boneFrames)
    {
        var deformer = GetValue(skinOperator, "boneSpaceDeformer");
        // Vanilla files commonly use packed localPs. Some exports use the
        // unpacked equivalent, so accept either without changing the format.
        var positionBlocks = GetList(GetValue(skinOperator, "localPs"))
            ?? GetList(GetValue(skinOperator, "localUnpackedPs"));
        var transformSubset = GetList(GetValue(skinOperator, "transformSubset"));
        if (deformer == null || positionBlocks == null || positionBlocks.Count == 0 || transformSubset == null)
            return;

        var blendMembers = new (string Member, int Count)[]
        {
            ("eightBlendEntries", 8), ("sevenBlendEntries", 7), ("sixBlendEntries", 6),
            ("fiveBlendEntries", 5), ("fourBlendEntries", 4), ("threeBlendEntries", 3),
            ("twoBlendEntries", 2), ("oneBlendEntries", 1)
        };
        var positionBlockIndex = 0;
        foreach (var (member, blendCount) in blendMembers)
        {
            var blendBlocks = GetList(GetValue(deformer, member)) ?? Array.Empty<object>();
            foreach (var blendBlock in blendBlocks)
            {
                if (positionBlockIndex >= positionBlocks.Count)
                    return;

                var positionBlock = positionBlocks[positionBlockIndex++];
                var rebasedVertices = new HashSet<int>();
                for (var slot = 0; slot < 16; slot++)
                {
                    var vertexIndex = ToInt(GetValue(blendBlock, $"vertexIndices_{slot}"), -1);
                    if (vertexIndex < 0 || !rebasedVertices.Add(vertexIndex) ||
                        !vertexDeltas.TryGetValue(vertexIndex, out var worldDelta))
                    {
                        continue;
                    }

                    for (var influence = 0; influence < blendCount; influence++)
                    {
                        var packedIndex = slot * blendCount + influence;
                        var subsetIndex = ToInt(GetValue(blendBlock, $"boneIndices_{packedIndex}"), -1);
                        var skeletonIndex = subsetIndex >= 0 && subsetIndex < transformSubset.Count
                            ? ToInt(transformSubset[subsetIndex], -1)
                            : -1;
                        if (skeletonIndex < 0 || !boneFrames.TryGetValue(skeletonIndex, out var bone) ||
                            GetValue(positionBlock, $"localPosition_{packedIndex}") is not Vector4 position)
                        {
                            continue;
                        }

                        // The packed point is in the coordinate frame of this
                        // influence. Project the world-space particle move onto
                        // that frame before writing it back.
                        var localDelta = new Vector3(
                            Vector3.Dot(worldDelta, bone.AxisX),
                            Vector3.Dot(worldDelta, bone.AxisY),
                            Vector3.Dot(worldDelta, bone.AxisZ));
                        SetValue(
                            positionBlock,
                            $"localPosition_{packedIndex}",
                            new Vector4(
                                position.X + localDelta.X,
                                position.Y + localDelta.Y,
                                position.Z + localDelta.Z,
                                position.W));
                    }
                }
            }
        }
    }

    public int AddParticle(int clothIndex, ParticleEditRow? sourceRow = null)
    {
        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simData = GetFirst(GetValue(cloth, "simClothDatas")) ?? throw new InvalidOperationException("No simulation cloth data found.");
        var particles = GetMutableList(GetValue(simData, "particleDatas"), "particleDatas");
        if (particles.Count == 0)
            throw new InvalidOperationException("Cannot add a particle because this cloth has no particle template to clone.");

        var newIndex = particles.Count;
        var sourceIndex = sourceRow?.Index >= 0 && sourceRow.Index < particles.Count ? sourceRow.Index : particles.Count - 1;
        var template = CloneForCurrentGraph(particles[sourceIndex]!);
        particles.Add(template);

        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetValue(pose, "positions") as IList;
        var sourcePosition = positions != null && sourceIndex < positions.Count && positions[sourceIndex] is Vector4 vector
            ? vector
            : Vector4.Zero;
        AddListItem(positions, sourceRow == null
            ? new Vector4(0.0f, 0.0f, 0.0f, 1.0f)
            : new Vector4(sourceRow.X, sourceRow.Y, sourceRow.Z, sourceRow.W == 0.0f ? 1.0f : sourceRow.W));

        var collisionMasks = GetValue(simData, "staticCollisionMasks") as IList;
        AddListItem(collisionMasks, sourceRow?.CollisionMask ?? 0u);

        AddMatchingListDefault(simData, "perParticlePinchDetectionEnabledFlags", false);
        AddMatchingListDefault(simData, "particlePinchDetectionEnabledFlags", false);

        var rows = GetParticleRows(clothIndex).ToList();
        var row = rows.FirstOrDefault(x => x.Index == newIndex);
        if (row != null)
        {
            row.Fixed = sourceRow?.Fixed ?? false;
            row.Mass = sourceRow?.Mass ?? row.Mass;
            row.InverseMass = sourceRow?.InverseMass ?? row.InverseMass;
            row.Radius = sourceRow?.Radius ?? row.Radius;
            row.Friction = sourceRow?.Friction ?? row.Friction;
            row.CollisionMask = sourceRow?.CollisionMask ?? row.CollisionMask;
        }
        ApplyParticleRows(cloth, rows);
        return newIndex;
    }

    public int AddBone(int clothIndex, BoneEditRow? sourceRow = null)
    {
        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var bones = GetMutableList(GetValue(skeleton, "bones"), "bones");
        var parents = GetValue(skeleton, "parentIndices") as IList ?? throw new InvalidOperationException("Skeleton parent list is not editable.");
        var poses = GetValue(skeleton, "referencePose") as IList ?? throw new InvalidOperationException("Skeleton reference pose list is not editable.");
        if (bones.Count == 0)
            throw new InvalidOperationException("Cannot add a bone because this skeleton has no bone template to clone.");

        var newIndex = bones.Count;
        var sourceIndex = sourceRow?.Index >= 0 && sourceRow.Index < bones.Count ? sourceRow.Index : bones.Count - 1;
        var template = CloneForCurrentGraph(bones[sourceIndex]!);
        SetValue(template, "name", $"New_Bone_{newIndex}");
        bones.Add(template);

        var parentIndex = sourceRow?.Index ?? -1;
        AddListItem(parents, parentIndex);

        var sourcePose = poses[sourceIndex] is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
        var offsetPose = sourceRow == null
            ? new Matrix4x4(
                0.0f, 0.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f,
                1.0f, 1.0f, 1.0f, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f)
            : new Matrix4x4(
                sourceRow.X, sourceRow.Y, sourceRow.Z, sourcePose.M14,
                sourceRow.RotationX, sourceRow.RotationY, sourceRow.RotationZ, sourceRow.RotationW,
                sourceRow.ScaleX, sourceRow.ScaleY, sourceRow.ScaleZ, sourcePose.M34,
                sourcePose.M41, sourcePose.M42, sourcePose.M43, sourcePose.M44);
        AddListItem(poses, offsetPose);
        return newIndex;
    }

    public int AddCollider(
        int clothIndex,
        ColliderEditRow? sourceRow = null,
        BoneEditRow? targetBone = null,
        string shapeTypeName = "hclCapsuleShape")
    {
        var root = RequireRoot();
        var collidables = GetMutableCollidables(root);
        if (collidables.Count == 0)
            throw new InvalidOperationException("Cannot add a collider because this file has no collider template to clone.");

        var newIndex = collidables.Count;
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var sourceCollider = sourceRow?.Index >= 0 && sourceRow.Index < collidables.Count
            ? collidables[sourceRow.Index]!
            : EnumerateReferencedCollidables(cloth)
                .Distinct(ReferenceEquality.Instance)
                .LastOrDefault()
              ?? collidables[collidables.Count - 1]!;
        var template = CloneForCurrentGraph(sourceCollider);
        var boneIndex = targetBone?.Index ?? sourceRow?.BoneIndex ?? 0;
        var boneName = targetBone?.Name ?? sourceRow?.BoneName ?? "Root";
        SetValue(template, "name", $"Collidable_{boneName}_{newIndex}");
        SetValue(template, "transformIndex", boneIndex);

        var shape = GetValue(template, "shape");
        if (sourceRow == null && !string.Equals(shape?.GetType().Name, shapeTypeName, StringComparison.Ordinal))
        {
            shape = CreateHkclColliderShape(template, shapeTypeName);
            SetValue(template, "shape", shape);
        }
        if (shape != null)
        {
            var start = sourceRow == null ? Vector4.Zero : new Vector4(sourceRow.StartX, sourceRow.StartY, sourceRow.StartZ, 0.0f);
            var end = sourceRow == null ? new Vector4(0.0f, 0.08f, 0.0f, 0.0f) : new Vector4(sourceRow.EndX, sourceRow.EndY, sourceRow.EndZ, 0.0f);
            switch (shape.GetType().Name)
            {
                case "hclSphereShape":
                {
                    var sphere = GetValue(shape, "sphere")
                        ?? throw new InvalidOperationException("The generated HKCL sphere shape has no hkSphere value.");
                    SetValue(sphere, "pos", new Vector4(start.X, start.Y, start.Z, sourceRow?.Radius ?? 0.05f));
                    break;
                }
                case "hclPlaneShape":
                    SetValue(shape, "planeEquation", new Vector4(0.0f, 1.0f, 0.0f, 0.0f));
                    break;
                default:
                    SetValue(shape, "start", start);
                    SetValue(shape, "end", end);
                    SetValue(shape, "radius", sourceRow?.Radius ?? 0.05f);
                    UpdateCapsuleDerivedValues(shape, start, end);
                    break;
            }
        }

        collidables.Add(template);

        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var perInstance = GetValue(simData, "perInstanceCollidables") as IList;
        AddListItem(perInstance, template);
        CopyColliderTransformMapEntry(simData, perInstance, sourceCollider, template);
        // A copied collider is expected to affect the same particles as its
        // source. Static collision masks address per-instance slots, so adding
        // the reference alone leaves the clone invisible to every particle.
        CopyColliderCollisionMaskBit(simData, perInstance, sourceCollider, template);

        // The map is indexed by the per-instance collider order. A collider
        // without a companion entry can draw in the editor but will not follow
        // its chosen bone in Havok at runtime.
        var initialTransform = GetValue(template, "transform") is Matrix4x4 initialMatrix
            ? initialMatrix
            : Matrix4x4.Identity;
        SynchronizeCollidableTransformMaps(root, new ColliderEditRow
        {
            ClothIndex = clothIndex,
            Index = newIndex,
            BoneIndex = boneIndex,
            Transform = initialTransform
        }, template);

        // Editor rows store collider endpoints in world space, while an HKCL
        // shape stores them in its transform-local space. Reuse the normal
        // serializer for copies so a pasted collider stays exactly in place.
        if (sourceRow != null)
        {
            var copiedRow = new ColliderEditRow
            {
                ClothIndex = clothIndex,
                Index = newIndex,
                Name = GetString(template, "name") ?? sourceRow.Name,
                ShapeType = sourceRow.ShapeType,
                BoneIndex = boneIndex,
                BoneName = boneName,
                StartX = sourceRow.StartX,
                StartY = sourceRow.StartY,
                StartZ = sourceRow.StartZ,
                EndX = sourceRow.EndX,
                EndY = sourceRow.EndY,
                EndZ = sourceRow.EndZ,
                Radius = sourceRow.Radius,
                Transform = sourceRow.Transform
            };
            UpdateColliderRows(new[] { copiedRow });
        }
        return newIndex;
    }
    public void DeleteParticle(int clothIndex, int particleIndex)
    {
        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simData = GetFirst(GetValue(cloth, "simClothDatas")) ?? throw new InvalidOperationException("No simulation cloth data found.");
        RemoveParticleTriangleReferences(simData, cloth, particleIndex);
        RemoveParticleConstraintReferences(simData, cloth, particleIndex);
        RemoveParticleFromSimulation(simData, particleIndex);
        ReindexFixedParticles(simData, particleIndex);
        RecalculateParticleTotals(simData);
    }

    public void DeleteBone(int clothIndex, int boneIndex)
    {
        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var bones = GetMutableList(GetValue(skeleton, "bones"), "bones");
        var parents = GetValue(skeleton, "parentIndices") as IList ?? throw new InvalidOperationException("Skeleton parent list is not editable.");
        var poses = GetValue(skeleton, "referencePose") as IList ?? throw new InvalidOperationException("Skeleton reference pose list is not editable.");
        if (boneIndex < 0 || boneIndex >= bones.Count)
            throw new ArgumentOutOfRangeException(nameof(boneIndex));

        for (var i = 0; i < parents.Count; i++)
        {
            if (ToInt(parents[i], -1) == boneIndex)
                throw new InvalidOperationException("That bone has child bones. Delete or reparent the child bones first.");
        }

        foreach (var collider in GetColliderRows(clothIndex))
        {
            if (collider.BoneIndex == boneIndex)
                throw new InvalidOperationException("That bone has a collider bound to it. Delete or move the collider first.");
        }

        bones.RemoveAt(boneIndex);
        parents.RemoveAt(boneIndex);
        poses.RemoveAt(boneIndex);
        for (var i = 0; i < parents.Count; i++)
        {
            var parent = ToInt(parents[i], -1);
            if (parent > boneIndex)
                SetListItem(parents, i, parent - 1);
        }
    }

    public void DeleteCollider(int clothIndex, int colliderIndex)
    {
        var root = RequireRoot();
        var collidables = GetMutableCollidables(root);
        if (colliderIndex < 0 || colliderIndex >= collidables.Count)
            throw new ArgumentOutOfRangeException(nameof(colliderIndex));

        var target = collidables[colliderIndex]
            ?? throw new InvalidOperationException("The selected collider slot is empty.");
        var selectedCloth = GetClothDatas(root).ElementAtOrDefault(clothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));

        // Delete from this cloth first. A global hclCollidable can be shared
        // by another cloth, in which case removing it from the container would
        // silently break that other cloth's physics.
        RemoveCollidableReferences(selectedCloth, target);
        var stillReferenced = GetClothDatas(root)
            .Any(cloth => EnumerateReferencedCollidables(cloth).Any(collidable => ReferenceEquals(collidable, target)));
        if (!stillReferenced)
            collidables.RemoveAt(colliderIndex);
    }

    private static void RemoveCollidableReferences(object cloth, object target)
    {
        RemoveFromList(GetValue(cloth, "perInstanceCollidables") as IList, target);
        foreach (var simData in GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
            RemoveFromList(GetValue(simData, "perInstanceCollidables") as IList, target);
    }

    private static void RemoveFromList(IList? list, object target)
    {
        if (list == null)
            return;

        for (var index = list.Count - 1; index >= 0; index--)
        {
            if (ReferenceEquals(list[index], target))
                list.RemoveAt(index);
        }
    }

    public void LinkParticles(int clothIndex, IReadOnlyList<int> particleIndices)
    {
        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var simData = GetFirst(GetValue(cloth, "simClothDatas")) ?? throw new InvalidOperationException("No simulation cloth data found.");
        if (particleIndices.Count == 2)
        {
            AddConstraintLink(simData, cloth, particleIndices[0], particleIndices[1]);
            return;
        }

        if (particleIndices.Count == 3)
        {
            AddTriangle(simData, particleIndices[0], particleIndices[1], particleIndices[2]);
            AddConstraintLink(simData, cloth, particleIndices[0], particleIndices[1]);
            AddConstraintLink(simData, cloth, particleIndices[1], particleIndices[2]);
            AddConstraintLink(simData, cloth, particleIndices[2], particleIndices[0]);
            return;
        }

        throw new InvalidOperationException("Select exactly 2 particles for a link, or exactly 3 particles for a triangle.");
    }

    private static void RemoveParticleTriangleReferences(object simData, object cloth, int particleIndex)
    {
        if (GetValue(simData, "triangleIndices") is not IList triangles)
            return;

        var removedTriangles = new HashSet<int>();
        var rewritten = new List<int>(triangles.Count);
        for (var offset = 0; offset + 2 < triangles.Count; offset += 3)
        {
            var a = ToInt(triangles[offset], -1);
            var b = ToInt(triangles[offset + 1], -1);
            var c = ToInt(triangles[offset + 2], -1);
            var triangleIndex = offset / 3;
            if (a == particleIndex || b == particleIndex || c == particleIndex)
            {
                removedTriangles.Add(triangleIndex);
                continue;
            }

            rewritten.Add(a > particleIndex ? a - 1 : a);
            rewritten.Add(b > particleIndex ? b - 1 : b);
            rewritten.Add(c > particleIndex ? c - 1 : c);
        }

        if (removedTriangles.Count == 0)
            return;

        triangles.Clear();
        foreach (var index in rewritten)
            AddListItem(triangles, index);

        // SimpleMeshBoneDeform addresses source triangles in six-byte slots
        // (three UInt16 particle indexes). Remove mappings for deleted faces
        // and shift later offsets to match the compacted triangle list.
        foreach (var op in GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>())
        {
            if (GetValue(op, "triangleBonePairs") is not IList pairs)
                continue;

            var localTransforms = GetValue(op, "localBoneTransforms") as IList;
            var alignedLocalTransforms = localTransforms?.Count == pairs.Count;
            for (var pairIndex = pairs.Count - 1; pairIndex >= 0; pairIndex--)
            {
                var pair = pairs[pairIndex];
                var triangleIndex = ToInt(GetValue(pair, "triangleOffset"), 0) / 6;
                if (removedTriangles.Contains(triangleIndex))
                {
                    pairs.RemoveAt(pairIndex);
                    if (alignedLocalTransforms && pairIndex < localTransforms!.Count)
                        localTransforms.RemoveAt(pairIndex);
                    continue;
                }

                var removedBefore = removedTriangles.Count(index => index < triangleIndex);
                if (removedBefore > 0)
                    SetValue(pair, "triangleOffset", (triangleIndex - removedBefore) * 6);
            }
        }
    }

    private static void RemoveParticleConstraintReferences(object simData, object cloth, int particleIndex)
    {
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        foreach (var constraint in constraints)
        {
            if (GetValue(constraint, "links") is IList links)
            {
                for (var index = links.Count - 1; index >= 0; index--)
                {
                    var link = links[index];
                    var a = ToInt(GetValue(link, "particleA"), -1);
                    var b = ToInt(GetValue(link, "particleB"), -1);
                    if (a == particleIndex || b == particleIndex)
                    {
                        links.RemoveAt(index);
                        continue;
                    }

                    if (a > particleIndex)
                        SetValue(link, "particleA", a - 1);
                    if (b > particleIndex)
                        SetValue(link, "particleB", b - 1);
                }
            }

            if (GetValue(constraint, "localConstraints") is IList locals)
            {
                for (var index = locals.Count - 1; index >= 0; index--)
                {
                    var local = locals[index];
                    var referenced = ToInt(GetValue(local, "particleIndex"), -1);
                    if (referenced == particleIndex)
                    {
                        locals.RemoveAt(index);
                        continue;
                    }

                    if (referenced > particleIndex)
                        SetValue(local, "particleIndex", referenced - 1);
                }
            }
        }
    }

    private static void RemoveParticleFromSimulation(object simData, int particleIndex)
    {
        var particles = GetMutableList(GetValue(simData, "particleDatas"), "particleDatas");
        if (particleIndex < 0 || particleIndex >= particles.Count)
            throw new ArgumentOutOfRangeException(nameof(particleIndex));
        particles.RemoveAt(particleIndex);

        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        if (GetValue(pose, "positions") is IList positions && particleIndex < positions.Count)
            positions.RemoveAt(particleIndex);
        if (GetValue(simData, "staticCollisionMasks") is IList masks && particleIndex < masks.Count)
            masks.RemoveAt(particleIndex);
        if (GetValue(simData, "perParticlePinchDetectionEnabledFlags") is IList pinch && particleIndex < pinch.Count)
            pinch.RemoveAt(particleIndex);
        if (GetValue(simData, "particlePinchDetectionEnabledFlags") is IList pinch2 && particleIndex < pinch2.Count)
            pinch2.RemoveAt(particleIndex);
    }

    private static void ReindexFixedParticles(object simData, int removedIndex)
    {
        if (GetValue(simData, "fixedParticles") is not IList fixedParticles)
            return;

        var values = fixedParticles.Cast<object>()
            .Select(x => ToInt(x, -1))
            .Where(x => x >= 0 && x != removedIndex)
            .Select(x => x > removedIndex ? x - 1 : x)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        fixedParticles.Clear();
        foreach (var value in values)
            AddListItem(fixedParticles, value);
    }

    private static void AddTriangle(object simData, int a, int b, int c)
    {
        var indices = GetValue(simData, "triangleIndices") as IList;
        if (indices == null)
            throw new InvalidOperationException("This cloth does not expose editable triangle indices.");

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var existing = new[] { ToInt(indices[i], -1), ToInt(indices[i + 1], -1), ToInt(indices[i + 2], -1) }.OrderBy(x => x).ToArray();
            var incoming = new[] { a, b, c }.OrderBy(x => x).ToArray();
            if (existing.SequenceEqual(incoming))
                return;
        }

        AddListItem(indices, a);
        AddListItem(indices, b);
        AddListItem(indices, c);
    }

    private static void AddConstraintLink(object simData, object cloth, int a, int b)
    {
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        var target = constraints.FirstOrDefault(c => GetList(GetValue(c, "links"))?.Count > 0 && c.GetType().Name.Contains("Standard", StringComparison.OrdinalIgnoreCase))
            ?? constraints.FirstOrDefault(c => GetList(GetValue(c, "links"))?.Count > 0);
        if (target == null)
            throw new InvalidOperationException("No editable link constraint set was found.");

        var links = GetMutableList(GetValue(target, "links"), "links");
        foreach (var link in links)
        {
            var existingA = ToInt(GetValue(link, "particleA"), -1);
            var existingB = ToInt(GetValue(link, "particleB"), -1);
            if ((existingA == a && existingB == b) || (existingA == b && existingB == a))
                return;
        }

        var template = CloneForCurrentGraph(links[0]!);
        SetValue(template, "particleA", a);
        SetValue(template, "particleB", b);
        SetValue(template, "restLength", GetParticleDistance(simData, a, b));
        SetValue(template, "stiffness", 1.0f);
        links.Add(template);
    }

    private static float GetParticleDistance(object simData, int a, int b)
    {
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetList(GetValue(pose, "positions"));
        if (positions == null || a < 0 || b < 0 || a >= positions.Count || b >= positions.Count)
            return 0.0f;

        var pa = positions[a] is Vector4 va ? va : Vector4.Zero;
        var pb = positions[b] is Vector4 vb ? vb : Vector4.Zero;
        var dx = pa.X - pb.X;
        var dy = pa.Y - pb.Y;
        var dz = pa.Z - pb.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static void AddMatchingListDefault(object owner, string fieldName, object defaultValue)
    {
        if (GetValue(owner, fieldName) is IList list)
            AddListItem(list, defaultValue);
    }

    private static void UpdateCapsuleDerivedValues(object shape, Vector4 start, Vector4 end)
    {
        var delta = end - start;
        var lengthSq = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;
        if (lengthSq <= 0.000001f)
        {
            throw new InvalidOperationException(
                "Capsule endpoints are too close together. Use a sphere collider for a zero-length collision shape.");
        }

        var length = MathF.Sqrt(lengthSq);
        SetValue(shape, "dir", new Vector4(delta.X / length, delta.Y / length, delta.Z / length, 0.0f));
        SetValue(shape, "capLenSqrdInv", 1.0f / lengthSq);
    }

    public void RemoveCloth(int index)
    {
        if (IsHelperBoneSidecar)
            throw new InvalidOperationException("Helper/support-bone files do not contain removable cloth entries.");

        if (_bphcl != null)
        {
            if (index < 0 || index >= _bphcl.ClothCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            var tempPath = CreateTemporaryBphclPath();
            _bphcl = BphclBridge.DeleteCloth(_bphcl.SourcePath, tempPath, index);
            return;
        }

        var root = RequireRoot();
        var cloths = GetMutableClothDatas(root);
        if (index < 0 || index >= cloths.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        cloths.RemoveAt(index);

        var skeletons = GetMutableSkeletons(root);
        if (index >= 0 && index < skeletons.Count)
            skeletons.RemoveAt(index);
    }

    private static void RecalculateParticleTotals(object simData)
    {
        var particles = GetList(GetValue(simData, "particleDatas")) ?? Array.Empty<object>();
        var totalMass = 0.0f;
        var maxRadius = 0.0f;
        foreach (var particle in particles)
        {
            totalMass += Convert.ToSingle(GetValue(particle, "mass") ?? 0.0f, CultureInfo.InvariantCulture);
            maxRadius = Math.Max(maxRadius, Convert.ToSingle(GetValue(particle, "radius") ?? 0.0f, CultureInfo.InvariantCulture));
        }

        SetValue(simData, "totalMass", totalMass);
        SetValue(simData, "maxParticleRadius", maxRadius);
    }

    public void RenameCloth(int index, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A cloth name cannot be empty.", nameof(name));

        if (IsHelperBoneSidecar)
            throw new InvalidOperationException("Helper/support-bone files do not contain cloth names.");

        if (_bphcl != null)
        {
            if (index < 0 || index >= _bphcl.ClothCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            var tempPath = CreateTemporaryBphclPath();
            _bphcl = BphclBridge.RenameCloth(_bphcl.SourcePath, tempPath, index, name.Trim());
            return;
        }

        var cloths = GetMutableClothDatas(RequireRoot());
        if (index < 0 || index >= cloths.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var clothName = name.Trim();
        SetValue(cloths[index], "name", clothName);

        var skeletons = GetMutableSkeletons(RequireRoot());
        if (index < skeletons.Count)
            SetValue(skeletons[index], "name", $"cloth_skeleton_{StripBphclPrefix(clothName)}");
    }

    /// <summary>
    /// Duplicates one complete cloth package and returns its new cloth index.
    /// HKCL uses the ordinary graph-cloning merge path; BPHCL deliberately
    /// routes through its native DATA/ITEM/PTCH importer so the duplicate is
    /// a real independent Phive entry rather than an aliased object graph.
    /// </summary>
    public int DuplicateCloth(int index)
    {
        if (IsHelperBoneSidecar)
            throw new InvalidOperationException("Helper/support-bone files do not contain duplicable cloth entries.");

        var duplicateName = GetUniqueDuplicateClothName(index);
        if (_bphcl?.NativeDocument is { } bphcl)
        {
            if (index < 0 || index >= bphcl.Cloths.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            // Persist the current in-memory document to an isolated target
            // first. Viewport edits may not exist in SourcePath yet.
            var targetPath = CreateTemporaryBphclPath();
            BphclBridge.Save(bphcl, targetPath);
            var donorPath = CreateTemporaryBphclPath();
            NativeBphclWriter.SaveRenamedCloth(bphcl, index, duplicateName, donorPath);
            var outputPath = CreateTemporaryBphclPath();
            _bphcl = BphclBridge.DuplicateCloth(targetPath, donorPath, outputPath, index);
            return _bphcl.ClothCount - 1;
        }

        var cloneSource = new HkclService();
        cloneSource.RestoreState(CaptureState());
        MergeClothFrom(cloneSource, index);
        var duplicateIndex = GetMutableClothDatas(RequireRoot()).Count - 1;
        RenameCloth(duplicateIndex, duplicateName);
        return duplicateIndex;
    }

    private string GetUniqueDuplicateClothName(int index)
    {
        var sourceName = GetClothName(index);
        var existingNames = GetClothSummaries()
            .Select(summary => summary[(summary.IndexOf(':') + 1)..].Split("  |", StringSplitOptions.None)[0].Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var suffix = 1; ; suffix++)
        {
            var candidate = $"{sourceName}_{suffix}";
            if (!existingNames.Contains(candidate))
                return candidate;
        }
    }

    private string CreateTemporaryBphclPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PhysicsTool");
        Directory.CreateDirectory(directory);
        var baseName = string.IsNullOrWhiteSpace(_path)
            ? "Edited"
            : Path.GetFileNameWithoutExtension(_path);
        return Path.Combine(directory, $"{baseName}.{Guid.NewGuid():N}.bphcl");
    }

    // Returns a human-readable gate for the conservative BPHCL -> HKCL
    // converter. It is deliberately based on the same requirements used by
    // the writer, so a green result means the conversion is eligible rather
    // than merely visually similar in the viewport.
    public string DescribeMergePreflight(HkclService reference, int clothIndex, int templateClothIndex = 0)
    {
        if (_root != null && reference._bphcl?.NativeDocument is { } sourceDocument)
            return GetBphclToHkclConversionPreflight(sourceDocument, clothIndex, templateClothIndex).ToDisplayText();

        if (_bphcl != null && reference._bphcl != null)
            return GetBphclMergePreflight(reference, clothIndex).ToDisplayText();

        return "The selected physics files use the same native format. " +
               "The selected cloth will be copied with its paired skeleton and referenced colliders.";
    }

    internal BphclToHkclConversionPreflight GetBphclToHkclConversionPreflight(
        HkclService reference,
        int sourceClothIndex,
        int templateClothIndex)
    {
        if (_root == null || reference._bphcl?.NativeDocument is not { } sourceDocument)
            throw new InvalidOperationException("BPHCL -> HKCL preflight requires an HKCL target and a BPHCL source.");

        return GetBphclToHkclConversionPreflight(sourceDocument, sourceClothIndex, templateClothIndex);
    }

    internal NativeBphclMergePreflight GetBphclMergePreflight(HkclService reference, int clothIndex)
    {
        if (_bphcl?.NativeDocument is not { } target || reference._bphcl?.NativeDocument is not { } source)
            throw new InvalidOperationException("BPHCL merge preflight requires a BPHCL target and reference.");
        return NativeBphclMergePreflight.Analyze(target, source, clothIndex);
    }

    private BphclToHkclConversionPreflight GetBphclToHkclConversionPreflight(
        NativeBphclDocument sourceDocument,
        int sourceClothIndex,
        int templateClothIndex)
    {
        var root = RequireRoot();
        var sourceCloth = sourceDocument.Cloths.ElementAtOrDefault(sourceClothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(sourceClothIndex));
        var sourceSkeleton = sourceDocument.Skeletons.ElementAtOrDefault(sourceClothIndex);
        var sourceSimulation = sourceCloth.SimCloths.FirstOrDefault();
        var template = GetClothDatas(root).ElementAtOrDefault(templateClothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(templateClothIndex));
        var templateSkeleton = GetSkeletons(root).ElementAtOrDefault(templateClothIndex);

        var templateName = GetString(template, "name") ?? $"cloth {templateClothIndex}";
        var passed = new List<string>();
        var blocked = new List<string>();
        var notes = new List<string>();

        void Check(bool succeeded, string description)
        {
            if (succeeded)
                passed.Add(description);
            else
                blocked.Add(description);
        }

        Check(sourceCloth.SimClothCount == 1,
            $"one simulation cloth (source has {sourceCloth.SimClothCount})");
        if (sourceSimulation == null)
        {
            blocked.Add("source has no readable simulation data");
        }
        else
        {
            notes.Add(
                sourceSimulation.Particles.Count == GetParticleCount(template)
                    ? $"particle topology has {sourceSimulation.Particles.Count} particles"
                    : $"particle topology will be rebuilt ({sourceSimulation.Particles.Count} source / {GetParticleCount(template)} template particles)");
        }

        var templateBones = templateSkeleton is null
            ? Array.Empty<object>()
            : (GetList(GetValue(templateSkeleton, "bones")) ?? Array.Empty<object>());
        var sourceBoneNames = sourceSkeleton?.Bones
            .Select(bone => StripBphclPrefix(bone.Name))
            .ToArray() ?? Array.Empty<string>();
        Check(sourceSkeleton != null, "source skeleton is readable");
        Check(templateBones.Count > 0, "HKCL template provides a skeleton shell");
        var templateBoneNames = templateBones
            .Select(bone => StripBphclPrefix(GetString(bone, "name") ?? string.Empty))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!sourceBoneNames.All(templateBoneNames.Contains))
            notes.Add("source bone names differ; the converter will rebuild the template skeleton shell from BPHCL names, parents, and reference poses");

        var sourceColliders = sourceSimulation == null
            ? Array.Empty<NativeBphclCollider>()
            : sourceDocument.Colliders
                .Where(collider => sourceSimulation.CollidableItemIndices.Contains(collider.ItemIndex))
                .ToArray();
        var templateColliderCount = EnumerateReferencedCollidables(template)
            .Distinct(ReferenceEquality.Instance)
            .Count();
        Check(sourceColliders.Length == 0 || templateColliderCount > 0,
            sourceColliders.Length == 0
                ? "source does not use colliders"
                : $"HKCL template provides collider shell(s) ({templateColliderCount})");
        if (sourceColliders.Length != templateColliderCount)
        {
            notes.Add($"source uses {sourceColliders.Length} collider(s); conversion will create that count from the available HKCL collider shell(s)");
        }
        var unsupportedShapes = sourceColliders
            .Select(collider => collider.Shape.TypeName)
            .Where(type => type is not ("hclCapsuleShape" or "hclTaperedCapsuleShape" or "hclSphereShape" or "hclPlaneShape"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Check(unsupportedShapes.Length == 0,
            unsupportedShapes.Length == 0
                ? "collider shapes are supported by this converter"
                : $"unsupported shapes: {string.Join(", ", unsupportedShapes)}");

        if (sourceSimulation != null)
        {
            Check(HasMatchingBphclTopologyTemplate(sourceCloth, sourceSimulation, template),
                "matching buffer, virtual-triangle, and operator shell layout");
            var targetSimulation = GetFirst(GetValue(template, "simClothDatas"));
            var targetSets = GetList(GetValue(targetSimulation, "staticConstraintSets"))
                ?? GetList(GetValue(template, "constraintSets"))
                ?? Array.Empty<object>();
            Check(targetSets.Count > 0, "writable HKCL constraint-set collection");
            var importedSets = sourceSimulation.ConstraintSets.Count(set => set.Links.Count > 0);
            if (importedSets > 0)
                notes.Add($"conversion will create or replace {importedSets} constraint-set record group(s) from BPHCL");
        }

        return new BphclToHkclConversionPreflight(
            StripBphclPrefix(sourceCloth.Name),
            templateName,
            passed,
            blocked,
            notes);
    }

    public string MergeClothFrom(HkclService reference, int clothIndex, int templateClothIndex = 0)
    {
        if (IsHelperBoneSidecar || reference.IsHelperBoneSidecar)
        {
            if (_bphhb != null && reference._bphhb != null)
            {
                _bphhb = BphhbBridge.MergeDriverGroup(_bphhb, reference._bphhb, clothIndex);
                return "Merged selected BPHHB driver group.";
            }

            if (_bphyssb != null && reference._bphyssb != null)
            {
                _bphyssb = BphyssbBridge.MergeDriverGroup(_bphyssb, reference._bphyssb, clothIndex);
                return "Merged selected BPHYSSB driver group.";
            }

            throw new InvalidOperationException(
                "Driver-group merging currently requires both files to use the same sidecar format. " +
                "Convert the donor first when moving between BPHHB and BPHYSSB.");
        }

        if (_bphcl != null || reference._bphcl != null)
        {
            // This is deliberately a template conversion, not a binary-format
            // transplant. The selected HKCL cloth keeps the data BPHCL does not
            // expose yet (deformers, buffers, and operator layout).
            if (_bphcl == null && reference._bphcl?.NativeDocument is { } bphcl)
                return ConvertBphclClothFrom(reference, bphcl, clothIndex, templateClothIndex);

            if (_bphcl == null || reference._bphcl == null)
                throw new InvalidOperationException("BPHCL -> HKCL conversion requires an HKCL target and a BPHCL reference. The reverse direction is not implemented yet.");
            if (clothIndex < 0 || clothIndex >= reference._bphcl.ClothCount)
                throw new ArgumentOutOfRangeException(nameof(clothIndex));

            var tempPath = CreateTemporaryBphclPath();
            _bphcl = BphclBridge.MergeCloth(_bphcl.SourcePath, reference._bphcl.SourcePath, tempPath, clothIndex);
            return "Merged selected BPHCL cloth.";
        }

        var root = RequireRoot();
        var refRoot = reference.RequireRoot();

        var sourceCloths = GetClothDatas(refRoot).ToList();
        if (clothIndex < 0 || clothIndex >= sourceCloths.Count)
            throw new ArgumentOutOfRangeException(nameof(clothIndex));

        var sourceCloth = sourceCloths[clothIndex];
        var importedCloth = CloneForCurrentGraph(sourceCloth);
        var targetCloths = GetMutableClothDatas(root);
        targetCloths.Add(importedCloth);

        var sourceSkeleton = GetSkeletons(refRoot).ElementAtOrDefault(clothIndex);
        if (sourceSkeleton != null)
            GetMutableSkeletons(root).Add(CloneForCurrentGraph(sourceSkeleton));

        // A cloth does not own its hclCollidables.  It points at entries in the
        // container-wide list, so clone that small graph once and reconnect the
        // imported per-instance lists to the new container objects.
        var sourceColliders = EnumerateReferencedCollidables(sourceCloth)
            .Distinct(ReferenceEquality.Instance)
            .ToList();
        var importedColliders = sourceColliders
            .Select(CloneForCurrentGraph)
            .ToList();

        var targetCollidables = GetMutableCollidables(root);
        foreach (var collidable in importedColliders)
            targetCollidables.Add(collidable);

        RemapClothCollidableReferences(importedCloth, sourceCloth,
            sourceColliders.Zip(importedColliders).ToDictionary(pair => pair.First, pair => pair.Second, ReferenceEquality.Instance));

        return "Merged selected HKCL cloth.";
    }

    private string ConvertBphclClothFrom(
        HkclService reference,
        NativeBphclDocument sourceDocument,
        int sourceClothIndex,
        int templateClothIndex)
    {
        var root = RequireRoot();
        var sourceCloth = sourceDocument.Cloths.ElementAtOrDefault(sourceClothIndex)
            ?? throw new ArgumentOutOfRangeException(nameof(sourceClothIndex));
        var sourceSimulation = sourceCloth.SimCloths.FirstOrDefault()
            ?? throw new InvalidOperationException("The selected BPHCL cloth has no simulation data to convert.");
        var sourceSkeleton = sourceDocument.Skeletons.ElementAtOrDefault(sourceClothIndex)
            ?? throw new InvalidOperationException("The selected BPHCL cloth has no matching skeleton.");

        var templates = GetClothDatas(root).ToList();
        var skeletons = GetSkeletons(root).ToList();
        if (templateClothIndex < 0 || templateClothIndex >= templates.Count)
            throw new InvalidOperationException("Select an HKCL cloth to use as the conversion template.");

        // A cloth's particle count is only one part of its topology. The
        // skinning blocks and gather/copy operator payloads also encode the
        // vertex layout. Do not produce a mixed BPHCL/HKCL graph until those
        // payloads can be rebuilt from native BPHCL data.
        var selectedTemplate = templates[templateClothIndex];
        var selectedSkeleton = skeletons.ElementAtOrDefault(templateClothIndex);
        var selectedParticleCount = GetParticleCount(selectedTemplate);

        // The particle count alone is not a topology match. Triangle indices
        // feed directly into SimpleMeshBoneDeform; retaining a template's
        // triangles while importing source particle/skin data can send output
        // bones arbitrarily far from the actor. Always rebuild this source-
        // owned virtual cloth mesh before applying its particle values.
        var reconstructParticleTopology = true;
        // The BPHCL transform-set/deformer path indexes this skeleton
        // positionally. Preserve the source count exactly even when a BotW
        // shell contains an extra name such as Arm_1_L; keeping the extra
        // slot causes the imported simulation to remain static in-game.
        var reconstructSkeleton = selectedSkeleton is object skeletonShell
            ? (GetList(GetValue(skeletonShell, "bones"))?.Count ?? 0) != sourceSkeleton.BoneCount
            : false;
        // Conversion replaces exactly the HKCL cloth selected in the UI. An
        // earlier automatic fallback could silently pick another compatible
        // shell, leaving the user with a valid binary attached to the wrong
        // actor entry. The shared preflight now blocks an incompatible choice
        // before this method is allowed to modify the object graph.
        var template = templates[templateClothIndex];
        var templateSkeleton = skeletons.ElementAtOrDefault(templateClothIndex)
            ?? throw new InvalidOperationException("The compatible HKCL template has no matching skeleton.");
        // A transform-set count is part of the cloth's internal topology.
        // When TotK and BotW shells differ, retain the selected HKCL object's
        // serialization classes but rebuild its skeleton shell to the source
        // count. This avoids leaving a source deformer bound to a phantom
        // template transform.
        var sourceColliders = sourceDocument.Colliders
            .Where(collider => sourceSimulation.CollidableItemIndices.Contains(collider.ItemIndex))
            .ToList();
        var templateColliders = EnumerateReferencedCollidables(template)
            .Distinct(ReferenceEquality.Instance)
            .ToList();

        if (sourceCloth.SimClothCount != 1)
            throw new InvalidOperationException("This first BPHCL -> HKCL pass supports one simulation cloth per imported unit.");
        ValidateBphclTopologyTemplate(sourceCloth, sourceSimulation, template);
        ValidateBphclConstraintTemplate(sourceSimulation, template);
        if (sourceColliders.Any(collider => collider.Shape.TypeName is not ("hclCapsuleShape" or "hclTaperedCapsuleShape" or "hclSphereShape" or "hclPlaneShape")))
        {
            var unsupported = sourceColliders
                .Select(collider => collider.Shape.TypeName)
                .Where(type => type is not ("hclCapsuleShape" or "hclTaperedCapsuleShape" or "hclSphereShape" or "hclPlaneShape"))
                .Distinct();
            throw new InvalidOperationException($"This conversion pass cannot create these BPHCL collider shape(s) yet: {string.Join(", ", unsupported)}.");
        }

        var convertedCloth = CloneForCurrentGraph(template);
        var convertedSkeleton = CloneForCurrentGraph(templateSkeleton);
        var colliderTemplates = EnumerateReferencedCollidables(convertedCloth)
            .Distinct(ReferenceEquality.Instance)
            .ToList();
        if (sourceColliders.Count > 0 && colliderTemplates.Count == 0)
            throw new InvalidOperationException("The selected HKCL template has no collider object to use as a serialization template.");

        var boneMap = reconstructSkeleton
            ? PrepareBphclSkeletonShell(convertedSkeleton, sourceSkeleton)
            : BuildBphclBoneMap(sourceSkeleton, GetList(GetValue(convertedSkeleton, "bones")) ?? Array.Empty<object>());
        ApplyBphclSkeleton(convertedSkeleton, sourceSkeleton, boneMap);
        if (reconstructParticleTopology)
            RebuildBphclParticleTopology(convertedCloth, sourceSimulation);
        ApplyBphclSimulationSettings(convertedCloth, sourceSimulation);

        // Use the same catalog/structural suggestion as the full BPHCL
        // converter. The former particle-count fallback was only a last
        // resort and can be badly wrong for two cloths with similar sizes.
        var bphclSolverScale = reference.ResolveBphclSolverScale(
            sourceClothIndex,
            sourceSimulation,
            overrides: null);
        ApplyBphclParticles(
            convertedCloth,
            sourceSimulation,
            bphclSolverScale,
            preserveSourceLandscapeCollisionBit: false);
        var topologyNotes = ApplyBphclBufferAndOperatorLayout(
            convertedCloth,
            sourceCloth,
            boneMap,
            preserveTemplateTransformSetShape: !reconstructSkeleton);
        var convertedColliders = CreateBphclColliders(
            convertedCloth,
            colliderTemplates,
            sourceColliders,
            sourceSkeleton,
            convertedSkeleton,
            boneMap,
            sourceSimulation.CollidableItemIndices);
        var activeConvertedColliders = OrderConvertedCollidersBySourceItems(
            sourceColliders,
            convertedColliders,
            sourceSimulation.CollidableItemIndices);
        var colliderMapNote = ApplyBphclCollidableTransformMap(
            convertedCloth,
            sourceSimulation.CollidableTransformMap,
            sourceSimulation.CollidableItemIndices.Count,
            boneMap,
            convertedSkeleton,
            activeConvertedColliders);
        // BPHCL and HKCL do not expose identical constraint-set layouts.
        // Keep the verified HKCL sets (including local-range and transition
        // data) and patch only matching source records into them. Creating
        // empty constraint classes from partially decoded BPHCL data can
        // disable the entire BotW cloth container at runtime.
        var constraintNotes = ApplyBphclConstraintLinks(
            convertedCloth,
            sourceSimulation,
            _bphcl?.SourcePath,
            sourceCloth.Name,
            preserveTemplateLayout: true,
            solverScale: bphclSolverScale);
        var executionNotes = ApplyBphclConstraintExecution(
            convertedCloth,
            sourceSimulation,
            sourceCloth.Name,
            sourceCloth.Operators);
        var deformerNotes = ApplyBphclSimpleMeshBoneDeform(convertedCloth, sourceCloth.SimpleMeshBoneDeformers, boneMap);

        // BPHCL -> HKCL is a template replacement. Preserve the selected
        // HKCL object's exact internal name and index; the actor parameter
        // may use a Link: name while the binary HKCL itself does not. BPHCL
        // source labels can also be mesh-facing rather than cloth-facing.
        // Skeleton bone names remain unprefixed so they match the BotW model.
        SetValue(convertedCloth, "name", GetString(template, "name") ?? StripBphclPrefix(sourceCloth.Name));
        SetValue(convertedSkeleton, "name", StripBphclPrefix(sourceSkeleton.Name));

        SetListItem(GetMutableClothDatas(root), templateClothIndex, convertedCloth);
        SetListItem(GetMutableSkeletons(root), templateClothIndex, convertedSkeleton);
        var targetCollidables = GetMutableCollidables(root);
        ReplaceTemplateColliderSlots(
            root,
            templateClothIndex,
            targetCollidables,
            templateColliders,
            convertedColliders);
        // Reattach after the objects are rooted in the container. BotW reads
        // colliders from hclSimClothData, rather than the legacy cloth-level
        // list, so this is deliberately performed at simulation level.
        ReplaceClothCollidableReferences(convertedCloth, activeConvertedColliders);

        var notes = new List<string>
        {
            "BPHCL -> HKCL conversion replaced the selected HKCL template in place, preserving its internal name and index.",
            "Skeleton, particles, one-to-one colliders, buffers, and verified matching link values were copied.",
            "The source object-space skinning, copy/gather routing, and simple bone-output mapping were rebuilt from BPHCL data."
        };
        if (reconstructParticleTopology)
            notes.Insert(0, $"Rebuilt the virtual cloth topology from the source ({selectedParticleCount} template particles -> {sourceSimulation.Particles.Count} BPHCL particles; {sourceSimulation.TriangleCount} source triangles).");
        if (reconstructSkeleton)
            notes.Insert(1, "The cloth skeleton was rebuilt from the BPHCL source names, parents, and reference poses so its transform layout matches exactly.");
        notes.AddRange(constraintNotes);
        notes.AddRange(executionNotes);
        notes.AddRange(topologyNotes);
        notes.AddRange(deformerNotes);
        notes.Add(colliderMapNote);
        return string.Join(" ", notes);
    }

    private static void ReplaceTemplateColliderSlots(
        object root,
        int templateClothIndex,
        IList containerCollidables,
        IReadOnlyList<object> templateColliders,
        IReadOnlyList<object> convertedColliders)
    {
        // A template replacement must replace its rooted collider shells too.
        // Appending clones leaves an unused duplicate in hclClothContainer;
        // the vanilla self-conversion test exposes that as five collidables
        // where the untouched pair has four. Do not replace a shell shared by
        // another cloth: that cloth still owns the original object.
        var otherCloths = GetClothDatas(root)
            .Where((_, index) => index != templateClothIndex)
            .ToArray();
        var hasSharedShell = templateColliders.Any(templateCollider =>
            otherCloths.Any(cloth => EnumerateReferencedCollidables(cloth)
                .Any(collidable => ReferenceEquals(collidable, templateCollider))));

        if (hasSharedShell)
        {
            foreach (var collider in convertedColliders)
                AddListItem(containerCollidables, collider);
            return;
        }

        var replacementCount = Math.Min(templateColliders.Count, convertedColliders.Count);
        for (var index = 0; index < replacementCount; index++)
        {
            var containerIndex = IndexOfReference(containerCollidables.Cast<object>().ToArray(), templateColliders[index]);
            if (containerIndex >= 0)
                SetListItem(containerCollidables, containerIndex, convertedColliders[index]);
            else
                AddListItem(containerCollidables, convertedColliders[index]);
        }

        // A source can use fewer colliders than its template. Remove only the
        // now-unreferenced tail shells; no other cloth can reference them here.
        for (var index = templateColliders.Count - 1; index >= replacementCount; index--)
        {
            var containerIndex = IndexOfReference(containerCollidables.Cast<object>().ToArray(), templateColliders[index]);
            if (containerIndex >= 0)
                containerCollidables.RemoveAt(containerIndex);
        }

        for (var index = replacementCount; index < convertedColliders.Count; index++)
            AddListItem(containerCollidables, convertedColliders[index]);
    }

    private static bool HasMatchingBphclTopologyTemplate(
        NativeBphclCloth source,
        NativeBphclSimCloth sourceSimulation,
        object template)
    {
        var buffers = GetList(GetValue(template, "bufferDefinitions")) ?? Array.Empty<object>();
        if (buffers.Count != source.BufferDefinitions.Count ||
            buffers.Where((buffer, index) => !string.Equals(buffer.GetType().Name, source.BufferDefinitions[index].ClassName, StringComparison.Ordinal)).Any())
            return false;

        var targetSimulation = GetFirst(GetValue(template, "simClothDatas"));
        if ((GetList(GetValue(targetSimulation, "triangleIndices"))?.Count ?? 0) / 3 != sourceSimulation.TriangleCount)
            return false;

        var targetOperators = GetList(GetValue(template, "operators")) ?? Array.Empty<object>();
        return targetOperators.Count == source.Operators.Count &&
               !targetOperators.Where((target, index) =>
                   !string.Equals(target.GetType().Name, source.Operators[index].ClassName, StringComparison.Ordinal)).Any();
    }

    private static void ValidateBphclTopologyTemplate(
        NativeBphclCloth source,
        NativeBphclSimCloth sourceSimulation,
        object template)
    {
        if (source.Operators.Any(@operator =>
                string.Equals(@operator.ClassName, "hclBoneSpaceSkinPOperator", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "This BPHCL cloth uses hclBoneSpaceSkinPOperator. PhysicsTool currently reconstructs the object-space skinning path only, so it will not export an incomplete HKCL conversion.");
        }

        var buffers = GetList(GetValue(template, "bufferDefinitions")) ?? Array.Empty<object>();
        if (buffers.Count != source.BufferDefinitions.Count)
            throw new InvalidOperationException(
                $"The HKCL template has {buffers.Count} buffers but the BPHCL source has {source.BufferDefinitions.Count}. Choose a same-layout template.");

        for (var index = 0; index < buffers.Count; index++)
        {
            if (!string.Equals(buffers[index].GetType().Name, source.BufferDefinitions[index].ClassName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"HKCL buffer {index} is {buffers[index].GetType().Name}, but the BPHCL source needs {source.BufferDefinitions[index].ClassName}. Choose a same-layout template.");
            }
        }

        var targetSimulation = GetFirst(GetValue(template, "simClothDatas"));
        var targetTriangleCount = (GetList(GetValue(targetSimulation, "triangleIndices"))?.Count ?? 0) / 3;
        if (targetTriangleCount != sourceSimulation.TriangleCount)
        {
            throw new InvalidOperationException(
                $"The HKCL template has {targetTriangleCount} virtual triangles but the BPHCL source has {sourceSimulation.TriangleCount}. Choose a same-layout template.");
        }

        var targetOperators = GetList(GetValue(template, "operators")) ?? Array.Empty<object>();
        if (targetOperators.Count != source.Operators.Count ||
            targetOperators.Where((target, index) => !string.Equals(target.GetType().Name, source.Operators[index].ClassName, StringComparison.Ordinal)).Any())
        {
            throw new InvalidOperationException(
                "The HKCL template does not use the same operator sequence as the BPHCL source. Choose a same-layout template.");
        }

        if (source.ObjectSpaceSkins.Count > 0 &&
            targetOperators.Count(op => string.Equals(op.GetType().Name, "hclObjectSpaceSkinPOperator", StringComparison.Ordinal)) != source.ObjectSpaceSkins.Count)
        {
            throw new InvalidOperationException(
                "The HKCL template does not provide the required object-space skinning operator shell.");
        }
    }

    private static IReadOnlyList<string> ApplyBphclBufferAndOperatorLayout(
        object cloth,
        NativeBphclCloth source,
        IReadOnlyDictionary<int, int> sourceToTargetBone,
        bool preserveTemplateTransformSetShape)
    {
        var targetBuffers = GetList(GetValue(cloth, "bufferDefinitions")) ?? Array.Empty<object>();
        for (var index = 0; index < targetBuffers.Count; index++)
        {
            var sourceBuffer = source.BufferDefinitions[index];
            var target = targetBuffers[index];
            // BotW's first scratch buffer uses the HKCL template's internal
            // cloth name. Link: belongs to actor parameters, not this name.
            var name = index == 0
                ? GetString(target, "name") ?? StripBphclPrefix(sourceBuffer.MeshName)
                : sourceBuffer.BufferName;
            SetValue(target, "name", name);
            SetValue(target, "type", sourceBuffer.Type);
            SetValue(target, "subType", sourceBuffer.SubType);
            SetValue(target, "numVertices", sourceBuffer.VertexCount);
            SetValue(target, "numTriangles", sourceBuffer.TriangleCount);
        }

        var targetSets = GetList(GetValue(cloth, "transformSetDefinitions")) ?? Array.Empty<object>();
        if (targetSets.Count != source.TransformSetDefinitions.Count)
            throw new InvalidOperationException("The HKCL template transform-set count changed during conversion.");
        for (var index = 0; index < targetSets.Count; index++)
        {
            var sourceSet = source.TransformSetDefinitions[index];
            SetValue(targetSets[index], "type", sourceSet.Type);
            if (!preserveTemplateTransformSetShape)
            {
                SetValue(targetSets[index], "name", StripBphclPrefix(sourceSet.Name));
                SetValue(targetSets[index], "numTransforms", sourceSet.TransformCount);
            }
        }

        var targetOperators = GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>();
        foreach (var sourceOperator in source.Operators)
        {
            var target = targetOperators[sourceOperator.Index];
            switch (sourceOperator.ClassName)
            {
                case "hclCopyVerticesOperator":
                    SetValue(target, "inputBufferIdx", sourceOperator.InputBufferIndex ?? 0);
                    SetValue(target, "outputBufferIdx", sourceOperator.OutputBufferIndex ?? 0);
                    SetValue(target, "numberOfVertices", sourceOperator.VertexCount ?? 0);
                    SetValue(target, "startVertexIn", sourceOperator.StartVertexIn ?? 0);
                    SetValue(target, "startVertexOut", sourceOperator.StartVertexOut ?? 0);
                    break;
                case "hclGatherAllVerticesOperator":
                    SetValue(target, "inputBufferIdx", sourceOperator.InputBufferIndex ?? 0);
                    SetValue(target, "outputBufferIdx", sourceOperator.OutputBufferIndex ?? 0);
                    ReplaceList(EnsureMutableObjectList(target, "vertexInputFromVertexOutput", "gather map"), sourceOperator.VertexInputFromVertexOutput);
                    break;
                case "hclMoveParticlesOperator":
                    ApplyBphclMoveParticlesOperator(target, sourceOperator);
                    break;
                case "hclSimulateOperator":
                    ApplyBphclSimulateOperator(target, sourceOperator);
                    break;
            }
        }

        foreach (var sourceSkin in source.ObjectSpaceSkins)
        {
            var target = targetOperators[sourceSkin.OperatorIndex];
            ApplyBphclObjectSpaceSkin(target, sourceSkin, sourceToTargetBone);
        }

        foreach (var sourceSkin in source.BoneSpaceSkins)
        {
            var target = targetOperators[sourceSkin.OperatorIndex];
            ApplyBphclBoneSpaceSkin(target, sourceSkin, sourceToTargetBone);
        }

        return new[]
        {
            $"Rebuilt {source.BufferDefinitions.Count} buffer definition(s), {source.TransformSetDefinitions.Count} transform set(s), {source.ObjectSpaceSkins.Count} object-space skinning operator(s), and {source.BoneSpaceSkins.Count} bone-space skinning operator(s)."
        };
    }

    private static void ApplyBphclMoveParticlesOperator(object target, NativeBphclOperatorLayout source)
    {
        SetValue(target, "simClothIndex", source.SimulationClothIndex ?? 0);
        SetValue(target, "refBufferIdx", source.ReferenceBufferIndex ?? 0);

        if (source.VertexParticlePairs.Count == 0)
            return;

        var pairs = EnsureMutableObjectList(target, "vertexParticlePairs", "vertex-to-particle anchors");
        var prototype = pairs.Count > 0 ? pairs[0] : null;
        var pairType = prototype?.GetType()
            ?? GetListElementType(pairs)
            ?? FindHavokType(target.GetType().Assembly, "hclMoveParticlesOperatorVertexParticlePair")
            ?? throw new InvalidOperationException(
                "PhysicsTool could not locate the HKX2 vertex-to-particle pair class.");

        pairs.Clear();
        foreach (var sourcePair in source.VertexParticlePairs)
        {
            var pair = prototype == null
                ? Activator.CreateInstance(pairType)
                : CloneForCurrentGraph(prototype);
            if (pair == null)
                throw new InvalidOperationException("PhysicsTool could not create a vertex-to-particle pair.");
            SetValue(pair, "vertexIndex", sourcePair.VertexIndex);
            SetValue(pair, "particleIndex", sourcePair.ParticleIndex);
            AddListItem(pairs, pair);
        }
    }

    private static void ApplyBphclSimulateOperator(object target, NativeBphclOperatorLayout source)
    {
        SetValue(target, "simClothIndex", source.SimulationClothIndex ?? 0);
        if (source.SimulateConfigs.Count == 0)
            return;

        // TotK stores one config per state; BotW's older generated class
        // exposes the active config directly on the operator. The template's
        // constraint-execution references remain intact, while these solver
        // controls are safe one-to-one values.
        var sourceConfig = source.SimulateConfigs[0];
        SetValue(target, "subSteps", sourceConfig.SubSteps);
        SetValue(target, "numberOfSolveIterations", sourceConfig.NumberOfSolveIterations);
        SetValue(target, "adaptConstraintStiffness", sourceConfig.AdaptConstraintStiffness);

        // The per-particle masks only say which local collider slots are
        // allowed. The simulate operator still decides whether those instance
        // collidables participate in the solve. Preserving this source flag
        // avoids generating HKCL files where colliders are present and masked
        // correctly, but the solver never selects them.
        SetValue(target, "useAllInstanceCollidables", sourceConfig.UseAllInstanceCollidables);
        if (GetValue(target, "instanceCollidablesUsed") is IList instanceCollidablesUsed)
            ReplaceList(instanceCollidablesUsed, sourceConfig.InstanceCollidablesUsed);
    }

    private static void ApplyBphclObjectSpaceSkin(
        object target,
        NativeBphclObjectSpaceSkin source,
        IReadOnlyDictionary<int, int> sourceToTargetBone)
    {
        ReplaceList(EnsureMutableObjectList(target, "boneFromSkinMeshTransforms", "bone-from-skin matrices"), source.BoneFromSkinMeshTransforms);
        ReplaceList(
            EnsureMutableObjectList(target, "transformSubset", "object-space transform subset"),
            source.TransformSubset.Select(sourceIndex =>
            {
                if (!sourceToTargetBone.TryGetValue(sourceIndex, out var targetIndex))
                    throw new InvalidOperationException($"The BPHCL object-space skin references source bone {sourceIndex}, which is absent from the HKCL skeleton shell.");
                return checked((ushort)targetIndex);
            }));
        SetValue(target, "outputBufferIndex", source.OutputBufferIndex);
        SetValue(target, "transformSetIndex", source.TransformSetIndex);

        var deformer = GetValue(target, "objectSpaceDeformer")
            ?? throw new InvalidOperationException("The HKCL object-space skin operator has no deformer.");
        ApplyBphclObjectSpaceDeformer(deformer, source.Deformer);
        SetValue(target, "objectSpaceDeformer", deformer);
        // TotK stores object-space positions as hkPackedVector3 values. BotW's
        // matching operator consumes unpacked Vector4 positions instead. The
        // fourth signed-short is an exponent, not a homogeneous W component.
        // Decode it into Vector4 values with W = 1 and leave localPs empty.
        ReplacePackedPositionBlocks(target, "localPs", "hclObjectSpaceDeformerLocalBlockP", Array.Empty<NativeBphclPackedPositionBlock>());
        var unpackedPositions = source.LocalUnpackedPs.Count > 0
            ? source.LocalUnpackedPs
            : UnpackBphclObjectSpacePositions(source.LocalPs);
        ReplacePositionBlocks(target, "localUnpackedPs", "hclObjectSpaceDeformerLocalBlockUnpackedP", unpackedPositions);
    }

    private static void ApplyBphclObjectSpaceDeformer(object target, NativeBphclObjectSpaceDeformer source)
    {
        var assembly = target.GetType().Assembly;
        ReplacePackedBlendBlocks(target, "eightBlendEntries", "hclObjectSpaceDeformerEightBlendEntryBlock", 8, source.EightBlendEntries, assembly);
        ReplacePackedBlendBlocks(target, "sevenBlendEntries", "hclObjectSpaceDeformerSevenBlendEntryBlock", 7, source.SevenBlendEntries, assembly);
        ReplacePackedBlendBlocks(target, "sixBlendEntries", "hclObjectSpaceDeformerSixBlendEntryBlock", 6, source.SixBlendEntries, assembly);
        ReplacePackedBlendBlocks(target, "fiveBlendEntries", "hclObjectSpaceDeformerFiveBlendEntryBlock", 5, source.FiveBlendEntries, assembly);
        ReplacePackedBlendBlocks(target, "fourBlendEntries", "hclObjectSpaceDeformerFourBlendEntryBlock", 4, source.FourBlendEntries, assembly);
        ReplacePackedBlendBlocks(target, "threeBlendEntries", "hclObjectSpaceDeformerThreeBlendEntryBlock", 3, source.ThreeBlendEntries, assembly);
        ReplacePackedBlendBlocks(target, "twoBlendEntries", "hclObjectSpaceDeformerTwoBlendEntryBlock", 2, source.TwoBlendEntries, assembly);
        ReplacePackedBlendBlocks(target, "oneBlendEntries", "hclObjectSpaceDeformerOneBlendEntryBlock", 1, source.OneBlendEntries, assembly);
        ReplaceList(EnsureMutableObjectList(target, "controlBytes", "object-space deformer controls"), source.ControlBytes);
        SetValue(target, "startVertexIndex", source.StartVertexIndex);
        SetValue(target, "endVertexIndex", source.EndVertexIndex);
        SetValue(target, "partialWrite", source.PartialWrite);
    }

    private static void ApplyBphclBoneSpaceSkin(
        object target,
        NativeBphclBoneSpaceSkin source,
        IReadOnlyDictionary<int, int> sourceToTargetBone)
    {
        ReplaceList(
            EnsureMutableObjectList(target, "transformSubset", "bone-space transform subset"),
            source.TransformSubset.Select(sourceIndex =>
            {
                if (!sourceToTargetBone.TryGetValue(sourceIndex, out var targetIndex))
                    throw new InvalidOperationException($"The BPHCL bone-space skin references source bone {sourceIndex}, which is absent from the HKCL skeleton shell.");
                return checked((ushort)targetIndex);
            }));
        SetValue(target, "outputBufferIndex", source.OutputBufferIndex);
        SetValue(target, "transformSetIndex", source.TransformSetIndex);

        var deformer = GetValue(target, "boneSpaceDeformer")
            ?? throw new InvalidOperationException("The HKCL bone-space skin operator has no deformer.");
        ApplyBphclBoneSpaceDeformer(deformer, source.Deformer);
        SetValue(target, "boneSpaceDeformer", deformer);

        // TotK's BoneSpace local position blocks are represented by the
        // packed-looking localPs member. BotW's matching operator consumes
        // those same Vector4 positions from localUnpackedPs instead. Leaving
        // them in localPs makes the cloth look plausible at rest while its
        // animation-to-simulation motion is wrong.
        var unpackedPositions = source.LocalUnpackedPs.Count > 0
            ? source.LocalUnpackedPs
            : source.LocalPs;
        ReplacePositionBlocks(target, "localPs", "hclBoneSpaceDeformerLocalBlockP", Array.Empty<NativeBphclPositionBlock>());
        ReplacePositionBlocks(target, "localUnpackedPs", "hclBoneSpaceDeformerLocalBlockUnpackedP", unpackedPositions);
    }

    private static void ApplyBphclBoneSpaceDeformer(object target, NativeBphclBoneSpaceDeformer source)
    {
        var assembly = target.GetType().Assembly;
        ReplaceBoneSpaceBlendBlocks(target, "fourBlendEntries", "hclBoneSpaceDeformerFourBlendEntryBlock", 4, 4, 16, source.FourBlendEntries, assembly);
        ReplaceBoneSpaceBlendBlocks(target, "threeBlendEntries", "hclBoneSpaceDeformerThreeBlendEntryBlock", 3, 5, 15, source.ThreeBlendEntries, assembly);
        ReplaceBoneSpaceBlendBlocks(target, "twoBlendEntries", "hclBoneSpaceDeformerTwoBlendEntryBlock", 2, 8, 16, source.TwoBlendEntries, assembly);
        ReplaceBoneSpaceBlendBlocks(target, "oneBlendEntries", "hclBoneSpaceDeformerOneBlendEntryBlock", 1, 16, 16, source.OneBlendEntries, assembly);
        ReplaceList(EnsureMutableObjectList(target, "controlBytes", "bone-space deformer controls"), source.ControlBytes);
        SetValue(target, "startVertexIndex", source.StartVertexIndex);
        SetValue(target, "endVertexIndex", source.EndVertexIndex);
        SetValue(target, "batchSizeSpu", source.BatchSizeSpu == 0 ? (ushort)512 : source.BatchSizeSpu);
        SetValue(target, "partialWrite", source.PartialWrite);
    }

    private static void ReplaceBoneSpaceBlendBlocks(
        object target,
        string memberName,
        string typeName,
        int blendCount,
        int vertexCount,
        int boneCount,
        IReadOnlyList<NativeBphclPackedArray> sourceArrays,
        Assembly assembly)
    {
        var targetBlocks = EnsureMutableObjectList(target, memberName, memberName);
        targetBlocks.Clear();
        var type = FindHavokType(assembly, typeName)
            ?? throw new InvalidOperationException($"PhysicsTool could not locate {typeName}.");
        var requiredBytes = checked((vertexCount + boneCount) * sizeof(ushort));

        foreach (var source in sourceArrays)
        {
            if (source.ElementSize < requiredBytes)
            {
                throw new InvalidDataException(
                    $"{source.ElementType} is {source.ElementSize} bytes, but {typeName} requires at least {requiredBytes} bytes.");
            }

            for (var blockIndex = 0; blockIndex < source.Count; blockIndex++)
            {
                var block = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException($"PhysicsTool could not create {typeName}.");
                var blockOffset = checked(blockIndex * source.ElementSize);
                for (var index = 0; index < vertexCount; index++)
                    SetValue(block, $"vertexIndices_{index}", ReadUInt16(source.Bytes, blockOffset + index * sizeof(ushort)));

                var boneOffset = blockOffset + vertexCount * sizeof(ushort);
                for (var index = 0; index < boneCount; index++)
                    SetValue(block, $"boneIndices_{index}", ReadUInt16(source.Bytes, boneOffset + index * sizeof(ushort)));
                AddListItem(targetBlocks, block);
            }
        }
    }

    private static void ReplacePackedBlendBlocks(
        object target,
        string memberName,
        string typeName,
        int blendCount,
        IReadOnlyList<NativeBphclPackedArray> sourceArrays,
        Assembly assembly)
    {
        var targetBlocks = EnsureMutableObjectList(target, memberName, memberName);
        targetBlocks.Clear();
        var type = FindHavokType(assembly, typeName)
            ?? throw new InvalidOperationException($"PhysicsTool could not locate {typeName}.");

        foreach (var source in sourceArrays)
        {
            for (var blockIndex = 0; blockIndex < source.Count; blockIndex++)
            {
                var block = Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException($"PhysicsTool could not create {typeName}.");
                var blockOffset = checked(blockIndex * source.ElementSize);
                for (var index = 0; index < 16; index++)
                    SetValue(block, $"vertexIndices_{index}", ReadUInt16(source.Bytes, blockOffset + index * 2));

                var boneOffset = blockOffset + 32;
                for (var index = 0; index < blendCount * 16; index++)
                    SetValue(block, $"boneIndices_{index}", ReadUInt16(source.Bytes, boneOffset + index * 2));

                // One-blend entries only store a vertex index and one bone
                // index per vertex. Weight bytes start with the two-blend
                // block type, where each vertex needs an actual blend ratio.
                if (blendCount > 1)
                {
                    var weightOffset = boneOffset + blendCount * 32;
                    for (var index = 0; index < blendCount * 16; index++)
                        SetValue(block, $"boneWeights_{index}", source.Bytes[weightOffset + index]);
                }
                AddListItem(targetBlocks, block);
            }
        }
    }

    private static void ReplacePositionBlocks(
        object target,
        string memberName,
        string typeName,
        IReadOnlyList<NativeBphclPositionBlock> sourceBlocks)
    {
        var targetBlocks = EnsureMutableObjectList(target, memberName, memberName);
        targetBlocks.Clear();
        if (sourceBlocks.Count == 0)
            return;
        var type = FindHavokType(target.GetType().Assembly, typeName)
            ?? throw new InvalidOperationException($"PhysicsTool could not locate {typeName}.");
        foreach (var source in sourceBlocks)
        {
            var block = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"PhysicsTool could not create {typeName}.");
            for (var index = 0; index < source.Positions.Count; index++)
                SetValue(block, $"localPosition_{index}", source.Positions[index]);
            AddListItem(targetBlocks, block);
        }
    }

    private static void ReplacePackedPositionBlocks(
        object target,
        string memberName,
        string typeName,
        IReadOnlyList<NativeBphclPackedPositionBlock> sourceBlocks)
    {
        var targetBlocks = EnsureMutableObjectList(target, memberName, memberName);
        targetBlocks.Clear();
        if (sourceBlocks.Count == 0)
            return;
        var type = FindHavokType(target.GetType().Assembly, typeName)
            ?? throw new InvalidOperationException($"PhysicsTool could not locate {typeName}.");
        foreach (var source in sourceBlocks)
        {
            var block = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"PhysicsTool could not create {typeName}.");
            for (var index = 0; index < source.Positions.Count; index++)
                SetValue(block, $"localPosition_{index}", source.Positions[index]);
            AddListItem(targetBlocks, block);
        }
    }

    private static IReadOnlyList<NativeBphclPositionBlock> UnpackBphclObjectSpacePositions(
        IReadOnlyList<NativeBphclPackedPositionBlock> sourceBlocks)
    {
        const int packedComponentsPerPosition = 4;
        const int positionsPerBlock = 16;

        var unpackedBlocks = new List<NativeBphclPositionBlock>(sourceBlocks.Count);
        foreach (var source in sourceBlocks)
        {
            if (source.Positions.Count != positionsPerBlock * packedComponentsPerPosition)
            {
                throw new InvalidDataException(
                    $"BPHCL packed object-space position block has {source.Positions.Count} values; expected {positionsPerBlock * packedComponentsPerPosition}.");
            }

            var positions = new Vector4[positionsPerBlock];
            for (var positionIndex = 0; positionIndex < positionsPerBlock; positionIndex++)
            {
                var offset = positionIndex * packedComponentsPerPosition;
                // hkPackedVector3 stores three signed mantissas followed by a
                // fixed-point exponent. Paired vanilla BotW/TotK cloths show:
                // value = mantissa * 2^(exponent / 128 - 111).
                var scale = MathF.Pow(2f, source.Positions[offset + 3] / 128f - 111f);
                positions[positionIndex] = new Vector4(
                    source.Positions[offset] * scale,
                    source.Positions[offset + 1] * scale,
                    source.Positions[offset + 2] * scale,
                    1f);
            }

            unpackedBlocks.Add(new NativeBphclPositionBlock(positions));
        }

        return unpackedBlocks;
    }

    private static void ReplaceList(IList target, IEnumerable source)
    {
        target.Clear();
        foreach (var item in source)
            AddListItem(target, item);
    }

    private static ushort ReadUInt16(IReadOnlyList<byte> bytes, int offset)
    {
        if (offset < 0 || offset + 2 > bytes.Count)
            throw new InvalidDataException("BPHCL packed blend block ended unexpectedly.");
        return BinaryPrimitives.ReadUInt16LittleEndian(new[] { bytes[offset], bytes[offset + 1] });
    }

    private static void ValidateBphclConstraintTemplate(NativeBphclSimCloth source, object template)
    {
        var targetSimulation = GetFirst(GetValue(template, "simClothDatas"));
        if (GetValue(targetSimulation, "staticConstraintSets") is not IList &&
            GetValue(template, "constraintSets") is not IList)
        {
            throw new InvalidOperationException(
                "The HKCL template has no writable constraint-set collection. It cannot host imported BPHCL constraints.");
        }
    }

    private static IReadOnlyDictionary<int, int> BuildBphclBoneMap(
        NativeBphclSkeleton source,
        IReadOnlyList<object> templateBones)
    {
        var targetByName = templateBones
            .Select((bone, index) => (Name: StripBphclPrefix(GetString(bone, "name") ?? string.Empty), Index: index))
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<int, int>();
        var missing = new List<string>();

        foreach (var bone in source.Bones)
        {
            var name = StripBphclPrefix(bone.Name);
            if (!targetByName.TryGetValue(name, out var targetIndex))
            {
                missing.Add(name);
                continue;
            }
            map.Add(bone.Index, targetIndex);
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The HKCL template is missing {missing.Count} BPHCL skeleton bone name(s): {string.Join(", ", missing.Take(8))}. Choose a closer template.");
        }

        return map;
    }

    private static void ApplyBphclSkeleton(
        object skeleton,
        NativeBphclSkeleton source,
        IReadOnlyDictionary<int, int> sourceToTargetBone)
    {
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetValue(skeleton, "parentIndices") as IList;
        var poses = GetValue(skeleton, "referencePose") as IList;

        foreach (var sourceBone in source.Bones)
        {
            var targetIndex = sourceToTargetBone[sourceBone.Index];
            SetValue(bones[targetIndex], "name", StripBphclPrefix(sourceBone.Name));
            SetValue(bones[targetIndex], "lockTranslation", sourceBone.LockTranslation);
            if (parents != null)
            {
                var targetParent = sourceBone.ParentIndex < 0
                    ? -1
                    : sourceToTargetBone[sourceBone.ParentIndex];
                SetListItem(parents, targetIndex, targetParent);
            }

            if (poses != null && targetIndex < poses.Count)
            {
                var existing = poses[targetIndex] is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
                SetListItem(poses, targetIndex, new Matrix4x4(
                    sourceBone.Translation.X, sourceBone.Translation.Y, sourceBone.Translation.Z, existing.M14,
                    sourceBone.Rotation.X, sourceBone.Rotation.Y, sourceBone.Rotation.Z, sourceBone.Rotation.W,
                    existing.M31, existing.M32, existing.M33, existing.M34,
                    existing.M41, existing.M42, existing.M43, existing.M44));
            }
        }
    }

    private static void RebuildBphclParticleTopology(object cloth, NativeBphclSimCloth source)
    {
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"))
            ?? throw new InvalidOperationException("The HKCL template has no simulation data to rebuild.");
        var particles = GetMutableList(GetValue(simulation, "particleDatas"), "particleDatas");
        var previousParticleCount = particles.Count;
        ResizeObjectList(particles, source.Particles.Count, "particleDatas");

        var pose = GetFirst(GetValue(simulation, "simClothPoses"));
        ResizeParallelList(GetValue(pose, "positions") as IList, source.Particles.Count, Vector4.Zero);
        ResizeParallelList(GetValue(simulation, "staticCollisionMasks") as IList, source.Particles.Count, 0u);
        ResizeParallelList(GetValue(simulation, "perParticlePinchDetectionEnabledFlags") as IList, source.Particles.Count, false);

        // These arrays are per-particle only when they matched the original
        // template count. Leave unrelated arrays intact rather than guessing
        // their semantics from their length alone.
        var pinchingData = GetValue(simulation, "collidablePinchingDatas") as IList;
        if (pinchingData?.Count == previousParticleCount)
            ResizeParallelList(pinchingData, source.Particles.Count, pinchingData.Count > 0 ? pinchingData[0] : null);

        var triangleIndices = GetValue(simulation, "triangleIndices") as IList
            ?? throw new InvalidOperationException("The HKCL template has no writable triangle-index list.");
        triangleIndices.Clear();
        foreach (var index in source.TriangleIndices)
            AddListItem(triangleIndices, index);

        var triangleFlips = GetValue(simulation, "triangleFlips") as IList;
        triangleFlips?.Clear();
        if (triangleFlips != null)
        {
            foreach (var flip in source.TriangleFlips)
                AddListItem(triangleFlips, flip);
        }
    }

    private static IReadOnlyDictionary<int, int> PrepareBphclSkeletonShell(
        object skeleton,
        NativeBphclSkeleton source)
    {
        var bones = GetMutableList(GetValue(skeleton, "bones"), "bones");
        var parents = GetValue(skeleton, "parentIndices") as IList
            ?? throw new InvalidOperationException("The HKCL template skeleton has no editable parent list.");
        var poses = GetValue(skeleton, "referencePose") as IList
            ?? throw new InvalidOperationException("The HKCL template skeleton has no editable reference-pose list.");
        if (bones.Count == 0)
            throw new InvalidOperationException("The HKCL template skeleton has no bone entry to clone.");

        // BPHCL owns this cloth skeleton. Its count must match exactly: an
        // extra template bone leaves Havok with a different transform layout
        // than the source transform-set and deformer data expects.
        var shellCount = source.Bones.Count;
        ResizeObjectList(bones, shellCount, "bones");
        ResizeParallelList(parents, shellCount, -1);
        ResizeParallelList(poses, shellCount, Matrix4x4.Identity);

        return source.Bones.ToDictionary(bone => bone.Index, bone => bone.Index);
    }

    private static void ResizeObjectList(IList list, int count, string displayName)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (list.Count == 0 && count > 0)
            throw new InvalidOperationException($"The HKCL template has no {displayName} entry to clone.");

        while (list.Count > count)
            list.RemoveAt(list.Count - 1);
        while (list.Count < count)
            list.Add(CloneForCurrentGraph(list[list.Count - 1]!));
    }

    private static void ResizeParallelList(IList? list, int count, object? defaultValue)
    {
        if (list == null)
            return;

        while (list.Count > count)
            list.RemoveAt(list.Count - 1);
        while (list.Count < count)
            AddListItem(list, defaultValue);
    }

    private static void ApplyBphclParticles(
        object cloth,
        NativeBphclSimCloth source,
        float solverScale,
        bool preserveSourceLandscapeCollisionBit)
    {
        var rows = source.Particles.Select((particle, index) =>
        {
            // Havok cloth authors particle mass; inverse mass is derived
            // solver data. Recalculate it after applying the BotW scale
            // instead of carrying a TotK cached inverse-mass value across.
            var mass = particle.Fixed ? 0.0f : particle.Mass * solverScale;
            var inverseMass = mass > float.Epsilon ? 1.0f / mass : 0.0f;

            return new ParticleEditRow
            {
                Index = index,
                Fixed = particle.Fixed,
                X = particle.Position.X,
                Y = particle.Position.Y,
                Z = particle.Position.Z,
                W = particle.Position.W,

                Mass = mass,
                InverseMass = inverseMass,

                Radius = particle.Radius,
                Friction = particle.Friction,
                CollisionMask = GetBphclCollisionMask(source, particle, index, preserveSourceLandscapeCollisionBit)
            };
        });

        ApplyParticleRows(cloth, rows);
    }

    private static uint GetBphclCollisionMask(
        NativeBphclSimCloth source,
        NativeBphclParticle particle,
        int particleIndex,
        bool preserveSourceLandscapeCollisionBit)
    {
        const uint landscapeCollisionBit = 0x80000000u;
        if (particleIndex >= 0 && particleIndex < source.StaticCollisionMasks.Count)
        {
            // TotK marks world/landscape collision in bit 31. BotW HKCL
            // particle masks use the low per-instance collider bits here; the
            // export path clears bit 31 while native BPHCL inspection may keep it.
            var sourceMask = source.StaticCollisionMasks[particleIndex];
            return preserveSourceLandscapeCollisionBit
                ? sourceMask
                : sourceMask & ~landscapeCollisionBit;
        }

        // An absent source array is not a reason to borrow the template's
        // masks. Fixed particles normally have no collision bits; dynamic
        // particles fall back to every collider assigned to this simulation.
        if (particle.Fixed)
            return 0u;

        var colliderCount = source.CollidableItemIndices.Count;
        var fallbackMask = colliderCount switch
        {
            <= 0 => 0u,
            >= 32 => uint.MaxValue,
            _ => (1u << colliderCount) - 1u
        };
        return preserveSourceLandscapeCollisionBit && source.Runtime.LandscapeCollisionEnabled
            ? fallbackMask | landscapeCollisionBit
            : fallbackMask;
    }

    private static void ApplyBphclSimulationSettings(object cloth, NativeBphclSimCloth source)
    {
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"))
            ?? throw new InvalidOperationException("The HKCL template has no simulation data.");
        var simulationInfo = GetValue(simulation, "simulationInfo") ?? simulation;

        // These are direct equivalents in both formats. Earlier conversion
        // passes left the BotW template's gravity and damping in place, which
        // made the imported particle constraints run under mixed settings.
        SetValue(simulationInfo, "gravity", source.SimulationInfo.Gravity);
        SetValue(simulationInfo, "globalDampingPerSecond", source.SimulationInfo.GlobalDampingPerSecond);

        // These fields are stored on hclSimClothData itself. Newer BPHCL
        // files additionally expose simOpIds; retain that list whenever the
        // older HKCL class version has a matching field.
        SetValue(simulation, "doNormals", source.Runtime.DoNormals);
        if (GetValue(simulation, "simOpIds") is IList simulationOperatorIds)
            ReplaceList(simulationOperatorIds, source.SimulationOperatorIds);
        SetValue(simulation, "transferMotionEnabled", source.Runtime.TransferMotionEnabled);
        // BotW does not retain TotK's landscape collision bit in the particle
        // masks, but the source count still sizes a collision-runtime work
        // array. Zeroing it made ordinary per-instance capsules inert even
        // though their own mask bits and transforms were valid. Keep the
        // source count while allowing the BotW class version to ignore the
        // unsupported landscape-enabled flag.
        // TotK marks these runtime cloths as landscape-enabled, but BotW vanilla HKCL keeps this flag off while retaining the per-particle landscape work-array count. Leaving it on makes some converted cloths run through the wrong collision path.\r\n        SetValue(simulationInfo, "landscapeCollisionEnabled", false);
        SetValue(simulation, "numLandscapeCollidableParticles", source.Runtime.NumLandscapeCollidableParticles);
        SetValue(simulation, "pinchDetectionEnabled", source.Runtime.PinchDetectionEnabled);
        SetValue(simulation, "minPinchedParticleIndex", source.Runtime.MinPinchedParticleIndex);
        SetValue(simulation, "maxPinchedParticleIndex", source.Runtime.MaxPinchedParticleIndex);
        // The working BotW baseline retains the source value (normally zero).
        // Treating this as a preallocated contact-pair count was an unproven
        // conversion experiment and made the generated runtime diverge from
        // the known-good HKCL collision layout.
        SetValue(simulation, "maxCollisionPairs", source.Runtime.MaxCollisionPairs);

        var transfer = GetValue(simulation, "transferMotionData");
        if (transfer != null)
        {
            var sourceTransfer = source.Runtime.TransferMotionData;
            SetValue(transfer, "transformSetIndex", sourceTransfer.TransformSetIndex);
            SetValue(transfer, "transformIndex", sourceTransfer.TransformIndex);
            SetValue(transfer, "transferTranslationMotion", sourceTransfer.TransferTranslationMotion);
            SetValue(transfer, "minTranslationSpeed", sourceTransfer.MinTranslationSpeed);
            SetValue(transfer, "maxTranslationSpeed", sourceTransfer.MaxTranslationSpeed);
            SetValue(transfer, "minTranslationBlend", sourceTransfer.MinTranslationBlend);
            SetValue(transfer, "maxTranslationBlend", sourceTransfer.MaxTranslationBlend);
            SetValue(transfer, "transferRotationMotion", sourceTransfer.TransferRotationMotion);
            SetValue(transfer, "minRotationSpeed", sourceTransfer.MinRotationSpeed);
            SetValue(transfer, "maxRotationSpeed", sourceTransfer.MaxRotationSpeed);
            SetValue(transfer, "minRotationBlend", sourceTransfer.MinRotationBlend);
            SetValue(transfer, "maxRotationBlend", sourceTransfer.MaxRotationBlend);
        }

        var landscape = GetValue(simulation, "landscapeCollisionData");
        if (landscape != null)
        {
            var sourceLandscape = source.Runtime.LandscapeCollisionData;
            SetValue(landscape, "landscapeRadius", sourceLandscape.LandscapeRadius);
            SetValue(landscape, "enableStuckParticleDetection", sourceLandscape.EnableStuckParticleDetection);
            SetValue(landscape, "stuckParticlesStretchFactorSq", sourceLandscape.StuckParticlesStretchFactorSq);
            SetValue(landscape, "pinchDetectionEnabled", sourceLandscape.PinchDetectionEnabled);
            SetValue(landscape, "pinchDetectionPriority", sourceLandscape.PinchDetectionPriority);
            SetValue(landscape, "pinchDetectionRadius", sourceLandscape.PinchDetectionRadius);
            SetValue(landscape, "collisionTolerance", sourceLandscape.CollisionTolerance);
        }

        var pinchFlags = EnsureMutableObjectList(
            simulation,
            "perParticlePinchDetectionEnabledFlags",
            "per-particle pinch flags");
        ReplaceList(pinchFlags, source.Runtime.PerParticlePinchDetectionEnabledFlags);

        var pinchingData = EnsureMutableObjectList(
            simulation,
            "collidablePinchingDatas",
            "collider pinching data");
        pinchingData.Clear();
        foreach (var sourcePinchingData in source.Runtime.CollidablePinchingData)
        {
            pinchingData.Add(new hclSimClothDataCollidablePinchingData
            {
                m_pinchDetectionEnabled = sourcePinchingData.Enabled,
                m_pinchDetectionPriority = sourcePinchingData.Priority,
                m_pinchDetectionRadius = sourcePinchingData.Radius
            });
        }
    }

    private static IReadOnlyList<object> CreateBphclColliders(
        object cloth,
        IReadOnlyList<object> colliderTemplates,
        IReadOnlyList<NativeBphclCollider> sourceColliders,
        NativeBphclSkeleton skeleton,
        object targetSkeleton,
        IReadOnlyDictionary<int, int> sourceToTargetBone,
        IReadOnlyList<int>? activeColliderItemIndices = null)
    {
        var references = GetPrimaryMutableCollidableReferences(cloth);
        references.Clear();
        var result = new List<object>(sourceColliders.Count);
        var collidersBySourceItem = new Dictionary<int, object>();

        for (var index = 0; index < sourceColliders.Count; index++)
        {
            var source = sourceColliders[index];
            var target = colliderTemplates.Count > 0
                ? CloneForCurrentGraph(colliderTemplates[index % colliderTemplates.Count])
                : new hclCollidable();
            var targetShape = CreateHkclColliderShape(target, source.Shape);

            var worldTransform = new Matrix4x4(
                source.AxisX.X, source.AxisX.Y, source.AxisX.Z, source.AxisX.W,
                source.AxisY.X, source.AxisY.Y, source.AxisY.Z, source.AxisY.W,
                source.AxisZ.X, source.AxisZ.Y, source.AxisZ.Z, source.AxisZ.W,
                source.Translation.X, source.Translation.Y, source.Translation.Z, source.Translation.W);

            SetValue(target, "name", StripBphclPrefix(source.Name));
            SetValue(target, "shape", targetShape);
            ApplyBphclColliderShape(targetShape, source.Shape);
            SetValue(target, "pinchDetectionRadius", source.PinchDetectionRadius);
            SetValue(target, "pinchDetectionPriority", source.PinchDetectionPriority);
            SetValue(target, "pinchDetectionEnabled", source.PinchDetectionEnabled);

            var sourceBoneIndex = ResolveColliderBoneIndex(StripBphclPrefix(source.Name), skeleton.Bones);
            if (sourceBoneIndex >= 0 && sourceToTargetBone.TryGetValue(sourceBoneIndex, out var targetBoneIndex))
            {
                // BPHCL exposes a collider rest transform in model space. Keep
                // its authored offset from the source bone, then reapply that
                // offset to the matching HKCL bone. This preserves the collider
                // in the same rest-pose location while giving HKCL a valid bone
                // binding for animated updates.
                var sourceBoneWorld = GetBphclBoneWorldTransform(skeleton, sourceBoneIndex);
                var targetBoneWorld = GetHkclBoneWorldTransform(targetSkeleton, targetBoneIndex);
                if (!Matrix4x4.Invert(sourceBoneWorld, out var sourceBoneInverse))
                    throw new InvalidOperationException($"Could not invert source bone {sourceBoneIndex} while baking collider {source.Name}.");

                var localToBone = worldTransform * sourceBoneInverse;
                worldTransform = localToBone * targetBoneWorld;
                SetValue(target, "transformIndex", targetBoneIndex);
            }
            SetValue(target, "transform", worldTransform);

            result.Add(target);
            collidersBySourceItem.Add(source.ItemIndex, target);
        }

        // The transform map addresses the per-instance collider list by its
        // own reference order, not by the global collidable-container order.
        // Those orders happen to agree in simple files, but diverge in larger
        // hair setups with shared capsules and head planes.
        var referenceOrder = activeColliderItemIndices ?? sourceColliders
            .Select(collider => collider.ItemIndex)
            .ToArray();
        foreach (var itemIndex in referenceOrder)
        {
            if (!collidersBySourceItem.TryGetValue(itemIndex, out var target))
            {
                throw new InvalidOperationException(
                    $"The BPHCL simulation references collider ITEM {itemIndex}, but it was not available for conversion.");
            }
            AddListItem(references, target);
        }

        return result;
    }

    private static IReadOnlyList<object> OrderConvertedCollidersBySourceItems(
        IReadOnlyList<NativeBphclCollider> sourceColliders,
        IReadOnlyList<object> convertedColliders,
        IReadOnlyList<int> sourceItemIndices)
    {
        if (sourceColliders.Count != convertedColliders.Count)
        {
            throw new InvalidOperationException(
                "PhysicsTool could not preserve BPHCL collider slot order because the converted collider count changed.");
        }

        var convertedBySourceItem = sourceColliders
            .Select((collider, index) => new { collider.ItemIndex, Collider = convertedColliders[index] })
            .ToDictionary(entry => entry.ItemIndex, entry => entry.Collider);

        return sourceItemIndices.Select(itemIndex =>
        {
            if (!convertedBySourceItem.TryGetValue(itemIndex, out var collider))
            {
                throw new InvalidOperationException(
                    $"The BPHCL simulation references collider ITEM {itemIndex}, but it was not converted.");
            }
            return collider;
        }).ToArray();
    }

    /// <summary>
    /// Returns collider choices in the exact per-cloth order used by a
    /// particle's static collision mask. Bit 31 is reserved by Havok for
    /// landscape collision, so normal collider choices use bits 0 through 30.
    /// </summary>
    public IReadOnlyList<ParticleColliderOption> GetParticleColliderOptions(int clothIndex)
    {
        if (IsHelperBoneSidecar)
            return Array.Empty<ParticleColliderOption>();

        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var bphclSimulation = bphcl.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths.FirstOrDefault();
            if (bphclSimulation == null)
                return Array.Empty<ParticleColliderOption>();

            return bphclSimulation.CollidableItemIndices
                .Take(31)
                .Select((itemIndex, bitIndex) =>
                {
                    var collider = bphcl.Colliders.FirstOrDefault(value => value.ItemIndex == itemIndex);
                    return new ParticleColliderOption(
                        bitIndex,
                        collider?.Index ?? -1,
                        collider?.Name ?? $"Collider item {itemIndex}");
                })
                .ToArray();
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        if (cloth == null)
            return Array.Empty<ParticleColliderOption>();

        var hkclSimulation = GetFirst(GetValue(cloth, "simClothDatas"));
        var references = GetList(GetValue(hkclSimulation, "perInstanceCollidables"))
            ?? GetList(GetValue(cloth, "perInstanceCollidables"))
            ?? Array.Empty<object>();
        var outerColliders = GetCollidables(root);

        return references
            .Take(31)
            .Select((collider, bitIndex) =>
            {
                var colliderIndex = IndexOfReference(outerColliders, collider);
                return new ParticleColliderOption(
                    bitIndex,
                    colliderIndex,
                    GetString(collider, "name") ?? $"Collider {colliderIndex}");
            })
            .ToArray();
    }

    /// <summary>
    /// Returns the collision-related simulation state without applying any
    /// conversion. This is intentionally diagnostic-only: it lets us compare
    /// a TotK source, a converted file, and a vanilla BotW file field by
    /// field when collision behaviour differs in-game.
    /// </summary>
    public IReadOnlyList<string> GetCollisionDiagnostics(int clothIndex)
    {
        if (_bphcl?.NativeDocument is { } bphcl)
        {
            var sourceCloth = bphcl.Cloths.ElementAtOrDefault(clothIndex);
            var sourceSimulation = sourceCloth?.SimCloths.FirstOrDefault();
            if (sourceCloth == null || sourceSimulation == null)
                return Array.Empty<string>();

            var sourceColliderNames = sourceSimulation.CollidableItemIndices
                .Select(itemIndex => bphcl.Colliders.FirstOrDefault(collider => collider.ItemIndex == itemIndex))
                .Select((collider, slot) => collider == null
                    ? $"slot={slot}:missing"
                    : $"slot={slot}:{collider.Name}|pinch={collider.PinchDetectionEnabled},{collider.PinchDetectionPriority},{collider.PinchDetectionRadius:G8}")
                .ToArray();

            return new[]
            {
                "format=BPHCL",
                $"cloth={sourceCloth.Name}",
                $"colliders=[{string.Join(';', sourceColliderNames)}]",
                $"masks=[{string.Join(',', sourceSimulation.StaticCollisionMasks.Select(mask => $"0x{mask:X8}"))}]",
                $"particlePinch=[{string.Join(',', sourceSimulation.Runtime.PerParticlePinchDetectionEnabledFlags.Select(value => value ? '1' : '0'))}]",
                $"colliderPinch=[{string.Join(';', sourceSimulation.Runtime.CollidablePinchingData.Select(value => $"{value.Enabled},{value.Priority},{value.Radius:G8}"))}]",
                $"pinch={sourceSimulation.Runtime.PinchDetectionEnabled}|range={sourceSimulation.Runtime.MinPinchedParticleIndex}-{sourceSimulation.Runtime.MaxPinchedParticleIndex}|pairs={sourceSimulation.Runtime.MaxCollisionPairs}",
                $"landscape={sourceSimulation.Runtime.LandscapeCollisionEnabled}|particles={sourceSimulation.Runtime.NumLandscapeCollidableParticles}"
            };
        }

        var root = RequireRoot();
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        var simulation = cloth == null ? null : GetFirst(GetValue(cloth, "simClothDatas"));
        if (simulation == null)
            return Array.Empty<string>();

        var references = GetList(GetValue(simulation, "perInstanceCollidables")) ?? Array.Empty<object>();
        var colliders = GetCollidables(root);
        var colliderNames = references.Select((collider, slot) =>
        {
            var outerIndex = IndexOfReference(colliders, collider);
            return $"slot={slot}:#{outerIndex}:{GetString(collider, "name") ?? "unnamed"}|pinch=" +
                   $"{GetValue(collider, "pinchDetectionEnabled")},{GetValue(collider, "pinchDetectionPriority")},{GetValue(collider, "pinchDetectionRadius")}";
        }).ToArray();
        var masks = GetList(GetValue(simulation, "staticCollisionMasks")) ?? Array.Empty<object>();
        var particlePinch = GetList(GetValue(simulation, "perParticlePinchDetectionEnabledFlags")) ?? Array.Empty<object>();
        var colliderPinch = GetList(GetValue(simulation, "collidablePinchingDatas")) ?? Array.Empty<object>();
        var simulationInfo = GetValue(simulation, "simulationInfo") ?? simulation;

        return new[]
        {
            "format=HKCL",
            $"cloth={GetString(cloth, "name")}",
            $"colliders=[{string.Join(';', colliderNames)}]",
            $"masks=[{string.Join(',', masks.Select(value => $"0x{ToUInt32(value, 0u):X8}"))}]",
            $"particlePinch=[{string.Join(',', particlePinch.Select(value => Convert.ToBoolean(value, CultureInfo.InvariantCulture) ? '1' : '0'))}]",
            $"colliderPinch=[{string.Join(';', colliderPinch.Select(value => $"{GetValue(value, "pinchDetectionEnabled")},{GetValue(value, "pinchDetectionPriority")},{GetValue(value, "pinchDetectionRadius")}"))}]",
            $"pinch={GetValue(simulationInfo, "pinchDetectionEnabled")}|range={GetValue(simulation, "minPinchedParticleIndex")}-{GetValue(simulation, "maxPinchedParticleIndex")}|pairs={GetValue(simulation, "maxCollisionPairs")}",
            $"landscape={GetValue(simulationInfo, "landscapeCollisionEnabled")}|particles={GetValue(simulation, "numLandscapeCollidableParticles")}"
        };
    }

    private static string ApplyBphclCollidableTransformMap(
        object cloth,
        NativeBphclCollidableTransformMap source,
        int sourceColliderCount,
        IReadOnlyDictionary<int, int> sourceToTargetBone,
        object? targetSkeleton = null,
        IReadOnlyList<object>? targetColliders = null)
    {
        var simulation = (GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>()).FirstOrDefault()
            ?? throw new InvalidOperationException("The HKCL template has no simulation cloth for the collider transform map.");
        var targetMap = GetValue(simulation, "collidableTransformMap")
            ?? throw new InvalidOperationException("The HKCL template has no collidable transform map.");

        if (source.TransformIndices.Count != sourceColliderCount || source.Offsets.Count != sourceColliderCount)
        {
            throw new InvalidOperationException(
                $"BPHCL collider transform-map count does not match its active colliders " +
                $"({source.TransformIndices.Count} indices, {source.Offsets.Count} offsets, {sourceColliderCount} colliders).");
        }

        var targetIndices = source.TransformIndices.Select(sourceIndex =>
        {
            if (sourceIndex > int.MaxValue || !sourceToTargetBone.TryGetValue((int)sourceIndex, out var targetIndex))
            {
                throw new InvalidOperationException(
                    $"The BPHCL collider transform map references source bone {sourceIndex}, which is not present in the HKCL template mapping.");
            }
            return targetIndex;
        }).ToArray();

        if (!SetValue(targetMap, "transformSetIndex", source.TransformSetIndex))
            throw new InvalidOperationException("PhysicsTool could not set the HKCL collider transform-set index.");
        ReplaceList(EnsureMutableObjectList(targetMap, "transformIndices", "collider transform indices"), targetIndices);

        // BPHCL already stores these offsets in the cloth's collider-transform-map space.
        // Rebuilding them from the converted world transforms drifts from known-good BotW
        // conversions and breaks otherwise valid collider behavior.
        ReplaceList(EnsureMutableObjectList(targetMap, "offsets", "collider transform offsets"), source.Offsets);

        return $"Copied the {sourceColliderCount}-entry BPHCL collider transform map and remapped it to the HKCL skeleton.";
    }

    private (Matrix4x4 Transform, int BoneIndex, bool HasMapEntry) GetCollidableRuntimeBinding(
        hkRootLevelContainer root,
        int clothIndex,
        object collidable,
        Matrix4x4 fallbackTransform)
    {
        var cloth = GetClothDatas(root).ElementAtOrDefault(clothIndex);
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex);
        if (cloth == null || skeleton == null)
            return (fallbackTransform, -1, false);

        foreach (var simulation in GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
        {
            var references = GetList(GetValue(simulation, "perInstanceCollidables")) ?? Array.Empty<object>();
            var slot = IndexOfReference(references, collidable);
            var map = GetValue(simulation, "collidableTransformMap");
            var indices = map == null ? null : GetList(GetValue(map, "transformIndices"));
            var offsets = map == null ? null : GetList(GetValue(map, "offsets"));
            if (slot < 0 || indices == null || offsets == null || slot >= indices.Count || slot >= offsets.Count)
                continue;

            var boneIndex = ToInt(indices[slot], -1);
            if (boneIndex < 0 || offsets[slot] is not Matrix4x4 offset)
                return (fallbackTransform, boneIndex, true);

            return (offset * GetHkclBoneWorldTransform(skeleton, boneIndex), boneIndex, true);
        }

        return (fallbackTransform, -1, false);
    }

    private void SynchronizeCollidableTransformMaps(
        hkRootLevelContainer root,
        ColliderEditRow row,
        object collidable)
    {
        if (row.BoneIndex < 0)
            return;

        var clothDatas = GetClothDatas(root);
        var index = row.ClothIndex;
        if (index < 0 || index >= clothDatas.Count)
            return;

        var candidate = clothDatas[index];
        var skeleton = GetSkeletons(root).ElementAtOrDefault(index);
        if (skeleton == null)
            return;

        foreach (var simulation in GetList(GetValue(candidate, "simClothDatas")) ?? Array.Empty<object>())
        {
            var references = GetValue(simulation, "perInstanceCollidables") as IList;
            if (references == null)
                continue;

            var slot = IndexOfReference(references.Cast<object>().ToArray(), collidable);
            if (slot < 0)
                continue;

            var map = GetValue(simulation, "collidableTransformMap");
            if (map == null)
                continue;

            var boneWorld = GetHkclBoneWorldTransform(skeleton, row.BoneIndex);
            if (!Matrix4x4.Invert(boneWorld, out var inverseBoneWorld))
                throw new InvalidOperationException($"Could not invert bone {row.BoneIndex} while updating collider {row.Name}.");

            var indices = EnsureMutableObjectList(map, "transformIndices", "collider transform indices");
            var offsets = EnsureMutableObjectList(map, "offsets", "collider transform offsets");
            var hadOffset = slot < offsets.Count && offsets[slot] is Matrix4x4;
            while (indices.Count <= slot)
                AddListItem(indices, 0u);
            while (offsets.Count <= slot)
                AddListItem(offsets, Matrix4x4.Identity);

            // The transform map's orientation is not interchangeable with
            // hclCollidable.transform's orientation. Vanilla neck capsules
            // deliberately use different bases here. Preserve that authored
            // basis and only solve the local translation needed to keep a
            // viewport move in the same rest-pose location.
            var authoredOffset = offsets[slot] is Matrix4x4 existingOffset
                ? existingOffset
                : Matrix4x4.Identity;
            var solvedOffset = row.Transform * inverseBoneWorld;
            if (hadOffset)
            {
                authoredOffset.M41 = solvedOffset.M41;
                authoredOffset.M42 = solvedOffset.M42;
                authoredOffset.M43 = solvedOffset.M43;
            }
            else
            {
                authoredOffset = solvedOffset;
            }

            SetListItem(indices, slot, (uint)row.BoneIndex);
            SetListItem(offsets, slot, authoredOffset);
        }
    }

    private static void ClearCollidableTransformMapBinding(
        hkRootLevelContainer root,
        ColliderEditRow row,
        object collidable)
    {
        var cloth = GetClothDatas(root).ElementAtOrDefault(row.ClothIndex);
        if (cloth == null)
            return;

        foreach (var simulation in GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
        {
            var references = GetValue(simulation, "perInstanceCollidables") as IList;
            var map = GetValue(simulation, "collidableTransformMap");
            if (references == null || map == null)
                continue;

            var slot = IndexOfReference(references.Cast<object>().ToArray(), collidable);
            if (slot < 0 || GetValue(map, "transformIndices") is not IList indices || slot >= indices.Count)
                continue;

            SetListItem(indices, slot, uint.MaxValue);
        }
    }

    private static void CopyColliderTransformMapEntry(
        object? simulation,
        IList? references,
        object sourceCollider,
        object addedCollider)
    {
        if (simulation == null || references == null)
            return;

        var map = GetValue(simulation, "collidableTransformMap");
        if (map == null)
            return;

        var sourceSlot = IndexOfReference(references.Cast<object>().ToArray(), sourceCollider);
        var addedSlot = IndexOfReference(references.Cast<object>().ToArray(), addedCollider);
        if (sourceSlot < 0 || addedSlot < 0)
            return;

        var indices = EnsureMutableObjectList(map, "transformIndices", "collider transform indices");
        var offsets = EnsureMutableObjectList(map, "offsets", "collider transform offsets");
        var sourceIndex = sourceSlot < indices.Count ? ToInt(indices[sourceSlot], 0) : 0;
        var sourceOffset = sourceSlot < offsets.Count && offsets[sourceSlot] is Matrix4x4 offset
            ? offset
            : Matrix4x4.Identity;

        while (indices.Count <= addedSlot)
            AddListItem(indices, 0u);
        while (offsets.Count <= addedSlot)
            AddListItem(offsets, Matrix4x4.Identity);

        SetListItem(indices, addedSlot, (uint)Math.Max(sourceIndex, 0));
        SetListItem(offsets, addedSlot, sourceOffset);
    }

    private static void CopyColliderCollisionMaskBit(
        object? simulation,
        IList? references,
        object sourceCollider,
        object addedCollider)
    {
        if (simulation == null || references == null)
            return;

        var sourceSlot = IndexOfReference(references.Cast<object>().ToArray(), sourceCollider);
        var addedSlot = IndexOfReference(references.Cast<object>().ToArray(), addedCollider);
        // Slots 0..30 are normal colliders; Havok reserves bit 31 for
        // landscape collision and it must never be shifted or repurposed.
        if (sourceSlot is < 0 or >= 31 || addedSlot is < 0 or >= 31)
            return;

        if (GetValue(simulation, "staticCollisionMasks") is not IList masks)
            return;

        var sourceBit = 1u << sourceSlot;
        var addedBit = 1u << addedSlot;
        for (var particleIndex = 0; particleIndex < masks.Count; particleIndex++)
        {
            var mask = ToUInt32(masks[particleIndex], 0u);
            if ((mask & sourceBit) != 0)
                SetListItem(masks, particleIndex, mask | addedBit);
        }
    }

    private static IList GetPrimaryMutableCollidableReferences(object cloth)
    {
        IList? direct = GetValue(cloth, "perInstanceCollidables") as IList;
        var simulationLists = new List<IList>();

        foreach (var simulation in GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
        {
            if (GetValue(simulation, "perInstanceCollidables") is IList references)
                simulationLists.Add(references);
        }

        return simulationLists.FirstOrDefault()
            ?? direct
            ?? throw new InvalidOperationException("The HKCL template has no mutable per-instance collider reference list.");
    }

    private static void ReplaceClothCollidableReferences(object cloth, IReadOnlyList<object> collidables)
    {
        var direct = GetValue(cloth, "perInstanceCollidables") as IList;
        var simulationLists = (GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
            .Select(simulation => GetValue(simulation, "perInstanceCollidables") as IList)
            .Where(list => list != null)
            .Cast<IList>()
            .ToList();

        direct?.Clear();
        foreach (var list in simulationLists)
            list.Clear();

        // BotW HKCL stores active collider references on the simulation.
        // The cloth-level list remains a compatibility fallback for older files.
        var destination = simulationLists.FirstOrDefault() ?? direct
            ?? throw new InvalidOperationException("The HKCL template has no mutable per-instance collider reference list.");
        foreach (var collidable in collidables)
            AddListItem(destination, collidable);
    }

    private static void RemapClothCollidableReferences(
        object importedCloth,
        object sourceCloth,
        IReadOnlyDictionary<object, object> colliderMap)
    {
        RemapColliderList(
            GetValue(sourceCloth, "perInstanceCollidables") as IList,
            GetValue(importedCloth, "perInstanceCollidables") as IList,
            colliderMap);

        var sourceSimulations = GetList(GetValue(sourceCloth, "simClothDatas")) ?? Array.Empty<object>();
        var importedSimulations = GetList(GetValue(importedCloth, "simClothDatas")) ?? Array.Empty<object>();

        for (var index = 0; index < Math.Min(sourceSimulations.Count, importedSimulations.Count); index++)
        {
            RemapColliderList(
                GetValue(sourceSimulations[index], "perInstanceCollidables") as IList,
                GetValue(importedSimulations[index], "perInstanceCollidables") as IList,
                colliderMap);
        }
    }

    private static void RemapColliderList(
        IList? source,
        IList? destination,
        IReadOnlyDictionary<object, object> colliderMap)
    {
        if (source == null || destination == null)
            return;

        var remapped = source.Cast<object>()
            .Select(collider => colliderMap.TryGetValue(collider, out var imported)
                ? imported
                : throw new InvalidOperationException("The imported HKCL cloth references a collider that was not copied."))
            .ToArray();

        destination.Clear();
        foreach (var collider in remapped)
            AddListItem(destination, collider);
    }

    private static object CreateHkclColliderShape(object targetCollider, string shapeTypeName)
    {
        // Resolve generated HKX2 shape types from hclCollidable's assembly so
        // new editor colliders do not depend on a matching template shape.
        var targetType = targetCollider.GetType().Assembly
            .GetTypes()
            .FirstOrDefault(type => string.Equals(type.Name, shapeTypeName, StringComparison.Ordinal));
        if (targetType == null)
            throw new InvalidOperationException($"PhysicsTool does not contain the {shapeTypeName} collider shape class.");

        return Activator.CreateInstance(targetType)
            ?? throw new InvalidOperationException($"Could not create HKCL collider shape {shapeTypeName}.");
    }

    private static object CreateHkclColliderShape(object targetCollider, NativeBphclColliderShape source)
    {
        return CreateHkclColliderShape(targetCollider, source.TypeName);
    }

    private static void ApplyBphclColliderShape(object target, NativeBphclColliderShape source)
    {
        switch (source.TypeName)
        {
            case "hclCapsuleShape":
                SetValue(target, "start", source.Start);
                SetValue(target, "end", source.End);
                SetValue(target, "radius", source.Radius);
                UpdateCapsuleDerivedValues(target, source.Start, source.End);
                break;
            case "hclTaperedCapsuleShape":
                SetValue(target, "small", source.Start);
                SetValue(target, "big", source.End);
                SetValue(target, "smallRadius", source.Radius);
                SetValue(target, "bigRadius", source.EndRadius);
                UpdateCapsuleDerivedValues(target, source.Start, source.End);
                break;
            case "hclSphereShape":
            {
                // Freshly-created shape objects do not populate their embedded
                // hkSphere automatically. Create it before assigning the
                // center/radius stored together in its Vector4 position.
                var sphere = EnsureObjectValue(target, "sphere", "HKCL sphere");
                SetValue(sphere, "pos", new Vector4(
                    source.Start.X,
                    source.Start.Y,
                    source.Start.Z,
                    source.Radius));
                break;
            }
            case "hclPlaneShape":
                SetValue(target, "planeEquation", source.PlaneEquation);
                break;
            default:
                throw new InvalidOperationException($"Unsupported BPHCL collider shape {source.TypeName}.");
        }
    }

    private static IReadOnlyList<string> ApplyBphclConstraintLinks(
        object cloth,
        NativeBphclSimCloth source,
        string? sourcePath,
        string sourceClothName,
        bool preserveTemplateLayout,
        float solverScale)
    {
        var staticTargetSets = GetMutableConstraintSets(cloth);
        var antiPinchTargetSets = GetMutableAntiPinchConstraintSets(cloth);
        var notes = new List<string>();

        if (!preserveTemplateLayout)
        {
            staticTargetSets.Clear();
            antiPinchTargetSets.Clear();
        }

        foreach (var sourceSet in source.ConstraintSets)
        {
            var targetSets = sourceSet.IsAntiPinch ? antiPinchTargetSets : staticTargetSets;
            var sourceBucket = sourceSet.IsAntiPinch ? "anti-pinch" : "static";
            if (BphclConversionProfileCatalog.ShouldOmitConstraintSet(sourceClothName, sourceSet.ClassName))
            {
                foreach (var targetSetToRemove in targetSets.Cast<object?>()
                             .Where(set => set != null && string.Equals(
                                 set.GetType().Name,
                                 sourceSet.ClassName,
                                 StringComparison.Ordinal))
                             .ToArray())
                {
                    targetSets.Remove(targetSetToRemove!);
                }

                notes.Add($"Omitted {sourceBucket} {sourceSet.ClassName} using the verified {StripBphclPrefix(sourceClothName)} constraint profile.");
                continue;
            }

            var createdTargetSet = false;
            var targetSet = targetSets.Cast<object?>().FirstOrDefault(set =>
                set != null && string.Equals(set.GetType().Name, sourceSet.ClassName, StringComparison.Ordinal));
            if (targetSet == null)
            {
                // BotW's generated HKX2 classes support this set even when
                // the selected shell did not need one. It is an essential
                // solver stage in several TotK hair layouts, so retain the
                // otherwise verified BotW transition/local-range data while
                // adding this missing source-owned constraint class.
                var canAppendCompatibleSet = string.Equals(
                    sourceSet.ClassName,
                    "hclBendStiffnessConstraintSet",
                    StringComparison.Ordinal);
                if (preserveTemplateLayout && !canAppendCompatibleSet)
                {
                    notes.Add($"Kept the HKCL template layout; skipped unsupported BPHCL {sourceSet.ClassName}.");
                    continue;
                }

                targetSet = CreateHavokConstraintSet(cloth, targetSets, sourceSet.ClassName);
                createdTargetSet = true;
                notes.Add($"Added {sourceBucket} {sourceSet.ClassName} from the BPHCL source.");
            }
            SetValue(targetSet, "name", string.IsNullOrWhiteSpace(sourceSet.Name) ? sourceSet.ClassName : sourceSet.Name);
            // The concrete C# class identifies the broad constraint family,
            // but Havok also serializes these source-owned identifiers on the
            // hclConstraintSet base record. Fresh files previously left all
            // of them at their constructor defaults.
            SetValue(targetSet, "constraintId", sourceSet.ConstraintId);
            SetValue(targetSet, "constraintType", sourceSet.ConstraintType);
            if (string.Equals(sourceSet.ClassName, "hclVolumeConstraint", StringComparison.Ordinal))
            {
                ApplyBphclVolumeConstraintData(targetSet, sourceSet, preserveTemplateLayout, createdTargetSet, notes);
                foreach (var value in sourceSet.Values)
                    SetValue(targetSet, value.Key, value.Value);
                continue;
            }
            var usesLocalConstraints = string.Equals(sourceSet.ClassName, "hclLocalRangeConstraintSet", StringComparison.Ordinal);
            var usesTransitionParticles = string.Equals(sourceSet.ClassName, "hclTransitionConstraintSet", StringComparison.Ordinal);
            var sourceRecords = usesLocalConstraints
                ? GetBphclLocalRangeRecordsForHkcl(sourceSet, notes)
                : usesTransitionParticles
                    ? sourceSet.TransitionParticles
                    : sourceSet.Links;
            var recordMember = usesLocalConstraints
                ? "localConstraints"
                : usesTransitionParticles
                    ? "perParticleData"
                    : "links";
            var targetLinks = EnsureMutableObjectList(targetSet, recordMember, $"{sourceSet.ClassName}.{recordMember}");

            if (preserveTemplateLayout && !createdTargetSet && targetLinks.Count != sourceRecords.Count)
            {
                notes.Add($"Kept the HKCL template {sourceSet.ClassName} records because BPHCL exposes {sourceRecords.Count} but HKCL needs {targetLinks.Count}.");
                continue;
            }

            if (targetLinks.Count != sourceRecords.Count)
            {
                var prototype = targetLinks.Count > 0 ? targetLinks[0] : null;
                var linkType = prototype?.GetType()
                    ?? GetListElementType(targetLinks)
                    ?? FindHavokType(cloth.GetType().Assembly, usesLocalConstraints
                        ? "hclLocalRangeConstraintSetLocalConstraint"
                        : usesTransitionParticles
                            ? "hclTransitionConstraintSetPerParticle"
                        : sourceSet.ClassName + "Link");
                if (linkType == null)
                {
                    throw new InvalidOperationException(
                        $"PhysicsTool could not locate the generated HKX2 constraint record class for {sourceSet.ClassName}.");
                }

                targetLinks.Clear();
                for (var index = 0; index < sourceRecords.Count; index++)
                {
                    var link = prototype == null
                        ? Activator.CreateInstance(linkType)
                        : CloneForCurrentGraph(prototype);
                    targetLinks.Add(link ?? throw new InvalidOperationException(
                        $"PhysicsTool could not create a {sourceSet.ClassName} constraint record."));
                }
                notes.Add($"Created {sourceRecords.Count} {sourceSet.ClassName} record(s) from BPHCL data.");
            }

            for (var index = 0; index < sourceRecords.Count; index++)
            {
                var sourceLink = sourceRecords[index];
                var targetLink = targetLinks[index];
                if (sourceLink.ParticleA is int particleA)
                    SetValue(targetLink, "particleA", particleA);
                if (sourceLink.ParticleB is int particleB)
                    SetValue(targetLink, "particleB", particleB);

                foreach (var value in sourceLink.Values)
                {
                    var scaledValue = ScaleBphclSoftConstraintValue(
                        sourceSet.ClassName,
                        value.Key,
                        value.Value,
                        solverScale);
                    SetValue(
                        targetLink,
                        value.Key,
                        BphclConversionProfileCatalog.ApplyConstraintFieldProfile(
                            sourcePath,
                            sourceClothName,
                            sourceSet.ClassName,
                            value.Key,
                            scaledValue));
                }

                // TotK's LocalConstraint calls this shapeRadius; BotW's
                // equivalent record calls it maximumDistance. Leaving the
                // template value here makes the converted particle positions
                // fight unrelated local-range limits at runtime.
                if (usesLocalConstraints &&
                    sourceLink.Values.TryGetValue("shapeRadius", out var shapeRadius))
                {
                    SetValue(
                        targetLink,
                        "maximumDistance",
                        BphclConversionProfileCatalog.ApplyConstraintFieldProfile(
                            sourcePath,
                            sourceClothName,
                            sourceSet.ClassName,
                            "maximumDistance",
                            shapeRadius));
                }
            }

            foreach (var value in sourceSet.Values)
                SetValue(
                    targetSet,
                    value.Key,
                    value.Value);
        }

        return notes;
    }

    private static void ApplyBphclVolumeConstraintData(
        object targetSet,
        NativeBphclConstraintSet sourceSet,
        bool preserveTemplateLayout,
        bool createdTargetSet,
        ICollection<string> notes)
    {
        var targetFrames = EnsureMutableObjectList(targetSet, "frameDatas", "hclVolumeConstraint.frameDatas");
        var targetApplies = EnsureMutableObjectList(targetSet, "applyDatas", "hclVolumeConstraint.applyDatas");
        if (preserveTemplateLayout && !createdTargetSet &&
            (targetFrames.Count != sourceSet.VolumeFrameData.Count ||
             targetApplies.Count != sourceSet.VolumeApplyData.Count))
        {
            notes.Add(
                $"Kept the HKCL template hclVolumeConstraint records because BPHCL exposes " +
                $"{sourceSet.VolumeFrameData.Count} frame and {sourceSet.VolumeApplyData.Count} apply records, " +
                $"while HKCL needs {targetFrames.Count} and {targetApplies.Count}.");
            return;
        }

        ResizeHavokRecordList(targetSet, targetFrames, sourceSet.VolumeFrameData.Count,
            "hclVolumeConstraintFrameData", "volume frame");
        ResizeHavokRecordList(targetSet, targetApplies, sourceSet.VolumeApplyData.Count,
            "hclVolumeConstraintApplyData", "volume apply");

        for (var index = 0; index < sourceSet.VolumeFrameData.Count; index++)
        {
            var source = sourceSet.VolumeFrameData[index];
            var target = targetFrames[index];
            SetValue(target, "frameVector", source.FrameVector);
            SetValue(target, "particleIndex", source.ParticleIndex);
            SetValue(target, "weight", source.Weight);
        }

        for (var index = 0; index < sourceSet.VolumeApplyData.Count; index++)
        {
            var source = sourceSet.VolumeApplyData[index];
            var target = targetApplies[index];
            SetValue(target, "frameVector", source.FrameVector);
            SetValue(target, "particleIndex", source.ParticleIndex);
            SetValue(target, "stiffness", source.Stiffness);
        }

        notes.Add(
            $"Created {sourceSet.VolumeFrameData.Count} volume frame and " +
            $"{sourceSet.VolumeApplyData.Count} volume apply record(s) from BPHCL data.");
    }

    private static void ResizeHavokRecordList(
        object owner,
        IList records,
        int requiredCount,
        string generatedTypeName,
        string displayName)
    {
        if (records.Count == requiredCount)
            return;

        var prototype = records.Count > 0 ? records[0] : null;
        var recordType = prototype?.GetType()
            ?? GetListElementType(records)
            ?? FindHavokType(owner.GetType().Assembly, generatedTypeName);
        if (recordType == null)
            throw new InvalidOperationException(
                $"PhysicsTool could not locate the generated HKX2 {displayName} record class.");

        records.Clear();
        for (var index = 0; index < requiredCount; index++)
        {
            var record = prototype == null
                ? Activator.CreateInstance(recordType)
                : CloneForCurrentGraph(prototype);
            records.Add(record ?? throw new InvalidOperationException(
                $"PhysicsTool could not create a {displayName} record."));
        }
    }
    private static IReadOnlyList<NativeBphclConstraintLink> GetBphclLocalRangeRecordsForHkcl(
        NativeBphclConstraintSet sourceSet,
        ICollection<string> notes)
    {
        if (sourceSet.LocalStiffnessConstraints.Count == 0)
            return sourceSet.LocalConstraints;
        if (sourceSet.LocalConstraints.Count == 0)
            return sourceSet.LocalStiffnessConstraints;

        // TotK can retain both arrays. BotW only has a single local-constraint
        // array and one set-wide stiffness value, so keep every range record
        // rather than silently dropping one source array. In the rare case of
        // different per-record stiffness values, the parent-set value remains
        // the closest representable BotW approximation.
        var stiffnessValues = sourceSet.LocalStiffnessConstraints
            .Select(link => link.Values.TryGetValue("stiffness", out var stiffness) ? stiffness : double.NaN)
            .Where(double.IsFinite)
            .Distinct()
            .ToArray();
        if (stiffnessValues.Length > 1)
        {
            notes.Add(
                $"{sourceSet.Name} has multiple per-record local-range stiffness values; BotW supports only the set-wide stiffness value.");
        }

        notes.Add(
            $"Merged {sourceSet.LocalConstraints.Count} local-range and {sourceSet.LocalStiffnessConstraints.Count} local-stiffness record(s) into BotW's single local-range list.");
        return sourceSet.LocalConstraints.Concat(sourceSet.LocalStiffnessConstraints).ToArray();
    }

    // Matched BPHCL/HKCL pairs such as Armor_008's Earring_028 show that
    // standard-link stiffness, bend-link stiffness, and bend-stiffness
    // coefficients are multiplied by the same cloth scale as dynamic mass.
    // Stretch-link stiffness remains a normalized limit (normally 1.0).
    private static double ScaleBphclSoftConstraintValue(
        string constraintClass,
        string fieldName,
        double value,
        float solverScale)
    {
        if (!float.IsFinite(solverScale) || solverScale <= 0)
            return value;

        var isSoftCoefficient = constraintClass switch
        {
            "hclStandardLinkConstraintSet" => string.Equals(fieldName, "stiffness", StringComparison.Ordinal),
            "hclBendLinkConstraintSet" =>
                string.Equals(fieldName, "bendStiffness", StringComparison.Ordinal) ||
                string.Equals(fieldName, "stretchStiffness", StringComparison.Ordinal),
            "hclBendStiffnessConstraintSet" => string.Equals(fieldName, "bendStiffness", StringComparison.Ordinal),
            _ => false
        };

        return isSoftCoefficient ? value * solverScale : value;
    }

    private static IReadOnlyList<string> ApplyBphclConstraintExecution(
        object cloth,
        NativeBphclSimCloth sourceSimulation,
        string sourceClothName,
        IReadOnlyList<NativeBphclOperatorLayout> sourceOperators)
    {
        var sourceOperator = sourceOperators.FirstOrDefault(op =>
            string.Equals(op.ClassName, "hclSimulateOperator", StringComparison.Ordinal));
        var sourceExecution = sourceOperator?.SimulateConfigs.FirstOrDefault()?.ConstraintExecution;
        if (sourceExecution is null || sourceExecution.Count == 0)
            return Array.Empty<string>();

        var targetOperator = (GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>())
            .FirstOrDefault(op => string.Equals(op.GetType().Name, "hclSimulateOperator", StringComparison.Ordinal));
        if (targetOperator is null)
            throw new InvalidOperationException("The HKCL template has no hclSimulateOperator to receive BPHCL constraint execution.");

        var targetSets = GetMutableConstraintSets(cloth).Cast<object>().ToList();
        var targetByClass = targetSets
            .Select((set, index) => (set, index))
            .ToDictionary(entry => entry.set.GetType().Name, entry => entry.index, StringComparer.Ordinal);
        var sourceToTarget = new Dictionary<int, int>();
        foreach (var sourceSet in sourceSimulation.ConstraintSets.Where(set => !set.IsAntiPinch))
        {
            if (BphclConversionProfileCatalog.ShouldOmitConstraintSet(sourceClothName, sourceSet.ClassName))
            {
                // In the BotW counterparts that omit a dedicated bend-stiffness
                // set, the solver executes the normal bend set a second time in
                // its place. Preserve that phase rather than silently dropping it.
                if (string.Equals(sourceSet.ClassName, "hclBendStiffnessConstraintSet", StringComparison.Ordinal) &&
                    targetByClass.TryGetValue("hclBendLinkConstraintSet", out var bendTargetIndex))
                {
                    sourceToTarget[sourceSet.Index] = bendTargetIndex;
                }

                continue;
            }

            if (targetByClass.TryGetValue(sourceSet.ClassName, out var targetIndex))
            {
                sourceToTarget[sourceSet.Index] = targetIndex;
                continue;
            }

        }

        // TotK's execution array can contain a -1 phase separator followed by
        // transition constraints. Translate the phase order for BotW, but keep
        // a final terminator when the source supplied one. The collision-working
        // Armor_1300 reference has the same translated sets followed by -1;
        // dropping that sentinel lets the cloth animate while bypassing its
        // per-instance collision stage.
        var primaryPhase = new List<int>(sourceExecution.Count);
        var deferredPhase = new List<int>();
        var afterPhaseSeparator = false;
        foreach (var sourceValue in sourceExecution)
        {
            if (sourceValue < 0)
            {
                afterPhaseSeparator = true;
                continue;
            }

            if (!sourceToTarget.TryGetValue(sourceValue, out var targetValue))
            {
                var sourceSet = sourceSimulation.ConstraintSets
                    .FirstOrDefault(set => set.Index == sourceValue);
                if (sourceSet != null && BphclConversionProfileCatalog.ShouldOmitConstraintSet(
                        sourceClothName,
                        sourceSet.ClassName))
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"The BPHCL solver executes constraint set {sourceValue}, but the HKCL template has no compatible set for it.");
            }
            (afterPhaseSeparator ? deferredPhase : primaryPhase).Add(targetValue);
        }

        var translated = new List<int>(primaryPhase.Count + deferredPhase.Count);
        var localRangeIndexes = targetSets
            .Select((set, index) => (set, index))
            .Where(entry => string.Equals(entry.set.GetType().Name, "hclLocalRangeConstraintSet", StringComparison.Ordinal))
            .Select(entry => entry.index)
            .ToHashSet();
        var localPrefixLength = 0;
        while (localPrefixLength < primaryPhase.Count && localRangeIndexes.Contains(primaryPhase[localPrefixLength]))
            localPrefixLength++;

        translated.AddRange(primaryPhase.Take(localPrefixLength));
        translated.AddRange(deferredPhase);
        translated.AddRange(primaryPhase.Skip(localPrefixLength));

        if (sourceExecution.Any(value => value < 0) &&
            BphclConversionProfileCatalog.ShouldKeepConstraintExecutionTerminator(sourceClothName))
        {
            translated.Add(-1);
        }

        ReplaceList(EnsureMutableObjectList(targetOperator, "constraintExecution", "constraint execution"), translated);
        return new[]
        {
            $"Normalized the BPHCL solver phases for HKCL constraint execution: [{string.Join(", ", translated)}]."
        };
    }

    private static IReadOnlyList<string> ApplyBphclSimpleMeshBoneDeform(
        object cloth,
        IReadOnlyList<NativeBphclSimpleMeshBoneDeform> sourceDeformers,
        IReadOnlyDictionary<int, int> sourceToTargetBone)
    {
        if (sourceDeformers.Count == 0)
            return Array.Empty<string>();

        var target = (GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>())
            .FirstOrDefault(op => string.Equals(op.GetType().Name, "hclSimpleMeshBoneDeformOperator", StringComparison.Ordinal));
        if (target == null)
            throw new InvalidOperationException("The HKCL template has no hclSimpleMeshBoneDeformOperator to receive the BPHCL bone-output mapping.");

        // BotW's simple bone deform stage is singular for the bone-chain
        // cloths currently supported by the converter.
        var source = sourceDeformers[0];
        SetValue(target, "inputBufferIdx", source.InputBufferIndex);
        SetValue(target, "outputTransformSetIdx", source.OutputTransformSetIndex);
        SetValue(target, "boneAxis", source.BoneAxis);

        var pairs = EnsureMutableObjectList(target, "triangleBonePairs", "triangle bone pairs");
        var pairPrototype = pairs.Count > 0 ? pairs[0] : null;
        var pairType = pairPrototype?.GetType()
            ?? GetListElementType(pairs)
            ?? FindHavokType(cloth.GetType().Assembly, "hclSimpleMeshBoneDeformOperatorTriangleBonePair")
            ?? throw new InvalidOperationException("PhysicsTool could not locate the HKCL triangle-bone pair type.");
        pairs.Clear();
        foreach (var sourcePair in source.TriangleBonePairs)
        {
            var pair = pairPrototype == null
                ? Activator.CreateInstance(pairType)
                : CloneForCurrentGraph(pairPrototype);
            if (pair == null)
                throw new InvalidOperationException("PhysicsTool could not create an HKCL triangle-bone pair.");
            var sourceBoneIndex = sourcePair.BoneOffset / 64;
            if (!sourceToTargetBone.TryGetValue(sourceBoneIndex, out var targetBoneIndex))
            {
                throw new InvalidOperationException(
                    $"The BPHCL bone-output mapping references source bone {sourceBoneIndex}, which is absent from the selected HKCL skeleton template.");
            }

            // Havok stores a bone index in 64-byte transform slots here.
            SetValue(pair, "boneOffset", targetBoneIndex * 64);
            SetValue(pair, "triangleOffset", sourcePair.TriangleOffset);
            AddListItem(pairs, pair);
        }

        var localTransforms = EnsureMutableObjectList(target, "localBoneTransforms", "local bone transforms");
        localTransforms.Clear();
        foreach (var sourceTransform in source.LocalBoneTransforms)
            AddListItem(localTransforms, sourceTransform);

        return new[]
        {
            $"Rebuilt SimpleMeshBoneDeform with {source.TriangleBonePairs.Count} triangle-to-bone pair(s) and {source.LocalBoneTransforms.Count} local bone transform(s)."
        };
    }

    private static IList GetMutableConstraintSets(object cloth)
    {
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"));
        if (GetValue(simulation, "staticConstraintSets") is IList staticSets)
            return staticSets;
        if (GetValue(cloth, "constraintSets") is IList sets)
            return sets;

        throw new InvalidOperationException("The converted HKCL cloth has no writable constraint-set collection.");
    }

    private static IList GetMutableAntiPinchConstraintSets(object cloth)
    {
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simulation == null)
            throw new InvalidOperationException("The converted HKCL cloth has no simulation data for anti-pinch constraints.");

        return EnsureMutableObjectList(simulation, "antiPinchConstraintSets", "anti-pinch constraint sets");
    }

    private static object CreateHavokConstraintSet(object cloth, IList targetSets, string className)
    {
        var type = FindHavokType(cloth.GetType().Assembly, className)
            ?? throw new InvalidOperationException(
                $"PhysicsTool could not locate the generated HKX2 class {className} required by this BPHCL cloth.");
        var instance = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"PhysicsTool could not create {className}.");
        AddListItem(targetSets, instance);
        return instance;
    }

    private static IList EnsureMutableObjectList(object owner, string name, string displayName)
    {
        if (GetValue(owner, name) is IList existing)
            return existing;

        var type = owner.GetType();
        var candidates = new[] { name, "m_" + name };
        var memberType = candidates
            .Select(candidate => type.GetField(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.FieldType
                ?? type.GetProperty(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.PropertyType)
            .FirstOrDefault(candidate => candidate != null);
        var elementType = memberType == null ? null : GetCollectionElementType(memberType);
        if (elementType == null)
            throw new InvalidOperationException($"PhysicsTool could not determine the link type for {displayName}.");

        var concreteType = memberType is { IsInterface: false, IsAbstract: false }
            ? memberType
            : typeof(List<>).MakeGenericType(elementType);
        var list = Activator.CreateInstance(concreteType) as IList
            ?? throw new InvalidOperationException($"PhysicsTool could not create the list for {displayName}.");
        if (!SetValue(owner, name, list))
            throw new InvalidOperationException($"PhysicsTool could not assign the list for {displayName}.");
        return list;
    }

    private static Type? FindHavokType(Assembly assembly, string className)
    {
        try
        {
            return assembly.GetTypes().FirstOrDefault(type =>
                string.Equals(type.Name, className, StringComparison.Ordinal) &&
                !type.IsAbstract);
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.FirstOrDefault(type => type != null &&
                string.Equals(type.Name, className, StringComparison.Ordinal) &&
                !type.IsAbstract);
        }
    }

    private static int ResolveColliderBoneIndex(string colliderName, IReadOnlyList<NativeBphclBone> bones)
    {
        const string prefix = "Collidable_";
        if (!colliderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return -1;

        var suffix = colliderName[prefix.Length..];
        var exact = bones.FirstOrDefault(bone => string.Equals(StripBphclPrefix(bone.Name), suffix, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact.Index;

        // Names such as Collidable_Spine_2_1 still belong to Spine_2.
        for (var end = suffix.Length; end > 0; end = suffix.LastIndexOf('_', end - 1))
        {
            if (end <= 0)
                break;
            var candidate = suffix[..end];
            var match = bones.FirstOrDefault(bone => string.Equals(StripBphclPrefix(bone.Name), candidate, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match.Index;
        }

        return -1;
    }

    private static string StripBphclPrefix(string name) =>
        name.StartsWith("Link:", StringComparison.OrdinalIgnoreCase) ? name[5..] : name;

    private static string EnsureLinkPrefix(string name) =>
        name.StartsWith("Link:", StringComparison.OrdinalIgnoreCase) ? name : "Link:" + name;

    private hkRootLevelContainer RequireRoot()
    {
        return _root ?? throw new InvalidOperationException("No physics file is loaded.");
    }

    private static hkRootLevelContainer LoadHkcl(string path)
    {
        using var stream = File.OpenRead(path);
        var reader = new BinaryReaderEx(stream);
        var deserializer = new PackFileDeserializer();
        return (hkRootLevelContainer)deserializer.Deserialize(reader);
    }

    private static hkRootLevelContainer LoadJson(string path)
    {
        var text = File.ReadAllText(path);
        var token = JToken.Parse(text);

        if (token is not JObject obj)
            return DeserializeRoot(text);

        if (obj["technical"] is JObject technical)
        {
            hkRootLevelContainer? root = null;

            if (technical["hkxObjectGraphCompressed"] is JValue compressedToken)
                root = DeserializeRoot(DecompressJson(compressedToken.Value<string>() ?? string.Empty));
            else if (technical["hkxObjectGraph"] is JToken graphToken)
                root = DeserializeRoot(graphToken.ToString(Formatting.None));

            if (root != null)
            {
                ApplyAuthoring(root, obj);
                return root;
            }
        }

        if (obj.TryGetValue("raw", out var rawToken))
        {
            var root = DeserializeRoot(rawToken.ToString(Formatting.None));
            if (obj["authoring"] is JObject authoring)
                ApplyAuthoring(root, authoring);
            return root;
        }

        if (obj.TryGetValue("authoring", out var nestedAuthoring) && nestedAuthoring is JObject nestedAuthoringObj)
        {
            var sourceRoot = LoadLegacyAuthoringBase(path, obj);
            ApplyAuthoring(sourceRoot, nestedAuthoringObj);
            return sourceRoot;
        }

        if ((string?)obj["format"] == "HKCLTool.Authoring" || obj["cloths"] is JArray)
        {
            var sourceRoot = LoadLegacyAuthoringBase(path, obj);
            ApplyAuthoring(sourceRoot, obj);
            return sourceRoot;
        }

        return DeserializeRoot(text);
    }

    private static hkRootLevelContainer LoadLegacyAuthoringBase(string jsonPath, JObject document)
    {
        var sourceFile = (string?)document["sourceFile"];
        if (string.IsNullOrWhiteSpace(sourceFile))
            throw new InvalidOperationException("This JSON does not contain a technical.hkxObjectGraph section. Older authoring JSON files also need sourceFile so HKCLTool can load the original HKCL base.");

        var basePath = Path.IsPathRooted(sourceFile)
            ? sourceFile
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(jsonPath) ?? Environment.CurrentDirectory, sourceFile));

        if (!File.Exists(basePath))
            throw new FileNotFoundException("The source HKCL referenced by this legacy authoring JSON could not be found.", basePath);

        return LoadHkcl(basePath);
    }

    private JObject BuildReadableJson()
    {
        var root = RequireRoot();
        var authoring = BuildAuthoring(root);

        return new JObject
        {
            ["format"] = "HKCLTool.SelfContainedAuthoring",
            ["version"] = 4,
            ["notes"] = "Self-contained editable JSON. Edit the readable cloths section. The compact technical payload preserves the remaining HKX2 data needed to rebuild without the original HKCL file.",
            ["cloths"] = authoring["cloths"],
            ["technical"] = new JObject
            {
                ["encoding"] = "gzip+base64-json",
                ["warning"] = "Do not delete this compact payload yet. It contains required HKX2 fields that have not all been promoted into the readable authoring schema.",
                ["hkxObjectGraphCompressed"] = CompressJson(SerializeRaw(root))
            }
        };
    }

    private static JObject BuildAuthoring(hkRootLevelContainer root)
    {
        var cloths = GetClothDatas(root).ToList();
        var skeletons = GetSkeletons(root).ToList();
        var collidables = GetCollidables(root).ToList();
        var clothArray = new JArray();

        for (var i = 0; i < cloths.Count; i++)
        {
            var cloth = cloths[i];
            var skeleton = i < skeletons.Count ? skeletons[i] : null;
            clothArray.Add(new JObject
            {
                ["index"] = i,
                ["name"] = GetString(cloth, "name") ?? $"Cloth {i}",
                ["class"] = cloth.GetType().Name,
                ["skeleton"] = skeleton == null ? null : BuildSkeleton(skeleton),
                ["simulation"] = BuildSimulation(cloth),
                ["particles"] = BuildParticles(cloth),
                ["triangles"] = BuildTriangles(cloth),
                ["constraints"] = BuildConstraintSummary(cloth),
                ["colliders"] = BuildColliders(collidables, skeleton)
            });
        }

        return new JObject
        {
            ["notes"] = "These values are applied onto the source HKCL when this authoring JSON is reopened.",
            ["cloths"] = clothArray
        };
    }

    private static JObject BuildSkeleton(object skeleton)
    {
        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();
        var boneArray = new JArray();

        for (var i = 0; i < bones.Count; i++)
        {
            var parentIndex = ToInt(parents.ElementAtOrDefault(i), -1);
            boneArray.Add(new JObject
            {
                ["index"] = i,
                ["name"] = GetString(bones[i], "name") ?? $"Bone {i}",
                ["parentIndex"] = parentIndex,
                ["parent"] = parentIndex >= 0 && parentIndex < bones.Count ? GetString(bones[parentIndex], "name") : null,
                ["lockTranslation"] = ToBool(GetValue(bones[i], "lockTranslation")),
                ["referencePose"] = BuildReferencePose(poses.ElementAtOrDefault(i))
            });
        }

        return new JObject
        {
            ["name"] = GetString(skeleton, "name"),
            ["boneCount"] = bones.Count,
            ["bones"] = boneArray
        };
    }

    private static JToken? BuildReferencePose(object? pose)
    {
        if (pose is Matrix4x4 matrix)
        {
            return new JObject
            {
                ["translation"] = Vector(matrix.M11, matrix.M12, matrix.M13, matrix.M14),
                ["rotationQuaternion"] = Vector(matrix.M21, matrix.M22, matrix.M23, matrix.M24),
                ["scale"] = Vector(matrix.M31, matrix.M32, matrix.M33, matrix.M34),
                ["rawRows"] = MatrixRows(matrix)
            };
        }

        return ToToken(pose);
    }

    private static JObject BuildSimulation(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData == null)
            return new JObject();

        return new JObject
        {
            ["gravity"] = ToToken(GetValue(simData, "gravity")),
            ["globalDampingPerSecond"] = ToToken(GetValue(simData, "globalDampingPerSecond")),
            ["collisionTolerance"] = ToToken(GetValue(simData, "collisionTolerance")),
            ["totalMass"] = ToToken(GetValue(simData, "totalMass")),
            ["maxParticleRadius"] = ToToken(GetValue(simData, "maxParticleRadius")),
            ["numberOfParticles"] = GetList(GetValue(simData, "particleDatas"))?.Count ?? 0
        };
    }

    private static JArray BuildParticles(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var particles = GetList(GetValue(simData, "particleDatas")) ?? Array.Empty<object>();
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetList(GetValue(pose, "positions")) ?? Array.Empty<object>();
        var fixedParticles = new HashSet<int>((GetList(GetValue(simData, "fixedParticles")) ?? Array.Empty<object>()).Select(x => ToInt(x, -1)));
        var collisionMasks = GetList(GetValue(simData, "staticCollisionMasks")) ?? Array.Empty<object>();
        var result = new JArray();

        for (var i = 0; i < particles.Count; i++)
        {
            var particle = particles[i];
            result.Add(new JObject
            {
                ["index"] = i,
                ["position"] = ToToken(positions.ElementAtOrDefault(i)),
                ["fixed"] = fixedParticles.Contains(i),
                ["mass"] = ToToken(GetValue(particle, "mass")),
                ["inverseMass"] = ToToken(GetValue(particle, "invMass")),
                ["radius"] = ToToken(GetValue(particle, "radius")),
                ["friction"] = ToToken(GetValue(particle, "friction")),
                ["collisionMask"] = ToToken(collisionMasks.ElementAtOrDefault(i))
            });
        }

        return result;
    }

    private static JArray BuildTriangles(object cloth)
    {
        var result = new JArray();
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var indices = GetList(GetValue(simData, "triangleIndices"));

        if (indices == null || indices.Count < 3)
        {
            foreach (var buffer in EnumerateObjects(cloth).Where(x => x.GetType().Name.Contains("BufferDefinition", StringComparison.Ordinal)))
            {
                indices = GetList(GetValue(buffer, "triangleIndices"));
                if (indices is { Count: >= 3 })
                    break;
            }
        }

        if (indices == null)
            return result;

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            result.Add(new JObject
            {
                ["index"] = i / 3,
                ["particles"] = new JArray(
                    ToInt(indices[i], 0),
                    ToInt(indices[i + 1], 0),
                    ToInt(indices[i + 2], 0))
            });
        }

        return result;
    }

    private static JArray BuildConstraintSummary(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();
        var result = new JArray();

        for (var i = 0; i < constraints.Count; i++)
        {
            var constraint = constraints[i];
            var links = GetList(GetValue(constraint, "links"));
            var ranges = GetList(GetValue(constraint, "localConstraints"));
            result.Add(new JObject
            {
                ["index"] = i,
                ["class"] = constraint.GetType().Name,
                ["name"] = GetString(constraint, "name"),
                ["linkCount"] = links?.Count,
                ["localConstraintCount"] = ranges?.Count,
                ["links"] = BuildConstraintLinks(links),
                ["localConstraints"] = BuildLocalConstraints(ranges)
            });
        }

        return result;
    }

    private static JArray BuildConstraintLinks(IReadOnlyList<object>? links)
    {
        var result = new JArray();
        if (links == null)
            return result;

        for (var i = 0; i < links.Count; i++)
        {
            var link = links[i];
            result.Add(new JObject
            {
                ["index"] = i,
                ["particleA"] = ToToken(GetValue(link, "particleA")),
                ["particleB"] = ToToken(GetValue(link, "particleB")),
                ["restLength"] = ToToken(GetValue(link, "restLength")),
                ["stiffness"] = ToToken(GetValue(link, "stiffness")),
                ["bendMinLength"] = ToToken(GetValue(link, "bendMinLength")),
                ["stretchMaxLength"] = ToToken(GetValue(link, "stretchMaxLength")),
                ["bendStiffness"] = ToToken(GetValue(link, "bendStiffness")),
                ["stretchStiffness"] = ToToken(GetValue(link, "stretchStiffness"))
            });
        }

        return result;
    }

    private static JArray BuildLocalConstraints(IReadOnlyList<object>? constraints)
    {
        var result = new JArray();
        if (constraints == null)
            return result;

        for (var i = 0; i < constraints.Count; i++)
        {
            var constraint = constraints[i];
            result.Add(new JObject
            {
                ["index"] = i,
                ["particleIndex"] = ToToken(GetValue(constraint, "particleIndex")),
                ["maximumDistance"] = ToToken(GetValue(constraint, "maximumDistance")),
                ["maxDistance"] = ToToken(GetValue(constraint, "maxDistance"))
            });
        }

        return result;
    }

    private static JArray BuildColliders(IReadOnlyList<object> collidables, object? skeleton)
    {
        var bones = skeleton == null
            ? Array.Empty<object>()
            : GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();

        var result = new JArray();

        for (var i = 0; i < collidables.Count; i++)
        {
            var collidable = collidables[i];
            var boneIndex = ToInt(GetValue(collidable, "transformIndex"), -1);
            var shape = GetValue(collidable, "shape");

            result.Add(new JObject
            {
                ["index"] = i,
                ["name"] = GetString(collidable, "name"),
                ["boneIndex"] = boneIndex,
                ["bone"] = boneIndex >= 0 && boneIndex < bones.Count ? GetString(bones[boneIndex], "name") : null,
                ["transform"] = ToToken(GetValue(collidable, "transform")),
                ["boneOffset"] = ToToken(GetValue(collidable, "boneOffset")),
                ["shape"] = shape == null ? null : new JObject
                {
                    ["class"] = shape.GetType().Name,
                    ["start"] = ToToken(GetValue(shape, "start")),
                    ["end"] = ToToken(GetValue(shape, "end")),
                    ["dir"] = ToToken(GetValue(shape, "dir")),
                    ["radius"] = ToToken(GetValue(shape, "radius")),
                    ["capLenSqrdInv"] = ToToken(GetValue(shape, "capLenSqrdInv"))
                }
            });
        }

        return result;
    }

    private static void ApplyAuthoring(hkRootLevelContainer root, JObject authoring)
    {
        if (authoring["cloths"] is not JArray clothArray)
            return;

        var cloths = GetClothDatas(root).ToList();
        var skeletons = GetSkeletons(root).ToList();
        var collidables = GetCollidables(root).ToList();

        foreach (var clothToken in clothArray.OfType<JObject>())
        {
            var clothIndex = (int?)clothToken["index"] ?? clothArray.IndexOf(clothToken);
            if (clothIndex < 0 || clothIndex >= cloths.Count)
                continue;

            var cloth = cloths[clothIndex];
            SetValue(cloth, "name", (string?)clothToken["name"]);

            if (clothToken["skeleton"] is JObject skeletonToken && clothIndex < skeletons.Count)
                ApplySkeleton(skeletons[clothIndex], skeletonToken);

            if (clothToken["particles"] is JArray particleArray)
                ApplyParticles(cloth, particleArray);

            if (clothToken["simulation"] is JObject simulationToken)
                ApplySimulation(cloth, simulationToken);

            if (clothToken["constraints"] is JArray constraintArray)
                ApplyConstraints(cloth, constraintArray);

            if (clothToken["colliders"] is JArray colliderArray)
                ApplyColliders(collidables, colliderArray);
        }
    }

    private static void ApplySkeleton(object skeleton, JObject skeletonToken)
    {
        SetValue(skeleton, "name", (string?)skeletonToken["name"]);

        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parentList = GetValue(skeleton, "parentIndices") is IList parents ? parents : null;
        var poses = GetValue(skeleton, "referencePose") is IList poseList ? poseList : null;

        if (skeletonToken["bones"] is not JArray boneArray)
            return;

        foreach (var boneToken in boneArray.OfType<JObject>())
        {
            var index = (int?)boneToken["index"] ?? -1;
            if (index < 0 || index >= bones.Count)
                continue;

            var bone = bones[index];
            SetValue(bone, "name", (string?)boneToken["name"]);
            SetValue(bone, "lockTranslation", (bool?)boneToken["lockTranslation"]);

            if (parentList != null && boneToken["parentIndex"] != null)
                SetListItem(parentList, index, boneToken["parentIndex"]!.Value<int>());

            if (poses != null && boneToken["referencePose"] is JObject poseToken)
                SetListItem(poses, index, ReadReferencePose(poseToken, poses[index]));
        }
    }

    private static Matrix4x4 ReadReferencePose(JObject poseToken, object? existing)
    {
        if (poseToken["rawRows"] is JArray rows)
            return ReadMatrixRows(rows);

        var fallback = existing is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
        var translation = ReadVector(poseToken["translation"] as JObject, new Vector4(fallback.M11, fallback.M12, fallback.M13, fallback.M14));
        var rotation = ReadVector(poseToken["rotationQuaternion"] as JObject, new Vector4(fallback.M21, fallback.M22, fallback.M23, fallback.M24));
        var scale = ReadVector(poseToken["scale"] as JObject, new Vector4(fallback.M31, fallback.M32, fallback.M33, fallback.M34));

        return new Matrix4x4(
            translation.X, translation.Y, translation.Z, translation.W,
            rotation.X, rotation.Y, rotation.Z, rotation.W,
            scale.X, scale.Y, scale.Z, scale.W,
            fallback.M41, fallback.M42, fallback.M43, fallback.M44);
    }

    private static void ApplySimulation(object cloth, JObject simulationToken)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData == null)
            return;

        SetValue(simData, "gravity", ReadVectorToken(simulationToken["gravity"], GetValue(simData, "gravity")));
        SetValue(simData, "globalDampingPerSecond", ReadFloatToken(simulationToken["globalDampingPerSecond"], GetValue(simData, "globalDampingPerSecond")));
        SetValue(simData, "collisionTolerance", ReadFloatToken(simulationToken["collisionTolerance"], GetValue(simData, "collisionTolerance")));
        SetValue(simData, "totalMass", ReadFloatToken(simulationToken["totalMass"], GetValue(simData, "totalMass")));
        SetValue(simData, "maxParticleRadius", ReadFloatToken(simulationToken["maxParticleRadius"], GetValue(simData, "maxParticleRadius")));
    }

    private static void AddTriangleRelationships(List<ParticleRelationshipRow> rows, object? simData, int particleIndex)
    {
        var indices = GetList(GetValue(simData, "triangleIndices"));
        if (indices == null)
            return;

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = ToInt(indices[i], -1);
            var b = ToInt(indices[i + 1], -1);
            var c = ToInt(indices[i + 2], -1);
            if (a != particleIndex && b != particleIndex && c != particleIndex)
                continue;

            rows.Add(new ParticleRelationshipRow
            {
                Kind = "Triangle",
                Name = $"Triangle {i / 3}",
                Particles = $"{a}, {b}, {c}",
                Details = "Virtual cloth surface used by mesh/bone deformation."
            });
        }
    }

    private static void AddConstraintRelationships(List<ParticleRelationshipRow> rows, object? simData, object cloth, int particleIndex)
    {
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();

        for (var setIndex = 0; setIndex < constraints.Count; setIndex++)
        {
            var constraint = constraints[setIndex];
            var setName = GetString(constraint, "name") ?? constraint.GetType().Name;
            var links = GetList(GetValue(constraint, "links"));
            if (links != null)
            {
                for (var linkIndex = 0; linkIndex < links.Count; linkIndex++)
                {
                    var link = links[linkIndex];
                    var a = ToInt(GetValue(link, "particleA"), -1);
                    var b = ToInt(GetValue(link, "particleB"), -1);
                    if (a != particleIndex && b != particleIndex)
                        continue;

                    rows.Add(new ParticleRelationshipRow
                    {
                        Kind = "Link",
                        Name = $"{setName} #{linkIndex}",
                        Particles = $"{a} - {b}",
                        ConstraintSetIndex = setIndex,
                        LinkIndex = linkIndex,
                        ParticleA = a,
                        ParticleB = b,
                        RestLength = ReadConstraintFloat(link, "restLength"),
                        BendMinLength = ReadConstraintFloat(link, "bendMinLength"),
                        StretchMaxLength = ReadConstraintFloat(link, "stretchMaxLength"),
                        Stiffness = ReadConstraintFloat(link, "stiffness"),
                        BendStiffness = ReadConstraintFloat(link, "bendStiffness"),
                        StretchStiffness = ReadConstraintFloat(link, "stretchStiffness"),
                        CompressionLength = ReadConstraintFloat(link, "compressionLength"),
                        Details = BuildLinkDetails(link)
                    });
                }
            }

            var locals = GetList(GetValue(constraint, "localConstraints"));
            if (locals == null)
                continue;

            for (var localIndex = 0; localIndex < locals.Count; localIndex++)
            {
                var local = locals[localIndex];
                var p = ToInt(GetValue(local, "particleIndex"), -1);
                if (p != particleIndex)
                    continue;

                rows.Add(new ParticleRelationshipRow
                {
                    Kind = "Local",
                    Name = $"{setName} #{localIndex}",
                    Particles = p.ToString(CultureInfo.InvariantCulture),
                    ConstraintSetIndex = setIndex,
                    LocalConstraintIndex = localIndex,
                    ParticleA = p,
                    MaximumDistance = ReadConstraintFloat(local, "maximumDistance")
                        ?? ReadConstraintFloat(local, "maxDistance"),
                    Details = BuildLocalConstraintDetails(local)
                });
            }
        }
    }

    private static void AddPreviewTriangles(ParticlePreviewData preview, object? simData)
    {
        var indices = GetList(GetValue(simData, "triangleIndices"));
        if (indices == null)
            return;

        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            preview.Triangles.Add(new ParticlePreviewTriangle
            {
                ParticleA = ToInt(indices[i], -1),
                ParticleB = ToInt(indices[i + 1], -1),
                ParticleC = ToInt(indices[i + 2], -1)
            });
        }
    }

    private static void AddPreviewLinks(ParticlePreviewData preview, object? simData, object cloth)
    {
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var constraintIndex = 0; constraintIndex < constraints.Count; constraintIndex++)
        {
            var constraint = constraints[constraintIndex];
            var kind = constraint.GetType().Name;
            var name = GetString(constraint, "name") ?? kind;
            preview.ConstraintSets.Add(new ParticlePreviewConstraintSet
            {
                Index = constraintIndex,
                Kind = kind,
                Name = name
            });
            var links = GetList(GetValue(constraint, "links")) ?? Array.Empty<object>();

            foreach (var link in links)
            {
                var a = ToInt(GetValue(link, "particleA"), -1);
                var b = ToInt(GetValue(link, "particleB"), -1);
                if (a < 0 || b < 0)
                    continue;

                if (kind.Contains("BendStiffness", StringComparison.Ordinal))
                {
                    var c = ToInt(GetValue(link, "particleC"), -1);
                    var d = ToInt(GetValue(link, "particleD"), -1);
                    if (c >= 0 && d >= 0)
                    {
                        preview.BendStiffnessConstraints.Add(new ParticlePreviewBendStiffness
                        {
                            ConstraintSetIndex = constraintIndex,
                            ParticleA = a,
                            ParticleB = b,
                            ParticleC = c,
                            ParticleD = d,
                            WeightA = ReadConstraintFloat(link, "weightA") ?? 0.0f,
                            WeightB = ReadConstraintFloat(link, "weightB") ?? 0.0f,
                            WeightC = ReadConstraintFloat(link, "weightC") ?? 0.0f,
                            WeightD = ReadConstraintFloat(link, "weightD") ?? 0.0f,
                            BendStiffness = ReadConstraintFloat(link, "bendStiffness") ?? 0.0f,
                            RestCurvature = ReadConstraintFloat(link, "restCurvature") ?? 0.0f
                        });
                    }
                    continue;
                }

                var key = a < b ? $"{a}:{b}:{constraintIndex}" : $"{b}:{a}:{constraintIndex}";
                if (!seen.Add(key))
                    continue;

                preview.Links.Add(new ParticlePreviewLink
                {
                    ConstraintSetIndex = constraintIndex,
                    ParticleA = a,
                    ParticleB = b,
                    Kind = kind,
                    RestLength = ReadConstraintFloat(link, "restLength"),
                    BendMinLength = ReadConstraintFloat(link, "bendMinLength"),
                    StretchMaxLength = ReadConstraintFloat(link, "stretchMaxLength"),
                    Stiffness = ReadConstraintFloat(link, "stiffness"),
                    BendStiffness = ReadConstraintFloat(link, "bendStiffness"),
                    StretchStiffness = ReadConstraintFloat(link, "stretchStiffness")
                });
            }

            foreach (var local in GetList(GetValue(constraint, "localConstraints")) ?? Array.Empty<object>())
            {
                var particleIndex = ToInt(GetValue(local, "particleIndex"), -1);
                var maximumDistance = ReadConstraintFloat(local, "maximumDistance")
                    ?? ReadConstraintFloat(local, "maxDistance")
                    ?? ReadConstraintFloat(local, "shapeRadius");
                if (particleIndex < 0 || !maximumDistance.HasValue || maximumDistance.Value < 0.0f)
                    continue;

                preview.LocalRanges.Add(new ParticlePreviewLocalRange
                {
                    ConstraintSetIndex = constraintIndex,
                    ParticleIndex = particleIndex,
                    ReferenceVertex = ToInt(GetValue(local, "referenceVertex"), -1),
                    ReferenceBufferIndex = ToInt(GetValue(constraint, "referenceMeshBufferIdx"), 0),
                    MaximumDistance = maximumDistance.Value,
                    MinNormalDistance = ReadConstraintFloat(local, "minNormalDistance") ?? float.NegativeInfinity,
                    MaxNormalDistance = ReadConstraintFloat(local, "maxNormalDistance") ?? float.PositiveInfinity,
                    Stiffness = ReadConstraintFloat(local, "stiffness")
                        ?? ReadConstraintFloat(constraint, "stiffness")
                        ?? 1.0f,
                    ApplyNormalComponent = ToBool(GetValue(constraint, "applyNormalComponent"))
                });
            }
        }
    }

    private void AddPreviewSimulationConfig(ParticlePreviewData preview, int clothIndex)
    {
        var config = GetClothRuntimeData(clothIndex).Operators
            .Where(op => string.Equals(op.ClassName, "hclSimulateOperator", StringComparison.Ordinal))
            .SelectMany(op => op.SimulateConfigs)
            .FirstOrDefault();
        if (config == null)
        {
            preview.ConstraintExecution.AddRange(preview.ConstraintSets.Select(set => set.Index));
            preview.ConstraintExecution.Add(-1);
            return;
        }

        preview.ConstraintExecution.AddRange(config.ConstraintExecution);
        // HKX2 exposes hclSimulateOperator's -1 collision opcode as the array
        // terminator on some platforms. Keep the runtime sequence explicit for
        // the preview even when that sentinel is not returned as an element.
        if (!preview.ConstraintExecution.Contains(-1))
            preview.ConstraintExecution.Add(-1);
        preview.SimulationSubSteps = Math.Max(1, (int)config.SubSteps);
        preview.SimulationSolveIterations = Math.Max(1, (int)config.NumberOfSolveIterations);
        preview.AdaptConstraintStiffness = config.AdaptConstraintStiffness;
    }

    private static void AddPreviewSkeleton(ParticlePreviewData preview, hkRootLevelContainer root, int clothIndex)
    {
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex);
        if (skeleton == null)
            return;

        var bones = GetList(GetValue(skeleton, "bones")) ?? Array.Empty<object>();
        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();
        var worldTransforms = new Matrix4x4[bones.Count];

        for (var i = 0; i < bones.Count; i++)
        {
            var local = PoseToLocalMatrix(poses.ElementAtOrDefault(i));
            var parentIndex = ToInt(parents.ElementAtOrDefault(i), -1);
            var world = parentIndex >= 0 && parentIndex < i
                ? local * worldTransforms[parentIndex]
                : local;

            worldTransforms[i] = world;
            preview.Bones.Add(new BonePreviewPoint
            {
                Index = i,
                ParentIndex = parentIndex,
                Name = GetString(bones[i], "name") ?? $"Bone {i}",
                Position = new Vector3(world.M41, world.M42, world.M43),
                AxisX = NormalizeOr(new Vector3(world.M11, world.M12, world.M13), Vector3.UnitX),
                AxisY = NormalizeOr(new Vector3(world.M21, world.M22, world.M23), Vector3.UnitY),
                AxisZ = NormalizeOr(new Vector3(world.M31, world.M32, world.M33), Vector3.UnitZ)
            });
        }

        var rootBone = preview.Bones.FirstOrDefault(x => x.ParentIndex < 0)
            ?? preview.Bones.FirstOrDefault(x => x.Index == 0);
        if (rootBone != null)
        {
            preview.ViewRoot = rootBone.Position;
            preview.HasViewRoot = true;
        }
    }

    private static void AddPreviewBoneBindings(ParticlePreviewData preview, object cloth)
    {
        foreach (var op in GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>())
        {
            if (GetValue(op, "triangleBonePairs") is not IList pairs)
                continue;

            var localTransforms = (GetList(GetValue(op, "localBoneTransforms")) ?? Array.Empty<object>())
                .OfType<Matrix4x4>()
                .ToArray();
            var boneAxis = ToInt(GetValue(op, "boneAxis"), 3);
            for (var pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
            {
                var pair = pairs[pairIndex];
                var boneIndex = ToInt(GetValue(pair, "boneOffset"), -1) / 64;
                var triangleIndex = ToInt(GetValue(pair, "triangleOffset"), -1) / 6;
                if (boneIndex < 0 || triangleIndex < 0 || triangleIndex >= preview.Triangles.Count)
                    continue;

                var triangle = preview.Triangles[triangleIndex];
                preview.BoneBindings.Add(new ParticlePreviewBoneBinding
                {
                    BoneIndex = boneIndex,
                    ParticleA = triangle.ParticleA,
                    ParticleB = triangle.ParticleB,
                    ParticleC = triangle.ParticleC,
                    BoneAxis = boneAxis,
                    LocalBoneTransform = pairIndex < localTransforms.Length
                        ? localTransforms[pairIndex]
                        : Matrix4x4.Identity
                });
            }
        }
    }

    private void AddPreviewParticleAttachments(ParticlePreviewData preview, int clothIndex)
    {
        var moveOperators = GetClothRuntimeData(clothIndex).Operators
            .Where(op => string.Equals(op.ClassName, "hclMoveParticlesOperator", StringComparison.Ordinal) &&
                         op.VertexParticlePairs.Count > 0)
            .ToArray();
        var skinsByBuffer = GetSkinOperatorData(clothIndex)
            .GroupBy(skin => skin.OutputBufferIndex)
            .ToDictionary(
                group => group.Key,
                group => group.Select(BuildSkinVertexBoneWeights).ToArray());
        var attachments = new Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>>();
        foreach (var move in moveOperators)
        {
            var referenceBuffer = move.ReferenceBufferIndex ?? 0;
            if (!skinsByBuffer.TryGetValue(referenceBuffer, out var skinMaps))
                continue;

            foreach (var pair in move.VertexParticlePairs)
            {
                if (attachments.ContainsKey(pair.ParticleIndex))
                    continue;
                var weights = skinMaps
                    .Select(map => map.TryGetValue(pair.VertexIndex, out var value) ? value : null)
                    .FirstOrDefault(value => value is { Count: > 0 });
                if (weights != null)
                    attachments.Add(pair.ParticleIndex, weights);
            }
        }

        foreach (var (particleIndex, weights) in attachments.OrderBy(entry => entry.Key))
        {
            var attachment = new ParticlePreviewAttachment { ParticleIndex = particleIndex };
            attachment.BoneWeights.AddRange(weights);
            preview.ParticleAttachments.Add(attachment);
        }

        foreach (var range in preview.LocalRanges.Where(range => range.ReferenceVertex >= 0))
        {
            if (!skinsByBuffer.TryGetValue(range.ReferenceBufferIndex, out var skinMaps))
                continue;
            var weights = skinMaps
                .Select(map => map.TryGetValue(range.ReferenceVertex, out var value) ? value : null)
                .FirstOrDefault(value => value is { Count: > 0 });
            if (weights != null)
                range.ReferenceBoneWeights.AddRange(weights);
        }
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> BuildSkinVertexBoneWeights(
        SkinOperatorData skin)
    {
        var result = new Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>>();
        var positionBlocks = skin.LocalUnpackedPositionBlocks.Count > 0
            ? skin.LocalUnpackedPositionBlocks
            : skin.LocalPositionBlocks;
        var positionBlockIndex = 0;

        foreach (var blendArray in skin.BlendArrays)
        {
            foreach (var block in blendArray.Blocks)
            {
                var positions = positionBlockIndex < positionBlocks.Count
                    ? positionBlocks[positionBlockIndex].Positions
                    : Array.Empty<Vector4>();
                positionBlockIndex++;

                for (var slot = 0; slot < blendArray.VertexCount && slot < block.VertexIndices.Count; slot++)
                {
                    var vertexIndex = block.VertexIndices[slot];
                    if (vertexIndex == ushort.MaxValue)
                        continue;

                    var candidates = new List<(int BoneIndex, float Weight)>();
                    for (var influence = 0; influence < blendArray.BlendCount; influence++)
                    {
                        var packedIndex = checked(slot * blendArray.BlendCount + influence);
                        if (packedIndex >= block.BoneIndices.Count)
                            continue;
                        var subsetIndex = block.BoneIndices[packedIndex];
                        if (subsetIndex >= skin.TransformSubset.Count)
                            continue;

                        var weight = block.BoneWeights.Count > packedIndex
                            ? block.BoneWeights[packedIndex] / 255f
                            : positions.Count > packedIndex && float.IsFinite(positions[packedIndex].W)
                                ? MathF.Abs(positions[packedIndex].W)
                                : 1f;
                        if (weight > 0.000001f)
                            candidates.Add((skin.TransformSubset[subsetIndex], weight));
                    }

                    if (candidates.Count == 0)
                        continue;
                    var combined = candidates
                        .GroupBy(candidate => candidate.BoneIndex)
                        .Select(group => new ParticlePreviewBoneWeight
                        {
                            BoneIndex = group.Key,
                            Weight = group.Sum(candidate => candidate.Weight)
                        })
                        .ToArray();
                    var total = combined.Sum(candidate => candidate.Weight);
                    if (total <= 0.000001f)
                        continue;
                    foreach (var candidate in combined)
                        candidate.Weight /= total;
                    result[vertexIndex] = combined;
                }
            }
        }

        return result;
    }

    private static void AddNativeBphclPreviewSkeleton(ParticlePreviewData preview, NativeBphclSkeleton? skeleton)
    {
        if (skeleton is null)
            return;

        var worldTransforms = new Matrix4x4[skeleton.Bones.Count];
        for (var index = 0; index < skeleton.Bones.Count; index++)
        {
            var bone = skeleton.Bones[index];
            var rotation = new Quaternion(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W);
            if (rotation.LengthSquared() < 0.000001f)
                rotation = Quaternion.Identity;
            else
                rotation = Quaternion.Normalize(rotation);

            var local = Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(bone.Translation.X, bone.Translation.Y, bone.Translation.Z);
            var world = bone.ParentIndex >= 0 && bone.ParentIndex < index
                ? local * worldTransforms[bone.ParentIndex]
                : local;
            worldTransforms[index] = world;

            preview.Bones.Add(new BonePreviewPoint
            {
                Index = bone.Index,
                ParentIndex = bone.ParentIndex,
                Name = bone.Name,
                Position = new Vector3(world.M41, world.M42, world.M43),
                AxisX = NormalizeOr(new Vector3(world.M11, world.M12, world.M13), Vector3.UnitX),
                AxisY = NormalizeOr(new Vector3(world.M21, world.M22, world.M23), Vector3.UnitY),
                AxisZ = NormalizeOr(new Vector3(world.M31, world.M32, world.M33), Vector3.UnitZ)
            });
        }

        var rootBone = preview.Bones.FirstOrDefault(bone => bone.ParentIndex < 0)
            ?? preview.Bones.FirstOrDefault(bone => bone.Index == 0);
        if (rootBone is not null)
        {
            preview.ViewRoot = rootBone.Position;
            preview.HasViewRoot = true;
        }
    }

    private static void AddNativeBphclPreviewBoneBindings(ParticlePreviewData preview, NativeBphclCloth? cloth)
    {
        if (cloth is null)
            return;

        // hclSimpleMeshBoneDeform stores the destination bone and source
        // virtual-surface triangle as byte offsets. BPHCL and HKCL use the
        // same 64-byte bone transform / six-byte triangle record layout.
        foreach (var deformer in cloth.SimpleMeshBoneDeformers)
        {
            for (var pairIndex = 0; pairIndex < deformer.TriangleBonePairs.Count; pairIndex++)
            {
                var pair = deformer.TriangleBonePairs[pairIndex];
                var boneIndex = pair.BoneOffset / 64;
                var triangleIndex = pair.TriangleOffset / 6;
                if (triangleIndex < 0 || triangleIndex >= preview.Triangles.Count)
                    continue;

                var triangle = preview.Triangles[triangleIndex];
                preview.BoneBindings.Add(new ParticlePreviewBoneBinding
                {
                    BoneIndex = boneIndex,
                    ParticleA = triangle.ParticleA,
                    ParticleB = triangle.ParticleB,
                    ParticleC = triangle.ParticleC,
                    BoneAxis = checked((int)deformer.BoneAxis),
                    LocalBoneTransform = pairIndex < deformer.LocalBoneTransforms.Count
                        ? deformer.LocalBoneTransforms[pairIndex]
                        : Matrix4x4.Identity
                });
            }
        }
    }

    private static void AddNativeBphclPreviewColliders(ParticlePreviewData preview, NativeBphclDocument document, int clothIndex)
    {
        var bones = document.Skeletons.ElementAtOrDefault(clothIndex)?.Bones ?? Array.Empty<NativeBphclBone>();
        var referencedColliderItems = document.Cloths.ElementAtOrDefault(clothIndex)?.SimCloths
            .SelectMany(simulation => simulation.CollidableItemIndices)
            .ToHashSet() ?? new HashSet<int>();
        foreach (var collider in document.Colliders.Where(collider => collider.Enabled && referencedColliderItems.Contains(collider.ItemIndex)))
        {
            var boneIndex = ResolveColliderBoneIndex(collider.Name, bones.Select(bone => (bone.Index, bone.Name)), -1);
            var shape = collider.Shape;
            var kind = shape.TypeName switch
            {
                "hclSphereShape" => ColliderPreviewKind.Sphere,
                "hclTaperedCapsuleShape" => ColliderPreviewKind.TaperedCapsule,
                "hclPlaneShape" => ColliderPreviewKind.Plane,
                "hclCapsuleShape" => ColliderPreviewKind.Capsule,
                _ => ColliderPreviewKind.Point
            };

            var start = TransformNativeColliderPoint(collider, shape.Start);
            var end = TransformNativeColliderPoint(collider, shape.End);
            var planeNormal = TransformNativeColliderDirection(collider, new Vector3(shape.PlaneEquation.X, shape.PlaneEquation.Y, shape.PlaneEquation.Z));
            if (kind == ColliderPreviewKind.Plane)
            {
                var localNormal = new Vector3(shape.PlaneEquation.X, shape.PlaneEquation.Y, shape.PlaneEquation.Z);
                start = TransformNativeColliderPoint(collider, new Vector4(localNormal * -shape.PlaneEquation.W, 0.0f));
                end = start;
            }

            preview.Colliders.Add(new ColliderPreviewShape
            {
                Index = collider.Index,
                Name = collider.Name,
                BoneIndex = boneIndex,
                Start = start,
                End = end,
                Radius = shape.Radius,
                EndRadius = shape.EndRadius,
                PlaneNormal = NormalizeOr(planeNormal, Vector3.UnitY),
                Kind = kind
            });
        }
    }

    private void AddPreviewColliders(ParticlePreviewData preview, hkRootLevelContainer root, object cloth)
    {
        var collidables = GetCollidables(root);
        var clothIndex = IndexOfReference(GetClothDatas(root), cloth);

        // Static collision-mask bits are not global collider IDs. Bit n refers
        // to slot n in this simulation's own perInstanceCollidables array.
        // Keep both the order and duplicate entries intact: removing either
        // changes which collider every particle mask selects.
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"));
        var referencedCollidables = GetList(GetValue(simulation, "perInstanceCollidables"))
            ?? GetList(GetValue(cloth, "perInstanceCollidables"))
            ?? Array.Empty<object>();
        for (var collisionBit = 0; collisionBit < referencedCollidables.Count && collisionBit < 31; collisionBit++)
        {
            var collidable = referencedCollidables[collisionBit];
            var i = IndexOfReference(collidables, collidable);
            if (i < 0)
                continue;
            var shape = GetValue(collidable, "shape");
            if (shape == null)
                continue;

            var name = GetString(collidable, "name") ?? $"Collider {i}";
            var storedTransform = GetValue(collidable, "transform") is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
            var runtimeBinding = GetCollidableRuntimeBinding(root, clothIndex, collidable, storedTransform);
            var boneIndex = runtimeBinding.HasMapEntry
                ? runtimeBinding.BoneIndex
                : ToInt(GetValue(collidable, "transformIndex"), -1);
            var transform = storedTransform;
            var shapeType = shape.GetType().Name;
            var kind = shapeType switch
            {
                "hclSphereShape" => ColliderPreviewKind.Sphere,
                "hclTaperedCapsuleShape" => ColliderPreviewKind.TaperedCapsule,
                "hclPlaneShape" => ColliderPreviewKind.Plane,
                "hclCapsuleShape" => ColliderPreviewKind.Capsule,
                _ => ColliderPreviewKind.Point
            };

            var localStart = Vector4.Zero;
            var localEnd = Vector4.Zero;
            var radius = 0.0f;
            var endRadius = 0.0f;
            var planeEquation = Vector4.Zero;
            switch (kind)
            {
                case ColliderPreviewKind.Capsule:
                    localStart = GetValue(shape, "start") is Vector4 start ? start : Vector4.Zero;
                    localEnd = GetValue(shape, "end") is Vector4 end ? end : Vector4.Zero;
                    radius = Convert.ToSingle(GetValue(shape, "radius") ?? 0.0f, CultureInfo.InvariantCulture);
                    endRadius = radius;
                    break;
                case ColliderPreviewKind.Sphere:
                    localStart = GetValue(GetValue(shape, "sphere"), "pos") is Vector4 sphere ? sphere : Vector4.Zero;
                    radius = localStart.W;
                    endRadius = radius;
                    break;
                case ColliderPreviewKind.TaperedCapsule:
                    localStart = GetValue(shape, "small") is Vector4 small ? small : Vector4.Zero;
                    localEnd = GetValue(shape, "big") is Vector4 big ? big : Vector4.Zero;
                    radius = Convert.ToSingle(GetValue(shape, "smallRadius") ?? 0.0f, CultureInfo.InvariantCulture);
                    endRadius = Convert.ToSingle(GetValue(shape, "bigRadius") ?? radius, CultureInfo.InvariantCulture);
                    break;
                case ColliderPreviewKind.Plane:
                    planeEquation = GetValue(shape, "planeEquation") is Vector4 plane ? plane : Vector4.Zero;
                    localStart = new Vector4(new Vector3(planeEquation.X, planeEquation.Y, planeEquation.Z) * -planeEquation.W, 0.0f);
                    break;
            }

            preview.Colliders.Add(new ColliderPreviewShape
            {
                Index = i,
                CollisionBit = collisionBit,
                Name = name,
                BoneIndex = boneIndex,
                Start = TransformHkclColliderPoint(transform, localStart),
                End = TransformHkclColliderPoint(transform, localEnd),
                Radius = radius,
                EndRadius = endRadius,
                PlaneNormal = TransformHkclColliderDirection(transform, new Vector3(planeEquation.X, planeEquation.Y, planeEquation.Z)),
                Kind = kind
            });
        }
    }

    private static int ResolveColliderBoneIndex(string? colliderName, IReadOnlyList<object> bones, int fallbackIndex)
    {
        return ResolveColliderBoneIndex(
            colliderName,
            Enumerable.Range(0, bones.Count)
                .Select(i => (Index: i, Name: GetString(bones[i], "name") ?? string.Empty)),
            fallbackIndex);
    }

    private static int ResolveColliderBoneIndex(string? colliderName, IEnumerable<BonePreviewPoint> bones, int fallbackIndex)
    {
        return ResolveColliderBoneIndex(colliderName, bones.Select(b => (b.Index, b.Name)), fallbackIndex);
    }

    private static int ResolveColliderBoneIndex(string? colliderName, IEnumerable<(int Index, string Name)> bones, int fallbackIndex)
    {
        var boneEntries = bones.ToList();
        // HKCL's transformIndex is an explicit binding. Collider names are
        // only a useful recovery rule for older/unbound data.
        if (fallbackIndex >= 0 && boneEntries.Any(bone => bone.Index == fallbackIndex))
            return fallbackIndex;

        if (string.IsNullOrWhiteSpace(colliderName))
            return fallbackIndex;

        var byName = boneEntries
            .Where(b => !string.IsNullOrWhiteSpace(b.Name))
            .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.OrdinalIgnoreCase);

        var candidate = colliderName.StartsWith("Collidable_", StringComparison.OrdinalIgnoreCase)
            ? colliderName["Collidable_".Length..]
            : colliderName;

        while (!string.IsNullOrWhiteSpace(candidate))
        {
            if (byName.TryGetValue(candidate, out var resolved))
                return resolved;

            var lastUnderscore = candidate.LastIndexOf('_');
            if (lastUnderscore < 0)
                break;

            var suffix = candidate[(lastUnderscore + 1)..];
            if (suffix.Length == 0 || !suffix.All(char.IsDigit))
                break;

            candidate = candidate[..lastUnderscore];
        }

        return fallbackIndex;
    }

    private static Vector3 TransformByBone(Vector3 origin, Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 local)
    {
        return origin + axisX * local.X + axisY * local.Y + axisZ * local.Z;
    }

    private static Vector3 TransformNativeColliderPoint(NativeBphclCollider collider, Vector4 local) =>
        TransformByBone(
            new Vector3(collider.Translation.X, collider.Translation.Y, collider.Translation.Z),
            NormalizeOr(new Vector3(collider.AxisX.X, collider.AxisX.Y, collider.AxisX.Z), Vector3.UnitX),
            NormalizeOr(new Vector3(collider.AxisY.X, collider.AxisY.Y, collider.AxisY.Z), Vector3.UnitY),
            NormalizeOr(new Vector3(collider.AxisZ.X, collider.AxisZ.Y, collider.AxisZ.Z), Vector3.UnitZ),
            new Vector3(local.X, local.Y, local.Z));

    private static Matrix4x4 GetNativeColliderTransform(NativeBphclCollider collider) => new(
        collider.AxisX.X, collider.AxisX.Y, collider.AxisX.Z, 0.0f,
        collider.AxisY.X, collider.AxisY.Y, collider.AxisY.Z, 0.0f,
        collider.AxisZ.X, collider.AxisZ.Y, collider.AxisZ.Z, 0.0f,
        collider.Translation.X, collider.Translation.Y, collider.Translation.Z, 1.0f);

    private static Vector3 InverseTransformNativeColliderPoint(Matrix4x4 transform, Vector3 world)
    {
        var delta = world - new Vector3(transform.M41, transform.M42, transform.M43);
        var axisX = NormalizeOr(new Vector3(transform.M11, transform.M12, transform.M13), Vector3.UnitX);
        var axisY = NormalizeOr(new Vector3(transform.M21, transform.M22, transform.M23), Vector3.UnitY);
        var axisZ = NormalizeOr(new Vector3(transform.M31, transform.M32, transform.M33), Vector3.UnitZ);
        return new Vector3(Vector3.Dot(delta, axisX), Vector3.Dot(delta, axisY), Vector3.Dot(delta, axisZ));
    }

    private static Vector3 TransformNativeColliderDirection(NativeBphclCollider collider, Vector3 direction)
    {
        var transformed = new Vector3(
            collider.AxisX.X * direction.X + collider.AxisY.X * direction.Y + collider.AxisZ.X * direction.Z,
            collider.AxisX.Y * direction.X + collider.AxisY.Y * direction.Y + collider.AxisZ.Y * direction.Z,
            collider.AxisX.Z * direction.X + collider.AxisY.Z * direction.Y + collider.AxisZ.Z * direction.Z);
        return NormalizeOr(transformed, Vector3.UnitY);
    }

    // hkTransform stores three rotation rows followed by a translation row.
    // Shape points are row vectors in that local collider space.
    private static Vector3 TransformHkclColliderPoint(Matrix4x4 transform, Vector4 local)
    {
        return new Vector3(
            transform.M11 * local.X + transform.M21 * local.Y + transform.M31 * local.Z + transform.M41,
            transform.M12 * local.X + transform.M22 * local.Y + transform.M32 * local.Z + transform.M42,
            transform.M13 * local.X + transform.M23 * local.Y + transform.M33 * local.Z + transform.M43);
    }

    private static Matrix4x4 GetHkclBoneWorldTransform(object skeleton, int boneIndex)
    {
        if (boneIndex < 0)
            return Matrix4x4.Identity;

        var parents = GetList(GetValue(skeleton, "parentIndices")) ?? Array.Empty<object>();
        var poses = GetList(GetValue(skeleton, "referencePose")) ?? Array.Empty<object>();
        var chain = new Stack<int>();
        var visited = new HashSet<int>();
        var current = boneIndex;

        while (current >= 0 && current < poses.Count && visited.Add(current))
        {
            chain.Push(current);
            current = ToInt(parents.ElementAtOrDefault(current), -1);
        }

        var world = Matrix4x4.Identity;
        while (chain.Count > 0)
            world = PoseToLocalMatrix(poses[chain.Pop()]) * world;
        return world;
    }

    private static Matrix4x4 GetBphclBoneWorldTransform(NativeBphclSkeleton skeleton, int boneIndex)
    {
        if (boneIndex < 0)
            return Matrix4x4.Identity;

        var bones = skeleton.Bones.ToDictionary(bone => bone.Index);
        var chain = new Stack<NativeBphclBone>();
        var visited = new HashSet<int>();
        var currentIndex = boneIndex;
        while (currentIndex >= 0 && visited.Add(currentIndex) && bones.TryGetValue(currentIndex, out var bone))
        {
            chain.Push(bone);
            currentIndex = bone.ParentIndex;
        }

        var world = Matrix4x4.Identity;
        while (chain.Count > 0)
        {
            var bone = chain.Pop();
            var rotation = new Quaternion(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W);
            rotation = rotation.LengthSquared() < 0.000001f ? Quaternion.Identity : Quaternion.Normalize(rotation);
            var local = Matrix4x4.CreateFromQuaternion(rotation)
                * Matrix4x4.CreateTranslation(bone.Translation.X, bone.Translation.Y, bone.Translation.Z);
            world = local * world;
        }

        return world;
    }

    private static Vector4 InverseTransformHkclColliderPoint(Matrix4x4 transform, Vector3 world)
    {
        // hclCollidable uses an hkTransform-style 3x4 basis. Its fourth
        // column is not a conventional Matrix4x4 homogeneous column, so do
        // not invert the complete matrix here.
        var basis = new Matrix4x4(
            transform.M11, transform.M12, transform.M13, 0.0f,
            transform.M21, transform.M22, transform.M23, 0.0f,
            transform.M31, transform.M32, transform.M33, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);
        if (!Matrix4x4.Invert(basis, out var inverse))
            throw new InvalidOperationException("Collider transform cannot be inverted.");

        var translated = world - new Vector3(transform.M41, transform.M42, transform.M43);
        // HKCL uses row-vector transforms: local * basis + translation = world.
        return new Vector4(
            inverse.M11 * translated.X + inverse.M21 * translated.Y + inverse.M31 * translated.Z,
            inverse.M12 * translated.X + inverse.M22 * translated.Y + inverse.M32 * translated.Z,
            inverse.M13 * translated.X + inverse.M23 * translated.Y + inverse.M33 * translated.Z,
            0.0f);
    }

    private static Vector3 TransformHkclColliderDirection(Matrix4x4 transform, Vector3 direction)
    {
        var transformed = new Vector3(
            transform.M11 * direction.X + transform.M21 * direction.Y + transform.M31 * direction.Z,
            transform.M12 * direction.X + transform.M22 * direction.Y + transform.M32 * direction.Z,
            transform.M13 * direction.X + transform.M23 * direction.Y + transform.M33 * direction.Z);
        return NormalizeOr(transformed, Vector3.UnitY);
    }

    private static Vector3 NormalizeOr(Vector3 vector, Vector3 fallback)
    {
        return vector.LengthSquared() < 0.000001f ? fallback : Vector3.Normalize(vector);
    }

    private static float MaxMatrixDifference(Matrix4x4 left, Matrix4x4 right)
    {
        return new[]
        {
            MathF.Abs(left.M11 - right.M11), MathF.Abs(left.M12 - right.M12), MathF.Abs(left.M13 - right.M13), MathF.Abs(left.M14 - right.M14),
            MathF.Abs(left.M21 - right.M21), MathF.Abs(left.M22 - right.M22), MathF.Abs(left.M23 - right.M23), MathF.Abs(left.M24 - right.M24),
            MathF.Abs(left.M31 - right.M31), MathF.Abs(left.M32 - right.M32), MathF.Abs(left.M33 - right.M33), MathF.Abs(left.M34 - right.M34),
            MathF.Abs(left.M41 - right.M41), MathF.Abs(left.M42 - right.M42), MathF.Abs(left.M43 - right.M43), MathF.Abs(left.M44 - right.M44)
        }.Max();
    }

    private static Matrix4x4 PoseToLocalMatrix(object? pose)
    {
        if (pose is not Matrix4x4 matrix)
            return Matrix4x4.Identity;

        var translation = new Vector3(matrix.M11, matrix.M12, matrix.M13);
        var rotation = new Quaternion(matrix.M21, matrix.M22, matrix.M23, matrix.M24);
        if (rotation.LengthSquared() < 0.000001f)
            rotation = Quaternion.Identity;
        else
            rotation = Quaternion.Normalize(rotation);

        var scale = new Vector3(matrix.M31, matrix.M32, matrix.M33);
        if (Math.Abs(scale.X) < 0.000001f)
            scale.X = 1.0f;
        if (Math.Abs(scale.Y) < 0.000001f)
            scale.Y = 1.0f;
        if (Math.Abs(scale.Z) < 0.000001f)
            scale.Z = 1.0f;

        return Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation);
    }

    private static string BuildLinkDetails(object link)
    {
        var parts = new List<string>();
        AddDetail(parts, "rest", GetValue(link, "restLength"));
        AddDetail(parts, "stiff", GetValue(link, "stiffness"));
        AddDetail(parts, "bendMin", GetValue(link, "bendMinLength"));
        AddDetail(parts, "stretchMax", GetValue(link, "stretchMaxLength"));
        AddDetail(parts, "bendStiff", GetValue(link, "bendStiffness"));
        AddDetail(parts, "stretchStiff", GetValue(link, "stretchStiffness"));
        return parts.Count == 0 ? "No exposed scalar fields." : string.Join("; ", parts);
    }

    private static string BuildLocalConstraintDetails(object local)
    {
        var parts = new List<string>();
        AddDetail(parts, "maxDist", GetValue(local, "maximumDistance"));
        AddDetail(parts, "maxDist", GetValue(local, "maxDistance"));
        return parts.Count == 0 ? "Local particle movement limit." : string.Join("; ", parts);
    }

    private static float? ReadConstraintFloat(object source, string field)
    {
        var value = GetValue(source, field);
        return value == null ? null : Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    private static void ApplyConstraintFloat(object target, string field, float? value)
    {
        if (!value.HasValue || GetValue(target, field) == null)
            return;

        SetValue(target, field, value.Value);
    }

    private static void ScaleConstraintFloat(object target, string field, float ratio)
    {
        var current = ReadConstraintFloat(target, field);
        if (current.HasValue)
            ApplyConstraintFloat(target, field, current.Value * ratio);
    }

    private static Vector3 ToPosition(ParticleEditRow row) => new(row.X, row.Y, row.Z);

    private static void AddDetail(List<string> parts, string name, object? value)
    {
        if (value == null)
            return;

        parts.Add($"{name}={Convert.ToString(value, CultureInfo.InvariantCulture)}");
    }
    private static void ApplyParticleRows(object cloth, IEnumerable<ParticleEditRow> rows)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData == null)
            return;

        var particles = GetList(GetValue(simData, "particleDatas")) ?? Array.Empty<object>();
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetValue(pose, "positions") as IList;
        var collisionMasks = GetValue(simData, "staticCollisionMasks") as IList;
        var fixedParticles = GetValue(simData, "fixedParticles") as IList;
        fixedParticles?.Clear();

        var totalMass = 0.0f;
        var maxRadius = 0.0f;

        foreach (var row in rows)
        {
            if (row.Index < 0 || row.Index >= particles.Count)
                continue;

            var particle = particles[row.Index];
            SetValue(particle, "mass", row.Mass);
            SetValue(particle, "invMass", row.InverseMass);
            SetValue(particle, "radius", row.Radius);
            SetValue(particle, "friction", row.Friction);

            if (positions != null)
                SetListItem(positions, row.Index, new Vector4(row.X, row.Y, row.Z, row.W));

            if (collisionMasks != null)
                SetListItem(collisionMasks, row.Index, row.CollisionMask);

            if (row.Fixed)
                AddListItem(fixedParticles, row.Index);

            totalMass += row.Mass;
            maxRadius = Math.Max(maxRadius, row.Radius);
        }

        SetValue(simData, "totalMass", totalMass);
        SetValue(simData, "maxParticleRadius", maxRadius);
    }
    private static void ApplyParticles(object cloth, JArray particleArray)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        if (simData == null)
            return;

        var particles = GetList(GetValue(simData, "particleDatas")) ?? Array.Empty<object>();
        var pose = GetFirst(GetValue(simData, "simClothPoses"));
        var positions = GetValue(pose, "positions") as IList;
        var collisionMasks = GetValue(simData, "staticCollisionMasks") as IList;
        var fixedParticles = GetValue(simData, "fixedParticles") as IList;
        fixedParticles?.Clear();

        var totalMass = 0.0f;
        var maxRadius = 0.0f;

        foreach (var particleToken in particleArray.OfType<JObject>())
        {
            var index = (int?)particleToken["index"] ?? -1;
            if (index < 0 || index >= particles.Count)
                continue;

            var particle = particles[index];
            var mass = ReadFloatToken(particleToken["mass"], GetValue(particle, "mass"));
            var inverseMass = particleToken["inverseMass"] != null
                ? ReadFloatToken(particleToken["inverseMass"], GetValue(particle, "invMass"))
                : mass == 0.0f ? 0.0f : 1.0f / mass;
            var radius = ReadFloatToken(particleToken["radius"], GetValue(particle, "radius"));

            SetValue(particle, "mass", mass);
            SetValue(particle, "invMass", inverseMass);
            SetValue(particle, "radius", radius);
            SetValue(particle, "friction", ReadFloatToken(particleToken["friction"], GetValue(particle, "friction")));

            if (positions != null && particleToken["position"] != null)
                SetListItem(positions, index, ReadVectorToken(particleToken["position"], positions[index]));

            if (collisionMasks != null && particleToken["collisionMask"] != null)
                SetListItem(collisionMasks, index, particleToken["collisionMask"]!.Value<uint>());

            if ((bool?)particleToken["fixed"] == true)
                AddListItem(fixedParticles, index);

            totalMass += mass;
            maxRadius = Math.Max(maxRadius, radius);
        }

        SetValue(simData, "totalMass", totalMass);
        SetValue(simData, "maxParticleRadius", maxRadius);
    }

    private static void ApplyConstraints(object cloth, JArray constraintArray)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        var constraints = GetList(GetValue(simData, "staticConstraintSets"))
            ?? GetList(GetValue(cloth, "constraintSets"))
            ?? Array.Empty<object>();

        foreach (var constraintToken in constraintArray.OfType<JObject>())
        {
            var constraintIndex = (int?)constraintToken["index"] ?? -1;
            if (constraintIndex < 0 || constraintIndex >= constraints.Count)
                continue;

            var constraint = constraints[constraintIndex];
            SetValue(constraint, "name", (string?)constraintToken["name"]);

            if (constraintToken["links"] is JArray linkArray)
                ApplyConstraintLinks(constraint, linkArray);

            if (constraintToken["localConstraints"] is JArray localArray)
                ApplyLocalConstraints(constraint, localArray);
        }
    }

    private static void ApplyConstraintLinks(object constraint, JArray linkArray)
    {
        var links = GetList(GetValue(constraint, "links")) ?? Array.Empty<object>();
        foreach (var linkToken in linkArray.OfType<JObject>())
        {
            var index = (int?)linkToken["index"] ?? -1;
            if (index < 0 || index >= links.Count)
                continue;

            var link = links[index];
            ApplyOptionalInt(link, "particleA", linkToken["particleA"]);
            ApplyOptionalInt(link, "particleB", linkToken["particleB"]);
            ApplyOptionalFloat(link, "restLength", linkToken["restLength"]);
            ApplyOptionalFloat(link, "stiffness", linkToken["stiffness"]);
            ApplyOptionalFloat(link, "bendMinLength", linkToken["bendMinLength"]);
            ApplyOptionalFloat(link, "stretchMaxLength", linkToken["stretchMaxLength"]);
            ApplyOptionalFloat(link, "bendStiffness", linkToken["bendStiffness"]);
            ApplyOptionalFloat(link, "stretchStiffness", linkToken["stretchStiffness"]);
        }
    }

    private static void ApplyLocalConstraints(object constraint, JArray localArray)
    {
        var localConstraints = GetList(GetValue(constraint, "localConstraints")) ?? Array.Empty<object>();
        foreach (var localToken in localArray.OfType<JObject>())
        {
            var index = (int?)localToken["index"] ?? -1;
            if (index < 0 || index >= localConstraints.Count)
                continue;

            var local = localConstraints[index];
            ApplyOptionalInt(local, "particleIndex", localToken["particleIndex"]);
            ApplyOptionalFloat(local, "maximumDistance", localToken["maximumDistance"]);
            ApplyOptionalFloat(local, "maxDistance", localToken["maxDistance"]);
        }
    }

    private static void ApplyOptionalInt(object target, string fieldName, JToken? token)
    {
        if (token != null && token.Type != JTokenType.Null)
            SetValue(target, fieldName, token.Value<int>());
    }

    private static void ApplyOptionalFloat(object target, string fieldName, JToken? token)
    {
        if (token != null && token.Type != JTokenType.Null)
            SetValue(target, fieldName, token.Value<float>());
    }
    private static void ApplyColliders(IReadOnlyList<object> collidables, JArray colliderArray)
    {
        foreach (var colliderToken in colliderArray.OfType<JObject>())
        {
            var index = (int?)colliderToken["index"] ?? -1;
            if (index < 0 || index >= collidables.Count)
                continue;

            var collidable = collidables[index];
            SetValue(collidable, "name", (string?)colliderToken["name"]);

            if (colliderToken["boneIndex"] != null)
                SetValue(collidable, "transformIndex", colliderToken["boneIndex"]!.Value<int>());

            if (colliderToken["transform"] != null)
                SetValue(collidable, "transform", ReadMatrixToken(colliderToken["transform"], GetValue(collidable, "transform")));

            if (colliderToken["boneOffset"] != null)
                SetValue(collidable, "boneOffset", ReadMatrixToken(colliderToken["boneOffset"], GetValue(collidable, "boneOffset")));

            var shape = GetValue(collidable, "shape");
            if (shape == null || colliderToken["shape"] is not JObject shapeToken)
                continue;

            var start = ReadVectorToken(shapeToken["start"], GetValue(shape, "start"));
            var end = ReadVectorToken(shapeToken["end"], GetValue(shape, "end"));
            SetValue(shape, "start", start);
            SetValue(shape, "end", end);
            SetValue(shape, "radius", ReadFloatToken(shapeToken["radius"], GetValue(shape, "radius")));

            if (shapeToken["dir"] != null)
                SetValue(shape, "dir", ReadVectorToken(shapeToken["dir"], GetValue(shape, "dir")));

            if (shapeToken["capLenSqrdInv"] != null)
            {
                SetValue(shape, "capLenSqrdInv", ReadFloatToken(shapeToken["capLenSqrdInv"], GetValue(shape, "capLenSqrdInv")));
            }
            else
            {
                var delta = end - start;
                var lengthSq = delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z;
                if (lengthSq > 0.0f)
                {
                    var length = MathF.Sqrt(lengthSq);
                    SetValue(shape, "dir", new Vector4(delta.X / length, delta.Y / length, delta.Z / length, 0.0f));
                    SetValue(shape, "capLenSqrdInv", 1.0f / lengthSq);
                }
            }
        }
    }
    private static int GetParticleCount(object cloth)
    {
        var simData = GetFirst(GetValue(cloth, "simClothDatas"));
        return GetList(GetValue(simData, "particleDatas"))?.Count ?? 0;
    }

    private static IReadOnlyList<object> GetClothDatas(object root)
    {
        var container = FindFirst(root, "hclClothContainer");
        return GetList(GetValue(container, "clothDatas")) ?? Array.Empty<object>();
    }

    private static IList GetMutableClothDatas(object root)
    {
        var container = FindFirst(root, "hclClothContainer") ?? throw new InvalidOperationException("No hclClothContainer found.");
        return GetMutableList(GetValue(container, "clothDatas"), "clothDatas");
    }

    private static IReadOnlyList<object> GetSkeletons(object root)
    {
        var animationContainer = FindFirst(root, "hkaAnimationContainer");
        return GetList(GetValue(animationContainer, "skeletons")) ?? Array.Empty<object>();
    }

    private static IList GetMutableSkeletons(object root)
    {
        var animationContainer = FindFirst(root, "hkaAnimationContainer") ?? throw new InvalidOperationException("No hkaAnimationContainer found.");
        return GetMutableList(GetValue(animationContainer, "skeletons"), "skeletons");
    }

    private static IReadOnlyList<object> GetCollidables(object root)
    {
        var container = FindFirst(root, "hclClothContainer");
        return GetList(GetValue(container, "collidables")) ?? Array.Empty<object>();
    }

    private static IList GetMutableCollidables(object root)
    {
        var container = FindFirst(root, "hclClothContainer") ?? throw new InvalidOperationException("No hclClothContainer found.");
        return GetMutableList(GetValue(container, "collidables"), "collidables");
    }

    private static int IndexOfReference(IReadOnlyList<object> values, object target)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (ReferenceEquals(values[index], target))
                return index;
        }

        return -1;
    }

    private static IEnumerable<object> EnumerateReferencedCollidables(object cloth)
    {
        // Older files can keep the list directly on hclClothData, while BotW
        // stores it on each hclSimClothData. Support both layouts so every
        // editor surface follows the actual simulation dependencies.
        foreach (var collidable in GetList(GetValue(cloth, "perInstanceCollidables")) ?? Array.Empty<object>())
            yield return collidable;

        foreach (var simulation in GetList(GetValue(cloth, "simClothDatas")) ?? Array.Empty<object>())
        {
            foreach (var collidable in GetList(GetValue(simulation, "perInstanceCollidables")) ?? Array.Empty<object>())
                yield return collidable;
        }
    }

    private static object CloneForCurrentGraph(object source)
    {
        var json = JsonConvert.SerializeObject(source, RawJsonSettings());
        return JsonConvert.DeserializeObject(json, source.GetType(), RawJsonSettings())
            ?? throw new InvalidOperationException($"Could not clone {source.GetType().Name}.");
    }

    private static object? FindFirst(object root, string typeName)
    {
        return EnumerateObjects(root).FirstOrDefault(x => x.GetType().Name == typeName);
    }

    private static IEnumerable<object> EnumerateObjects(object? root)
    {
        if (root == null)
            yield break;

        var seen = new HashSet<object>(ReferenceEquality.Instance);
        var stack = new Stack<object>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (!seen.Add(item))
                continue;

            yield return item;

            if (item is string)
                continue;

            if (item is IEnumerable enumerable && item is not IDictionary)
            {
                foreach (var child in enumerable)
                {
                    if (child != null && ShouldTraverse(child.GetType()))
                        stack.Push(child);
                }

                continue;
            }

            foreach (var child in GetChildValues(item))
            {
                if (child != null && ShouldTraverse(child.GetType()))
                    stack.Push(child);
            }
        }
    }

    private static IEnumerable<object?> GetChildValues(object item)
    {
        var type = item.GetType();

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            yield return field.GetValue(item);

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
                continue;

            if (property.Name is "Signature" or "IsIdentity")
                continue;

            object? value;
            try
            {
                value = property.GetValue(item);
            }
            catch
            {
                continue;
            }

            yield return value;
        }
    }

    private static bool ShouldTraverse(Type type)
    {
        if (type.IsPrimitive || type.IsEnum)
            return false;

        if (type == typeof(string) || type == typeof(decimal) || type == typeof(Vector4) || type == typeof(Matrix4x4))
            return false;

        return true;
    }

    private static bool SetValue(object? obj, string name, object? value)
    {
        if (obj == null || value == null)
            return false;

        var type = obj.GetType();
        var candidates = new[] { name, "m_" + name };

        foreach (var candidate in candidates)
        {
            var field = type.GetField(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (field != null)
            {
                field.SetValue(obj, ConvertForType(value, field.FieldType));
                return true;
            }

            var property = type.GetProperty(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is { CanWrite: true } && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(obj, ConvertForType(value, property.PropertyType));
                return true;
            }
        }

        return false;
    }

    private static object EnsureObjectValue(object owner, string name, string displayName)
    {
        var existing = GetValue(owner, name);
        if (existing != null)
            return existing;

        var type = owner.GetType();
        var member = new[] { name, "m_" + name }
            .Select(candidate =>
                (MemberInfo?)type.GetField(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase) ??
                type.GetProperty(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase))
            .FirstOrDefault(candidate => candidate != null);
        var memberType = member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property when property.CanWrite => property.PropertyType,
            _ => null
        };
        if (memberType == null)
            throw new InvalidOperationException($"The {displayName} member could not be located.");

        var valueType = Nullable.GetUnderlyingType(memberType) ?? memberType;
        var value = Activator.CreateInstance(valueType)
            ?? throw new InvalidOperationException($"PhysicsTool could not create the embedded {displayName} value.");
        if (!SetValue(owner, name, value))
            throw new InvalidOperationException($"PhysicsTool could not assign the embedded {displayName} value.");

        return value;
    }

    private static object? ConvertForType(object? value, Type targetType)
    {
        if (value == null)
            return null;

        var actualTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualTarget.IsInstanceOfType(value))
            return value;

        if (actualTarget.IsEnum)
            return Enum.ToObject(actualTarget, value);

        return Convert.ChangeType(value, actualTarget, CultureInfo.InvariantCulture);
    }

    private static void SetListItem(IList list, int index, object? value)
    {
        if (index < 0 || index >= list.Count)
            return;

        var targetType = GetListElementType(list) ?? list[index]?.GetType() ?? typeof(object);
        list[index] = ConvertForType(value, targetType);
    }

    private static void AddListItem(IList? list, object? value)
    {
        if (list == null)
            return;

        var targetType = GetListElementType(list) ?? typeof(object);
        list.Add(ConvertForType(value, targetType));
    }

    private static Type? GetListElementType(IList list)
    {
        return GetCollectionElementType(list.GetType());
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();

        return type.GetInterfaces()
            .Concat(new[] { type })
            .FirstOrDefault(x => x.IsGenericType &&
                (x.GetGenericTypeDefinition() == typeof(IList<>) ||
                 x.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
            ?.GetGenericArguments()[0];
    }

    private static float ReadFloatToken(JToken? token, object? fallback)
    {
        if (token == null || token.Type == JTokenType.Null)
            return fallback == null ? 0.0f : Convert.ToSingle(fallback, CultureInfo.InvariantCulture);

        return token.Value<float>();
    }

    private static Vector4 ReadVectorToken(JToken? token, object? fallback)
    {
        var fallbackVector = fallback is Vector4 vector ? vector : Vector4.Zero;
        return token switch
        {
            JObject obj => ReadVector(obj, fallbackVector),
            _ => fallbackVector
        };
    }

    private static Vector4 ReadVector(JObject? obj, Vector4 fallback)
    {
        if (obj == null)
            return fallback;

        return new Vector4(
            ReadFloatProperty(obj, "x", fallback.X),
            ReadFloatProperty(obj, "y", fallback.Y),
            ReadFloatProperty(obj, "z", fallback.Z),
            ReadFloatProperty(obj, "w", fallback.W));
    }

    private static float ReadFloatProperty(JObject obj, string name, float fallback)
    {
        return obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token)
            ? token.Value<float>()
            : fallback;
    }

    private static Matrix4x4 ReadMatrixToken(JToken? token, object? fallback)
    {
        var fallbackMatrix = fallback is Matrix4x4 matrix ? matrix : Matrix4x4.Identity;
        if (token is JObject obj && obj["rows"] is JArray rows)
            return ReadMatrixRows(rows);

        return fallbackMatrix;
    }

    private static Matrix4x4 ReadMatrixRows(JArray rows)
    {
        var r0 = ReadMatrixRow(rows.ElementAtOrDefault(0));
        var r1 = ReadMatrixRow(rows.ElementAtOrDefault(1));
        var r2 = ReadMatrixRow(rows.ElementAtOrDefault(2));
        var r3 = ReadMatrixRow(rows.ElementAtOrDefault(3));

        return new Matrix4x4(
            r0[0], r0[1], r0[2], r0[3],
            r1[0], r1[1], r1[2], r1[3],
            r2[0], r2[1], r2[2], r2[3],
            r3[0], r3[1], r3[2], r3[3]);
    }

    private static float[] ReadMatrixRow(JToken? row)
    {
        var values = row as JArray ?? new JArray();
        return new[]
        {
            values.ElementAtOrDefault(0)?.Value<float>() ?? 0.0f,
            values.ElementAtOrDefault(1)?.Value<float>() ?? 0.0f,
            values.ElementAtOrDefault(2)?.Value<float>() ?? 0.0f,
            values.ElementAtOrDefault(3)?.Value<float>() ?? 0.0f
        };
    }
    private static object? GetValue(object? obj, string name)
    {
        if (obj == null)
            return null;

        var type = obj.GetType();
        var candidates = new[] { name, "m_" + name };

        foreach (var candidate in candidates)
        {
            var field = type.GetField(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (field != null)
                return field.GetValue(obj);

            var property = type.GetProperty(candidate, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is { CanRead: true } && property.GetIndexParameters().Length == 0)
                return property.GetValue(obj);
        }

        return null;
    }

    private static string? GetString(object? obj, string name)
    {
        return GetValue(obj, name) as string;
    }

    private static IReadOnlyList<object>? GetList(object? value)
    {
        if (value == null || value is string)
            return null;

        if (value is IEnumerable enumerable)
            return enumerable.Cast<object>().ToList();

        return null;
    }

    private static IList GetMutableList(object? value, string name)
    {
        if (value is IList list)
            return list;

        throw new InvalidOperationException($"{name} is not a mutable list.");
    }

    private static object? GetFirst(object? value)
    {
        return GetList(value)?.FirstOrDefault();
    }

    private static int ToInt(object? value, int fallback)
    {
        if (value == null)
            return fallback;

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static uint ToUInt32(object? value, uint fallback)
    {
        if (value == null)
            return fallback;

        try
        {
            return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ToBool(object? value)
    {
        if (value is bool b)
            return b;

        return value != null && bool.TryParse(value.ToString(), out var parsed) && parsed;
    }

    private static string FormatSimpleValue(object? value)
    {
        return value switch
        {
            null => "(none)",
            Vector4 vector => $"[{vector.X}, {vector.Y}, {vector.Z}, {vector.W}]",
            Matrix4x4 => "(matrix)",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "(none)"
        };
    }

    private static JToken? ToToken(object? value)
    {
        return value switch
        {
            null => null,
            Vector4 vector => Vector(vector.X, vector.Y, vector.Z, vector.W),
            Matrix4x4 matrix => new JObject { ["rows"] = MatrixRows(matrix) },
            string text => text,
            bool boolean => boolean,
            byte or sbyte or short or ushort or int or uint or long or ulong => JToken.FromObject(value),
            float f => f,
            double d => d,
            decimal m => m,
            _ => JToken.FromObject(value, RawJsonSerializer())
        };
    }

    private static JObject Vector(float x, float y, float z, float w)
    {
        return new JObject
        {
            ["x"] = x,
            ["y"] = y,
            ["z"] = z,
            ["w"] = w
        };
    }

    private static JArray MatrixRows(Matrix4x4 matrix)
    {
        return new JArray
        {
            new JArray(matrix.M11, matrix.M12, matrix.M13, matrix.M14),
            new JArray(matrix.M21, matrix.M22, matrix.M23, matrix.M24),
            new JArray(matrix.M31, matrix.M32, matrix.M33, matrix.M34),
            new JArray(matrix.M41, matrix.M42, matrix.M43, matrix.M44)
        };
    }

    private static string CompressJson(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);

        return Convert.ToBase64String(output.ToArray());
    }

    private static string DecompressJson(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            throw new InvalidOperationException("The technical HKX2 payload is empty.");

        var bytes = Convert.FromBase64String(encoded);
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
    private static string SerializeRaw(hkRootLevelContainer root)
    {
        return JsonConvert.SerializeObject(root, Formatting.Indented, RawJsonSettings());
    }

    private static hkRootLevelContainer DeserializeRoot(string json)
    {
        return JsonConvert.DeserializeObject<hkRootLevelContainer>(json, RawJsonSettings())
            ?? throw new InvalidOperationException("JSON did not contain an HKX root object.");
    }

    private static JsonSerializer RawJsonSerializer()
    {
        return JsonSerializer.Create(RawJsonSettings());
    }

    private static JsonSerializerSettings RawJsonSettings()
    {
        return new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            PreserveReferencesHandling = PreserveReferencesHandling.Objects,
            TypeNameHandling = TypeNameHandling.Auto,
            ContractResolver = new HavokContractResolver(),
            Converters = { new Vector4JsonConverter(), new Matrix4x4JsonConverter() }
        };
    }

    private sealed class HavokContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            if (property.PropertyName?.StartsWith("m_", StringComparison.Ordinal) == true)
                property.PropertyName = property.PropertyName[2..];

            if (property.PropertyName is "Signature" or "IsIdentity")
                property.Ignored = true;

            return property;
        }
    }

    private sealed class Vector4JsonConverter : JsonConverter<Vector4>
    {
        public override void WriteJson(JsonWriter writer, Vector4 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.X);
            writer.WritePropertyName("y");
            writer.WriteValue(value.Y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.Z);
            writer.WritePropertyName("w");
            writer.WriteValue(value.W);
            writer.WriteEndObject();
        }

        public override Vector4 ReadJson(JsonReader reader, Type objectType, Vector4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            return new Vector4(
                ReadFloat(obj, "x", "X"),
                ReadFloat(obj, "y", "Y"),
                ReadFloat(obj, "z", "Z"),
                ReadFloat(obj, "w", "W"));
        }
    }

    private sealed class Matrix4x4JsonConverter : JsonConverter<Matrix4x4>
    {
        public override void WriteJson(JsonWriter writer, Matrix4x4 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("rows");
            MatrixRows(value).WriteTo(writer);
            writer.WriteEndObject();
        }

        public override Matrix4x4 ReadJson(JsonReader reader, Type objectType, Matrix4x4 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);

            if (obj["rows"] is JArray rows && rows.Count >= 4)
            {
                var r0 = ReadRow(rows[0]);
                var r1 = ReadRow(rows[1]);
                var r2 = ReadRow(rows[2]);
                var r3 = ReadRow(rows[3]);
                return new Matrix4x4(
                    r0[0], r0[1], r0[2], r0[3],
                    r1[0], r1[1], r1[2], r1[3],
                    r2[0], r2[1], r2[2], r2[3],
                    r3[0], r3[1], r3[2], r3[3]);
            }

            return new Matrix4x4(
                ReadFloat(obj, "M11"), ReadFloat(obj, "M12"), ReadFloat(obj, "M13"), ReadFloat(obj, "M14"),
                ReadFloat(obj, "M21"), ReadFloat(obj, "M22"), ReadFloat(obj, "M23"), ReadFloat(obj, "M24"),
                ReadFloat(obj, "M31"), ReadFloat(obj, "M32"), ReadFloat(obj, "M33"), ReadFloat(obj, "M34"),
                ReadFloat(obj, "M41"), ReadFloat(obj, "M42"), ReadFloat(obj, "M43"), ReadFloat(obj, "M44"));
        }

        private static float[] ReadRow(JToken row)
        {
            var values = row as JArray ?? new JArray();
            return new[]
            {
                values.ElementAtOrDefault(0)?.Value<float>() ?? 0,
                values.ElementAtOrDefault(1)?.Value<float>() ?? 0,
                values.ElementAtOrDefault(2)?.Value<float>() ?? 0,
                values.ElementAtOrDefault(3)?.Value<float>() ?? 0
            };
        }
    }

    private static float ReadFloat(JObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                return token.Value<float>();
        }

        return 0;
    }

    private sealed class ReferenceEquality : IEqualityComparer<object>
    {
        public static readonly ReferenceEquality Instance = new();

        public new bool Equals(object? x, object? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
