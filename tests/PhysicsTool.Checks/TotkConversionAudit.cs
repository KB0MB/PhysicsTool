using HKCLTool;
using HKX2;
using PhysicsTool.Core;
using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using static HKCLTool.BotwAampWriter;

internal static class TotkConversionAudit
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static ClothSimulateConfigData Config(HkclService d, int i) => d.GetClothRuntimeData(i).Operators.Single(o => o.ClassName == "hclSimulateOperator").SimulateConfigs.Single();

    public static void Run(string output, string? fixtures = null)
    {
        Directory.CreateDirectory(output);
        SyntheticCloth(output);
        WeightedHelpers(output);
        var files = 0; var cloths = 0; var helpers = 0; var blocked = new List<string>();
        if (fixtures != null)
        {
            foreach (var file in Directory.GetFiles(fixtures, "*.hkcl"))
            {
                var hash = SHA256.HashData(File.ReadAllBytes(file));
                var source = new HkclService(); source.Load(file);
                CheckCloth(source, Path.Combine(output, Path.GetFileNameWithoutExtension(file) + ".bphcl"));
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))), "Audit modified source cloth");
                files++; cloths += source.GetClothSummaries().Count;
            }
            foreach (var file in Directory.GetFiles(fixtures, "*.bphyssb"))
            {
                var hash = SHA256.HashData(File.ReadAllBytes(file));
                var source = BphyssbBridge.Load(file);
                var parents = new Dictionary<string, string>();
                var clothPath = Path.ChangeExtension(file, ".hkcl");
                if (File.Exists(clothPath))
                {
                    var doc = new HkclService(); doc.Load(clothPath);
                    for (var i = 0; i < doc.GetClothSummaries().Count; i++)
                    {
                        var bones = doc.GetBoneRows(i).ToDictionary(b => b.Index);
                        foreach (var b in bones.Values)
                            if (bones.TryGetValue(b.ParentIndex, out var parent)) parents.TryAdd(b.Name, parent.Name);
                    }
                }
                try
                {
                    var result = BphhbBridge.ExportWithParents(source, Path.Combine(output, Path.GetFileNameWithoutExtension(file) + ".bphhb"), parents);
                    Require(result.Graph != null, "Native helper export lost graph");
                    CheckHelperCurves(source, result);
                    helpers++;
                }
                catch (InvalidDataException e) when (e.Message.Contains("Cannot resolve the parent") || e.Message.Contains("unsupported or invalid reference space") || e.Message.Contains("linear or unsupported connection"))
                { blocked.Add(Path.GetFileName(file) + ": " + e.Message); }
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))), "Audit modified source helper");
            }
        }
        var report = $"PASS: synthetic solver order/repeats, iterations, bone planes, independent inverse mass and weighted helper curves.\nNative: {files} HKCL files, {cloths} cloths, {helpers} support exports; {blocked.Count} safely blocked helpers requiring model/context or unsupported connections.\n" + string.Join("\n", blocked);
        File.WriteAllText(Path.Combine(output, "totk-conversion-audit.txt"), report);
        Console.WriteLine(report);
    }

    private static void SyntheticCloth(string output)
    {
        var session = new EditorSession(); session.New("ConversionProbe", 0);
        var doc = session.Document; doc.AddParticle(0); doc.AddParticle(0);
        var root = typeof(HkclService).GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc);
        var cloth = ((IEnumerable)typeof(HkclService).GetMethod("GetClothDatas", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [root])!).Cast<hclClothData>().Single();
        var sim = cloth.m_operators.OfType<hclSimulateOperator>().Single();
        sim.m_subSteps = 2; sim.m_numberOfSolveIterations = 7; sim.m_adaptConstraintStiffness = false;
        var data = cloth.m_simClothDatas.Single();
        var planeIndex = data.m_staticConstraintSets.Count;
        data.m_staticConstraintSets.Add(new hclBonePlanesConstraintSet { m_name = "Plane probe", m_transformSetIndex = 0,
            m_bonePlanes = [new hclBonePlanesConstraintSetBonePlane { m_particleIndex = 1, m_transformIndex = 0,
                m_planeEquationBone = new Vector4(0, 1, 0, -.25f), m_stiffness = .4f }] });
        sim.m_constraintExecution = [planeIndex, -1, 0, planeIndex, -1];
        data.m_particleDatas[1].m_mass = 2f; data.m_particleDatas[1].m_invMass = .3f;
        CheckCloth(doc, Path.Combine(output, "synthetic.bphcl"));
        var saved = new HkclService(); saved.Load(Path.Combine(output, "synthetic.bphcl"));
        var config = Config(saved, 0);
        var sets = saved.GetConstraintSetSummaries(0).ToDictionary(s => s.Index, s => s.ClassName);
        Require(config.ConstraintExecution.Select(i => i == -1 ? "collision" : sets[i]).SequenceEqual(new[] {
            "hclBonePlanesConstraintSet", "collision", "hclStandardLinkConstraintSet", "hclBonePlanesConstraintSet", "collision" }), "Source order or repeated plane pass changed");
        Require(config.SubSteps == 2 && config.NumberOfSolveIterations == 7 && !config.AdaptConstraintStiffness, "Source solver settings reset");
        Require(saved.GetParticleRows(0)[1].Mass == 2 && saved.GetParticleRows(0)[1].InverseMass == .3f, "Independent inverse mass regenerated");
        Require(saved.GetClothRuntimeData(0).Operators.Single(o => o.ClassName == "hclSimulateOperator").OperatorTransformSetAccesses!.Count == 1, "Bone-plane skeleton access missing");
        sim.m_constraintExecution.Clear();
        CheckCloth(doc, Path.Combine(output, "empty-schedule.bphcl"));
        var bad = HkclToBphclGameReadyGraphBuilder.Build(doc);
        var c = bad.Cloths[0]; var particles = c.Particles.ToArray(); particles[1] = particles[1] with { InverseMass = float.NaN };
        try { (bad with { Cloths = [c with { Particles = particles }] }).Validate(); throw new Exception("NaN inverse mass accepted"); }
        catch (InvalidDataException) { }
    }

    private static void CheckCloth(HkclService source, string output)
    {
        var graph = HkclToBphclGameReadyGraphBuilder.Build(source);
        File.WriteAllBytes(output, NativeBphclStandaloneGraphWriter.WriteGameReady(graph));
        BphclStandaloneValidation.Validate(NativeBphclDocument.Open(output), graph, true);
        var result = new HkclService(); result.Load(output);
        Require(result.GetClothSummaries().Count == graph.Cloths.Count, "Lost cloths");
        for (var i = 0; i < graph.Cloths.Count; i++)
        {
            var g = graph.Cloths[i]; var config = Config(result, i); var expected = g.GeneratedSimulateConfig!;
            Require(config.ConstraintExecution.SequenceEqual(expected.ConstraintExecution), $"Solver schedule changed: {g.Name}");
            Require(config.SubSteps == expected.SubSteps && config.NumberOfSolveIterations == expected.NumberOfSolveIterations &&
                config.AdaptConstraintStiffness == expected.AdaptConstraintStiffness && config.UseAllInstanceCollidables == expected.UseAllInstanceCollidables,
                $"Solver controls changed: {g.Name}");
            Require(config.InstanceCollidablesUsed.SequenceEqual(expected.InstanceCollidablesUsed), "Collider selection changed");
            var rows = result.GetParticleRows(i);
            for (var p = 0; p < rows.Count; p++)
                Require(rows[p].Mass == g.Particles[p].Mass && rows[p].InverseMass == g.Particles[p].InverseMass && rows[p].Fixed == g.Particles[p].Fixed, "Mass/fixed data changed");
            Require(result.GetBonePlaneConstraintSets(i).Sum(s => s.Planes.Count) == source.GetBonePlaneConstraintSets(i).Sum(s => s.Planes.Count), "Bone planes lost");
        }
    }

    private static void WeightedHelpers(string output)
    {
        var root = new AampListNode("param_root"); root.Objects.Add(new AampObjectNode("support_bone_header"));
        var data = new AampListNode("support_bone_data"); root.Children.Add(data);
        AampListNode List(string name) { var list = new AampListNode(name); data.Children.Add(list); return list; }
        var bones = List("bone_list");
        foreach (var name in new[] { "Root", "Driver", "Driven" })
        { var bone = new AampObjectNode("bone_" + bones.Objects.Count); bone.Parameters.Add(AampParameterNode.String256Value("name", name)); bones.Objects.Add(bone); }
        var curve = new AampObjectNode("connection_curve_0"); List("connection_curve_list").Objects.Add(curve);
        curve.Parameters.Add(AampParameterNode.Integer("bone_attribute", 3));
        curve.Parameters.Add(AampParameterNode.Vector4("key_0", new Vector4(-1, -2, .2f, .3f)));
        curve.Parameters.Add(AampParameterNode.Vector4("key_1", new Vector4(0, .5f, -.1f, .7f)));
        curve.Parameters.Add(AampParameterNode.Vector4("key_2", new Vector4(1, 3, .4f, .6f)));
        var singles = List("output_single_list");
        foreach (var weight in new[] { 0f, .25f, -2f, 1f })
        { var o = new AampObjectNode("output_single_" + singles.Objects.Count); singles.Objects.Add(o);
          o.Parameters.Add(AampParameterNode.Integer("connection", 0x10000)); o.Parameters.Add(AampParameterNode.Single("weight", weight)); }
        var dual = new AampObjectNode("output_double_0"); List("output_double_list").Objects.Add(dual);
        dual.Parameters.Add(AampParameterNode.Integer("connection_0", 0x10000)); dual.Parameters.Add(AampParameterNode.Single("weight_0", .25f));
        dual.Parameters.Add(AampParameterNode.Integer("connection_1", 0x10000)); dual.Parameters.Add(AampParameterNode.Single("weight_1", -2f));
        var main = new AampObjectNode("main_bone_0"); List("main_bone_list").Objects.Add(main);
        var support = new AampObjectNode("support_bone_0"); List("support_bone_list").Objects.Add(support);
        foreach (var (pose, bone) in new[] { (main, 1), (support, 2) })
        {
            pose.Parameters.Add(AampParameterNode.Integer("bone", bone)); pose.Parameters.Add(AampParameterNode.Integer("space", 0));
            pose.Parameters.Add(AampParameterNode.Vector3("base_translate", Vector3.Zero)); pose.Parameters.Add(AampParameterNode.Vector4("base_rotate", Vector4.UnitW));
            pose.Parameters.Add(AampParameterNode.Vector3("aim", Vector3.UnitZ)); pose.Parameters.Add(AampParameterNode.Vector3("up", Vector3.UnitY));
        }
        foreach (var (channel, handle) in new[] { ("translateX", 0), ("translateY", 1), ("translateZ", 2), ("roll", 3), ("bendH", 0x10000) })
            support.Parameters.Add(AampParameterNode.Integer(channel, handle));
        var source = BphyssbBridge.LoadBytes(WriteArchive("physsb", root), "weighted.bphyssb");
        var result = BphhbBridge.Export(source, Path.Combine(output, "weighted.bphhb"));
        CheckHelperCurves(source, result);
        Require(BphhbBridge.GetConversionWarnings(source).Count == 0, "Clamped helper curve received an extrapolation warning");
        curve.Parameters.Add(AampParameterNode.Boolean("constant_in", false));
        var extrapolated = BphyssbBridge.LoadBytes(WriteArchive("physsb", root), "extrapolated.bphyssb");
        Require(BphhbBridge.GetConversionWarnings(extrapolated).Count == 1, "Unverified helper extrapolation not disclosed");
    }

    private static void CheckHelperCurves(BphyssbDocumentSummary source, BphhbDocumentSummary target)
    {
        var a = source.Archive!.Root.Children.Single(c => c.FindChild("support_bone_list") != null);
        var b = target.Archive!.Root.Children.Single(c => c.FindChild("pose_driven_list") != null);
        var curves = b.FindChild("connection_curve_list")!.Objects;
        var outputs = b.FindChild("output_list")!.Objects;
        var readKeys = typeof(SidecarPreviewEvaluator).GetMethod("ReadKeys", BindingFlags.NonPublic | BindingFlags.Static)!;
        var eval = typeof(SidecarPreviewEvaluator).GetMethod("EvaluateCurve", BindingFlags.NonPublic | BindingFlags.Static)!;
        float TargetCurve(int id, float x) => id < 0 ? 0 : (float)eval.Invoke(null, [readKeys.Invoke(null, [curves[id]]), x])!;
        float SourceOutput(int handle, float x)
        {
            if (handle < 0) return 0;
            var dual = handle >= 0x10000;
            var o = a.FindChild(dual ? "output_double_list" : "output_single_list")!.Objects[handle & 0xffff];
            float Term(string connection, string weight)
            {
                var packed = o.Find(connection)?.AsInt32() ?? -1;
                return packed < 0 ? 0 : SidecarPreviewEvaluator.EvaluateBotwCurve(a.FindChild("connection_curve_list")!.Objects[packed & 0xffff], x) * (o.Find(weight)?.AsSingle() ?? 0);
            }
            return dual ? Term("connection_0", "weight_0") + Term("connection_1", "weight_1") : Term("connection", "weight");
        }
        var channels = new[] { "roll", "bendH", "bendV", "translateX", "translateY", "translateZ" };
        var hashes = new uint[] { 2869652774, 3079588026, 122218518, 3595547632, 1861473429, 2084993915 };
        var poses = b.FindChild("pose_driven_list")!.Objects;
        for (var i = 0; i < poses.Count; i++)
        for (var ch = 0; ch < 6; ch++)
        {
            var handle = a.FindChild("support_bone_list")!.Objects[i].Find(channels[ch])?.AsInt32() ?? -1;
            var targetId = poses[i].Find(hashes[ch])?.AsInt32() ?? -1;
            // Inside each source curve domain; extrapolation policies are separately documented.
            foreach (var x in new[] { -.5f, -.2f, 0f, .2f, .5f })
            {
                var actual = targetId < 0 ? 0 : TargetCurve(outputs[targetId].Find(2390703269u)?.AsInt32() ?? -1, x) + TargetCurve(outputs[targetId].Find(918772672u)?.AsInt32() ?? -1, x);
                var expected = SourceOutput(handle, x);
                Require(Math.Abs(expected - actual) < Math.Max(1e-5f, Math.Abs(expected) * 1e-5f), $"Helper weight/curve response changed: channel {ch}, {expected} vs {actual}");
            }
        }
    }
}
