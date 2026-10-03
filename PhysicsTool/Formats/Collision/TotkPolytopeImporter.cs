using System.Numerics;
using BymlLibrary;

namespace HKCLTool;

internal static class TotkPolytopeImporter
{
    public static IReadOnlyList<BotwCollisionMesh> Import(string path)
    {
        if (new FileInfo(path).Length > 32*1024*1024) throw new InvalidDataException("ShapeParam exceeds the 32 MiB import limit.");
        var data=File.ReadAllBytes(path);
        var root = data.AsSpan().StartsWith("YB"u8) || data.AsSpan().StartsWith("BY"u8)
            ? Byml.FromBinary(data) : Byml.FromText(System.Text.Encoding.UTF8.GetString(data));
        return Read(root);
    }

    internal static IReadOnlyList<BotwCollisionMesh> Read(Byml root)
    {
        var map=root.GetMap();
        if (map.ContainsKey("$parent")) throw new InvalidDataException("This ShapeParam inherits another file. Resolve its parent first; partial collisions are not imported.");
        foreach (var pair in map)
        {
            if (pair.Key != "Polytope" && pair.Value.Type == BymlNodeType.Array && pair.Value.GetArray().Count > 0)
                throw new InvalidDataException($"This ShapeParam also contains '{pair.Key}' shapes. Mixed shape files are not yet supported; importing only the polytopes would lose collision.");
        }
        if (!map.TryGetValue("Polytope",out var list) || list.GetArray().Count == 0) throw new InvalidDataException("The ShapeParam contains no polytopes.");
        if (list.GetArray().Count > 4096) throw new InvalidDataException("ShapeParam exceeds the 4,096-shape import limit.");
        var result=new List<BotwCollisionMesh>();
        foreach (var node in list.GetArray())
        {
            var p=node.GetMap();
            var name=p.TryGetValue("Name",out var n) && !string.IsNullOrWhiteSpace(n.GetString()) ? n.GetString() : "Polytope_" + result.Count;
            Vector3 Get(string key,Vector3 fallback) => p.TryGetValue(key,out var value) ? Vector(value) : fallback;
            var rotation=Get("OffsetRotation",Vector3.Zero) * (MathF.PI/180);
            var transform=Matrix4x4.CreateScale(Get("OffsetScale",Vector3.One)) * Matrix4x4.CreateRotationX(rotation.X) * Matrix4x4.CreateRotationY(rotation.Y) * Matrix4x4.CreateRotationZ(rotation.Z) * Matrix4x4.CreateTranslation(Get("OffsetTranslation",Vector3.Zero));
            var presets=p.TryGetValue("MaterialPresets",out var mats) ? string.Join(", ",mats.GetArray().Select(m=>m.GetString())) : "Material_Wood";
            var vertices=p["Vertices"].GetArray();
            if (vertices.Count > 100_000) throw new InvalidDataException("A polytope exceeds the 100,000-point import limit.");
            result.Add(TotkPolytopeBuilder.Hull(name,vertices.Select(v=>Vector3.Transform(Vector(v),transform)),presets));
        }
        return result;
    }

    private static Vector3 Vector(Byml node)
    {
        var map=node.GetMap();
        float Value(string key) => map[key].Type switch { BymlNodeType.Float=>map[key].GetFloat(), BymlNodeType.Int=>map[key].GetInt(), _=>throw new InvalidDataException("Expected a numeric ShapeParam coordinate.") };
        var v=new Vector3(Value("X"),Value("Y"),Value("Z"));
        if (!TotkPolytopeBuilder.Finite(v)) throw new InvalidDataException("ShapeParam contains non-finite coordinates.");
        return v;
    }
}
