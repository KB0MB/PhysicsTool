using HKCLTool;

namespace PhysicsTool.Core;

public static class RelationshipEdits
{
    public sealed record Field(string Property, string Label, float Value, bool Editable);
    private static readonly (string Property, string Label)[] Properties = [
        (nameof(ParticleRelationshipRow.RestLength), "Rest length"),
        (nameof(ParticleRelationshipRow.BendMinLength), "Bend minimum"),
        (nameof(ParticleRelationshipRow.StretchMaxLength), "Stretch maximum"),
        (nameof(ParticleRelationshipRow.MaximumDistance), "Maximum distance"),
        (nameof(ParticleRelationshipRow.Stiffness), "Stiffness"),
        (nameof(ParticleRelationshipRow.BendStiffness), "Bend stiffness"),
        (nameof(ParticleRelationshipRow.StretchStiffness), "Stretch stiffness"),
        (nameof(ParticleRelationshipRow.CompressionLength), "Compression length")];

    public static bool Same(ParticleRelationshipRow a, ParticleRelationshipRow b) =>
        a.Kind == b.Kind && a.ConstraintSetIndex == b.ConstraintSetIndex && a.LinkIndex == b.LinkIndex &&
        a.LocalConstraintIndex == b.LocalConstraintIndex && a.ParticleA == b.ParticleA && a.ParticleB == b.ParticleB;

    public static IReadOnlyList<Field> Fields(ParticleRelationshipRow row, bool bphcl) => Properties
        .Where(p => typeof(ParticleRelationshipRow).GetProperty(p.Property)!.GetValue(row) is float)
        .Select(p => new Field(p.Property, p.Label, (float)typeof(ParticleRelationshipRow).GetProperty(p.Property)!.GetValue(row)!,
            row.IsEditable && p.Property != nameof(row.CompressionLength) &&
            (row.LocalConstraintIndex < 0 || bphcl || p.Property == nameof(row.MaximumDistance))))
        .ToArray();

    public static void Apply(HkclService document, int cloth, int particle, ParticleRelationshipRow identity, IReadOnlyDictionary<string, float> values)
    {
        if (!document.CanEditConstraintValues) throw new InvalidOperationException("Constraint values cannot be edited for this document.");
        var row = document.GetParticleRelationships(cloth, particle).SingleOrDefault(r => Same(r, identity))
            ?? throw new InvalidOperationException("The selected relationship no longer exists.");
        var fields = Fields(row, document.IsBphcl).Where(f => f.Editable).ToDictionary(f => f.Property);
        foreach (var (property, value) in values)
        {
            if (!fields.ContainsKey(property)) throw new InvalidOperationException("This relationship field is read-only.");
            if (!float.IsFinite(value) || value < 0) throw new ArgumentException("Constraint values must be finite and non-negative.");
            if (property is nameof(row.RestLength) or nameof(row.StretchMaxLength) && value == 0)
                throw new ArgumentException("Rest and maximum link lengths must be positive.");
            typeof(ParticleRelationshipRow).GetProperty(property)!.SetValue(row, value);
        }
        if (row.BendMinLength is { } minimum && row.StretchMaxLength is { } maximum && minimum > maximum)
            throw new ArgumentException("Bend minimum cannot exceed stretch maximum.");
        document.UpdateParticleRelationshipRows(cloth, [row]);
    }
}
