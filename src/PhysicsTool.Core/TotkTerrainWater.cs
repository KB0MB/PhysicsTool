using System.Buffers.Binary;
using System.Numerics;
using HKCLTool;
using static PhysicsTool.Core.TotkTerrainFormat;

namespace PhysicsTool.Core;

internal static class TotkTerrainWater
{
    internal const int Side = 68, SampleBytes = 8;
    internal readonly record struct Sample(float Height, byte Material);
    private readonly record struct Point(Vector3 Position, float Depth, byte Material);

    internal static Sample ReadSample(ReadOnlySpan<byte> data, int x, int z, float heightScale)
    {
        if (data.Length < Side * Side * SampleBytes) throw new InvalidDataException("Truncated terrain water image.");
        if ((uint)x >= Side || (uint)z >= Side) throw new ArgumentOutOfRangeException(nameof(x));
        int p = (z * Side + x) * SampleBytes;
        return new(BinaryPrimitives.ReadUInt16LittleEndian(data[p..]) * heightScale, data[p + 7]);
    }

    internal static float? Add(VisualModelData model, string root, Scene scene, float left, float top,
        float[] groundHeights, Vector3 start, HashSet<string> warnings, IProgress<string>? progress, CancellationToken cancellation)
    {
        const int grid = TotkTerrain.GridSize, chunk = TotkTerrain.ChunkSize, stride = grid + 1;
        const float step = TotkTerrain.Spacing;
        var lookup = new Area?[256];
        var sources = scene.Areas.Where(a => a.HasWater).OrderBy(a => a.Size).ToArray();
        for (int z = 0; z < 16; z++)
        for (int x = 0; x < 16; x++)
        {
            float wx = left + (x + .5f) * 62.5f, wz = top + (z + .5f) * 62.5f;
            lookup[z * 16 + x] = sources.FirstOrDefault(a => a.Size >= 62.5f && Math.Abs(a.X - wx) < a.Size * .5f && Math.Abs(a.Z - wz) < a.Size * .5f);
        }
        var tiles = new Dictionary<string, byte[]>();
        foreach (var group in lookup.OfType<Area>().DistinctBy(a => a.Name).GroupBy(a => ArchiveName(a.Name)))
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report("Loading water " + group.Key + "...");
            try
            {
                var archive = TotkTerrain.OpenArchive(Path.Combine(root, "TerrainArc", "MainField", group.Key + ".water.extm.ta.zs"));
                foreach (var area in group)
                {
                    var entry = archive.Entries.SingleOrDefault(e => e.Name == area.Name + ".water.extm")
                        ?? throw new InvalidDataException("Missing water member " + area.Name);
                    var bytes = archive.Read(entry).ToArray();
                    _ = ReadSample(bytes, 0, 0, scene.HeightScale);
                    tiles.Add(area.Name, bytes);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            { warnings.Add("Terrain water " + group.Key + ": " + ex.Message); }
        }
        var points = new Point[stride * stride];
        for (int z = 0; z <= grid; z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x <= grid; x++)
            {
                float wx = left + x * step, wz = top + z * step;
                int slot = Math.Min((int)(z * step / 62.5f), 15) * 16 + Math.Min((int)(x * step / 62.5f), 15);
                var area = lookup[slot];
                float ground = groundHeights[z * stride + x];
                if (area == null || !float.IsFinite(ground) || !tiles.TryGetValue(area.Name, out var bytes))
                { points[z * stride + x] = new(new(wx, float.NaN, wz), -1, 0); continue; }
                float u = Math.Clamp(2 + (wx - area.X + area.Size * .5f) * 64 / area.Size, 0, Side - 1);
                float v = Math.Clamp(2 + (wz - area.Z + area.Size * .5f) * 64 / area.Size, 0, Side - 1);
                int ix = Math.Min((int)u, Side - 2), iz = Math.Min((int)v, Side - 2);
                float Height(int xx, int zz) => ReadSample(bytes, xx, zz, scene.HeightScale).Height;
                float height = float.Lerp(float.Lerp(Height(ix, iz), Height(ix + 1, iz), u - ix),
                    float.Lerp(Height(ix, iz + 1), Height(ix + 1, iz + 1), u - ix), v - iz);
                byte material = ReadSample(bytes, (int)MathF.Round(u), (int)MathF.Round(v), scene.HeightScale).Material;
                points[z * stride + x] = new(new(wx, height, wz), height - ground, material);
            }
        }
        var textures = new Dictionary<byte, VisualModelTexture>();
        for (int cz = 0; cz < grid; cz += chunk)
        for (int cx = 0; cx < grid; cx += chunk)
        {
            cancellation.ThrowIfCancellationRequested();
            var batches = new Dictionary<byte, Batch>();
            for (int z = cz; z < cz + chunk; z++)
            for (int x = cx; x < cx + chunk; x++)
            {
                int a = z * stride + x, b = a + stride;
                Triangle(points[a], points[b], points[a + 1]);
                Triangle(points[a + 1], points[b], points[b + 1]);
            }
            void Triangle(Point a, Point b, Point c)
            {
                if (!float.IsFinite(a.Position.Y) || !float.IsFinite(b.Position.Y) || !float.IsFinite(c.Position.Y)) return;
                var wettest = a.Depth > b.Depth ? a : b; if (c.Depth > wettest.Depth) wettest = c;
                if (wettest.Depth <= 0) return;
                if (!batches.TryGetValue(wettest.Material, out var batch)) batches[wettest.Material] = batch = new();
                Span<Point> input = [a, b, c]; Span<Vector3> clipped = stackalloc Vector3[6]; int count = 0;
                // Intersect shoreline triangles with the ground surface; do not
                // join lakes across dry samples or draw a rectangular water sheet.
                for (int i = 0; i < 3; i++)
                {
                    var current = input[i]; var next = input[(i + 1) % 3];
                    if (current.Depth > 0) clipped[count++] = current.Position;
                    if ((current.Depth > 0) != (next.Depth > 0))
                        clipped[count++] = Vector3.Lerp(current.Position, next.Position, current.Depth / (current.Depth - next.Depth));
                }
                for (int i = 1; i + 1 < count; i++) batch.Add(clipped[0], clipped[i], clipped[i + 1]);
            }
            foreach (var (material, batch) in batches)
            {
                if (batch.Indices.Count == 0) continue;
                if (!textures.TryGetValue(material, out var texture)) textures[material] = texture = Surface(material);
                string name = $"water:MainField/{left},{top}/{cx},{cz}/{material}";
                var mesh = new VisualModelMesh { Name = name, Vertices = batch.Vertices.ToArray(), Indices = batch.Indices.ToArray(), Texture = texture };
                model.Meshes.Add(mesh); model.TriangleCount += mesh.Indices.Length / 3;
                model.BoundsMinimum = Vector3.Min(model.BoundsMinimum, batch.Minimum);
                model.BoundsMaximum = Vector3.Max(model.BoundsMaximum, batch.Maximum);
            }
        }
        int startX = Math.Clamp((int)MathF.Round((start.X - left) / step), 0, grid);
        int startZ = Math.Clamp((int)MathF.Round((start.Z - top) / step), 0, grid);
        var startPoint = points[startZ * stride + startX];
        return startPoint.Depth > 0 ? startPoint.Position.Y : null;
    }

