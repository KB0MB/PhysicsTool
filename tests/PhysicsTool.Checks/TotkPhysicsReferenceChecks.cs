using BymlLibrary;
using HKCLTool;
using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;

internal static class TotkPhysicsReferenceChecks
{
    public static void Resources(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var resources = new BotwActorResources(path);
            foreach (var file in resources.PhysicsFiles)
                if (resources.Read(file) == null) throw new Exception($"Missing active physics for {resources.ActorName}: {file}");
            Console.WriteLine($"PASS: {resources.ActorName}: " + string.Join(", ", resources.PhysicsFiles.Select(Path.GetFileName)));
        }
    }
    public static void Synthetic()
    {
        static Byml Items(params Byml[] values) => new(values.ToList());
        static Byml Map(string key, string value) => new(new Dictionary<string, Byml> { [key] = new(value) });
        var controller = new Dictionary<string, Byml> {
            ["ClothList"] = Items(Map("Path", "Work/Phive/Cloth/Active.phcl"), Map("Path", "?Phive/Cloth/Active.phcl")),
            ["Cloth"] = Items(Map("FilePath", "Work/Unused.gyml")),
            ["HelperBoneList"] = Items(Map("FilePath", "Work/Phive/HelperBone/Active.phhb")) };
        var files = BotwActorResources.ResolveTotkPhysicsFiles(controller, _ => throw new Exception("Explicit cloth list must take precedence"));
        if (!files.SequenceEqual(new[] { "Phive/Cloth/Active.bphcl", "Phive/HelperBone/Active.bphhb" })) throw new Exception("Active physics lists were not resolved/deduplicated");
        controller["ClothList"] = Items(); controller["HelperBoneList"] = Items();
        if (BotwActorResources.ResolveTotkPhysicsFiles(controller, _ => throw new Exception("Empty list must disable cloth")).Count != 0) throw new Exception("Disabled cloth reintroduced");
        controller.Remove("ClothList");
        files = BotwActorResources.ResolveTotkPhysicsFiles(controller, _ => new() { ["HktPath"] = new("Work/Phive/Cloth/Legacy.hkt") });
        if (files.Single() != "Phive/Cloth/Legacy.bphcl") throw new Exception("Cloth-parameter reference was not resolved");
        Console.WriteLine("PASS: TotK active cloth/helper references, path normalization, deduplication, explicit-empty lists and HKT fallback.");
    }
    public static void Native(string path)
    {
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        using var scene = BotwActorScene.Load(path);
        if (scene.Resources.ActorName != "Enemy_Ganondorf") throw new Exception("This regression fixture expects Enemy_Ganondorf");
        if (!scene.Resources.PhysicsFiles.SequenceEqual(new[] { "Phive/Cloth/Npc_Ganondorf_Human_Battle.bphcl", "Phive/HelperBone/Npc_Ganondorf_Human_Battle.bphhb" }))
            throw new Exception("Ganondorf loaded inactive form physics");
        var layers = ((IEnumerable)typeof(BotwActorScene).GetField("_layers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scene)!).Cast<object>()
            .Select(l => (Data: (ParticlePreviewData)l.GetType().GetProperty("Source")!.GetValue(l)!, Solver: (HkclPreviewSimulator)l.GetType().GetProperty("Solver")!.GetValue(l)!)).ToArray();
        var driven = layers.SelectMany(l => l.Data.Bones.Where(b => l.Solver.DrivenBoneIndices.Contains(b.Index)).Select(b => b.Name)).ToArray();
        if (driven.Distinct(StringComparer.OrdinalIgnoreCase).Count() != driven.Length) throw new Exception("Multiple active cloths overwrite the same model bones");
        var rest = scene.RestModel.Bones.ToDictionary(b => b.Name);
        float maxAnchorError = 0, maxJewelryRootError = 0;
        foreach (var clip in scene.Animations.Where(a => a.Name is "Attack_Bow_Reload" or "Sword_Lockon_Wait" or "Attack_Sword_Combo"))
        {
            scene.Update(clip, 0, true, 0, true);
            for (int frame = 0; frame < 180; frame++)
            {
                scene.Update(clip, frame % Math.Max(1, clip.FrameCount), true, 1f / 60);
                var current = scene.CurrentModel.Bones.ToDictionary(b => b.Name);
                foreach (var (data, solver) in layers.Where(l => l.Data.Bones.Any(b => b.Name.StartsWith("Earrings_") || b.Name.StartsWith("Pendant_"))))
                {
                    var positions = solver.GetPositions();
                    foreach (var p in data.Particles.Where(p => p.Fixed))
                    {
                        var weights = data.ParticleAttachments.Single(a => a.ParticleIndex == p.Index).BoneWeights;
                        var expected = Vector3.Zero; float total = 0;
                        foreach (var w in weights)
                        {
                            var name = data.Bones.Single(b => b.Index == w.BoneIndex).Name;
                            Matrix4x4.Invert(BotwActorScene.Matrix(rest[name]), out var inverse);
                            expected += Vector3.Transform(w.ReferencePosition ?? p.Position, inverse * BotwActorScene.Matrix(current[name])) * w.Weight;
                            total += w.Weight;
                        }
                        maxAnchorError = Math.Max(maxAnchorError, Vector3.Distance(expected / total, positions[p.Index]));
                    }
                    if (solver.NumericalResetCount != 0 || positions.Values.Any(v => !float.IsFinite(v.LengthSquared()))) throw new Exception("Jewelry simulation became nonfinite");
                }
                foreach (var b in current.Values.Where(b => b.Name.StartsWith("Earrings_") || b.Name == "Pendant_1"))
                {
                    var reference = rest[b.Name]; var parent = rest.Values.Single(p => p.Index == reference.ParentIndex);
                    Matrix4x4.Invert(BotwActorScene.Matrix(parent), out var inverse);
                    var expected = Vector3.Transform(reference.Position, inverse * BotwActorScene.Matrix(current[parent.Name]));
                    maxJewelryRootError = Math.Max(maxJewelryRootError, Vector3.Distance(expected, b.Position));
                }
            }
        }
        Console.WriteLine($"Ganondorf: {scene.ClothCount} cloths; max fixed-anchor error={maxAnchorError:F7}, max jewelry attachment displacement={maxJewelryRootError:F5}");
        if (maxAnchorError > .0001f || maxJewelryRootError > .04f) throw new Exception("Jewelry attachment drift exceeded tolerance");
        if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))) throw new Exception("Actor asset changed");
        Console.WriteLine("PASS: native Ganondorf active resources, unique bone ownership, fixed anchors, jewelry roots, animation loops and source preservation.");
    }
}
