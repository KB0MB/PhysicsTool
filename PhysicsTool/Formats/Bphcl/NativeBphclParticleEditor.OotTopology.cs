using System.Buffers.Binary;
using System.Numerics;

namespace HKCLTool;

internal sealed partial class NativeBphclParticleEditor
{
    internal static NativeBphclDocument AddOotParticle(NativeBphclDocument document, int clothIndex, int sourceIndex,
        IReadOnlyList<SkinOperatorData> skins)
    {
        ValidateRestShape(document, clothIndex); ValidateOotGeometry(document, clothIndex);
        var cloth = document.Cloths[clothIndex]; var sim = cloth.SimCloths.Single();
        int count = sim.Particles.Count;
        if (count == 0 || count >= short.MaxValue || sourceIndex < 0 || sourceIndex >= count)
            throw new InvalidOperationException("Choose an existing particle in a cloth with fewer than 32767 particles.");
        var edit = new NativeBphclParticleEditor(document);
        edit.Append(edit.Field(sim.ItemIndex, "particleDatas"), 16, sourceIndex, count);
        var particles = edit.Array(edit.Field(sim.ItemIndex, "particleDatas"))!;
        var mass = sim.Particles[sourceIndex].Mass;
        if (mass <= 0) mass = .1f;
        edit.Float(particles.DataOffset + (uint)count * 16, mass);
        edit.Float(particles.DataOffset + (uint)count * 16 + 4, 1 / mass);
        foreach (int pose in document.GetReferenceItemIndices(edit.Field(sim.ItemIndex, "simClothPoses")))
        {
            edit.Append(edit.Field(pose, "positions"), 16, sourceIndex, count);
            if (edit.OptionalField(pose, "normals") is { } normals) edit.AppendOptional(normals, 16, sourceIndex, count);
        }
        edit.AppendOptional(edit.Field(sim.ItemIndex, "staticCollisionMasks"), 4, sourceIndex, count);
        var pinchField = edit.Field(sim.ItemIndex, "perParticlePinchDetectionEnabledFlags");
        edit.AppendOptional(pinchField, 1, sourceIndex, count);
        if (edit.Array(pinchField) is { Count: > 0 } pinch) edit._data[(int)pinch.DataOffset + count] = 0;
        var virtualField = edit.Field(sim.ItemIndex, "virtualCollisionPointsData");
        var virtualType = edit.Member(document.GetItem(sim.ItemIndex).TypeIndex, "virtualCollisionPointsData").TypeIndex;
        foreach (string name in new[] { "landscapeParticlesBlockIndex", "edgeFanIndices", "triangleFanIndices", "edgeFanIndicesLandscape", "triangleFanIndicesLandscape" })
        {
            uint field = virtualField + edit.Member(virtualType, name).Offset;
            if (edit.Array(field) is not { Count: > 0 }) continue;
            edit.Append(field, 2, sourceIndex, count);
            var array = edit.Array(field)!; edit._data[(int)array.DataOffset + count * 2] = 255; edit._data[(int)array.DataOffset + count * 2 + 1] = 255;
        }
        var cloned = new Dictionary<(int Buffer, int Vertex), int>();
        var buffers = cloth.BufferDefinitions.Where(b => b.Type is 1 or 2 && b.SubType == sim.Index).ToArray();
        if (buffers.Length == 0) throw new InvalidOperationException("No simulation buffer found.");
        foreach (var buffer in buffers)
        {
            if (buffer.VertexCount != count) throw new InvalidOperationException("The simulation buffer has a nonstandard particle layout.");
            var writers = cloth.Operators.Where(o => o.OutputBufferIndex == buffer.Index).ToArray();
            if (writers.Length != 1) throw new InvalidOperationException("Particle creation requires one initializer per simulation buffer.");
            var op = writers[0];
            int sourceVertex = op.ClassName switch
            {
                "hclCopyVerticesOperator" when op.StartVertexOut == 0 && op.VertexCount == count => op.StartVertexIn.GetValueOrDefault() + sourceIndex,
                "hclGatherAllVerticesOperator" when op.VertexInputFromVertexOutput.Count == count => op.VertexInputFromVertexOutput[sourceIndex],
                _ => throw new InvalidOperationException("This cloth initializer cannot be extended yet.")
            };
            int input = op.InputBufferIndex ?? throw new InvalidDataException("Missing skin input buffer.");
            if (!cloned.TryGetValue((input, sourceVertex), out int vertex))
            {
                var skin = skins.SingleOrDefault(s => s.OutputBufferIndex == input)
                    ?? throw new InvalidOperationException("The initializer has no unique supported skin operator.");
                var reference = cloth.BufferDefinitions.Single(b => b.Index == input);
                vertex = checked((int)reference.VertexCount);
                if (vertex >= short.MaxValue) throw new InvalidOperationException("The reference buffer is full.");
                edit.CloneOotSkinVertex(cloth, skin, sourceVertex, vertex);
                edit.UInt(edit.Field(reference.ItemIndex, "numVertices"), checked((uint)vertex + 1));
                cloned[(input, sourceVertex)] = vertex;
            }
            if (op.ClassName == "hclCopyVerticesOperator" && vertex == op.StartVertexIn.GetValueOrDefault() + count)
                edit.UInt(edit.Field(op.ItemIndex, "numberOfVertices"), checked((uint)count + 1));
            else
            {
                try { edit.ExtendInitializer(op, count, sourceIndex); }
                catch (InvalidDataException ex) { throw new InvalidOperationException("This initializer cannot grow without additional native type metadata.", ex); }
                var mapping = edit.Array(edit.Field(op.ItemIndex, "vertexInputFromVertexOutput"))!;
                edit._data[(int)mapping.DataOffset + count * 2] = (byte)vertex;
                edit._data[(int)mapping.DataOffset + count * 2 + 1] = (byte)(vertex >> 8);
            }
            edit.UInt(edit.Field(buffer.ItemIndex, "numVertices"), checked((uint)count + 1));
        }
        edit.UpdateStatistics(sim, sim.Particles.Where(p => p.Fixed).Select(p => p.Index).ToHashSet());
        return edit.Finish();
    }

