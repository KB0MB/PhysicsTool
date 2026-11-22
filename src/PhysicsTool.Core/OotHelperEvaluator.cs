using BymlLibrary;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

// Compile BYML once. Playback only evaluates numeric curves and skeleton matrices.
internal sealed class OotHelperEvaluator
{
    private sealed record Connection(int Driver, int Attribute, Vector4[] Keys);
    private sealed record Channel(float Static, Connection[] Inputs);
    private sealed record Basis(int Bone, int Base, Vector3 Position, Quaternion Rotation, Vector3 Aim, Vector3 Up);
    private sealed record Driven(Basis Basis, Channel[] Channels)
    {
        public bool Rotate { get; } = Channels.Take(3).Any(c => c.Inputs.Length != 0);
        public bool Translate { get; } = Channels.Skip(3).Take(3).Any(c => c.Inputs.Length != 0);
        public bool Scale { get; } = Channels.Skip(6).Take(3).Any(c => c.Inputs.Length != 0);
    }
    private readonly Basis[] _drivers;
    private readonly Dictionary<int, Driven> _driven = new();
    private readonly int[] _parents;
    private readonly int[] _evaluationOrder;
    public IReadOnlyList<string> Warnings { get; }
    public int DrivenCount => _driven.Count;

    // Murmur3-32 of the authored channel names (seed zero), verified against
    // DriverAttribute values in the native helper files.
    private static readonly uint[] Attributes = [0x7273210a, 0x26df7675, 0x6de5cadd,
        0xa13adc76, 0x509c8278, 0x3f3a1269, 0x4ee71a4f, 0x0bce334d, 0xbb47bf0a];

