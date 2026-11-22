using BymlLibrary;
using Revrs;
using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

internal static class OotHelperSimulationChecks
{
    internal static void Run()
    {
        AffineBones();
        UndrivenChannels();
        NativeOrder();
        Byml Vector(float x, float y, float z) => new(new Dictionary<string, Byml> { ["X"] = new(x), ["Y"] = new(y), ["Z"] = new(z) });
        Byml Basis(int bone) => new(new Dictionary<string, Byml> {
            ["BoneNameId"] = new(bone), ["BaseBoneNameId"] = new(-2),
            ["BaseTranslate"] = Vector(0, 0, 0), ["BaseRotate"] = new(new List<Byml> { new(0f), new(0f), new(0f), new(1f) }),
            ["AimAxis"] = Vector(0, 0, 1), ["UpAxis"] = Vector(0, 1, 0)
        });
        var driven = Basis(1);
        driven.GetMap()["TranslateX"] = new(new Dictionary<string, Byml> { ["InConnections"] = new(new List<Byml> {
            new(new Dictionary<string, Byml> { ["DriverParamId"] = new(0), ["DriverAttribute"] = new(0x509c8278u),
                ["DrivenCurve"] = new(new Dictionary<string, Byml> { ["Type"] = new("Hermit2DSplit"),
                    ["Data"] = new(new List<Byml> { new(-10f), new(2f), new(0f), new(0f), new(10f), new(2f), new(0f), new(0f) }) }) }) }) });
        var root = new Byml(new Dictionary<string, Byml> {
            ["BoneNames"] = new(new List<Byml> { new("Root"), new("Helper"), new("Collider") }),
            ["DriverParams"] = new(new List<Byml> { Basis(2) }), ["DrivenParams"] = new(new List<Byml> { driven }) });
        var bytes = root.ToBinary(Endianness.Little, 7);
        foreach (var order in new[] { new[] { 0, 1, 2, 3 }, new[] { 3, 2, 1, 0 } })
        {
            var bones = new VisualModelBone[] {
                new() { Index = 0, ParentIndex = -1, Name = "Root" },
                new() { Index = 1, ParentIndex = 0, Name = "Helper" },
                new() { Index = 2, ParentIndex = 1, Name = "Collider", Position = new(0, 3, 0) },
                new() { Index = 3, ParentIndex = 2, Name = "Tip", Position = new(0, 4, 0) }
            };
            var pose = new VisualModelData(); pose.Bones.AddRange(order.Select(i => bones[i]));
            var evaluator = new OotHelperEvaluator(bytes, pose.Bones);
            var evaluated = evaluator.Evaluate(pose).Bones.ToDictionary(b => b.Name);
            if (Vector3.Distance(evaluated["Helper"].Position, new(2, 0, 0)) > 1e-5f ||
                Vector3.Distance(evaluated["Collider"].Position, new(2, 3, 0)) > 1e-5f ||
                Vector3.Distance(evaluated["Tip"].Position, new(2, 4, 0)) > 1e-5f)
                throw new Exception("Helper descendant driver cached a provisional world pose");
            if (pose.Bones.Single(b => b.Name == "Collider").Position != new Vector3(0, 3, 0)) throw new Exception("Helper modified source pose");
            var preview = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental, Gravity = Vector3.Zero };
            preview.Particles.Add(new() { Index = 0, Position = new(2.5f, 3, 0), Mass = 1, InverseMass = 1, CollisionMask = 1 });
            preview.Colliders.Add(new() { Index = 0, BoneIndex = 2, CollisionBit = 0, Kind = ColliderPreviewKind.Sphere,
                Start = new(0, 3, 0), Radius = 1 });
            var solver = new HkclPreviewSimulator(preview);
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> {
                [2] = Matrix4x4.CreateTranslation(evaluated["Collider"].Position - new Vector3(0, 3, 0)) });
            solver.Reset();
            solver.Step(1f / 60);
            if (Vector3.Distance(solver.GetColliderPoses()[0].Start, new(2, 3, 0)) > 1e-5f ||
                Vector3.Distance(solver.GetPositions()[0], new(3, 3, 0)) > 1e-5f)
                throw new Exception("Cloth did not collide at the helper descendant's evaluated pose");
        }
        Console.WriteLine("PASS: helper descendant drivers, child collider inheritance/contact, reordered skeleton and source isolation");
        Collisions();
    }

    private static void UndrivenChannels()
    {
        Byml Vector(Vector3 v) => new(new Dictionary<string, Byml> { ["X"] = new(v.X), ["Y"] = new(v.Y), ["Z"] = new(v.Z) });
        var parent = Matrix4x4.CreateRotationZ(.3f) * Matrix4x4.CreateTranslation(10, 2, 3);
        var position = new Vector3(2, 3, 4);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .2f);
        var scale = new Vector3(1.3f, .9f, 1.1f);
        var local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        var pose = new VisualModelData();
        pose.Bones.Add(VisualBoneTransform.Bone(0, -1, "Root", parent));
        pose.Bones.Add(VisualBoneTransform.Bone(1, 0, "Helper", local * parent));
        Byml Basis(int bone) => new(new Dictionary<string, Byml> {
            ["BoneNameId"] = new(bone), ["BaseBoneNameId"] = new(-2),
            ["BaseTranslate"] = Vector(new(9, 8, 7)),
            ["BaseRotate"] = new(new List<Byml> { new(0f), new(0f), new(0f), new(1f) }),
            ["AimAxis"] = Vector(Vector3.UnitZ), ["UpAxis"] = Vector(Vector3.UnitY)
        });
        foreach (int group in new[] { -1, 0, 3, 6 })
        {
            var driven = Basis(1);
            for (int c = 0; c < 9; c++)
                driven.GetMap()[OotHelperDocument.Channels[c]] = new(new Dictionary<string, Byml> {
                    ["StaticValue"] = new(c >= 6 ? 1f : 0f), ["InConnections"] = new(new List<Byml>()) });
            if (group >= 0)
            {
                driven.GetMap()[OotHelperDocument.Channels[group]].GetMap()["InConnections"] = new(new List<Byml> {
                    new(new Dictionary<string, Byml> { ["DriverParamId"] = new(0), ["DriverAttribute"] = new(0x509c8278u),
                        ["DrivenCurve"] = new(new Dictionary<string, Byml> { ["Type"] = new("Hermit2DSplit"),
                            ["Data"] = new(new List<Byml> { new(-10f), new(.4f), new(0f), new(0f), new(10f), new(.4f), new(0f), new(0f) }) }) }) });
                driven.GetMap()[OotHelperDocument.Channels[group + 1]].GetMap()["StaticValue"] = new(.7f);
            }
            var bytes = new Byml(new Dictionary<string, Byml> {
                ["BoneNames"] = new(new List<Byml> { new("Root"), new("Helper") }),
                ["DriverParams"] = new(new List<Byml> { Basis(0) }),
                ["DrivenParams"] = new(new List<Byml> { driven }) }).ToBinary(Endianness.Little, 7);
            var result = new OotHelperEvaluator(bytes, pose.Bones).Evaluate(pose);
            var expectedRotation = group == 0 ? SidecarPreviewEvaluator.BuildBotwRotation(.4f, .7f, 0, Vector3.UnitZ, Vector3.UnitY) : rotation;
            var expectedScale = group == 6 ? new Vector3(.4f, .7f, 1) : scale;
            var expectedPosition = group == 3 ? new Vector3(9.4f, 8.7f, 7) : position;
            var expected = Matrix4x4.CreateScale(expectedScale) * Matrix4x4.CreateFromQuaternion(expectedRotation)
                * Matrix4x4.CreateTranslation(expectedPosition) * parent;
            foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                if (Vector3.Distance(Vector3.Transform(point, result.Bones[1].WorldMatrix), Vector3.Transform(point, expected)) > 1e-4f)
                    throw new Exception($"Native helper group {group} replaced an undriven pose component or lost an active group's static value.");
            if (pose.Bones[1].WorldMatrix != local * parent) throw new Exception("Helper group evaluation modified the input.");
        }
        Console.WriteLine("PASS: native helper TRS group gating, animation preservation, static sibling values and source isolation");
    }

    private static void NativeOrder()
    {
        Byml Vector(float x, float y, float z) => new(new Dictionary<string, Byml> { ["X"] = new(x), ["Y"] = new(y), ["Z"] = new(z) });
        Byml Basis(int bone) => new(new Dictionary<string, Byml> {
            ["BoneNameId"] = new(bone), ["BaseBoneNameId"] = new(-2),
            ["BaseTranslate"] = Vector(0, 0, 0), ["BaseRotate"] = new(new List<Byml> { new(0f), new(0f), new(0f), new(1f) }),
            ["AimAxis"] = Vector(0, 0, 1), ["UpAxis"] = Vector(0, 1, 0) });
        Byml Channel(int driver, uint attribute, float? constant = null) => new(new Dictionary<string, Byml> {
            ["InConnections"] = new(new List<Byml> { new(new Dictionary<string, Byml> {
                ["DriverParamId"] = new(driver), ["DriverAttribute"] = new(attribute),
                ["DrivenCurve"] = new(new Dictionary<string, Byml> { ["Type"] = new("Hermit2DSplit"),
                    ["Data"] = new(new List<Byml> { new(-10f), new(constant ?? -10f), new(constant.HasValue ? 0f : 1f), new(constant.HasValue ? 0f : 1f),
                        new(10f), new(constant ?? 10f), new(constant.HasValue ? 0f : 1f), new(constant.HasValue ? 0f : 1f) }) }) }) }) });
        var pose = new VisualModelData();
        pose.Bones.Add(new() { Index = 0, ParentIndex = -1, Name = "Root" });
        pose.Bones.Add(new() { Index = 1, ParentIndex = 0, Name = "Early" });
        pose.Bones.Add(new() { Index = 2, ParentIndex = 0, Name = "Producer", Position = Vector3.UnitX });
        pose.Bones.Add(new() { Index = 3, ParentIndex = 0, Name = "Late" });
        var early = Basis(1); var producer = Basis(2); var late = Basis(3);
        early.GetMap()["TranslateX"] = Channel(0, 0xa13adc76u);
        producer.GetMap()["TranslateX"] = Channel(1, 0xa13adc76u, 2);
        late.GetMap()["TranslateX"] = Channel(0, 0xa13adc76u);
        var root = new Byml(new Dictionary<string, Byml> {
            ["BoneNames"] = new(new List<Byml> { new("Root"), new("Early"), new("Producer"), new("Late") }),
            ["DriverParams"] = new(new List<Byml> { Basis(2), Basis(0) }),
            ["DrivenParams"] = new(new List<Byml> { producer, early, late }) });
        foreach (bool topology in new[] { true, false })
        foreach (bool reversed in new[] { true, false })
        {
            root.GetMap()["UseBoneNamesOrderAsTopology"] = new(topology);
            var input = new VisualModelData();
            input.Bones.AddRange(reversed ? pose.Bones.AsEnumerable().Reverse() : pose.Bones);
            var result = new OotHelperEvaluator(root.ToBinary(Endianness.Little, 7), input.Bones).Evaluate(input).Bones.ToDictionary(b => b.Name);
            float expected = topology ? 1 : 2;
            if (MathF.Abs(result["Early"].Position.X - expected) > 1e-5f || MathF.Abs(result["Late"].Position.X - expected) > 1e-5f ||
                MathF.Abs(result["Producer"].Position.X - 2) > 1e-5f)
                throw new Exception("Helper order or first-use group cache differs from native traversal.");
        }
        // Sampling rotation must not freeze translation or local scale too early.
        early.GetMap()["TranslateX"] = Channel(0, 0x7273210au);
        producer.GetMap()["ScaleX"] = Channel(1, 0xa13adc76u, 3);
        late.GetMap()["TranslateY"] = Channel(0, 0x4ee71a4fu);
        root.GetMap()["UseBoneNamesOrderAsTopology"] = new(true);
        var scaled = new VisualModelData();
        var parent = Matrix4x4.CreateScale(2);
        foreach (var bone in pose.Bones)
            scaled.Bones.Add(VisualBoneTransform.Bone(bone.Index, bone.ParentIndex, bone.Name, bone.WorldMatrix * parent));
        var updated = new OotHelperEvaluator(root.ToBinary(Endianness.Little, 7), scaled.Bones).Evaluate(scaled).Bones.ToDictionary(b => b.Name);
        if (Vector3.Distance(updated["Late"].Position, new(4, 6, 0)) > 1e-5f)
            throw new Exception("Native driver groups were cached together or scale was not sampled in local space.");
        if (pose.Bones[2].Position != Vector3.UnitX) throw new Exception("Native-order evaluation modified its source.");
        Console.WriteLine("PASS: native authored/helper-list order, lazy TRS driver caches and local scale sampling");
    }

    private static void AffineBones()
    {
        var parent = Matrix4x4.CreateScale(.65f, 1.2f, 1) * Matrix4x4.CreateRotationZ(.3f) * Matrix4x4.CreateTranslation(1, 2, 3);
        var child = Matrix4x4.CreateRotationY(.7f) * Matrix4x4.CreateTranslation(0, 1, 0) * parent;
        if (Matrix4x4.Decompose(child, out _, out _, out _)) throw new Exception("Shear fixture unexpectedly decomposes.");
        var pose = new VisualModelData();
        pose.Bones.Add(VisualBoneTransform.Bone(0, -1, "Parent", parent));
        pose.Bones.Add(VisualBoneTransform.Bone(1, 0, "Child", child));
        var bytes = new Byml(new Dictionary<string, Byml> {
            ["BoneNames"] = new(new List<Byml>()), ["DriverParams"] = new(new List<Byml>()), ["DrivenParams"] = new(new List<Byml>())
        }).ToBinary(Endianness.Little, 7);
        var result = new OotHelperEvaluator(bytes, pose.Bones).Evaluate(pose);
        var overrides = PhysicsTool.Desktop.Rendering.OotActorPreview.InheritOutputs(result, new Dictionary<string, VisualBoneOverride>());
        foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            if (Vector3.Distance(Vector3.Transform(point, overrides[1].WorldMatrix), Vector3.Transform(point, child)) > .00001f)
                throw new Exception("Helper or inheritance discarded affine shear.");
        if (pose.Bones[1].WorldMatrix != child || Math.Abs(result.Bones[1].Rotation.LengthSquared() - 1) > .00001f)
            throw new Exception("Affine output mutated the input or has an invalid display rotation.");
        var bad = child; bad.M12 = float.NaN;
        try { VisualBoneTransform.Override("Invalid", bad); throw new Exception("Nonfinite affine accepted."); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS: nonuniform parent scale/shear survives helper evaluation and model overrides; invalid matrices still rejected.");
    }

    private static void Collisions()
    {
        foreach (var kind in new[] { ColliderPreviewKind.Sphere, ColliderPreviewKind.Capsule })
        foreach (var profile in new[] { ClothSolverProfile.OotExperimental, ClothSolverProfile.BotwPreview, ClothSolverProfile.TotkExperimental })
        {
            var source = new ParticlePreviewData { SolverProfile = profile, Gravity = Vector3.Zero, PinchDetectionEnabled = true };
            source.Particles.Add(new() { Index = 0, Position = new(.5f, 0, 0), Mass = 1, InverseMass = 1, CollisionMask = 3 });
            source.Particles.Add(new() { Index = 1, Position = new(.5f, 0, 0), Mass = 1, InverseMass = 1, CollisionMask = 0 });
            source.Particles.Add(new() { Index = 2, Position = new(.5f, 0, 0), Fixed = true, CollisionMask = 3 });
            for (int slot = 0; slot < 2; slot++)
            {
                source.Colliders.Add(new() { Index = slot, CollisionBit = slot, Kind = kind, Radius = 1,
                    Start = kind == ColliderPreviewKind.Sphere ? Vector3.Zero : -Vector3.UnitY, End = Vector3.UnitY });
                source.ColliderPinching.Add(new(slot, true, 0, 0));
            }
            source.PinchEnabledParticles.UnionWith([0, 1, 2]);
            var solver = new HkclPreviewSimulator(source);
            solver.Step(1f / 60);
            float expected = profile == ClothSolverProfile.OotExperimental ? 1 : .5f;
            if (MathF.Abs(solver.GetPositions()[0].X - expected) > 1e-5f)
                throw new Exception($"{profile} overlapping {kind} contact selection");
            if (solver.GetPositions()[1] != new Vector3(.5f, 0, 0) || solver.GetPositions()[2] != new Vector3(.5f, 0, 0))
                throw new Exception("Overlap contact moved a masked particle or fixed anchor");
        }
        Console.WriteLine("PASS: OoT overlapping sphere/capsule contacts, masks, fixed anchors and BotW/TotK isolation");
    }
}
