using HKCLTool;
using HKX2;
using PhysicsTool.Core;
using System.Numerics;
using System.Reflection;

internal static class ConversionFidelityChecks
{
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run()
    {
        var session = new EditorSession(); session.New("Repeated sets", 2);
        var native = session.Document.CaptureBphclNativeDocument()!.Cloths[0];
        NativeBphclConstraintLink Local(float stiffness) => new(0, null, null,
            new Dictionary<string, double> { ["particleIndex"] = 0, ["referenceVertex"] = 0,
                ["shapeRadius"] = .1, ["stiffness"] = stiffness }, 0, new Dictionary<string, uint>());
        var first = new NativeBphclConstraintSet(0, "Same name", "hclLocalRangeConstraintSet", 0, 0, 0, 0, false,
            [], [], [Local(.2f), Local(.6f), Local(.2f)], [], [], [],
            new Dictionary<string, double> { ["stiffness"] = 1, ["referenceMeshBufferIdx"] = 0 });
        var second = first with { Index = 1, LocalStiffnessConstraints = [Local(.8f)] };
        NativeBphclConstraintSet Standard(int index, double length) => first with {
            Index = index, ClassName = "hclStandardLinkConstraintSet", LocalStiffnessConstraints = [],
            Links = [new(0, 0, 1, new Dictionary<string, double> { ["restLength"] = length, ["stiffness"] = .3 }, 0, new Dictionary<string, uint>())],
            Values = new Dictionary<string, double>() };
        var source = native.SimCloths[0] with { ConstraintSets = [first, second, Standard(2, 1), Standard(3, 2)] };
        var op = new hclSimulateOperator { m_constraintExecution = new() };
        var cloth = new hclClothData { m_simClothDatas = [new hclSimClothData {
            m_staticConstraintSets = new(), m_antiPinchConstraintSets = new() }], m_operators = [op] };
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        typeof(HkclService).GetMethod("ApplyBphclConstraintLinks", flags)!.Invoke(null,
            [cloth, source, null, "Repeated sets", false, 1f]);
        var sourceOp = native.Operators.First(o => o.ClassName == "hclSimulateOperator");
        var config = sourceOp.SimulateConfigs[0] with { ConstraintExecution = [1, -1, 0, 3, 2, 1] };
        typeof(HkclService).GetMethod("ApplyBphclConstraintExecution", flags)!.Invoke(null,
            [cloth, source, "Repeated sets", new[] { sourceOp with { SimulateConfigs = [config] } }]);
        var sets = cloth.m_simClothDatas[0].m_staticConstraintSets;
        Require(sets.Count == 6, "Same-family source sets were collapsed");
        Require(sets.OfType<hclLocalRangeConstraintSet>().Select(s => s.m_stiffness).SequenceEqual(new[] { .2f, .6f, .2f, .8f }),
            "Repeated local ranges lost individual stiffness/order");
        Require(sets.OfType<hclStandardLinkConstraintSet>().Select(s => s.m_links.Single().m_restLength).SequenceEqual(new[] { 1f, 2f }),
            "Same-family links overwrote one another");
        Require(op.m_constraintExecution.SequenceEqual(new[] { 3, -1, 0, 1, 2, 5, 4, 3 }),
            "Same-family solver passes targeted the wrong sets");
        var secondOperator = new hclSimulateOperator { m_constraintExecution = new() };
        cloth.m_operators.Add(secondOperator);
        typeof(HkclService).GetMethod("ApplyBphclConstraintExecution", flags)!.Invoke(null,
            [cloth, source, "Repeated sets", new[] { sourceOp with { SimulateConfigs = [config] },
                sourceOp with { Index = sourceOp.Index + 1, SimulateConfigs = [config with { ConstraintExecution = [3, -1, 0] }] } }]);
        Require(secondOperator.m_constraintExecution.SequenceEqual(new[] { 5, -1, 0, 1, 2 }), "Second simulation program lost");
        var applyConfig = typeof(HkclService).GetMethod("ApplyBphclSimulateOperator", flags)!;
        applyConfig.Invoke(null, [op, sourceOp with { SimulateConfigs = [config, config] }]);
        var rejected = false;
        try { applyConfig.Invoke(null, [op, sourceOp with { SimulateConfigs = [config,
            config with { NumberOfSolveIterations = checked((byte)(config.NumberOfSolveIterations + 1)) }] }]); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
        Require(rejected, "Distinct per-state solver settings silently flattened");
        var deformA = new hclSimpleMeshBoneDeformOperator();
        var deformB = new hclSimpleMeshBoneDeformOperator();
        cloth.m_operators.Add(deformA); cloth.m_operators.Add(deformB);
        var transform = Matrix4x4.CreateTranslation(1, 2, 3);
        var deformSource = new NativeBphclSimpleMeshBoneDeform(2, 0, 0, 1, 0,
            [new NativeBphclTriangleBonePair(64, 0)], [Matrix4x4.Identity], 3);
        typeof(HkclService).GetMethod("ApplyBphclSimpleMeshBoneDeform", flags)!.Invoke(null,
            [cloth, new[] { deformSource, deformSource with { OperatorIndex = 3, InputBufferIndex = 2,
                TriangleBonePairs = [new NativeBphclTriangleBonePair(128, 6)], LocalBoneTransforms = [transform] } },
                new Dictionary<int, int> { [1] = 3, [2] = 4 }]);
        Require(deformA.m_triangleBonePairs.Single().m_boneOffset == 192 &&
            deformB.m_triangleBonePairs.Single().m_boneOffset == 256 &&
            deformB.m_triangleBonePairs.Single().m_triangleOffset == 6 &&
            deformB.m_localBoneTransforms.Single() == transform && deformB.m_inputBufferIdx == 2,
            "Additional mesh-to-bone output operator lost its source bindings");

        foreach (var rotation in new[] { Quaternion.Identity, Quaternion.CreateFromYawPitchRoll(.3f, .7f, -.4f) })
        foreach (var limit in new[] { .5f, 1.01f })
        {
            var preview = new ParticlePreviewData { Gravity = Vector3.Zero };
            var points = new[] { new Vector3(-1, 1, 0), new Vector3(-1, -1, 0), Vector3.Zero, Vector3.UnitX };
            for (var i = 0; i < points.Length; i++) preview.Particles.Add(new() { Index = i,
                Position = Vector3.Transform(points[i], rotation), InverseMass = 1 });
            var bend = new ParticlePreviewBendStiffness { ConstraintSetIndex = 0, ParticleA = 0, ParticleB = 1,
                ParticleC = 2, ParticleD = 3, WeightA = .5f, WeightB = .5f, WeightC = -.5f, WeightD = -.5f,
                BendStiffness = -.1f, RestCurvature = 1, UseRestPoseConfig = true,
                ClampBendStiffness = true, MaxRestPoseHeightSq = limit };
            preview.BendStiffnessConstraints.Add(bend);
            var solver = new HkclPreviewSimulator(preview);
            typeof(HkclPreviewSimulator).GetMethod("SolveBendStiffness", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(solver, [0]);
            var moved = solver.GetPositions().Any(p => Vector3.Distance(p.Value, preview.Particles[p.Key].Position) > 1e-5f);
            Require(moved == (limit > 1), "TotK curvature cutoff does not match native threshold/rotation behavior");
            bend.ClampBendStiffness = false;
            solver = new(preview);
            typeof(HkclPreviewSimulator).GetMethod("SolveBendStiffness", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(solver, [0]);
            Require(solver.GetPositions().Any(p => Vector3.Distance(p.Value, preview.Particles[p.Key].Position) > 1e-5f),
                "BotW/unclamped bending was incorrectly suppressed");
        }
        Console.WriteLine("PASS: distinct same-family sets, split-set execution identity, native TotK bend cutoff and unclamped BotW path.");
    }
}
