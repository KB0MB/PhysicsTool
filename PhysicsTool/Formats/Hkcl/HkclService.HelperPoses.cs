using System.Numerics;
using static HKCLTool.BotwAampWriter;

namespace HKCLTool;

public sealed record HelperPoseFrame(int Record, int Bone, string Name, int Space, string? SpaceName,
    bool Botw, Vector3 Translation, Quaternion Rotation, Vector3 Aim, Vector3 Up)
{
    private static string Key(string name) => SidecarPreviewEvaluator.NormalizeBoneName(name);
    private static VisualModelBone Find(IReadOnlyList<VisualModelBone> bones, string name) =>
        bones.FirstOrDefault(b => Key(b.Name) == Key(name)) ?? throw new InvalidOperationException($"The model is missing {name}.");
    private Matrix4x4 Basis(IReadOnlyList<VisualModelBone> bones)
    {
        var target = Find(bones, Name);
        if (Space >= 0) return BotwActorScene.Matrix(Find(bones, SpaceName!));
        if (Space == (Botw ? -1 : -2))
            return bones.FirstOrDefault(b => b.Index == target.ParentIndex) is { } parent ? BotwActorScene.Matrix(parent) : Matrix4x4.Identity;
        return Matrix4x4.Identity;
    }
    public float[] Measure(IReadOnlyList<VisualModelBone> bones)
    {
        if (!Matrix4x4.Invert(Basis(bones), out var inverse)) throw new InvalidOperationException("The reference bone has a singular transform.");
        if (!Matrix4x4.Decompose(BotwActorScene.Matrix(Find(bones, Name)) * inverse, out _, out var rotation, out var position))
            throw new InvalidOperationException("The bone pose cannot be decomposed.");
        var angles = SidecarPreviewEvaluator.MeasureBotwRotation(Quaternion.Normalize(Quaternion.Inverse(Rotation) * rotation), Aim, Up);
        var translation = position - Translation;
        return [angles.Z, angles.X, angles.Y, translation.X, translation.Y, translation.Z];
    }
    public VisualBoneOverride Pose(IReadOnlyList<VisualModelBone> bones, IReadOnlyList<float> values)
    {
        if (values.Count != 6 || values.Any(v => !float.IsFinite(v))) throw new ArgumentException("Pose values must be finite.");
        var basis = Basis(bones);
        if (!Matrix4x4.Invert(basis, out var inverse)) throw new InvalidOperationException("The reference bone has a singular transform.");
        Matrix4x4.Decompose(BotwActorScene.Matrix(Find(bones, Name)) * inverse, out var scale, out _, out _);
        var rotation = Quaternion.Normalize(Rotation * SidecarPreviewEvaluator.BuildBotwRotation(values[1], values[2], values[0], Aim, Up));
        var local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) *
            Matrix4x4.CreateTranslation(Translation + new Vector3(values[3], values[4], values[5]));
        if (!Matrix4x4.Decompose(local * basis, out var worldScale, out var worldRotation, out var worldPosition))
            throw new InvalidOperationException("The requested pose cannot be decomposed.");
        return new(Find(bones, Name).Name, worldPosition, worldRotation, worldScale);
    }
    public override string ToString() => Name;
}

public sealed record HelperPoseExample(float Input, IReadOnlyDictionary<int, float[]> Helpers);
public sealed record HelperPoseResponse(int Bone, int Channel, IReadOnlyList<Vector4> Keys);

public static class HelperPoseFitting
{
    // Three keys fit both native formats. With two examples, add a midpoint;
    // smooth mode splits the same cubic exactly, rather than adding a flat spot.
    public static IReadOnlyList<HelperPoseResponse> Fit(IReadOnlyList<HelperPoseExample> examples,
        IReadOnlyDictionary<int, int[]> channels, bool smooth)
    {
        if (examples.Count is < 2 or > 3) throw new ArgumentException("Capture two or three poses.");
        var sorted = examples.OrderBy(p => p.Input).ToArray();
        if (sorted.Any(p => !float.IsFinite(p.Input)) || sorted.Zip(sorted.Skip(1)).Any(p => p.Second.Input - p.First.Input < .00001f))
            throw new ArgumentException("Each pose needs a different driver value.");
        if (channels.Count == 0 || channels.All(c => c.Value.Length == 0)) throw new ArgumentException("Choose at least one affected movement.");
        var responses = new List<HelperPoseResponse>();
        foreach (var (bone, selected) in channels)
        foreach (var channel in selected.Distinct())
        {
            if ((uint)channel >= 6) throw new ArgumentException("Invalid helper channel.");
            var points = sorted.Select(p => p.Helpers.TryGetValue(bone, out var values) && values.Length == 6 && float.IsFinite(values[channel])
                ? new Vector2(p.Input, values[channel]) : throw new ArgumentException("Every captured pose must include each affected helper.")).ToList();
            if (points.Count == 2) points.Insert(1, (points[0] + points[1]) / 2);
            var keys = points.Select((p, i) =>
            {
                float left = i == 0 ? 0 : (p.Y - points[i - 1].Y) / (p.X - points[i - 1].X);
                float right = i == 2 ? 0 : (points[i + 1].Y - p.Y) / (points[i + 1].X - p.X);
                if (smooth) left = right = sorted.Length == 2 && i == 1 ? 1.5f * left : 0;
                return new Vector4(p.X, p.Y, left, right);
            }).ToArray();
            responses.Add(new(bone, channel, keys));
        }
        return responses;
    }
}