    public OotHelperEvaluator(byte[] bytes, IReadOnlyList<VisualModelBone> bones)
    {
        var root = Byml.FromBinary(bytes).GetMap();
        var names = root["BoneNames"].GetArray().Select(n => n.GetString()).ToArray();
        var indices = bones.Select((b, i) => (b.Name, i)).ToDictionary(p => p.Name, p => p.i, StringComparer.OrdinalIgnoreCase);
        var ids = bones.Select((b, i) => (b.Index, i)).ToDictionary(p => p.Index, p => p.i);
        _parents = bones.Select(b => ids.GetValueOrDefault(b.ParentIndex, -1)).ToArray();
        var warnings = new HashSet<string>();
        int Index(int id) => id >= 0 && id < names.Length ? indices.GetValueOrDefault(names[id], -1) : -1;
        Basis ReadBasis(IDictionary<string, Byml> m)
        {
            int boneId = m["BoneNameId"].GetInt(), bone = Index(boneId), baseId = m["BaseBoneNameId"].GetInt();
            if (bone < 0) warnings.Add("Helper bone missing: " + names.ElementAtOrDefault(boneId));
            var q = m["BaseRotate"].GetArray().Select(v => v.GetFloat()).ToArray();
            if (q.Length != 4) throw new InvalidDataException("Invalid helper base quaternion.");
            var rotation = new Quaternion(q[0], q[1], q[2], q[3]);
            if (rotation.LengthSquared() < 1e-12f) throw new InvalidDataException("Zero helper base quaternion.");
            int space = baseId == -2 && bone >= 0 ? _parents[bone] : Index(baseId);
            if (baseId >= 0 && space < 0)
            {
                warnings.Add("Helper base bone missing: " + names.ElementAtOrDefault(baseId));
                bone = -1;
            }
            return new(bone, space, Vector(m["BaseTranslate"]), Quaternion.Normalize(rotation), Vector(m["AimAxis"]), Vector(m["UpAxis"]));
        }
        _drivers = root["DriverParams"].GetArray().Select(v => ReadBasis(v.GetMap())).ToArray();
        foreach (var value in root["DrivenParams"].GetArray())
        {
            var m = value.GetMap(); var basis = ReadBasis(m); bool supported = basis.Bone >= 0;
            var channels = OotHelperDocument.Channels.Select((name, c) =>
            {
                float fallback = c >= 6 ? 1 : 0;
                if (!m.TryGetValue(name, out var node)) return new Channel(fallback, []);
                var channel = node.GetMap();
                if (!channel.TryGetValue("InConnections", out var inputs)) return new Channel(channel.TryGetValue("StaticValue", out var s) ? s.GetFloat() : fallback, []);
                var connections = new List<Connection>();
                foreach (var input in inputs.GetArray())
                {
                    var connection = input.GetMap(); int driver = connection["DriverParamId"].GetInt();
                    uint hash = connection["DriverAttribute"].GetUInt32(); int attribute = Array.IndexOf(Attributes, hash);
                    var curve = connection["DrivenCurve"].GetMap();
                    if (attribute < 0 || driver < 0 || driver >= _drivers.Length || _drivers[driver].Bone < 0 || curve["Type"].GetString() != "Hermit2DSplit")
                    { supported = false; warnings.Add($"Unsupported helper input: {name}, attribute 0x{hash:x8}."); continue; }
                    var data = curve["Data"].GetArray().Select(v => v.GetFloat()).ToArray();
                    if (data.Length == 0 || data.Length % 4 != 0 || data.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Invalid helper curve.");
                    var keys = Enumerable.Range(0, data.Length / 4).Select(i => new Vector4(data[i*4], data[i*4+1], data[i*4+2], data[i*4+3])).ToArray();
                    if (keys.Zip(keys.Skip(1)).Any(p => p.First.X >= p.Second.X)) throw new InvalidDataException("Helper keys are not increasing.");
                    connections.Add(new(driver, attribute, keys));
                }
                return new Channel(channel.TryGetValue("StaticValue", out var value) ? value.GetFloat() : fallback, connections.ToArray());
            }).ToArray();
            if (supported) _driven[basis.Bone] = new(basis, channels);
        }
        _evaluationOrder = root.TryGetValue("UseBoneNamesOrderAsTopology", out var topology) && topology.GetBool()
            ? Enumerable.Range(0, names.Length).Select(Index).Where(_driven.ContainsKey).Distinct().ToArray()
            : _driven.Keys.ToArray();
        Warnings = warnings.ToArray();
    }

    public VisualModelData Evaluate(VisualModelData source)
    {
        if (source.Bones.Count != _parents.Length) throw new InvalidOperationException("Helper skeleton changed; rebind the actor.");
        var input = source.Bones.Select(Matrix).ToArray();
        var local = new Matrix4x4[input.Length]; var output = new Matrix4x4[input.Length]; var state = new byte[input.Length];
        var driverValues = new Vector3?[_drivers.Length, 3];
        for (int i = 0; i < input.Length; i++)
        {
            local[i] = input[i];
            if (_parents[i] >= 0 && Matrix4x4.Invert(input[_parents[i]], out var inverse)) local[i] *= inverse;
        }
        Matrix4x4 World(int i)
        {
            if (i < 0) return Matrix4x4.Identity;
            if (state[i] == 2) return output[i];
            if (state[i] == 1) throw new InvalidDataException("Cyclic helper skeleton hierarchy.");
            state[i] = 1;
            output[i] = local[i] * World(_parents[i]);
            state[i] = 2;
            return output[i];
        }
        // Native traversal commits each helper before visiting the next authored
        // entry. Sampling a driver must not evaluate helpers later in the list.
        foreach (int i in _evaluationOrder)
        {
            var driven = _driven[i];
            if (driven.Rotate || driven.Translate || driven.Scale)
            {
                float ChannelValue(int c)
                {
                    var channel = driven.Channels[c];
                    if (channel.Inputs.Length == 0) return channel.Static;
                    float sum = 0;
                    foreach (var connection in channel.Inputs)
                        sum += SidecarPreviewEvaluator.EvaluateTotkCurve(connection.Keys, Driver(connection.Driver, connection.Attribute));
                    return sum;
                }
                var b = driven.Basis;
                Matrix4x4 RelativeBasis()
                {
                    if (!Matrix4x4.Invert(World(_parents[i]), out var inverseParent))
                        throw new InvalidDataException("Singular helper parent transform.");
                    return World(b.Base) * inverseParent;
                }
                if (driven.Rotate)
                {
                    var rotation = Quaternion.Normalize(b.Rotation * SidecarPreviewEvaluator.BuildBotwRotation(ChannelValue(0), ChannelValue(1), ChannelValue(2), b.Aim, b.Up));
                    var relative = Matrix4x4.CreateFromQuaternion(rotation) * RelativeBasis();
                    VisualBoneTransform.Decompose(relative, out _, out var drivenRotation, out _);
                    VisualBoneTransform.Decompose(local[i], out var inputScale, out _, out var inputPosition);
                    local[i] = Matrix4x4.CreateScale(inputScale) * Matrix4x4.CreateFromQuaternion(drivenRotation);
                    local[i].Translation = inputPosition;
                    Array.Clear(state);
                }
                if (driven.Translate)
                {
                    var position = b.Position + new Vector3(ChannelValue(3), ChannelValue(4), ChannelValue(5));
                    local[i].Translation = Vector3.Transform(position, RelativeBasis());
                    Array.Clear(state);
                }
                if (driven.Scale)
                {
                    var scale = new Vector3(ChannelValue(6), ChannelValue(7), ChannelValue(8));
                    VisualBoneTransform.Decompose(local[i], out _, out var rotation, out var position);
                    local[i] = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation);
                    local[i].Translation = position;
                    Array.Clear(state);
                }
            }
        }
        float Driver(int id, int attribute)
        {
            int group = attribute / 3;
            if (driverValues[id, group] is not { } cached)
            {
                var d = _drivers[id];
                if (group == 2)
                    VisualBoneTransform.Decompose(local[d.Bone], out cached, out _, out _);
                else
                {
                    var relative = World(d.Bone);
                    if (!Matrix4x4.Invert(World(d.Base), out var inverse))
                        throw new InvalidDataException("Singular helper driver basis.");
                    relative *= inverse;
                    VisualBoneTransform.Decompose(relative, out _, out var rotation, out var position);
                    cached = group == 0
                        ? SidecarPreviewEvaluator.MeasureBotwRotation(Quaternion.Normalize(Quaternion.Inverse(d.Rotation) * rotation), d.Aim, d.Up)
                        : position - d.Position;
                }
                driverValues[id, group] = cached;
            }
            return attribute % 3 == 0 ? cached.X : attribute % 3 == 1 ? cached.Y : cached.Z;
        }
        var result = new VisualModelData { SourcePath = source.SourcePath, AnimationFrame = source.AnimationFrame, AnimationSourcePath = source.AnimationSourcePath };
        for (int i = 0; i < input.Length; i++)
        {
            var b = source.Bones[i];
            try { result.Bones.Add(VisualBoneTransform.Bone(b.Index, b.ParentIndex, b.Name, World(i))); }
            catch (InvalidDataException ex) { throw new InvalidDataException("OoT helper produced an invalid transform: " + b.Name, ex); }
        }
        return result;
    }
    private static Matrix4x4 Matrix(VisualModelBone b) => b.WorldMatrix;
    private static Vector3 Vector(Byml node)
    { var m = node.GetMap(); return new(m["X"].GetFloat(), m["Y"].GetFloat(), m["Z"].GetFloat()); }
}
