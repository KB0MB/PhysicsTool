using System.Diagnostics;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;

internal static class OotPerformanceChecks
{
    internal static void Run(string root, string actor)
    {
        var entry = new BotwActorCatalogEntry(actor, Path.Combine(root, "Pack/Actor", actor + ".pack.zs"), null, root, true, true, true) { IsOot = true };
        var resources = OotActorResources.Load(entry);
        Console.WriteLine($"Actor {actor}: {resources.Assets.Count} physics resources, model {resources.ModelPath}");
        foreach (var asset in resources.Assets.Where(a => a.IsCloth))
        {
            var document = new HkclService();
            document.RestoreState("BPHCL:" + Convert.ToBase64String(asset.Bytes));
            for (int i = 0; i < document.GetClothSummaries().Count; i++)
            {
                var support = document.GetTotkSimulationSupport(i);
                if (!support.Supported) { Console.WriteLine($"BLOCKED {document.GetClothName(i)}: {string.Join(';', support.Blockers)}"); continue; }
                var source = document.GetTotkSimulationPreview(i);
                source.SolverProfile = ClothSolverProfile.OotExperimental;
                var solver = new HkclPreviewSimulator(source);
                Console.WriteLine($"Cloth {document.GetClothName(i)}: {source.Particles.Count} particles, {source.Bones.Count} bones, {source.Colliders.Count} colliders, {solver.VirtualCollisionPointCount} virtual points");
                Console.WriteLine($"  {source.SimulationSubSteps} substeps, {source.SimulationSolveIterations} iterations; kernels: {string.Join(", ", source.ConstraintSets.Select(s => s.Kind))}");
                Measure("  solve", () => solver.Step(1f / 60));
                if (solver.NumericalResetCount != 0) throw new Exception("Solver reset during benchmark");
            }
        }
        var rig = PortableModelImporter.LoadRig(resources.ModelPath!);
        foreach (var mesh in rig.Rest.Meshes) Console.WriteLine($"Mesh {mesh.Name}: {mesh.Vertices.Length / 8} vertices, {mesh.Indices.Distinct().Count()} referenced");
        var helpers = resources.Assets.Where(a => !a.IsCloth).Select(a => new OotHelperEvaluator(a.Bytes, rig.Rest.Bones)).ToArray();
        VisualModelData clean = rig.Rest;
        Measure("Helpers", () => { clean = rig.Rest; foreach (var helper in helpers) clean = helper.Evaluate(clean); });
        var scene = new OotActorPreview(resources.Assets);
        IReadOnlyList<VisualBoneOverride> overrides = [];
        Measure("All cloths without output", () => scene.Update(rig.Rest, clean, true, 1f / 60, false, false, false));
        Measure("All cloths with output", () => overrides = scene.Update(rig.Rest, clean, true, 1f / 60, false, false, true));
        VisualModelData skinned = rig.Rest;
        Measure("Skinning", () => skinned = rig.Sample(overrides));
        Measure("Complete changing pose", () =>
        {
            clean = rig.Rest; foreach (var helper in helpers) clean = helper.Evaluate(clean);
            overrides = scene.Update(rig.Rest, clean, true, 1f / 60, false, false, true);
            skinned = rig.Sample(overrides);
        });
        var packed = rig.Rest.Meshes.Select(ModelVertices.Build).ToArray();
        Measure("GPU packing", () => { for (int i = 0; i < packed.Length; i++) ModelVertices.UpdateOotPose(rig.Rest.Meshes[i], skinned.Meshes[i], packed[i]); });
        int clips = 0;
        foreach (var path in resources.AnimationPaths)
        {
            try
            {
                var loaded = ModelAnimationClip.Load(path, allowEmptyBfres: true);
                clips += loaded.Count;
                Console.WriteLine($"Animations {Path.GetFileName(path)}: {loaded.Count} ({string.Join(", ", loaded.Take(3).Select(c => c.Name))})");
            }
            catch (Exception ex) { Console.WriteLine($"Animations {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}"); }
        }
        if (actor == "Enemy_DungeonBoss_Twinrova" && clips != 44) throw new Exception("Twinrova's animation list was truncated.");
        CheckCompaction(root, resources, overrides);
    }

    private static void CheckCompaction(string root, OotActorResources resources, IReadOnlyList<VisualBoneOverride> overrides)
    {
        var bytes = new TotkResourceCompression().Decode(File.ReadAllBytes(resources.ModelPath!), [root]);
        var doc = new ToolboxBfresDocument(bytes, resources.ModelPath);
        string name = (string)doc.Models.First().Name;
        var full = new ToolboxActorModel(doc, name, new(), compactOotVertices: false);
        var compact = new ToolboxActorModel(doc, name, new());
        Compare(full.RestPose, compact.RestPose);
        var inputs = overrides.ToDictionary(b => b.Name);
        Compare(full.Pose(null, 0, inputs), compact.Pose(null, 0, inputs));
        Console.WriteLine($"Vertex count: {full.RestPose.Meshes.Sum(m => m.Vertices.Length / 8):N0} -> {compact.RestPose.Meshes.Sum(m => m.Vertices.Length / 8):N0}; triangles {compact.RestPose.TriangleCount:N0}");
        Measure("Same-run original skinning", () => full.Pose(null, 0, inputs));
        Measure("Same-run compact skinning", () => compact.Pose(null, 0, inputs));
        foreach (var path in resources.AnimationPaths)
        {
            var animDoc = new ToolboxBfresDocument(new TotkResourceCompression().Decode(File.ReadAllBytes(path), [root]), path);
            foreach (object native in animDoc.SkeletalAnimations.Take(2))
            {
                var animation = new ToolboxActorAnimation(native, path);
                foreach (double frame in new[] { 0.0, 8.5, 25.0 })
                    Compare(full.Pose(animation, frame), compact.Pose(animation, frame));
            }
        }
        Console.WriteLine("PASS: every indexed GPU vertex matches the uncompacted mesh in rest, cloth/helper and animated poses; UVs, tangents, colors and triangle ordering preserved.");
    }

    private static void Compare(VisualModelData full, VisualModelData compact)
    {
        if (full.Meshes.Count != compact.Meshes.Count || full.TriangleCount != compact.TriangleCount) throw new Exception("Mesh compaction changed topology.");
        for (int i = 0; i < full.Meshes.Count; i++)
        {
            var a = full.Meshes[i]; var b = compact.Meshes[i];
            if (a.Name != b.Name || a.Indices.Length != b.Indices.Length) throw new Exception("Mesh compaction changed draw order.");
            var av = ModelVertices.Build(a); var bv = ModelVertices.Build(b);
            int stride = av.Length / (a.Vertices.Length / 8);
            for (int j = 0; j < a.Indices.Length; j++)
                if (!av.AsSpan((int)a.Indices[j] * stride, stride).SequenceEqual(bv.AsSpan((int)b.Indices[j] * stride, stride)))
                    throw new Exception($"Mesh compaction changed {a.Name}, corner {j}.");
        }
    }

    private static void Measure(string name, Action action)
    {
        for (int i = 0; i < 32; i++) action();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var times = new double[60];
        for (int i = 0; i < times.Length; i++)
        {
            long start = Stopwatch.GetTimestamp(); action();
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(times);
        Console.WriteLine($"{name}: median {times[30]:F3} ms, p95 {times[57]:F3} ms, {allocated / times.Length:N0} bytes/frame");
    }
}
