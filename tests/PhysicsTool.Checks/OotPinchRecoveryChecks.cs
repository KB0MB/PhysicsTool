using System.Numerics;
using HKCLTool;
using Reference = HKCLTool.OotPinchRecovery;
using State = HKCLTool.OotPinchRecovery.State;

internal static class OotPinchRecoveryChecks
{
    internal static void Run()
    {
        OotSidednessChecks.Run();
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Near(Vector3 actual, Vector3 expected, string message) => Require(Vector3.Distance(actual, expected) < 1e-5f, message);
        var recovery = new Reference(.25f, .5f, 1);
        var position = new Vector3(4, 0, 0);
        Near(recovery.Apply(false, position, Vector3.Zero), position, "Free recovery changed position");
        Near(recovery.Apply(true, position, Vector3.Zero), position, "Initial pinch must cache distance without snapping");
        Require(recovery.Phase == State.Recovering && recovery.Elapsed == 0, "Initial pinch state");
        recovery.EndSubstep(.125f);
        Near(recovery.Apply(true, position, Vector3.Zero), new(2, 0, 0), "Half recovery distance");
        Near(recovery.Apply(true, new(1, 0, 0), Vector3.Zero), new(1, 0, 0), "Recovery must not push an already nearer particle outward");
        recovery.EndSubstep(.125f);
        Require(recovery.Phase == State.Recovering, "Exact recovery duration must not advance state early");
        Near(recovery.Apply(true, position, Vector3.Zero), Vector3.Zero, "Exact recovery duration must snap position");
        recovery.EndSubstep(.125f);
        Require(recovery.Phase == State.Held, "Recovery completion");
        var movingReference = new Vector3(1, 2, 3);
        Near(recovery.Apply(true, position, movingReference), movingReference, "Held particle must follow current reference");
        Near(recovery.Apply(false, position, movingReference), movingReference, "Release starts on current reference");
        Require(recovery.Phase == State.Releasing && recovery.Elapsed == 0, "Release timer must restart");
        recovery.EndSubstep(.25f);
        Near(recovery.Apply(false, movingReference + position, movingReference), movingReference + position * .5f, "Half release");
        recovery.EndSubstep(.25f);
        Require(recovery.Phase == State.Releasing, "Exact release duration must not advance state early");
        Near(recovery.Apply(false, position, movingReference), position, "Exact release duration must allow free position");
        recovery.EndSubstep(.125f);
        Require(recovery.Phase == State.Free, "Release completion");

        foreach (int passes in new[] { 1, 4, 12 })
        {
            var repeated = new Reference(.25f, .5f, 1);
            for (int i = 0; i < passes; i++) repeated.Apply(true, position, Vector3.Zero);
            Require(repeated.Elapsed == 0, "Collision/constraint passes must not advance recovery time");
            repeated.EndSubstep(.125f);
            Require(repeated.Elapsed == .125f, "One timer update per substep");
            for (int i = 0; i < passes; i++) Near(repeated.Apply(true, position, Vector3.Zero), new(2, 0, 0), "Iteration-independent recovery time");
            repeated.Apply(false, position, Vector3.Zero);
            repeated.EndSubstep(.125f);
            var rePinched = new Vector3(8, 0, 0);
            Near(repeated.Apply(true, rePinched, Vector3.Zero), rePinched, "Re-pinch should start a new recovery");
            Require(repeated.Elapsed == 0 && Math.Abs(repeated.InitialDistance - 8) < 1e-5f, "Re-pinch must discard old elapsed time and distance");
        }

        var firstSubstep = new Reference(.25f, .5f, 1);
        firstSubstep.Apply(true, position, Vector3.Zero);
        firstSubstep.Apply(true, new(6, 0, 0), Vector3.Zero);
        Require(firstSubstep.Elapsed == 0 && Math.Abs(firstSubstep.InitialDistance - 6) < 1e-5f,
            "Native zero-time passes refresh the recovery distance, not just the first call");
        firstSubstep.EndSubstep(.125f);
        Near(firstSubstep.Apply(true, new(6, 0, 0), Vector3.Zero), new(3, 0, 0), "Recovery uses the latest zero-time distance");

        var immediate = new Reference(0, 0, 0);
        immediate.Apply(true, position, Vector3.Zero);
        immediate.EndSubstep(.125f);
        Near(immediate.Apply(true, position, Vector3.Zero), Vector3.Zero, "Zero-duration recovery");
        immediate.Apply(false, position, Vector3.Zero);
        immediate.EndSubstep(.125f);
        Require(immediate.Phase == State.Free, "Zero-duration release");
        ContactsAndPreview();
        OotPinchDecodingChecks.Run();
        OotReferenceBufferChecks.Run();
        OotVirtualPinchChecks.Run();
        Console.WriteLine("PASS: OoT native pinch transitions, timer boundaries, overlap detection, priorities, animated references, reset and guarded preview integration");
    }

