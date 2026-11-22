using System.Numerics;

namespace HKCLTool;

public partial class HkclService
{
    public int AddOotParticle(int cloth, int source)
    {
        ValidateOotGeometry(cloth);
        int index = GetParticleRows(cloth).Count;
        _bphcl = CreateBphclSummary(NativeBphclParticleEditor.AddOotParticle(_bphcl!.NativeDocument!, cloth, source,
            GetSkinOperatorData(cloth)), _bphcl.SourcePath);
        return index;
    }

    public void AddOotTriangle(int cloth, IReadOnlyList<int> indices)
    {
        ValidateOotGeometry(cloth);
        var original = _bphcl!;
        var updated = NativeBphclParticleEditor.AddOotTriangle(original.NativeDocument!, cloth, indices);
        if (ReferenceEquals(updated, original.NativeDocument)) return;
        try
        {
            _bphcl = CreateBphclSummary(updated, original.SourcePath);
            var positions = GetParticleRows(cloth).ToDictionary(p => p.Index, p => new Vector3(p.X, p.Y, p.Z));
            ApplyBphclRestShape(cloth, GetClothFitPreview(cloth), positions, positions, positions.Keys.ToDictionary(i => i, _ => 1f),
                BoneEditGeometry.WorldTransforms(GetBoneRows(cloth)), preserveUnchangedReferences: true);
        }
        catch { _bphcl = original; throw; }
    }

    public void UpdateOotBonePositions(int cloth, IReadOnlyDictionary<int, Vector3> positions, bool followParticles)
    {
        ValidateOotGeometry(cloth);
        var before = GetBoneRows(cloth);
        var after = GetBoneRows(cloth);
        foreach (var (index, position) in positions)
        {
            var bone = after.SingleOrDefault(b => b.Index == index) ?? throw new ArgumentException("Bone does not exist.");
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z)) throw new ArgumentException("Bone coordinates must be finite.");
            bone.X = position.X; bone.Y = position.Y; bone.Z = position.Z;
        }
        if (after.Zip(before).All(p => p.First.X == p.Second.X && p.First.Y == p.Second.Y && p.First.Z == p.Second.Z)) return;
        var desiredBones = BoneEditGeometry.WorldTransforms(after);
        var colliders = BoneEditGeometry.FollowColliders(before, after, GetColliderRows(cloth));
        var native = _bphcl!.NativeDocument!;
        foreach (var collider in colliders)
            if (Enumerable.Range(0, native.Cloths.Count).Any(c => c != cloth && GetColliderRows(c).Any(r => r.Index == collider.Index)))
                throw new InvalidOperationException("This bone moves a collider shared with another cloth. OoT shared-collider pose editing is not supported yet; other bones can still be edited.");
        var particles = GetParticleRows(cloth);
        var old = particles.ToDictionary(p => p.Index, p => new Vector3(p.X, p.Y, p.Z));
        var updated = new Dictionary<int, Vector3>(old);
        if (followParticles)
            foreach (var p in BoneEditGeometry.FollowParticles(before, after, particles, GetParticleBoneAttachments(cloth))) updated[p.Index] = new(p.X, p.Y, p.Z);
        var preview = GetClothFitPreview(cloth);
        var snapshot = CaptureState();
        try
        {
            var edits = positions.ToDictionary(p => p.Key, p => new NativeBphclBoneEdit(new Vector4(p.Value, native.Skeletons[cloth].Bones[p.Key].Translation.W)));
            var document = NativeBphclWriter.ApplyBoneEdits(native, cloth, edits);
            if (colliders.Count > 0) document = NativeBphclParticleEditor.MoveOotColliders(document, colliders);
            _bphcl = CreateBphclSummary(document, _bphcl.SourcePath);
            ApplyBphclRestShape(cloth, preview, old, updated, old.Keys.ToDictionary(i => i, _ => 1f), desiredBones);
        }
        catch { RestoreState(snapshot); throw; }
    }

    public void ValidateOotGeometry(int cloth)
    {
        if (!IsOot) throw new InvalidOperationException("Select an OoT cloth first.");
        ValidateClothBoneFit(cloth);
        NativeBphclParticleEditor.ValidateOotGeometry(_bphcl!.NativeDocument!, cloth);
    }

    public void UpdateOotGeometry(int cloth, IReadOnlyDictionary<int, Vector3> edits)
    {
        ValidateOotGeometry(cloth);
        var old = GetParticleRows(cloth).ToDictionary(p => p.Index, p => new Vector3(p.X, p.Y, p.Z));
        var positions = new Dictionary<int, Vector3>(old);
        foreach (var (index, position) in edits)
        {
            if (!old.ContainsKey(index) || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                throw new ArgumentException("Choose an existing particle and finite coordinates.");
            positions[index] = position;
        }
        if (positions.All(p => p.Value == old[p.Key])) return;
        var snapshot = CaptureState();
        try
        {
            var preview = GetClothFitPreview(cloth);
            var neighbors = old.Keys.ToDictionary(i => i, _ => new HashSet<int>());
            void Edge(int a, int b) { if (a != b && neighbors.ContainsKey(a) && neighbors.ContainsKey(b)) { neighbors[a].Add(b); neighbors[b].Add(a); } }
            foreach (var triangle in preview.Triangles) { Edge(triangle.ParticleA, triangle.ParticleB); Edge(triangle.ParticleB, triangle.ParticleC); Edge(triangle.ParticleC, triangle.ParticleA); }
            foreach (var link in preview.Links) Edge(link.ParticleA, link.ParticleB);
            var scales = old.Keys.ToDictionary(i => i, i =>
            {
                var before = neighbors[i].Sum(j => Vector3.DistanceSquared(old[i], old[j]));
                var after = neighbors[i].Sum(j => Vector3.DistanceSquared(positions[i], positions[j]));
                return before > 1e-12f ? MathF.Sqrt(after / before) : 1f;
            });
            if (scales.Values.Any(s => !float.IsFinite(s) || s < 1e-5f)) throw new InvalidOperationException("The edit collapses part of the cloth. No changes were applied.");
            ApplyBphclRestShape(cloth, preview, old, positions, scales, BoneEditGeometry.WorldTransforms(GetBoneRows(cloth)), preserveUnchangedReferences: true);
        }
        catch { RestoreState(snapshot); throw; }
    }

    public IReadOnlyList<OotClothProperty> GetOotProperties(int cloth, int item, bool collider) =>
        OotClothProperties.Get(_bphcl?.NativeDocument ?? throw new InvalidOperationException("No OoT cloth loaded."), cloth, item, collider);

    public void UpdateOotProperties(int cloth, int item, bool collider, IReadOnlyDictionary<uint, float> values)
    {
        var document = _bphcl?.NativeDocument ?? throw new InvalidOperationException("No OoT cloth loaded.");
        _bphcl = CreateBphclSummary(OotClothProperties.Apply(document, cloth, item, collider, values), _bphcl!.SourcePath);
    }
}
