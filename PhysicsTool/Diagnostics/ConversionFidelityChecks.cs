using System.Collections;
using System.Reflection;
using static HKCLTool.BotwAampWriter;

namespace HKCLTool;

internal static class ConversionFidelityChecks
{
    public static void Run(string output, string fixtures)
    {
        Directory.CreateDirectory(output);
        var cloths = 0;
        var files = Directory.GetFiles(Path.Combine(fixtures, "Matches"), "*.bphcl")
            .Append(Path.Combine(fixtures, "Yiga", "Armor_1300_Head.bphcl")).ToArray();
        foreach (var file in files)
        {
            var source = new HkclService(); source.Load(file);
            foreach (var scale in source.GetBphclConversionScaleSuggestions())
            {
                var index = scale.ClothIndex;
                if (!BphclConversionProfileCatalog.HasExactSourceProfile(file, scale.ClothName))
                {
                    Near(scale.DefaultMassScale, 1, "Unknown cloth received guessed mass scaling");
                    Near(scale.DefaultConstraintScale, 1, "Unknown cloth received guessed coefficient scaling");
                }
                var converted = source.CreateFreshHkclFromCurrentBphcl(index);
                var sourceSets = source.GetConstraintSetSummaries(index).ToDictionary(s => s.Index, s => s.ClassName);
                var targetSets = converted.GetConstraintSetSummaries(0).ToDictionary(s => s.Index, s => s.ClassName);
                var expected = Schedule(source, index).Select(i => i == -1 ? "collision" : sourceSets[i])
                    .Select(name => BphclConversionProfileCatalog.ShouldOmitConstraintSet(source.GetClothName(index), name)
                        ? name == "hclBendStiffnessConstraintSet" && targetSets.ContainsValue("hclBendLinkConstraintSet")
                            ? "hclBendLinkConstraintSet" : null
                        : name).Where(name => name != null).ToArray();
                var actual = Schedule(converted, 0).Select(i => i == -1 ? "collision" : targetSets[i]).ToArray();
                Require(expected.SequenceEqual(actual), $"Solver order changed: {file} / {scale.ClothName}");
                var before = source.GetParticleRows(index);
                var after = converted.GetParticleRows(0);
                Require(before.Count == after.Count, "Particle count changed.");
                for (var p = 0; p < before.Count; p++)
                {
                    Require(before[p].Fixed == after[p].Fixed, "Fixed membership changed.");
                    if (before[p].Fixed) continue;
                    Near(after[p].Mass, before[p].Mass * scale.DefaultMassScale, "Mass scaling");
                    Near(after[p].InverseMass, before[p].InverseMass / scale.DefaultMassScale, "Inverse mass scaling");
                }
                foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
                {
                    var path = Path.Combine(output, $"cloth-{cloths}-{platform}.hkcl");
                    converted.SaveHkcl(path, platform);
                    Require(HkclService.DetectHkclPlatform(path) == platform, "Wrong packfile platform.");
                    var reload = new HkclService(); reload.Load(path);
                    Require(Schedule(converted, 0).SequenceEqual(Schedule(reload, 0)), "Saved solver order changed.");
                    var particles = reload.GetParticleRows(0);
                    Require(particles.Count == after.Count, "Saved particle count changed.");
                    for (var p = 0; p < after.Count; p++)
                    {
                        Near(particles[p].Mass, after[p].Mass, "Saved mass");
                        Near(particles[p].InverseMass, after[p].InverseMass, "Saved inverse mass");
                        Require(particles[p].Fixed == after[p].Fixed, "Saved fixed membership changed.");
                    }
                }
                cloths++;
            }
        }
        RepeatedCollisionPasses(files[0]);

        var helpers = 0;
        BphyssbDocumentSummary? fixture = null;
        foreach (var file in Directory.GetFiles(fixtures, "*.bphhb", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(fixtures, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "Research" or "GhidraProjects")))
        {
            var source = BphhbBridge.Load(file);
            var intermediate = Path.Combine(output, $"helper-{helpers}.bphyssb");
            BphyssbBridge.Export(source, intermediate);
            var mid = BphyssbBridge.Load(intermediate);
            fixture ??= mid;
            var result = BphhbBridge.Export(mid, Path.Combine(output, $"helper-{helpers}.bphhb"));
            Require(Bases(source.Graph!).SequenceEqual(Bases(result.Graph!)), $"Helper references changed: {file}");
            Require(source.Graph!.Drivers.Count == result.Graph!.Drivers.Count, "Driver count changed.");
            Require(source.Graph.Driven.Count == result.Graph.Driven.Count, "Driven count changed.");
            for (var i = 0; i < source.Graph.Drivers.Count; i++)
                Require(source.Graph.Drivers[i].Translation == result.Graph.Drivers[i].Translation
                    && source.Graph.Drivers[i].Rotation == result.Graph.Drivers[i].Rotation, "Driver reference transform changed.");
            for (var i = 0; i < source.Graph.PoseDriven.Count; i++)
                Require(source.Graph.PoseDriven[i].Translation == result.Graph.PoseDriven[i].Translation
                    && source.Graph.PoseDriven[i].Rotation == result.Graph.PoseDriven[i].Rotation, "Pose reference transform changed.");
            helpers++;
        }
        Require(fixture != null, "No helper fixtures found.");
        HelperSafety(fixture!, output);
        File.WriteAllText(Path.Combine(output, "conversion.txt"),
            $"PASS: {cloths} cloth conversions, {cloths * 2} Wii U/Switch save/reloads, {helpers} helper round trips; collision order/repetition, mass/inverse mass, fixed membership, reference bones, all six channels, parent resolution, and safe rejection.\n");
    }

