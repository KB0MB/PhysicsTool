using System.Numerics;

namespace HKCLTool;

public sealed record SidecarCurveRow(int Index, int DriverIndex, int Attribute, IReadOnlyList<Vector4> Keys, bool Editable)
{
    public static float Sample(IReadOnlyList<Vector4> keys, float input) => SidecarPreviewEvaluator.EvaluateCurve(keys, input);
    public string DriverName { get; init; } = string.Empty;
    public string AttributeName => Attribute switch { 0 => "Roll", 1 => "Bend H", 2 => "Bend V", 3 => "Translate X", 4 => "Translate Y", 5 => "Translate Z", _ => $"Attribute {Attribute}" };
    public override string ToString() => $"{(DriverName.Length > 0 ? DriverName : $"Driver {DriverIndex}")} / {AttributeName} (curve {Index})";
}

internal static class SidecarCurveEditing
{
    private static readonly uint[] KeyHashes = [1692570776u, 333816846u, 2330785204u, 4260087074u];
    internal static IReadOnlyList<SidecarCurveRow> Read(AampReadArchive archive, SidecarDriverGroup group, bool totk)
    {
        var curves = archive.Root.Children.Single().FindChild("connection_curve_list");
        if (curves == null) return [];
        var data = archive.Root.Children.Single();
        var drivers = data.FindChild(totk ? "driver_bone_list" : "main_bone_list");
        var bones = data.FindChild("bone_list");
        return curves.Objects.Select((curve, index) => {
            var packed = curve.Find(554147540u)?.AsInt32() ?? -1;
            var driver = totk ? curve.Find(249727330u)?.AsInt32() ?? -1 : (int)((uint)packed >> 16);
            var attribute = totk ? curve.Find(75375380u)?.AsInt32() ?? -1 : packed & 0xffff;
            var count = totk ? curve.Find(139113150u)?.AsInt32() ?? 0 : 3;
            var keys = KeyHashes.Take(Math.Clamp(count, 0, 4)).Select(hash => curve.Find(hash)?.AsVector4()).TakeWhile(v => v.HasValue).Select(v => v!.Value).ToArray();
            var bone = drivers?.Objects.ElementAtOrDefault(driver)?.Find(totk ? 3407736066u : 893828983u)?.AsInt32() ?? -1;
            var name = bones?.Objects.ElementAtOrDefault(bone)?.Find(1579384326u)?.AsString() ?? string.Empty;
            return new SidecarCurveRow(index, driver, attribute, keys, count is >= 1 and <= 4 && keys.Length == count && keys.All(Finite)) { DriverName = name };
        }).Where(c => group.DriverIndices.Contains(c.DriverIndex)).ToArray();
    }
    internal static void Validate(SidecarCurveRow current, IReadOnlyList<Vector4> keys)
    {
        if (!current.Editable || keys.Count != current.Keys.Count || !keys.All(Finite))
            throw new ArgumentException("Keep the existing key count and use finite curve values.");
        if (keys.Where((k, i) => k.X != current.Keys[i].X).Any() && keys.Zip(keys.Skip(1)).Any(pair => pair.First.X > pair.Second.X))
            throw new ArgumentException("Curve inputs must stay in ascending order; equal inputs are allowed.");
    }
    private static bool Finite(Vector4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
    internal static void Write(BotwAampWriter.AampListNode curves, int index, IReadOnlyList<Vector4> keys)
    {
        if ((uint)index >= curves.Objects.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var record = curves.Objects[index];
        for (var i = 0; i < keys.Count; i++)
        {
            var slot = record.Parameters.FindIndex(p => (p.NameHash ?? BotwAampWriter.Crc32(p.Name)) == KeyHashes[i]);
            if (slot < 0) throw new InvalidDataException("Curve key is missing.");
            var v = keys[i]; var bytes = new byte[16];
            BitConverter.GetBytes(v.X).CopyTo(bytes, 0); BitConverter.GetBytes(v.Y).CopyTo(bytes, 4);
            BitConverter.GetBytes(v.Z).CopyTo(bytes, 8); BitConverter.GetBytes(v.W).CopyTo(bytes, 12);
            record.Parameters[slot] = BotwAampWriter.AampParameterNode.Raw(KeyHashes[i], record.Parameters[slot].Type, bytes);
        }
    }
}
