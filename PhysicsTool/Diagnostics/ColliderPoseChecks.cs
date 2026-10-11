using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace HKCLTool;

internal static class ColliderPoseChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Run(string directory, string[] files)
    {
        Directory.CreateDirectory(directory);
        var report = new StringBuilder();
        foreach (var file in files)
        {
            var hash = SHA256.HashData(File.ReadAllBytes(file));
            var document = new HkclService(); document.Load(file);
            var state = document.CaptureState();
            try
            {
                for (var cloth = 0; cloth < document.GetClothSummaries().Count; cloth++)
                {
                    var source = document.GetParticlePreview(cloth);
                    var repeated = source.Colliders.GroupBy(c => c.Index).Where(g => g.Count() > 1).ToArray();
                    var heading = $"{Path.GetFileName(file)} / {document.GetClothName(cloth)}: {source.Colliders.Count} slots, {source.Colliders.Select(c => c.Index).Distinct().Count()} IDs; repeats {string.Join(", ", repeated.Select(g => $"{g.Key} x{g.Count()}"))}";
                    Console.WriteLine(heading); report.AppendLine(heading);
                    if (document.IsBphcl) continue;
                    var slots = source.Colliders.Select(c => (c.Index, c.CollisionBit, c.Start, c.End)).ToArray();
                    var solver = new HkclPreviewSimulator(source);
                    for (var frame = 0; frame < 120; frame++)
                    {
                        var delta = Matrix4x4.CreateRotationY(.04f * MathF.Sin(frame * .05f)) * Matrix4x4.CreateTranslation(.01f * MathF.Sin(frame * .1f), 0, 0);
                        solver.SetInputBoneDeltas(source.Bones.ToDictionary(b => b.Index, _ => delta));
                        solver.Step(1f / 60);
                        Require(solver.GetColliderPoses().Count == source.Colliders.Select(c => c.Index).Distinct().Count(), "Display pose count does not match unique native IDs.");
                        Require(solver.GetPositions().Values.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)), "Non-finite particle position.");
                        solver.GetBonePoses();
                    }
                    solver.Reset(); solver.GetColliderPoses();
                    Require(slots.SequenceEqual(source.Colliders.Select(c => (c.Index, c.CollisionBit, c.Start, c.End))), "Simulation mutated authored collider slots.");
                }
                Require(document.CaptureState() == state, "Simulation changed native document data.");
            }
            finally { Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))), "Source fixture was changed."); }
        }
        CheckRepeatedSlots();
        report.AppendLine("PASS: repeated collider pose IDs, masked secondary slots, animated poses, reset, native cloth stepping and source preservation.");
        File.WriteAllText(Path.Combine(directory, "collider-poses.txt"), report.ToString());
        Console.WriteLine(report.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Last());
    }
    private static void CheckRepeatedSlots()
    {
        foreach (var mask in new uint[] { 1, 2, 0 })
        {
            var source = new ParticlePreviewData { Gravity = Vector3.Zero };
            source.Bones.Add(new() { Index = 0, ParentIndex = -1 });
            source.Particles.Add(new() { Index = 0, Position = new(.25f, 0, 0), InverseMass = 1, CollisionMask = mask });
            for (var slot = 0; slot < 2; slot++)
                source.Colliders.Add(new() { Index = 2, CollisionBit = slot, BoneIndex = 0, Kind = ColliderPreviewKind.Sphere, Start = Vector3.Zero, End = Vector3.Zero, Radius = 1, EndRadius = 1 });
            var solver = new HkclPreviewSimulator(source);
            var poses = solver.GetColliderPoses();
            Require(poses.Count == 1 && poses.ContainsKey(2), "Repeated native ID failed display export.");
            solver.Step(1f / 60);
            var position = solver.GetPositions()[0];
            Require(mask == 0 ? position == source.Particles[0].Position : position.X >= .99f, "A repeated collision slot was dropped or its mask ignored.");
            solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.CreateTranslation(0, 2, 0) });
            Require(Vector3.Distance(solver.GetColliderPoses()[2].Start, new(0, 2, 0)) < 1e-5, "Repeated collider pose did not animate.");
            solver.Reset(); solver.GetColliderPoses();
            Require(source.Colliders.Count == 2 && source.Colliders[1].CollisionBit == 1 && source.Colliders.All(c => c.Start == Vector3.Zero), "Display export changed solver slots or rest data.");
        }
    }
}
