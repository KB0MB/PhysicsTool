using System.Numerics;
using BymlLibrary;

namespace PhysicsTool.Core;

internal sealed record ShrineActorLink(ulong Destination, string Name);
internal sealed record ShrineActor(int Index, string Name, string Label, ulong Hash, Vector3 Position, Vector3 Rotation,
    Vector3 Scale, ulong PlacementId, uint GroupId, ulong GroupGuid, string MergedPath,
    IReadOnlyList<ShrineActorLink> Links, IReadOnlyList<string> Parameters)
{
    internal Matrix4x4 WorldTransform { get; init; } = Matrix4x4.Identity;
    internal string SourceScene { get; init; } = "";
    internal int SourceIndex { get; init; }
    internal int ParentIndex { get; init; } = -1;
    internal Vector3 LocalPosition { get; init; }
    public override string ToString() => string.IsNullOrWhiteSpace(Label) ? Name : Name + " / " + Label;
}

internal sealed class ShrineSceneDocument
{
    internal IReadOnlyList<ShrineActor> Actors { get; }
    internal int UnresolvedLinkCount { get; }
    internal ShrineSceneDocument(List<ShrineActor> actors)
    {
        Actors = actors;
        var hashes = actors.Select(a => a.Hash).ToHashSet();
        UnresolvedLinkCount = actors.Sum(a => a.Links.Count(l => !hashes.Contains(l.Destination)));
    }
    internal static ShrineSceneDocument Load(string path, IEnumerable<string>? roots = null) => Parse(BphscDocument.ReadResource(path, roots));
    internal static ShrineSceneDocument Parse(byte[] bytes)
    {
        var root = Byml.FromBinary(bytes).GetMap();
        if (!root.TryGetValue("Actors", out var array) || array.Type != BymlNodeType.Array)
            throw new InvalidDataException("This BANC does not contain an Actors list.");
        if (array.GetArray().Count > 100000) throw new InvalidDataException("Scene exceeds the 100,000 actor limit.");
        var actors = new List<ShrineActor>();
        foreach (var item in array.GetArray())
        {
            var map = item.GetMap(); var phive = Child(map, "Phive"); var placement = Child(phive, "Placement"); var dynamic = Child(map, "Dynamic");
            var links = new List<ShrineActorLink>();
            if (map.TryGetValue("Links", out var node))
                foreach (var link in node.GetArray()) links.Add(new(Unsigned(link.GetMap(), "Dst"), Text(link.GetMap(), "Name")));
            var parameters = dynamic.Select(p => p.Key + ": " + Display(p.Value)).ToArray();
            actors.Add(new(actors.Count, Text(map, "Gyaml"), Text(map, "Name"), Unsigned(map, "Hash"),
                Vector(map, "Translate", Vector3.Zero), Vector(map, "Rotate", Vector3.Zero), Vector(map, "Scale", Vector3.One),
                Unsigned(placement, "ID"), checked((uint)Unsigned(placement, "GroupID")), Unsigned(placement, "GroupGUID"), Text(dynamic, "BancPath"), links, parameters));
        }
        if (actors.Select(a => a.Hash).Distinct().Count() != actors.Count) throw new InvalidDataException("Duplicate actor hashes in this BANC; scene selection would be ambiguous.");
        return new(actors);
    }
    private static IDictionary<string, Byml> Child(IDictionary<string, Byml> map, string key) => map.TryGetValue(key, out var node) ? node.GetMap() : new Dictionary<string, Byml>();
    private static string Text(IDictionary<string, Byml> map, string key) => map.TryGetValue(key, out var node) ? node.GetString() : "";
    private static ulong Unsigned(IDictionary<string, Byml> map, string key) => map.TryGetValue(key, out var node) ? node.Type switch
    { BymlNodeType.UInt64 => node.GetUInt64(), BymlNodeType.UInt32 => node.GetUInt32(), BymlNodeType.Int64 => checked((ulong)node.GetInt64()), BymlNodeType.Int => checked((ulong)node.GetInt()), _ => throw new InvalidDataException("Invalid actor identifier: " + key) } : 0;
    private static Vector3 Vector(IDictionary<string, Byml> map, string key, Vector3 fallback)
    {
        if (!map.TryGetValue(key, out var node)) return fallback;
        var values = node.GetArray(); if (values.Count != 3) throw new InvalidDataException(key + " must have three coordinates.");
        float Number(Byml v) => v.Type == BymlNodeType.Int ? v.GetInt() : v.GetFloat();
        var result = new Vector3(Number(values[0]), Number(values[1]), Number(values[2]));
        if (!float.IsFinite(result.X) || !float.IsFinite(result.Y) || !float.IsFinite(result.Z)) throw new InvalidDataException("Nonfinite actor coordinates.");
        return result;
    }
    private static string Display(Byml node) => node.Type switch
    {
        BymlNodeType.String => node.GetString(), BymlNodeType.Bool => node.GetBool() ? "true" : "false",
        BymlNodeType.Float => node.GetFloat().ToString("G7", System.Globalization.CultureInfo.InvariantCulture),
        BymlNodeType.Int => node.GetInt().ToString(), BymlNodeType.UInt32 => node.GetUInt32().ToString(),
        BymlNodeType.UInt64 => $"0x{node.GetUInt64():x16}", _ => "[" + node.Type + "]"
    };
}
