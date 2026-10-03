using System.Collections;
using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    public void DeleteParticles(int clothIndex, IReadOnlyCollection<int> indices)
    {
        var count = GetParticleRows(clothIndex).Count;
        if (indices.Any(i => i < 0 || i >= count)) throw new ArgumentOutOfRangeException(nameof(indices));
        if (indices.Distinct().Count() == count) throw new InvalidOperationException("Keep at least one particle, or remove the whole cloth instead.");
        if (_bphcl?.NativeDocument is { } native)
        {
            var updated = NativeBphclParticleEditor.Delete(native, clothIndex, indices);
            _bphcl = CreateBphclSummary(updated, _bphcl.SourcePath);
            return;
        }
        var saved = SerializeRaw(RequireRoot());
        try { foreach (var index in indices.Distinct().OrderDescending()) DeleteParticle(clothIndex, index); }
        catch { _root = DeserializeRoot(saved); throw; }
    }

    public void AddClothConstraint(int clothIndex, IReadOnlyList<int> indices, ClothLinkOptions options)
    {
        var snapshot = CaptureState();
        try { AddClothConstraintCore(clothIndex, indices, options); }
        catch { RestoreState(snapshot); throw; }
    }

    private void AddClothConstraintCore(int clothIndex, IReadOnlyList<int> indices, ClothLinkOptions options)
    {
        if (indices.Count != 2 || indices.Distinct().Count() != 2) throw new ArgumentException("Select two different particles.");
        var particles = GetParticleRows(clothIndex);
        if (indices.Any(i => i < 0 || i >= particles.Count)) throw new ArgumentOutOfRangeException(nameof(indices));
        if (!float.IsFinite(options.Length) || options.Length <= 0 || !float.IsFinite(options.MinimumLength) ||
            options.MinimumLength < 0 || options.MinimumLength > options.Length || !float.IsFinite(options.Stiffness) ||
            options.Stiffness < 0 || !float.IsFinite(options.StretchStiffness) || options.StretchStiffness < 0)
            throw new ArgumentException("Lengths and stiffness must be finite and nonnegative; maximum length must be positive and at least minimum length.");
        if (!ClothConstraintCatalog.Kinds.Any(k => k.CanCreate && k.ClassName == options.ClassName)) throw new ArgumentException("Unsupported constraint type.");
        if (_bphcl?.NativeDocument is { } native)
        {
            var updated = NativeBphclParticleEditor.AddConstraint(native, clothIndex, indices, options);
            _bphcl = CreateBphclSummary(updated, _bphcl.SourcePath);
            return;
        }
        var cloth = GetClothDatas(RequireRoot()).ElementAt(clothIndex);
        var sim = GetFirst(GetValue(cloth, "simClothDatas"))!;
        var sets = GetMutableList(GetValue(sim, "staticConstraintSets"), "constraint sets");
        var simulators = (GetList(GetValue(cloth, "operators")) ?? System.Array.Empty<object>()).Where(o => o.GetType().Name == "hclSimulateOperator").ToArray();
        var target = sets.Cast<object>().FirstOrDefault(s => s.GetType().Name == options.ClassName && simulators.Any(op =>
            GetValue(op, "constraintExecution") is IList execution && (execution.Count == 0 || execution.Cast<object>().Any(i => ToInt(i, -1) == sets.IndexOf(s)))));
        var created = target == null;
        target ??= CreateHavokConstraintSet(cloth, sets, options.ClassName);
        var links = EnsureMutableObjectList(target, "links", "links");
        if (created)
        {
            SetValue(target, "name", ClothConstraintCatalog.Kinds.Single(k => k.ClassName == options.ClassName).Name);
            SetValue(target, "constraintId", sets.Count - 1);
        }
        if (links.Cast<object>().Any(l => ToInt(GetValue(l, "particleA"), -1) == indices[0] && ToInt(GetValue(l, "particleB"), -1) == indices[1] ||
            options.ClassName != "hclStretchLinkConstraintSet" && ToInt(GetValue(l, "particleA"), -1) == indices[1] && ToInt(GetValue(l, "particleB"), -1) == indices[0]))
            throw new InvalidOperationException("This constraint already exists.");
        var linkType = links.GetType().GetGenericArguments()[0];
        var link = Activator.CreateInstance(linkType)!;
        SetValue(link, "particleA", indices[0]); SetValue(link, "particleB", indices[1]);
        if (options.ClassName == "hclBendLinkConstraintSet")
        {
            SetValue(link, "bendMinLength", options.MinimumLength); SetValue(link, "stretchMaxLength", options.Length);
            SetValue(link, "bendStiffness", options.Stiffness); SetValue(link, "stretchStiffness", options.StretchStiffness);
        }
        else { SetValue(link, "restLength", options.Length); SetValue(link, "stiffness", options.Stiffness); }
        links.Add(link);
        if (created)
        {
            foreach (var op in GetList(GetValue(cloth, "operators")) ?? System.Array.Empty<object>())
                    if (GetValue(op, "constraintExecution") is IList execution && execution.Count > 0)
                    {
                        var index = sets.IndexOf(target);
                        var collision = execution.Cast<object>().Select(x => ToInt(x, -1)).ToList().IndexOf(-1);
                        execution.Insert(collision < 0 ? execution.Count : collision, index);
                    }
        }
    }

    private static void RefreshUnboundDraftBuffers(object cloth, object sim)
    {
        var operators = GetList(GetValue(cloth, "operators")) ?? System.Array.Empty<object>();
        if (operators.Any(o => o.GetType().Name != "hclSimulateOperator")) return;
        var count = (GetValue(sim, "particleDatas") as IList)?.Count ?? 0;
        var triangles = (GetValue(sim, "triangleIndices") as IList)?.Count / 3 ?? 0;
        foreach (var buffer in GetList(GetValue(cloth, "bufferDefinitions")) ?? System.Array.Empty<object>())
            if (ToInt(GetValue(buffer, "type"), 0) is 1 or 2)
            {
                SetValue(buffer, "numVertices", count);
                SetValue(buffer, "numTriangles", triangles);
            }
    }
}