    private static VisualModelTexture Surface(byte material)
    {
        byte[] color = material switch
        {
            1 => [85, 146, 149, 190], 2 => [86, 69, 106, 215], 3 => [220, 69, 14, 255],
            4 => [104, 164, 181, 185], 5 => [89, 75, 50, 240], 6 => [71, 140, 151, 145],
            7 => [41, 107, 133, 205], _ => [53, 122, 140, 185]
        };
        return new() { Name = "Terrain water " + material, Width = 1, Height = 1, Pixels = color,
            IsSrgb = true, AlphaMode = VisualModelAlphaMode.Blend };
    }

    private sealed class Batch
    {
        internal List<float> Vertices { get; } = [];
        internal List<uint> Indices { get; } = [];
        internal Vector3 Minimum = new(float.MaxValue), Maximum = new(float.MinValue);
        private readonly Dictionary<Vector3, uint> _vertices = [];
        internal void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-10f) return;
            foreach (var p in new[] { a, b, c })
            {
                if (!_vertices.TryGetValue(p, out uint index))
                {
                    index = (uint)_vertices.Count; _vertices.Add(p, index);
                    Vertices.AddRange([p.X, p.Y, p.Z, 0, 1, 0, 0, 0]);
                    Minimum = Vector3.Min(Minimum, p); Maximum = Vector3.Max(Maximum, p);
                }
                Indices.Add(index);
            }
        }
    }
}
