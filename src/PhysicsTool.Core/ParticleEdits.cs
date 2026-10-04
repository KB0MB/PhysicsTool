using HKCLTool;

namespace PhysicsTool.Core;

public static class ParticleEdits
{
    // Call inside EditorSession.Edit so native geometry and row updates roll back together.
    public static void Apply(HkclService document, int cloth, IEnumerable<ParticleEditRow> edits)
    {
        var previous = document.GetParticleRows(cloth);
        var replacements = edits.ToDictionary(row => row.Index);
        if (replacements.Keys.Any(index => !previous.Any(row => row.Index == index)))
            throw new ArgumentOutOfRangeException(nameof(edits), "A particle no longer exists in this cloth.");
        var current = previous.Select(row => replacements.GetValueOrDefault(row.Index, row)).ToArray();
        if (previous.Zip(current).Any(pair => pair.First.X != pair.Second.X ||
            pair.First.Y != pair.Second.Y || pair.First.Z != pair.Second.Z))
            document.SynchronizeParticleConstraintGeometry(cloth, previous, current);
        // HKCL rebuilds fixed anchors and aggregate mass/radius from the complete list.
        document.UpdateParticleRows(cloth, current);
    }
}
