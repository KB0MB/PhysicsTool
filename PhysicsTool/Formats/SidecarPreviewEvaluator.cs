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
        IReadOnlyList<VisualModelBone> restBones,
        IReadOnlyList<SidecarDriverGroup>? preparedGroups = null)
    {
        var archive = document.Archive;
        var group = EvaluationGroup(preparedGroups ?? BphhbBridge.GetDriverGroups(document), groupIndex);
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
        var driverValues = new Dictionary<int, (Lazy<DriverValues> Rotation, Lazy<DriverValues> Translation)>();

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

            if (baseIndex >= 0 && GetBone(current, boneNames, baseIndex) == null)
            {
                missing.Add(BoneName(boneNames, baseIndex));
                continue;
            }
            // TotK caches rotation and translation separately on first use.
            // Earlier helper outputs may have changed this driver or its base.
            DriverValues Sample()
            {
                var sampled = current[NormalizeBoneName(name)];
                var space = baseIndex == -2 ? current.Values.FirstOrDefault(b => b.Index == sampled.ParentIndex)
                    : GetBone(current, boneNames, baseIndex);
                Matrix4x4.Decompose(RelativeMatrix(sampled, space), out _, out var rotation, out var translation);
                var angles = MeasureBotwRotation(
                    Quaternion.Normalize(Quaternion.Inverse(GetQuaternion(source, BaseRotationHash)) * rotation),
                    GetVector3(source, AimAxisHash, Vector3.UnitZ), GetVector3(source, UpAxisHash, Vector3.UnitY));
                return new DriverValues(angles.Z, angles.X, angles.Y,
                    translation - GetVector3(source, BaseTranslationHash, Vector3.Zero));
            }
            driverValues[driverIndex] = (new Lazy<DriverValues>(Sample), new Lazy<DriverValues>(Sample));
            result.Add(new SidecarBonePreviewPose(
                name,
                currentBone.Position,
                currentBone.Rotation,
                currentBone.Scale,
                false));
        }

        float ReadCurve(int curveIndex)
        {
            if ((uint)curveIndex >= (uint)curves.Objects.Count) return 0f;
            var curve = curves.Objects[curveIndex];
            var driverIndex = GetInt(curve, CurveDriverHash);
            var attribute = GetInt(curve, CurveAttributeHash);
            if (attribute < 0 || attribute > 5 || !driverValues.TryGetValue(driverIndex, out var values)) return 0f;
            return EvaluateTotkCurve(ReadKeys(curve),
                (attribute < 3 ? values.Rotation : values.Translation).Value.GetAttribute(attribute));
        }

        float ReadOutput(int outputIndex)
        {
            if ((uint)outputIndex >= (uint)outputs.Objects.Count)
                return 0f;
            var output = outputs.Objects[outputIndex];
            return ReadCurve(GetInt(output, OutputConnection0Hash)) +
                ReadCurve(GetInt(output, OutputConnection1Hash));
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
            var drivenPose = BuildDrivenPose(
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
                rest);
            result.Add(drivenPose);
            ApplyDrivenPose(current, drivenPose);
        }

        return new SidecarPreviewEvaluation(result.Select(p => p.IsDriven && TryGetBone(current, p.Name, out var bone)
            ? new SidecarBonePreviewPose(p.Name, bone!.Position, bone.Rotation, bone.Scale, true) : p).ToArray(),
            missing.OrderBy(name => name).ToArray());
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
        IReadOnlyList<VisualModelBone> restBones,
        IReadOnlyList<SidecarDriverGroup>? preparedGroups = null)
    {
        var group = EvaluationGroup(preparedGroups ?? BphyssbBridge.GetDriverGroups(document), groupIndex);
        var data = document.Archive?.Root.Children.FirstOrDefault(child =>
            child.FindChild(BphyssbBoneListHash) != null &&
            child.FindChild(BphyssbMainListHash) != null &&
            child.FindChild(BphyssbSupportListHash) != null);
        if (group == null || data == null)
            return Empty;

        var bones = data.FindChild(BphyssbBoneListHash);
        var curves = data.FindChild(BphyssbCurveListHash) ?? new AampReadList(BphyssbCurveListHash);
        var linears = data.FindChild("connection_linear_list");
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
        var driverValues = new Dictionary<int, (Lazy<DriverValues> Rotation, Lazy<DriverValues> Translation)>();

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

            var baseBone = GetBone(current, boneNames, baseIndex);
            if (baseIndex >= 0 && baseBone == null)
            {
                missing.Add(BoneName(boneNames, baseIndex));
                continue;
            }
            if (baseIndex == -1)
                baseBone = current.Values.FirstOrDefault(bone => bone.Index == currentBone.ParentIndex);
            // Native connections sample/cache a driver when first used. An earlier
            // support output in this group may already have moved that driver.
            DriverValues Sample()
            {
                var sampled = current[NormalizeBoneName(name)];
                var space = baseIndex == -1 ? current.Values.FirstOrDefault(b => b.Index == sampled.ParentIndex)
                    : GetBone(current, boneNames, baseIndex);
                var relative = RelativeMatrix(sampled, space);
                Matrix4x4.Decompose(relative, out _, out var relativeRotation, out var relativeTranslation);
                var angles = MeasureBotwRotation(
                    Quaternion.Normalize(Quaternion.Inverse(GetQuaternion(source, BaseRotationHash)) * relativeRotation),
                    GetVector3(source, TargetAimHash, Vector3.UnitZ), GetVector3(source, TargetUpHash, Vector3.UnitY));
                return new DriverValues(angles.Z, angles.X, angles.Y,
                    relativeTranslation - GetVector3(source, BaseTranslationHash, Vector3.Zero));
            }
            driverValues[driverIndex] = (new Lazy<DriverValues>(Sample), new Lazy<DriverValues>(Sample));
            result.Add(new SidecarBonePreviewPose(
                name,
                currentBone.Position,
                currentBone.Rotation,
                currentBone.Scale,
                false));
        }

        float ReadOutput(int handle)
        {
            if (handle < 0)
                return 0f;

            float ReadConnection(AampReadObject output, uint connectionHash, uint weightHash)
            {
                var packedCurve = GetInt(output, connectionHash);
                if (packedCurve < 0) return 0f;
                var connectionType = (uint)packedCurve >> 16;
                var connectionIndex = packedCurve & 0xffff;
                var table = connectionType == 0 ? linears : connectionType == 1 ? curves : null;
                if (table == null || connectionIndex >= table.Objects.Count)
                    return 0f;
                var curve = table.Objects[connectionIndex];
                var packedAttribute = GetInt(curve, BoneAttributeHash);
                var driverIndex = (int)((uint)packedAttribute >> 16);
                var channel = packedAttribute & 0xffff;
                if (channel > 5) return 0f;
                if (!driverValues.TryGetValue(driverIndex, out var values))
                    return 0f;
                var attribute = channel switch
                {
                    0 => 1,
                    1 => 2,
                    2 => 0,
                    >= 3 and <= 5 => channel,
                    _ => -1
                };
                var weight = output.Find(weightHash)?.AsSingle() ?? 0f;
                var input = (attribute < 3 ? values.Rotation : values.Translation).Value.GetAttribute(attribute);
                float value;
                if (connectionType == 0)
                {
                    var coefficients = curve.Find("slope_intercept");
                    value = coefficients == null ? 0f : coefficients.AsSingle() * input + BitConverter.ToSingle(coefficients.Bytes, 4);
                }
                else value = EvaluateBotwCurve(curve, input);
                return (float.IsNaN(value) ? 0f : value) * weight;
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
            var pose = BuildDrivenPose(
                name,
                boneIndex,
                GetInt(source, TargetSpaceHash),
                GetVector3(source, BaseTranslationHash, Vector3.Zero),
                GetQuaternion(source, BaseRotationHash),
                GetVector3(source, TargetAimHash, Vector3.UnitZ),
                GetVector3(source, TargetUpHash, Vector3.UnitY),
                new Vector3(
                    ReadOutput(GetInt(source, TargetTranslateXHash)),
                    ReadOutput(GetInt(source, TargetTranslateYHash)),
                    ReadOutput(GetInt(source, TargetTranslateZHash))),
                ReadOutput(GetInt(source, TargetRollHash)),
                ReadOutput(GetInt(source, TargetBendHHash)),
                ReadOutput(GetInt(source, TargetBendVHash)),
                boneNames,
                current,
                rest, botw: true);
            result.Add(pose);
            ApplyDrivenPose(current, pose);
        }

        return new SidecarPreviewEvaluation(result.Select(p => p.IsDriven && TryGetBone(current, p.Name, out var bone)
            ? new SidecarBonePreviewPose(p.Name, bone!.Position, bone.Rotation, bone.Scale, true) : p).ToArray(),
            missing.OrderBy(name => name).ToArray());
    }

    private static SidecarDriverGroup? EvaluationGroup(IReadOnlyList<SidecarDriverGroup> groups, int index)
    {
        if (index != -1) return groups.ElementAtOrDefault(index);
        // UI groups are not execution batches. A whole-file preview must retain
        // resource order and one driver cache across every driven bone.
        return new SidecarDriverGroup(0, groups.SelectMany(g => g.DriverIndices).Distinct().ToArray(), [],
            groups.SelectMany(g => g.DrivenBoneIndices).Distinct().ToArray(), [], [], [], [], 0, 0);
    }

    private static void ApplyDrivenPose(Dictionary<string, VisualModelBone> current, SidecarBonePreviewPose pose)
    {
        if (!TryGetBone(current, pose.Name, out var previous) || !Matrix4x4.Invert(ToMatrix(previous!), out var inverse)) return;
        var world = Matrix4x4.CreateScale(pose.Scale) * Matrix4x4.CreateFromQuaternion(pose.Rotation) * Matrix4x4.CreateTranslation(pose.Position);
        var delta = inverse * world;
        var descendants = new HashSet<int> { previous!.Index };
        var pending = new Queue<int>(); pending.Enqueue(previous.Index);
        while (pending.TryDequeue(out var parent))
            foreach (var bone in current.Values.Where(b => b.ParentIndex == parent))
                if (descendants.Add(bone.Index)) pending.Enqueue(bone.Index);
        foreach (var entry in current.Where(p => descendants.Contains(p.Value.Index)).ToArray())
        {
            var bone = entry.Value;
            var matrix = bone.Index == previous.Index ? world : ToMatrix(bone) * delta;
            if (Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var position))
                current[entry.Key] = new VisualModelBone { Index = bone.Index, ParentIndex = bone.ParentIndex, Name = bone.Name,
                    Sources = bone.Sources, Position = position, Rotation = rotation, Scale = scale };
        }
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
        IReadOnlyDictionary<string, VisualModelBone> rest,
        bool botw = false)
    {
        TryGetBone(rest, name, out var restDriven);
        var currentBase = GetBone(current, boneNames, baseBoneIndex);
        var restBase = GetBone(rest, boneNames, baseBoneIndex);

        if (currentBase == null && baseBoneIndex == (botw ? -1 : -2) && restDriven != null && restDriven.ParentIndex >= 0)
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

        // Both games use the same stereographic swing/twist convention.
        var outputRotation = BuildBotwRotation(bendH, bendV, roll, aim, up);
        localRotation = Quaternion.Normalize(localRotation * outputRotation);
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

    internal static Vector3 MeasureBotwRotation(Quaternion rotation, Vector3 aim, Vector3 up)
    {
        var frame = BuildFrame(aim, up);
        rotation = Quaternion.Normalize(rotation);
        var direction = Vector3.Normalize(Vector3.Transform(frame.Aim, rotation));
        var denominator = 1f + Vector3.Dot(frame.Aim, direction);
        if (denominator <= 0.000001f) return Vector3.Zero;
        var swing = Quaternion.Normalize(new Quaternion(Vector3.Cross(frame.Aim, direction), denominator));
        var twist = Quaternion.Normalize(Quaternion.Inverse(swing) * rotation);
        var roll = 2f * MathF.Atan2(Vector3.Dot(new Vector3(twist.X, twist.Y, twist.Z), frame.Aim), twist.W);
        roll = MathF.IEEERemainder(roll, 2f * MathF.PI);
        return new Vector3(
            2f * MathF.Atan2(Vector3.Dot(direction, frame.Right), denominator),
            2f * MathF.Atan2(Vector3.Dot(direction, frame.Up), denominator), roll);
    }

    internal static Quaternion BuildBotwRotation(float bendH, float bendV, float roll, Vector3 aim, Vector3 up)
    {
        // BotW uses a stereographic swing followed by twist, not Euler angles.
        // Double precision keeps the half-angle mapping finite near +/- pi.
        var frame = BuildFrame(aim, up);
        var h = Math.Tan(bendH * 0.5);
        var v = Math.Tan(bendV * 0.5);
        var factor = 2.0 / (1.0 + h * h + v * v);
        var direction = frame.Aim * (float)(factor - 1) +
            frame.Right * (float)(h * factor) + frame.Up * (float)(v * factor);
        var denominator = 1f + Vector3.Dot(frame.Aim, direction);
        if (!float.IsFinite(denominator) || denominator <= 0.000001f) return Quaternion.Identity;
        var swing = Quaternion.Normalize(new Quaternion(Vector3.Cross(frame.Aim, direction), denominator));
        return Quaternion.Normalize(swing * Quaternion.CreateFromAxisAngle(frame.Aim, roll));
    }

    internal static float EvaluateBotwCurve(AampReadObject curve, float input)
    {
        // Keep authored key order and exact key hits, including coincident keys.
        var keys = new[] { CurveKey0Hash, CurveKey1Hash, CurveKey2Hash }
            .Select(hash => curve.Find(hash)?.AsVector4()).TakeWhile(key => key.HasValue)
            .Select(key => key!.Value).ToArray();
        if (keys.Length == 0) return 0f;
        foreach (var key in keys)
            if (input == key.X) return float.IsNaN(key.Y) ? 0f : key.Y;
        float result;
        if (input < keys[0].X)
            result = keys[0].Y + ((curve.Find("constant_in")?.AsBoolean() ?? true) ? 0f : (input - keys[0].X) * keys[0].Z);
        else if (input > keys[^1].X)
            result = keys[^1].Y + ((curve.Find("constant_out")?.AsBoolean() ?? true) ? 0f : (input - keys[^1].X) * keys[^1].Z);
        else result = EvaluateCurve(keys, input);
        return float.IsNaN(result) ? 0f : result;
    }

    internal static float EvaluateTotkCurve(IReadOnlyList<Vector4> keys, float input)
    {
        if (keys.Count == 0) return 0f;
        // Native BPHHB extrapolates with the endpoint key's incoming slope.
        if (input <= keys[0].X) return keys[0].Y + (input - keys[0].X) * keys[0].Z;
        if (input > keys[^1].X) return keys[^1].Y + (input - keys[^1].X) * keys[^1].Z;
        return EvaluateCurve(keys, input);
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

    internal static float EvaluateCurve(IReadOnlyList<Vector4> keys, float input)
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
        return available.Take(Math.Clamp(declared, 0, available.Length)).ToArray();
    }

    private static string[] ReadBoneNames(AampReadList bones) => bones.Objects
        .Select((bone, index) => bone.Find(NameHash)?.AsString() ?? $"bone_{index}")
        .ToArray();

    private static string BoneName(IReadOnlyList<string> names, int index) =>
        (uint)index < (uint)names.Count ? names[index] : $"bone_{index}";

    private static Dictionary<string, VisualModelBone> IndexBones(IEnumerable<VisualModelBone> bones)
    {
        var source = bones.ToArray();
        var byIndex = source.ToDictionary(b => b.Index);
        var result = source.GroupBy(b => NormalizeBoneName(b.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // Equipped models repeat the base skeleton with different indices. Keep
        // their unique children connected to the selected copy of each parent.
        foreach (var entry in result.ToArray())
        {
            var b = entry.Value;
            if (byIndex.TryGetValue(b.ParentIndex, out var parent) &&
                result.TryGetValue(NormalizeBoneName(parent.Name), out var canonical) && canonical.Index != b.ParentIndex)
                result[entry.Key] = new VisualModelBone { Index = b.Index, ParentIndex = canonical.Index, Name = b.Name,
                    Sources = b.Sources, Position = b.Position, Rotation = b.Rotation, Scale = b.Scale };
        }
        return result;
    }

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
