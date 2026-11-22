using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

internal static class OotSkinMappingChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new Exception("Malformed direct skin mapping accepted");
    }

    internal static void Run()
    {
        SkinBlendArrayData Array(ushort[] bones) => new("oneBlendEntries", 1, 1, 1, [new([0], bones, [255])]);
        var skin = new SkinOperatorData(0, "hclObjectSpaceSkinPOperator", 0, 0,
            [Matrix4x4.Identity], [], [Array([0])], [3], 0, 0, 0, false, [], []);
        HkclService.ValidateOotDirectSkinMapping(skin, 1, 1);
        Reject(() => HkclService.ValidateOotDirectSkinMapping(skin, 2, 1));
        Reject(() => HkclService.ValidateOotDirectSkinMapping(skin, 2, 2));
        Reject(() => HkclService.ValidateOotDirectSkinMapping(skin with { BlendArrays = [Array([1])] }, 1, 1));
        Reject(() => HkclService.ValidateOotDirectSkinMapping(skin with { BlendArrays = [Array([])] }, 1, 1));
        Console.WriteLine("PASS OoT direct skin mapping count/index guards.");
    }

    internal static void Native(string root)
    {
        int checkedSkins = 0;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Phive/Cloth"), "*.bphcl"))
        {
            var session = new EditorSession(); session.Load(path);
            var document = session.Document.CaptureBphclNativeDocument()!;
            var original = session.Document.CaptureState();
            foreach (var cloth in document.Cloths)
            foreach (var skin in cloth.ObjectSpaceSkins.Where(s => s.TransformSubset.Count == 0 && s.BoneFromSkinMeshTransforms.Count > 0))
            {
                Console.WriteLine($"Implicit skin: {Path.GetFileName(path)}/{cloth.Name}; binds={skin.BoneFromSkinMeshTransforms.Count}, skeleton={document.Skeletons[cloth.Index].Bones.Count}");
                var support = session.Document.GetTotkSimulationSupport(cloth.Index);
                Require(support.Supported, string.Join("; ", support.Blockers));
                var preview = session.Document.GetTotkSimulationPreview(cloth.Index);
                var decoded = session.Document.GetSkinOperatorData(cloth.Index).Single(s => s.OperatorIndex == skin.OperatorIndex);
                var expected = new Dictionary<int, Dictionary<int, float>>();
                foreach (var (array, blockIndex, _) in HkclService.GetSkinBlockOrder(decoded.BlendArrays, decoded.ControlBytes))
                {
                    var block = array.Blocks[blockIndex];
                    for (int vertex = 0; vertex < array.VertexCount; vertex++)
                    {
                        if (block.VertexIndices[vertex] == ushort.MaxValue) continue;
                        var weights = new Dictionary<int, float>();
                        for (int influence = 0; influence < array.BlendCount; influence++)
                        {
                            int i = vertex * array.BlendCount + influence;
                            if (block.BoneWeights[i] != 0)
                                weights[block.BoneIndices[i]] = weights.GetValueOrDefault(block.BoneIndices[i]) + block.BoneWeights[i];
                        }
                        float total = weights.Values.Sum();
                        expected[block.VertexIndices[vertex]] = weights.ToDictionary(p => p.Key, p => p.Value / total);
                    }
                }
                int checkedAttachments = 0;
                foreach (var move in cloth.Operators.Where(o => o.ClassName == "hclMoveParticlesOperator" && o.ReferenceBufferIndex == skin.OutputBufferIndex))
                foreach (var pair in move.VertexParticlePairs)
                {
                    var actual = preview.ParticleAttachments.Single(a => a.ParticleIndex == pair.ParticleIndex).BoneWeights;
                    var weights = expected[pair.VertexIndex];
                    Require(actual.Count == weights.Count && actual.All(w => weights.TryGetValue(w.BoneIndex, out var value) && Math.Abs(value - w.Weight) < 1e-5f), "Implicit skin used the wrong bones/weights");
                    Require(actual.All(w => w.ReferencePosition is { } p && float.IsFinite(p.LengthSquared())), "Missing direct skin reference point");
                    checkedAttachments++;
                }
                Require(checkedAttachments > 0, "Direct skin fixture did not exercise MoveParticles");
                var solver = new HkclPreviewSimulator(preview);
                var initial = solver.GetPositions();
                var translation = new Vector3(.1f, .2f, -.1f);
                solver.SetInputBoneDeltas(preview.Bones.ToDictionary(b => b.Index, _ => Matrix4x4.CreateTranslation(translation)));
                solver.Reset();
                foreach (var particle in preview.Particles.Where(p => p.Fixed))
                    Require(Vector3.Distance(solver.GetPositions()[particle.Index], initial[particle.Index] + translation) < 1e-4f, "Direct skin anchor did not follow skeleton");
                for (int i = 0; i < 120; i++) solver.Step(1f / 60);
                Require(solver.NumericalResetCount == 0 && solver.GetPositions().Values.All(p => float.IsFinite(p.LengthSquared())), "Direct skin preview was unstable");
                Require(session.Document.GetSkinOperatorData(cloth.Index).Single(s => s.OperatorIndex == skin.OperatorIndex).TransformSubset.Count == 0, "Preview materialized the authored empty subset");
                bool editBlocked = false;
                try { session.Document.ValidateOotGeometry(cloth.Index); } catch (InvalidOperationException) { editBlocked = true; }
                Require(editBlocked, "Preview change accidentally enabled unverified geometry rebuilding");
                checkedSkins++;
            }
            Require(session.Document.CaptureState() == original, "Preview mutated source cloth");
        }
        Require(checkedSkins > 0, "No direct skin fixture tested");
        Console.WriteLine($"PASS {checkedSkins} native direct skins: authored weights, animated anchors, stable simulation, unchanged source and editing guard.");
    }
}
