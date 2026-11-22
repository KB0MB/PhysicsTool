using System.Numerics;
using HKCLTool;

internal static class OotSidednessChecks
{
    internal static void Run()
    {
        OotSidedVirtualChecks.Run();
        static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        Require(OotContactSidedness.ChooseSide(2, 0, 2) == 1, "Sidedness tie must prefer negative");
        Require(OotContactSidedness.ChooseSide(1, 3, 2) == 0, "Zero-majority should disable sidedness");
        Require(OotContactSidedness.ChooseSide(1, 2, 2) == 2, "Zero tie should preserve positive mode");
        Require(OotContactSidedness.ChooseSide(0, 0, 0) == 1, "Empty vote must use native tie rule");
        Require(!OotContactSidedness.Accept(1, 0) && OotContactSidedness.Accept(2, 0), "Zero-dot sidedness boundary");
        Require(OotContactSidedness.Accept(1, -1) && !OotContactSidedness.Accept(1, 1) &&
            !OotContactSidedness.Accept(2, -1) && OotContactSidedness.Accept(2, 1), "Opposite side tests");
        var tri = new ParticlePreviewTriangle { ParticleA = 0, ParticleB = 1, ParticleC = 2 };
        var positions = new Dictionary<int, Vector3> { [0] = Vector3.Zero, [1] = Vector3.UnitX, [2] = Vector3.UnitY };
        var normals = new OotContactSidedness(new([tri, tri, tri, tri], [0], [0]));
        normals.Update(positions);
        Require(normals.Normal(0) == Vector3.UnitZ && normals.Normal(99) == Vector3.Zero, "Surface normal accumulation");
        normals = new(new([tri, tri, tri, tri], [15], [0])); normals.Update(positions);
        Require(normals.Normal(0) == -Vector3.UnitZ, "Packed triangle flip signs");
        normals = new(new([tri], [128], [0])); normals.Update(positions);
        Require(normals.Normal(0) == -Vector3.UnitZ, "Native scalar-tail flip bit");
        positions[2] = Vector3.UnitX; normals.Update(positions);
        Require(normals.Normal(0) == Vector3.Zero, "Degenerate normal invented a fallback direction");

        foreach (var shape in new[] { ColliderPreviewKind.Sphere, ColliderPreviewKind.Capsule })
        foreach (bool pinch in new[] { false, true })
        {
            var data = new ParticlePreviewData { SolverProfile = ClothSolverProfile.OotExperimental,
                Gravity = Vector3.Zero, PinchDetectionEnabled = pinch, HasAntiPinchConstraints = pinch,
                OotContactSurface = new([tri], [0], [0, 1]) };
            data.Particles.Add(new() { Index = 0, Position = Vector3.Zero, Mass = 1, InverseMass = 1, CollisionMask = 3 });
            data.Particles.Add(new() { Index = 1, Position = Vector3.UnitX, Fixed = true, CollisionMask = 0 });
            data.Particles.Add(new() { Index = 2, Position = Vector3.UnitY, Fixed = true, CollisionMask = 0 });
            for (int i = 0; i < 2; i++)
            {
                var center = new Vector3(0, 0, -.5f);
                data.Colliders.Add(new() { Index = i, CollisionBit = i, BoneIndex = 7, Kind = shape, Radius = 1,
                    Start = center - (shape == ColliderPreviewKind.Capsule ? Vector3.UnitY : Vector3.Zero),
                    End = center + Vector3.UnitY });
                data.ColliderPinching.Add(new(i, pinch, 1, .25f));
            }
            if (pinch)
            {
                data.PinchEnabledParticles.Add(0);
                data.AntiPinchRecovery.Add(new(0, 0, 0, .1f, .2f, 1,
                    [new() { BoneIndex = 9, Weight = 1, ReferencePosition = Vector3.UnitX }]));
            }
            var solver = new HkclPreviewSimulator(data);
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [7] = Matrix4x4.CreateTranslation(0, 0, 1) });
            solver.Reset();
            for (int i = 0; i < 4; i++) solver.Step(1f / 60);
            Require(solver.GetPositions()[0] == Vector3.Zero, "Wrong-side contacts projected or entered pinch recovery");
            Require(solver.GetPositions()[1] == Vector3.UnitX && solver.GetPositions()[2] == Vector3.UnitY, "Sidedness moved fixed particles");
            data.OotContactSurface = null;
            var ordinary = new HkclPreviewSimulator(data);
            ordinary.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [7] = Matrix4x4.CreateTranslation(0, 0, 1) });
            ordinary.Reset();
            for (int i = 0; i < 4; i++) ordinary.Step(1f / 60);
            Require(ordinary.GetPositions()[0] != Vector3.Zero, "Control fixture did not exercise contact/recovery");
        }
        Console.WriteLine("PASS: OoT normal flips, setup votes, one-sided sphere/capsule contacts and pinch-cache rejection");
    }
}
