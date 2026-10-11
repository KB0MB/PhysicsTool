namespace HKCLTool;

public sealed partial class HkclService
{
    private static void PreserveBphclLocalRangeStiffness(
        object cloth, NativeBphclSimCloth source, ICollection<string> notes)
    {
        var originals = new Dictionary<NativeBphclConstraintSet, object>();
        foreach (var bucket in source.ConstraintSets.Where(s => s.ClassName == "hclLocalRangeConstraintSet")
                     .GroupBy(s => s.IsAntiPinch))
        {
            var targets = (bucket.Key ? GetMutableAntiPinchConstraintSets(cloth) : GetMutableConstraintSets(cloth))
                .Cast<object>().Where(s => s.GetType().Name == "hclLocalRangeConstraintSet").ToArray();
            if (targets.Length != bucket.Count())
                throw new InvalidOperationException("Cannot safely map source local-range sets during conversion.");
            foreach (var pair in bucket.Zip(targets)) originals.Add(pair.First, pair.Second);
        }
        foreach (var sourceSet in source.ConstraintSets.Where(set =>
            set.ClassName == "hclLocalRangeConstraintSet" && set.LocalStiffnessConstraints.Count > 0))
        {
            var targetSets = sourceSet.IsAntiPinch ? GetMutableAntiPinchConstraintSets(cloth) : GetMutableConstraintSets(cloth);
            var target = originals[sourceSet];
            var records = EnsureMutableObjectList(target, "localConstraints", "local constraints");
            var parentStiffness = sourceSet.Values.TryGetValue("stiffness", out var value) ? value : 1.0;
            var stiffness = sourceSet.LocalConstraints.Select(_ => parentStiffness)
                .Concat(sourceSet.LocalStiffnessConstraints.Select(record =>
                    record.Values.TryGetValue("stiffness", out var perRecord) ? perRecord : parentStiffness)).ToArray();
            if (records.Count != stiffness.Length)
                throw new InvalidOperationException("The HKCL template local-range count does not match the source; per-particle softness cannot be preserved safely.");
            if (stiffness.Any(s => !double.IsFinite(s) || s < 0 || s > 1))
                throw new InvalidOperationException("Local-range stiffness must be finite and within 0 to 1.");

            // Contiguous runs preserve record order, even if a source repeats
            // a particle. Every source execution expands to these same runs.
            var originalRecords = records.Cast<object>().ToArray();
            var prototype = CloneForCurrentGraph(target);
            var runCount = 0;
            for (var start = 0; start < stiffness.Length;)
            {
                var end = start + 1;
                while (end < stiffness.Length && stiffness[end] == stiffness[start]) end++;
                var run = runCount++ == 0 ? target : CloneForCurrentGraph(prototype);
                if (!ReferenceEquals(run, target)) targetSets.Insert(targetSets.IndexOf(target) + runCount - 1, run);
                SetValue(run, "stiffness", stiffness[start]);
                var runRecords = EnsureMutableObjectList(run, "localConstraints", "local constraints");
                runRecords.Clear();
                for (var i = start; i < end; i++) AddListItem(runRecords, originalRecords[i]);
                start = end;
            }
            notes.Add($"Preserved {stiffness.Length} per-particle local-range stiffness values in {runCount} ordered HKCL set(s).");
        }
    }

    private static int GetBphclLocalRangeRunCount(NativeBphclConstraintSet set)
    {
        if (set.ClassName != "hclLocalRangeConstraintSet" || set.LocalStiffnessConstraints.Count == 0) return 1;
        var parent = set.Values.GetValueOrDefault("stiffness", 1.0);
        var values = set.LocalConstraints.Select(_ => parent).Concat(set.LocalStiffnessConstraints
            .Select(r => r.Values.GetValueOrDefault("stiffness", parent))).ToArray();
        return 1 + values.Zip(values.Skip(1)).Count(p => p.First != p.Second);
    }
}
