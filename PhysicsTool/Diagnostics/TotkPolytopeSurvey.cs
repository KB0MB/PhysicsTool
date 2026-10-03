using System.Buffers.Binary;
using System.Text.Json;
using BymlLibrary;
using ZstdSharp;

namespace HKCLTool;

internal static class TotkPolytopeSurvey
{
    public static void Run(string romfs, string output)
    {
        Directory.CreateDirectory(output);
        var dictionaries = new Dictionary<uint, byte[]>();
        using var decoder = new Decompressor();
        var dictionaryPack = BotwSarcArchive.Parse(decoder.Unwrap(File.ReadAllBytes(Path.Combine(romfs, "Pack", "ZsDic.pack.zs"))).ToArray());
        foreach (var entry in dictionaryPack.Entries)
        {
            var bytes = dictionaryPack.Read(entry).ToArray();
            dictionaries[BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))] = bytes;
        }
        var seen = new HashSet<string>();
        var counts = new SortedDictionary<string, int>();
        var errors = new List<string>();
        var rejectedHulls = new List<string>();
        var materials = new SortedSet<string>(StringComparer.Ordinal);
        var sampleCount = 0;
        var packs = 0;
        void Count(string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
        void Inspect(string name, byte[] bytes)
        {
            if (!seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)))) return;
            try
            {
                var root = Byml.FromBinary(bytes, out var endian, out var version).GetMap();
                Count("ShapeParam"); Count($"Version:{version}/{endian}");
                foreach (var key in root.Keys) Count("Root:" + key);
                if (!root.TryGetValue("Polytope", out var polytopes)) return;
                Count("PolytopeFiles");
                foreach (var poly in polytopes.GetArray())
                {
                    var map = poly.GetMap();
                    if (map.TryGetValue("ConvexRadius", out var radius)) Count("ConvexRadius:" + radius.GetFloat().ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (map.TryGetValue("ShrinkByRadius", out var shrink)) Count("ShrinkByRadius:" + shrink.GetBool());
                    if (map.TryGetValue("AutoCalc", out var calc))
                    {
                        var c = calc.GetMap();
                        double[] Vec(string key) => new[] { "X", "Y", "Z" }.Select(k => (double)c[key].GetMap()[k].GetFloat()).ToArray();
                        if (c.ContainsKey("Axis") && Vec("Axis").Any(v => Math.Abs(v) > 1e-6)) Count("NonzeroPolytopeAxis");
                        if (c.ContainsKey("Min") && c.ContainsKey("Max") && c.ContainsKey("Volume") && c.ContainsKey("Tensor"))
                        {
                            var min = Vec("Min"); var max = Vec("Max"); var t = Vec("Tensor");
                            var d = max.Zip(min, (a,b) => a-b).ToArray();
                            var v = c["Volume"].GetFloat();
                            if (Math.Abs(d[0]*d[1]*d[2]-v) < Math.Max(0.00001, Math.Abs(v)*0.001)) Count("PolytopeVolumeMatchesBox");
                            else Count("PolytopeVolumeNotBox");
                            if (Enumerable.Range(0,3).All(i => Math.Abs(t[i]-(d[(i+1)%3]*d[(i+1)%3]+d[(i+2)%3]*d[(i+2)%3])/12) < Math.Max(0.00001,Math.Abs(t[i])*0.001))) Count("PolytopeTensorMatchesBox");
                            else Count("PolytopeTensorNotBox");
                        }
                    }
                    foreach (var key in map.Keys) Count("Polytope:" + key);
                    try
                    {
                        TotkPolytopeImporter.Read(TotkPolytopeBuilder.Map(("Polytope",new Byml(new List<Byml> { poly }))));
                        Count("HullValidationPassed");
                    }
                    catch (InvalidDataException ex) { Count("HullValidationRejected"); rejectedHulls.Add(name + ": " + ex.Message); }
                    if (map.TryGetValue("Vertices", out var vertices)) Count($"Vertices:{vertices.GetArray().Count}");
                    if (map.TryGetValue("MaterialPresets", out var presets))
                        foreach (var preset in presets.GetArray()) materials.Add(preset.GetString());
                }
                if (sampleCount < 16)
                    File.WriteAllText(Path.Combine(output, $"sample-{sampleCount++:00}.yaml"), "# " + name + "\n" + new Byml(root).ToYaml());
                if (!root.ContainsKey("$parent") && !root.Any(p => p.Key != "Polytope" && p.Value.Type == BymlNodeType.Array && p.Value.GetArray().Count > 0) && polytopes.GetArray().Count > 0)
                {
                    try
                    {
                        var meshes = TotkPolytopeImporter.Read(new Byml(root));
                        var generated = TotkPolytopeBuilder.Create(meshes,1,System.Numerics.Vector3.Zero,false);
                        var bytes2 = generated.Root.ToBinary(Revrs.Endianness.Little,7);
                        if (TotkPolytopeImporter.Read(Byml.FromBinary(bytes2)).Count != meshes.Count) throw new InvalidDataException("Shape count changed.");
                        Count("PurePolytopeRegenerationPassed");
                    }
                    catch (InvalidDataException) { Count("PurePolytopeRegenerationRejected"); }
                }
            }
            catch (Exception ex) { errors.Add(name + ": " + ex.Message); }
        }
        foreach (var file in Directory.EnumerateFiles(Path.Combine(romfs, "Phive", "ShapeParam"), "*.bgyml", SearchOption.AllDirectories))
            Inspect(Path.GetFileName(file), File.ReadAllBytes(file));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(romfs, "Pack", "Actor"), "*.pack.zs"))
        {
            try
            {
                var bytes = File.ReadAllBytes(file);
                var offset = (bytes[4] & 0x20) != 0 ? 5 : 6;
                var id = (bytes[4] & 3) switch { 0 => 0u, 1 => bytes[offset], 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)), _ => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) };
                decoder.LoadDictionary(id == 0 ? Array.Empty<byte>() : dictionaries[id]);
                var archive = BotwSarcArchive.Parse(decoder.Unwrap(bytes).ToArray());
                foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".phive__ShapeParam.bgyml", StringComparison.Ordinal)))
                    Inspect(Path.GetFileName(file) + "/" + entry.Name, archive.Read(entry).ToArray());
                packs++;
            }
            catch (Exception ex) { errors.Add(Path.GetFileName(file) + ": " + ex.Message); }
        }
        File.WriteAllText(Path.Combine(output, "survey.json"), JsonSerializer.Serialize(new { Packs=packs, Counts=counts, Materials=materials, RejectedHulls=rejectedHulls, Errors=errors }, new JsonSerializerOptions { WriteIndented=true }));
        Console.WriteLine($"Surveyed {packs} packs; {counts.GetValueOrDefault("PolytopeFiles")} unique polytope files; {errors.Count} errors.");
    }
}
