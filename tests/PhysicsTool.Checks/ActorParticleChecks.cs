using System.Collections;
using System.Numerics;
using System.Reflection;
using HKCLTool;

internal static class ActorParticleChecks
{
    public static void Synthetic()
    {
        var source = new ParticlePreviewData { SolverProfile = ClothSolverProfile.TotkExperimental, Gravity = Vector3.Zero };
        source.Bones.Add(new() { Index = 0, Name = "Root", ParentIndex = -1 });
        source.Bones.Add(new() { Index = 1, Name = "Child", ParentIndex = 0 });
        source.Particles.Add(new() { Index = 0, Position = Vector3.UnitX, Fixed = true });
        source.Particles.Add(new() { Index = 1, Position = new(2, 3, 4), Mass = 1, InverseMass = 1 });
        var anchor = new ParticlePreviewAttachment { ParticleIndex = 0 };
        anchor.BoneWeights.Add(new() { BoneIndex = 0, Weight = 1, ReferencePosition = Vector3.UnitX }); source.ParticleAttachments.Add(anchor);
        var range = new ParticlePreviewLocalRange { ParticleIndex = 1, MaximumDistance = 100, Stiffness = 0 };
        range.ReferenceBoneWeights.Add(new() { BoneIndex = 0, Weight = .25f, ReferencePosition = Vector3.Zero });
        range.ReferenceBoneWeights.Add(new() { BoneIndex = 1, Weight = .75f, ReferencePosition = Vector3.UnitY }); source.LocalRanges.Add(range);
        var solver = new HkclPreviewSimulator(source); solver.Reset();
        Near(solver.GetPositions()[1], source.Particles[1].Position, "Reset lost an edited particle offset");
        var root = Matrix4x4.CreateTranslation(3, 0, 1);
        var child = Matrix4x4.CreateRotationZ(.5f) * root;
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = root, [1] = child }); solver.Reset();
        var offset = source.Particles[1].Position - Vector3.UnitY * .75f;
        var expected = Vector3.Transform(offset, root) * .25f + Vector3.Transform(Vector3.UnitY + offset, child) * .75f;
        Near(solver.GetPositions()[1], expected, "Weighted particle pose did not follow both animated bones");
        Near(solver.GetPositions()[0], Vector3.Transform(Vector3.UnitX, root), "Fixed input changed");
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = root * Matrix4x4.CreateTranslation(1, 0, 0), [1] = child * Matrix4x4.CreateTranslation(1, 0, 0) });
        solver.Step(1f / 60);
        Near(solver.GetPositions()[1], expected, "Free particle became a kinematic anchor during simulation");
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4>()); solver.Reset();
        Near(solver.GetPositions()[1], source.Particles[1].Position, "Rest restoration drifted");
        if (source.ParticleAttachments.Count != 1 || range.ReferenceBoneWeights[0].ReferencePosition != Vector3.Zero || range.ReferenceBoneWeights[1].ReferencePosition != Vector3.UnitY)
            throw new Exception("Particle preview mutated authored skin inputs");
        source.SolverProfile = ClothSolverProfile.BotwPreview;
        solver = new(source); solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = root, [1] = child }); solver.Reset();
        Near(solver.GetPositions()[1], source.Particles[1].Position, "BotW particle reset behavior changed");
        Console.WriteLine("PASS: TotK weighted free-particle posing, edited offsets, fixed inputs, dynamic freedom, rest restoration and BotW isolation.");
    }
    private static void Near(Vector3 actual, Vector3 expected, string message)
    {
        if (!float.IsFinite(actual.LengthSquared()) || Vector3.Distance(actual, expected) > .001f)
            throw new Exception($"{message}: expected {expected}, got {actual}");
    }
    public static void Inspect(string path)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
        using var scene = BotwActorScene.Load(path);
        Console.WriteLine($"{scene.Resources.ActorName}: {scene.ClothCount} cloths; {scene.Animations.Count} clips");
        foreach (var warning in scene.Resources.Warnings) Console.WriteLine(warning);
        var layers = (IEnumerable)typeof(BotwActorScene).GetField("_layers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!;
        foreach (var layer in layers)
        {
            var data = (ParticlePreviewData)layer.GetType().GetProperty("Source")!.GetValue(layer)!;
            Console.WriteLine($"LAYER particles={data.Particles.Count} fixed={data.Particles.Count(p => p.Fixed)} attachments={data.ParticleAttachments.Count} ranges={data.LocalRanges.Count} boneBindings={data.BoneBindings.Count}");
        }
        foreach (var clip in scene.Animations)
        {
            scene.Update(clip, 0, false, 0, true);
            var start = scene.Overlay.Particles.Select(p => p.Position).ToArray();
            var bones = scene.CurrentModel.Bones.Select(b => b.Position).ToArray();
            scene.Update(clip, clip.FrameCount / 2, false, 0);
            Console.WriteLine($"CLIP {clip.Name}: frames={clip.FrameCount} movingParticles={scene.Overlay.Particles.Where((p,i) => Vector3.Distance(p.Position,start[i]) > .001f).Count()}/{start.Length} movingBones={scene.CurrentModel.Bones.Where((b,i) => Vector3.Distance(b.Position,bones[i]) > .001f).Count()}/{bones.Length}");
            CheckPose();
            scene.Update(clip, clip.FrameCount, false, 0); CheckPose();
            scene.Update(clip, 0, false, 0); CheckPose();
            foreach (var (p, i) in scene.Overlay.Particles.Select((p, i) => (p, i))) Near(p.Position, start[i], "Seeking back did not restore particles");
        }
        var solvers = new List<HkclPreviewSimulator>(); scene.ConfigureSimulation(solvers.Add);
        var moving = scene.Animations.First(a => a.Name.Contains("Cough"));
        for (int i = 0; i < 120; i++) scene.Update(moving, i % Math.Max(1, moving.FrameCount), true, 1f / 60, i == 0);
        if (solvers.Any(s => s.NumericalResetCount != 0) || scene.Overlay.Particles.Any(p => !float.IsFinite(p.Position.LengthSquared()))) throw new Exception("Actor simulation became unstable");
        scene.Update(moving, moving.FrameCount / 2, false, 0); CheckPose();
        if (!hash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))) throw new Exception("Actor source was modified");
        Console.WriteLine("PASS: native HorseGod weighted particle poses match the animated model, seeking restores positions, simulation loops/stop and source isolation.");

        void CheckPose()
        {
            var rest = scene.RestModel.Bones.ToDictionary(b => b.Name);
            var current = scene.CurrentModel.Bones.ToDictionary(b => b.Name);
            foreach (var layer in layers)
            {
                var data = (ParticlePreviewData)layer.GetType().GetProperty("Source")!.GetValue(layer)!;
                int indexOffset = (int)layer.GetType().GetProperty("ParticleOffset")!.GetValue(layer)!;
                foreach (var p in data.Particles)
                {
                    var weights = data.ParticleAttachments.FirstOrDefault(a => a.ParticleIndex == p.Index)?.BoneWeights;
                    bool freeReference = weights == null;
                    weights ??= data.LocalRanges.FirstOrDefault(r => r.ParticleIndex == p.Index)?.ReferenceBoneWeights;
                    if (weights is not { Count: > 0 }) throw new Exception($"HorseGod P{p.Index} has no pose weights");
                    float total = weights.Sum(w => w.Weight);
                    var reference = weights.Aggregate(Vector3.Zero, (sum, w) => sum + (w.ReferencePosition ?? p.Position) * w.Weight) / total;
                    var offset = freeReference ? p.Position - reference : Vector3.Zero;
                    var expected = Vector3.Zero;
                    foreach (var w in weights)
                    {
                        string name = data.Bones.First(b => b.Index == w.BoneIndex).Name;
                        Matrix4x4.Invert(BotwActorScene.Matrix(rest[name]), out var inverse);
                        expected += Vector3.Transform((w.ReferencePosition ?? p.Position) + offset, inverse * BotwActorScene.Matrix(current[name])) * (w.Weight / total);
                    }
                    Near(scene.Overlay.Particles.Single(point => point.Index == p.Index + indexOffset).Position, expected, "Actor particle diverged from current model bones");
                }
            }
        }
    }
}
