using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using HKCLTool;

internal static class ActorAnimationChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Synthetic()
    {
        var data = new ParticlePreviewData { Gravity = Vector3.Zero };
        data.Bones.Add(new() { Index = 0, Name = "Skl_Root", ParentIndex = -1 });
        data.Bones.Add(new() { Index = 1, Name = "Cloth", ParentIndex = 0 });
        data.Particles.Add(new() { Index = 0, Position = Vector3.UnitX, Fixed = true });
        data.Particles.Add(new() { Index = 1, Position = new(2, 3, 4), Mass = 1, InverseMass = 1 });
        var fixedInput = new ParticlePreviewAttachment { ParticleIndex = 0 };
        fixedInput.BoneWeights.Add(new() { BoneIndex = 0, Weight = 1, ReferencePosition = Vector3.UnitX });
        data.ParticleAttachments.Add(fixedInput);
        var freeInput = new ParticlePreviewAttachment { ParticleIndex = 1 };
        freeInput.BoneWeights.Add(new() { BoneIndex = 0, Weight = .25f, ReferencePosition = Vector3.Zero });
        freeInput.BoneWeights.Add(new() { BoneIndex = 1, Weight = .75f, ReferencePosition = Vector3.UnitY });
        BotwActorScene.AddParticlePoseReferences(data, new[] { fixedInput, freeInput });
        BotwActorScene.AddParticlePoseReferences(data, new[] { freeInput });
        Require(data.ParticleAttachments.Count == 2 && ReferenceEquals(data.ParticleAttachments[0], fixedInput), "Actor weights replaced fixed inputs or added duplicates");
        var solver = new HkclPreviewSimulator(data); solver.Reset();
        Near(solver.GetPositions()[1], data.Particles[1].Position, "Rest offset changed");
        var root = Matrix4x4.CreateRotationZ(.3f) * Matrix4x4.CreateTranslation(1, 2, 3);
        var child = Matrix4x4.CreateRotationY(.5f) * root;
        var offset = data.Particles[1].Position - Vector3.UnitY * .75f;
        var expected = Vector3.Transform(offset, root) * .25f + Vector3.Transform(Vector3.UnitY + offset, child) * .75f;
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = root, [1] = child }); solver.Reset();
        Near(solver.GetPositions()[1], expected, "Actor pose ignored multi-bone skinning");
        Near(solver.GetPositions()[0], Vector3.Transform(Vector3.UnitX, root), "Fixed input did not follow root");
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = root * Matrix4x4.CreateTranslation(2, 0, 0), [1] = child * Matrix4x4.CreateTranslation(2, 0, 0) });
        solver.Step(1f / 60);
        Near(solver.GetPositions()[1], expected, "Free particle was pinned to its animation pose");
        Require(freeInput.BoneWeights[0].ReferencePosition == Vector3.Zero && freeInput.BoneWeights[1].ReferencePosition == Vector3.UnitY, "Authored references were modified");
        Console.WriteLine("PASS: BotW actor weighted particle poses, edited offsets, fixed inputs and dynamic freedom.");
        static void Near(Vector3 actual, Vector3 expected, string message) => Require(Vector3.Distance(actual, expected) < .0001f, message);
    }
    public static void Run(string output, string path, string clipName)
    {
        Directory.CreateDirectory(output);
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        using var scene = BotwActorScene.Load(path);
        var clip = scene.Animations.First(a => a.Name == clipName);
        var layers = ((IEnumerable)typeof(BotwActorScene).GetField("_layers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!).Cast<object>()
            .Select(l => (Source: (ParticlePreviewData)l.GetType().GetProperty("Source")!.GetValue(l)!,
                Solver: (HkclPreviewSimulator)l.GetType().GetProperty("Solver")!.GetValue(l)!,
                ParticleOffset: (int)l.GetType().GetProperty("ParticleOffset")!.GetValue(l)!,
                ColliderOffset: (int)l.GetType().GetProperty("ColliderOffset")!.GetValue(l)!)).ToArray();
        var authored = new List<IReadOnlyList<ParticlePreviewAttachment>>();
        foreach (var resource in scene.Resources.PhysicsFiles.Where(f => f.EndsWith(".hkcl", StringComparison.OrdinalIgnoreCase)))
        {
            var file = Path.Combine(output, Path.GetFileName(resource));
            File.WriteAllBytes(file, scene.Resources.Read(resource)!);
            var service = new HkclService(); service.Load(file);
            for (int c = 0; c < service.GetClothSummaries().Count; c++) authored.Add(service.GetParticleBoneAttachments(c));
        }
        Require(authored.Count == layers.Length, "Expected one authored attachment map per HKCL layer");
        var driven = layers.SelectMany(l => l.Source.Bones.Where(b => l.Solver.DrivenBoneIndices.Contains(b.Index)).Select(b => b.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rest = scene.RestModel.Bones.DistinctBy(b => b.Name).ToDictionary(b => b.Name);
        var failures = new List<string>();
        foreach (var frame in new[] { 0d, 49.8, clip.FrameCount * .5, clip.FrameCount - 1 })
        {
            var model = scene.Update(clip, frame, false, 0, true);
            var current = model.Bones.DistinctBy(b => b.Name).ToDictionary(b => b.Name);
            float colliderError = 0, particleError = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                Matrix4x4 Delta(int index)
                {
                    var name = layer.Source.Bones.Single(b => b.Index == index).Name;
                    Matrix4x4.Invert(BotwActorScene.Matrix(rest[name]), out var inverse);
                    return inverse * BotwActorScene.Matrix(current[name]);
                }
                foreach (var c in layer.Source.Colliders.Where(c => c.BoneIndex >= 0))
                {
                    var actual = scene.Overlay.Colliders.Single(x => x.Index == c.Index + layer.ColliderOffset);
                    colliderError = Math.Max(colliderError, Vector3.Distance(actual.Start, Vector3.Transform(c.Start, Delta(c.BoneIndex))));
                    colliderError = Math.Max(colliderError, Vector3.Distance(actual.End, Vector3.Transform(c.End, Delta(c.BoneIndex))));
                }
                foreach (var a in authored[i])
                {
                    var p = layer.Source.Particles.Single(p => p.Index == a.ParticleIndex);
                    var total = a.BoneWeights.Sum(w => w.Weight);
                    var reference = a.BoneWeights.Aggregate(Vector3.Zero, (sum, w) => sum + (w.ReferencePosition ?? p.Position) * (w.Weight / total));
                    var offset = p.Fixed ? Vector3.Zero : p.Position - reference;
                    var expected = a.BoneWeights.Aggregate(Vector3.Zero, (sum, w) => sum + Vector3.Transform((w.ReferencePosition ?? p.Position) + offset, Delta(w.BoneIndex)) * (w.Weight / total));
                    var actual = scene.Overlay.Particles.Single(x => x.Index == p.Index + layer.ParticleOffset).Position;
                    particleError = Math.Max(particleError, Vector3.Distance(actual, expected));
                }
            }
            var simulated = scene.Update(clip, frame, true, 1f / 60, true);
            var byIndex = model.Bones.ToDictionary(b => b.Index);
            bool AnimationOwned(VisualModelBone b)
            {
                if (driven.Contains(b.Name)) return false;
                return !byIndex.TryGetValue(b.ParentIndex, out var parent) || AnimationOwned(parent);
            }
            float boneError = 0;
            foreach (var b in simulated.Bones.Where(AnimationOwned))
            {
                var expected = current[b.Name];
                boneError = Math.Max(boneError, Vector3.Distance(b.Position, expected.Position));
                if (Math.Abs(Quaternion.Dot(b.Rotation, expected.Rotation)) < .9999f) failures.Add($"Frame {frame}: simulation changed animation-owned rotation {b.Name}");
            }
            Console.WriteLine($"{scene.Resources.ActorName} {clipName} frame {frame}: animation-bone error={boneError:F6}, collider error={colliderError:F6}, particle error={particleError:F6}");
            if (boneError > .001f || colliderError > .001f || particleError > .001f) failures.Add($"Frame {frame}: model/physics pose mismatch");
        }
        for (int i = 0; i < 180; i++) scene.Update(clip, (clip.FrameCount - 30 + i * .5) % clip.FrameCount, true, 1f / 60, i == 0);
        Require(layers.All(l => l.Solver.NumericalResetCount == 0), "Simulation reset during animation/loop");
        Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Actor source changed");
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
        Console.WriteLine("PASS: actor animation-owned bones, authored cloth skinning, collider alignment, simulation loops and source preservation.");
    }
}
