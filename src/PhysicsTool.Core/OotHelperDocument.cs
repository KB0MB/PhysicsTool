using BymlLibrary;
using HKCLTool;
using System.Buffers.Binary;
using System.Globalization;
using System.Text.RegularExpressions;
using System.IO;
using Revrs;
using System.Numerics;

namespace PhysicsTool.Core;

internal sealed record OotHelperGroup(string Kind, int Index, string Name)
{
    public override string ToString() => Name;
}
internal sealed record OotHelperField(string Path, string Label, BymlNodeType Type, string Value);

internal sealed partial class OotHelperDocument
{
    private byte[] _bytes, _saved;
    private readonly Stack<byte[]> _undo = new(), _redo = new();
    internal OotHelperDocument(byte[] bytes)
    {
        var map = Byml.FromBinary(bytes).GetMap();
        if (!map.ContainsKey("BoneNames") || !map.ContainsKey("DrivenParams")) throw new InvalidDataException("Not an OoT helper-bone parameter file.");
        _bytes = bytes.ToArray(); _saved = _bytes;
    }
    internal bool IsDirty => !_bytes.AsSpan().SequenceEqual(_saved);
    internal bool CanUndo => _undo.Count > 0;
    internal bool CanRedo => _redo.Count > 0;
    internal byte[] Bytes => _bytes.ToArray();
    internal IReadOnlyList<string> BoneNames => Byml.FromBinary(_bytes).GetMap()["BoneNames"].GetArray().Select(n => n.GetString()).ToArray();
    internal IReadOnlyList<OotHelperGroup> Groups
    {
        get
        {
            var map = Byml.FromBinary(_bytes).GetMap();
            var result = new List<OotHelperGroup>();
            foreach (var kind in new[] { "DrivenParams", "DriverParams" })
                if (map.TryGetValue(kind, out var list))
                    for (int i = 0; i < list.GetArray().Count; i++)
                    {
                        var values = list.GetArray()[i].GetMap();
                        var bones = map["BoneNames"].GetArray();
                        var boneId = values.TryGetValue("BoneNameId", out var id) ? id.GetInt() : -1;
                        var label = values.TryGetValue("BoneName", out var name) ? name.GetString() : boneId >= 0 && boneId < bones.Count ? bones[boneId].GetString() : "Unknown bone";
                        result.Add(new(kind, i, kind == "DriverParams" ? $"{i}: {label}" : label));
                    }
            return result;
        }
    }

    internal static string Label(string text) => text switch
    {
        "BaseBoneNameId" => "Base bone", "BoneNameId" => "Affected bone", "DriverParamId" => "Driver",
        "BaseTranslate" => "Base position", "BaseRotation" => "Base rotation", "StaticValue" => "Static value",
        "BendH" => "Horizontal bend", "BendV" => "Vertical bend", "InConnections" => "Connections",
        "DrivenCurve" => "Response curve", _ => Regex.Replace(text, "(?<=[a-z])([A-Z])", " $1")
    };

    internal IReadOnlyList<OotHelperField> Fields(OotHelperGroup group)
    {
        var map = Byml.FromBinary(_bytes).GetMap();
        var result = new List<OotHelperField>();
        var entry = map[group.Kind].GetArray()[group.Index];
        Walk(entry, "", "");
        if (entry.GetMap().TryGetValue("BaseRotate", out var rotate) && rotate.GetArray().Count == 4)
        {
            var q = rotate.GetArray().Select(v => v.GetFloat()).ToArray();
            var angles = BoneRotationAngles.ToRadians(new(q[0], q[1], q[2], q[3]));
            result.Add(new("$RotationX", "Base rotation X (rad)", BymlNodeType.Float, angles.X.ToString("R", CultureInfo.InvariantCulture)));
            result.Add(new("$RotationY", "Base rotation Y (rad)", BymlNodeType.Float, angles.Y.ToString("R", CultureInfo.InvariantCulture)));
            result.Add(new("$RotationZ", "Base rotation Z (rad)", BymlNodeType.Float, angles.Z.ToString("R", CultureInfo.InvariantCulture)));
        }
        return result;
        void Walk(Byml node, string path, string label)
        {
            if (node.Type == BymlNodeType.Map)
                foreach (var (key, child) in node.GetMap())
                {
                    // Keep bone identities, hashed attributes, quaternion bases and axis conventions intact.
                    if (key is "BaseRotate" or "AimAxis" or "UpAxis") continue;
                    if (key == "Data" && node.GetMap().TryGetValue("Type", out var type) && type.GetString() == "Hermit2DSplit")
                    {
                        var data = child.GetArray();
                        for (int i = 0; i < data.Count; i++) Walk(data[i], path + "/Data/" + i,
                            label + " / Key " + (i / 4 + 1) + " / " + new[] { "X", "Value", "Incoming tangent", "Outgoing tangent" }[i % 4]);
                        continue;
                    }
                    Walk(child, path.Length == 0 ? key : path + "/" + key, label.Length == 0 ? Label(key) : label + " / " + Label(key));
                }
            else if (node.Type == BymlNodeType.Array)
            {
                var array = node.GetArray();
                for (int i = 0; i < array.Count; i++) Walk(array[i], path + "/" + i, label + " / " + (i + 1));
            }
            else if (node.Type is BymlNodeType.Float or BymlNodeType.Bool || node.Type == BymlNodeType.Int &&
                (path.EndsWith("DriverParamId", StringComparison.Ordinal) || path is "BoneNameId" or "BaseBoneNameId"))
            {
                var value = node.Type == BymlNodeType.Float ? node.GetFloat().ToString("R", CultureInfo.InvariantCulture)
                    : node.Type == BymlNodeType.Bool ? node.GetBool() ? "true" : "false" : node.GetInt().ToString(CultureInfo.InvariantCulture);
                result.Add(new(path, label, node.Type, value));
            }
        }
    }

