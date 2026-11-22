using System.Numerics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using HKCLTool;
using PhysicsTool.Core;

internal static class TotkCaveChecks
{
    internal static void Geometry(string root)
    {
        foreach (string name in new[] { "Cave_Hebra_0000", "Cave_Hebra_0039" })
        {
            string folder = Path.Combine(root, "Cave/cave017", name);
            var source = File.ReadAllBytes(Path.Combine(folder, "C.crbin"));
            var cave = new TotkCaveDocument(source);
            var pages = new Dictionary<int, byte[]>();
            var unions = new Dictionary<int, int>();
            int triangles = 0, clippedNodes = 0, outside = 0; float longest = 0;
            var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
            foreach (var node in cave.Nodes.Where(n => n.Lod == cave.Subdivisions))
            {
                if (node.Max.X < -5000 || node.Min.X > -4000 || node.Max.Z < -3000 || node.Min.Z > -2000) clippedNodes++;
                float side = cave.SideLength;
                var origin = cave.Origin - new Vector3(side * .49993896f) + new Vector3(node.X, node.Y, node.Z) * side;
                for (int s = node.Stream; s < node.Stream + node.Streams; s++)
                {
                    var stream = cave.Streams[s];
                    if (!pages.TryGetValue(stream.Page, out var page))
                        pages[stream.Page] = page = TotkMeshCodec.DecodeChunk(File.ReadAllBytes(Path.Combine(folder, $"C.crbin.{cave.Id:x8}/{stream.Page:D6}.chunk")), cave.Id, cave.PageSizes[stream.Page]);
                    for (int t = 0; t < stream.Triangles; t++)
                    {
                        var materials = new HashSet<int>(); var positions = new Vector3[3];
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int index = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(stream.FirstIndex * 2 + t * 6 + corner * 2));
                            var v = TotkCaveScene.Vertex(page, index, origin, side / 4096, node.Material);
                            positions[corner] = v.Position; min = Vector3.Min(min, v.Position); max = Vector3.Max(max, v.Position);
                            if (Vector3.Distance(v.Position, Vector3.Clamp(v.Position, node.Min, node.Max)) > .1f) outside++;
                            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(index * 28));
                            ulong weights = BinaryPrimitives.ReadUInt64LittleEndian(page.AsSpan(index * 28 + 16));
                            int w0 = (int)(weights >> 45 & 31), w1 = (int)(weights >> 50 & 31);
                            if (w0 > 0) materials.Add(node.Material + (int)(packed >> 5 & 511));
                            if (w1 > 0) materials.Add(node.Material + (int)(packed >> 14 & 511));
                            if (w0 + w1 < 31) materials.Add(node.Material + (int)(packed >> 23 & 511));
                        }
                        unions[materials.Count] = unions.GetValueOrDefault(materials.Count) + 1;
                        for (int c = 0; c < 3; c++) longest = Math.Max(longest, Vector3.Distance(positions[c], positions[(c+1)%3]));
                        triangles++;
                    }
                }
            }
            Console.WriteLine($"{name}: {triangles} triangles, bounds {min}..{max}, longest edge {longest}, outside node bounds {outside}, clipped nodes {clippedNodes}; material unions {string.Join(", ", unions.OrderBy(p => p.Key))}");
            Require(outside == 0 && longest < 6, "A-2 cave has corrupted packed vertex positions");
        }
        var warnings = new List<string>();
        var model = TotkCaveScene.Load(root, "Banc/MainField/A-2_Static.bcett.byml.zs", warnings, null, default);
        Require(model.TriangleCount == 105390, "A-2 cave lost geometry at its map-cell boundary");
        Require(model.BoundsMaximum.X > -3941 && model.BoundsMaximum.Z > -1969, "A-2 cave does not extend across the cell boundary");
        Require(!warnings.Any(w => w.StartsWith("Cave Cave_")), string.Join("\n", warnings));
        Console.WriteLine($"PASS: complete A-2 cave shells, {model.TriangleCount} triangles / {model.Meshes.Count} batches.");
    }
    internal static void Run(string root)
    {
        string indexPath = Path.Combine(root, "Cave/cave017/Cave_Lanayru_0045/C.crbin");
        var bytes = File.ReadAllBytes(indexPath);
        var originalHash = SHA256.HashData(bytes);
        var document = new TotkCaveDocument(bytes);
        Require(document.PageSizes.Count == 3 && document.PageSizes[2] == 1048576, "Native page table layout changed");
        void Bad(Action action)
        {
            try { action(); } catch (Exception ex) when (ex is InvalidDataException or OverflowException) { return; }
            throw new Exception("Malformed cave input was accepted");
        }
        foreach (int length in new[] {0, 32, 475, bytes.Length - 1}) Bad(() => new TotkCaveDocument(bytes[..length]));
        foreach (int offset in new[] {0x68,0x6c,0x80,0x88,0x98,0xa0,0xa8,0xc8})
        {
            var broken = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(offset), uint.MaxValue);
            Bad(() => new TotkCaveDocument(broken));
        }
        string pagePath = Path.Combine(Path.GetDirectoryName(indexPath)!, $"C.crbin.{document.Id:x8}/000002.chunk");
        var page = File.ReadAllBytes(pagePath);
        var decoded = TotkMeshCodec.DecodeChunk(page, document.Id, document.PageSizes[2]);
        Bad(() => TotkMeshCodec.DecodeChunk(page, document.Id + 1, document.PageSizes[2]));
        Bad(() => TotkMeshCodec.DecodeChunk(page, document.Id, document.PageSizes[2] + 1));
        foreach (int length in new[] {0, 27, 28, page.Length / 2}) Bad(() => TotkMeshCodec.DecodeChunk(page[..length], document.Id, document.PageSizes[2]));
        Require(decoded.SequenceEqual(TotkMeshCodec.DecodeChunk(page, document.Id, document.PageSizes[2])), "Decoder did not recover after malformed cave data");
        var packed = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(packed, (3u << 5) | (4u << 14) | (5u << 23));
        BinaryPrimitives.WriteUInt64LittleEndian(packed.AsSpan(16), (100ul << 6) | (200ul << 19) | (300ul << 32) | (31ul << 45));
        var vertex = TotkCaveScene.Vertex(packed, 0, Vector3.One, .5f, 10);
        Require(vertex.Position == new Vector3(51,101,151) && vertex.Material == 13, "Packed position or material lane decoding failed");
        Bad(() => TotkCaveScene.Vertex(packed, 1, Vector3.Zero, 1, 0));
        var warnings = new List<string>();
        var model = TotkCaveScene.Load(root, "Banc/MainField/I-4_Static.bcett.byml.zs", warnings, new Progress<string>(Console.WriteLine), default);
        Console.WriteLine(string.Join("\n", warnings));
        var target = model.Meshes.Where(m => m.Name.StartsWith("cave:Cave_Lanayru_0045/", StringComparison.Ordinal)).ToArray();
        Require(target.Length > 0, "I-4 cave shell missing");
        Require(target.All(m => m.Texture != null), "Cave material palette was not loaded");
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        foreach (var mesh in target)
        {
            Require(ShrineSceneAssets.MeshActorIndex(mesh) == null && mesh.Vertices.All(float.IsFinite), "Cave is editable or has nonfinite data");
            for (int i = 0; i < mesh.Vertices.Length; i += 8)
            {
                var v = new Vector3(mesh.Vertices[i], mesh.Vertices[i+1], mesh.Vertices[i+2]); min = Vector3.Min(min,v); max = Vector3.Max(max,v);
                Require(Math.Abs(new Vector3(mesh.Vertices[i+3],mesh.Vertices[i+4],mesh.Vertices[i+5]).Length()-1) < .001f, "Invalid cave normal");
            }
            Require(mesh.Indices.All(i => i < mesh.Vertices.Length / 8), "Cave index out of range");
        }
        var entrance = new Vector3(3416.142f,503.9314f,-870.19f);
        Require(Vector3.Distance(entrance, Vector3.Clamp(entrance,min,max)) < 5, "Cave does not align with its entrance");
        Require(!warnings.Any(w => w.StartsWith("Cave Cave_") || w.StartsWith("Cave Well_")), "Native cave failed: " + string.Join("\n", warnings));
        Require(model.Meshes.Count < 1000, "Cave batching regressed");
        Require(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(indexPath))), "Source cave changed");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { TotkCaveScene.Load(root, "MainField/I-4_Static.bcett.byml.zs", [], null, cancel.Token); throw new Exception("Cave cancellation ignored"); }
        catch (OperationCanceledException) { }
        Console.WriteLine($"PASS I-4 cave geometry: {model.Meshes.Count} batches / {model.TriangleCount} triangles. Target: {target.Length} batches, {min}..{max}.");
    }
    private static void Require(bool ok,string message) { if (!ok) throw new Exception(message); }
}
