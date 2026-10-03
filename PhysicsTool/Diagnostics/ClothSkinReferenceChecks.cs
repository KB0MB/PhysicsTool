using System.Numerics;
using System.Reflection;

namespace HKCLTool;

internal static class ClothSkinReferenceChecks
{
    private static IReadOnlyDictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>> Decode(
        SkinOperatorData skin, IReadOnlyDictionary<int, Matrix4x4> frames) =>
        (IReadOnlyDictionary<int, IReadOnlyList<ParticlePreviewBoneWeight>>)typeof(HkclService)
            .GetMethod("BuildSkinVertexBoneWeights", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { skin, frames })!;

    public static void Run(string directory)
    {
        SkinPositionBlockData Block(Vector4 value) => new(Enumerable.Repeat(value, 16).ToArray());
        var single = new SkinBlendArrayData("oneBlendEntries", 1, 1, 1, new[] {
            new SkinBlendBlockData(new ushort[] { 11 }, new ushort[] { 0 }, Array.Empty<byte>()),
            new SkinBlendBlockData(new ushort[] { 12 }, new ushort[] { 0 }, Array.Empty<byte>()) });
        var four = new SkinBlendArrayData("fourBlendEntries", 4, 1, 4, new[] {
            new SkinBlendBlockData(new ushort[] { 10 }, new ushort[] { 0, 0, 0, 0 }, new byte[] { 255, 0, 0, 0 }) });
        var frames = new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.CreateTranslation(10, 0, 0), [1] = Matrix4x4.CreateTranslation(20, 0, 0) };
        var skin = new SkinOperatorData(0, "hclObjectSpaceSkinPOperator", 0, 0,
            new[] { Matrix4x4.CreateScale(2) }, new ushort[] { 0 }, new[] { four, single }, new byte[] { 3, 4, 0, 3 },
            0, 12, 0, false, new[] { Block(new(1, 0, 0, 1)), Block(new(2, 0, 0, 1)), Block(new(3, 0, 0, 1)) },
            new[] { Block(new(100, 0, 0, 1)), Block(new(100, 0, 0, 1)), Block(new(100, 0, 0, 1)) });
        var decoded = Decode(skin, frames);
        Near(Point(decoded[11]), new(12, 0, 0), "One-blend block did not follow the control stream.");
        Near(Point(decoded[10]), new(14, 0, 0), "Four-blend block used grouped table order or the inactive unpacked stream.");
        Near(Point(decoded[12]), new(16, 0, 0), "Repeated one-blend control lost its independent cursor.");
        var boneFour = four with { Blocks = new[] { new SkinBlendBlockData(new ushort[] { 10 }, new ushort[] { 0, 1, 0, 0 }, Array.Empty<byte>()) } };
        var positions = new Vector4[16]; positions[0] = new(0.25f, 0, 0, 0.25f); positions[1] = new(1.5f, 0, 0, 0.75f);
        var boneSkin = skin with { ClassName = "hclBoneSpaceSkinPOperator", TransformSubset = new ushort[] { 0, 1 },
            BlendArrays = new[] { boneFour }, ControlBytes = new byte[] { 0 },
            LocalPositionBlocks = new[] { new SkinPositionBlockData(positions) }, LocalUnpackedPositionBlocks = Array.Empty<SkinPositionBlockData>() };
        decoded = Decode(boneSkin, frames);
        Near(Point(decoded[10]), new(19.25f, 0, 0), "Bone-space homogeneous weights were applied twice or dropped.");
        var data = new ParticlePreviewData { Gravity = Vector3.Zero };
        data.Particles.Add(new() { Index = 0, Position = new(-100, 0, 0), Fixed = true });
        var attachment = new ParticlePreviewAttachment { ParticleIndex = 0 }; attachment.BoneWeights.AddRange(decoded[10]); data.ParticleAttachments.Add(attachment);
        var solver = new HkclPreviewSimulator(data); solver.Reset();
        Near(solver.GetPositions()[0], new(19.25f, 0, 0), "Fixed input substituted the particle rest position for the native reference.");
        solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4> { [0] = Matrix4x4.CreateTranslation(4, 0, 0) });
        solver.Step(1f / 60);
        Near(solver.GetPositions()[0], new(20.25f, 0, 0), "Reference influences did not receive their own animated transforms.");
        File.WriteAllText(Path.Combine(directory, "skin-reference.txt"), "PASS: native control order, independent block cursors, separator, packed precedence, bind scale, weighted bone-space positions, and animated fixed references.");
    }

    public static void CheckAsset(string path, string directory, int fileIndex)
    {
        var service = new HkclService(); service.Load(path);
        var delta = new Vector3(0.007f, -0.003f, 0.005f);
        for (var cloth = 0; cloth < service.GetClothSummaries().Count; cloth++)
        foreach (var singleParticle in new[] { false, true })
        {
            var before = service.GetParticlePreview(cloth);
            var selected = before.ParticleAttachments.LastOrDefault()?.ParticleIndex ?? -1;
            if (singleParticle && selected < 0) continue;
            var previous = service.GetParticleRows(cloth);
            var current = service.GetParticleRows(cloth);
            foreach (var row in current.Where(row => !singleParticle || row.Index == selected))
                { row.X += delta.X; row.Y += delta.Y; row.Z += delta.Z; }
            service.UpdateParticleRows(cloth, current);
            service.SynchronizeParticleConstraintGeometry(cloth, previous, current);
            var output = Path.Combine(directory, $"skin-edit-{fileIndex}-{cloth}-{singleParticle}.hkcl");
            service.SaveHkcl(output, HkclService.DetectHkclPlatform(path));
            var loaded = new HkclService(); loaded.Load(output);
            var after = loaded.GetParticlePreview(cloth);
            foreach (var attachment in before.ParticleAttachments.Where(a => a.BoneWeights.All(w => w.ReferencePosition.HasValue)))
            {
                var actual = after.ParticleAttachments.Single(a => a.ParticleIndex == attachment.ParticleIndex);
                var shift = !singleParticle || attachment.ParticleIndex == selected ? delta : Vector3.Zero;
                Near(Point(actual.BoneWeights), Point(attachment.BoneWeights) + shift, $"Cloth {cloth}: fixed reference edit did not round-trip.", 0.00015f);
            }
            for (var i = 0; i < before.LocalRanges.Count; i++)
                if (before.LocalRanges[i].ReferenceBoneWeights is { Count: > 0 } weights && weights.All(w => w.ReferencePosition.HasValue))
                {
                    var shift = !singleParticle || before.LocalRanges[i].ParticleIndex == selected ? delta : Vector3.Zero;
                    Near(Point(after.LocalRanges[i].ReferenceBoneWeights), Point(weights) + shift, $"Cloth {cloth}: movement-limit reference did not follow the edit.", 0.00015f);
                }
        }
    }

    private static Vector3 Point(IReadOnlyList<ParticlePreviewBoneWeight> weights) =>
        weights.Aggregate(Vector3.Zero, (sum, w) => sum + w.ReferencePosition!.Value * w.Weight) / weights.Sum(w => w.Weight);
    private static void Near(Vector3 actual, Vector3 expected, string message, float tolerance = 0.00002f)
    {
        if (!float.IsFinite(actual.LengthSquared()) || Vector3.Distance(actual, expected) > tolerance)
            throw new InvalidOperationException($"{message} Expected {expected}, actual {actual}.");
    }
}
