using System.Numerics;

namespace HKCLTool;

public sealed record SidecarBonePreviewPose(
    string Name,
    Vector3 Position,
    Quaternion Rotation,
    Vector3 Scale,
    bool IsDriven);

public sealed record SidecarPreviewEvaluation(
    IReadOnlyList<SidecarBonePreviewPose> Poses,
    IReadOnlyList<string> MissingDriverBones);

/// <summary>
/// Evaluates support/helper-bone graphs for the visual FBX overlay. This never
/// mutates the loaded physics document; it only builds the pose drawn by the
/// editor while an animation is playing.
/// </summary>
internal static class SidecarPreviewEvaluator
{
    private const uint NameHash = 1579384326u;
    private const uint BoneIdHash = 3407736066u;
    private const uint BaseBoneIdHash = 4279981158u;
    private const uint BaseTranslationHash = 4181521146u;
    private const uint BaseRotationHash = 1609157221u;
    private const uint AimAxisHash = 3442436866u;
    private const uint UpAxisHash = 907422927u;
    private const uint CurveDriverHash = 249727330u;
    private const uint CurveAttributeHash = 75375380u;
    private const uint CurveKeyCountHash = 139113150u;
    private const uint CurveKey0Hash = 1692570776u;
    private const uint CurveKey1Hash = 333816846u;
    private const uint CurveKey2Hash = 2330785204u;
    private const uint CurveKey3Hash = 4260087074u;
    private const uint OutputConnection0Hash = 2390703269u;
    private const uint OutputConnection1Hash = 918772672u;
    private const uint RollOutputHash = 2869652774u;
    private const uint BendHOutputHash = 3079588026u;
    private const uint BendVOutputHash = 122218518u;
    private const uint TranslateXOutputHash = 3595547632u;
    private const uint TranslateYOutputHash = 1861473429u;
    private const uint TranslateZOutputHash = 2084993915u;
    private static readonly uint TranslateDrivenTypeHash = BotwAampWriter.Crc32("translate_driven_type");
    private static readonly uint TranslateDrivenIdHash = BotwAampWriter.Crc32("translate_driven_id");
    private static readonly uint RotateDrivenTypeHash = BotwAampWriter.Crc32("rotate_driven_type");
    private static readonly uint RotateDrivenIdHash = BotwAampWriter.Crc32("rotate_driven_id");
    private static readonly uint AimDrivenTypeHash = BotwAampWriter.Crc32("aim_driven_type");
    private static readonly uint AimDrivenIdHash = BotwAampWriter.Crc32("aim_driven_id");

    private const uint BphyssbBoneListHash = 3240679892u;
    private const uint BphyssbCurveListHash = 1471120930u;
    private const uint BphyssbSingleListHash = 2123203909u;
    private const uint BphyssbDoubleListHash = 2452354690u;
    private const uint BphyssbMainListHash = 3785260662u;
    private const uint BphyssbSupportListHash = 2012889698u;
    private const uint BoneAttributeHash = 554147540u;
    private const uint ConnectionHash = 704082790u;
    private const uint WeightHash = 130897217u;
    private const uint Connection0Hash = 2693310104u;
    private const uint Weight0Hash = 2822815546u;
    private const uint Connection1Hash = 3616511502u;
    private const uint Weight1Hash = 3746009004u;
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

