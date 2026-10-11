using System.Collections;
using System.Numerics;
using System.Reflection;

namespace HKCLTool;

internal static class NativeClothSolverChecks
{
    private const float Dt = 1f / 60;
    public static void Run(string directory)
    {
        CheckLinks();
        CheckCurvature();
        CheckContacts();
        CheckPinchContacts();
        CheckInputInterpolation();
        CheckNativeInputSampling();
        CheckLocalRangesAndExecution();
        CheckConstraintImport();
        ClothSkinReferenceChecks.Run(directory);
        File.WriteAllText(Path.Combine(directory, "native-cloth.txt"),
            "PASS: directed stretch, mass-scaled standard links, constraint velocity, native substep scaling, current-frame curvature/rotation invariance, surface projection, opt-in pinch priorities/ties/margins/masks/fallbacks, translating/rotating collider friction, offset collider origins and linear substep motion, rotating multi-bone world-space anchor interpolation, zero fixed-input Verlet velocity, frame-stable local-range reference buffers, legacy input compatibility, local-range timing/velocity, authored collision order/repeated constraints, import preservation, and reset history.");
    }

    private static ParticlePreviewData Pair(string kind, float strength = 1f)
    {
        var data = new ParticlePreviewData { Gravity = Vector3.Zero };
        data.Particles.Add(new() { Index = 0, Position = Vector3.Zero, InverseMass = 0.25f });
        data.Particles.Add(new() { Index = 1, Position = Vector3.UnitX * 2, InverseMass = 0.75f });
        data.ConstraintSets.Add(new() { Index = 0, Kind = kind });
        data.Links.Add(new() { ConstraintSetIndex = 0, ParticleA = 0, ParticleB = 1, RestLength = 1f, Stiffness = strength });
        data.ConstraintExecution.Add(0);
        return data;
    }

    private static void CheckLinks()
    {
        var stretch = new HkclPreviewSimulator(Pair("hclStretchLinkConstraintSet", 0.5f));
        stretch.Step(Dt);
        Near(stretch.GetPositions()[0], Vector3.Zero, "Stretch moved its anchor.");
        Near(stretch.GetPositions()[1], Vector3.UnitX * 1.5f, "Stretch used normalized mass weights or clipped projection.");
        Near(Previous(stretch, 1), Vector3.UnitX * 2, "Structural projection incorrectly damped velocity.");
        var standard = new HkclPreviewSimulator(Pair("hclStandardLinkConstraintSet"));
        standard.Step(Dt);
        Near(standard.GetPositions()[0], Vector3.UnitX * 0.25f, "Standard endpoint A differs from native coefficient.");
        Near(standard.GetPositions()[1], Vector3.UnitX * 1.25f, "Standard endpoint B differs from native coefficient.");

        var data = Pair("hclStandardLinkConstraintSet", 0.2f);
        data.Particles[1].Position = Vector3.UnitX * 1.1f;
        data.SimulationSubSteps = 4; data.SimulationSolveIterations = 3; data.AdaptConstraintStiffness = true;
        var expectedA = Vector3.Zero; var expectedB = Vector3.UnitX * 1.1f;
        var previousA = expectedA; var previousB = expectedB;
        var coefficient = 0.2f * MathF.Pow(4, -1.725f);
        for (var substep = 0; substep < 4; substep++)
        {
            (expectedA, previousA) = (2 * expectedA - previousA, expectedA);
            (expectedB, previousB) = (2 * expectedB - previousB, expectedB);
            for (var iteration = 0; iteration < 3; iteration++)
            {
                var delta = expectedB - expectedA;
                var correction = Vector3.Normalize(delta) * (delta.Length() - 1) * coefficient;
                expectedA += correction * 0.25f; expectedB -= correction * 0.75f;
            }
        }
        var adapted = new HkclPreviewSimulator(data); adapted.Step(Dt);
        Near(adapted.GetPositions()[0], expectedA, "Adaptation incorrectly depends on solve iterations.");
        Near(adapted.GetPositions()[1], expectedB, "Native adaptation exponent differs.");
    }

