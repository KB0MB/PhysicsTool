using System;
using System.Collections.Generic;
using System.Linq;

namespace HKCLTool;

/// <summary>
/// Applies an HKCL cloth's authored values to an already-valid BPHCL cloth
/// slot. The BPHCL template remains responsible for its own ITEM, TYPE, INDX,
/// AAMP, constraint, and collision-runtime layout.
/// </summary>
public sealed record HkclToBphclTemplatePreflight(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Notes)
{
    public bool CanConvert => Errors.Count == 0;

    public string ToDisplayText()
    {
        var lines = new List<string>();
        if (Errors.Count > 0)
        {
            lines.Add("This HKCL cloth cannot safely replace the selected BPHCL template slot:");
            lines.AddRange(Errors.Select(error => "- " + error));
        }
        else
        {
            lines.Add("Topology check passed.");
        }

        if (Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("This pass intentionally preserves from the BPHCL template:");
            lines.AddRange(Notes.Select(note => "- " + note));
        }

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Strict, template-backed HKCL to BPHCL conversion. It only writes fields
/// for which the native BPHCL writer has validated in-place support.
/// </summary>
public static class HkclToBphclTemplateConversion
{
    public static HkclToBphclTemplatePreflight Check(
        HkclService source,
        int sourceClothIndex,
        HkclService template,
        int templateClothIndex)
    {
        var errors = new List<string>();
        var notes = new List<string>
        {
            "constraint records and rest lengths",
            "fixed-particle flags and per-particle collision masks",
            "collider ownership, transform maps, operators, states, buffers, and AAMP metadata"
        };

        if (source.IsBphcl || source.IsBphhb || !source.HasDocument)
            errors.Add("The source must be a normal HKCL file.");
        if (!template.IsBphcl || !template.HasDocument)
            errors.Add("The template must be a BPHCL file.");
        if (errors.Count > 0)
            return new HkclToBphclTemplatePreflight(errors, notes);

        var sourceParticles = source.GetParticleRows(sourceClothIndex);
        var templateParticles = template.GetParticleRows(templateClothIndex);
        if (sourceParticles.Count != templateParticles.Count)
            errors.Add($"Particle count differs ({sourceParticles.Count} in HKCL, {templateParticles.Count} in BPHCL).");

        var sourceBones = source.GetBoneRows(sourceClothIndex);
        var templateBones = template.GetBoneRows(templateClothIndex);
        if (sourceBones.Count != templateBones.Count)
        {
            errors.Add($"Skeleton bone count differs ({sourceBones.Count} in HKCL, {templateBones.Count} in BPHCL).");
        }
        else
        {
            for (var index = 0; index < sourceBones.Count; index++)
            {
                if (!EquivalentBoneName(sourceBones[index].Name, templateBones[index].Name))
                {
                    errors.Add($"Bone {index} differs ({sourceBones[index].Name} vs {templateBones[index].Name}).");
                    break;
                }
            }
        }

        var sourceColliders = source.GetColliderRows(sourceClothIndex);
        var templateColliders = template.GetColliderRows(templateClothIndex);
        if (sourceColliders.Count != templateColliders.Count)
        {
            errors.Add($"Referenced collider count differs ({sourceColliders.Count} in HKCL, {templateColliders.Count} in BPHCL).");
        }
        else
        {
            for (var index = 0; index < sourceColliders.Count; index++)
            {
                if (!string.Equals(sourceColliders[index].ShapeType, templateColliders[index].ShapeType, StringComparison.Ordinal))
                {
                    errors.Add($"Collider {index} shape differs ({sourceColliders[index].ShapeType} vs {templateColliders[index].ShapeType}).");
                    break;
                }
            }
        }

        return new HkclToBphclTemplatePreflight(errors, notes);
    }

    public static NativeBphclDocument Apply(
        HkclService source,
        int sourceClothIndex,
        HkclService template,
        int templateClothIndex)
    {
        var preflight = Check(source, sourceClothIndex, template, templateClothIndex);
        if (!preflight.CanConvert)
            throw new InvalidOperationException(preflight.ToDisplayText());

        var sourceParticles = source.GetParticleRows(sourceClothIndex);
        var targetParticles = template.GetParticleRows(templateClothIndex).ToList();
        for (var index = 0; index < targetParticles.Count; index++)
        {
            var input = sourceParticles[index];
            var output = targetParticles[index];
            output.X = input.X;
            output.Y = input.Y;
            output.Z = input.Z;
            output.W = input.W;
            output.Mass = input.Mass;
            output.Radius = input.Radius;
            output.Friction = input.Friction;
        }
        template.UpdateParticleRows(templateClothIndex, targetParticles);

        var sourceBones = source.GetBoneRows(sourceClothIndex);
        var targetBones = template.GetBoneRows(templateClothIndex).ToList();
        for (var index = 0; index < targetBones.Count; index++)
        {
            var input = sourceBones[index];
            var output = targetBones[index];
            output.X = input.X;
            output.Y = input.Y;
            output.Z = input.Z;
            output.RotationX = input.RotationX;
            output.RotationY = input.RotationY;
            output.RotationZ = input.RotationZ;
            output.RotationW = input.RotationW;
        }
        template.UpdateBoneRows(templateClothIndex, targetBones);

        var sourceColliders = source.GetColliderRows(sourceClothIndex);
        var targetColliders = template.GetColliderRows(templateClothIndex).ToList();
        for (var index = 0; index < targetColliders.Count; index++)
        {
            var input = sourceColliders[index];
            var output = targetColliders[index];
            output.Transform = input.Transform;
            output.StartX = input.StartX;
            output.StartY = input.StartY;
            output.StartZ = input.StartZ;
            output.EndX = input.EndX;
            output.EndY = input.EndY;
            output.EndZ = input.EndZ;
            output.Radius = input.Radius;
            output.PlaneNormalX = input.PlaneNormalX;
            output.PlaneNormalY = input.PlaneNormalY;
            output.PlaneNormalZ = input.PlaneNormalZ;
        }
        template.UpdateColliderRows(targetColliders);

        return template.CaptureBphclNativeDocument()
            ?? throw new InvalidOperationException("The BPHCL template no longer has a native document.");
    }

    private static bool EquivalentBoneName(string left, string right) =>
        string.Equals(NormalizeBoneName(left), NormalizeBoneName(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeBoneName(string name)
    {
        var value = name.Trim();
        while (value.StartsWith("Link:", StringComparison.OrdinalIgnoreCase))
            value = value[5..];
        return value;
    }
}
