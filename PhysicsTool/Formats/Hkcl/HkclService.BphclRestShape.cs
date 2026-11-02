using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    private void ApplyBphclRestShape(int clothIndex, ParticlePreviewData preview,
        IReadOnlyDictionary<int, Vector3> old, IReadOnlyDictionary<int, Vector3> positions,
        IReadOnlyDictionary<int, float> scales, IReadOnlyDictionary<int, Matrix4x4> desiredBones)
    {
        var native = _bphcl!.NativeDocument!;
        var cloth = native.Cloths[clothIndex];
        var edits = new List<NativeBphclConstraintEdit>();
        foreach (var set in cloth.SimCloths[0].ConstraintSets)
        {
            foreach (var link in set.Links)
            {
                var a = link.ParticleA ?? -1; var b = link.ParticleB ?? -1;
                if (!positions.ContainsKey(a) || !positions.ContainsKey(b)) throw new InvalidDataException("A constraint references a missing particle.");
                var values = new Dictionary<string, float>();
                if (set.ClassName == "hclBendStiffnessConstraintSet")
                {
                    var c = checked((int)link.Values["particleC"]); var d = checked((int)link.Values["particleD"]);
                    var ids = new[] { a, b, c, d };
                    var ratios = (from i in Enumerable.Range(0, 4) from j in Enumerable.Range(i + 1, 3 - i)
                        let length = Vector3.Distance(old[ids[i]], old[ids[j]]) where length > 1e-7f
                        select Vector3.Distance(positions[ids[i]], positions[ids[j]]) / length).ToArray();
                    if (ratios.Length > 0 && ratios.Min() > 1e-6f && ratios.Max() - ratios.Min() < 1e-4f * ratios.Average())
                        values["restCurvature"] = (float)link.Values["restCurvature"] / ratios.Average();
                    else
                    {
                        if (!HkclToBphclStandaloneGraphBuilder.TryGetBendRestConfiguration(positions[a], positions[b], positions[c], positions[d],
                            out var wa, out var wb, out var wc, out var wd, out var curvature, out _, validateCandidate: false))
                            throw new InvalidOperationException("A surface bend cannot be rebuilt for this shape. Try smaller bone edits.");
                        values["weightA"] = wa; values["weightB"] = wb; values["weightC"] = wc; values["weightD"] = wd;
                        values["restCurvature"] = curvature;
                    }
                }
                else
                {
                    var length = Vector3.Distance(old[a], old[b]); var replacement = Vector3.Distance(positions[a], positions[b]);
                    if (length < 1e-8f || replacement < 1e-8f) throw new InvalidOperationException("A fitted constraint has zero length.");
                    foreach (var field in new[] { "restLength", "bendMinLength", "stretchMaxLength", "compressionLength" })
                        if (link.Values.TryGetValue(field, out var value)) values[field] = (float)value * replacement / length;
                }
                edits.Add(new(set.Index, link.Index, false, values));
            }
            foreach (var (record, index) in set.LocalConstraints.Concat(set.LocalStiffnessConstraints).Select((record, index) => (record, index)))
            {
                var particle = checked((int)record.Values.GetValueOrDefault("particleIndex", record.ParticleA ?? -1));
                if (!scales.TryGetValue(particle, out var scale)) throw new InvalidDataException("A local range references a missing particle.");
                var values = new Dictionary<string, float>();
                foreach (var field in new[] { "maximumDistance", "maxDistance", "shapeRadius", "maxNormalDistance", "minNormalDistance" })
                    if (record.Values.TryGetValue(field, out var value) && double.IsFinite(value) && Math.Abs(value) < 1e20)
                        values[field] = (float)value * scale;
                edits.Add(new(set.Index, index, true, values));
            }
        }

        var referenceBefore = new Dictionary<int, Vector3>(old);
        foreach (var attachment in GetParticleBoneAttachments(clothIndex))
        {
            var weights = attachment.BoneWeights;
            if (weights.Count == 0 || weights.Any(w => !w.ReferencePosition.HasValue || !float.IsFinite(w.Weight) || w.Weight <= 0)) continue;
            referenceBefore[attachment.ParticleIndex] = weights.Aggregate(Vector3.Zero, (p, w) => p + w.ReferencePosition!.Value * w.Weight) / weights.Sum(w => w.Weight);
        }
        var skins = RebaseBphclSkinPositions(clothIndex, referenceBefore, positions);
        var matrices = new Dictionary<int, IReadOnlyList<Matrix4x4>>();
        foreach (var deformer in cloth.SimpleMeshBoneDeformers)
            matrices[deformer.ItemIndex] = deformer.TriangleBonePairs.Select(pair => {
                var t = preview.Triangles[pair.TriangleOffset / 6];
                var frame = FitTriangleFrame(positions[t.ParticleA], positions[t.ParticleB], positions[t.ParticleC]);
                if (!Matrix4x4.Invert(frame, out var inverse)) throw new InvalidOperationException("A fitted triangle collapsed.");
                return desiredBones[pair.BoneOffset / 64] * inverse;
            }).ToArray();
        var updated = NativeBphclParticleEditor.FitRestShape(native, clothIndex, positions, scales, edits, skins, matrices);
        _bphcl = CreateBphclSummary(updated, _bphcl.SourcePath);
    }

    private IReadOnlyList<NativeBphclSkinPositionEdit> RebaseBphclSkinPositions(int clothIndex,
        IReadOnlyDictionary<int, Vector3> previous, IReadOnlyDictionary<int, Vector3> current)
    {
        var cloth = _bphcl!.NativeDocument!.Cloths[clothIndex];
        var vertices = new Dictionary<(int Buffer, int Vertex), List<Vector3>>();
        void Add(int buffer, int vertex, int particle)
        {
            if (!previous.TryGetValue(particle, out var before) || !current.TryGetValue(particle, out var after) || vertex < 0)
                throw new InvalidDataException("A skin reference points outside the cloth.");
            var key = (buffer, vertex);
            if (!vertices.TryGetValue(key, out var values)) vertices[key] = values = new();
            values.Add(after - before);
        }
        var simulationBuffers = cloth.BufferDefinitions.Where(b => b.Type is 1 or 2 && b.SubType == 0).Select(b => b.Index).ToHashSet();
        foreach (var op in cloth.Operators)
        {
            if (op.ClassName == "hclMoveParticlesOperator")
                foreach (var pair in op.VertexParticlePairs) Add(op.ReferenceBufferIndex ?? -1, pair.VertexIndex, pair.ParticleIndex);
            if (!simulationBuffers.Contains(op.OutputBufferIndex ?? -1)) continue;
            if (op.ClassName == "hclCopyVerticesOperator")
                for (var i = 0; i < op.VertexCount; i++) Add(op.InputBufferIndex ?? -1, (op.StartVertexIn ?? 0) + i, (op.StartVertexOut ?? 0) + i);
            if (op.ClassName == "hclGatherAllVerticesOperator")
                for (var i = 0; i < op.VertexInputFromVertexOutput.Count; i++) Add(op.InputBufferIndex ?? -1, op.VertexInputFromVertexOutput[i], i);
        }
        foreach (var set in cloth.SimCloths[0].ConstraintSets.Where(s => s.ClassName is "hclLocalRangeConstraintSet" or "hclTransitionConstraintSet"))
            foreach (var record in set.LocalConstraints.Concat(set.LocalStiffnessConstraints).Concat(set.TransitionParticles))
                Add(checked((int)set.Values.GetValueOrDefault("referenceMeshBufferIdx")), checked((int)record.Values["referenceVertex"]),
                    checked((int)record.Values.GetValueOrDefault("particleIndex", record.ParticleA ?? -1)));

        var frames = BoneEditGeometry.WorldTransforms(GetBoneRows(clothIndex));
        var result = new List<NativeBphclSkinPositionEdit>();
        var resolved = new HashSet<(int Buffer, int Vertex)>();
        foreach (var skin in GetSkinOperatorData(clothIndex))
        {
            var objectSpace = skin.ClassName == "hclObjectSpaceSkinPOperator";
            var packed = skin.LocalPositionBlocks.Count > 0;
            var source = packed ? skin.LocalPositionBlocks : skin.LocalUnpackedPositionBlocks;
            var blocks = source.Select(b => b.NativePackedValues is { } raw
                ? UnpackBphclObjectSpacePositions([new(raw)])[0].Positions.ToArray() : b.Positions.ToArray()).ToArray();
            var changed = false;
            foreach (var (array, block, position) in GetSkinBlockOrder(skin.BlendArrays, skin.ControlBytes))
            {
                if (position >= blocks.Length) throw new InvalidDataException("Cloth skin position stream is incomplete.");
                var blend = array.Blocks[block];
                for (var slot = 0; slot < array.VertexCount; slot++)
                {
                    var key = (skin.OutputBufferIndex, (int)blend.VertexIndices[slot]);
                    if (!vertices.TryGetValue(key, out var deltas)) continue;
                    resolved.Add(key);
                    var delta = deltas.Aggregate(Vector3.Zero, (a, b) => a + b) / deltas.Count;
                    if (delta.LengthSquared() < 1e-16f) continue;
                    if (objectSpace)
                    {
                        var matrix = new Matrix4x4(); float total = 0;
                        for (var influence = 0; influence < array.BlendCount; influence++)
                        {
                            var i = slot * array.BlendCount + influence; var index = blend.BoneIndices[i];
                            var weight = array.BlendCount == 1 ? 1 : blend.BoneWeights[i] / 255f;
                            if (weight == 0) continue;
                            matrix += skin.BoneFromSkinMeshTransforms[index] * frames[skin.TransformSubset[index]] * weight; total += weight;
                        }
                        if (total <= 0 || !Matrix4x4.Invert(matrix * (1 / total), out var inverse)) throw new InvalidOperationException("A skin reference has a singular bone transform.");
                        blocks[position][slot] += new Vector4(Vector3.TransformNormal(delta, inverse), 0);
                    }
                    else
                        for (var influence = 0; influence < array.BlendCount; influence++)
                        {
                            var i = slot * array.BlendCount + influence; var weight = blocks[position][i].W;
                            if (weight == 0) continue;
                            if (!Matrix4x4.Invert(frames[skin.TransformSubset[blend.BoneIndices[i]]], out var inverse)) throw new InvalidOperationException("A skin reference has a singular bone transform.");
                            blocks[position][i] += new Vector4(Vector3.TransformNormal(delta, inverse) * weight, 0);
                        }
                    changed = true;
                }
            }
            if (changed) result.Add(new(cloth.Operators[skin.OperatorIndex].ItemIndex, packed ? "localPs" : "localUnpackedPs",
                objectSpace && packed, blocks.Select(b => new SkinPositionBlockData(b)).ToArray()));
        }
        if (vertices.Any(v => !resolved.Contains(v.Key) && v.Value.Any(d => d.LengthSquared() > 1e-16f)))
            throw new InvalidOperationException("A cloth reference buffer cannot be rebuilt from its skin operators. No changes were applied.");
        return result;
    }
}
