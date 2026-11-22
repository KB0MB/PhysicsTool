using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;

internal static class OotMalonChecks
{
    internal static void Run(string root)
    {
        var resources = OotActorResources.Load(new("Npc_Hylia_Malon_Adult", Path.Combine(root, "Pack/Actor/Npc_Hylia_Malon_Adult.pack.zs"), null, root, true, true, true) { IsOot = true });
        var rig = PortableModelImporter.LoadRig(resources.ModelPath!);
        var direct = new BfresModelPose(resources.ModelPath!);
        var helpers = resources.Assets.Where(a => !a.IsCloth).Select(a => new OotHelperEvaluator(a.Bytes, rig.Rest.Bones)).ToArray();
        var original = resources.Assets.Select(a => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(a.Bytes))).ToArray();
        var clips = resources.AnimationPaths.SelectMany(ModelAnimationClip.Load).ToArray();
        var songs = clips.Where(c => c.Name.Contains("Song", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("Storm", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (songs.Length == 0) throw new Exception("Song fixture not found.");
        foreach (var clip in songs)
        {
            var scene = new OotActorPreview(resources.Assets);
            for (int frame = 0; frame <= clip.EndFrame; frame++)
            {
                var pose = rig.AnimationSkeleton(clip, frame);
                if (clip.Name == "Act_Ocarina_StormSong" && frame % 20 == 0)
                {
                    var roundTrip = rig.Sample(pose.Bones.Select(b => VisualBoneTransform.Override(b.Name, b.WorldMatrix)).ToArray());
                    var expected = direct.Sample(null, null, clip.Native, frame);
                    for (int mesh = 0; mesh < expected.Meshes.Count; mesh++)
                        if (expected.Meshes[mesh].Vertices.Zip(roundTrip.Meshes[mesh].Vertices).Any(v => Math.Abs(v.First - v.Second) > .0001f))
                            throw new Exception("Affine animation round-trip changed native mesh positions/normals.");
                }
                foreach (var helper in helpers) pose = helper.Evaluate(pose);
                var withoutSimulation = scene.Update(rig.Rest, pose, false, 0, true, false, true);
                if (frame % 30 == 0) CheckMesh(rig.Sample(withoutSimulation));
            }
            for (int frame = 0; frame <= clip.EndFrame; frame++)
            {
                var pose = rig.AnimationSkeleton(clip, frame + .5, scene.DrivenNames);
                foreach (var helper in helpers) pose = helper.Evaluate(pose);
                var output = scene.Update(rig.Rest, pose, true, 1f / 60, frame == 0, false, true);
                if (frame % 30 == 0) CheckMesh(rig.Sample(output));
                if (scene.Overlay.Particles.Any(p => !float.IsFinite(p.Position.LengthSquared()))) throw new Exception("Nonfinite simulation.");
            }
            Console.WriteLine($"PASS: {clip.Name}, {clip.EndFrame + 1} frames, helpers, simulation off/on and fractional animation samples.");
        }
        if (!original.SequenceEqual(resources.Assets.Select(a => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(a.Bytes)))))
            throw new Exception("Animation changed actor resources.");
        void CheckMesh(VisualModelData model)
        { if (model.Meshes.Any(m => m.Vertices.Any(v => !float.IsFinite(v)))) throw new Exception("Nonfinite mesh."); }
    }
}
