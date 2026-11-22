using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;
using System.Diagnostics;
using System.Numerics;

internal static class OotPlaybackChecks
{
    internal static void Audit(string root)
    {
        int clips = 0, tracks = 0, bottleClips = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Model"), "Player_Adult*Animation*.bfres.zs"))
        {
            var bytes = new TotkResourceCompression().Decode(File.ReadAllBytes(path), [root]);
            var document = new ToolboxBfresDocument(bytes, path);
            foreach (dynamic animation in document.SkeletalAnimations)
            {
                var sample = new ToolboxActorAnimation((object)animation, path);
                if (sample.FramesPerSecond != 60 || new ModelAnimationClip(sample).FramesPerSecond != 60)
                    throw new Exception("OoT clip did not retain its 60 FPS playback rate");
                clips++;
                if (sample.Name.Contains("Bottle"))
                {
                    bottleClips++;
                    if (sample.Name.Contains("Open") || sample.Name.Contains("Drink")) Console.WriteLine(sample.Name);
                }
                foreach (dynamic bone in ToolboxBfresDocument.Items((object)animation.BoneAnims))
                {
                    if (!OotAnimationTracks.TryGet((object)bone, out var native) || native.Curve is not { } curve) continue;
                    tracks++;
                    foreach (int k in new[] { 0, curve.Keys.Length / 2, curve.Keys.Length - 1 }.Distinct())
                    {
                        var p = Vector3.Zero; var q = Vector4.UnitW; var scale = Vector3.One; bool euler = true;
                        sample.Sample((string)bone.Name, curve.Frames[k], ref p, ref q, ref scale, ref euler);
                        if (euler || MathF.Abs(Quaternion.Dot(new(q.X, q.Y, q.Z, q.W), curve.Keys[k])) < .99999f)
                            throw new Exception("Mixed quaternion key sampled as scalar/Euler: " + sample.Name + "/" + bone.Name);
                    }
                    if (curve.Keys.Length > 1)
                    {
                        var midpoint = curve.Sample((curve.Frames[0] + curve.Frames[1]) * .5);
                        var expected = Quaternion.Slerp(curve.Keys[0], curve.Keys[1], .5f);
                        if (MathF.Abs(Quaternion.Dot(midpoint, expected)) < .99999f) throw new Exception("Quaternion interpolation changed");
                    }
                }
            }
        }
        if (tracks < 100 || bottleClips == 0) throw new Exception("Mixed quaternion fixtures missing");
        Console.WriteLine($"PASS: {clips} Player_Adult clips, {tracks} mixed quaternion tracks, {bottleClips} bottle clips; keys, endpoints and interpolation");
    }
    internal static void Run(string root)
    {
        var path = Path.Combine(root, "Model/Player_Adult_01_Animation.anim.bfres.zs");
        var bytes = new TotkResourceCompression().Decode(File.ReadAllBytes(path), [root]);
        var doc = new ToolboxBfresDocument(bytes, path);
        foreach (dynamic anim in doc.SkeletalAnimations.Take(2))
        {
            var sample = new ToolboxActorAnimation(anim, path);
            foreach (dynamic bone in ToolboxBfresDocument.Items((object)anim.BoneAnims))
            {
                var p = Vector3.Zero; var r = Vector4.UnitW; var s = Vector3.One; bool euler = false;
                sample.Sample((string)bone.Name, 0, ref p, ref r, ref s, ref euler);
                if (bone.UseRotation && euler == OotAnimationTracks.TryGet((object)bone, out _)) throw new Exception("OoT mixed rotation mode lost");
            }
        }
        var rig = PortableModelImporter.LoadRig(Path.Combine(root, "Model/Player_Adult.Player_Adult.bfres.zs"));
        var clip = ModelAnimationClip.Load(path).First(c => c.EndFrame > 20);
        if (clip.FramesPerSecond != 60) throw new Exception("Standalone OoT BFRES playback is not 60 FPS");
        var watch = Stopwatch.StartNew();
        VisualModelData? skinned = null;
        for (int i = 0; i < 60; i++)
        {
            var pose = rig.AnimationSkeleton(clip, i % 20);
            skinned = rig.Sample(pose.Bones.Select(b => new VisualBoneOverride(b.Name, b.Position, b.Rotation, b.Scale)).ToArray());
        }
        Console.WriteLine($"Skinning: {watch.Elapsed.TotalMilliseconds / 60:F2} ms/frame");
        if (skinned!.Meshes.Where(m => rig.Rest.Meshes.Single(s => s.Name == m.Name).Tangents.Length > 0).Any(m => m.Tangents.Length == 0))
            throw new Exception("Skinned tangent streams lost");
        watch.Restart();
        for (int i = 0; i < 20; i++) foreach (var mesh in skinned.Meshes) _ = ModelVertices.Build(mesh);
        Console.WriteLine($"GPU packing: {watch.Elapsed.TotalMilliseconds / 20:F2} ms/frame");
        int reused = 0;
        var packed = rig.Rest.Meshes.Select(ModelVertices.Build).ToArray();
        for (int i = 0; i < packed.Length; i++)
        {
            if (!ModelVertices.UpdateOotPose(rig.Rest.Meshes[i], skinned.Meshes[i], packed[i])) continue;
            reused++;
            if (!packed[i].SequenceEqual(ModelVertices.Build(skinned.Meshes[i]))) throw new Exception("Cached pose packing differs from full rebuild");
        }
        if (reused == 0) throw new Exception("No OoT dynamic streams reused");
        watch.Restart();
        for (int j = 0; j < 120; j++) for (int i = 0; i < packed.Length; i++) ModelVertices.UpdateOotPose(rig.Rest.Meshes[i], skinned.Meshes[i], packed[i]);
        Console.WriteLine($"Cached GPU packing: {watch.Elapsed.TotalMilliseconds / 120:F2} ms/frame ({reused} meshes)");
        var entry = new BotwActorCatalogEntry("Player_Adult", Path.Combine(root, "Pack/Actor/Player_Adult.pack.zs"), null, root, true, true, true) { IsOot = true };
        var resources = OotActorResources.Load(entry);
        var evaluator = new OotHelperEvaluator(resources.Assets.Single(a => !a.IsCloth).Bytes, rig.Rest.Bones);
        if (evaluator.DrivenCount == 0 || evaluator.Warnings.Count > 0) throw new Exception(string.Join("\n", evaluator.Warnings));
        var input = rig.AnimationSkeleton(clip, 8);
        var original = input.Bones.Select(b => b.Position).ToArray();
        var result = evaluator.Evaluate(input);
        var repeated = evaluator.Evaluate(input);
        if (!original.SequenceEqual(input.Bones.Select(b => b.Position))) throw new Exception("Helpers changed source animation");
        if (!result.Bones.Select(b => b.Position).SequenceEqual(repeated.Bones.Select(b => b.Position))) throw new Exception("Helper feedback on held frame");
        if (original.SequenceEqual(result.Bones.Select(b => b.Position))) throw new Exception("Helpers did not affect pose");
        var reversed = new VisualModelData();
        reversed.Bones.AddRange(input.Bones.AsEnumerable().Reverse());
        var reordered = new OotHelperEvaluator(resources.Assets.Single(a => !a.IsCloth).Bytes, reversed.Bones).Evaluate(reversed);
        var expectedByName = result.Bones.ToDictionary(b => b.Name);
        foreach (var bone in reordered.Bones)
        {
            var expected = expectedByName[bone.Name];
            if (Vector3.Distance(bone.Position, expected.Position) > .0001f ||
                MathF.Abs(Quaternion.Dot(bone.Rotation, expected.Rotation)) < .9999f)
                throw new Exception("Helper pose depends on skeleton order: " + bone.Name);
        }
        var translated = new VisualModelData();
        var delta = new Vector3(.2f, 1f, -.3f);
        translated.Bones.AddRange(input.Bones.Select(b => new VisualModelBone { Index = b.Index, ParentIndex = b.ParentIndex, Name = b.Name, Position = b.Position + delta, Rotation = b.Rotation, Scale = b.Scale }));
        var moved = evaluator.Evaluate(translated);
        if (result.Bones.Zip(moved.Bones).Any(p => Vector3.Distance(p.First.Position + delta, p.Second.Position) > .001f)) throw new Exception("Helpers detached under root movement");
        watch.Restart();
        for (int i = 0; i < 120; i++) _ = evaluator.Evaluate(input);
        Console.WriteLine($"PASS: {evaluator.DrivenCount} Player_Adult helpers; {watch.Elapsed.TotalMilliseconds / 120:F2} ms/pose; no feedback, source mutation or root detachment");
    }
}