    private static void RepeatedCollisionPasses(string path)
    {
        var native = BphclBridge.Load(path).NativeDocument!.Cloths[0];
        var sourceOp = native.Operators.First(op => op.ClassName == "hclSimulateOperator");
        var set = sourceOp.SimulateConfigs[0].ConstraintExecution.First(i => i >= 0);
        var config = sourceOp.SimulateConfigs[0] with { ConstraintExecution = new[] { -1, set, -1, set, -1 } };
        var service = new HkclService(); service.Load(path);
        var converted = service.CreateFreshHkclFromCurrentBphcl(0);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var root = typeof(HkclService).GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(converted);
        var cloth = ((IEnumerable)typeof(HkclService).GetMethod("GetClothDatas", flags)!.Invoke(null, new[] { root })!).Cast<object>().First();
        typeof(HkclService).GetMethod("ApplyBphclConstraintExecution", flags)!.Invoke(null,
            new object[] { cloth, native.SimCloths[0], native.Name, new[] { sourceOp with { SimulateConfigs = new[] { config } } } });
        var sequence = Schedule(converted, 0);
        Require(sequence.Length == 5 && sequence[0] == -1 && sequence[2] == -1 && sequence[4] == -1
            && sequence[1] == sequence[3] && sequence[1] >= 0, "Repeated collision/constraint stages changed.");
    }

    private static void HelperSafety(BphyssbDocumentSummary source, string output)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var pack = typeof(BphyssbBridge).GetMethod("PackBoneAttribute", flags)!;
        var unpack = typeof(BphhbBridge).GetMethod("UnpackBoneAttribute", flags)!;
        for (var channel = 0; channel < 6; channel++)
        {
            var packed = (int)pack.Invoke(null, new object[] { 7, channel })!;
            Require((int)unpack.Invoke(null, new object[] { packed & 0xffff, 0 })! == channel, "Channel round trip failed.");
        }
        var data = source.Archive!.Root.Children.First(c => c.FindChild("main_bone_list") != null);
        var curve = data.FindChild("connection_curve_list")!.Objects[0];
        var originalAttribute = curve.Find("bone_attribute")!.AsInt32();
        for (var channel = 0; channel < 6; channel++)
        {
            var attribute = (int)pack.Invoke(null, new object[] { originalAttribute >> 16, channel })!;
            Set(curve, "bone_attribute", attribute);
            var native = BphhbBridge.Export(source, Path.Combine(output, $"channel-{channel}.bphhb"));
            var roundPath = Path.Combine(output, $"channel-{channel}.bphyssb");
            BphyssbBridge.Export(native, roundPath);
            var round = BphyssbBridge.Load(roundPath).Archive!.Root.Children[0]
                .FindChild("connection_curve_list")!.Objects[0].Find("bone_attribute")!.AsInt32();
            Require((round & 0xffff) == (attribute & 0xffff), "Serialized channel round trip failed.");
        }
        Set(curve, "bone_attribute", originalAttribute);
        var driver = data.FindChild("main_bone_list")!.Objects[0];
        var boneIndex = driver.Find("bone")!.AsInt32();
        var name = data.FindChild("bone_list")!.Objects[boneIndex].Find("name")!.AsString();
        Set(driver, "space", -1);
        var destination = Path.Combine(output, "protected.bphhb");
        File.WriteAllText(destination, "sentinel");
        Reject(() => BphhbBridge.Export(source, destination), destination);
        var parents = new Dictionary<string, string> { [name] = "ActualModelParent" };
        var resolved = BphhbBridge.ExportWithParents(source, Path.Combine(output, "resolved.bphhb"), parents).Graph!;
        Require(Local(resolved.Bones[resolved.Drivers[0].BaseBoneId]) == "ActualModelParent", "Actual parent was not used.");
        Set(driver, "space", -2);
        Reject(() => BphhbBridge.ExportWithParents(source, destination, parents), destination);
        Set(driver, "space", -1);
        var ambiguous = new Dictionary<string, string> { ["A:" + Local(name)] = "ParentA", ["B:" + Local(name)] = "ParentB" };
        Reject(() => BphhbBridge.ExportWithParents(source, destination, ambiguous), destination);
        var singles = data.FindChild("output_single_list")!;
        Set(singles.Objects[0], "connection", 0);
        Reject(() => BphhbBridge.ExportWithParents(source, destination, parents), destination);
    }

    private static void Set(AampReadObject item, string name, int value)
    {
        item.Parameters.RemoveAll(p => p.Hash == Crc32(name));
        item.Parameters.Add(new(Crc32(name), 2, BitConverter.GetBytes(value)));
    }
    private static int[] Schedule(HkclService service, int cloth) => service.GetClothRuntimeData(cloth)
        .Operators.First(op => op.ClassName == "hclSimulateOperator").SimulateConfigs[0].ConstraintExecution.ToArray();
    private static string Local(string name) => name[(name.LastIndexOf(':') + 1)..];
    private static IEnumerable<string> Bases(BphhbGraphSummary g) =>
        g.Drivers.Select(d => Local(g.Bones[d.BoneId]) + ":" + Local(g.Bones[d.BaseBoneId]))
            .Concat(g.PoseDriven.Select(p => Local(g.Bones[p.BaseBoneId])));
    private static void Near(float actual, float expected, string name) =>
        Require(float.IsFinite(actual) && MathF.Abs(actual - expected) <= MathF.Max(0.00001f, MathF.Abs(expected) * 0.00001f), name);
    private static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string path)
    {
        try { action(); }
        catch (InvalidDataException) { Require(File.ReadAllText(path) == "sentinel", "Rejected export overwrote its destination."); return; }
        throw new InvalidOperationException("Unsafe helper conversion was accepted.");
    }
}