    public static SidecarPreviewEvaluation Evaluate(
        BphhbDocumentSummary document,
        int groupIndex,
        IReadOnlyList<VisualModelBone> currentBones,
        IReadOnlyList<VisualModelBone> restBones)
    {
        var archive = document.Archive;
        var group = BphhbBridge.GetDriverGroups(document).ElementAtOrDefault(groupIndex);
        var data = archive?.Root.Children.SingleOrDefault();
        if (group == null || data == null)
            return Empty;

        var bones = data.FindChild("bone_list");
        var curves = data.FindChild("connection_curve_list");
        var outputs = data.FindChild("output_list");
        var drivers = data.FindChild("driver_bone_list");
        var driven = data.FindChild("driven_bone_list");
        var poses = data.FindChild("pose_driven_list");
        if (bones == null || curves == null || outputs == null || drivers == null || driven == null || poses == null)
            return Empty;

        var boneNames = ReadBoneNames(bones);
        var current = IndexBones(currentBones);
        var rest = IndexBones(restBones);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SidecarBonePreviewPose>();
        var driverValues = new Dictionary<int, DriverValues>();

        foreach (var driverIndex in group.DriverIndices)
        {
            if ((uint)driverIndex >= (uint)drivers.Objects.Count)
                continue;
            var source = drivers.Objects[driverIndex];
            var boneIndex = GetInt(source, BoneIdHash);
            var baseIndex = GetInt(source, BaseBoneIdHash);
            var name = BoneName(boneNames, boneIndex);
            if (!TryGetBone(current, name, out var currentBone) || !TryGetBone(rest, name, out var restBone))
            {
                missing.Add(name);
                continue;
            }

            var values = MeasureDriver(
                currentBone,
                restBone,
                GetBone(current, boneNames, baseIndex),
                GetBone(rest, boneNames, baseIndex),
                GetVector3(source, AimAxisHash, Vector3.UnitZ),
                GetVector3(source, UpAxisHash, Vector3.UnitY));
            driverValues[driverIndex] = values;
            result.Add(new SidecarBonePreviewPose(
                name,
                currentBone.Position,
                currentBone.Rotation,
                currentBone.Scale,
                false));
        }

        var curveValues = curves.Objects.Select(curve =>
        {
            var driverIndex = GetInt(curve, CurveDriverHash);
            var attribute = GetInt(curve, CurveAttributeHash);
            return driverValues.TryGetValue(driverIndex, out var values)
                ? EvaluateCurve(ReadKeys(curve), values.GetAttribute(attribute))
                : 0f;
        }).ToArray();

        float ReadOutput(int outputIndex)
        {
            if ((uint)outputIndex >= (uint)outputs.Objects.Count)
                return 0f;
            var output = outputs.Objects[outputIndex];
            return ReadCurveValue(curveValues, GetInt(output, OutputConnection0Hash)) +
                ReadCurveValue(curveValues, GetInt(output, OutputConnection1Hash));
        }

        for (var index = 0; index < driven.Objects.Count; index++)
        {
            var drivenRecord = driven.Objects[index];
            var poseIndex = GetPoseDrivenIndex(drivenRecord, poses.Objects.Count, index);
            if (poseIndex < 0)
                continue;
            var drivenBoneIndex = GetInt(drivenRecord, BoneIdHash);
            if (!group.DrivenBoneIndices.Contains(drivenBoneIndex))
                continue;
            var pose = poses.Objects[poseIndex];
            var name = BoneName(boneNames, drivenBoneIndex);
            result.Add(BuildDrivenPose(
                name,
                drivenBoneIndex,
                GetInt(pose, BaseBoneIdHash),
                GetVector3(pose, BaseTranslationHash, Vector3.Zero),
                GetQuaternion(pose, BaseRotationHash),
                GetVector3(pose, AimAxisHash, Vector3.UnitZ),
                GetVector3(pose, UpAxisHash, Vector3.UnitY),
                new Vector3(
                    ReadOutput(GetInt(pose, TranslateXOutputHash)),
                    ReadOutput(GetInt(pose, TranslateYOutputHash)),
                    ReadOutput(GetInt(pose, TranslateZOutputHash))),
                ReadOutput(GetInt(pose, RollOutputHash)),
                ReadOutput(GetInt(pose, BendHOutputHash)),
                ReadOutput(GetInt(pose, BendVOutputHash)),
                boneNames,
                current,
                rest));
        }

        return new SidecarPreviewEvaluation(result, missing.OrderBy(name => name).ToArray());
    }

