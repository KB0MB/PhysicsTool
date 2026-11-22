using System.Numerics;
using HKCLTool;

internal static class OotVirtualPinchChecks
{
    internal static void Run()
    {
        static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        foreach (var kind in new[] { ColliderPreviewKind.Sphere, ColliderPreviewKind.Capsule })
        {
            ParticlePreviewData Data(Vector3 owner, Vector3 other)
            {
                var data = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental,
                    Gravity = Vector3.Zero, PinchDetectionEnabled = true, HasAntiPinchConstraints = true };
                data.Particles.Add(new() { Index = 0, Position = owner, Mass = 1, InverseMass = 1, CollisionMask = 7 });
                data.Particles.Add(new() { Index = 1, Position = other, Fixed = true });
                data.PinchEnabledParticles.Add(0);
                data.VirtualCollisionData = new([new(0, 1, -1, new(.5f, .5f, 0))], 1);
                data.AntiPinchRecovery.Add(new(0, 0, 0, .1f, .2f, 1,
                    [new() { BoneIndex = 0, Weight = 1, ReferencePosition = owner }]));
                return data;
            }
            void Collider(ParticlePreviewData data, Vector3 center, int priority, bool virtualEnabled = true, bool pinch = true)
            {
                int i = data.Colliders.Count;
                data.Colliders.Add(new() { Index = i, CollisionBit = i, Kind = kind, Radius = 1,
                    Start = center - (kind == ColliderPreviewKind.Capsule ? Vector3.UnitZ : Vector3.Zero),
                    End = center + Vector3.UnitZ, VirtualCollisionEnabled = virtualEnabled });
                data.ColliderPinching.Add(new(i, pinch, priority, 0));
            }
            var projected = Data(new(0, .5f, 0), new(0, .5f, 0));
            Collider(projected, Vector3.Zero, 1);
            Collider(projected, new(100, 0, 0), 1);
            var solver = new HkclPreviewSimulator(projected); solver.Step(1f / 60);
            Require(solver.VirtualCollisionCorrectionCount == 1, "Virtual sample did not run inside pinch pass");
            Require(Vector3.Distance(solver.GetPositions()[0], Vector3.UnitY) < 1e-5f,
                "Retained contact reused stale penetration after a virtual correction");

            // A virtual sample can move beyond the plane; native projection pulls it back.
            var negative = Data(new(0, .5f, 0), new(0, -.5f, 0));
            negative.Particles.Add(new() { Index = 2, Position = new(1, .5f, 0), Fixed = true });
            negative.VirtualCollisionData = new([new(0, 1, -1, new(.75f, .25f, 0))], 1);
            Collider(negative, Vector3.Zero, 1);
            Collider(negative, new(100, 0, 0), 1);
            solver = new(negative); solver.Step(1f / 60);
            Require(Vector3.Distance(solver.GetPositions()[0], Vector3.UnitY) < 1e-5f, "Retained projection must allow negative correction");

            foreach (int secondPriority in new[] { 0, 1, 2 })
            {
                var priority = Data(new(-2, .5f, 0), new(2, .5f, 0));
                Collider(priority, new(-2, 0, 0), 1, false);
                Collider(priority, Vector3.Zero, secondPriority);
                solver = new(priority); solver.Step(1f / 60);
                Require(solver.VirtualCollisionCorrectionCount == (secondPriority >= 1 ? 1 : 0), "Virtual owner ignored retained contact priority");
                Require(Vector3.Distance(solver.GetPositions()[0], new(-2, 1, 0)) < 1e-5f, "Virtual correction broke retained plane");
            }

            var pinched = Data(new(0, .5f, 0), new(0, .5f, 0));
            Collider(pinched, Vector3.Zero, 1, false);
            Collider(pinched, Vector3.Zero, 1);
            solver = new(pinched); solver.Step(1f / 60);
            Require(solver.VirtualCollisionCorrectionCount == 0 && solver.GetPositions()[0] == pinched.Particles[0].Position,
                "Already pinched owner must skip virtual fans");

            var triangle = Data(new(-2, 0, .5f), new(2, 0, .5f));
            triangle.Particles.Add(new() { Index = 2, Position = new(0, 3, .5f), Fixed = true });
            triangle.VirtualCollisionData = new([new(0, 1, 2, new(.25f, .25f, .5f))], 1);
            Collider(triangle, new(-.25f, 1.5f, 0), 1);
            Collider(triangle, new(100, 0, 0), 1);
            solver = new(triangle); solver.Step(1f / 60);
            Require(solver.VirtualCollisionCorrectionCount == 1 && solver.GetPositions()[0].Z > .5f,
                "Triangle virtual fans were lost in the pinch-enabled path");
            Require(solver.GetPositions()[1] == triangle.Particles[1].Position && solver.GetPositions()[2] == triangle.Particles[2].Position,
                "Triangle pinch correction moved a non-owner");

            var ordinary = Data(new(-2, .5f, 0), new(2, .5f, 0));
            Collider(ordinary, new(100, 0, 0), 1);
            Collider(ordinary, new(200, 0, 0), 1);
            Collider(ordinary, Vector3.Zero, 1, pinch: false);
            solver = new(ordinary); solver.Step(1f / 60);
            Require(solver.VirtualCollisionCorrectionCount == 1 && Math.Abs(solver.GetPositions()[0].Y - 1) < 1e-5f,
                "Non-pinch collider lost virtual contacts when recovery was enabled");
            Require(solver.GetPositions()[1] == ordinary.Particles[1].Position, "Virtual pinch moved a fixed endpoint");
        }
        Console.WriteLine("PASS: OoT virtual pinch priority gating, retained-plane projection and ordinary collider fans");
    }
}
