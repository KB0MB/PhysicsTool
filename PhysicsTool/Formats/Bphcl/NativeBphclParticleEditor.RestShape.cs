using System.Buffers.Binary;
using System.Numerics;

namespace HKCLTool;

internal sealed record NativeBphclSkinPositionEdit(int ItemIndex, string MemberName, bool Packed,
    IReadOnlyList<SkinPositionBlockData> Blocks);

internal sealed partial class NativeBphclParticleEditor
{
    internal static void ValidateRestShape(NativeBphclDocument document, int clothIndex)
    {
        var cloth = document.Cloths[clothIndex];
        if (cloth.SimCloths.Count != 1) throw new InvalidOperationException("Fitting requires one simulation mesh per cloth.");
        var allowedSets = new[] { "hclStandardLinkConstraintSet", "hclStretchLinkConstraintSet", "hclBendLinkConstraintSet",
            "hclCompressibleLinkConstraintSet", "hclBendStiffnessConstraintSet", "hclLocalRangeConstraintSet", "hclTransitionConstraintSet" };
        var unsupported = cloth.SimCloths[0].ConstraintSets.Where(s => s.IsAntiPinch || !allowedSets.Contains(s.ClassName)).Select(s => s.ClassName).Distinct().ToArray();
        if (unsupported.Length > 0) throw new InvalidOperationException("Rest-shape editing is not yet supported for: " + string.Join(", ", unsupported) + ". No changes were applied.");
        var allowedOperators = new[] { "hclObjectSpaceSkinPOperator", "hclBoneSpaceSkinPOperator", "hclSimpleMeshBoneDeformOperator",
            "hclMoveParticlesOperator", "hclSimulateOperator", "hclCopyVerticesOperator", "hclGatherAllVerticesOperator" };
        if (cloth.Operators.Any(o => !allowedOperators.Contains(o.ClassName))) throw new InvalidOperationException("This cloth has an unsupported rest-shape operator. No changes were applied.");
        if (cloth.SimpleMeshBoneDeformers.Count == 0 || cloth.SimpleMeshBoneDeformers.Any(d => d.TriangleBonePairs.Count != d.LocalBoneTransforms.Count ||
            d.OutputTransformSetIndex != 0 || !cloth.BufferDefinitions.Any(b => b.Index == d.InputBufferIndex && b.Type is 1 or 2 && b.SubType == 0)))
            throw new InvalidOperationException("This cloth has no supported simulation-to-bone bindings.");

        HashSet<int> Objects(NativeBphclCloth c) => c.SimCloths.Select(s => s.ItemIndex)
            .Concat(c.SimCloths.SelectMany(s => document.GetReferenceItemIndices(s.DataOffset + 120)))
            .Concat(c.SimCloths.SelectMany(s => s.ConstraintSets.Select(set => set.ItemIndex)))
            .Concat(c.Operators.Select(o => o.ItemIndex)).ToHashSet();
        var owned = Objects(cloth);
        if (document.Cloths.Where(c => c.Index != clothIndex).Any(c => owned.Overlaps(Objects(c))))
            throw new InvalidOperationException("This cloth shares simulation objects with another cloth. Duplicate it before fitting.");
        var skeleton = document.Skeletons.ElementAtOrDefault(clothIndex) ?? throw new InvalidDataException("Missing cloth skeleton.");
        if (cloth.SimpleMeshBoneDeformers.SelectMany(d => d.TriangleBonePairs).Any(p => p.BoneOffset % 64 != 0 ||
            p.TriangleOffset % 6 != 0 || p.BoneOffset / 64 >= skeleton.BoneCount || p.TriangleOffset / 6 >= cloth.SimCloths[0].TriangleCount))
            throw new InvalidDataException("A simulation bone binding references a missing bone or triangle.");
        if (document.Skeletons.Any(s => s.Index != clothIndex && s.ItemIndex == skeleton.ItemIndex))
            throw new InvalidOperationException("This cloth shares its skeleton with another cloth. Duplicate it before fitting.");
        if (cloth.ObjectSpaceSkins.Any(s => s.TransformSetIndex != 0) || cloth.BoneSpaceSkins.Any(s => s.TransformSetIndex != 0))
            throw new InvalidOperationException("Rest-shape editing requires skin references to the cloth's own skeleton.");
    }