    private void CloneOotSkinVertex(NativeBphclCloth cloth, SkinOperatorData skin, int source, int target)
    {
        var blockOrder = HkclService.GetSkinBlockOrder(skin.BlendArrays, skin.ControlBytes);
        var match = blockOrder.FirstOrDefault(p => p.Array.Blocks[p.BlockIndex].VertexIndices.Contains(checked((ushort)source)));
        if (match.Array == null) throw new InvalidOperationException("The source particle has no skinned reference vertex.");
        var blend = match.Array.Blocks[match.BlockIndex]; int slot = blend.VertexIndices.ToList().IndexOf((ushort)source);
        bool objectSpace = skin.ClassName == "hclObjectSpaceSkinPOperator";
        int item = cloth.Operators[skin.OperatorIndex].ItemIndex;
        string deformerName = objectSpace ? "objectSpaceDeformer" : "boneSpaceDeformer";
        uint deformer = Field(item, deformerName), deformerType = Member(_items[item].TypeIndex, deformerName).TypeIndex;
        var blendField = deformer + Member(deformerType, match.Array.MemberName).Offset;
        var array = Array(blendField)!; int stride = ElementSize(array);
        var bytes = ArrayBytes(blendField, stride); var block = new byte[stride];
        int vertices = match.Array.VertexCount, influences = match.Array.BlendCount;
        for (int v = 0; v < vertices; v++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(v * 2), (ushort)target);
            for (int i = 0; i < influences; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(vertices * 2 + (v * influences + i) * 2), blend.BoneIndices[slot * influences + i]);
                if (objectSpace && influences > 1) block[vertices * 2 + match.Array.BoneCount * 2 + v * influences + i] = blend.BoneWeights[slot * influences + i];
            }
        }
        GrowOotArray(blendField, bytes.Concat(block).ToArray(), stride);
        bool packed = skin.LocalPositionBlocks.Count > 0;
        var positions = (packed ? skin.LocalPositionBlocks : skin.LocalUnpackedPositionBlocks)[match.PositionIndex];
        var values = positions.NativePackedValues is { } raw
            ? HkclService.UnpackBphclObjectSpacePositions([new(raw)])[0].Positions : positions.Positions;
        var cloned = new Vector4[16];
        for (int v = 0; v < vertices; v++)
            for (int i = 0; i < (objectSpace ? 1 : influences); i++) cloned[objectSpace ? v : v * influences + i] = values[objectSpace ? slot : slot * influences + i];
        byte[] local;
        if (objectSpace && packed)
        {
            var record = new SkinPositionBlockData(cloned);
            local = NativeBphclStandaloneGraphWriter.PackObjectPositionBlock(record, NativeBphclStandaloneGraphWriter.CalculatePackedObjectPositionExponent([record]));
        }
        else
        {
            local = new byte[256];
            for (int i = 0; i < 16; i++) { WriteVector(local, i * 16, new(cloned[i].X, cloned[i].Y, cloned[i].Z)); WriteFloat(local, i * 16 + 12, cloned[i].W); }
        }
        uint localField = Field(item, packed ? "localPs" : "localUnpackedPs");
        GrowOotArray(localField, ArrayBytes(localField, local.Length).Concat(local).ToArray(), local.Length);
        uint controlsField = deformer + Member(deformerType, "controlBytes").Offset;
        if (skin.ControlBytes.Count == 0) throw new InvalidOperationException("Skin creation requires an authored control stream.");
        byte control = checked((byte)(influences <= 4 ? 4 - influences : 13 - influences));
        GrowOotArray(controlsField, ArrayBytes(controlsField, 1).Concat(new byte[] { control, 4 }).ToArray(), 1);
        uint end = deformer + Member(deformerType, "endVertexIndex").Offset;
        _data[(int)end] = (byte)target; _data[(int)end + 1] = (byte)(target >> 8);
    }

    internal static NativeBphclDocument AddOotTriangle(NativeBphclDocument document, int clothIndex, IReadOnlyList<int> indices)
    {
        ValidateRestShape(document, clothIndex); ValidateOotGeometry(document, clothIndex);
        var sim = document.Cloths[clothIndex].SimCloths.Single();
        if (indices.Count != 3 || indices.Distinct().Count() != 3 || indices.Any(i => i < 0 || i >= sim.Particles.Count)) throw new ArgumentException("Select three different particles.");
        var points = indices.Select(i => sim.Particles[i].Position).Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
        if (Vector3.Cross(points[1] - points[0], points[2] - points[0]).LengthSquared() < 1e-12f) throw new InvalidOperationException("The triangle is collapsed. Move its particles apart first.");
        if (sim.TriangleIndices.Chunk(3).Any(t => t.Select(i => (int)i).Order().SequenceEqual(indices.Order()))) return document;
        var edit = new NativeBphclParticleEditor(document);
        uint field = edit.Field(sim.ItemIndex, "triangleIndices");
        var bytes = sim.TriangleIndices.Concat(indices.Select(i => checked((ushort)i))).SelectMany(BitConverter.GetBytes).ToArray();
        edit.ReplaceArray(field, bytes, bytes.Length / 2, edit.Type("hkUint16"), edit.MemberType(sim.ItemIndex, "triangleIndices"));
        uint flipsField = edit.Field(sim.ItemIndex, "triangleFlips");
        if (edit.Array(flipsField) is { Count: > 0 } flips)
        {
            if (flips.Count != sim.TriangleCount) throw new InvalidDataException("Triangle flip count mismatch.");
            edit.GrowOotArray(flipsField, edit.ArrayBytes(flipsField, 1).Append((byte)0).ToArray(), 1);
        }
        foreach (var buffer in document.Cloths[clothIndex].BufferDefinitions.Where(b => b.Type is 1 or 2 && b.SubType == sim.Index))
            edit.UInt(edit.Field(buffer.ItemIndex, "numTriangles"), checked((uint)sim.TriangleCount + 1));
        var updated = edit.Finish();
        for (int i = 0; i < 3; i++) updated = Link(updated, clothIndex, new[] { indices[i], indices[(i + 1) % 3] });
        return updated;
    }

    private void GrowOotArray(uint field, byte[] bytes, int stride)
    {
        var array = Array(field) ?? throw new InvalidDataException("Missing native array.");
        if (bytes.Length % stride != 0) throw new InvalidDataException("Invalid native array length.");
        ReplaceArray(field, bytes, bytes.Length / stride, array.TypeIndex, _patches.Single(p => p.Value.Contains(field)).Key);
    }

    internal static NativeBphclDocument MoveOotColliders(NativeBphclDocument document, IReadOnlyList<ColliderEditRow> rows)
    {
        var edit = new NativeBphclParticleEditor(document);
        foreach (var row in rows)
        {
            var collider = document.Colliders[row.Index];
            uint field = edit.Field(collider.ItemIndex, "transform");
            uint type = edit.Member(document.GetItem(collider.ItemIndex).TypeIndex, "transform").TypeIndex;
            if (document.GetTypeName(type) != "hkPreciseTransform") throw new InvalidOperationException("Unsupported OoT collider transform.");
            var m = row.Transform;
            foreach (var (offset, v) in new[] { (0u, new Vector4(m.M11, m.M12, m.M13, collider.AxisX.W)),
                (16u, new Vector4(m.M21, m.M22, m.M23, collider.AxisY.W)), (32u, new Vector4(m.M31, m.M32, m.M33, collider.AxisZ.W)),
                (48u, new Vector4(m.M41, m.M42, m.M43, collider.Translation.W)), (64u, Vector4.Zero) })
                for (int i = 0; i < 4; i++) edit.Float(field + offset + (uint)i * 4, v[i]);
        }
        return edit.Finish();
    }
}
