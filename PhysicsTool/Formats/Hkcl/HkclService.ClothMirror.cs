using System.Collections;
using System.Buffers.Binary;
using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    public void ValidateWholeClothMirror(int clothIndex)
    {
        if (!HasDocument || IsHelperBoneSidecar || IsHkrg) throw new InvalidOperationException("Select a cloth to mirror.");
        var allowed = new HashSet<string>(StringComparer.Ordinal) {
            "hclStretchLinkConstraintSet", "hclBendLinkConstraintSet", "hclStandardLinkConstraintSet", "hclAntiPinchConstraintSet",
            "hclLocalRangeConstraintSet", "hclTransitionConstraintSet", "hclCompressibleLinkConstraintSet", "hclBendStiffnessConstraintSet" };
        var unsupported = GetConstraintSetSummaries(clothIndex).Where(s => !allowed.Contains(s.ClassName)).Select(s => s.ClassName).Distinct().ToArray();
        if (unsupported.Length > 0) throw new InvalidOperationException("Whole-cloth mirroring is not yet validated for: " + string.Join(", ", unsupported) + ". No data was changed.");
        var colliders = GetColliderRows(clothIndex).Select(c => c.Index).ToHashSet();
        for (var i = 0; i < GetClothSummaries().Count; i++)
            if (i != clothIndex && GetColliderRows(i).Any(c => colliders.Contains(c.Index)))
                throw new InvalidOperationException("This cloth shares colliders with another cloth. Duplicate the cloth first to isolate its collision data before mirroring.");
        var skin = GetSkinOperatorData(clothIndex);
        var supported = new HashSet<string>(StringComparer.Ordinal) { "hclObjectSpaceSkinPOperator", "hclBoneSpaceSkinPOperator", "hclSimpleMeshBoneDeformOperator",
            "hclMoveParticlesOperator", "hclSimulateOperator", "hclCopyVerticesOperator", "hclGatherAllVerticesOperator" };
        if (GetClothRuntimeData(clothIndex).Operators.Any(o => !supported.Contains(o.ClassName)))
            throw new InvalidOperationException("This cloth contains an operator not yet validated for whole-cloth mirroring.");
        if (skin.Any(s => s.ClassName is not ("hclObjectSpaceSkinPOperator" or "hclBoneSpaceSkinPOperator")))
            throw new InvalidOperationException("This skin operator layout has not been validated for mirroring.");
        if (IsBphcl && skin.Any(s => s.ClassName == "hclBoneSpaceSkinPOperator"))
            throw new InvalidOperationException("TotK bone-space skin mirroring needs additional bone-frame rebasing and is not yet supported.");
        if (_bphcl?.NativeDocument is { } native)
        {
            if (native.Cloths[clothIndex].SimCloths.Count != 1) throw new InvalidOperationException("Mirroring requires a single simulation mesh per cloth.");
            if (native.Cloths[clothIndex].SimCloths[0].ConstraintSets.Where(s => s.ClassName == "hclBendStiffnessConstraintSet")
                .SelectMany(s => s.Links).Any(l => !l.EditableValueOffsets.ContainsKey("restCurvature")))
                throw new InvalidOperationException("This native bend layout does not expose its signed rest curvature for mirroring.");
            return;
        }
        var root = RequireRoot(); var skeletons = GetSkeletons(root).ToArray();
        if (skeletons.Where((s, i) => i != clothIndex).Any(s => ReferenceEquals(s, skeletons.ElementAtOrDefault(clothIndex))))
            throw new InvalidOperationException("This cloth shares its skeleton with another cloth. Isolate the cloth before mirroring.");
        var cloth = GetClothDatas(root).ElementAt(clothIndex);
        if ((GetList(GetValue(cloth, "simClothDatas"))?.Count ?? 0) != 1)
            throw new InvalidOperationException("Mirroring requires a single simulation mesh per cloth.");
        foreach (var field in new[] { "operators", "bufferDefinitions", "simClothDatas" })
        {
            var owned = new HashSet<object>(GetList(GetValue(cloth, field)) ?? Array.Empty<object>(), ReferenceEqualityComparer.Instance);
            if (GetClothDatas(root).Where((_, i) => i != clothIndex).Any(other => (GetList(GetValue(other, field)) ?? Array.Empty<object>()).Any(owned.Contains)))
                throw new InvalidOperationException("This cloth shares native geometry or operators with another cloth. Isolate it before mirroring.");
        }
    }

    public void MirrorClothConstraintOrientation(int clothIndex)
    {
        if (_bphcl?.NativeDocument is { } native)
        {
            var edits = native.Cloths[clothIndex].SimCloths[0].ConstraintSets.Where(s => s.ClassName == "hclBendStiffnessConstraintSet")
                .SelectMany(s => s.Links.Select(l => new NativeBphclConstraintEdit(s.Index, l.Index, false,
                    new Dictionary<string, float> { ["restCurvature"] = -(float)l.Values["restCurvature"] }))).ToArray();
            _bphcl = CreateBphclSummary(NativeBphclWriter.ApplyConstraintEdits(native, clothIndex, 0, edits), _bphcl.SourcePath);
            return;
        }
        var cloth = GetClothDatas(RequireRoot()).ElementAt(clothIndex);
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"));
        foreach (var set in GetList(GetValue(simulation, "staticConstraintSets")) ?? GetList(GetValue(cloth, "constraintSets")) ?? Array.Empty<object>())
            if (set.GetType().Name == "hclBendStiffnessConstraintSet")
                foreach (var link in GetList(GetValue(set, "links")) ?? Array.Empty<object>())
                    if (GetValue(link, "restCurvature") is float curvature) SetValue(link, "restCurvature", -curvature);
    }

    // Call after the primary pose, skeleton and triangle winding have been mirrored.
    public void MirrorHkclReferenceGeometryAcrossX(int clothIndex)
    {
        if (IsBphcl) return;
        var cloth = GetClothDatas(RequireRoot()).ElementAt(clothIndex);
        var reflection = Matrix4x4.CreateScale(-1, 1, 1);
        // Swapping vertices 1 and 2 changes centroid-frame row 1 to -row0-row1.
        // This basis change preserves authored output offsets, unlike rebuilding
        // every deformer from the skeleton's rest pose.
        var windingBasis = new Matrix4x4(1, 0, 0, 0, -1, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
        void ReflectVectors(object? value)
        {
            if (value is not IList list) return;
            for (var i = 0; i < list.Count; i++)
                if (list[i] is Vector4 v) SetListItem(list, i, new Vector4(-v.X, v.Y, v.Z, v.W));
        }
        var sim = GetFirst(GetValue(cloth, "simClothDatas"));
        var poses = GetList(GetValue(sim, "simClothPoses")) ?? Array.Empty<object>();
        foreach (var pose in poses) ReflectVectors(GetValue(pose, "normals"));
        foreach (var pose in poses.Skip(1)) ReflectVectors(GetValue(pose, "positions"));
        foreach (var op in GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>())
        {
            var type = op.GetType().Name;
            var matrixField = type == "hclSimpleMeshBoneDeformOperator" ? "localBoneTransforms" : "boneFromSkinMeshTransforms";
            if (GetValue(op, matrixField) is IList matrices)
                for (var i = 0; i < matrices.Count; i++)
                    if (matrices[i] is Matrix4x4 m) SetListItem(matrices, i, reflection * m * (type == "hclSimpleMeshBoneDeformOperator" ? windingBasis : reflection));
            foreach (var field in new[] { "localPs", "localUnpackedPs" })
            {
                var blocks = GetList(GetValue(op, field)); if (blocks == null || blocks.Count == 0) continue;
                var decoded = ReadSkinPositionBlocks(blocks);
                for (var b = 0; b < blocks.Count; b++)
                {
                    var positions = decoded[b].Positions.Select(p => new Vector4(-p.X, p.Y, p.Z, p.W)).ToArray();
                    var target = blocks[b];
                    if (GetValue(target, "localPosition_0") is short)
                    {
                        var block = new SkinPositionBlockData(positions);
                        var exponent = NativeBphclStandaloneGraphWriter.CalculatePackedObjectPositionExponent([block]);
                        var bytes = NativeBphclStandaloneGraphWriter.PackObjectPositionBlock(block, exponent);
                        for (var i = 0; i < 64; i++) SetValue(target, $"localPosition_{i}", BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2)));
                    }
                    else for (var i = 0; i < positions.Length; i++) SetValue(target, $"localPosition_{i}", positions[i]);
                }
            }
        }
    }
}