    private static int GetPoseDrivenIndex(AampReadObject driven, int poseCount, int legacyFallback)
    {
        foreach (var (typeHash, idHash) in new[]
        {
            (TranslateDrivenTypeHash, TranslateDrivenIdHash),
            (RotateDrivenTypeHash, RotateDrivenIdHash),
            (AimDrivenTypeHash, AimDrivenIdHash)
        })
        {
            var id = GetInt(driven, idHash);
            // Driven type zero addresses pose_driven_list. Other types address
            // the specialized position/rotation/aim tables.
            if (id >= 0 && id < poseCount && GetInt(driven, typeHash) is -1 or 0)
                return id;
        }

        // Early converted files omitted explicit IDs but emitted aligned lists.
        return legacyFallback >= 0 && legacyFallback < poseCount ? legacyFallback : -1;
    }

    public static SidecarPreviewEvaluation Evaluate(
        BphyssbDocumentSummary document,
        int groupIndex,
        IReadOnlyList<VisualModelBone> currentBones,
        IReadOnlyList<VisualModelBone> restBones)
    {
        var group = BphyssbBridge.GetDriverGroups(document).ElementAtOrDefault(groupIndex);
        var data = document.Archive?.Root.Children.FirstOrDefault(child =>
            child.FindChild(BphyssbBoneListHash) != null &&
            child.FindChild(BphyssbMainListHash) != null &&
            child.FindChild(BphyssbSupportListHash) != null);
        if (group == null || data == null)
            return Empty;

        var bones = data.FindChild(BphyssbBoneListHash);
        var curves = data.FindChild(BphyssbCurveListHash);
        var singles = data.FindChild(BphyssbSingleListHash);
        var doubles = data.FindChild(BphyssbDoubleListHash);
        var drivers = data.FindChild(BphyssbMainListHash);
        var support = data.FindChild(BphyssbSupportListHash);
        if (bones == null || curves == null || singles == null || drivers == null || support == null)
            return Empty;

        var boneNames = ReadBoneNames(bones);
        var current = IndexBones(currentBones);
        var rest = IndexBones(restBones);
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SidecarBonePreviewPose>();
        var driverValues = new Dictionary<int, DriverValues>();

        foreach (var driverIndex in group.DriverIndices)
        {
            if ((uint)driverIndex >= (uint)drivers.Objects.Count)
                continue;
            var source = drivers.Objects[driverIndex];
            var boneIndex = GetInt(source, TargetBoneHash);
            var baseIndex = GetInt(source, TargetSpaceHash);
            var name = BoneName(boneNames, boneIndex);
            if (!TryGetBone(current, name, out var currentBone) || !TryGetBone(rest, name, out var restBone))
            {
                missing.Add(name);
                continue;
            }

            driverValues[driverIndex] = MeasureDriver(
                currentBone,
                restBone,
                GetBone(current, boneNames, baseIndex),
                GetBone(rest, boneNames, baseIndex),
                GetVector3(source, TargetAimHash, Vector3.UnitZ),
                GetVector3(source, TargetUpHash, Vector3.UnitY));
            result.Add(new SidecarBonePreviewPose(
                name,
                currentBone.Position,
                currentBone.Rotation,
                currentBone.Scale,
                false));
        }

        float ReadOutput(int handle, bool translationInput)
        {
            if (handle < 0)
                return 0f;

            float ReadConnection(AampReadObject output, uint connectionHash, uint weightHash)
            {
                var packedCurve = GetInt(output, connectionHash);
                var curveIndex = packedCurve < 0 ? -1 : packedCurve & 0xffff;
                if ((uint)curveIndex >= (uint)curves.Objects.Count)
                    return 0f;
                var curve = curves.Objects[curveIndex];
                var packedAttribute = GetInt(curve, BoneAttributeHash);
                var driverIndex = (int)((uint)packedAttribute >> 16);
                var channel = packedAttribute & 0xffff;
                if (!driverValues.TryGetValue(driverIndex, out var values))
                    return 0f;
                var attribute = translationInput ? channel + 3 : channel switch
                {
                    0 => 1,
                    1 => 2,
                    2 => 0,
                    _ => -1
                };
                var weight = output.Find(weightHash)?.AsSingle() ?? 1f;
                return EvaluateCurve(ReadKeys(curve), values.GetAttribute(attribute)) * weight;
            }

            if (((uint)handle & 0xffff0000u) == 0x00010000u)
            {
                var index = handle & 0xffff;
                if (doubles == null || (uint)index >= (uint)doubles.Objects.Count)
                    return 0f;
                var output = doubles.Objects[index];
                return ReadConnection(output, Connection0Hash, Weight0Hash) +
                    ReadConnection(output, Connection1Hash, Weight1Hash);
            }

            if ((uint)handle >= (uint)singles.Objects.Count)
                return 0f;
            return ReadConnection(singles.Objects[handle], ConnectionHash, WeightHash);
        }

        foreach (var source in support.Objects)
        {
            var boneIndex = GetInt(source, TargetBoneHash);
            if (!group.DrivenBoneIndices.Contains(boneIndex))
                continue;
            var name = BoneName(boneNames, boneIndex);
            result.Add(BuildDrivenPose(
                name,
                boneIndex,
                GetInt(source, TargetSpaceHash),
                GetVector3(source, BaseTranslationHash, Vector3.Zero),
                GetQuaternion(source, BaseRotationHash),
                GetVector3(source, TargetAimHash, Vector3.UnitZ),
                GetVector3(source, TargetUpHash, Vector3.UnitY),
                new Vector3(
                    ReadOutput(GetInt(source, TargetTranslateXHash), true),
                    ReadOutput(GetInt(source, TargetTranslateYHash), true),
                    ReadOutput(GetInt(source, TargetTranslateZHash), true)),
                ReadOutput(GetInt(source, TargetRollHash), false),
                ReadOutput(GetInt(source, TargetBendHHash), false),
                ReadOutput(GetInt(source, TargetBendVHash), false),
                boneNames,
                current,
                rest));
        }

        return new SidecarPreviewEvaluation(result, missing.OrderBy(name => name).ToArray());
    }

