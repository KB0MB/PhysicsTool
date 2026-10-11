using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection;
using System.Numerics;
using HKCLTool;
using HKX2;
using PhysicsTool.Core;
using ZstdSharp;

internal static class ClothConversionAudit
{
    public static void Synthetic()
    {
        ConversionFidelityChecks.Run();
        var session = new EditorSession(); session.New("Soft ranges", 2);
        var native = session.Document.CaptureBphclNativeDocument()!.Cloths[0];
        NativeBphclConstraintLink Record(int index, double? stiffness) => new(index, null, null,
            new Dictionary<string, double> { ["particleIndex"] = 0, ["referenceVertex"] = index, ["shapeRadius"] = .1,
                ["stiffness"] = stiffness ?? .8 }, 0, new Dictionary<string, uint>());
        foreach (var mixed in new[] { false, true })
        {
            var set = new NativeBphclConstraintSet(0, "Soft ranges", "hclLocalRangeConstraintSet", 0, 0, 0, 0, false,
                [], mixed ? [Record(0, null)] : [], [Record(1, .2), Record(2, .2), Record(3, .6), Record(4, .2)], [], [], [],
                new Dictionary<string, double> { ["stiffness"] = .8, ["referenceMeshBufferIdx"] = 0 });
            var source = native.SimCloths[0] with { ConstraintSets = new[] { set } };
            var op = new hclSimulateOperator { m_constraintExecution = new() };
            var cloth = new hclClothData { m_simClothDatas = new() { new hclSimClothData {
                m_staticConstraintSets = new(), m_antiPinchConstraintSets = new() } }, m_operators = new() { op } };
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            typeof(HkclService).GetMethod("ApplyBphclConstraintLinks", flags)!.Invoke(null,
                new object?[] { cloth, source, null, "Soft ranges", false, 1f });
            var sourceOp = native.Operators.First(o => o.ClassName == "hclSimulateOperator");
            var config = sourceOp.SimulateConfigs[0] with { ConstraintExecution = new[] { 0, -1, 0 } };
            typeof(HkclService).GetMethod("ApplyBphclConstraintExecution", flags)!.Invoke(null,
                new object[] { cloth, source, "Soft ranges", new[] { sourceOp with { SimulateConfigs = new[] { config } } } });
            var result = cloth.m_simClothDatas[0].m_staticConstraintSets.Cast<hclLocalRangeConstraintSet>().ToArray();
            var coefficients = result.SelectMany(s => s.m_localConstraints.Select(_ => s.m_stiffness)).ToArray();
            var expected = (mixed ? new[] { .8f } : Array.Empty<float>()).Concat(new[] { .2f, .2f, .6f, .2f }).ToArray();
            Require(coefficients.SequenceEqual(expected), "Mixed local range stiffness/order changed");
            Require(result.Length == (mixed ? 4 : 3), "Local ranges were not split into contiguous runs");
            var indices = Enumerable.Range(0, result.Length).ToArray();
            Require(op.m_constraintExecution.SequenceEqual(indices.Concat(new[] { -1 }).Concat(indices)), "Repeated local-range/collision schedule changed");
        }
        Console.WriteLine("PASS: mixed hard/soft and soft-only local ranges, repeated particle order, contiguous runs and repeated collision schedule.");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static IReadOnlyList<int> Schedule(HkclService doc, int cloth) => doc.GetClothRuntimeData(cloth).Operators
        .First(op => op.ClassName == "hclSimulateOperator").SimulateConfigs[0].ConstraintExecution;

    public static void Run(string output, string[] paths)
    {
        Synthetic();
        Directory.CreateDirectory(output);
        var report = new List<object>();
        foreach (var input in paths)
        {
            var hash = SHA256.HashData(File.ReadAllBytes(input));
            foreach (var path in Extract(input, output))
            {
                var source = new HkclService(); source.Load(path);
                if (!source.IsBphcl)
                {
                    for (var i = 0; i < source.GetClothSummaries().Count; i++) report.Add(Describe(path, source, i));
                    continue;
                }
                var target = source.CreateFreshHkclFromCurrentBphclDocument();
                var json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
                File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".source.json"),
                    JsonSerializer.Serialize(new { source.CaptureBphclNativeDocument()!.Cloths,
                        source.CaptureBphclNativeDocument()!.Skeletons, source.CaptureBphclNativeDocument()!.Colliders,
                        Warnings = new PhysicsConversion(source).FidelityWarnings }, json));
                File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".runtime.json"),
                    JsonSerializer.Serialize(Enumerable.Range(0, source.GetClothSummaries().Count).Select(i => new {
                        Cloth = source.GetClothName(i), Source = source.GetClothRuntimeData(i), Converted = target.GetClothRuntimeData(i),
                        SourcePreview = source.GetParticlePreview(i), ConvertedPreview = target.GetParticlePreview(i)
                    }), json));
                for (var i = 0; i < source.GetClothSummaries().Count; i++)
                {
                    Check(source, target, i);
                    report.Add(new { Source = Describe(path, source, i), Converted = Describe(path, target, i),
                        MotionProbe = CompareMotion(source, target, i) });
                }
                foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
                {
                    var destination = Path.Combine(output, Path.GetFileNameWithoutExtension(path) + "." + platform + ".hkcl");
                    target.SaveHkcl(destination, platform);
                    var reload = new HkclService(); reload.Load(destination);
                    for (var i = 0; i < source.GetClothSummaries().Count; i++)
                    {
                        Check(source, reload, i);
                        Require(Schedule(target, i).SequenceEqual(Schedule(reload, i)), "Saved schedule changed");
                        var sim = new HkclPreviewSimulator(reload.GetParticlePreview(i));
                        for (var frame = 0; frame < 120; frame++) sim.Step(1f / 60);
                        Require(sim.GetPositions().Values.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)), "Nonfinite simulation");
                    }
                }
                Console.WriteLine($"PASS: {Path.GetFileName(path)}, {source.GetClothSummaries().Count} cloths, exact local softness/order, mass/fixed values, Wii U/Switch save/reload and 120 preview frames.");
            }
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))), "Input file modified");
        }
        File.WriteAllText(Path.Combine(output, "audit.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Check(HkclService source, HkclService target, int cloth)
    {
        var bendsBefore = source.GetBendStiffnessConstraintSets(cloth);
        var bendsAfter = target.GetBendStiffnessConstraintSets(cloth);
        if (!BphclConversionProfileCatalog.ShouldOmitConstraintSet(source.GetClothName(cloth), "hclBendStiffnessConstraintSet"))
        {
            Require(bendsBefore.Count == bendsAfter.Count, "Four-particle bend sets lost");
            var coefficientScale = source.GetBphclConversionScaleSuggestions()[cloth].DefaultConstraintScale;
            for (var set = 0; set < bendsBefore.Count; set++)
            {
                Require(bendsBefore[set].UseRestPoseConfig == bendsAfter[set].UseRestPoseConfig, "Rest-pose mode changed");
                Require(bendsBefore[set].Links.Select(l => l with { BendStiffness = (float)((double)l.BendStiffness * coefficientScale) })
                    .SequenceEqual(bendsAfter[set].Links), "Four-particle weights, curvature, order or stiffness changed");
            }
        }
        var a = source.GetLocalRangeConstraintSets(cloth).SelectMany(set => set.Constraints.Select(c => (c.ParticleIndex, c.ReferenceVertex, Stiffness: c.RecordStiffness ?? set.Stiffness))).ToArray();
        var b = target.GetLocalRangeConstraintSets(cloth).SelectMany(set => set.Constraints.Select(c => (c.ParticleIndex, c.ReferenceVertex, set.Stiffness))).ToArray();
        Require(a.SequenceEqual(b), $"Local softness/order mismatch: {source.GetClothName(cloth)}");
        var sourcePlanes = source.GetBonePlaneConstraintSets(cloth);
        var targetPlanes = target.GetBonePlaneConstraintSets(cloth);
        Require(sourcePlanes.Count == targetPlanes.Count, "Bone-plane sets lost");
        for (var i = 0; i < sourcePlanes.Count; i++) Require(sourcePlanes[i].TransformSetIndex == targetPlanes[i].TransformSetIndex
            && sourcePlanes[i].Planes.SequenceEqual(targetPlanes[i].Planes), "Bone-plane equation/binding/stiffness changed");
        var scale = source.GetBphclConversionScaleSuggestions()[cloth].DefaultMassScale;
        var before = source.GetParticleRows(cloth); var after = target.GetParticleRows(cloth);
        Require(before.Count == after.Count, "Particle count changed");
        for (var i = 0; i < before.Count; i++)
        {
            Require(before[i].Fixed == after[i].Fixed, "Fixed membership changed");
            if (!before[i].Fixed) Require(Math.Abs(after[i].Mass - before[i].Mass * scale) < 1e-5f && Math.Abs(after[i].InverseMass - before[i].InverseMass / scale) < Math.Max(1e-5f, Math.Abs(after[i].InverseMass) * 1e-5f), "Mass policy changed");
        }
        string[] Tokens(HkclService doc)
        {
            var sets = doc.GetConstraintSetSummaries(cloth).ToDictionary(s => s.Index, s => s.ClassName);
            var local = doc.GetLocalRangeConstraintSets(cloth).ToDictionary(s => s.ConstraintSetIndex);
            return Schedule(doc, cloth).SelectMany(index => index == -1 ? new[] { "collision" } : local.TryGetValue(index, out var set)
                ? set.Constraints.Select(c => $"local:{c.ParticleIndex}:{c.ReferenceVertex}:{(c.RecordStiffness ?? set.Stiffness):R}")
                : new[] { sets[index] }).ToArray();
        }
        var targetKinds = target.GetConstraintSetSummaries(cloth).Select(s => s.ClassName).ToHashSet();
        var expected = Tokens(source).Select(token => BphclConversionProfileCatalog.ShouldOmitConstraintSet(source.GetClothName(cloth), token)
            ? targetKinds.Contains("hclBendLinkConstraintSet") ? "hclBendLinkConstraintSet" : null : token).Where(t => t != null);
        Require(expected.SequenceEqual(Tokens(target)), "Constraint order/repetition changed: " + source.GetClothName(cloth));
    }

    private static object Describe(string path, HkclService doc, int cloth) => new {
        File = Path.GetFileName(path), Cloth = doc.GetClothName(cloth), ParticleCount = doc.GetParticleRows(cloth).Count,
        LocalRanges = doc.GetLocalRangeConstraintSets(cloth), ConstraintSets = doc.GetConstraintSetSummaries(cloth),
        Schedule = Schedule(doc, cloth)
    };

    private static object CompareMotion(HkclService source, HkclService target, int cloth)
    {
        var original = source.GetParticlePreview(cloth);
        var unclamped = source.GetParticlePreview(cloth);
        var converted = target.GetParticlePreview(cloth);
        // BPHCL's editor-only collider preview has no active collision-slot map.
        // Isolate constraints; it is not a native TotK collision reference.
        original.Colliders.Clear(); unclamped.Colliders.Clear(); converted.Colliders.Clear();
        original.UseNativeHkclInputSampling = unclamped.UseNativeHkclInputSampling = true;
        foreach (var bend in unclamped.BendStiffnessConstraints) bend.ClampBendStiffness = false;
        var a = new HkclPreviewSimulator(original);
        var b = new HkclPreviewSimulator(converted);
        var c = new HkclPreviewSimulator(unclamped);
        var maxExportDifference = 0f; var maxWithoutCutoffDifference = 0f; var maxCutoffEffect = 0f;
        for (var frame = 0; frame < 600; frame++)
        {
            var t = frame / 60f;
            var motion = Matrix4x4.CreateRotationZ(.15f * MathF.Sin(t * 2)) *
                Matrix4x4.CreateTranslation(.08f * MathF.Sin(t * 3), .025f * MathF.Sin(t * 5), 0);
            var deltas = original.Bones.ToDictionary(bone => bone.Index, _ => motion);
            foreach (var solver in new[] { a, b, c }) { solver.SetInputBoneDeltas(deltas); solver.Step(1f / 60); }
            var ap = a.GetPositions(); var bp = b.GetPositions(); var cp = c.GetPositions();
            foreach (var p in ap)
            {
                Require(float.IsFinite(p.Value.LengthSquared()) && float.IsFinite(bp[p.Key].LengthSquared()) &&
                    float.IsFinite(cp[p.Key].LengthSquared()), "Nonfinite motion probe");
                maxExportDifference = Math.Max(maxExportDifference, Vector3.Distance(p.Value, bp[p.Key]));
                maxWithoutCutoffDifference = Math.Max(maxWithoutCutoffDifference, Vector3.Distance(cp[p.Key], bp[p.Key]));
                maxCutoffEffect = Math.Max(maxCutoffEffect, Vector3.Distance(p.Value, cp[p.Key]));
            }
        }
        Require(a.NumericalResetCount + b.NumericalResetCount + c.NumericalResetCount == 0, "Motion probe triggered numerical reset");
        return new { Frames = 600, MaxExportDifference = maxExportDifference,
            MaxWithoutCutoffDifference = maxWithoutCutoffDifference, MaxCutoffEffect = maxCutoffEffect,
            Scope = "Collision-free constraint probe with shared BotW input sampling; source skin-reference precision differs. Not native-game equivalence." };
    }

    private static IEnumerable<string> Extract(string input, string output)
    {
        if (!input.EndsWith(".pack.zs") && !input.EndsWith(".sbactorpack")) return new[] { input };
        BotwSarcArchive archive;
        if (input.EndsWith(".pack.zs"))
        {
            using var decoder = new Decompressor();
            var romfs = Directory.GetParent(input)!.Parent!.Parent!.FullName;
            var dictionaries = BotwSarcArchive.Parse(decoder.Unwrap(File.ReadAllBytes(Path.Combine(romfs, "Pack", "ZsDic.pack.zs"))).ToArray());
            var bytes = File.ReadAllBytes(input); var offset = (bytes[4] & 0x20) != 0 ? 5 : 6;
            var id = (bytes[4] & 3) switch { 0 => 0u, 1 => bytes[offset], 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)), _ => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) };
            if (id != 0) decoder.LoadDictionary(dictionaries.Entries.Select(e => dictionaries.Read(e).ToArray()).Single(b => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4)) == id));
            archive = BotwSarcArchive.Parse(decoder.Unwrap(bytes).ToArray());
        }
        else archive = BotwSarcArchive.Open(input);
        var folder = Path.Combine(output, input.EndsWith(".pack.zs") ? "TotK" : "BotW", Path.GetFileName(input)); Directory.CreateDirectory(folder);
        var result = new List<string>();
        foreach (var entry in archive.Entries.Where(e => e.Name.EndsWith(".bphcl") || e.Name.EndsWith(".hkcl")))
        {
            var path = Path.Combine(folder, Path.GetFileName(entry.Name)); File.WriteAllBytes(path, archive.Read(entry).ToArray()); result.Add(path);
        }
        Console.WriteLine($"Extracted {result.Count} cloth files from {Path.GetFileName(input)}.");
        return result;
    }
}
