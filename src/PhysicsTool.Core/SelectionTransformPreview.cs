using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

// A render-only snapshot: no native graph parsing, constraint rebuilds or file writes per pointer event.
public sealed class SelectionTransformPreview
{
    private readonly ParticlePreviewData _source;
    private readonly PhysicsSelectionKind _kind;
    private readonly IReadOnlySet<int> _selected;
    private readonly bool _follow;
    private readonly IReadOnlyList<BoneEditRow> _bones;
    private readonly IReadOnlyList<ParticleEditRow> _particles;
    private readonly IReadOnlyList<ColliderEditRow> _colliders;
    private readonly IReadOnlyList<ParticlePreviewAttachment> _attachments;
    private readonly Dictionary<int, Matrix4x4> _world;
    private readonly bool _ragdoll;
    private readonly BoneBranchResizeGeometry? _resize;

    public SelectionTransformPreview(HkclService document, int cloth, ParticlePreviewData source,
        PhysicsSelectionKind kind, IReadOnlySet<int> selected, bool followBones, bool bakeBoneScale = false, Vector3? pivot = null)
    {
        _source = source; _kind = kind; _selected = selected.ToHashSet(); _follow = followBones;
        _ragdoll = document.IsHkrg;
        if (_ragdoll) _follow = false;
        _bones = kind == PhysicsSelectionKind.Bones ? document.GetBoneRows(cloth) : [];
        _particles = kind == PhysicsSelectionKind.Bones && (_follow || bakeBoneScale) ? document.GetParticleRows(cloth) : [];
        _colliders = kind != PhysicsSelectionKind.Particles ? document.GetColliderRows(cloth) : [];
        _attachments = kind == PhysicsSelectionKind.Bones && (_follow || bakeBoneScale) ? document.GetParticleBoneAttachments(cloth) : [];
        _world = BoneEditGeometry.WorldTransforms(_bones);
        if (bakeBoneScale)
        {
            document.ResizeClothBoneBranches(cloth, selected, Vector3.One);
            _resize = new(_bones, selected, source, _attachments, pivot);
        }
    }

    public ParticlePreviewData Sample(Matrix4x4 delta)
    {
        if (_ragdoll) RagdollEdits.ValidateTransform(delta);
        var particles = new Dictionary<int, Vector3>();
        var bones = new Dictionary<int, Matrix4x4>();
        var colliders = new Dictionary<int, ColliderEditRow>();
        var scale = new Vector3(delta.M11, delta.M22, delta.M33);
        var radii = new Dictionary<int, float>();
        if (_kind == PhysicsSelectionKind.Particles)
            foreach (var p in _source.Particles.Where(p => _selected.Contains(p.Index))) particles[p.Index] = Vector3.Transform(p.Position, delta);
        else if (_kind == PhysicsSelectionKind.Colliders)
            colliders = SelectionTransforms.TransformColliders(_colliders, _selected, delta).ToDictionary(c => c.Index);
        else
        {
            var edits = (_resize != null ? _resize.BakeBones(scale) : SelectionTransforms.TransformBones(_bones, _selected, delta)).ToDictionary(b => b.Index);
            var after = _bones.Select(b => edits.GetValueOrDefault(b.Index, b)).ToArray();
            bones = BoneEditGeometry.WorldTransforms(after);
            colliders = (_resize != null ? _resize.BakeColliders(_colliders, scale) : BoneEditGeometry.FollowColliders(_bones, after, _colliders)).ToDictionary(c => c.Index);
            if (_resize != null)
                foreach (var p in _source.Particles)
                {
                    particles[p.Index] = _resize.ParticlePosition(p.Index, p.Position, scale);
                    radii[p.Index] = p.Radius * BoneBranchResizeGeometry.RadiusScale(_resize.ParticleScale(p.Index, scale));
                }
            else if (_follow)
                particles = BoneEditGeometry.FollowParticles(_bones, after, _particles, _attachments)
                    .ToDictionary(p => p.Index, p => new Vector3(p.X, p.Y, p.Z));
        }
        var result = new ParticlePreviewData();
        result.Links.AddRange(_source.Links); result.Triangles.AddRange(_source.Triangles);
        result.RagdollConstraints.AddRange(_source.RagdollConstraints);
        foreach (var p in _source.Particles)
            result.Particles.Add(!particles.TryGetValue(p.Index, out var position) ? p : new ParticlePreviewPoint {
                Index = p.Index, Fixed = p.Fixed, Position = position, Radius = radii.GetValueOrDefault(p.Index, p.Radius),
                Mass = p.Mass, InverseMass = p.InverseMass, Friction = p.Friction, CollisionMask = p.CollisionMask });
        foreach (var b in _source.Bones)
        {
            if (!bones.TryGetValue(b.Index, out var world) || world == _world[b.Index]) { result.Bones.Add(b); continue; }
            Vector3 Axis(Vector3 axis, Vector3 fallback) => axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : fallback;
            result.Bones.Add(new BonePreviewPoint { Index = b.Index, EditorIndex = b.EditorIndex, ParentIndex = b.ParentIndex, Name = b.Name,
                IsReference = b.IsReference, IsSelectable = b.IsSelectable, Position = world.Translation,
                AxisX = _resize != null ? b.AxisX : Axis(new(world.M11, world.M12, world.M13), Vector3.UnitX),
                AxisY = _resize != null ? b.AxisY : Axis(new(world.M21, world.M22, world.M23), Vector3.UnitY),
                AxisZ = _resize != null ? b.AxisZ : Axis(new(world.M31, world.M32, world.M33), Vector3.UnitZ), StretchScale = b.StretchScale });
        }
        foreach (var c in _source.Colliders)
        {
            if (!colliders.TryGetValue(c.Index, out var row)) { result.Colliders.Add(c); continue; }
            result.Colliders.Add(new ColliderPreviewShape { Index = c.Index, Name = c.Name, BoneIndex = c.BoneIndex, Kind = c.Kind,
                CollisionBit = c.CollisionBit, TransformOrigin = new Vector3(row.Transform.M41, row.Transform.M42, row.Transform.M43),
                Start = new(row.StartX, row.StartY, row.StartZ), End = new(row.EndX, row.EndY, row.EndZ), Radius = row.Radius,
                EndRadius = _resize != null && _resize.Branch.Contains(c.BoneIndex) ? c.EndRadius * BoneBranchResizeGeometry.RadiusScale(scale) : c.EndRadius,
                PlaneNormal = new(row.PlaneNormalX, row.PlaneNormalY, row.PlaneNormalZ) });
        }
        return result;
    }
}
