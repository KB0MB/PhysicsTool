using HKCLTool;
using System.Numerics;
using System.Reflection;
using System.Collections;

internal static class GanondorfChecks
{
    public static void References(string path)
    {
        var codec = new TotkResourceCompression();
        var pack = BotwSarcArchive.Parse(codec.Decode(File.ReadAllBytes(path), TotkResourceCompression.Roots(path)));
        foreach (var entry in pack.Entries.Where(e => e.Name.EndsWith(".bgyml") &&
            (e.Name.StartsWith("Phive/ControllerSet") || e.Name.StartsWith("Phive/ClothParam") || e.Name.StartsWith("Phive/HelperBoneParam"))))
            Console.WriteLine(entry.Name + "\n" + BymlLibrary.Byml.FromBinary(pack.Read(entry).ToArray()).ToYaml());
    }
    public static void Inspect(string path)
    {
        using var scene = BotwActorScene.Load(path);
        var layers = ((IEnumerable)typeof(BotwActorScene).GetField("_layers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!).Cast<object>().ToArray();
        var sources = layers.Select(l => (ParticlePreviewData)l.GetType().GetProperty("Source")!.GetValue(l)!).ToArray();
        var solvers = layers.Select(l => (HkclPreviewSimulator)l.GetType().GetProperty("Solver")!.GetValue(l)!).ToArray();
        Console.WriteLine(string.Join(", ", scene.Animations.Select(a => a.Name)));
        for (int i = 0; i < sources.Length; i++)
        {
            var data = sources[i];
            Console.WriteLine($"LAYER {i}: {data.Particles.Count} particles, {data.Particles.Count(p => p.Fixed)} fixed, {data.ParticleAttachments.Count} attachments, {data.BoneBindings.Count} bindings");
            Console.WriteLine("Driven: " + string.Join(", ", data.Bones.Where(b => solvers[i].DrivenBoneIndices.Contains(b.Index)).Select(b => b.Index + ":" + b.Name)));
            foreach (var p in data.Particles.Where(p => p.Fixed))
                Console.WriteLine($" Fixed {p.Index} {p.Position} weights=" + string.Join(",", data.ParticleAttachments.Where(a => a.ParticleIndex == p.Index).SelectMany(a => a.BoneWeights).Select(w => data.Bones.FirstOrDefault(b => b.Index == w.BoneIndex)?.Name + ":" + w.Weight)));
        }
        foreach (var clip in scene.Animations.Take(3))
        {
            scene.Update(clip, 0, true, 0, true);
            var initial = solvers.Select(s => s.GetPositions()).ToArray();
            for (int f = 0; f < 180; f++) scene.Update(clip, f % Math.Max(1, clip.FrameCount), true, 1f / 60);
            Console.WriteLine("SIM " + clip.Name);
            foreach (var b in scene.CurrentModel.Bones.Where(b => b.Name.Contains("Earrings") || b.Name.Contains("Pendant")))
            {
                var rest = scene.RestModel.Bones.First(x => x.Name == b.Name);
                Console.WriteLine($"MODEL {b.Name}: rest={rest.Position} posed={b.Position} scale={b.Scale} rotationDot={Quaternion.Dot(rest.Rotation, b.Rotation)}");
            }
            for (int i = 0; i < sources.Length; i++)
            {
                var positions = solvers[i].GetPositions();
                var poses = solvers[i].GetBonePoses();
                Console.WriteLine($" L{i}: maxMovement={positions.Max(p => Vector3.Distance(p.Value, initial[i][p.Key])):F4} fallback={solvers[i].LastBindingFallbackCount} resets={solvers[i].NumericalResetCount}");
                foreach (var b in sources[i].Bones.Where(b => solvers[i].DrivenBoneIndices.Contains(b.Index)))
                    if (b.Name.Contains("Neck", StringComparison.OrdinalIgnoreCase) || b.Name.Contains("Ear", StringComparison.OrdinalIgnoreCase))
                        Console.WriteLine($"  {b.Name}: rest={b.Position} pose={poses[b.Index].Position}");
            }
        }
    }
}
