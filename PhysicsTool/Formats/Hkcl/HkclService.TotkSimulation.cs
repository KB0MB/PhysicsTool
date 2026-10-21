using System.Numerics;

namespace HKCLTool;

public sealed record TotkSimulationSupport(IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings)
{
    public bool Supported => Blockers.Count == 0;
}

public sealed partial class HkclService
{
    // This is a steady-state runtime, not an implicit BPHCL -> HKCL conversion.
    // Reject missing kernels/routing instead of letting the generic preview ignore them.
    public TotkSimulationSupport GetTotkSimulationSupport(int clothIndex)
    {
        var blockers = new List<string>();
        var warnings = new List<string>
        {
            "Experimental TotK cloth preview; game parity is not yet validated.",
            "Animation-to-simulation transitions, world collision, and game wind/actions are not reconstructed."
        };
        var cloth = _bphcl?.NativeDocument?.Cloths.ElementAtOrDefault(clothIndex);
        if (cloth == null) return new(new[] { "Select a native BPHCL cloth." }, warnings);
        if (cloth.SimCloths.Count != 1) blockers.Add("Multiple simulation meshes in one cloth are not implemented.");
        var simulation = cloth.SimCloths.FirstOrDefault();
        var operators = cloth.Operators.Where(op => op.ClassName == "hclSimulateOperator").ToArray();
        if (operators.Length != 1 || operators[0].SimulateConfigs.Count != 1)
            blockers.Add("This cloth requires multiple simulation programs/configurations.");
        var config = operators.FirstOrDefault()?.SimulateConfigs.FirstOrDefault();
        if (config is { AdaptConstraintStiffness: true, SubSteps: > 1 })
            blockers.Add("TotK adaptive multi-substep stiffness is not yet verified.");
        if (config != null && (config.SubSteps is 0 or > 32 || config.NumberOfSolveIterations is 0 or > 32))
            blockers.Add("The authored substep/iteration count is outside the supported preview range.");
        foreach (var op in cloth.Operators)
            if (op.ClassName is not ("hclObjectSpaceSkinPOperator" or "hclBoneSpaceSkinPOperator" or
                "hclMoveParticlesOperator" or "hclSimulateOperator" or "hclSimpleMeshBoneDeformOperator" or
                "hclCopyVerticesOperator" or "hclGatherAllVerticesOperator"))
                blockers.Add($"Unsupported operator: {op.ClassName}.");
        if (cloth.TransformSetDefinitions.Count > 1)
            blockers.Add("Multiple skeleton transform sets are not implemented.");
        if (cloth.ObjectSpaceSkins.Any(skin => skin.TransformSetIndex != 0) || cloth.BoneSpaceSkins.Any(skin => skin.TransformSetIndex != 0) ||
            cloth.SimpleMeshBoneDeformers.Any(deformer => deformer.OutputTransformSetIndex != 0) ||
            cloth.Operators.Any(op => op.SimulationClothIndex.HasValue && op.SimulationClothIndex != 0))
            blockers.Add("An operator references an unsupported skeleton or simulation mesh.");
        if (cloth.ObjectSpaceSkins.Select(skin => skin.OutputBufferIndex).Concat(cloth.BoneSpaceSkins.Select(skin => skin.OutputBufferIndex))
            .GroupBy(index => index).Any(group => group.Count() > 1))
            blockers.Add("Multiple skin writers to one buffer require native state routing.");
        if (cloth.SimpleMeshBoneDeformers.Any(deformer => !cloth.BufferDefinitions.Any(buffer =>
            buffer.Index == deformer.InputBufferIndex && buffer.Type == 1 && buffer.SubType == 0)))
            blockers.Add("A bone deformer reads a buffer other than current simulation positions.");
        if (cloth.ObjectSpaceSkins.Count + cloth.BoneSpaceSkins.Count == 0 &&
            cloth.Operators.Any(op => op.ClassName == "hclMoveParticlesOperator" && op.VertexParticlePairs.Count > 0))
            blockers.Add("MoveParticles has no skinned reference buffer; bind the cloth before native simulation.");
        var activeIndices = cloth.States.FirstOrDefault()?.OperatorItemIndices ?? Array.Empty<int>();
        if (activeIndices.Count > 0)
        {
            if (activeIndices.Any(index => index < 0 || index >= cloth.Operators.Count) || activeIndices.Distinct().Count() != activeIndices.Count)
                blockers.Add("The default state contains invalid or repeated operator indices.");
            else
            {
                var active = activeIndices.Select(index => cloth.Operators[index]).ToArray();
                var solveAt = Array.FindIndex(active, op => op.ClassName == "hclSimulateOperator");
                if (solveAt < 0 || active.Any(op => op.ClassName is "hclCopyVerticesOperator" or "hclGatherAllVerticesOperator") ||
                    active.Take(Math.Max(0, solveAt)).Any(op => op.ClassName == "hclSimpleMeshBoneDeformOperator") ||
                    active.Skip(solveAt + 1).Any(op => op.ClassName is "hclMoveParticlesOperator" or "hclObjectSpaceSkinPOperator" or "hclBoneSpaceSkinPOperator"))
                    blockers.Add("The default-state operator routing requires stages not yet reconstructed.");
            }
        }
        if (simulation != null)
        {
            foreach (var set in simulation.ConstraintSets)
            {
                if (set.ClassName is not ("hclStandardLinkConstraintSet" or "hclStretchLinkConstraintSet" or "hclCompressibleLinkConstraintSet" or
                    "hclBendLinkConstraintSet" or "hclBendStiffnessConstraintSet" or "hclLocalRangeConstraintSet" or
                    "hclTransitionConstraintSet"))
                    blockers.Add($"Unsupported constraint kernel: {set.ClassName}.");
                if (set.Values.GetValueOrDefault("applyNormalComponent") != 0)
                    blockers.Add("Normal-component local ranges are not implemented.");
            }
            var indices = simulation.ConstraintSets.Select(set => set.Index).ToHashSet();
            if (config?.ConstraintExecution.Any(index => index != -1 && !indices.Contains(index)) == true)
                blockers.Add("The constraint schedule contains an invalid set index.");
            if (simulation.Particles.Any(p => !float.IsFinite(p.Mass) || !float.IsFinite(p.InverseMass) || p.Mass < 0 || p.InverseMass < 0))
                blockers.Add("Particle mass/inverse mass must be finite and nonnegative.");
            if (simulation.CollidableItemIndices.Count > 31)
                blockers.Add("More than 31 instance collision slots are not supported.");
            if (config?.InstanceCollidablesUsed.Any(slot => slot < 0 || slot >= simulation.CollidableItemIndices.Count) == true)
                blockers.Add("The simulation configuration contains an invalid collision slot.");
            if (simulation.Runtime.TransferMotionEnabled && simulation.Runtime.TransferMotionData.TransformSetIndex != 0)
                blockers.Add("Motion transfer references an unsupported skeleton transform set.");
            var map = simulation.CollidableTransformMap;
            var boneCount = _bphcl!.NativeDocument!.Skeletons.ElementAtOrDefault(clothIndex)?.Bones.Count ?? 0;
            if (map.TransformSetIndex >= 0 && (map.TransformSetIndex != 0 ||
                map.TransformIndices.Count != simulation.CollidableItemIndices.Count ||
                map.Offsets.Count != simulation.CollidableItemIndices.Count || map.TransformIndices.Any(b => b >= boneCount)))
                blockers.Add("The collider transform map is incomplete or references an unsupported skeleton.");
            foreach (var item in simulation.CollidableItemIndices)
            {
                var collider = _bphcl!.NativeDocument!.Colliders.FirstOrDefault(c => c.ItemIndex == item);
                if (collider == null || collider.Shape.TypeName is not ("hclSphereShape" or "hclCapsuleShape" or "hclTaperedCapsuleShape" or "hclPlaneShape"))
                    blockers.Add("An instance collider is missing or uses an unsupported shape.");
            }
            if (simulation.Runtime.PinchDetectionEnabled)
                warnings.Add("Pinch contacts currently use the shared preview recovery, not fully reconstructed TotK recovery.");
        }
        return new(blockers.Distinct().ToArray(), warnings);
    }