    private static SidecarBonePreviewPose BuildDrivenPose(
        string name,
        int boneIndex,
        int baseBoneIndex,
        Vector3 storedTranslation,
        Quaternion storedRotation,
        Vector3 aim,
        Vector3 up,
        Vector3 translationOutput,
        float roll,
        float bendH,
        float bendV,
        IReadOnlyList<string> boneNames,
        IReadOnlyDictionary<string, VisualModelBone> current,
        IReadOnlyDictionary<string, VisualModelBone> rest)
    {
        TryGetBone(rest, name, out var restDriven);
        var currentBase = GetBone(current, boneNames, baseBoneIndex);
        var restBase = GetBone(rest, boneNames, baseBoneIndex);

        if (currentBase == null && restDriven != null && restDriven.ParentIndex >= 0)
        {
            var parentName = rest.Values.FirstOrDefault(bone => bone.Index == restDriven.ParentIndex)?.Name;
            if (!string.IsNullOrWhiteSpace(parentName))
            {
                TryGetBone(current, parentName, out currentBase);
                TryGetBone(rest, parentName, out restBase);
            }
        }

        var baseCurrentMatrix = currentBase == null ? Matrix4x4.Identity : ToMatrix(currentBase);
        var localScale = Vector3.One;
        if (restDriven != null)
        {
            var drivenRestMatrix = ToMatrix(restDriven);
            if (restBase != null && Matrix4x4.Invert(ToMatrix(restBase), out var inverseBase))
                drivenRestMatrix *= inverseBase;
            if (Matrix4x4.Decompose(drivenRestMatrix, out var modelScale, out _, out _))
                localScale = modelScale;
        }

        // The sidecar base transform is the authored bind pose used by the
        // runtime graph. The FBX bone is useful for display scale, but replacing
        // this transform with the FBX rest pose disconnects support output from
        // its declared space as soon as animation moves the driver hierarchy.
        var localRotation = storedRotation.LengthSquared() > 0.000001f
            ? Quaternion.Normalize(storedRotation)
            : Quaternion.Identity;
        var localTranslation = storedTranslation;

        var frame = BuildFrame(aim, up);
        var outputRotation = Quaternion.Normalize(
            Quaternion.CreateFromAxisAngle(frame.Aim, roll) *
            Quaternion.CreateFromAxisAngle(frame.Up, bendH) *
            Quaternion.CreateFromAxisAngle(frame.Right, bendV));
        localRotation = Quaternion.Normalize(outputRotation * localRotation);
        localTranslation += translationOutput;

        var world = Matrix4x4.CreateScale(localScale) *
            Matrix4x4.CreateFromQuaternion(localRotation) *
            Matrix4x4.CreateTranslation(localTranslation) * baseCurrentMatrix;
        if (!Matrix4x4.Decompose(world, out var worldScale, out var worldRotation, out var worldTranslation))
            return new SidecarBonePreviewPose(name, localTranslation, localRotation, localScale, true);
        return new SidecarBonePreviewPose(
            name,
            worldTranslation,
            Quaternion.Normalize(worldRotation),
            worldScale,
            true);
    }