    private static ParticlePreviewData Bend(bool restPose)
    {
        var data = new ParticlePreviewData { Gravity = Vector3.Zero };
        var positions = new[] { new Vector3(0, 1, 0.25f), new Vector3(0, -1, 0.25f), new Vector3(-1, 0, 0), new Vector3(1, 0, 0) };
        for (var i = 0; i < 4; i++) data.Particles.Add(new() { Index = i, Position = positions[i], InverseMass = 1 });
        data.ConstraintSets.Add(new() { Index = 0, Kind = "hclBendStiffnessConstraintSet" });
        data.ConstraintExecution.Add(0);
        data.BendStiffnessConstraints.Add(new() { ConstraintSetIndex = 0, ParticleA = 0, ParticleB = 1, ParticleC = 2, ParticleD = 3,
            WeightA = 1, WeightB = 1, WeightC = -1, WeightD = -1, BendStiffness = -0.1f, UseRestPoseConfig = restPose, RestCurvature = -0.5f / 1.0625f });
        return data;
    }

    private static void CheckCurvature()
    {
        var flat = Bend(false); var solver = new HkclPreviewSimulator(flat); solver.Step(Dt);
        Near(solver.GetPositions()[0], flat.Particles[0].Position - Vector3.UnitZ * 0.05f, "Flat bending retained the world-space rest vector.");
        var curved = Bend(true); solver = new(curved); solver.Step(Dt);
        for (var i = 0; i < 4; i++) Near(solver.GetPositions()[i], curved.Particles[i].Position, "Authored rest curvature did not preserve its shape.");
        foreach (var restPose in new[] { false, true })
        {
            var source = Bend(restPose); var rotated = Bend(restPose);
            var transform = Matrix4x4.CreateFromYawPitchRoll(0.7f, -0.9f, 0.25f) * Matrix4x4.CreateTranslation(0.2f, 0.4f, -0.3f);
            var originalSolver = new HkclPreviewSimulator(source);
            var rotatedSolver = new HkclPreviewSimulator(rotated);
            for (var i = 0; i < 4; i++) SetParticle(rotatedSolver, i, Vector3.Transform(source.Particles[i].Position, transform));
            originalSolver.Step(Dt); rotatedSolver.Step(Dt);
            for (var i = 0; i < 4; i++) Near(rotatedSolver.GetPositions()[i], Vector3.Transform(originalSolver.GetPositions()[i], transform), "Bending is not rigid-transform invariant.");
        }
    }

    private static ParticlePreviewData Contact(float friction)
    {
        var data = new ParticlePreviewData { Gravity = new(0, -9.81f, 0) };
        data.Particles.Add(new() { Index = 0, Position = new(1, 0.02f, 0), Radius = 0.02f, InverseMass = 1, Friction = friction, CollisionMask = 1 });
        data.Bones.Add(new() { Index = 0, ParentIndex = -1, Position = Vector3.Zero, AxisX = Vector3.UnitX, AxisY = Vector3.UnitY, AxisZ = Vector3.UnitZ });
        data.Colliders.Add(new() { Index = 0, BoneIndex = 0, CollisionBit = 0, Kind = ColliderPreviewKind.Plane, Start = Vector3.Zero, PlaneNormal = Vector3.UnitY });
        return data;
    }

