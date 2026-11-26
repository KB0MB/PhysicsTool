using System.Numerics;
using HKCLTool;

namespace PhysicsTool.Core;

internal sealed class IconArmorPose
{
    private readonly (ToolboxActorModel Model, VisualModelData Base)[] _models;
    private readonly ToolboxActorAnimation? _animation;
    private readonly bool _headMannequin;
    private readonly bool _characterPose;
    private readonly Matrix4x4 _earRotation;
    internal Vector3 EarRotationDegrees { get; }
    internal IReadOnlyList<string> Bones { get; }
    internal bool HasRecoveredPose => !_characterPose && _models.Any(m => IconRecoveredPose.Supports(m.Model));
    internal bool UsesRecoveredIconPose => !_characterPose && (_headMannequin || _models.Any(m => m.Model.Name.EndsWith("_Upper", StringComparison.Ordinal) && IconRecoveredPose.Supports(m.Model)));
    internal IconArmorBelt? Belt { get; set; }
    internal IconNeckSplice? NeckSplice { get; set; }
    internal bool HasBelt => Belt != null || _models.Any(m => IconArmorProps.IsModel(m.Model) && m.Model.RestPose.Meshes.Any(mesh => mesh.Name.StartsWith("Icon_Lower_Belt__", StringComparison.Ordinal)));
    internal VisualModelData RebindMaterials(VisualModelData geometry)
    {
        var materials = _models.SelectMany(m => m.Model.RestPose.Meshes.Select(mesh => (Name: IconArmorProps.MeshName(m.Model, mesh.Name), Mesh: mesh))).ToDictionary(m => m.Name, m => m.Mesh, StringComparer.Ordinal);
        var result = new VisualModelData
        {
            SourcePath = geometry.SourcePath, AnimationSourcePath = geometry.AnimationSourcePath, AnimationFrame = geometry.AnimationFrame,
            BoundsMinimum = geometry.BoundsMinimum, BoundsMaximum = geometry.BoundsMaximum, TriangleCount = geometry.TriangleCount,
            EmbeddedTextureCount = geometry.EmbeddedTextureCount, ExternalTextureCount = geometry.ExternalTextureCount,
            DefaultTextureMeshCount = geometry.DefaultTextureMeshCount
        };
        result.Bones.AddRange(geometry.Bones); result.Warnings.AddRange(geometry.Warnings);
        foreach (var mesh in geometry.Meshes)
        {
            // Optional belt/waist meshes are not part of the armor's dye animation.
            if (!materials.TryGetValue(mesh.Name, out var appearance)) { result.Meshes.Add(mesh); continue; }
            result.Meshes.Add(new VisualModelMesh
            {
                Name = mesh.Name, UseMissingTextureFallback = appearance.UseMissingTextureFallback,
                Vertices = mesh.Vertices, Indices = mesh.Indices, Tangents = mesh.Tangents, TangentUvSet = mesh.TangentUvSet, ShadowOffsets = mesh.ShadowOffsets,
                UvSets = mesh.UvSets, BaseColor = mesh.BaseColor, CullingMode = mesh.CullingMode,
                Texture = appearance.Texture, SpecularTexture = appearance.SpecularTexture, NormalTexture = appearance.NormalTexture,
                AmbientOcclusionTexture = appearance.AmbientOcclusionTexture, AuxiliaryTexture = appearance.AuxiliaryTexture,
                EyeGlintTexture = appearance.EyeGlintTexture, Material = appearance.Material.Clone()
            });
        }
        IconMannequinMaterial.ConfigureShading(result);
        return result;
    }
    internal IconArmorPose(IEnumerable<ToolboxActorModel> models, ToolboxActorAnimation? animation, bool headMannequin = false,
        Vector3 earRotationDegrees = default, bool characterPose = false)
    {
        _animation = animation;
        _headMannequin = headMannequin;
        _characterPose = characterPose;
        EarRotationDegrees = earRotationDegrees;
        var radians = earRotationDegrees * (MathF.PI / 180);
        _earRotation = Matrix4x4.CreateRotationX(radians.X) * Matrix4x4.CreateRotationY(radians.Y) * Matrix4x4.CreateRotationZ(radians.Z);
        _models = models.Select(m => (m, m.Pose(animation, 0))).ToArray();
        Bones = _models.SelectMany(m => m.Base.Bones.Select(b => b.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
    }
    internal VisualModelData Apply(IReadOnlyDictionary<string, Vector3> angles, bool iconPose = false,
        ToolboxActorAnimation? imported = null, double frame = 0, bool recoveredPose = false, bool addBelt = false, bool restPose = false,
        IReadOnlyDictionary<string, VisualBoneOverride>? finalOverrides = null)
    {
        var result = new VisualModelData { BoundsMinimum = new(float.MaxValue), BoundsMaximum = new(float.MinValue) };
        bool beltAdded = false;
        foreach (var (model, cachedBasis) in _models)
        {
            var animation = restPose ? null : imported ?? _animation;
            var basis = restPose ? model.RestPose : imported == null ? cachedBasis : model.Pose(imported, frame);
            var bones = basis.Bones.ToDictionary(b => b.Index);
            var restBones = model.RestPose.Bones.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);
            var restIndices = model.RestPose.Bones.ToDictionary(b => b.Index);
            var worlds = new Dictionary<int, Matrix4x4>();
            Matrix4x4 World(int index)
            {
                if (worlds.TryGetValue(index, out var existing)) return existing;
                var bone = bones[index];
                if (finalOverrides?.TryGetValue(bone.Name, out var final) == true) return worlds[index] = final.WorldMatrix;
                var parent = bones.GetValueOrDefault(bone.ParentIndex);
                var inverse = Matrix4x4.Identity;
                if (parent != null && !Matrix4x4.Invert(parent.WorldMatrix, out inverse)) throw new InvalidDataException("Singular armor parent transform.");
                var local = bone.WorldMatrix * inverse;
                if (recoveredPose && !_characterPose && restBones.TryGetValue(bone.Name, out var original))
                {
                    var parentInverse = Matrix4x4.Identity;
                    if (restIndices.TryGetValue(original.ParentIndex, out var originalParent)) Matrix4x4.Invert(originalParent.WorldMatrix, out parentInverse);
                    string poseModel = _headMannequin ? "Armor_000_Upper" : IconArmorProps.IsModel(model) ? _models.First(m => !IconArmorProps.IsModel(m.Model)).Model.Name : model.Name;
                    if (IconRecoveredPose.TryLocal(poseModel, bone.Name, original.WorldMatrix * parentInverse, out var recovered)) local = recovered;
                }
                // Native ArmorHead modifier: the same local rotation on both ears,
                // composed before the bone pose in our row-vector convention.
                if (EarRotationDegrees != Vector3.Zero && bone.Name is "Ear_L" or "Ear_R") local = _earRotation * local;
                // The editable icon preset opens the idle hand using the model's own
                // finger orientations; translations and scale remain animation-driven.
                if (iconPose && bone.Name.StartsWith("Finger_", StringComparison.Ordinal) && restBones.TryGetValue(bone.Name, out var rest))
                {
                    var restInverse = Matrix4x4.Identity;
                    if (restIndices.TryGetValue(rest.ParentIndex, out var restParent) && !Matrix4x4.Invert(restParent.WorldMatrix, out restInverse))
                        throw new InvalidDataException("Singular armor rest transform.");
                    if (!Matrix4x4.Decompose(rest.WorldMatrix * restInverse, out _, out var rotation, out _) ||
                        !Matrix4x4.Decompose(local, out var scale, out var idleRotation, out var position))
                        throw new InvalidDataException("Invalid armor finger transform.");
                    rotation = Quaternion.Slerp(idleRotation, rotation, .5f);
                    local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
                }
                if (iconPose && bone.Name is "Arm_1_L" or "Arm_1_R")
                {
                    var translation = local.Translation; local.Translation = Vector3.Zero;
                    local *= Matrix4x4.CreateRotationX((bone.Name.EndsWith("_L", StringComparison.Ordinal) ? 15 : -15) * MathF.PI / 180);
                    local.Translation = translation;
                }
                if (angles.TryGetValue(bone.Name, out var degrees) && degrees != Vector3.Zero)
                {
                    if (!float.IsFinite(degrees.LengthSquared())) throw new InvalidDataException("Invalid pose rotation.");
                    var translation = local.Translation; local.Translation = Vector3.Zero;
                    var r = degrees * (MathF.PI / 180);
                    local *= Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z);
                    local.Translation = translation;
                }
                return worlds[index] = local * (parent == null ? Matrix4x4.Identity : World(parent.Index));
            }
            var overrides = basis.Bones.ToDictionary(b => b.Name, b => VisualBoneTransform.Override(b.Name, World(b.Index)), StringComparer.OrdinalIgnoreCase);
            var posed = angles.Count == 0 && !iconPose && !recoveredPose && finalOverrides == null && EarRotationDegrees == Vector3.Zero ? basis : model.Pose(animation, frame, overrides);
            var byIndex = posed.Bones.ToDictionary(b => b.Index);
            int CopyBone(VisualModelBone bone)
            {
                var existing = result.Bones.FirstOrDefault(b => b.Name == bone.Name);
                if (existing != null) return existing.Index;
                int parent = byIndex.TryGetValue(bone.ParentIndex, out var parentBone) ? CopyBone(parentBone) : -1;
                int index = result.Bones.Count;
                result.Bones.Add(VisualBoneTransform.Bone(index, parent, bone.Name, bone.WorldMatrix)); return index;
            }
            foreach (var bone in posed.Bones) CopyBone(bone);
            foreach (var mesh in posed.Meshes) ShrineSceneAssets.AddMesh(result, mesh, Matrix4x4.Identity, IconArmorProps.MeshName(model, mesh.Name));
            if (!beltAdded && Belt != null && posed.Bones.Any(b => b.Name == "Waist"))
            { Belt.AddTo(result, posed, animation, frame, addBelt); beltAdded = true; }
        }
        NeckSplice?.Apply(result);
        IconMannequinMaterial.ConfigureShading(result);
        return result;
    }
}
