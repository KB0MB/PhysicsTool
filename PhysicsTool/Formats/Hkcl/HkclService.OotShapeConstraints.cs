using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    private void AddOotShapeConstraints(int clothIndex, ParticlePreviewData preview)
    {
        foreach (var set in _bphcl!.NativeDocument!.Cloths[clothIndex].SimCloths[0].ConstraintSets
            .Where(s => s.ClassName == "hclBonePlanesConstraintSet"))
            if (!set.Values.ContainsKey("transformSetIndex") || set.Links.Any(p => !p.PlaneEquationBone.HasValue ||
                !p.Values.ContainsKey("particleIndex") || !p.Values.ContainsKey("transformIndex") || !p.Values.ContainsKey("stiffness")))
                throw new InvalidDataException("OoT bone plane is missing required reflected fields.");
        preview.OotVolumeConstraints.AddRange(GetVolumeConstraintSets(clothIndex));
        var planes = GetBonePlaneConstraintSets(clothIndex);
        if (planes.Count > 0)
        {
            var frames = BoneEditGeometry.WorldTransforms(GetBoneRows(clothIndex));
            foreach (var set in planes)
            {
                if (set.TransformSetIndex != 0) throw new NotSupportedException("OoT bone plane references an unsupported transform set.");
                foreach (var plane in set.Planes)
                {
                    if (!frames.TryGetValue(plane.TransformIndex, out var frame))
                        throw new InvalidDataException("OoT bone plane references a missing bone.");
                    var equation = plane.PlaneEquationBone;
                    preview.OotBonePlanes.Add(new(set.ConstraintSetIndex, plane.ParticleIndex, plane.TransformIndex,
                        Vector3.TransformNormal(new(equation.X, equation.Y, equation.Z), frame),
                        frame.Translation, equation.W, plane.Stiffness));
                }
            }
        }
        OotShapeConstraintValidation.Validate(preview);
    }
}
