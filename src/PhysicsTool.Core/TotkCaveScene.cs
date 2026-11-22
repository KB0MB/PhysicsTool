using System.Buffers.Binary;
using System.Numerics;
using HKCLTool;

namespace PhysicsTool.Core;

internal static class TotkCaveScene
{
    internal static VisualModelData Load(string root, string scenePath, List<string> warnings, IProgress<string>? progress, CancellationToken cancellation)
    {
        var result = new VisualModelData { BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
        if (!TotkTerrain.TryCell(scenePath, out float x, out float z)) return result;
        string folder = Path.Combine(root, "Cave/cave017");
        if (!Directory.Exists(folder)) { warnings.Add("Cave geometry: Cave/cave017 is missing from this dump."); return result; }
        var textures = new Dictionary<int, VisualModelTexture?>(); byte[]? palette = null;
        bool Intersects(Vector3 min, Vector3 max) => max.X >= x && min.X <= x + 1000 && max.Z >= z && min.Z <= z + 1000;
        string[] files;
        try { files = Directory.GetFiles(folder, "C.crbin", SearchOption.AllDirectories); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { warnings.Add("Cave geometry: " + ex.Message); return result; }
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            string name = Path.GetFileName(Path.GetDirectoryName(file))!;
            if (!name.StartsWith("Cave_", StringComparison.Ordinal) && !name.StartsWith("Well_", StringComparison.Ordinal)) continue;
            try
            {
                var cave = new TotkCaveDocument(ReadBounded(file, 16 * 1024 * 1024));
                if (!Intersects(cave.Minimum, cave.Maximum)) continue;
                progress?.Report("Loading cave " + name + "...");
                var pages = new Dictionary<int, byte[]>();
                var caveModel = new VisualModelData();
                var batches = new Dictionary<(int X, int Y, int Z, int Layer), Batch>();
                long triangleCount = 0;
                // Select whole cave resources by map cell, not individual chunks:
                // entrances and tunnels commonly cross cell boundaries.
                foreach (var node in cave.Nodes.Where(n => n.Lod == cave.Subdivisions))
                {
                    cancellation.ThrowIfCancellationRequested();
                    float side = cave.SideLength * MathF.Pow(2, cave.Subdivisions - node.Lod);
                    var origin = cave.Origin - new Vector3(side * .49993896f) + new Vector3(node.X, node.Y, node.Z) * side;
                    float scale = side / 4096;
                    var center = (node.Min + node.Max) * .5f;
                    int bucketX = (int)MathF.Floor(center.X / 100), bucketY = (int)MathF.Floor(center.Y / 100), bucketZ = (int)MathF.Floor(center.Z / 100);
                    for (int s = node.Stream; s < node.Stream + node.Streams; s++)
                    {
                        var stream = cave.Streams[s];
                        triangleCount += stream.Triangles;
                        if (triangleCount > 2000000) throw new InvalidDataException("Cave exceeds the two-million-triangle preview limit.");
                        if (!pages.TryGetValue(stream.Page, out var page))
                        {
                            string pagePath = Path.Combine(Path.GetDirectoryName(file)!, $"C.crbin.{cave.Id:x8}", $"{stream.Page:D6}.chunk");
                            pages[stream.Page] = page = TotkMeshCodec.DecodeChunk(ReadBounded(pagePath, 64 * 1024 * 1024), cave.Id, cave.PageSizes[stream.Page]);
                            if (pages.Values.Sum(p => (long)p.Length) > 256 * 1024 * 1024) throw new InvalidDataException("Cave page cache exceeds the preview limit.");
                        }
                        for (int t = 0; t < stream.Triangles; t++)
                        {
                            if ((t & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                            int p = checked(stream.FirstIndex * 2 + t * 6);
                            var a = Vertex(page, BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(p)), origin, scale, node.Material);
                            var b = Vertex(page, BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(p + 2)), origin, scale, node.Material);
                            var c = Vertex(page, BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(p + 4)), origin, scale, node.Material);
                            int material = a.Material;
                            if ((uint)material >= cave.Materials.Length) throw new InvalidDataException("Cave vertex material is outside the palette.");
                            var key = (bucketX, bucketY, bucketZ, cave.Materials[material].Layer);
                            if (!batches.TryGetValue(key, out var batch)) batches[key] = batch = new();
                            batch.Add(a, cave.Materials[material]); batch.Add(b, cave.Materials[material]); batch.Add(c, cave.Materials[material]);
                        }
                    }
                }
                foreach (var (key, batch) in batches)
                {
                    int layer = key.Layer;
                    if (!textures.TryGetValue(layer, out var texture))
                    {
                        try
                        {
                            palette ??= ReadBounded(Path.Combine(root, "TexToGo/MaterialAlb.txtg"), 128 * 1024 * 1024);
                            texture = TexToGoTexture.Decode(palette, "Cave material", layer);
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
                        { warnings.Add($"Cave texture layer {layer}: {ex.Message}"); }
                        textures[layer] = texture;
                    }
                    caveModel.Meshes.Add(new() { Name = $"cave:{name}/{key.X},{key.Y},{key.Z}/{layer}", Vertices = batch.Vertices.ToArray(),
                        Indices = batch.Indices.ToArray(), Texture = texture, CullingMode = VisualModelCullingMode.Off });
                }
                // Publish only a complete cave, never a partial shell after a bad page.
                if (result.Meshes.Sum(m => (long)m.Vertices.Length / 8) + caveModel.Meshes.Sum(m => (long)m.Vertices.Length / 8) > 4000000)
                    throw new InvalidDataException("Cave geometry exceeds the four-million-vertex scene limit.");
                foreach (var mesh in caveModel.Meshes)
                {
                    result.Meshes.Add(mesh); result.TriangleCount += mesh.Indices.Length / 3;
                    for (int i = 0; i < mesh.Vertices.Length; i += 8)
                    {
                        var p = new Vector3(mesh.Vertices[i], mesh.Vertices[i + 1], mesh.Vertices[i + 2]);
                        result.BoundsMinimum = Vector3.Min(result.BoundsMinimum, p); result.BoundsMaximum = Vector3.Max(result.BoundsMaximum, p);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or OverflowException or UnauthorizedAccessException)
            { warnings.Add($"Cave {name}: {ex.Message}"); }
        }
        if (result.Meshes.Count > 0) warnings.Add("Cave surfaces are read-only previews with approximate material blending.");
        return result;
    }

    internal readonly record struct CaveVertex(Vector3 Position, Vector3 Normal, int Material);
    internal static CaveVertex Vertex(byte[] page, int index, Vector3 origin, float scale, int baseMaterial)
    {
        int offset = checked(index * 28);
        if (offset < 0 || offset > page.Length - 28) throw new InvalidDataException("Cave vertex is outside its page.");
        var data = page.AsSpan(offset, 28);
        uint materials = BinaryPrimitives.ReadUInt32LittleEndian(data);
        ulong packed = BinaryPrimitives.ReadUInt64LittleEndian(data[16..]);
        uint high = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]);
        var position = origin + new Vector3((packed >> 6) & 8191, (packed >> 19) & 8191, (packed >> 32) & 8191) * scale;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new InvalidDataException("Nonfinite cave vertex.");
        int w0 = (int)(packed >> 45 & 31), w1 = (int)(packed >> 50 & 31), w2 = Math.Max(0, 31 - w0 - w1);
        int shift = w0 >= w1 && w0 >= w2 ? 5 : w1 >= w2 ? 14 : 23;
        int material = checked(baseMaterial + (int)(materials >> shift & 511));
        float nx = (high & 127) / 127f * 2 - 1, ny = (high >> 7 & 127) / 127f * 2 - 1;
        float nz = 1 - Math.Abs(nx) - Math.Abs(ny);
        if (nz < 0) (nx, ny) = ((1 - Math.Abs(ny)) * (nx >= 0 ? 1 : -1), (1 - Math.Abs(nx)) * (ny >= 0 ? 1 : -1));
        var normal = Vector3.Normalize(new(nx, ny, nz));
        return new(position, normal, material);
    }

    private static byte[] ReadBounded(string path, int limit)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > limit) throw new InvalidDataException("Cave resource exceeds its size limit.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
    private sealed class Batch
    {
        internal readonly List<float> Vertices = [];
        internal readonly List<uint> Indices = [];
        private readonly Dictionary<(CaveVertex, Vector2), uint> _indices = [];
        internal void Add(CaveVertex vertex, TotkCaveDocument.Material material)
        {
            var p = vertex.Position; var n = vertex.Normal;
            var uv = new Vector2(Vector3.Dot(p, material.U), Vector3.Dot(p, material.V)) * material.Scale;
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y)) throw new InvalidDataException("Nonfinite cave texture coordinate.");
            if (!_indices.TryGetValue((vertex, uv), out uint index))
            {
                index = (uint)_indices.Count; _indices.Add((vertex, uv), index);
                Vertices.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z, uv.X, uv.Y]);
            }
            Indices.Add(index);
        }
    }
}
