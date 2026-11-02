using System.Numerics;
using HKCLTool;

namespace PhysicsTool.Core;

internal sealed class ShrineModelPicking
{
    private sealed record Mesh(int Index, int Actor, VisualModelMesh Data, Vector3 Min, Vector3 Max);
    private readonly List<Mesh> _meshes = [];
    internal ShrineModelPicking(VisualModelData model)
    {
        foreach (var (mesh, index) in model.Meshes.Select((m, i) => (m, i)))
        {
            if (!int.TryParse(mesh.Name.Split(':')[0], out var actor)) continue;
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            for (int i = 0; i < mesh.Vertices.Length; i += 8) { var p = Vertex(mesh, i / 8); min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            _meshes.Add(new(index, actor, mesh, min, max));
        }
    }
    internal IEnumerable<int> Meshes(int actor) => _meshes.Where(m => m.Actor == actor).Select(m => m.Index);
    internal (Vector3 Minimum, Vector3 Maximum)? Bounds(int actor)
    {
        var meshes = _meshes.Where(m => m.Actor == actor).ToArray();
        return meshes.Length == 0 ? null : (meshes.Select(m => m.Min).Aggregate(Vector3.Min), meshes.Select(m => m.Max).Aggregate(Vector3.Max));
    }
    internal int? Pick(Vector3 origin, Vector3 direction, ISet<int> hidden)
    {
        int? actor = null; float closest = float.MaxValue;
        foreach (var mesh in _meshes)
        {
            if (hidden.Contains(mesh.Index) || !IntersectsBox(origin, direction, mesh.Min, mesh.Max, closest)) continue;
            for (int i = 0; i + 2 < mesh.Data.Indices.Length; i += 3)
            {
                var a = Vertex(mesh.Data, (int)mesh.Data.Indices[i]);
                var ab = Vertex(mesh.Data, (int)mesh.Data.Indices[i + 1]) - a; var ac = Vertex(mesh.Data, (int)mesh.Data.Indices[i + 2]) - a;
                var h = Vector3.Cross(direction, ac); float determinant = Vector3.Dot(ab, h);
                if (Math.Abs(determinant) < 1e-8f) continue;
                var s = origin - a; float u = Vector3.Dot(s, h) / determinant; if (u < 0 || u > 1) continue;
                var q = Vector3.Cross(s, ab); float v = Vector3.Dot(direction, q) / determinant; if (v < 0 || u + v > 1) continue;
                float distance = Vector3.Dot(ac, q) / determinant;
                if (distance >= 0 && distance < closest) { closest = distance; actor = mesh.Actor; }
            }
        }
        return actor;
    }
    private static Vector3 Vertex(VisualModelMesh mesh, int i) => new(mesh.Vertices[i * 8], mesh.Vertices[i * 8 + 1], mesh.Vertices[i * 8 + 2]);
    private static bool IntersectsBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, float far)
    {
        float near = 0;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = direction[axis];
            if (Math.Abs(d) < 1e-10f) { if (o < min[axis] || o > max[axis]) return false; continue; }
            float a = (min[axis] - o) / d, b = (max[axis] - o) / d;
            near = Math.Max(near, Math.Min(a, b)); far = Math.Min(far, Math.Max(a, b)); if (near > far) return false;
        }
        return true;
    }
}
