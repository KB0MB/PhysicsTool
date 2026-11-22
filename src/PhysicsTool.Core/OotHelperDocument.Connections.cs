using BymlLibrary;

namespace PhysicsTool.Core;

internal sealed record OotHelperConnection(int Bone, string Channel, int Index, int Driver, uint Attribute)
{
    public override string ToString() => $"{OotHelperDocument.Label(Channel)} / connection {Index + 1} / driver {Driver}";
}

internal readonly record struct OotCurveKey(float X, float Value, float Incoming, float Outgoing);

internal sealed partial class OotHelperDocument
{
    internal void ApplyConnections(byte[] edited, int bone)
    {
        var root = Byml.FromBinary(_bytes);
        var target = root.GetMap()["DrivenParams"].GetArray()[bone].GetMap();
        var source = Byml.FromBinary(edited).GetMap()["DrivenParams"].GetArray()[bone].GetMap();
        foreach (var channel in Channels)
            if (source.TryGetValue(channel, out var value)) target[channel] = value;
        Commit(root);
    }
    internal static readonly string[] Channels = ["BendH", "BendV", "Roll", "TranslateX", "TranslateY", "TranslateZ", "ScaleX", "ScaleY", "ScaleZ"];

    internal IReadOnlyList<OotHelperConnection> Connections(int? bone = null)
    {
        var driven = Byml.FromBinary(_bytes).GetMap()["DrivenParams"].GetArray();
        var result = new List<OotHelperConnection>();
        for (int b = 0; b < driven.Count; b++)
        {
            if (bone.HasValue && bone != b) continue;
            foreach (var channel in Channels)
                if (driven[b].GetMap().TryGetValue(channel, out var value) && value.GetMap().TryGetValue("InConnections", out var array))
                    for (int i = 0; i < array.GetArray().Count; i++)
                    {
                        var c = array.GetArray()[i].GetMap();
                        result.Add(new(b, channel, i, c["DriverParamId"].GetInt(), c["DriverAttribute"].GetUInt32()));
                    }
        }
        return result;
    }

    private static Byml Connection(Byml root, OotHelperConnection connection) =>
        root.GetMap()["DrivenParams"].GetArray()[connection.Bone].GetMap()[connection.Channel].GetMap()["InConnections"].GetArray()[connection.Index];

    internal IReadOnlyList<OotCurveKey> CurveKeys(OotHelperConnection connection)
    {
        var curve = Connection(Byml.FromBinary(_bytes), connection).GetMap()["DrivenCurve"].GetMap();
        if (curve["Type"].GetString() != "Hermit2DSplit") throw new InvalidOperationException("Only Hermit2DSplit curves can be edited here. This curve has been preserved.");
        var data = curve["Data"].GetArray();
        if (data.Count % 4 != 0) throw new InvalidDataException("Incomplete helper curve key.");
        return Enumerable.Range(0, data.Count / 4).Select(i => new OotCurveKey(data[4*i].GetFloat(), data[4*i+1].GetFloat(), data[4*i+2].GetFloat(), data[4*i+3].GetFloat())).ToArray();
    }

    internal void SetCurveKeys(OotHelperConnection connection, IReadOnlyList<OotCurveKey> keys)
    {
        if (CurveKeys(connection).SequenceEqual(keys)) return;
        if (keys.Count < 2) throw new ArgumentException("Keep at least two keys in the response curve.");
        for (int i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            if (!float.IsFinite(key.X) || !float.IsFinite(key.Value) || !float.IsFinite(key.Incoming) || !float.IsFinite(key.Outgoing))
                throw new ArgumentException("Curve keys must contain finite values.");
            if (i > 0 && key.X <= keys[i-1].X) throw new ArgumentException("Each key's input must be greater than the previous key's input.");
        }
        var root = Byml.FromBinary(_bytes);
        var curve = Connection(root, connection).GetMap()["DrivenCurve"].GetMap();
        curve["Data"] = new(keys.SelectMany(k => new[] { k.X, k.Value, k.Incoming, k.Outgoing }).Select(v => new Byml(v)).ToList());
        curve["MaxX"] = new(keys[^1].X);
        Commit(root);
    }