    private static DriverValues MeasureDriver(
        VisualModelBone current,
        VisualModelBone rest,
        VisualModelBone? currentBase,
        VisualModelBone? restBase,
        Vector3 aim,
        Vector3 up)
    {
        var currentRelative = RelativeMatrix(current, currentBase);
        var restRelative = RelativeMatrix(rest, restBase);
        Matrix4x4.Decompose(currentRelative, out _, out var currentRotation, out var currentTranslation);
        Matrix4x4.Decompose(restRelative, out _, out var restRotation, out var restTranslation);
        var deltaRotation = Quaternion.Normalize(currentRotation * Quaternion.Inverse(restRotation));
        var rotationVector = QuaternionToRotationVector(deltaRotation);
        var frame = BuildFrame(aim, up);
        var deltaTranslation = currentTranslation - restTranslation;
        return new DriverValues(
            Vector3.Dot(rotationVector, frame.Aim),
            Vector3.Dot(rotationVector, frame.Up),
            Vector3.Dot(rotationVector, frame.Right),
            deltaTranslation);
    }

    private static Matrix4x4 RelativeMatrix(VisualModelBone bone, VisualModelBone? baseBone)
    {
        var matrix = ToMatrix(bone);
        return baseBone != null && Matrix4x4.Invert(ToMatrix(baseBone), out var inverseBase)
            ? matrix * inverseBase
            : matrix;
    }

    private static Matrix4x4 ToMatrix(VisualModelBone bone) =>
        Matrix4x4.CreateScale(bone.Scale) *
        Matrix4x4.CreateFromQuaternion(bone.Rotation) *
        Matrix4x4.CreateTranslation(bone.Position);

    private static Vector3 QuaternionToRotationVector(Quaternion value)
    {
        var rotation = Quaternion.Normalize(value);
        if (rotation.W < 0f)
            rotation = new Quaternion(-rotation.X, -rotation.Y, -rotation.Z, -rotation.W);
        var sinHalf = MathF.Sqrt(MathF.Max(0f, 1f - rotation.W * rotation.W));
        if (sinHalf < 0.00001f)
            return new Vector3(rotation.X, rotation.Y, rotation.Z) * 2f;
        var angle = 2f * MathF.Atan2(sinHalf, rotation.W);
        return new Vector3(rotation.X, rotation.Y, rotation.Z) * (angle / sinHalf);
    }

    private static (Vector3 Aim, Vector3 Up, Vector3 Right) BuildFrame(Vector3 aim, Vector3 up)
    {
        aim = NormalizeOrDefault(aim, Vector3.UnitZ);
        up -= aim * Vector3.Dot(up, aim);
        up = NormalizeOrDefault(up, MathF.Abs(Vector3.Dot(aim, Vector3.UnitY)) < 0.95f ? Vector3.UnitY : Vector3.UnitX);
        var right = NormalizeOrDefault(Vector3.Cross(up, aim), Vector3.UnitX);
        up = NormalizeOrDefault(Vector3.Cross(aim, right), up);
        return (aim, up, right);
    }

    private static Vector3 NormalizeOrDefault(Vector3 value, Vector3 fallback) =>
        value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : fallback;