    internal static NativeBphclDocument FitRestShape(NativeBphclDocument document, int clothIndex,
        IReadOnlyDictionary<int, Vector3> positions, IReadOnlyDictionary<int, float> scales,
        IReadOnlyList<NativeBphclConstraintEdit> constraints, IReadOnlyList<NativeBphclSkinPositionEdit> skins,
        IReadOnlyDictionary<int, IReadOnlyList<Matrix4x4>> matrices)
    {
        ValidateRestShape(document, clothIndex);
        var edit = new NativeBphclParticleEditor(document);
        var sim = document.Cloths[clothIndex].SimCloths[0];
        foreach (var constraint in constraints)
        {
            var set = sim.ConstraintSets[constraint.ConstraintSetIndex];
            var localStiffness = constraint.IsLocalRecord && constraint.RecordIndex >= set.LocalConstraints.Count;
            var member = !constraint.IsLocalRecord ? "links" : localStiffness ? "localStiffnessConstraints" : "localConstraints";
            var records = !constraint.IsLocalRecord ? set.Links : localStiffness ? set.LocalStiffnessConstraints : set.LocalConstraints;
            var recordIndex = constraint.RecordIndex - (localStiffness ? set.LocalConstraints.Count : 0);
            var record = records[recordIndex];
            var field = edit.Field(set.ItemIndex, member);
            var array = edit.Array(field) ?? throw new InvalidDataException("Missing constraint records.");
            // Clone each array once. Shared source arrays remain untouched.
            if (array.DataOffset < document.DataSection!.PayloadSize)
                edit.CopyArray(field, edit.ElementSize(array));
            array = edit.Array(field)!;
            var stride = edit.ElementSize(array);
            foreach (var (name, value) in constraint.Values)
            {
                if (!record.EditableValueOffsets.TryGetValue(name, out var offset))
                    throw new InvalidDataException($"The native constraint does not expose {name} for rest-shape editing.");
                if (!float.IsFinite(value)) throw new InvalidOperationException("A fitted constraint value is not finite.");
                edit.Float(array.DataOffset + checked((uint)(recordIndex * stride)) + offset, value);
            }
        }
        foreach (var set in sim.ConstraintSets.Where(s => s.ClassName == "hclBendStiffnessConstraintSet"))
        {
            if (edit.OptionalField(set.ItemIndex, "maxRestPoseHeightSq") is not { } field) continue;
            var factor = scales.Values.Max();
            // Keep this conservative bound large enough after non-uniform edits.
            edit.Float(field, edit.Single(field) * factor * factor);
        }
        var particleField = edit.Field(sim.ItemIndex, "particleDatas");
        edit.CopyArray(particleField, 16);
        var particles = edit.Array(particleField)!;
        foreach (var particle in sim.Particles)
            edit.Float(particles.DataOffset + checked((uint)particle.Index * 16 + 8), particle.Radius * scales[particle.Index]);
        foreach (var pose in document.GetReferenceItemIndices(sim.DataOffset + 120))
        {
            var field = edit.Field(pose, "positions");
            var bytes = edit.ArrayBytes(field, 16);
            if (bytes.Length != sim.Particles.Count * 16) throw new InvalidDataException("Cloth pose particle count is inconsistent.");
            var points = new Vector3[sim.Particles.Count];
            foreach (var particle in sim.Particles)
            {
                var i = particle.Index; var offset = i * 16;
                var original = new Vector3(particle.Position.X, particle.Position.Y, particle.Position.Z);
                var prior = new Vector3(ReadFloat(bytes, offset), ReadFloat(bytes, offset + 4), ReadFloat(bytes, offset + 8));
                points[i] = prior + positions[i] - original;
                WriteVector(bytes, offset, points[i]);
            }
            edit.ReplaceExistingArray(field, bytes, 16);
            if (edit.OptionalField(pose, "normals") is not { } normalField) continue;
            if (edit.Array(normalField) is not { Count: > 0 } normals) continue;
            if (normals.Count != points.Length) throw new InvalidDataException("Cloth normal count is inconsistent.");
            var sums = new Vector3[points.Length];
            for (var i = 0; i < sim.TriangleIndices.Count; i += 3)
            {
                var a = sim.TriangleIndices[i]; var b = sim.TriangleIndices[i + 1]; var c = sim.TriangleIndices[i + 2];
                var normal = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                sums[a] += normal; sums[b] += normal; sums[c] += normal;
            }
            var normalBytes = edit.ArrayBytes(normalField, 16);
            for (var i = 0; i < sums.Length; i++)
                if (sums[i].LengthSquared() > 1e-20f) WriteVector(normalBytes, i * 16, Vector3.Normalize(sums[i]));
            edit.ReplaceExistingArray(normalField, normalBytes, 16);
        }
        foreach (var skin in skins)
        {
            var field = edit.Field(skin.ItemIndex, skin.MemberName);
            var stride = skin.Packed ? 128 : 256;
            var bytes = new byte[skin.Blocks.Count * stride];
            for (var i = 0; i < skin.Blocks.Count; i++)
            {
                var block = skin.Blocks[i];
                if (skin.Packed)
                {
                    var exponent = NativeBphclStandaloneGraphWriter.CalculatePackedObjectPositionExponent([block]);
                    NativeBphclStandaloneGraphWriter.PackObjectPositionBlock(block, exponent).CopyTo(bytes, i * stride);
                }
                else for (var j = 0; j < block.Positions.Count; j++)
                {
                    var p = block.Positions[j]; var offset = i * stride + j * 16;
                    WriteVector(bytes, offset, new(p.X, p.Y, p.Z)); WriteFloat(bytes, offset + 12, p.W);
                }
            }
            edit.ReplaceExistingArray(field, bytes, stride);
        }
        foreach (var (item, transforms) in matrices)
        {
            var bytes = new byte[transforms.Count * 64];
            for (var i = 0; i < transforms.Count; i++)
            {
                var m = transforms[i];
                var values = new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                    m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
                for (var j = 0; j < values.Length; j++) WriteFloat(bytes, i * 64 + j * 4, values[j]);
            }
            edit.ReplaceExistingArray(edit.Field(item, "localBoneTransforms"), bytes, 64);
        }
        edit.UpdateStatistics(sim, sim.Particles.Where(p => p.Fixed).Select(p => p.Index).ToHashSet());
        return edit.Finish();
    }