    private static void ContactsAndPreview()
    {
        static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        var contacts = new OotPinchContactState();
        contacts.Reset();
        Require(contacts.Add(1, .2f) && contacts.Valid && !contacts.Pinched, "First contact must not pinch");
        Require(!contacts.Add(1, .1f) && !contacts.Valid && contacts.Pinched, "Tied overlaps pinch and invalidate the contact");
        Require(contacts.Add(2, .1f) && contacts.Valid && contacts.Pinched, "Higher priority restores the retained contact");
        Require(!contacts.Add(0, .1f) && contacts.Valid && contacts.Pinched, "Lower priority must preserve the retained contact");
        contacts.Reset();
        Require(!contacts.Add(1, -.05f) && !contacts.Pinched && !contacts.Valid, "Detection margin is not physical penetration");
        contacts.Add(2, -.01f);
        Require(contacts.Pinched && !contacts.Valid, "Two margin overlaps can pinch without physical projection");
        contacts.Reset();
        Require(!contacts.Pinched && !contacts.Valid && contacts.Priority == -1, "Contact caches must clear each pass");

        var weights = new ParticlePreviewBoneWeight[] { new() { BoneIndex = 7, Weight = 1, ReferencePosition = Vector3.Zero } };
        var map = new Dictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> { [9] = weights };
        Require(ReferenceEquals(HkclService.ResolveOotPinchReference(map, 9), weights), "Reference vertex routing");
        bool rejected = false;
        try { HkclService.ResolveOotPinchReference(map, 0); } catch (NotSupportedException) { rejected = true; }
        Require(rejected, "Missing vertex must not fall back to particle index or rest position");

        foreach (var shape in new[] { ColliderPreviewKind.Sphere, ColliderPreviewKind.Capsule })
        foreach (int iterations in new[] { 1, 4, 12 })
        {
            var data = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental, Gravity = Vector3.Zero,
                PinchDetectionEnabled = true, HasAntiPinchConstraints = true, SimulationSolveIterations = iterations };
            data.Particles.Add(new() { Index = 0, Position = Vector3.UnitY, Mass = 1, InverseMass = 1, CollisionMask = 3 });
            data.Particles.Add(new() { Index = 1, Position = Vector3.UnitY, Fixed = true, CollisionMask = 3 });
            data.PinchEnabledParticles.Add(0);
            data.AntiPinchRecovery.Add(new(0, 3, 9, .1f, .2f, 1, weights));
            for (int i = 0; i < 2; i++)
            {
                var center = new Vector3(i == 0 ? -.2f : .2f, 0, 0);
                data.Colliders.Add(new() { Index = i, CollisionBit = i, Kind = shape, Radius = 2,
                    Start = center - (shape == ColliderPreviewKind.Capsule ? Vector3.UnitZ : Vector3.Zero), End = center + Vector3.UnitZ });
                data.ColliderPinching.Add(new(i, true, 1, .1f));
            }
            var solver = new HkclPreviewSimulator(data);
            solver.Step(.025f);
            Require(Vector3.Distance(solver.GetPositions()[0], Vector3.UnitY) < 1e-5f, "Initial pinch must not snap");
            solver.Step(.025f);
            Require(Math.Abs(solver.GetPositions()[0].Y - .75f) < 1e-5f, "Timer must advance once, not per solve iteration");
            for (int i = 0; i < 8; i++) solver.Step(.025f);
            var target = new Vector3(.1f, .2f, .3f);
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [7] = Matrix4x4.CreateTranslation(target) });
            solver.Step(.025f);
            Require(Vector3.Distance(solver.GetPositions()[0], target) < 1e-5f, "Held recovery must use the animated buffer vertex");
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4>());
            solver.Reset(); solver.Step(.025f);
            Require(Vector3.Distance(solver.GetPositions()[0], Vector3.UnitY) < 1e-5f, "Reset/seek must clear recovery history");
            Require(data.Particles[0].Position == Vector3.UnitY && weights[0].ReferencePosition == Vector3.Zero, "Preview modified source data");
            Require(solver.GetPositions()[1] == Vector3.UnitY, "Pinch contacts moved a fixed anchor");

            data.Particles[0].CollisionMask = 0;
            var masked = new HkclPreviewSimulator(data);
            for (int i = 0; i < 10; i++) masked.Step(.025f);
            Require(masked.GetPositions()[0] == Vector3.UnitY, "Masked colliders triggered recovery");
            data.Particles[0].CollisionMask = 3;
            data.PinchEnabledParticles.Clear();
            var unflagged = new HkclPreviewSimulator(data); unflagged.Step(.025f);
            Require(unflagged.GetPositions()[0].Y > 1, "Unflagged particle lost ordinary projection");
            data.PinchEnabledParticles.Add(0);
            data.PinchDetectionEnabled = false;
            var disabled = new HkclPreviewSimulator(data); disabled.Step(.025f);
            Require(disabled.GetPositions()[0].Y > 1, "Disabled pinch detection lost ordinary projection");
        }

        foreach (float height in new[] { 1.125f, 1.25f })
        {
            var data = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental, Gravity = Vector3.Zero,
                PinchDetectionEnabled = true, HasAntiPinchConstraints = true };
            data.Particles.Add(new() { Index = 0, Position = new(0, height, 0), Mass = 1, InverseMass = 1, CollisionMask = 3 });
            data.PinchEnabledParticles.Add(0);
            data.AntiPinchRecovery.Add(new(0, 3, 9, .1f, .2f, 1, weights));
            for (int i = 0; i < 2; i++)
            {
                data.Colliders.Add(new() { Index = i, CollisionBit = i, Kind = ColliderPreviewKind.Sphere, Radius = 1 });
                data.ColliderPinching.Add(new(i, true, 1, .25f));
            }
            var solver = new HkclPreviewSimulator(data); solver.Step(.025f);
            Require(solver.GetPositions()[0].Y == height, "Detection margin caused physical projection");
            solver.Step(.025f);
            float expected = height < 1.25f ? height * .75f : height;
            Require(Math.Abs(solver.GetPositions()[0].Y - expected) < 1e-5f, "Strict margin boundary / margin-only pinch recovery");
        }
    }
}