    internal void Apply(OotHelperGroup group, IReadOnlyDictionary<string, string> edits)
    {
        var allowed = Fields(group).ToDictionary(f => f.Path);
        var root = Byml.FromBinary(_bytes);
        var map = root.GetMap();
        var entry = map[group.Kind].GetArray()[group.Index];
        var rotation = new Dictionary<string, float>();
        foreach (var (path, text) in edits)
        {
            if (!allowed.TryGetValue(path, out var field)) throw new InvalidOperationException("This helper field is not editable.");
            if (text == field.Value) continue;
            Byml value;
            if (field.Type == BymlNodeType.Float)
            {
                var number = float.Parse(text, CultureInfo.InvariantCulture);
                if (!float.IsFinite(number)) throw new ArgumentException("Helper values must be finite.");
                if (number == float.Parse(field.Value, CultureInfo.InvariantCulture)) continue;
                value = new(number);
            }
            else if (field.Type == BymlNodeType.Bool) value = new(bool.Parse(text));
            else
            {
                int index = int.Parse(text, CultureInfo.InvariantCulture);
                int count = path is "BoneNameId" or "BaseBoneNameId" ? map["BoneNames"].GetArray().Count : map.TryGetValue("DriverParams", out var drivers) ? drivers.GetArray().Count : 0;
                if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(path, "Choose an existing bone or driver.");
                value = new(index);
            }
            if (path.StartsWith("$Rotation", StringComparison.Ordinal)) { rotation[path] = value.GetFloat(); continue; }
            var segments = path.Split('/'); var parent = entry;
            foreach (var segment in segments[..^1]) parent = parent.Type == BymlNodeType.Array ? parent.GetArray()[int.Parse(segment)] : parent.GetMap()[segment];
            if (parent.Type == BymlNodeType.Array) parent.GetArray()[int.Parse(segments[^1])] = value;
            else parent.GetMap()[segments[^1]] = value;
            if (path == "BoneNameId" && entry.GetMap().ContainsKey("BoneName")) entry.GetMap()["BoneName"] = map["BoneNames"].GetArray()[value.GetInt()];
        }
        if (rotation.Count > 0)
        {
            float Angle(string axis) => rotation.GetValueOrDefault("$Rotation" + axis, float.Parse(allowed["$Rotation" + axis].Value, CultureInfo.InvariantCulture));
            var q = BoneRotationAngles.FromRadians(new(Angle("X"), Angle("Y"), Angle("Z")));
            entry.GetMap()["BaseRotate"] = new(new List<Byml> { new(q.X), new(q.Y), new(q.Z), new(q.W) });
        }
        Commit(root);
    }

    private void Commit(Byml root)
    {
        var little = _bytes[0] == (byte)'Y';
        ushort version = little ? BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(2)) : BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(2));
        var output = root.ToBinary(little ? Endianness.Little : Endianness.Big, version);
        _ = new OotHelperDocument(output);
        if (Byml.FromBinary(output).ToYaml() == Byml.FromBinary(_bytes).ToYaml()) return;
        _undo.Push(_bytes); _redo.Clear(); _bytes = output;
    }
    internal void Undo() { if (_undo.TryPop(out var previous)) { _redo.Push(_bytes); _bytes = previous; } }
    internal void Redo() { if (_redo.TryPop(out var next)) { _undo.Push(_bytes); _bytes = next; } }
    internal void Save(string path)
    {
        if (!path.EndsWith(".bgyml", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Save helper parameters as .bgyml.");
        AtomicFile.WriteAllBytes(path, _bytes); _saved = _bytes;
    }
}
