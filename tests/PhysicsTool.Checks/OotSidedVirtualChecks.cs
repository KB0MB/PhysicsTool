using System.Numerics;
using HKCLTool;

internal static class OotSidedVirtualChecks
{
    internal static void Run()
    {
        foreach (var shape in new[] { ColliderPreviewKind.Sphere, ColliderPreviewKind.Capsule })
        foreach (bool triangle in new[] { false, true })
        foreach (bool pinch in new[] { false, true })
        foreach (int mode in new[] { 1, 2 })
        foreach (int side in new[] { -1, 0, 1 })
        {
            // A separate real triangle supplies the owner's cached normal. At
            // side zero it is perpendicular to the virtual triangle's normal.
            var normal = side == 0 ? Vector3.UnitX : Vector3.UnitZ;
            var initial = normal * (mode == 1 ? 10 : -10);
            var sample = new Vector3(1, triangle ? .5f : 0, 0);
            var target = sample + Vector3.UnitZ * (side == 0 ? .25f : -side * .25f);
            var data = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental,
                Gravity = Vector3.Zero, PinchDetectionEnabled = pinch, HasAntiPinchConstraints = pinch,
                OotContactSurface = new([new() { ParticleA = 0, ParticleB = 3, ParticleC = 4 }], [0], [0]),
                VirtualCollisionData = new([new(0, 1, triangle ? 2 : -1, triangle ? new(.25f, .5f, .25f) : new(.5f, .5f, 0))], 1) };
            data.Particles.Add(new() { Index = 0, Position = Vector3.Zero, Mass = 1, InverseMass = 1, CollisionMask = 3 });
            Vector3[] fixedPositions = [2 * Vector3.UnitX, 2 * Vector3.UnitY,
                side == 0 ? Vector3.UnitY : Vector3.UnitX, side == 0 ? Vector3.UnitZ : Vector3.UnitY];
            for (int i = 0; i < fixedPositions.Length; i++)
                data.Particles.Add(new() { Index = i + 1, Position = fixedPositions[i], Fixed = true, CollisionMask = 0 });
            var axis = shape == ColliderPreviewKind.Capsule ? 2 * Vector3.UnitY : Vector3.Zero;
            data.Colliders.Add(new() { Index = 0, CollisionBit = 0, BoneIndex = 7, Kind = shape,
                Start = initial - axis, End = initial + axis, Radius = .5f, VirtualCollisionEnabled = true });
            data.ColliderPinching.Add(new(0, pinch, 1, 0));
            if (pinch)
            {
                data.Colliders.Add(new() { Index = 1, CollisionBit = 1, Kind = shape,
                    Start = new(100, 0, 0), End = new(100, 1, 0), Radius = .5f, VirtualCollisionEnabled = true });
                data.ColliderPinching.Add(new(1, true, 1, 0));
                data.PinchEnabledParticles.Add(0);
                data.AntiPinchRecovery.Add(new(0, 0, 0, .1f, .2f, 1,
                    [new() { BoneIndex = 9, Weight = 1, ReferencePosition = Vector3.One }]));
            }
            var solver = new HkclPreviewSimulator(data);
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [7] = Matrix4x4.CreateTranslation(target - initial) });
            solver.Reset(); solver.Step(1f / 60);
            bool accepted = mode == 1 ? side < 0 : side >= 0;
            if (solver.VirtualCollisionCorrectionCount != (accepted ? 1 : 0) ||
                Vector3.Distance(solver.GetPositions()[0], accepted ? Vector3.UnitZ * (side == 0 ? -.25f : side * .25f) : Vector3.Zero) > 1e-5f)
                throw new Exception($"One-sided virtual contact: {shape}, triangle={triangle}, pinch={pinch}, mode={mode}, side={side}");
            for (int i = 0; i < fixedPositions.Length; i++)
                if (solver.GetPositions()[i + 1] != fixedPositions[i]) throw new Exception("One-sided virtual contact moved a non-owner");
            if (data.Particles[0].Position != Vector3.Zero) throw new Exception("One-sided virtual preview modified source data");
        }
        Console.WriteLine("PASS: one-sided OoT edge/triangle fans, owner-vs-face normals, zero-dot boundary, sphere/capsule and pinch paths");
    }
}
