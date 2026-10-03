using System.Numerics;
using System.Reflection;

namespace HKCLTool;

internal static class ClothSimulationChecks
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        NativeClothSolverChecks.Run(directory);
        ClothBoneDeformChecks.Run(directory);
        CheckLoopContinuity();
        CheckLoopContinuity(nativeInputSampling: true);
        CheckWind();
        var source = new ParticlePreviewData { Gravity = Vector3.Zero };
        source.Particles.Add(new() { Index = 0, Position = new(0.5f, 0.5f, 0), InverseMass = 1, Radius = 0.02f });
        var solver = new HkclPreviewSimulator(source);
        foreach (var dt in new[] { 0f, -1f, float.NaN, float.PositiveInfinity }) solver.Step(dt);
        Require(solver.GetPositions()[0] == source.Particles[0].Position, "Invalid/zero step advanced physics.");
        solver.Step(1f / 60);
        var particles = (System.Collections.IDictionary)typeof(HkclPreviewSimulator).GetField("_particles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(solver)!;
        var particle = particles[0]!;
        particle.GetType().GetProperty("PreviousPosition")!.SetValue(particle, source.Particles[0].Position - new Vector3(2f / 60, 0, 0));
        var elapsed = 0f;
        foreach (var dt in new[] { 1f / 30, 1f / 120, 1f / 60, 0.0001f, 1f / 240 })
        {
            solver.Step(dt); elapsed += dt;
            Require(Vector3.Distance(solver.GetPositions()[0], source.Particles[0].Position + new Vector3(2 * elapsed, 0, 0)) < 0.00001f,
                "Changing the step size changed free-particle speed.");
        }
        solver.Reset(); solver.Step(1f / 60);
        Require(solver.GetPositions()[0] == source.Particles[0].Position, "Reset retained motion history.");
        foreach (var damping in new[] { 0f, 0.2f, 0.8f, 1f })
        foreach (var rate in new[] { 30, 60, 120 })
        foreach (var substeps in new[] { 1, 4 })
        {
            var damped = new ParticlePreviewData { Gravity = Vector3.Zero, DampingPerSecond = damping, SimulationSubSteps = substeps };
            damped.Particles.Add(new() { Index = 0, Position = Vector3.Zero, InverseMass = 1 });
            var test = new HkclPreviewSimulator(damped);
            test.Step(1f / rate);
            var entries = (System.Collections.IDictionary)typeof(HkclPreviewSimulator).GetField("_particles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(test)!;
            var point = entries[0]!;
            var previous = point.GetType().GetProperty("PreviousPosition")!;
            previous.SetValue(point, new Vector3(-1f / (rate * substeps), 0, 0));
            for (var frame = 0; frame < rate; frame++) test.Step(1f / rate);
            var velocity = (test.GetPositions()[0] - (Vector3)previous.GetValue(point)!) * (rate * substeps);
            Require(MathF.Abs(velocity.X - (1f - damping)) < 0.002f, "Native damping changed with frame rate/substeps or used the wrong per-second meaning.");
        }

        // A short capsule with an unset EndRadius is still a constant-radius
        // capsule. The point lies inside it, but outside a tapered alternative.
        source.Particles[0].Position = new(0.45f, 0.75f, 0);
        source.Particles[0].CollisionMask = 1;
        source.Colliders.Add(new() { Index = 0, CollisionBit = 0, BoneIndex = -1, Kind = ColliderPreviewKind.Capsule,
            Start = Vector3.Zero, End = Vector3.UnitY, Radius = 0.5f, EndRadius = 0 });
        solver = new(source);
        for (var i = 0; i < 12; i++) solver.Step(1f / 60);
        Require(solver.GetPositions()[0].X > 0.51f, "Capsule incorrectly used EndRadius.");
        source.Colliders[0].Kind = ColliderPreviewKind.TaperedCapsule;
        solver = new(source); solver.Step(1f / 60);
        Require(solver.GetPositions()[0] == source.Particles[0].Position, "Tapered capsules lost their separate radius behavior.");
        source.Colliders[0].Kind = ColliderPreviewKind.Capsule;
        source.Particles[0].CollisionMask = 0;
        solver = new(source); solver.Step(1f / 60);
        Require(solver.GetPositions()[0] == source.Particles[0].Position, "Collision-mask exclusion was ignored.");
        File.WriteAllText(Path.Combine(directory, "solver.txt"), "PASS: zero/invalid timestep, variable timestep velocity, native per-second damping at 30/60/120 Hz and 1/4 substeps, reset, loop deformation/velocity continuity, wind strength/toggle, constant-radius capsule, separate tapered shape, and collision mask.");
    }

    private static void CheckWind()
    {
        var data = new ParticlePreviewData { Gravity = Vector3.Zero };
        data.Particles.Add(new() { Index = 0, InverseMass = 1 });
        var solver = new HkclPreviewSimulator(data) { RandomWindDirections = false, WindGustiness = 0 };
        solver.Step(1f / 60);
        Require(solver.GetPositions()[0] == Vector3.Zero, "Disabled wind moved a stationary particle.");
        solver.WindEnabled = true;
        solver.Step(1f / 60);
        var displacement = solver.GetPositions()[0].X;
        Require(MathF.Abs(displacement - HkclPreviewSimulator.DefaultWindSpeed / 3600) < 0.000001f,
            "Wind toggle did not apply the configured strength.");
        Require(displacement > 5 * 2.2f / 3600, "Default wind is still too weak.");
        solver.WindEnabled = false; solver.Step(1f / 60);
        Require(MathF.Abs(solver.GetPositions()[0].X - 2 * displacement) < 0.000001f,
            "Disabling wind continued acceleration or reset the existing cloth motion.");
        solver.WindEnabled = true; solver.WindSpeed = 0; solver.Step(1f / 60);
        Require(MathF.Abs(solver.GetPositions()[0].X - 3 * displacement) < 0.000001f, "Zero-strength wind applied a force.");
    }

    private static void CheckLoopContinuity(bool nativeInputSampling = false)
    {
        var data = new ParticlePreviewData { Gravity = Vector3.Zero, TransferMotionEnabled = true,
            UseNativeHkclInputSampling = nativeInputSampling,
            TransferMotionTransformIndex = 0, TransferTranslationMotion = true, MinTranslationBlend = 1, MaxTranslationBlend = 1 };
        data.Bones.Add(new() { Index = 0 });
        data.Particles.Add(new() { Index = 0, Fixed = true, Position = Vector3.Zero });
        data.Particles.Add(new() { Index = 1, InverseMass = 1, Position = Vector3.UnitY });
        data.ParticleAttachments.Add(new() { ParticleIndex = 0, BoneWeights = { new() { BoneIndex = 0, Weight = 1 } } });
        var solver = new HkclPreviewSimulator(data);
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.Identity });
        solver.Reset(); solver.Step(1f / 60);
        var particles = (System.Collections.IDictionary)typeof(HkclPreviewSimulator).GetField("_particles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(solver)!;
        var particle = particles[1]!;
        var positionProperty = particle.GetType().GetProperty("Position")!;
        var previousProperty = particle.GetType().GetProperty("PreviousPosition")!;
        var position = new Vector3(0.2f, 0.8f, 0.1f);
        var velocity = new Vector3(0.01f, -0.02f, 0.03f);
        positionProperty.SetValue(particle, position); previousProperty.SetValue(particle, position - velocity);
        solver.RebaseInputPose();
        Require(Vector3.Distance(solver.GetPositions()[1], position) < 0.000001f, "Seamless loop reset deformed cloth.");
        var jump = Matrix4x4.CreateRotationZ(0.8f) * Matrix4x4.CreateTranslation(-2, 0.5f, 1);
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = jump }); solver.RebaseInputPose();
        var expectedPosition = Vector3.Transform(position, jump);
        var expectedVelocity = Vector3.TransformNormal(velocity, jump);
        Require(Vector3.Distance(solver.GetPositions()[1], expectedPosition) < 0.000001f, "Loop lost cloth deformation or failed to follow its anchor.");
        Require(Vector3.Distance((Vector3)previousProperty.GetValue(particle)!, expectedPosition - expectedVelocity) < 0.000001f,
            "Loop discontinuity changed cloth velocity.");
        Require(Vector3.Distance(solver.GetPositions()[0], jump.Translation) < 0.000001f, "Loop left a fixed particle at the previous pose.");
        solver.Step(1f / 60);
        Require(Vector3.Distance(solver.GetPositions()[1], expectedPosition + expectedVelocity) < 0.00001f,
            "Loop jump was reapplied by transfer motion or injected an impulse.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
