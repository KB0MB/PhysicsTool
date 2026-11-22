using System.Numerics;
using HKCLTool;
using ZstdSharp;
using static PhysicsTool.Core.TotkTerrainFormat;

namespace PhysicsTool.Core;

internal sealed record TotkTerrainResult(VisualModelData Model, IReadOnlyList<string> Warnings, Vector3 StartPosition);

internal static class TotkTerrain
{
    internal const int GridSize = 500, ChunkSize = 50;
    internal const int TextureSize = 512;
    internal const float Spacing = 2;

    internal static TotkTerrainResult? Load(string root, string scenePath, IProgress<string>? progress = null,
        CancellationToken cancellation = default)
    {
        if (!TryCell(scenePath, out float left, out float top)) return null;
        cancellation.ThrowIfCancellationRequested();
        if (Directory.Exists(Path.Combine(root, "romfs", "TerrainArc"))) root = Path.Combine(root, "romfs");
        string indexPath = Path.Combine(root, "TerrainArc", "MainField.tscb");
        progress?.Report("Reading MainField terrain index...");
        var scene = Parse(ReadBounded(indexPath, 32 * 1024 * 1024));
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var lookup = new Area?[16 * 16];
        // Select one finest available source per 62.5 m footprint. Parent data is
        // sampled only within that footprint, so sparse LODs never overlap meshes.
        for (int z = 0; z < 16; z++)
            for (int x = 0; x < 16; x++)
            {
                float wx = left + (x + .5f) * 62.5f, wz = top + (z + .5f) * 62.5f;
                lookup[z * 16 + x] = scene.Areas.Where(a => a.Size >= 62.5f &&
                    Math.Abs(a.X - wx) < a.Size * .5f && Math.Abs(a.Z - wz) < a.Size * .5f)
                    .MinBy(a => a.Size);
            }
        var tiles = new Dictionary<string, Tile>();
        foreach (var group in lookup.OfType<Area>().DistinctBy(a => a.Name).GroupBy(a => ArchiveName(a.Name)))
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report($"Loading terrain {group.Key}...");
            try
            {
                var height = OpenArchive(Path.Combine(root, "TerrainArc", "MainField", group.Key + ".hght.ta.zs"));
                var material = OpenArchive(Path.Combine(root, "TerrainArc", "MainField", group.Key + ".mate.ta.zs"));
                foreach (var area in group)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var h = height.Entries.SingleOrDefault(e => e.Name == area.Name + ".hght")
                        ?? throw new InvalidDataException("Missing height member " + area.Name);
                    var m = material.Entries.SingleOrDefault(e => e.Name == area.Name + ".mate")
                        ?? throw new InvalidDataException("Missing material member " + area.Name);
                    tiles.Add(area.Name, new(area, DecodeHeight(height.Read(h).Span), DecodeMaterial(material.Read(m).Span)));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            { warnings.Add($"Terrain archive {group.Key}: {ex.Message}"); }
        }
        Tile? At(float x, float z)
        {
            int ix = Math.Clamp((int)MathF.Floor((x - left) / 62.5f), 0, 15);
            int iz = Math.Clamp((int)MathF.Floor((z - top) / 62.5f), 0, 15);
            return lookup[iz * 16 + ix] is { } a ? tiles.GetValueOrDefault(a.Name) : null;
        }
        int side = GridSize + 1;
        var heights = new float[side * side];
        for (int z = 0; z < side; z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < side; x++)
            {
                float wx = left + x * Spacing, wz = top + z * Spacing;
                heights[z * side + x] = At(wx, wz)?.Height(wx, wz, scene.HeightScale) ?? float.NaN;
            }
        }
        var visible = new bool[GridSize * GridSize];
        for (int z = 0; z < GridSize; z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < GridSize; x++)
            {
                int i = z * side + x;
                if (!float.IsFinite(heights[i]) || !float.IsFinite(heights[i + 1]) ||
                    !float.IsFinite(heights[i + side]) || !float.IsFinite(heights[i + side + 1])) continue;
                float x0 = left + x * Spacing, z0 = top + z * Spacing;
                bool hole = false;
                // Check every native material texel touched by the preview quad,
                // including quads crossing source tile boundaries. Thin holes survive.
                int sx0 = Math.Clamp((int)((x0 - left) / 62.5f), 0, 15);
                int sz0 = Math.Clamp((int)((z0 - top) / 62.5f), 0, 15);
                int sx1 = Math.Clamp((int)((x0 + Spacing - left) / 62.5f), 0, 15);
                int sz1 = Math.Clamp((int)((z0 + Spacing - top) / 62.5f), 0, 15);
                for (int sz = sz0; sz <= sz1; sz++)
                    for (int sx = sx0; sx <= sx1; sx++)
                    {
                        var tile = lookup[sz * 16 + sx] is { } a ? tiles.GetValueOrDefault(a.Name) : null;
                        hole |= tile == null || tile.HasHole(Math.Max(x0, left + sx * 62.5f),
                            Math.Max(z0, top + sz * 62.5f), Math.Min(x0 + Spacing, left + (sx + 1) * 62.5f),
                            Math.Min(z0 + Spacing, top + (sz + 1) * 62.5f));
                    }
                visible[z * GridSize + x] = !hole;
            }
        }
        var normals = new Vector3[heights.Length];
        for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                float Sample(int xx, int zz) => float.IsFinite(heights[zz * side + xx]) ? heights[zz * side + xx] : heights[z * side + x];
                int xa = Math.Max(x - 1, 0), xb = Math.Min(x + 1, GridSize);
                int za = Math.Max(z - 1, 0), zb = Math.Min(z + 1, GridSize);
                var n = new Vector3((Sample(xa, z) - Sample(xb, z)) / ((xb - xa) * Spacing), 1,
                    (Sample(x, za) - Sample(x, zb)) / ((zb - za) * Spacing));
                normals[z * side + x] = float.IsFinite(n.X) && float.IsFinite(n.Z) ? Vector3.Normalize(n) : Vector3.UnitY;
            }
        var model = new VisualModelData { SourcePath = indexPath, BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
        using var palette = new Palette(root, scene.Materials, warnings, cancellation);
        Vector3 start = new(left + 500, 3, top + 500); float closest = float.MaxValue;
        for (int cz = 0; cz < GridSize; cz += ChunkSize)
            for (int cx = 0; cx < GridSize; cx += ChunkSize)
            {
                cancellation.ThrowIfCancellationRequested();
                var indices = new List<uint>(ChunkSize * ChunkSize * 6);
                for (int z = 0; z < ChunkSize; z++)
                    for (int x = 0; x < ChunkSize; x++)
                    {
                        if (!visible[(cz + z) * GridSize + cx + x]) continue;
                        uint a = (uint)(z * (ChunkSize + 1) + x), b = a + ChunkSize + 1;
                        indices.Add(a); indices.Add(b); indices.Add(a + 1);
                        indices.Add(a + 1); indices.Add(b); indices.Add(b + 1);
                    }
                if (indices.Count == 0) continue;
                var vertices = new float[(ChunkSize + 1) * (ChunkSize + 1) * 8];
                for (int z = 0; z <= ChunkSize; z++)
                    for (int x = 0; x <= ChunkSize; x++)
                    {
                        int source = (cz + z) * side + cx + x, target = (z * (ChunkSize + 1) + x) * 8;
                        vertices[target] = left + (cx + x) * Spacing;
                        vertices[target + 1] = float.IsFinite(heights[source]) ? heights[source] : 0;
                        vertices[target + 2] = top + (cz + z) * Spacing;
                        vertices[target + 3] = normals[source].X; vertices[target + 4] = normals[source].Y; vertices[target + 5] = normals[source].Z;
                        // Half-texel inset avoids repeat-wrap bleeding at chunk boundaries.
                        vertices[target + 6] = (.5f + x * (TextureSize - 1f) / ChunkSize) / TextureSize;
                        vertices[target + 7] = (.5f + z * (TextureSize - 1f) / ChunkSize) / TextureSize;
                    }
                foreach (uint index in indices.Distinct())
                {
                    int i = (int)index * 8; var p = new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]);
                    model.BoundsMinimum = Vector3.Min(model.BoundsMinimum, p); model.BoundsMaximum = Vector3.Max(model.BoundsMaximum, p);
                    float distance = Vector2.DistanceSquared(new(p.X, p.Z), new(left + 500, top + 500));
                    if (distance < closest) { closest = distance; start = p + Vector3.UnitY * 3; }
                }
                progress?.Report($"Baking terrain chunk {cx / ChunkSize + 1},{cz / ChunkSize + 1}...");
                var pixels = new byte[TextureSize * TextureSize * 4];
                for (int z = 0; z < TextureSize; z++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    for (int x = 0; x < TextureSize; x++)
                    {
                        float wx = left + cx * Spacing + x * (ChunkSize * Spacing) / (TextureSize - 1), wz = top + cz * Spacing + z * (ChunkSize * Spacing) / (TextureSize - 1);
                        var tile = At(wx, wz);
                        var color = tile == null ? new Vector3(.5f) : palette.Color(tile.Material(wx, wz), wx, wz);
                        int p = (z * TextureSize + x) * 4;
                        pixels[p] = (byte)Math.Clamp(color.X * 255, 0, 255); pixels[p + 1] = (byte)Math.Clamp(color.Y * 255, 0, 255);
                        pixels[p + 2] = (byte)Math.Clamp(color.Z * 255, 0, 255); pixels[p + 3] = 255;
                    }
                }
                string name = $"terrain:MainField/{Path.GetFileName(scenePath).Split('_')[0]}/{cx / ChunkSize},{cz / ChunkSize}";
                model.Meshes.Add(new() { Name = name, Vertices = vertices, Indices = indices.ToArray(), BaseColor = Vector4.One,
                    CullingMode = VisualModelCullingMode.CounterClockwise,
                    Texture = new() { Name = name, SourcePath = indexPath, Width = TextureSize, Height = TextureSize, Pixels = pixels,
                        IsSrgb = true, AlphaMode = VisualModelAlphaMode.Opaque } });
                model.TriangleCount += indices.Count / 3;
            }
        if (heights.Any(h => !float.IsFinite(h))) warnings.Add("Terrain source coverage is incomplete; unavailable regions were omitted.");
        if (model.Meshes.Count == 0) { model.BoundsMinimum = model.BoundsMaximum = Vector3.Zero; warnings.Add("No terrain geometry was available for this cell."); }
        if (TotkTerrainWater.Add(model, root, scene, left, top, heights, start, warnings, progress, cancellation) is { } waterHeight)
            start.Y = Math.Max(start.Y, waterHeight + 3);
        warnings.Add("Terrain preview uses 2 m geometry, conservative hole edges and approximate surface materials. Water surfaces do not reproduce game waves or refraction.");
        model.ExternalTextureCount = model.Meshes.Count;
        return new(model, warnings.ToArray(), start);
    }

    internal static bool TryCell(string path, out float left, out float top)
    {
        left = top = 0;
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Length < 2 || !parts[^2].Equals("MainField", StringComparison.OrdinalIgnoreCase)) return false;
        string name = parts[^1].Split('_', '.')[0];
        if (name.Length != 3 || name[0] is < 'A' or > 'J' || name[1] != '-' || name[2] is < '1' or > '8') return false;
        left = (name[0] - 'A' - 5) * 1000; top = (name[2] - '1' - 4) * 1000;
        return true;
    }

    private static byte[] ReadBounded(string path, int limit)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > limit) throw new InvalidDataException("Terrain resource exceeds the preview size limit.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
    internal static BotwSarcArchive OpenArchive(string path)
    {
        var compressed = ReadBounded(path, 8 * 1024 * 1024);
        using var decoder = new Decompressor();
        // Terrain archives are dictionary-free Zstd/SARC; cap decompression explicitly.
        var bytes = decoder.Unwrap(compressed, 8 * 1024 * 1024).ToArray();
        return BotwSarcArchive.Parse(bytes);
    }

    private sealed class Tile
    {
        private readonly Area area;
        private readonly ushort[] heights;
        private readonly byte[] material;
        private readonly int[] holes = new int[(Width + 1) * (Width + 1)];
        internal Tile(Area area, ushort[] heights, byte[] material)
        {
            this.area = area; this.heights = heights; this.material = material;
            for (int z = 0; z < Width; z++)
                for (int x = 0; x < Width; x++)
                {
                    int i = (z * Width + x) * 4, p = (z + 1) * (Width + 1) + x + 1;
                    holes[p] = (Hole(material[i], material[i + 1]) ? 1 : 0) + holes[p - 1] + holes[p - Width - 1] - holes[p - Width - 2];
                }
        }
        private float X(float x) => Math.Clamp(Margin + (x - area.X + area.Size * .5f) * Interior / area.Size, 0, Width - 1);
        private float Z(float z) => Math.Clamp(Margin + (z - area.Z + area.Size * .5f) * Interior / area.Size, 0, Width - 1);
        internal float Height(float x, float z, float scale)
        {
            float u = X(x), v = Z(z); int ix = Math.Min((int)u, Width - 2), iz = Math.Min((int)v, Width - 2);
            float a = float.Lerp(heights[iz * Width + ix], heights[iz * Width + ix + 1], u - ix);
            float b = float.Lerp(heights[(iz + 1) * Width + ix], heights[(iz + 1) * Width + ix + 1], u - ix);
            return float.Lerp(a, b, v - iz) * scale;
        }
        internal (byte A, byte B, byte Blend) Material(float x, float z)
        {
            int i = ((int)Z(z) * Width + (int)X(x)) * 4;
            return ((byte)(material[i] & 127), (byte)(material[i + 1] & 127), material[i + 2]);
        }
        internal bool HasHole(float x0, float z0, float x1, float z1)
        {
            int xa = (int)X(x0), za = (int)Z(z0), xb = Math.Min(Width, (int)MathF.Ceiling(X(x1)) + 1), zb = Math.Min(Width, (int)MathF.Ceiling(Z(z1)) + 1);
            int stride = Width + 1;
            return holes[zb * stride + xb] - holes[za * stride + xb] - holes[zb * stride + xa] + holes[za * stride + xa] > 0;
        }
    }

    private sealed class Palette : IDisposable
    {
        private readonly Material[] materials;
        private readonly HashSet<string> warnings;
        private readonly CancellationToken cancellation;
        private readonly Dictionary<int, VisualModelTexture?> textures = new();
        private byte[]? file;
        internal Palette(string root, Material[] materials, HashSet<string> warnings, CancellationToken cancellation)
        {
            this.materials = materials; this.warnings = warnings; this.cancellation = cancellation;
            try { file = ReadBounded(Path.Combine(root, "TexToGo", "MaterialAlb.txtg"), 128 * 1024 * 1024); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add("Terrain MaterialAlb: " + ex.Message); }
        }
        internal Vector3 Color((byte A, byte B, byte Blend) sample, float x, float z) =>
            Vector3.Lerp(Sample(sample.A, x, z), Sample(sample.B, x, z), sample.Blend / 255f);
        private Vector3 Sample(int id, float x, float z)
        {
            if (id >= materials.Length) { warnings.Add($"Terrain material ID {id} has no TSCB descriptor."); return new(.5f); }
            var material = materials[id];
            if (file == null || material.Layer == 120) return new(.5f);
            if (!textures.TryGetValue(material.Layer, out var texture))
            {
                cancellation.ThrowIfCancellationRequested();
                try { texture = TexToGoTexture.Decode(file, "MaterialAlb", material.Layer); }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                { warnings.Add($"Terrain MaterialAlb layer {material.Layer}: {ex.Message}"); }
                textures[material.Layer] = texture;
            }
            if (texture == null) return new(.5f);
            float u = x * material.U, v = z * material.V;
            float px = (u - MathF.Floor(u)) * texture.Width - .5f, pz = (v - MathF.Floor(v)) * texture.Height - .5f;
            int x0 = (int)MathF.Floor(px), z0 = (int)MathF.Floor(pz);
            Vector3 Pixel(int xx, int zz)
            {
                int p = (((zz + texture.Height) % texture.Height) * texture.Width + (xx + texture.Width) % texture.Width) * 4;
                return new(texture.Pixels[p], texture.Pixels[p + 1], texture.Pixels[p + 2]);
            }
            return Vector3.Lerp(Vector3.Lerp(Pixel(x0, z0), Pixel(x0 + 1, z0), px - x0),
                Vector3.Lerp(Pixel(x0, z0 + 1), Pixel(x0 + 1, z0 + 1), px - x0), pz - z0) / 255;
        }
        public void Dispose() { file = null; textures.Clear(); }
    }
}
