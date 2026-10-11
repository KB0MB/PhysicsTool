using System.Collections;
using System.Numerics;

namespace HKCLTool;

public sealed partial class HkclService
{
    public void DeleteBones(int clothIndex, IReadOnlyCollection<int> indices)
    {
        if (IsBphcl) throw new InvalidOperationException("Native BPHCL bone deletion is not supported: packed skin and transform trackers must be rebuilt. No changes were made.");
        if (indices.Count == 0) return;
        var root = RequireRoot();
        var skeleton = GetSkeletons(root).ElementAtOrDefault(clothIndex) ?? throw new ArgumentOutOfRangeException(nameof(clothIndex));
        var cloth = GetClothDatas(root).ElementAt(clothIndex);
        var rows = GetBoneRows(clothIndex);
        var removed = indices.ToHashSet();
        if (removed.Any(i => i < 0 || i >= rows.Count)) throw new ArgumentOutOfRangeException(nameof(indices));
        if (removed.Count == rows.Count) throw new InvalidOperationException("Keep at least one bone in the cloth skeleton.");
        var graph = EnumerateObjects(cloth).ToArray();
        var supportedOperators = new HashSet<string>(StringComparer.Ordinal) {
            "hclGatherAllVerticesOperator", "hclCopyVerticesOperator", "hclMoveParticlesOperator", "hclSimulateOperator",
            "hclObjectSpaceSkinPOperator", "hclBoneSpaceSkinPOperator", "hclSimpleMeshBoneDeformOperator" };
        if ((GetList(GetValue(cloth, "operators")) ?? Array.Empty<object>()).Any(op => !supportedOperators.Contains(op.GetType().Name)))
            throw new InvalidOperationException("This cloth contains an operator whose bone references cannot safely be remapped yet.");
        var indexOwners = graph.Where(item => GetValue(item, "transformIndex") != null || GetValue(item, "transformSubset") != null ||
            GetValue(item, "transformIndices") != null || GetValue(item, "boneOffset") is ushort || GetValue(item, "perComponentTransformTrackers") != null)
            .ToHashSet(ReferenceEquality.Instance);
        if (GetSkeletons(root).Where((_, i) => i != clothIndex).Any(s => ReferenceEquals(s, skeleton)) ||
            GetClothDatas(root).Where((_, i) => i != clothIndex).Any(other => EnumerateObjects(other).Any(indexOwners.Contains)))
            throw new InvalidOperationException("This skeleton or its bone bindings are shared by another cloth. Separate those bindings before deleting bones.");
        // Only the single physics-skeleton transform domain can be remapped here.
        var sets = GetList(GetValue(cloth, "transformSetDefinitions")) ?? Array.Empty<object>();
        if (sets.Count > 1 || sets.Any(s => ToInt(GetValue(s, "numTransforms"), rows.Count) > rows.Count))
            throw new InvalidOperationException("This cloth has multiple or nonstandard transform sets; bone deletion cannot safely remap them yet.");
        if ((GetList(GetValue(skeleton, "localFrames"))?.Count ?? 0) > 0 || (GetList(GetValue(skeleton, "partitions"))?.Count ?? 0) > 0)
            throw new InvalidOperationException("This skeleton has local frames or partitions that bone deletion cannot remap yet.");
        foreach (var item in graph)
        {
            if ((GetList(GetValue(item, "transformSubset")) ?? Array.Empty<object>()).Any(v => removed.Contains(ToInt(v, -1))))
                throw new InvalidOperationException("A selected bone is used by cloth skinning. Rebind its particles before deleting the bone.");
            if (GetValue(item, "boneOffset") is ushort offset && removed.Contains(offset / 64))
                throw new InvalidOperationException("A selected bone is driven by a mesh-deform binding. Remove that output binding before deleting it.");
            if (item.GetType().Name != "hclCollidable" && GetValue(item, "transformIndex") is { } value && removed.Contains(ToInt(value, -1)))
                throw new InvalidOperationException("A selected bone is used by a motion or constraint transform. Change that binding before deleting it.");
        }
        var colliders = GetColliderRows(clothIndex);
        if (colliders.Any(c => removed.Contains(c.BoneIndex)))
            throw new InvalidOperationException("A selected bone has a collider bound to it. Rebind or delete that collider first.");
        var kept = rows.Where(b => !removed.Contains(b.Index)).ToArray();
        var map = kept.Select((b, i) => (b.Index, NewIndex: i)).ToDictionary(b => b.Index, b => b.NewIndex);
        int Remap(int value) => value < 0 ? value : map.TryGetValue(value, out var target) ? target : throw new InvalidDataException("An unexpected bone reference prevents deletion.");
        var world = BoneEditGeometry.WorldTransforms(rows);
        foreach (var b in kept)
        {
            var parent = b.ParentIndex;
            while (removed.Contains(parent)) parent = rows[parent].ParentIndex;
            if (parent != b.ParentIndex)
            {
                var parentWorld = parent < 0 ? Matrix4x4.Identity : world[parent];
                if (!Matrix4x4.Invert(parentWorld, out var inverse) || !Matrix4x4.Decompose(world[b.Index] * inverse, out var scale, out var rotation, out var translation))
                    throw new InvalidOperationException("Cannot preserve this child's transform while reparenting.");
                b.X = translation.X; b.Y = translation.Y; b.Z = translation.Z;
                b.RotationX = rotation.X; b.RotationY = rotation.Y; b.RotationZ = rotation.Z; b.RotationW = rotation.W;
                b.ScaleX = scale.X; b.ScaleY = scale.Y; b.ScaleZ = scale.Z;
            }
            b.ParentIndex = parent;
        }
        var snapshot = CaptureState();
        try
        {
            UpdateBoneRowsCore(clothIndex, kept);
            foreach (var item in graph)
            {
                if (GetValue(item, "transformSubset") is IList subset)
                    for (var i = 0; i < subset.Count; i++) SetListItem(subset, i, Remap(ToInt(subset[i], -1)));
                if (GetValue(item, "transformIndices") is IList transforms)
                    for (var i = 0; i < transforms.Count; i++) { var v = ToInt(transforms[i], -1); if (v != ushort.MaxValue) SetListItem(transforms, i, Remap(v)); }
                if (GetValue(item, "transformIndex") is { } index && ToInt(index, -1) is var boneIndex && boneIndex >= 0)
                    SetValue(item, "transformIndex", Remap(boneIndex));
                if (GetValue(item, "boneOffset") is ushort offset) SetValue(item, "boneOffset", checked((ushort)(Remap(offset / 64) * 64)));
                if (GetValue(item, "perComponentTransformTrackers") is not IEnumerable trackers) continue;
                foreach (var tracker in trackers)
                foreach (var member in new[] { "read", "readBeforeWrite", "written" })
                {
                    var storage = GetValue(GetValue(tracker, member), "storage");
                    if (GetValue(storage, "words") is not IList words) continue;
                    var old = words.Cast<object>().Select(ToRuntimeUInt32).ToArray();
                    var bits = ToInt(GetValue(storage, "numBits"), 0);
                    if (bits == 0) continue;
                    if (bits > rows.Count || old.Length < (bits + 31) / 32) throw new InvalidDataException("Nonstandard transform tracker length; no bones were deleted.");
                    var newBits = kept.Count(b => b.Index < bits);
                    var packed = new uint[(newBits + 31) / 32];
                    foreach (var (before, after) in map)
                        if (before < bits && (old[before / 32] & (1u << (before % 32))) != 0) packed[after / 32] |= 1u << (after % 32);
                    words.Clear(); foreach (var word in packed) words.Add(word);
                    SetValue(storage, "numBits", newBits);
                }
            }
            var bones = GetValue(skeleton, "bones") as IList ?? throw new InvalidDataException("Missing bones.");
            var parents = (IList)GetValue(skeleton, "parentIndices")!;
            var poses = (IList)GetValue(skeleton, "referencePose")!;
            foreach (var index in removed.OrderDescending()) { bones.RemoveAt(index); parents.RemoveAt(index); poses.RemoveAt(index); }
            for (var i = 0; i < parents.Count; i++) SetListItem(parents, i, Remap(kept[i].ParentIndex));
            foreach (var set in sets) SetValue(set, "numTransforms", kept.Count(b => b.Index < ToInt(GetValue(set, "numTransforms"), rows.Count)));
        }
        catch { RestoreState(snapshot); throw; }
    }
}