    private static void CheckContacts()
    {
        foreach (var friction in new[] { 0f, 0.5f, 1f })
        {
            var data = Contact(friction); var solver = new HkclPreviewSimulator(data);
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.Identity }); solver.Reset();
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.CreateTranslation(Dt, 0, 0) }); solver.Step(Dt);
            Near(solver.GetPositions()[0], data.Particles[0].Position, "Contact did not project completely onto the surface.");
            Near((solver.GetPositions()[0] - Previous(solver, 0)) / Dt, Vector3.UnitX * friction, "Moving surface friction differs from native relative motion.");
        }
        var rotating = new HkclPreviewSimulator(Contact(1));
        rotating.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.Identity }); rotating.Reset();
        rotating.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.CreateRotationY(0.03f) }); rotating.Step(Dt);
        Near((rotating.GetPositions()[0] - Previous(rotating, 0)) / Dt, new Vector3(0, 0, -0.03f / Dt), "Rotating surface friction ignored angular velocity.", 0.0001f);
        rotating.Reset(); rotating.Step(Dt);
        Near(rotating.GetPositions()[0] - Previous(rotating, 0), Vector3.Zero, "Reset left stale collider velocity.");

        var offsetContact = Contact(1);
        offsetContact.Colliders[0].TransformOrigin = Vector3.UnitX;
        var offsetSolver = new HkclPreviewSimulator(offsetContact);
        offsetSolver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.Identity }); offsetSolver.Reset();
        const float angle = 0.6f;
        var target = Matrix4x4.CreateRotationY(angle);
        offsetSolver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = target }); offsetSolver.Step(Dt);
        var origin = Vector3.Transform(Vector3.UnitX, target);
        var expectedVelocity = (origin - Vector3.UnitX) / Dt
            + Vector3.Cross(Vector3.UnitY * (angle / Dt), Vector3.UnitX - origin);
        Near((offsetSolver.GetPositions()[0] - Previous(offsetSolver, 0)) / Dt, expectedVelocity,
            "Offset collider friction used the bone pivot instead of the authored collider origin.", 0.0002f);
        offsetSolver.Reset(); offsetSolver.Step(Dt);
        Near(offsetSolver.GetPositions()[0] - Previous(offsetSolver, 0), Vector3.Zero, "Offset collider reset retained surface motion.");

        var sampleSolver = new HkclPreviewSimulator(offsetContact);
        sampleSolver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.Identity }); sampleSolver.Reset();
        var inputs = new Dictionary<int, Matrix4x4> { [0] = target };
        var methods = BindingFlags.Instance | BindingFlags.NonPublic;
        var motions = (System.Collections.IDictionary)typeof(HkclPreviewSimulator).GetMethod("BuildColliderMotions", methods)!
            .Invoke(sampleSolver, new object[] { inputs, Dt })!;
        foreach (var fraction in new[] { 0.25f, 0.5f, 0.75f, 1f })
        {
            typeof(HkclPreviewSimulator).GetMethod("SampleInputPose", methods)!.Invoke(sampleSolver, new object[] { inputs, fraction });
            var sampled = (ColliderPreviewShape)typeof(HkclPreviewSimulator).GetMethod("GetSubstepCollider", methods)!
                .Invoke(sampleSolver, new object[] { offsetContact.Colliders[0], motions.Values.Cast<object>().Single(), fraction })!;
            Near(sampled.TransformOrigin!.Value, Vector3.Lerp(Vector3.UnitX, origin, fraction), "Collider origin followed a bone arc instead of linear native substeps.");
            var rotatedLocalPoint = Vector3.Transform(-Vector3.UnitX, Matrix4x4.CreateRotationY(angle * fraction));
            Near(sampled.Start - sampled.TransformOrigin.Value, rotatedLocalPoint, "Collider substeps altered the authored shape offset.");
        }
        Near(offsetContact.Colliders[0].TransformOrigin!.Value, Vector3.UnitX, "Collider substeps mutated source geometry.");
    }

    private static void CheckInputInterpolation()
    {
        var data = Pair("hclStretchLinkConstraintSet");
        data.Particles[0].Fixed = true; data.Particles[1].Position = Vector3.UnitX;
        data.SimulationSubSteps = 4;
        data.ParticleAttachments.Add(new() { ParticleIndex = 0, BoneWeights = { new() { BoneIndex = 0, Weight = 1 } } });
        var solver = new HkclPreviewSimulator(data);
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.Identity }); solver.Reset();
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.CreateTranslation(0, 0.1f, 0) }); solver.Step(Dt);
        Near(solver.GetPositions()[0], new(0, 0.1f, 0), "Fixed particles missed final animated input.");
        Near(Previous(solver, 0), new(0, 0.075f, 0), "Fixed particles teleported instead of following substeps.");
    }

    private static void CheckNativeInputSampling()
    {
        var data = Pair("hclStretchLinkConstraintSet", 0.25f);
        data.UseNativeHkclInputSampling = true;
        data.SimulationSubSteps = 4;
        var referenceA = new Vector3(0.12f, 0.02f, 0);
        var referenceB = new Vector3(-0.02f, 0.08f, 0.01f);
        var start = referenceA * 0.7f + referenceB * 0.3f;
        data.Particles[0].Fixed = true;
        data.Particles[0].Position = start;
        data.Particles[1].Position = start + Vector3.UnitX * 0.06f;
        data.Links[0].RestLength = 0.06f;
        data.ParticleAttachments.Add(new() { ParticleIndex = 0, BoneWeights =
        {
            new() { BoneIndex = 0, Weight = 0.7f, ReferencePosition = referenceA },
            new() { BoneIndex = 1, Weight = 0.3f, ReferencePosition = referenceB }
        } });
        var solver = new HkclPreviewSimulator(data);
        solver.Reset();
        var expected = data.Particles[1].Position;
        var previous = expected;
        foreach (var angle in new[] { 0.7f, -0.25f, 0f })
        {
            var a = Matrix4x4.CreateRotationZ(angle) * Matrix4x4.CreateTranslation(0.01f, 0, 0);
            var b = Matrix4x4.CreateRotationX(-angle * 0.5f);
            var end = Vector3.Transform(referenceA, a) * 0.7f + Vector3.Transform(referenceB, b) * 0.3f;
            for (var step = 1; step <= 4; step++)
            {
                (expected, previous) = (2 * expected - previous, expected);
                var anchor = Vector3.Lerp(start, end, step / 4f);
                var offset = expected - anchor;
                var length = offset.Length();
                if (length > 0.06f) expected -= offset * ((length - 0.06f) / length * 0.25f);
            }
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = a, [1] = b });
            solver.Step(Dt);
            Near(solver.GetPositions()[1], expected, "Rotating multi-bone anchors did not follow the native world-space chord.");
            Near(solver.GetPositions()[0], end, "Native fixed input missed the final pose.");
            Near(Previous(solver, 0), end, "Native fixed input retained artificial Verlet velocity.");
            start = end;
        }
        solver.Reset();
        solver.Step(Dt);
        Near(solver.GetPositions()[0], start, "Reset reused a stale native anchor interval.");
        Near(Previous(solver, 0), start, "Reset injected fixed-particle motion.");

        foreach (var substeps in new[] { 1, 4 })
        foreach (var adaptive in new[] { false, true })
        {
            data = new ParticlePreviewData { Gravity = Vector3.Zero, UseNativeHkclInputSampling = true,
                SimulationSubSteps = substeps, AdaptConstraintStiffness = adaptive };
            data.Particles.Add(new() { Index = 0, InverseMass = 1 });
            data.ConstraintSets.Add(new() { Index = 0, Kind = "hclLocalRangeConstraintSet" });
            var reference = new Vector3(0.02f, 0.01f, 0);
            data.LocalRanges.Add(new() { ConstraintSetIndex = 0, ParticleIndex = 0,
                MaximumDistance = 0.005f, Stiffness = 0.3f, ReferenceBoneWeights =
                { new() { BoneIndex = 0, Weight = 1, ReferencePosition = reference } } });
            solver = new(data); solver.Reset();
            var pose = Matrix4x4.CreateRotationZ(0.8f) * Matrix4x4.CreateTranslation(0.01f, 0, 0);
            var center = Vector3.Transform(reference, pose);
            expected = previous = Vector3.Zero;
            for (var step = 0; step < substeps; step++)
            {
                (expected, previous) = (2 * expected - previous, expected);
                if (adaptive && step != substeps - 1) continue;
                var offset = expected - center;
                var length = offset.Length();
                if (length <= 0.005f) continue;
                var correction = offset * ((0.005f - length) / length * 0.3f);
                expected += correction;
                if (adaptive && substeps > 1) previous += correction;
            }
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = pose });
            solver.Step(Dt);
            Near(solver.GetPositions()[0], expected, "Local-range reference buffer was incorrectly re-skinned during simulation.");
            Near(Previous(solver, 0), previous, "Native local-range input changed velocity preservation.");
        }
    }

    private static void CheckPinchContacts()
    {
        ParticlePreviewData Create(bool capsule = false)
        {
            var data = new ParticlePreviewData { Gravity = Vector3.Zero, PinchDetectionEnabled = true };
            data.Particles.Add(new() { Index = 0, Radius = 0.1f, InverseMass = 1, CollisionMask = 3, Friction = 1 });
            data.PinchEnabledParticles.Add(0);
            for (var i = 0; i < 2; i++)
            {
                var center = new Vector3(i == 0 ? -0.9f : 0.9f, 0, 0);
                data.Colliders.Add(new() { Index = i, CollisionBit = i, BoneIndex = i,
                    Kind = capsule ? ColliderPreviewKind.Capsule : ColliderPreviewKind.Sphere,
                    Start = center - (capsule ? Vector3.UnitY : Vector3.Zero), End = center + Vector3.UnitY, Radius = 1 });
                data.ColliderPinching.Add(new(i, true, i == 0 ? 2 : 1, 0.1f));
            }
            return data;
        }
        foreach (var capsule in new[] { false, true })
        {
            var data = Create(capsule); var solver = new HkclPreviewSimulator(data); solver.Step(Dt);
            Near(solver.GetPositions()[0], new(0.2f, 0, 0), "Pinch response ignored the higher-priority contact.");
            data.Colliders.Reverse(); solver = new(data); solver.Step(Dt);
            Near(solver.GetPositions()[0], new(0.2f, 0, 0), "Pinch priority depended on collider order.");
            data.ColliderPinching[1] = new(1, true, 2, 0.1f); solver = new(data); solver.Step(Dt);
            Near(solver.GetPositions()[0], Vector3.Zero, "Equal pinch priorities did not cancel the cached contact.");
            data = Create(capsule); data.ColliderPinching[1] = new(1, true, 3, 0.1f);
            data.Colliders[1].Start += Vector3.UnitX * 0.25f; data.Colliders[1].End += Vector3.UnitX * 0.25f;
            solver = new(data); solver.Step(Dt);
            Near(solver.GetPositions()[0], Vector3.Zero, "Near-contact priority incorrectly inflated the collision surface.");
            data.Particles[0].CollisionMask = 1; solver = new(data); solver.Step(Dt);
            Near(solver.GetPositions()[0], new(0.2f, 0, 0), "Masked collider still affected pinch priority.");
            foreach (var fallback in new[] { "global", "particle", "anti-pinch", "single" })
            {
                data = Create(capsule);
                if (fallback == "global") data.PinchDetectionEnabled = false;
                if (fallback == "particle") data.PinchEnabledParticles.Clear();
                if (fallback == "anti-pinch") data.HasAntiPinchConstraints = true;
                if (fallback == "single") data.ColliderPinching[1] = new(1, false, 1, 0.1f);
                solver = new(data); solver.Step(Dt);
                Near(solver.GetPositions()[0], new(-0.2f, 0, 0), "Disabled pinch flags changed ordinary collision response.");
            }
        }
        var moving = Create(); moving.Bones.Add(new() { Index = 0, ParentIndex = -1 });
        var movingSolver = new HkclPreviewSimulator(moving);
        movingSolver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.Identity }); movingSolver.Reset();
        movingSolver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.CreateTranslation(0, Dt, 0) });
        movingSolver.Step(Dt);
        var position = movingSolver.GetPositions()[0];
        var normal = Vector3.Normalize(position - new Vector3(-0.9f, Dt, 0));
        var relative = position - Vector3.UnitY * Dt;
        Near(Previous(movingSolver, 0), relative - normal * Vector3.Dot(relative, normal), "Cached contact lost moving-surface friction.");
        movingSolver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.CreateTranslation(-3, 0, 0) });
        movingSolver.Reset(); movingSolver.Step(Dt);
        Near(movingSolver.GetPositions()[0], new(-0.2f, 0, 0), "Reset retained a stale high-priority contact.");
    }

    private static void CheckLocalRangesAndExecution()
    {
        var data = new ParticlePreviewData { Gravity = Vector3.Zero, SimulationSubSteps = 4, AdaptConstraintStiffness = true };
        data.Particles.Add(new() { Index = 0, Position = Vector3.Zero, InverseMass = 1 });
        data.ConstraintSets.Add(new() { Index = 0, Kind = "hclLocalRangeConstraintSet" });
        data.LocalRanges.Add(new() { ConstraintSetIndex = 0, ParticleIndex = 0, MaximumDistance = 0.1f, Stiffness = 0.5f });
        var solver = new HkclPreviewSimulator(data);
        SetParticle(solver, 0, Vector3.UnitX * 0.2f); solver.Step(Dt);
        Near(solver.GetPositions()[0], Vector3.UnitX * 0.15f, "Adaptive local range did not run only on the final substep.");
        Near(Previous(solver, 0), solver.GetPositions()[0], "Adaptive local range injected velocity.");

        foreach (var collisionFirst in new[] { false, true })
        {
            data = new ParticlePreviewData { Gravity = Vector3.Zero };
            data.Particles.Add(new() { Index = 0, Position = -Vector3.UnitY * 0.1f, InverseMass = 1, CollisionMask = 1 });
            data.ConstraintSets.Add(new() { Index = 0, Kind = "hclLocalRangeConstraintSet" });
            data.LocalRanges.Add(new() { ConstraintSetIndex = 0, ParticleIndex = 0, MaximumDistance = 0, Stiffness = 1 });
            data.Colliders.Add(new() { Index = 0, CollisionBit = 0, Kind = ColliderPreviewKind.Plane, PlaneNormal = Vector3.UnitY });
            data.ConstraintExecution.AddRange(collisionFirst ? new[] { -1, 0 } : new[] { 0, -1 });
            solver = new(data); solver.Step(Dt);
            Near(solver.GetPositions()[0], collisionFirst ? -Vector3.UnitY * 0.1f : Vector3.Zero, "Collision calls were moved out of authored order.");
        }
        data = Pair("hclStandardLinkConstraintSet", 0.5f);
        data.ConstraintExecution.Add(0); solver = new(data); solver.Step(Dt);
        Near(solver.GetPositions()[0], Vector3.UnitX * 0.1875f, "Repeated authored constraints were deduplicated.");
        Near(solver.GetPositions()[1], Vector3.UnitX * 1.4375f, "Repeated constraint endpoint differs.");
    }

    private static void CheckConstraintImport()
    {
        var simulation = new HKX2.hclSimClothData {
            m_staticConstraintSets = new() {
                new HKX2.hclStretchLinkConstraintSet { m_links = new() {
                    new() { m_particleA = 0, m_particleB = 1, m_restLength = 1, m_stiffness = 1 },
                    new() { m_particleA = 1, m_particleB = 0, m_restLength = 1, m_stiffness = 1 }
                } },
                new HKX2.hclBendStiffnessConstraintSet { m_useRestPoseConfig = true, m_links = new() {
                    new() { m_particleA = 0, m_particleB = 1, m_particleC = 2, m_particleD = 3, m_restCurvature = 0.2f }
                } }
            }
        };
        var preview = new ParticlePreviewData();
        typeof(HkclService).GetMethod("AddPreviewLinks", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { preview, simulation, new HKX2.hclClothData() });
        if (preview.Links.Count != 2 || preview.Links[1].ParticleA != 1 || !preview.BendStiffnessConstraints.Single().UseRestPoseConfig)
            throw new InvalidOperationException("HKCL preview discarded a directed constraint or its rest-curvature mode.");
        var op = new HKX2.hclSimulateOperator { m_constraintExecution = new() { 0, -1, 1, -1, 0 } };
        var execution = (IReadOnlyList<int>)typeof(HkclService).GetMethod("ReadRuntimeIntList", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { op, "constraintExecution", true })!;
        if (!execution.SequenceEqual(op.m_constraintExecution))
            throw new InvalidOperationException("Runtime decoding discarded or reordered collision stages.");
        simulation.m_simulationInfo = new() { m_pinchDetectionEnabled = true };
        simulation.m_perParticlePinchDetectionEnabledFlags = new() { false, true };
        simulation.m_perInstanceCollidables = new() { new HKX2.hclCollidable() };
        simulation.m_collidablePinchingDatas = new() { new() {
            m_pinchDetectionEnabled = true, m_pinchDetectionPriority = 4, m_pinchDetectionRadius = 0.03f
        } };
        typeof(HkclService).GetMethod("AddPreviewPinchSettings", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { preview, simulation });
        if (!preview.PinchDetectionEnabled || !preview.PinchEnabledParticles.SetEquals(new[] { 1 }) ||
            preview.ColliderPinching.Single() != new ParticlePreviewColliderPinch(0, true, 4, 0.03f))
            throw new InvalidOperationException("HKCL preview discarded authored pinch settings.");
    }

    private static object Particle(HkclPreviewSimulator solver, int index) =>
        ((IDictionary)typeof(HkclPreviewSimulator).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(solver)!)[index]!;
    private static Vector3 Previous(HkclPreviewSimulator solver, int index)
    {
        var particle = Particle(solver, index);
        return (Vector3)particle.GetType().GetProperty("PreviousPosition")!.GetValue(particle)!;
    }
    private static void SetParticle(HkclPreviewSimulator solver, int index, Vector3 position)
    {
        var particle = Particle(solver, index);
        particle.GetType().GetProperty("Position")!.SetValue(particle, position);
        particle.GetType().GetProperty("PreviousPosition")!.SetValue(particle, position);
    }
    private static void Near(Vector3 actual, Vector3 expected, string message, float tolerance = 0.00002f)
    {
        if (!float.IsFinite(actual.LengthSquared()) || Vector3.Distance(actual, expected) > tolerance)
            throw new InvalidOperationException($"{message} Expected {expected}; actual {actual}.");
    }
}
