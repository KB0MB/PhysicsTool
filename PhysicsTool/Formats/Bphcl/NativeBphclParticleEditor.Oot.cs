namespace HKCLTool;

internal sealed partial class NativeBphclParticleEditor
{
    internal static void ValidateOotGeometry(NativeBphclDocument document, int clothIndex, bool forSimulation = false)
    {
        var edit = new NativeBphclParticleEditor(document);
        var cloth = document.Cloths[clothIndex];
        if (!forSimulation && cloth.ObjectSpaceSkins.Any(s => s.TransformSubset.Count == 0 && s.BoneFromSkinMeshTransforms.Count > 0))
            throw new InvalidOperationException("This cloth uses an implicit skin transform mapping that cannot be rebuilt yet.");
        foreach (var op in cloth.Operators.Where(o => o.ClassName == "hclObjectSpaceSkinPOperator"))
        {
            foreach (var name in new[] { "useSequentialDeformer", "useDualQuaternion" })
                if (edit.OptionalField(op.ItemIndex, name) is { } field && edit._data[checked((int)field)] != 0)
                    throw new InvalidOperationException("This cloth uses " + name + "; its rest-shape editing is not supported yet.");
        }
        foreach (var sim in cloth.SimCloths)
        {
            if (edit.Read(edit.Field(sim.ItemIndex, "skinData")) != 0)
                throw new InvalidOperationException("This cloth uses additional simulation skin data that cannot be rebuilt yet.");
            // Preview reads these factors without rebuilding the authored geometry.
            if (forSimulation) continue;
            // Nonempty runtime aerodynamic caches need a dedicated area rebuild.
            var field = edit.Field(sim.ItemIndex, "aerodynamicsData");
            var type = edit.Member(document.GetItem(sim.ItemIndex).TypeIndex, "aerodynamicsData").TypeIndex;
            foreach (var name in new[] { "perParticleLiftFactor", "perParticleDragFactor" })
                if (edit.Array(field + edit.Member(type, name).Offset) is { Count: > 0 })
                    throw new InvalidOperationException("This cloth has cached aerodynamic data; geometry editing is not supported for it yet.");
        }
    }
}
