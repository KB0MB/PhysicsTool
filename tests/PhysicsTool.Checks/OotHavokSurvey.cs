using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using HKCLTool;
using BymlLibrary;

internal static class OotHavokSurvey
{
    internal static void Run(string root, string output)
    {
        Directory.CreateDirectory(output);
        var baseline = NativeBphclTagFileReader.ReadStandaloneSection(NativeBphclGameReadyReflectionCatalog.TypeSection, "TYPE");
        var oldNames = NativeBphclTagFileReader.ReadTypeNames(baseline);
        var oldDefinitions = NativeBphclTagFileReader.ReadTypeDefinitions(baseline);
        var oldByName = oldDefinitions.Where(d => d.TypeIndex > 0).GroupBy(d => oldNames[(int)d.TypeIndex]).ToDictionary(g => g.Key, g => g.First());
        var rows = new List<object>();
        var differences = new SortedSet<string>();
        var newTypes = new SortedSet<string>();
        var used = new SortedDictionary<string, int>();
        var sdkVersions = new Dictionary<string, int>();
        int parsed = 0, failures = 0, totalCloths = 0;
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root,"Phive/Cloth"), "*.bphcl").Order())
        {
            var bytes = File.ReadAllBytes(path);
            try
            {
                int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)));
                int size = (int)(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)) & 0x3fffffff);
                var tag = NativeBphclTagFileReader.ReadStandaloneSection(bytes.AsSpan(offset,size).ToArray(), "TAG0");
                var sdk = tag.Children.Single(s => s.Signature == "SDKV");
                string version = Encoding.ASCII.GetString(tag.OwnerBytes(), sdk.PayloadOffset, sdk.PayloadSize);
                sdkVersions[version] = sdkVersions.GetValueOrDefault(version) + 1;
                var names = NativeBphclTagFileReader.ReadTypeNames(tag);
                var definitions = NativeBphclTagFileReader.ReadTypeDefinitions(tag);
                var items = NativeBphclTagFileReader.ReadItems(tag);
                foreach (string name in items.Select(i => i.TypeIndex < names.Count ? names[(int)i.TypeIndex] : "INVALID").Distinct())
                    used[name] = used.GetValueOrDefault(name) + 1;
                foreach (var d in definitions.Where(d => d.TypeIndex > 0))
                {
                    string name = names[(int)d.TypeIndex];
                    if (!name.StartsWith("hcl", StringComparison.Ordinal) &&
                        !name.StartsWith("hka", StringComparison.Ordinal) &&
                        name is not ("hkBone" or "hkFloat3" or "hkPreciseTransform" or "hkSkeleton" or
                            "hkSkeletonContainer" or "hkRootLevelContainer" or "hkReferencedObject" or
                            "hkTransform" or "hkQsTransform" or "hkVector4")) continue;
                    if (!oldByName.TryGetValue(name, out var old)) { newTypes.Add(name); continue; }
                    string Layout(NativeBphclTypeDefinition def, IReadOnlyList<string> table) =>
                        $"size={def.Size} align={def.Alignment} parent={table[(int)def.ParentTypeIndex]} " +
                        string.Join("; ", def.Members.Select(m => $"{m.Name}@{m.Offset}:{table[(int)m.TypeIndex]}"));
                    string a = Layout(old, oldNames), b = Layout(d, names);
                    if (a != b) differences.Add($"{name}\n  TotK: {a}\n  OoT:  {b}");
                }
                string? error = null; int cloths = 0, skeletons = 0;
                try
                {
                    var document = NativeBphclDocument.Parse(bytes);
                    cloths = document.Cloths.Count; skeletons = document.Skeletons.Count;
                    parsed++; totalCloths += cloths;
                }
                catch (Exception ex) { failures++; error = ex.GetType().Name + ": " + ex.Message; }
                rows.Add(new { File = Path.GetFileName(path), Version = version, bytes.Length, TypeCount = names.Count,
                    ItemCount = items.Count, Cloths = cloths, Skeletons = skeletons, ReaderError = error });
                if (Path.GetFileName(path).StartsWith("Player_Adult."))
                    File.WriteAllText(Path.Combine(output, "player-adult-reflection.json"), JsonSerializer.Serialize(new { names, definitions }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { failures++; rows.Add(new { File = Path.GetFileName(path), ContainerError = ex.ToString() }); }
        }
        File.WriteAllText(Path.Combine(output,"cloth-survey.json"), JsonSerializer.Serialize(rows,new JsonSerializerOptions { WriteIndented=true }));
        File.WriteAllLines(Path.Combine(output,"schema-differences.txt"),differences);
        File.WriteAllLines(Path.Combine(output,"new-types.txt"),newTypes);
        File.WriteAllLines(Path.Combine(output,"used-types.txt"),used.Select(p => $"{p.Value}: {p.Key}"));
        Console.WriteLine($"SDKV: {JsonSerializer.Serialize(sdkVersions)}; current reader: {parsed} parsed / {failures} failures / {totalCloths} cloths");
        Console.WriteLine($"Changed named hcl/hka/base layouts: {differences.Count}; new types: {newTypes.Count}");
        Console.WriteLine(string.Join("\n",differences));
        Console.WriteLine("NEW: " + string.Join(", ",newTypes));
        var compression = new TotkResourceCompression();
        var packEntries = new List<string>();
        int packCount = 0;
        foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "Pack/Actor"), "*.pack.zs").Order())
        {
            try
            {
                var archive = BotwSarcArchive.Parse(compression.Decode(File.ReadAllBytes(path), [root]));
                packCount++;
                if (Path.GetFileName(path) == "Player_Adult.pack.zs")
                    File.WriteAllText(Path.Combine(output, "player-adult-helper-parameters.txt"),
                        string.Join("\n\n", archive.Entries.Where(e => e.Name.Contains("HelperBone", StringComparison.Ordinal))
                            .Select(e => e.Name + "\n" + Byml.FromBinary(archive.Read(e).ToArray()).ToYaml())));
                foreach (var entry in archive.Entries)
                    if (entry.Name.Contains("Phive", StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.Contains("Helper", StringComparison.OrdinalIgnoreCase) ||
                        entry.Name.Contains("SupportBone", StringComparison.OrdinalIgnoreCase))
                        packEntries.Add(Path.GetFileName(path) + ": " + entry.Name);
            }
            catch (Exception ex) { packEntries.Add(Path.GetFileName(path) + ": ERROR " + ex.Message); }
        }
        File.WriteAllLines(Path.Combine(output, "actor-physics-entries.txt"), packEntries);
        Console.WriteLine($"Actor packs inspected: {packCount}; physics-related entries: {packEntries.Count}");
    }
}
