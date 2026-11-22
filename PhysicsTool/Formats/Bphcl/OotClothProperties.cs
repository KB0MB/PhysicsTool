using System.Buffers.Binary;

namespace HKCLTool;

public sealed record OotClothProperty(uint Offset, string Group, string Label, float Value, float Minimum);

// This writer changes validated scalar fields only. Geometry/topology still requires
// a separate 2025 rest-shape rebuild, so it never enters the TotK structural writer.
public static class OotClothProperties
{
    private static NativeBphclTypeMember Member(NativeBphclDocument document, uint type, string name)
    {
        for (int depth = 0; type != 0 && depth < 64; depth++)
        {
            var definition = document.TypeDefinitions.Single(d => d.TypeIndex == type);
            var field = definition.Members.FirstOrDefault(m => m.Name == name || m.Name == "m_" + name);
            if (field != null) return field;
            type = definition.ParentTypeIndex;
        }
        throw new NotSupportedException("Missing OoT field: " + name);
    }

    private static uint Field(NativeBphclDocument document, int item, string name) =>
        checked(document.GetItem(item).DataOffset + Member(document, document.GetItem(item).TypeIndex, name).Offset);

    private static float Read(NativeBphclDocument document, uint offset) =>
        BinaryPrimitives.ReadSingleLittleEndian(document.Bytes.AsSpan(checked(document.DataSection!.PayloadOffset + (int)offset), 4));

    public static IReadOnlyList<OotClothProperty> Get(NativeBphclDocument document, int clothIndex, int itemIndex, bool collider)
    {
        if (!document.IsOot) throw new InvalidOperationException("Expected OoT cloth.");
        var fields = new List<OotClothProperty>();
        void Add(uint offset, string group, string label, float min = 0) => fields.Add(new(offset, group, label, Read(document, offset), min));
        if (collider)
        {
            var shape = document.Colliders.Single(c => c.Index == itemIndex).Shape;
            var item = document.Items.Select((value, index) => (value, index)).FirstOrDefault(p => p.value.DataOffset == shape.DataOffset && document.GetTypeName(p.value.TypeIndex) == shape.TypeName);
            if (item.value == null) return fields;
            if (shape.TypeName == "hclCapsuleShape") Add(Field(document, item.index, "radius"), "Shape", "Radius");
            if (shape.TypeName == "hclSphereShape") Add(checked(Field(document, item.index, "sphere") + 12), "Shape", "Radius");
            return fields;
        }

        var simulation = document.Cloths[clothIndex].SimCloths.First();
        var particle = simulation.Particles.Single(p => p.Index == itemIndex);
        if (!particle.Fixed) Add(particle.PhysicsDataOffset, "Particle", "Mass", float.Epsilon);
        Add(particle.PhysicsDataOffset + 8, "Particle", "Radius");
        Add(particle.PhysicsDataOffset + 12, "Particle", "Friction");
        foreach (var set in simulation.ConstraintSets)
        foreach (var record in set.Links.Concat(set.LocalConstraints).Concat(set.LocalStiffnessConstraints))
        {
            if (record.ParticleA != itemIndex && record.ParticleB != itemIndex) continue;
            var group = $"{set.Name} [{set.Index}] / {record.ParticleA}" + (record.ParticleB is { } b ? $" - {b}" : "") + $" / {record.Index}";
            foreach (var (name, offset) in record.EditableValueOffsets)
            {
                // The native corpus contains negative coefficients as well as signed distances.
                // Do not impose the normalized editor's coefficient ranges on serialized values.
                const float minimum = -1000000;
                var address = checked(record.DataOffset + offset);
                if (!fields.Any(f => f.Offset == address)) Add(address, group, name, minimum);
            }
        }
        return fields;
    }

    public static NativeBphclDocument Apply(NativeBphclDocument document, int cloth, int item, bool collider, IReadOnlyDictionary<uint, float> values)
    {
        var allowed = Get(document, cloth, item, collider).ToDictionary(f => f.Offset);
        var bytes = document.Bytes.ToArray();
        var changed = new Dictionary<uint, float>();
        void Write(uint offset, float value)
        {
            if (!float.IsFinite(value)) throw new ArgumentException("Values must be finite.");
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(checked(document.DataSection!.PayloadOffset + (int)offset), 4), value);
        }
        foreach (var (offset, value) in values)
        {
            if (!allowed.TryGetValue(offset, out var field)) throw new InvalidOperationException("This OoT property is not editable.");
            if (value == field.Value) continue;
            if (value < field.Minimum) throw new ArgumentOutOfRangeException(field.Label, "Value is below its allowed range.");
            changed[offset] = value;
            Write(offset, value);
        }
        if (!collider)
        {
            var simulation = document.Cloths[cloth].SimCloths.First();
            var particle = simulation.Particles.Single(p => p.Index == item);
            if (changed.TryGetValue(particle.PhysicsDataOffset, out var mass)) Write(particle.PhysicsDataOffset + 4, 1 / mass);
            var totalMass = simulation.Particles.Sum(p => values.GetValueOrDefault(p.PhysicsDataOffset, p.Mass));
            var maxRadius = simulation.Particles.Max(p => values.GetValueOrDefault(p.PhysicsDataOffset + 8, p.Radius));
            if (changed.ContainsKey(particle.PhysicsDataOffset)) Write(Field(document, simulation.ItemIndex, "totalMass"), totalMass);
            if (changed.ContainsKey(particle.PhysicsDataOffset + 8)) Write(Field(document, simulation.ItemIndex, "maxParticleRadius"), maxRadius);
        }
        return NativeBphclDocument.Parse(bytes);
    }
}
