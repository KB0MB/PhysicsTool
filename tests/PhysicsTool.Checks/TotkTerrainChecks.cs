using System.Buffers.Binary;
using System.Numerics;
using PhysicsTool.Core;

internal static class TotkTerrainChecks
{
    internal static void Run(string? root = null)
    {
        Synthetic();
        if (root != null) Native(root);
        if (root != null) Water(root);
    }

    internal static void Synthetic()
    {
        var water = new byte[68 * 68 * 8];
        BinaryPrimitives.WriteUInt16LittleEndian(water.AsSpan((2 * 68 + 2) * 8), 8675);
        water[(2 * 68 + 2) * 8 + 7] = 7;
        var sample = TotkTerrainWater.ReadSample(water, 2, 2, 800f / 65535);
        Require(Math.Abs(sample.Height - 105.89761f) < .002f && sample.Material == 7, "Water RGBA16 height/material decoding");
        Bad(() => TotkTerrainWater.ReadSample(water[..^1], 2, 2, 1));
        Require(TotkTerrain.TryCell("Banc/MainField/B-7_Static.bcett.byml.zs", out var x, out var z) && x == -4000 && z == 2000, "B-7 mapping");
        foreach (string path in new[] { "MainField/A-0_Static.bcett.byml.zs", "MinusField/B-7_Static.bcett.byml.zs", "MainField/Merged/B-7.bcett.byml.zs", "MainField/K-1", "SmallDungeon/Dungeon004_Static.bcett.byml.zs" })
            Require(TotkTerrain.Load("nonexistent", path) == null, "Non-MainField path must not read terrain: " + path);
        Require(TotkTerrainFormat.ArchiveName("5700002603") == "700002600", "Archive sibling grouping");
        int n = TotkTerrainFormat.Samples, width = TotkTerrainFormat.Width;
        var height = new byte[n * 2]; var expected = new ushort[n];
        for (int row = 0; row < width; row++)
        {
            int previous = 0;
            for (int col = 0; col < width; col++)
            {
                int i = row * width + col;
                int value = (row % 2 == 0 ? 1000 + row * 7 + col * 3 : 65530 + col * 5) & 65535;
                expected[i] = (ushort)value;
                int delta = unchecked((short)(value - previous)); previous = value;
                int low = ((delta + 8) & 15) - 8;
                int middle = (((delta - low) / 16 + 7) & 15) - 7;
                int high = (delta - low - middle * 16) / 256;
                int shift = col % 2 == 0 ? 4 : 0;
                height[i / 2] |= (byte)((low + 8) << shift);
                height[n / 2 + i / 2] |= (byte)((middle + 7) << shift);
                height[n + i] = unchecked((byte)high);
            }
        }
        Require(TotkTerrainFormat.DecodeHeight(height).SequenceEqual(expected), "Height signed delta, row reset and wrapping");
        var encoded = new byte[n * 4]; var decoded = new byte[n * 4];
        for (int c = 0; c < 4; c++)
            for (int row = 0; row < width; row++)
            {
                int previous = 0;
                for (int col = 0; col < width; col++)
                {
                    int i = row * width + col, value = (row * 17 + col * 41 + c * 13) & 255;
                    decoded[i * 4 + c] = (byte)value; encoded[c * n + i] = unchecked((byte)(value - previous)); previous = value;
                }
            }
        Require(TotkTerrainFormat.DecodeMaterial(encoded).SequenceEqual(decoded), "Material planar delta and fourth channel");
        Require(TotkTerrainFormat.Hole(134, 59) && TotkTerrainFormat.Hole(6, 187) && TotkTerrainFormat.Hole(125, 0) && !TotkTerrainFormat.Hole(6, 59), "Material hole flags");
        Bad(() => TotkTerrainFormat.DecodeHeight(new byte[n * 2 - 1]));
        Bad(() => TotkTerrainFormat.DecodeMaterial(new byte[n * 4 - 1]));
        var fixture = IndexFixture();
        var scene = TotkTerrainFormat.Parse(fixture);
        Require(scene.Areas.Single() is { Name: "5700002600", X: -3937.5f, Z: 2062.5f, Size: 125 }, "Relative area/resource/name pointers");
        Require(Math.Abs(scene.HeightScale - 800f / 65535) < 1e-9f && scene.Materials[0].Layer == 6, "Header scales/material descriptor");
        foreach (int offset in new[] { 0x70, 0x74, 0x78, 0x80, 0xa0, 0x2a0, 0x2a4 })
        {
            var broken = (byte[])fixture.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(offset), uint.MaxValue);
            Bad(() => TotkTerrainFormat.Parse(broken));
        }
        foreach (int length in new[] { 0, 10, 123, 200, 700 }) Bad(() => TotkTerrainFormat.Parse(fixture[..length]));
        var invalid = (byte[])fixture.Clone(); BinaryPrimitives.WriteSingleLittleEndian(invalid.AsSpan(16), float.NaN); Bad(() => TotkTerrainFormat.Parse(invalid));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { TotkTerrain.Load("nonexistent", "MainField/B-7_Static.bcett.byml.zs", cancellation: cancellation.Token); throw new Exception("Terrain cancellation ignored"); }
        catch (OperationCanceledException) { }
        Console.WriteLine("Terrain synthetic checks passed.");
    }

    internal static void Native(string root)
    {
        var terrain = TotkTerrain.Load(root, "Banc/MainField/B-7_Static.bcett.byml.zs") ?? throw new Exception("B-7 rejected");
        var model = terrain.Model;
        var ground = model.Meshes.Where(m => m.Name.StartsWith("terrain:")).ToArray();
        Require(ground.Length == 100, "B-7 chunk coverage");
        Require(ground.Sum(m => m.Indices.Length / 3) is > 400000 and < 500000, "Triangle budget and native hole removal");
        Require(model.BoundsMinimum.X == -4000 && model.BoundsMaximum.X == -3000 && model.BoundsMinimum.Z == 2000 && model.BoundsMaximum.Z == 3000, "B-7 bounded world coordinates");
        Require(model.BoundsMinimum.Y is > 70 and < 150 && model.BoundsMaximum.Y is > 300 and < 400, "Native nonflat height bounds");
        Require(!terrain.Warnings.Any(w => w.StartsWith("Terrain archive") || w.Contains("incomplete") || w.StartsWith("Terrain MaterialAlb") || w.StartsWith("Terrain material ID")), string.Join("\n", terrain.Warnings));
        var edges = new Dictionary<(float X, float Z), (float Y, Vector3 N)>();
        int shared = 0; bool referenceFound = false, colored = false;
        foreach (var mesh in ground)
        {
            Require(mesh.Name.StartsWith("terrain:") && mesh.Vertices.Length == 51 * 51 * 8, "Stable chunk shape and nonactor names");
            Require(mesh.Texture is { Width: 512, Height: 512 } && mesh.Texture.Pixels.Length == 512 * 512 * 4, "High resolution baked texture");
            for (int p = 0; p < mesh.Texture!.Pixels.Length; p += 4)
                colored |= mesh.Texture.Pixels[p] != mesh.Texture.Pixels[p + 1] || mesh.Texture.Pixels[p + 1] != mesh.Texture.Pixels[p + 2];
            for (int i = 0; i < mesh.Vertices.Length; i += 8)
            {
                var v = mesh.Vertices; var normal = new Vector3(v[i + 3], v[i + 4], v[i + 5]);
                Require(float.IsFinite(v[i + 1]) && Math.Abs(normal.Length() - 1) < 1e-5 && normal.Y > 0, "Finite heights and unit upward normals");
                if (v[i] == -4000 && v[i + 2] == 2000)
                { Require(Math.Abs(v[i + 1] - 13130 * (800f / 65535)) < .001f, "Known native B-7 sample (5700002600, pixel 2,2)"); referenceFound = true; }
                int vertex = i / 8;
                if (vertex % 51 is not (0 or 50) && vertex / 51 is not (0 or 50)) continue;
                var key = (v[i], v[i + 2]);
                if (edges.TryGetValue(key, out var prior))
                { Require(prior.Y == v[i + 1] && prior.N == normal, "Crack-free shared chunk positions/normals"); shared++; }
                else edges[key] = (v[i + 1], normal);
            }
            foreach (uint index in mesh.Indices) Require(index < mesh.Vertices.Length / 8, "Index range");
        }
        Require(shared > 9000 && referenceFound && colored, "Native seam/height/material evidence missing");
        Require(terrain.StartPosition.X is >= -4000 and <= -3000 && terrain.StartPosition.Y > model.BoundsMinimum.Y, "Terrain start position");
        Console.WriteLine($"B-7 terrain: {model.Meshes.Count} chunks, {model.TriangleCount:N0} triangles, Y={model.BoundsMinimum.Y:F3}..{model.BoundsMaximum.Y:F3}, {shared} shared border vertices; native checks passed.");
    }

    private static void Water(string root)
    {
        var terrain = TotkTerrain.Load(root, "Banc/MainField/F-4_Static.bcett.byml.zs")!;
        var water = terrain.Model.Meshes.Where(m => m.Name.StartsWith("water:")).ToArray();
        Require(water.Length > 0, "Native F-4 water missing");
        Require(!terrain.Warnings.Any(w => w.StartsWith("Terrain water")), string.Join("\n", terrain.Warnings));
        foreach (var mesh in water)
        {
            Require(mesh.Texture?.AlphaMode == HKCLTool.VisualModelAlphaMode.Blend && ShrineSceneAssets.MeshActorIndex(mesh) == null, "Water is not a translucent nonactor mesh");
            Require(mesh.Vertices.All(float.IsFinite), "Nonfinite water vertex");
            for (int i = 0; i < mesh.Vertices.Length; i += 8)
                Require(mesh.Vertices[i] is >= 0 and <= 1000 && mesh.Vertices[i + 2] is >= -1000 and <= 0 && mesh.Vertices[i + 4] == 1, "Water escaped F-4 bounds or has invalid normals");
            foreach (uint index in mesh.Indices) Require(index < mesh.Vertices.Length / 8, "Water index out of bounds");
        }
        var waterModel = new HKCLTool.VisualModelData(); waterModel.Meshes.AddRange(water);
        var picking = new ShrineModelPicking(waterModel);
        Require(picking.Pick(new(500, 1000, -500), -Vector3.UnitY, new HashSet<int>()) == null, "Water prevents picking actors underneath");
        Console.WriteLine($"F-4 water: {water.Length} chunks, {water.Sum(m => m.Indices.Length / 3):N0} shoreline-clipped triangles; native checks passed.");
    }

    private static byte[] IndexFixture()
    {
        var b = new byte[1024]; "TSCB"u8.CopyTo(b); b[4] = 12; b[5] = 7;
        void U(int p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p), v);
        void F(int p, float v) => BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(p), v);
        void P(int p, int target) => U(p, (uint)(target - p));
        U(8, 1); F(16, 500); F(20, 800); U(48, 2); U(52, 1); U(56, 1);
        P(0x70, 0x80); P(0x74, 0x90); P(0x78, 0xa0); P(0x80, 0x100); P(0x84, 0x180); P(0x90, 0x200); P(0xa0, 0x240);
        foreach (int p in new[] { 0x100, 0x180 })
        { U(p, p == 0x100 ? 0u : 0x2000); BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p + 16), 256); b[p + 20] = 2; b[p + 28] = (byte)(p == 0x100 ? 5 : 29); U(p + 32, 1); }
        U(0x200, 6); F(0x210, .1f); F(0x214, .1f);
        F(0x240, -7.875f); F(0x244, 4.125f); F(0x248, .25f); U(0x260, 2); P(0x2a0, 0x300); P(0x2a4, 0x340);
        "5700002600"u8.CopyTo(b.AsSpan(0x300)); P(0x340, 0x380); P(0x344, 0x3c0); U(0x3c0, 0x2000);
        return b;
    }
    private static void Bad(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Malformed terrain resource was accepted.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
