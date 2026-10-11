using HKCLTool;
using PhysicsTool.Desktop;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class SimulationAuditChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);

    public static void TaperedContacts()
    {
        var shape = new ColliderPreviewShape { Index = 0, CollisionBit = 0, BoneIndex = -1,
            Kind = ColliderPreviewKind.TaperedCapsule, Start = Vector3.Zero, End = new(0, 2, 0), Radius = .25f, EndRadius = .75f };
        var profile = CapsuleProfile.Create(shape);
        var point = new Vector3(.51f, 1, 0);
        var expectedNormal = new Vector3(MathF.Sqrt(15) / 4, -.25f, 0);
        var expectedDistance = .51f * expectedNormal.X - .5f;
        var distance = profile.SignedDistance(point, Vector3.Zero, out var normal);
        Require(Math.Abs(distance - expectedDistance) < 1e-6f && Vector3.Distance(normal, expectedNormal) < 1e-6f,
            "Tapered side distance/normal differs from native tangent-cone equations");
        Require(Math.Abs(profile.SignedDistance(new(0, -.4f, 0), Vector3.Zero, out normal) - .15f) < 1e-6f
            && normal == -Vector3.UnitY, "Small tapered cap distance/normal is incorrect");
        Require(Math.Abs(profile.SignedDistance(new(0, 2.8f, 0), Vector3.Zero, out normal) - .05f) < 1e-6f
            && normal == Vector3.UnitY, "Large tapered cap distance/normal is incorrect");
        foreach (var join in new[] { profile.Start + expectedNormal * profile.Radius, profile.End + expectedNormal * profile.EndRadius })
        foreach (var margin in new[] { -.0001f, 0, .0001f })
        {
            var d = profile.SignedDistance(join + expectedNormal * margin, Vector3.Zero, out normal);
            Require(Math.Abs(d - margin) < 1e-6f && Vector3.Distance(normal, expectedNormal) < 1e-5f,
                "Tapered tangent join introduced a discontinuity");
        }
        var rotation = Quaternion.CreateFromYawPitchRoll(.4f, -.6f, 1.1f);
        var translation = new Vector3(3, -2, 1);
        var moved = new ColliderPreviewShape { Kind = shape.Kind, Radius = shape.Radius, EndRadius = shape.EndRadius,
            Start = Vector3.Transform(shape.Start, rotation) + translation, End = Vector3.Transform(shape.End, rotation) + translation };
        distance = CapsuleProfile.Create(moved).SignedDistance(Vector3.Transform(point, rotation) + translation, Vector3.Zero, out normal);
        Require(Math.Abs(distance - expectedDistance) < 1e-6f && Vector3.Distance(normal, Vector3.Transform(expectedNormal, rotation)) < 1e-6f,
            "Rigid collider movement changed the contact geometry");
        var source = new ParticlePreviewData { Gravity = Vector3.Zero };
        source.Particles.Add(new() { Index = 0, Position = point, Radius = .01f, InverseMass = 1, CollisionMask = 1 });
        source.Colliders.Add(shape);
        var solver = new HkclPreviewSimulator(source); solver.Step(1f / 60);
        Require(Vector3.Distance(solver.GetPositions()[0], point + expectedNormal * (.01f - expectedDistance)) < 1e-5f,
            "Tapered contact was missed or projected radially instead of along the native sloped normal");
        source.Particles[0].CollisionMask = 0;
        solver = new(source); solver.Step(1f / 60);
        Require(solver.GetPositions()[0] == point, "Tapered contact ignored collision mask");
        source.Particles[0].CollisionMask = 1;

        foreach (var sample in new[] { new Vector3(0, -.4f, 0), new Vector3(0, 2.8f, 0), point, new Vector3(0, 1, 0) })
        {
            var reversed = new ColliderPreviewShape { Kind = shape.Kind, Start = shape.End, End = shape.Start, Radius = shape.EndRadius, EndRadius = shape.Radius };
            var d0 = profile.SignedDistance(sample, Vector3.UnitX, out var n0);
            var d1 = CapsuleProfile.Create(reversed).SignedDistance(sample, Vector3.UnitX, out var n1);
            Require(Math.Abs(d0 - d1) < 1e-6f && Vector3.Distance(n0, n1) < 1e-5f, "Endpoint order changed tapered contacts");
            Require(Finite(n0) && Math.Abs(n0.Length() - 1) < 1e-5f, "Tapered axis fallback is not a unit normal");
        }
        foreach (var end in new[] { Vector3.Zero, new Vector3(0, .2f, 0), new Vector3(0, 2, 0) })
        foreach (var radius in new[] { .25f, .75f })
        {
            shape.End = end; shape.EndRadius = radius;
            var current = CapsuleProfile.Create(shape);
            var vertices = ColliderGeometry.Surface(shape).SelectMany(t => new[] { t.A, t.B, t.C })
                .Concat(ColliderGeometry.Create(shape).SelectMany(l => new[] { l.Start, l.End }));
            Require(vertices.All(p => Finite(p) && Math.Abs(current.SignedDistance(p, Vector3.UnitX, out _)) < 2e-5f),
                "Tapered viewport geometry does not lie on the collision surface");
        }
        shape.End = Vector3.Zero; shape.EndRadius = .75f;
        source.Particles[0].Position = new(.5f, 0, 0);
        solver = new(source); solver.Step(1f / 60);
        Require(Math.Abs(solver.GetPositions()[0].X - .76f) < 1e-5f, "Collapsed tapered capsule did not retain the larger sphere");
        Console.WriteLine("PASS: tapered capsule side normals, caps, endpoint order, degenerate shapes, solver and display agreement.");
    }

    public static void Actors(string output, string[] packs)
    {
        Directory.CreateDirectory(output);
        var results = new List<object>();
        foreach (var pack in packs)
        {
            var source = pack.Split("::", 2, StringSplitOptions.None);
            byte[] SourceHash() { using var stream = File.OpenRead(source[0]); return SHA256.HashData(stream); }
            var hash = SourceHash();
            using var scene = BotwActorScene.Load(source[0], actorEntry: source.Length > 1 ? source[1] : null);
            Require(scene.HasCloth, "Native actor has no cloth");
            var solvers = new List<HkclPreviewSimulator>(); scene.ConfigureSimulation(solvers.Add);
            var clips = scene.Animations.OrderBy(a => a.FrameCount).Where(a => a.FrameCount >= 2).Take(2).ToArray();
            Require(clips.Length > 0, "Native actor has no testable animation");
            foreach (var clip in clips)
            {
                scene.Update(clip, 0, false, 0, true);
                var initial = scene.Overlay.Particles.Select(p => p.Position).ToArray();
                var maxMotion = 0f;
                const int frames = 600;
                // Exercise the full clip and loop, while keeping the native 30 Hz animation clock.
                for (var i = 0; i < frames; i++)
                {
                    scene.Update(clip, (i * .5) % clip.FrameCount, true, 1f / 60);
                    Require(scene.Overlay.Particles.All(p => Finite(p.Position)), "Native actor cloth became nonfinite");
                    Require(scene.CurrentModel.Bones.All(b => Finite(b.Position) && Finite(b.Scale) && float.IsFinite(b.Rotation.LengthSquared())),
                        "Animated helper/cloth bone became nonfinite");
                    Require(scene.Overlay.Colliders.All(c => Finite(c.Start) && Finite(c.End)), "Animated collider became nonfinite");
                    Require(solvers.All(s => s.NumericalResetCount == 0), "Native actor triggered a numerical safety reset");
                    maxMotion = Math.Max(maxMotion, scene.Overlay.Particles.Select((p, n) => Vector3.Distance(initial[n], p.Position)).DefaultIfEmpty().Max());
                }
                Require(maxMotion > 1e-4f, "Actor cloth did not move");
                Require(scene.CurrentModel.Meshes.SelectMany(m => m.Vertices).All(float.IsFinite), "Animated model became nonfinite");
                results.Add(new { actor = scene.Resources.ActorName, platform = scene.Resources.IsSwitch ? "Switch" : "WiiU", clip = clip.Name,
                    frames, cloths = scene.ClothCount, helpers = scene.SupportCount, particles = scene.Overlay.Particles.Count,
                    colliders = scene.Overlay.Colliders.GroupBy(c => c.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                    maxMotion, resets = solvers.Sum(s => s.NumericalResetCount) });
                Console.WriteLine($"PASS: {scene.Resources.ActorName} {(scene.Resources.IsSwitch ? "Switch" : "Wii U")} / {clip.Name}: {frames} frames, zero numerical resets.");
            }
            Require(hash.SequenceEqual(SourceHash()), "Native actor source changed");
        }
        File.WriteAllText(Path.Combine(output, "actor-simulation-audit.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