    internal void AddConnection(int bone, string channel, int driver, OotHelperConnection template, byte[]? templateDocument = null)
    {
        if (!Channels.Contains(channel)) throw new ArgumentException("Choose an output channel.");
        var root = Byml.FromBinary(_bytes);
        var map = root.GetMap();
        if (driver < 0 || driver >= map["DriverParams"].GetArray().Count) throw new ArgumentException("Choose an existing driver.");
        // Copy the native input attribute and optional metadata, rather than guessing enum hashes.
        var source = Connection(templateDocument == null ? root : Byml.FromBinary(templateDocument), template);
        var clone = Byml.FromBinary(source.ToBinary(Revrs.Endianness.Little, 7));
        clone.GetMap()["DriverParamId"] = new(driver);
        float value = channel.StartsWith("Scale", StringComparison.Ordinal) ? 1f : 0f;
        var driven = map["DrivenParams"].GetArray()[bone].GetMap();
        if (!driven.TryGetValue(channel, out var output)) driven[channel] = output = new(new Dictionary<string, Byml>());
        if (output.GetMap().TryGetValue("StaticValue", out var original)) value = original.GetFloat();
        // A neutral curve avoids an unexpected pose jump when replacing a static channel.
        clone.GetMap()["DrivenCurve"] = new(new Dictionary<string, Byml>
        {
            ["Type"] = new("Hermit2DSplit"), ["MaxX"] = new(1f),
            ["Data"] = new(new List<Byml> { new(-1f), new(value), new(0f), new(0f), new(1f), new(value), new(0f), new(0f) })
        });
        if (!output.GetMap().TryGetValue("InConnections", out var connections)) output.GetMap()["InConnections"] = connections = new(new List<Byml>());
        if (connections.GetArray().Any(c => c.GetMap()["DriverParamId"].GetInt() == driver && c.GetMap()["DriverAttribute"].GetUInt32() == template.Attribute))
            throw new InvalidOperationException("This output already has a connection from that driver and input attribute.");
        output.GetMap().Remove("StaticValue");
        connections.GetArray().Add(clone);
        Commit(root);
    }

    internal void InsertCurveKey(OotHelperConnection connection, float x)
    {
        var keys = CurveKeys(connection).ToList();
        if (!float.IsFinite(x) || keys.Any(k => k.X == x)) throw new ArgumentException("Choose a finite input not already used by another key.");
        if (keys.Count < 2) throw new InvalidOperationException("This curve needs at least two existing keys.");
        int index = keys.FindIndex(k => k.X > x);
        OotCurveKey added;
        if (index == 0 || index < 0)
        {
            var edge = index == 0 ? keys[0] : keys[^1];
            var tangent = index == 0 ? edge.Incoming : edge.Outgoing;
            added = new(x, edge.Value + (x - edge.X) * tangent, tangent, tangent);
        }
        else
        {
            var a = keys[index - 1]; var b = keys[index]; float h = b.X - a.X, t = (x - a.X) / h;
            float value = (2*t*t*t-3*t*t+1)*a.Value + (t*t*t-2*t*t+t)*h*a.Outgoing + (-2*t*t*t+3*t*t)*b.Value + (t*t*t-t*t)*h*b.Incoming;
            float slope = ((6*t*t-6*t)*a.Value + (3*t*t-4*t+1)*h*a.Outgoing + (-6*t*t+6*t)*b.Value + (3*t*t-2*t)*h*b.Incoming) / h;
            added = new(x, value, slope, slope);
        }
        keys.Insert(index < 0 ? keys.Count : index, added);
        SetCurveKeys(connection, keys);
    }

    internal void SetConnectionDriver(OotHelperConnection connection, int driver)
    {
        var root = Byml.FromBinary(_bytes);
        if (driver < 0 || driver >= root.GetMap()["DriverParams"].GetArray().Count) throw new ArgumentException("Choose an existing driver.");
        if (Connections(connection.Bone).Any(c => c.Channel == connection.Channel && c.Index != connection.Index && c.Driver == driver && c.Attribute == connection.Attribute))
            throw new InvalidOperationException("This output already has a connection from that driver and input attribute.");
        Connection(root, connection).GetMap()["DriverParamId"] = new(driver);
        Commit(root);
    }

    internal void RemoveConnection(OotHelperConnection connection)
    {
        var root = Byml.FromBinary(_bytes);
        var channel = root.GetMap()["DrivenParams"].GetArray()[connection.Bone].GetMap()[connection.Channel].GetMap();
        var list = channel["InConnections"].GetArray();
        list.RemoveAt(connection.Index);
        if (list.Count == 0)
        {
            channel.Remove("InConnections");
            channel["StaticValue"] = new(connection.Channel.StartsWith("Scale", StringComparison.Ordinal) ? 1f : 0f);
        }
        Commit(root);
    }
}
