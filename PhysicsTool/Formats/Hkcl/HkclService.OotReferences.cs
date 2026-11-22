namespace HKCLTool;

public sealed partial class HkclService
{
    private OotReferenceBuffers BuildOotReferenceBuffers(int clothIndex)
    {
        var cloth = _bphcl!.NativeDocument!.Cloths[clothIndex];
        if (cloth.SimCloths.Count != 1)
            throw new NotSupportedException("OoT reference routing requires one simulation mesh.");
        var state = cloth.States.FirstOrDefault()?.OperatorItemIndices;
        if (state == null || state.Count == 0 || state.Any(i => i < 0 || i >= cloth.Operators.Count) ||
            state.Distinct().Count() != state.Count)
            throw new NotSupportedException("OoT reference routing requires an explicit valid default operator state.");
        var active = state.Select(i => cloth.Operators[i]).ToArray();
        int solveAt = Array.FindIndex(active, o => o.ClassName == "hclSimulateOperator");
        if (solveAt < 0 || active.Skip(solveAt + 1).Any(o => o.ClassName != "hclSimpleMeshBoneDeformOperator"))
            throw new NotSupportedException("OoT reference writes after simulation are not implemented.");
        var frames = BoneEditGeometry.WorldTransforms(GetBoneRows(clothIndex));
        var skins = GetSkinOperatorData(clothIndex).ToDictionary(s => s.OperatorIndex);
        var buffers = new OotReferenceBuffers(cloth.BufferDefinitions, cloth.States[0].BufferAccesses,
            _bphcl.NativeDocument.ReadOotShadowPositionFlags(clothIndex));
        foreach (var op in active.Take(solveAt))
        {
            switch (op.ClassName)
            {
                case "hclObjectSpaceSkinPOperator":
                case "hclBoneSpaceSkinPOperator":
                    if (!skins.TryGetValue(op.Index, out var skin))
                        throw new InvalidDataException("Missing OoT reference skin payload.");
                    buffers.Skin(skin.OutputBufferIndex, BuildSkinVertexBoneWeights(skin, frames));
                    break;
                case "hclCopyVerticesOperator":
                    if (op.InputBufferIndex is not int copyIn || op.OutputBufferIndex is not int copyOut ||
                        op.StartVertexIn is not int startIn || op.StartVertexOut is not int startOut || op.VertexCount is not int count)
                        throw new InvalidDataException("Incomplete OoT scratch copy route.");
                    buffers.Copy(copyIn, copyOut, startIn, startOut, count);
                    break;
                case "hclGatherAllVerticesOperator":
                    if (op.InputBufferIndex is not int gatherIn || op.OutputBufferIndex is not int gatherOut)
                        throw new InvalidDataException("Incomplete OoT scratch gather route.");
                    buffers.Gather(gatherIn, gatherOut, op.VertexInputFromVertexOutput);
                    break;
                case "hclMoveParticlesOperator":
                    if (op.ReferenceBufferIndex is not int reference || op.VertexParticlePairs.Any(p => p.ParticleIndex >= cloth.SimCloths[0].Particles.Count))
                        throw new InvalidDataException("Invalid OoT MoveParticles reference.");
                    buffers.Move(reference, op.VertexParticlePairs);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported OoT reference stage: {op.ClassName}.");
            }
        }
        return buffers;
    }

    private bool HasOotReferenceRoutes(int clothIndex)
    {
        if (!IsOot) return false;
        var cloth = _bphcl!.NativeDocument!.Cloths[clothIndex];
        return cloth.States.FirstOrDefault()?.BufferAccesses.Any(a => a.BufferIndex != a.ShadowBufferIndex) == true ||
            cloth.States.FirstOrDefault()?.OperatorItemIndices.Any(i => i >= 0 && i < cloth.Operators.Count &&
                cloth.Operators[i].ClassName is "hclCopyVerticesOperator" or "hclGatherAllVerticesOperator") == true;
    }

    private void ApplyOotReferenceBuffers(int clothIndex, ParticlePreviewData preview)
    {
        var buffers = BuildOotReferenceBuffers(clothIndex);
        preview.ParticleAttachments.Clear();
        foreach (var pair in buffers.Attachments)
        {
            var attachment = new ParticlePreviewAttachment { ParticleIndex = pair.Key };
            attachment.BoneWeights.AddRange(pair.Value);
            preview.ParticleAttachments.Add(attachment);
        }
        foreach (var range in preview.LocalRanges)
        {
            range.ReferenceBoneWeights.Clear();
            range.ReferenceBoneWeights.AddRange(buffers.Resolve(range.ReferenceBufferIndex, range.ReferenceVertex));
        }
    }
}