    private uint Field(int itemIndex, string name) => _items[itemIndex].DataOffset + Member(_items[itemIndex].TypeIndex, name).Offset;
    private uint? OptionalField(int itemIndex, string name)
    {
        var type = _items[itemIndex].TypeIndex;
        while (type != 0)
        {
            var definition = _document.TypeDefinitions.Single(t => t.TypeIndex == type);
            var member = definition.Members.FirstOrDefault(m => m.Name == name || m.Name == "m_" + name);
            if (member != null) return _items[itemIndex].DataOffset + member.Offset;
            type = definition.ParentTypeIndex;
        }
        return null;
    }
    private int ElementSize(NativeBphclItem array) => checked((int)(_document.TypeDefinitions.Single(t => t.TypeIndex == array.TypeIndex).Size
        ?? throw new InvalidDataException("Missing array element size.")));
    private void CopyArray(uint field, int stride) => ReplaceExistingArray(field, ArrayBytes(field, stride), stride);
    private void ReplaceExistingArray(uint field, byte[] bytes, int stride)
    {
        var array = Array(field) ?? throw new InvalidDataException("Missing native array.");
        if (bytes.Length != checked(array.Count * stride)) throw new InvalidDataException("Rest-shape editing cannot change topology.");
        ReplaceArray(field, bytes, checked((int)array.Count), array.TypeIndex, _patches.Single(p => p.Value.Contains(field)).Key);
    }
    private static float ReadFloat(byte[] bytes, int offset) => BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset, 4));
    private static void WriteFloat(byte[] bytes, int offset, float value)
    {
        if (!float.IsFinite(value)) throw new InvalidOperationException("A fitted value is not finite.");
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset, 4), value);
    }
    private static void WriteVector(byte[] bytes, int offset, Vector3 value)
    { WriteFloat(bytes, offset, value.X); WriteFloat(bytes, offset + 4, value.Y); WriteFloat(bytes, offset + 8, value.Z); }
}
