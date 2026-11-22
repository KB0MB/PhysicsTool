using HKCLTool;
using PhysicsTool.Core;

internal static class OotCrossGameChecks
{
    internal static void SurveyAerodynamics(string root)
    {
        var patterns = new Dictionary<string, int>();
        var densities = new List<float>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var doc = NativeBphclDocument.Open(file);
            foreach (var sim in doc.Cloths.SelectMany(c => c.SimCloths))
            {
                var member = doc.TypeDefinitions.Single(t => t.TypeIndex == doc.GetItem(sim.ItemIndex).TypeIndex).Members.Single(m => m.Name == "aerodynamicsData");
                var aero = doc.TypeDefinitions.Single(t => t.TypeIndex == member.TypeIndex);
                var values = aero.Members.Select(m =>
                {
                    var offset = doc.DataSection!.PayloadOffset + (int)(sim.DataOffset + member.Offset + m.Offset);
                    var type = doc.GetTypeName(m.TypeIndex);
                    var value = type == "hkReal" ? System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(doc.Bytes.AsSpan(offset, 4)).ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                        : type is "hkBool" or "bool" ? doc.Bytes[offset].ToString()
                        : type == "hkArray" ? "count:" + System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(doc.Bytes.AsSpan(offset + 8, 4))
                        : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(doc.Bytes.AsSpan(offset, 4)).ToString();
                    if (m.Name == "surfaceDensity") { densities.Add(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)); return null; }
                    if (m.Name == "numParticles") { Require(uint.Parse(value) == sim.Particles.Count, "Native aerodynamic particle count mismatch"); return null; }
                    return m.Name + "=" + value;
                }).Where(v => v != null);
                var pattern = string.Join(", ", values);
                patterns[pattern] = patterns.GetValueOrDefault(pattern) + 1;
            }
        }
        foreach (var p in patterns.OrderByDescending(p => p.Value)) Console.WriteLine(p.Value + ": " + p.Key);
        Console.WriteLine($"Aerodynamic records: {densities.Count}; density range: {densities.Min()} to {densities.Max()}; particle counts match.");
    }
    internal static void Survey(string root)
    {
        var needed = new HashSet<string> { "hclBoneSpaceDeformer::ThreeBlendEntryBlock" };
        foreach (var pack in Directory.EnumerateFiles(Path.Combine(root, "Pack/Actor"), "*.pack.zs"))
        {
            var archive = BotwSarcArchive.Parse(new TotkResourceCompression().Decode(File.ReadAllBytes(pack), [root]));
            foreach (var entry in archive.Entries.Where(p => p.Name.EndsWith(".bphcl")))
            {
                var doc = NativeBphclDocument.Parse(archive.Read(entry).ToArray());
                foreach (var type in doc.TypeDefinitions.Where(t => needed.Contains(doc.GetTypeName(t.TypeIndex)!)))
                {
                    Console.WriteLine(Path.GetFileName(pack) + "\n" + string.Join("\n", NativeBphclTypeTable.DescribeTypeIndex(doc, type.TypeIndex)));
                    foreach (var member in type.Members) Console.WriteLine(string.Join("\n", NativeBphclTypeTable.DescribeTypeIndex(doc, member.TypeIndex)));
                    needed.Remove(doc.GetTypeName(type.TypeIndex)!);
                }
                if (needed.Count == 0) return;
            }
        }
        throw new Exception("Native virtual-collision fixture not found");
    }
    internal static void Run(string oot, string totk, string output)
    {
        Directory.CreateDirectory(output);
        var target = OotClothConversion.CreateTotkReference();
        var schemaFixtures = new Dictionary<string, string[]>
        {
            ["Enemy_Ganondorf"] = ["hclLocalRangeConstraintSet::LocalStiffnessConstraint", "hclVirtualCollisionPointsData::EdgeFan", "hclVirtualCollisionPointsData::EdgeFanSection", "hclVirtualCollisionPointsData::BarycentricDictionaryEntry"],
            ["Animal_SunazarashiSP_A"] = ["hclBoneSpaceDeformer::OneBlendEntryBlock"],
            ["Armor_1051_Upper"] = ["hclBoneSpaceDeformer::ThreeBlendEntryBlock"],
            ["Armor_1036_Head"] = ["hclVirtualCollisionPointsData::BarycentricPair", "hclVirtualCollisionPointsData::TriangleFan", "hclVirtualCollisionPointsData::TriangleFanSection"]
        };
        foreach (var (actor, names) in schemaFixtures)
        {
            var pack = Path.Combine(totk, "Pack/Actor", actor + ".pack.zs");
            var archive = BotwSarcArchive.Parse(new TotkResourceCompression().Decode(File.ReadAllBytes(pack), [totk]));
            var native = NativeBphclDocument.Parse(archive.Read(archive.Entries.First(e => e.Name.EndsWith(".bphcl"))).ToArray());
            var types = target.TypeDefinitions.Where(t => names.Contains(target.GetTypeName(t.TypeIndex))).Select(t => t.TypeIndex);
            var missing = NativeBphclTypeTable.FindMissingRequiredTypes(native, target, types);
            Require(missing.Count == 0, actor + " schema differs: " + string.Join("; ", missing));
        }
        Console.WriteLine("PASS: completed conversion schemas match native TotK layouts");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(oot, "Phive/Cloth"), "*.bphcl")
            .Where(p => Path.GetFileName(p).StartsWith("Player_Adult.") || Path.GetFileName(p).StartsWith("Npc_Hylia_WU_Zelda_Adult.")))
        {
            var source = NativeBphclDocument.Open(file);
            var original = source.Bytes.ToArray();
            var converted = OotClothConversion.Convert(source, target);
            var check = NativeBphclDocument.Parse(converted);
            var back = NativeBphclDocument.Parse(OotClothConversion.Convert(check, source));
            CheckNeutralAerodynamics(back);
            if (back.Cloths.Count != source.Cloths.Count || !original.SequenceEqual(source.Bytes))
                throw new Exception("Cross-game conversion changed the source or cloth count");
            File.WriteAllBytes(Path.Combine(output, Path.GetFileName(file)), converted);
            Console.WriteLine($"PASS: {Path.GetFileName(file)}: OoT -> TotK -> OoT, {check.Cloths.Count} cloths");

            var service = new HkclService(); service.Load(file);
            var before = service.CaptureState();
            var export = Path.Combine(output, Path.GetFileNameWithoutExtension(file) + "-export.bphcl");
            OotConversionExport.Export(service, "", export, file, targetOot: false);
            Require(NativeBphclDocument.Open(export).SdkVersion == "20220100", "Native export target SDK");
            var conversion = new PhysicsConversion(service, file);
            Require(conversion.FidelityWarnings.Contains(OotConversionExport.OlderGameWarning), "Missing aerodynamic warning");
            foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
            {
                var hkcl = Path.Combine(output, Path.GetFileNameWithoutExtension(file) + "-" + platform + ".hkcl");
                conversion.Export(hkcl, new(platform));
                Require(HkclService.DetectHkclPlatform(hkcl) == platform, "HKCL platform mismatch");
                var botw = new HkclService(); botw.Load(hkcl);
                var backPath = Path.ChangeExtension(hkcl, ".bphcl");
                OotConversionExport.Export(botw, oot, backPath, hkcl);
                var ootResult = NativeBphclDocument.Open(backPath);
                Require(ootResult.IsOot, "BotW -> OoT target SDK");
                CheckNeutralAerodynamics(ootResult);
            }
            Require(service.CaptureState() == before, "Export mutated the source document");
            var existing = File.ReadAllBytes(export);
            try { OotConversionExport.Export(service, "", export, export, false); throw new Exception("Source overwrite accepted"); }
            catch (InvalidOperationException) { }
            Require(existing.SequenceEqual(File.ReadAllBytes(export)), "Rejected export replaced output");
            var editedBytes = original.ToArray();
            var sim = source.Cloths.SelectMany(c => c.SimCloths).First();
            var radius = source.TypeDefinitions.Single(t => t.TypeIndex == source.GetItem(sim.ItemIndex).TypeIndex)
                .Members.Single(m => m.Name == "virtualCollisionPointsRadiusScale");
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(
                editedBytes.AsSpan(source.DataSection!.PayloadOffset + (int)(sim.DataOffset + radius.Offset), 4), 2f);
            var incompatible = new HkclService(); incompatible.RestoreState("BPHCL:" + Convert.ToBase64String(editedBytes));
            try { OotConversionExport.Export(incompatible, "", export, file, false); throw new Exception("Lossy radius scaling accepted"); }
            catch (NotSupportedException ex) when (ex.Message.Contains("radius scaling")) { }
            Require(existing.SequenceEqual(File.ReadAllBytes(export)), "Unsupported conversion replaced existing output");
            Console.WriteLine("PASS: service exports, both HKCL platforms, BotW -> OoT, source isolation and output protection");
        }
        int passed = 0;
        var blocked = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(oot, "Phive/Cloth"), "*.bphcl"))
        {
            var source = NativeBphclDocument.Open(file);
            try
            {
                var converted = NativeBphclDocument.Parse(OotClothConversion.Convert(source, target));
                CheckNeutralAerodynamics(NativeBphclDocument.Parse(OotClothConversion.Convert(converted, source)));
                passed++;
            }
            catch (NotSupportedException ex) { blocked.Add(Path.GetFileName(file) + ": " + ex.Message); }
        }
        File.WriteAllLines(Path.Combine(output, "guarded-conversions.txt"), blocked);
        Require(passed > 0, "No native conversion fixtures were checked");
        Console.WriteLine($"Native corpus round trips: {passed} passed, {blocked.Count} explicitly guarded.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    private static void CheckNeutralAerodynamics(NativeBphclDocument doc)
    {
        foreach (var sim in doc.Cloths.SelectMany(c => c.SimCloths))
        {
            var member = doc.TypeDefinitions.Single(t => t.TypeIndex == doc.GetItem(sim.ItemIndex).TypeIndex).Members.Single(m => m.Name == "aerodynamicsData");
            var fields = doc.TypeDefinitions.Single(t => t.TypeIndex == member.TypeIndex).Members.ToDictionary(m => m.Name);
            int At(string name) => checked(doc.DataSection!.PayloadOffset + (int)(sim.DataOffset + member.Offset + fields[name].Offset));
            float Float(string name) => System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(doc.Bytes.AsSpan(At(name), 4));
            Require(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(doc.Bytes.AsSpan(At("numParticles"), 4)) == sim.Particles.Count, "Converted aerodynamic particle count mismatch");
            Require(Float("liftCoefficient") == 0 && Float("dragCoefficient") == 0 && Float("surfaceDensity") == 1, "Neutral aerodynamic coefficients changed");
            Require(doc.Bytes[At("useSurfaceDensityWithSimpleWind")] == 0, "Conversion reinterpreted simple wind");
            foreach (var kind in new[] { "Lift", "Drag" })
            {
                Require(Float("constantParticle" + kind + "Factor") == 1 && doc.Bytes[At("useConstant" + kind + "Factor")] == 1, "Missing constant aerodynamic factor");
                Require(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(doc.Bytes.AsSpan(At("perParticle" + kind + "Factor") + 8, 4)) == 0, "Unexpected aerodynamic array");
            }
        }
    }
}