    public ParticlePreviewData GetTotkSimulationPreview(int clothIndex)
    {
        var support = GetTotkSimulationSupport(clothIndex);
        if (!support.Supported) throw new NotSupportedException(string.Join(" ", support.Blockers));
        var document = _bphcl!.NativeDocument!;
        var cloth = document.Cloths[clothIndex];
        var simulation = cloth.SimCloths[0];
        var config = cloth.Operators.Single(op => op.ClassName == "hclSimulateOperator").SimulateConfigs[0];
        var preview = GetParticlePreview(clothIndex);
        preview.SolverProfile = ClothSolverProfile.TotkExperimental;
        var frames = BoneEditGeometry.WorldTransforms(GetBoneRows(clothIndex));

        // Do not synthesize fixed/reference inputs from nearby particles. Native
        // MoveParticles and LocalRange must resolve their exact skinned vertices.
        var movedParticles = cloth.Operators.Where(op => op.ClassName == "hclMoveParticlesOperator")
            .SelectMany(op => op.VertexParticlePairs).Select(pair => (int)pair.ParticleIndex).ToHashSet();
        foreach (var fixedParticle in preview.Particles.Where(p => p.Fixed && movedParticles.Contains(p.Index)))
            if (!preview.ParticleAttachments.Any(a => a.ParticleIndex == fixedParticle.Index &&
                a.BoneWeights.Count > 0 && a.BoneWeights.All(w => w.ReferencePosition.HasValue)))
                throw new NotSupportedException($"Fixed particle {fixedParticle.Index} has no decoded skin input.");
        foreach (var range in preview.LocalRanges)
            if (range.ReferenceBoneWeights.Count == 0 || range.ReferenceBoneWeights.Any(w => !w.ReferencePosition.HasValue))
                throw new NotSupportedException($"Local range for particle {range.ParticleIndex} has no decoded reference vertex.");

        preview.Colliders.Clear();
        var map = simulation.CollidableTransformMap;
        for (var slot = 0; slot < simulation.CollidableItemIndices.Count; slot++)
        {
            var collider = document.Colliders.Single(c => c.ItemIndex == simulation.CollidableItemIndices[slot]);
            if (!collider.Enabled || !config.UseAllInstanceCollidables && !config.InstanceCollidablesUsed.Contains(slot)) continue;
            var bone = map.TransformSetIndex >= 0 ? checked((int)map.TransformIndices[slot]) : -1;
            // TotK 710048f830: drive each instance slot with bone * offset
            // (offset * bone for System.Numerics row vectors). Never infer by name.
            var transform = bone >= 0 ? map.Offsets[slot] * frames[bone] : GetNativeColliderTransform(collider);
            var shape = collider.Shape;
            var kind = shape.TypeName switch
            {
                "hclSphereShape" => ColliderPreviewKind.Sphere,
                "hclCapsuleShape" => ColliderPreviewKind.Capsule,
                "hclTaperedCapsuleShape" => ColliderPreviewKind.TaperedCapsule,
                _ => ColliderPreviewKind.Plane
            };
            var start = kind == ColliderPreviewKind.Plane ? new Vector4(GetPlanePoint(shape.PlaneEquation), 0) : shape.Start;
            preview.Colliders.Add(new ColliderPreviewShape
            {
                Index = collider.Index, CollisionBit = slot, Name = collider.Name, BoneIndex = bone,
                Start = TransformHkclColliderPoint(transform, start),
                End = TransformHkclColliderPoint(transform, kind == ColliderPreviewKind.Plane ? start : shape.End),
                TransformOrigin = transform.Translation, Radius = shape.Radius, EndRadius = shape.EndRadius,
                PlaneNormal = TransformHkclColliderDirection(transform, new(shape.PlaneEquation.X, shape.PlaneEquation.Y, shape.PlaneEquation.Z)),
                Kind = kind
            });
        }
        preview.PinchDetectionEnabled = simulation.Runtime.PinchDetectionEnabled;
        for (var i = 0; i < simulation.Runtime.PerParticlePinchDetectionEnabledFlags.Count; i++)
            if (simulation.Runtime.PerParticlePinchDetectionEnabledFlags[i]) preview.PinchEnabledParticles.Add(i);
        for (var i = 0; i < simulation.Runtime.CollidablePinchingData.Count; i++)
        {
            var pinch = simulation.Runtime.CollidablePinchingData[i];
            preview.ColliderPinching.Add(new(i, pinch.Enabled, pinch.Priority, pinch.Radius));
        }
        foreach (var binding in preview.BoneBindings) binding.UseNativeSimpleDeformer = true;
        return preview;
    }
}