public sealed partial class HkclService
{
    public IReadOnlyList<HelperPoseFrame> GetHelperPoseFrames(int groupIndex, bool drivers)
    {
        var data = HelperData(); var totk = IsBphhb;
        var group = GetSidecarDriverGroup(groupIndex) ?? throw new ArgumentException("Choose a helper group.");
        var names = GetSidecarBoneRows();
        float[] Values(AampObjectNode o, string key, params float[] fallback)
        {
            var p = o.Parameters.FirstOrDefault(p => (p.NameHash ?? Crc32(p.Name)) == Crc32(key));
            if (p == null) return fallback;
            var bytes = p.RawBytes ?? p.ToBinary();
            return Enumerable.Range(0, bytes.Length / 4).Select(i => BitConverter.ToSingle(bytes, i * 4)).ToArray();
        }
        HelperPoseFrame Frame(AampObjectNode source, int record, int bone)
        {
            var t = Values(source, "base_translate", 0, 0, 0); var q = Values(source, "base_rotate", 0, 0, 0, 1);
            var a = Values(source, totk ? "aim_axis" : "aim", 0, 0, 1); var u = Values(source, totk ? "up_axis" : "up", 0, 1, 0);
            // TotK uses hashed field names; resolve them explicitly below.
            if (totk)
            {
                float[] ByHash(uint hash, float[] fallback) { var p = source.Parameters.FirstOrDefault(p => p.NameHash == hash); var b = p?.RawBytes ?? p?.ToBinary(); return b == null ? fallback : Enumerable.Range(0, b.Length / 4).Select(i => BitConverter.ToSingle(b, i * 4)).ToArray(); }
                a = ByHash(3442436866, a); u = ByHash(907422927, u);
            }
            var space = SidecarConnectionEditing.Int(source, totk ? 4279981158u : Crc32("space"));
            return new(record, bone, names.Single(n => n.Index == bone).Name, space,
                space >= 0 ? names.Single(n => n.Index == space).Name : null, !totk,
                new(t[0], t[1], t[2]), Quaternion.Normalize(new(q[0], q[1], q[2], q[3])), new(a[0], a[1], a[2]), new(u[0], u[1], u[2]));
        }
        if (drivers)
        {
            var list = SidecarConnectionEditing.List(data, totk ? "driver_bone_list" : "main_bone_list");
            return group.DriverIndices.Select(i => Frame(list.Objects[i], i,
                SidecarConnectionEditing.Int(list.Objects[i], totk ? 3407736066u : Crc32("bone")))).ToArray();
        }
        return group.DrivenBoneIndices.Select(i => Frame(SidecarConnectionEditing.Pose(data, i, totk), -1, i)).ToArray();
    }

    public void ApplyHelperPoses(int groupIndex, int driver, int channel, IReadOnlyList<HelperPoseResponse> responses, bool replace)
    {
        var group = GetSidecarDriverGroup(groupIndex) ?? throw new ArgumentException("Choose a helper group.");
        if (!group.DriverIndices.Contains(driver) || (uint)channel >= 6 || responses.Count == 0)
            throw new ArgumentException("Choose a driver, movement and captured poses.");
        if (responses.Select(r => (r.Bone, r.Channel)).Distinct().Count() != responses.Count)
            throw new ArgumentException("Duplicate helper responses.");
        foreach (var response in responses)
        {
            if (!group.DrivenBoneIndices.Contains(response.Bone) || (uint)response.Channel >= 6)
                throw new ArgumentException("Choose affected bones from this group.");
            var existing = GetHelperConnections(response.Bone)[response.Channel];
            if (!replace && (existing.First >= 0 || existing.Second >= 0))
                throw new InvalidOperationException("Some selected movements already have responses. Enable Replace selected responses to replace them.");
        }
        EditHelperData(data => {
            foreach (var response in responses)
            {
                // BotW packs H/V/roll as 0/1/2; the UI and TotK use roll/H/V.
                var attribute = IsBphhb ? channel : channel switch { 0 => 2, 1 => 0, 2 => 1, _ => channel };
                var curve = SidecarConnectionEditing.CreateCurve(data, IsBphhb, driver, attribute, response.Keys);
                SidecarConnectionEditing.Connect(data, IsBphhb, response.Bone, response.Channel,
                    IsBphhb ? curve : 65536 | curve, -1, 1, 1);
            }
        });
    }
}