    private static float EvaluateCurve(IReadOnlyList<Vector4> keys, float input)
    {
        if (keys.Count == 0)
            return 0f;
        if (keys.Count == 1 || input <= keys[0].X)
            return keys[0].Y;
        if (input >= keys[^1].X)
            return keys[^1].Y;

        for (var index = 0; index + 1 < keys.Count; index++)
        {
            var first = keys[index];
            var second = keys[index + 1];
            if (input > second.X)
                continue;
            var span = second.X - first.X;
            if (MathF.Abs(span) < 0.000001f)
                return second.Y;
            var t = Math.Clamp((input - first.X) / span, 0f, 1f);
            var t2 = t * t;
            var t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * first.Y +
                (t3 - 2f * t2 + t) * span * first.W +
                (-2f * t3 + 3f * t2) * second.Y +
                (t3 - t2) * span * second.Z;
        }
        return keys[^1].Y;
    }

    private static IReadOnlyList<Vector4> ReadKeys(AampReadObject curve)
    {
        var values = new[]
        {
            curve.Find(CurveKey0Hash)?.AsVector4(),
            curve.Find(CurveKey1Hash)?.AsVector4(),
            curve.Find(CurveKey2Hash)?.AsVector4(),
            curve.Find(CurveKey3Hash)?.AsVector4()
        };
        var available = values.TakeWhile(value => value.HasValue).Select(value => value!.Value).ToArray();
        var declared = curve.Find(CurveKeyCountHash)?.AsInt32() ?? available.Length;
        return available.Take(Math.Clamp(declared, 0, available.Length)).OrderBy(key => key.X).ToArray();
    }

    private static float ReadCurveValue(IReadOnlyList<float> values, int index) =>
        (uint)index < (uint)values.Count ? values[index] : 0f;

    private static string[] ReadBoneNames(AampReadList bones) => bones.Objects
        .Select((bone, index) => bone.Find(NameHash)?.AsString() ?? $"bone_{index}")
        .ToArray();

    private static string BoneName(IReadOnlyList<string> names, int index) =>
        (uint)index < (uint)names.Count ? names[index] : $"bone_{index}";

    private static IReadOnlyDictionary<string, VisualModelBone> IndexBones(IEnumerable<VisualModelBone> bones) => bones
        .GroupBy(bone => NormalizeBoneName(bone.Name), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    private static bool TryGetBone(
        IReadOnlyDictionary<string, VisualModelBone> bones,
        string name,
        out VisualModelBone? bone) => bones.TryGetValue(NormalizeBoneName(name), out bone);

    private static VisualModelBone? GetBone(
        IReadOnlyDictionary<string, VisualModelBone> bones,
        IReadOnlyList<string> names,
        int index) => (uint)index < (uint)names.Count && TryGetBone(bones, names[index], out var bone) ? bone : null;

    private static string NormalizeBoneName(string name)
    {
        var value = name ?? string.Empty;
        var pipe = value.LastIndexOf('|');
        if (pipe >= 0)
            value = value[(pipe + 1)..];
        var colon = value.LastIndexOf(':');
        return colon >= 0 ? value[(colon + 1)..] : value;
    }

    private static int GetInt(AampReadObject item, uint hash) => item.Find(hash)?.AsInt32() ?? -1;

    private static Vector3 GetVector3(AampReadObject item, uint hash, Vector3 fallback) =>
        item.Find(hash)?.AsVector3() ?? fallback;

    private static Quaternion GetQuaternion(AampReadObject item, uint hash)
    {
        var value = item.Find(hash)?.AsVector4() ?? Vector4.UnitW;
        var quaternion = new Quaternion(value.X, value.Y, value.Z, value.W);
        return quaternion.LengthSquared() > 0.000001f ? Quaternion.Normalize(quaternion) : Quaternion.Identity;
    }

    private static readonly SidecarPreviewEvaluation Empty = new(
        Array.Empty<SidecarBonePreviewPose>(),
        Array.Empty<string>());

    private readonly record struct DriverValues(float Roll, float BendH, float BendV, Vector3 Translation)
    {
        public float GetAttribute(int attribute) => attribute switch
        {
            0 => Roll,
            1 => BendH,
            2 => BendV,
            3 => Translation.X,
            4 => Translation.Y,
            5 => Translation.Z,
            _ => 0f
        };
    }
}
