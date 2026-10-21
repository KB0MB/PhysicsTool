using System.Collections;
using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    public void ValidateClothBoneFit(int clothIndex)
    {
        if (!HasDocument || IsHelperBoneSidecar || IsHkrg)
            throw new InvalidOperationException("Select a cloth first.");
        if (IsBphcl)
            throw new InvalidOperationException("Fit cloth to bones currently supports HKCL. Native BPHCL rest-shape rebuilding is not available yet.");
        // The same ownership and operator checks apply to any whole-rest-shape edit.
        ValidateWholeClothMirror(clothIndex);
        var preview = GetParticlePreview(clothIndex);
        if (preview.BoneBindings.Count == 0 || preview.BoneBindings.Any(b => !b.UseNativeSimpleDeformer))
            throw new InvalidOperationException("This cloth needs native triangle-to-bone bindings before its shape can be fitted.");
    }

    public int FitClothToBones(int clothIndex)
    {
        ValidateClothBoneFit(clothIndex);
        var snapshot = CaptureState();
        try { return FitClothToBonesCore(clothIndex); }
        catch { RestoreState(snapshot); throw; }
    }

    public int MatchClothBonesToModel(int clothIndex, IReadOnlyDictionary<int, Matrix4x4> targets)
    {
        ValidateClothBoneFit(clothIndex);
        if (targets.Count == 0) return 0;
        var snapshot = CaptureState();
        try
        {
            var preview = GetParticlePreview(clothIndex);
            var before = GetBoneRows(clothIndex);
            var rows = GetBoneRows(clothIndex).ToDictionary(b => b.Index);
            var oldWorld = BoneEditGeometry.WorldTransforms(before);
            if (targets.Keys.Any(i => !rows.ContainsKey(i))) throw new ArgumentException("A selected cloth bone no longer exists.");
            var world = new Dictionary<int, Matrix4x4>();
            Matrix4x4 Resolve(int index)
            {
                if (index < 0) return Matrix4x4.Identity;
                if (world.TryGetValue(index, out var found)) return found;
                var row = rows[index];
                var parent = Resolve(row.ParentIndex);
                if (!Matrix4x4.Invert(parent, out var inverse)) throw new InvalidOperationException("A parent bone has a singular transform.");
                if (targets.TryGetValue(index, out var target))
                {
                    var local = target * inverse;
                    if (!Matrix4x4.Decompose(local, out var scale, out var rotation, out var position) ||
                        !float.IsFinite(scale.LengthSquared() + rotation.LengthSquared() + position.LengthSquared()) ||
                        MathF.Abs(scale.X * scale.Y * scale.Z) < 1e-8f)
                        throw new InvalidOperationException("A model bone has an invalid transform.");
                    var rebuilt = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
                    if (Vector3.DistanceSquared(new(local.M11, local.M12, local.M13), new(rebuilt.M11, rebuilt.M12, rebuilt.M13)) +
                        Vector3.DistanceSquared(new(local.M21, local.M22, local.M23), new(rebuilt.M21, rebuilt.M22, rebuilt.M23)) +
                        Vector3.DistanceSquared(new(local.M31, local.M32, local.M33), new(rebuilt.M31, rebuilt.M32, rebuilt.M33)) > 1e-8f)
                        throw new InvalidOperationException("The model pose requires shear, which cloth bones cannot store.");
                    row.X = position.X; row.Y = position.Y; row.Z = position.Z;
                    row.RotationX = rotation.X; row.RotationY = rotation.Y; row.RotationZ = rotation.Z; row.RotationW = rotation.W;
                    row.ScaleX = scale.X; row.ScaleY = scale.Y; row.ScaleZ = scale.Z;
                    return world[index] = rebuilt * parent;
                }
                var oldParent = row.ParentIndex < 0 ? Matrix4x4.Identity : oldWorld[row.ParentIndex];
                if (!Matrix4x4.Invert(oldParent, out var oldInverse)) throw new InvalidOperationException("A cloth bone has a singular transform.");
                return world[index] = oldWorld[index] * oldInverse * parent;
            }
            foreach (var index in rows.Keys) Resolve(index);
            var anchors = BoneEditGeometry.FollowParticles(before, rows.Values.ToArray(), GetParticleRows(clothIndex).Where(p => p.Fixed),
                GetParticleBoneAttachments(clothIndex)).ToDictionary(p => p.Index, ToPosition);
            UpdateBoneRows(clothIndex, rows.Values, false);
            return FitClothToBonesCore(clothIndex, preview, anchors);
        }
        catch { RestoreState(snapshot); throw; }
    }

    private int FitClothToBonesCore(int clothIndex, ParticlePreviewData? originalPreview = null, IReadOnlyDictionary<int, Vector3>? fixedTargets = null)
    {
        var preview = GetParticlePreview(clothIndex);
        var before = GetParticleRows(clothIndex);
        var old = before.ToDictionary(p => p.Index, ToPosition);
        var desiredBones = preview.Bones.ToDictionary(b => b.Index,
            b => FitBoneMatrix(b.Position, b.AxisX, b.AxisY, b.AxisZ));
        var simulated = new HkclPreviewSimulator(originalPreview ?? preview).GetBonePoses();
        var outputBones = preview.BoneBindings.Select(b => b.BoneIndex).Distinct().ToArray();
        var sums = new Dictionary<int, Vector3>();
        var counts = new Dictionary<int, int>();
        var changed = false;
        foreach (var binding in preview.BoneBindings)
        {
            if (!simulated.TryGetValue(binding.BoneIndex, out var pose) || !desiredBones.TryGetValue(binding.BoneIndex, out var target))
                throw new InvalidOperationException("A cloth output bone could not be resolved.");
            var original = FitBoneMatrix(pose.Position, pose.AxisX, pose.AxisY, pose.AxisZ);
            if (!Matrix4x4.Invert(original, out var inverse)) throw new InvalidOperationException("A cloth output bone has a singular transform.");
            // Infer local cloth size from spacing between simulation bones. A
            // translation alone should not retain the width of a shortened strip.
            var nearby = outputBones.Where(i => i != binding.BoneIndex && simulated.ContainsKey(i))
                .Select(i => (Index: i, Distance: Vector3.Distance(pose.Position, simulated[i].Position)))
                .Where(p => p.Distance > 1e-6f).OrderBy(p => p.Distance).Take(4).ToArray();
            var spacingScale = nearby.Length == 0 ? 1f : nearby.Average(p => Vector3.Distance(target.Translation, desiredBones[p.Index].Translation) / p.Distance);
            if (!float.IsFinite(spacingScale) || spacingScale < 1e-5f) throw new InvalidOperationException("The edited simulation bones collapse the cloth.");
            var delta = inverse * Matrix4x4.CreateScale(spacingScale) * target;
            foreach (var index in new[] { binding.ParticleA, binding.ParticleB, binding.ParticleC })
            {
                var displacement = Vector3.Transform(old[index], delta) - old[index];
                if (displacement.LengthSquared() > 1e-12f) changed = true;
                sums[index] = sums.GetValueOrDefault(index) + displacement;
                counts[index] = counts.GetValueOrDefault(index) + 1;
            }
        }
        foreach (var (index, position) in fixedTargets ?? new Dictionary<int, Vector3>())
        {
            sums[index] = position - old[index]; counts[index] = 1;
            changed |= sums[index].LengthSquared() > 1e-12f;
        }
        if (!changed) return 0;

        var neighbors = old.Keys.ToDictionary(i => i, _ => new HashSet<int>());
        void Edge(int a, int b) { if (a != b && neighbors.ContainsKey(a) && neighbors.ContainsKey(b)) { neighbors[a].Add(b); neighbors[b].Add(a); } }
        foreach (var t in preview.Triangles) { Edge(t.ParticleA, t.ParticleB); Edge(t.ParticleB, t.ParticleC); Edge(t.ParticleC, t.ParticleA); }
        foreach (var l in preview.Links) Edge(l.ParticleA, l.ParticleB);
        var displacementByParticle = old.Keys.ToDictionary(i => i, i => counts.TryGetValue(i, out var count) ? sums[i] / count : Vector3.Zero);
        var anchors = counts.Keys.Concat(before.Where(p => p.Fixed).Select(p => p.Index)).ToHashSet();
        // Shared triangle vertices move once. Smooth the displacement through the
        // remaining connected mesh, leaving unbound fixed anchors in place.
        for (var iteration = 0; iteration < 512; iteration++)
        {
            var next = new Dictionary<int, Vector3>(displacementByParticle); float maximum = 0;
            foreach (var (index, adjacent) in neighbors)
            {
                if (anchors.Contains(index) || adjacent.Count == 0) continue;
                var total = 0f; var sum = Vector3.Zero;
                foreach (var other in adjacent) { var w = 1f / MathF.Max(1e-6f, Vector3.Distance(old[index], old[other])); sum += displacementByParticle[other] * w; total += w; }
                next[index] = sum / total;
                maximum = MathF.Max(maximum, Vector3.DistanceSquared(next[index], displacementByParticle[index]));
            }
            displacementByParticle = next;
            if (maximum < 1e-14f) break;
            if (iteration == 511) throw new InvalidOperationException("The cloth fit did not converge. Try smaller bone edits.");
        }
        var positions = old.ToDictionary(p => p.Key, p => p.Value + displacementByParticle[p.Key]);
        foreach (var t in preview.Triangles) FitTriangleFrame(positions[t.ParticleA], positions[t.ParticleB], positions[t.ParticleC]);
        var scales = old.Keys.ToDictionary(i => i, i => {
            var adjacent = neighbors[i];
            var length = adjacent.Sum(j => Vector3.DistanceSquared(old[i], old[j]));
            var replacement = adjacent.Sum(j => Vector3.DistanceSquared(positions[i], positions[j]));
            return length > 1e-12f ? MathF.Sqrt(replacement / length) : 1f;
        });
        if (positions.Values.Any(p => !float.IsFinite(p.LengthSquared())) || scales.Values.Any(s => !float.IsFinite(s) || s < 1e-5f))
            throw new InvalidOperationException("These bone positions collapse part of the cloth. No changes were applied.");

        ApplyClothRestShape(clothIndex, preview, old, positions, scales, desiredBones);
        return positions.Count(p => Vector3.DistanceSquared(p.Value, old[p.Key]) > 1e-12f);
    }

    private void ApplyClothRestShape(int clothIndex, ParticlePreviewData preview,
        IReadOnlyDictionary<int, Vector3> old, IReadOnlyDictionary<int, Vector3> positions,
        IReadOnlyDictionary<int, float> scales, IReadOnlyDictionary<int, Matrix4x4> desiredBones)
    {
        var cloth = GetClothDatas(RequireRoot()).ElementAt(clothIndex);
        var simulation = GetFirst(GetValue(cloth, "simClothDatas"));
        FitConstraintRestData(cloth, simulation!, old, positions, scales);
        // Skin references may already have followed edited input bones. Measure
        // their current evaluated positions instead of applying the bone delta twice.
        var referenceBefore = new Dictionary<int, Vector3>(old);
        foreach (var attachment in GetParticleBoneAttachments(clothIndex))
        {
            var weights = attachment.BoneWeights;
            if (weights.Count == 0 || weights.Any(w => !w.ReferencePosition.HasValue || !float.IsFinite(w.Weight) || w.Weight <= 0)) continue;
            referenceBefore[attachment.ParticleIndex] = weights.Aggregate(Vector3.Zero, (p, w) => p + w.ReferencePosition!.Value * w.Weight) / weights.Sum(w => w.Weight);
        }
        RebaseParticleAnimationReferences(clothIndex, cloth, referenceBefore, positions);
        var edited = GetParticleRows(clothIndex);
        foreach (var p in edited) { var v = positions[p.Index]; p.X = v.X; p.Y = v.Y; p.Z = v.Z; p.Radius *= scales[p.Index]; }
        UpdateParticleRows(clothIndex, edited);
        foreach (var op in GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>())
        {
            if (op.GetType().Name != "hclSimpleMeshBoneDeformOperator") continue;
            var pairs = GetList(GetValue(op, "triangleBonePairs"))!;
            var matrices = GetValue(op, "localBoneTransforms") as IList ?? throw new InvalidDataException("Missing output bone matrices.");
            if (matrices.Count != pairs.Count) throw new InvalidDataException("Output bone matrix count is inconsistent.");
            for (var i = 0; i < pairs.Count; i++)
            {
                var bone = ToInt(GetValue(pairs[i], "boneOffset"), -1) / 64;
                var triangle = ToInt(GetValue(pairs[i], "triangleOffset"), -1) / 6;
                var t = preview.Triangles[triangle];
                var frame = FitTriangleFrame(positions[t.ParticleA], positions[t.ParticleB], positions[t.ParticleC]);
                if (!Matrix4x4.Invert(frame, out var inverse)) throw new InvalidOperationException("A fitted triangle collapsed.");
                SetListItem(matrices, i, desiredBones[bone] * inverse);
            }
        }
        FitPoseNormals(simulation!, preview.Triangles);
    }

    private static Matrix4x4 FitBoneMatrix(Vector3 p, Vector3 x, Vector3 y, Vector3 z) => new(
        x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, p.X, p.Y, p.Z, 1);

    private static Matrix4x4 FitTriangleFrame(Vector3 a, Vector3 b, Vector3 c)
    {
        var center = (a + b + c) / 3; var x = a - center; var y = b - center; var z = Vector3.Cross(x, y);
        if (!float.IsFinite(z.LengthSquared()) || z.LengthSquared() <= 1e-12f * x.LengthSquared() * y.LengthSquared() || x.LengthSquared() < 1e-16f || y.LengthSquared() < 1e-16f)
            throw new InvalidOperationException("The fitted cloth contains a collapsed triangle. Try smaller bone edits.");
        return FitBoneMatrix(center, x, y, z);
    }

    private static void FitConstraintRestData(object cloth, object simulation, IReadOnlyDictionary<int, Vector3> old,
        IReadOnlyDictionary<int, Vector3> positions, IReadOnlyDictionary<int, float> scales)
    {
        foreach (var set in GetList(GetValue(simulation, "staticConstraintSets")) ?? GetList(GetValue(cloth, "constraintSets")) ?? Array.Empty<object>())
        {
            var kind = set.GetType().Name;
            foreach (var link in GetList(GetValue(set, "links")) ?? Array.Empty<object>())
            {
                var a = ToInt(GetValue(link, "particleA"), -1); var b = ToInt(GetValue(link, "particleB"), -1);
                if (!positions.ContainsKey(a) || !positions.ContainsKey(b)) throw new InvalidDataException("A constraint references a missing particle.");
                if (kind == "hclBendStiffnessConstraintSet")
                {
                    var c = ToInt(GetValue(link, "particleC"), -1); var d = ToInt(GetValue(link, "particleD"), -1);
                    if (positions.ContainsKey(c) && positions.ContainsKey(d))
                    {
                        var ids = new[] { a, b, c, d };
                        var ratios = (from i in Enumerable.Range(0, 4) from j in Enumerable.Range(i + 1, 3 - i)
                            let length = Vector3.Distance(old[ids[i]], old[ids[j]]) where length > 1e-7f
                            select Vector3.Distance(positions[ids[i]], positions[ids[j]]) / length).ToArray();
                        if (ratios.Length > 0 && ratios.Max() - ratios.Min() < 1e-4f * ratios.Average())
                        {
                            ScaleConstraintFloat(link, "restCurvature", 1f / ratios.Average());
                            continue;
                        }
                    }
                    if (!positions.ContainsKey(c) || !positions.ContainsKey(d) ||
                        !HkclToBphclStandaloneGraphBuilder.TryGetBendRestConfiguration(positions[a], positions[b], positions[c], positions[d], out var wa, out var wb, out var wc, out var wd, out var curvature, out _, validateCandidate: false))
                        throw new InvalidOperationException($"A surface bend cannot be rebuilt for this shape ({a}, {b}, {c}, {d}). Try smaller bone edits.");
                    SetValue(link, "weightA", wa); SetValue(link, "weightB", wb); SetValue(link, "weightC", wc); SetValue(link, "weightD", wd);
                    SetValue(link, "restCurvature", curvature);
                }
                else
                {
                    var length = Vector3.Distance(old[a], old[b]); var newLength = Vector3.Distance(positions[a], positions[b]);
                    if (length <= 1e-8f || newLength <= 1e-8f) throw new InvalidOperationException("A fitted constraint has zero length.");
                    foreach (var field in new[] { "restLength", "bendMinLength", "stretchMaxLength", "compressionLength" })
                        ScaleConstraintFloat(link, field, newLength / length);
                }
            }
            foreach (var record in GetList(GetValue(set, "localConstraints")) ?? Array.Empty<object>())
            {
                var particle = ToInt(GetValue(record, "particleIndex"), -1);
                if (!scales.TryGetValue(particle, out var scale)) throw new InvalidDataException("A local range references a missing particle.");
                foreach (var field in new[] { "maximumDistance", "maxDistance", "shapeRadius", "maxNormalDistance", "minNormalDistance" })
                    if (ReadConstraintFloat(record, field) is { } value && float.IsFinite(value) && MathF.Abs(value) < 1e20f) ScaleConstraintFloat(record, field, scale);
            }
        }
    }

    private static void FitPoseNormals(object simulation, IReadOnlyList<ParticlePreviewTriangle> triangles)
    {
        foreach (var pose in GetList(GetValue(simulation, "simClothPoses")) ?? Array.Empty<object>())
        {
            if (GetValue(pose, "normals") is not IList normals || normals.Count == 0) continue;
            var points = GetList(GetValue(pose, "positions"))!.Cast<Vector4>().Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
            if (normals.Count != points.Length) throw new InvalidDataException("Cloth normal count is inconsistent.");
            var sums = new Vector3[points.Length];
            foreach (var t in triangles) { var n = Vector3.Cross(points[t.ParticleB] - points[t.ParticleA], points[t.ParticleC] - points[t.ParticleA]); sums[t.ParticleA] += n; sums[t.ParticleB] += n; sums[t.ParticleC] += n; }
            for (var i = 0; i < sums.Length; i++) if (sums[i].LengthSquared() > 1e-20f) SetListItem(normals, i, new Vector4(Vector3.Normalize(sums[i]), normals[i] is Vector4 v ? v.W : 0));
        }
    }
}
