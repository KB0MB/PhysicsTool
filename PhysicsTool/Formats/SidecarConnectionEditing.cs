using System.Numerics;
using static HKCLTool.BotwAampWriter;

namespace HKCLTool;

public sealed record HelperConnectionSource(int Index, bool Linear, string Label)
{
    public override string ToString() => Label;
}
public sealed record HelperConnectionRow(int Channel, string Name, int First, int Second, float FirstWeight, float SecondWeight);

// Native output tables are shared. Rewiring creates an output for this pose only;
// a shared TotK pose is copied before editing its references.
internal static class SidecarConnectionEditing
{
    internal static readonly string[] Channels = ["Roll", "Bend H", "Bend V", "Translate X", "Translate Y", "Translate Z"];
    private static readonly uint[] BotwChannels = [783626958, 318199082, 3908593737, 2040119991, 245297697, 2543297435];
    private static readonly uint[] TotkChannels = [2869652774, 3079588026, 122218518, 3595547632, 1861473429, 2084993915];
    private static readonly uint[] Keys = [1692570776, 333816846, 2330785204, 4260087074];
    private static readonly string[] DrivenChannels = ["translate", "rotate", "aim"];
    internal static AampListNode List(AampListNode data, string name) => data.Children.SingleOrDefault(c => c.NameHash == Crc32(name))
        ?? throw new InvalidDataException($"Missing helper table: {name}.");
    internal static int Int(AampObjectNode o, uint hash, int fallback = -1) => Find(o, hash) is { } p ? BitConverter.ToInt32(p.RawBytes ?? p.ToBinary()) : fallback;
    private static AampParameterNode? Find(AampObjectNode o, uint hash) => o.Parameters.FirstOrDefault(p => (p.NameHash ?? Crc32(p.Name)) == hash);
    private static float Float(AampObjectNode o, uint hash) => Find(o, hash) is { } p ? BitConverter.ToSingle(p.RawBytes ?? p.ToBinary()) : 1;
    internal static void Set(AampObjectNode o, uint hash, int value) => Put(o, AampParameterNode.Raw(hash, 2, BitConverter.GetBytes(value)));
    private static void Put(AampObjectNode o, AampParameterNode p)
    {
        var hash = p.NameHash ?? Crc32(p.Name);
        var i = o.Parameters.FindIndex(q => (q.NameHash ?? Crc32(q.Name)) == hash);
        if (i < 0) o.Parameters.Add(p); else o.Parameters[i] = p;
    }
    internal static AampObjectNode Clone(AampObjectNode o, string name)
    {
        var result = new AampObjectNode(name);
        result.Parameters.AddRange(o.Parameters.Select(p => AampParameterNode.Raw(p.NameHash ?? Crc32(p.Name), p.Type, p.RawBytes ?? p.ToBinary())));
        return result;
    }
    internal static AampListNode Clone(AampReadList list)
    {
        var result = new AampListNode(string.Empty, list.Hash);
        foreach (var child in list.Children) result.Children.Add(Clone(child));
        foreach (var o in list.Objects)
        {
            var copy = new AampObjectNode(string.Empty, o.Hash);
            copy.Parameters.AddRange(o.Parameters.Select(p => AampParameterNode.Raw(p.Hash, p.Type, p.Bytes)));
            result.Objects.Add(copy);
        }
        return result;
    }
    internal static IEnumerable<int> PoseIds(AampObjectNode driven)
    {
        foreach (var channel in DrivenChannels)
        {
            var id = Int(driven, Crc32(channel + "_driven_id"));
            if (id >= 0 && Int(driven, Crc32(channel + "_driven_type")) is -1 or 0) yield return id;
        }
    }
    internal static int SinglePose(AampObjectNode driven, int count)
    {
        foreach (var channel in DrivenChannels)
            if (Int(driven, Crc32(channel + "_driven_id")) >= 0 && Int(driven, Crc32(channel + "_driven_type")) is not (-1 or 0))
                throw new InvalidOperationException("This helper uses a position, rotation or aim solver rather than a pose response. Its connections cannot be edited here yet.");
        var ids = PoseIds(driven).Distinct().ToArray();
        if (ids.Length != 1 || (uint)ids[0] >= count)
            throw new InvalidOperationException("This helper does not have one explicit pose binding. Its connections were left unchanged.");
        return ids[0];
    }
    internal static AampObjectNode Pose(AampListNode data, int bone, bool totk, bool isolate = false)
    {
        var records = List(data, totk ? "driven_bone_list" : "support_bone_list");
        var matching = records.Objects.Where(o => Int(o, totk ? 3407736066u : 893828983u) == bone).ToArray();
        if (matching.Length != 1) throw new InvalidOperationException("Choose an affected bone with one helper record to edit its connections.");
        if (!totk) return matching[0];
        var poses = List(data, "pose_driven_list");
        var index = SinglePose(matching[0], poses.Objects.Count);
        if (isolate && records.Objects.Where(o => o != matching[0]).Any(o => PoseIds(o).Contains(index)))
        {
            var copy = Clone(poses.Objects[index], $"pose_driven_{poses.Objects.Count}");
            foreach (var channel in DrivenChannels)
            {
                var hash = Crc32(channel + "_driven_id");
                if (Int(matching[0], hash) == index) Set(matching[0], hash, poses.Objects.Count);
            }
            poses.Objects.Add(copy);
            return copy;
        }
        return poses.Objects[index];
    }
    internal static IReadOnlyList<HelperConnectionRow> Read(AampListNode data, int bone, bool totk)
    {
        var pose = Pose(data, bone, totk);
        return Enumerable.Range(0, 6).Select(channel =>
        {
            var output = Int(pose, (totk ? TotkChannels : BotwChannels)[channel]);
            if (output < 0) return new HelperConnectionRow(channel, Channels[channel], -1, -1, 1, 1);
            var doubleOutput = !totk && (output >> 16) == 1;
            if (!totk && (output >> 16) is not (0 or 1)) throw new InvalidDataException("Unsupported packed helper output type.");
            var list = List(data, totk ? "output_list" : doubleOutput ? "output_double_list" : "output_single_list");
            var index = totk ? output : output & 0xffff;
            if ((uint)index >= list.Objects.Count) throw new InvalidDataException("Helper output points outside its table.");
            var o = list.Objects[index];
            return new HelperConnectionRow(channel, Channels[channel], Int(o, totk ? 2390703269u : doubleOutput ? 2693310104u : 704082790u),
                totk || doubleOutput ? Int(o, totk ? 918772672u : 3616511502u) : -1,
                totk ? 1 : Float(o, doubleOutput ? 2822815546u : 130897217u), totk ? 1 : Float(o, 3746009004u));
        }).ToArray();
    }
    internal static void Connect(AampListNode data, bool totk, int bone, int channel, int first, int second, float firstWeight, float secondWeight)
    {
        if ((uint)channel >= 6 || !float.IsFinite(firstWeight) || !float.IsFinite(secondWeight)) throw new ArgumentException("Choose a channel and finite response weights.");
        if (totk && (firstWeight != 1 || secondWeight != 1)) throw new ArgumentException("TotK response strength belongs in the curve, not output weights.");
        foreach (var id in new[] { first, second })
        {
            if (id == -1) continue;
            var kind = totk ? 1 : id >> 16;
            var index = totk ? id : id & 0xffff;
            if (kind is not (0 or 1) || (uint)index >= List(data, kind == 1 ? "connection_curve_list" : "connection_linear_list").Objects.Count)
                throw new ArgumentException("Choose an existing driver response.");
        }
        var existing = Read(data, bone, totk)[channel];
        var enabled = !totk || first == -1 && second == -1 ||
            Int(List(data, "driven_bone_list").Objects.Single(o => Int(o, 3407736066) == bone), Crc32((channel < 3 ? "rotate" : "translate") + "_driven_id")) >= 0;
        if (enabled && existing.First == first && existing.Second == second && existing.FirstWeight == firstWeight && existing.SecondWeight == secondWeight) return;
        var pose = Pose(data, bone, totk, isolate: true);
        var field = (totk ? TotkChannels : BotwChannels)[channel];
        if (first == -1 && second == -1) { Set(pose, field, -1); return; }
        if (totk)
        {
            var driven = List(data, "driven_bone_list").Objects.Single(o => Int(o, 3407736066) == bone);
            var nativeChannel = channel < 3 ? "rotate" : "translate";
            Set(driven, Crc32(nativeChannel + "_driven_type"), 0);
            Set(driven, Crc32(nativeChannel + "_driven_id"), List(data, "pose_driven_list").Objects.IndexOf(pose));
        }
        if (first == -1) { first = second; firstWeight = secondWeight; second = -1; }
        var isDouble = second >= 0;
        var outputs = List(data, totk ? "output_list" : isDouble ? "output_double_list" : "output_single_list");
        if (!totk && outputs.Objects.Count >= 65536) throw new InvalidOperationException("The packed output table is full.");
        var o = new AampObjectNode($"{(totk ? "output" : isDouble ? "output_double" : "output_single")}_{outputs.Objects.Count}");
        Set(o, totk ? 2390703269u : isDouble ? 2693310104u : 704082790u, first);
        if (totk || isDouble) Set(o, totk ? 918772672u : 3616511502u, second);
        if (!totk)
        {
            Put(o, AampParameterNode.Raw(isDouble ? 2822815546u : 130897217u, 1, BitConverter.GetBytes(firstWeight)));
            if (isDouble) Put(o, AampParameterNode.Raw(3746009004u, 1, BitConverter.GetBytes(secondWeight)));
        }
        Set(pose, field, outputs.Objects.Count | (!totk && isDouble ? 1 << 16 : 0));
        outputs.Objects.Add(o);
    }
    internal static int CreateCurve(AampListNode data, bool totk, int driver, int attribute, IReadOnlyList<Vector4> keys)
    {
        if ((uint)driver >= List(data, totk ? "driver_bone_list" : "main_bone_list").Objects.Count || (uint)attribute >= 6)
            throw new ArgumentException("Choose an existing input bone and its movement attribute.");
        if ((!totk && keys.Count != 3) || keys.Count is < 1 or > 4 || keys.Any(k => !float.IsFinite(k.X) || !float.IsFinite(k.Y) || !float.IsFinite(k.Z) || !float.IsFinite(k.W)) || keys.Zip(keys.Skip(1)).Any(p => p.First.X >= p.Second.X))
            throw new ArgumentException("New curves need finite keys with strictly increasing inputs (BotW: 3 keys; TotK: 1 to 4).");
        var curves = List(data, "connection_curve_list");
        if (!totk && (curves.Objects.Count >= 65536 || driver >= 65536)) throw new InvalidOperationException("The packed curve table is full.");
        var curve = new AampObjectNode($"connection_curve_{curves.Objects.Count}");
        if (totk) { Set(curve, 249727330, driver); Set(curve, 75375380, attribute); Set(curve, 139113150, keys.Count); }
        else
        {
            Set(curve, 554147540, (driver << 16) | attribute);
            curve.Parameters.Add(AampParameterNode.Boolean("constant_in", true));
            curve.Parameters.Add(AampParameterNode.Boolean("constant_out", true));
        }
        for (var i = 0; i < keys.Count; i++)
        {
            var k = keys[i];
            var bytes = new[] { k.X, k.Y, k.Z, k.W }.SelectMany(BitConverter.GetBytes).ToArray();
            curve.Parameters.Add(AampParameterNode.Raw(Keys[i], 5, bytes));
        }
        curves.Objects.Add(curve);
        return curves.Objects.Count - 1;
    }
    internal static void DeleteHelper(AampListNode data, bool totk, int bone)
    {
        var records = List(data, totk ? "driven_bone_list" : "support_bone_list");
        var hash = totk ? 3407736066u : 893828983u;
        var removed = records.Objects.Where(o => Int(o, hash) == bone).ToArray();
        if (removed.Length == 0) throw new InvalidOperationException("The selected bone has no affected helper behavior to delete.");
        var candidates = removed.SelectMany(PoseIds).ToHashSet();
        SidecarGroupRemoval.Compact(records, totk ? "driven_bone" : "support_bone", i => Int(records.Objects[i], hash) == bone);
        if (!totk) return;
        var poses = List(data, "pose_driven_list");
        var retained = records.Objects.SelectMany(PoseIds).ToHashSet();
        var map = SidecarGroupRemoval.Compact(poses, "pose_driven", i => candidates.Contains(i) && !retained.Contains(i));
        foreach (var o in records.Objects)
            foreach (var channel in DrivenChannels)
            {
                var idHash = Crc32(channel + "_driven_id");
                if (Int(o, idHash) >= 0 && Int(o, Crc32(channel + "_driven_type")) is -1 or 0)
                    Set(o, idHash, SidecarGroupRemoval.Remap(Int(o, idHash), map));
            }
        // Keep bone names, driver records, curves and outputs: these can still be
        // used by other helpers, or be intentional unconnected authoring data.
    }
}
