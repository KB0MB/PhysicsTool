using System.Numerics;
using HKCLTool;

namespace PhysicsTool.Core;

internal sealed class IconArmorBelt
{
    private readonly VisualModelMesh? _mesh;
    private readonly Matrix4x4 _toWaist;
    private readonly ToolboxActorModel? _skinned;
    private IconArmorBelt(VisualModelMesh mesh, Matrix4x4 toWaist) { _mesh = mesh; _toWaist = toWaist; }
    private IconArmorBelt(ToolboxActorModel skinned) { _skinned = skinned; }
    internal static string OverridePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhysicsTool", "IconTool", "Belt.sbfres");
    internal bool IsCustom => _skinned != null;
    internal void AddTo(VisualModelData model, VisualModelData pose, ToolboxActorAnimation? animation, double frame, bool addBelt)
    {
        if (_skinned != null)
        {
            var overrides = pose.Bones.ToDictionary(b => b.Name, b => VisualBoneTransform.Override(b.Name, b.WorldMatrix));
            foreach (var mesh in _skinned.Pose(animation, frame, overrides).Meshes)
                if (IsSkin(mesh) || addBelt)
                    ShrineSceneAssets.AddMesh(model, mesh, Matrix4x4.Identity, IsSkin(mesh) ? "Icon waist skin" : "Icon belt");
        }
        else if (addBelt && _mesh != null && pose.Bones.FirstOrDefault(b => b.Name == "Waist") is { } waist)
            ShrineSceneAssets.AddMesh(model, _mesh, _toWaist * waist.WorldMatrix, "Icon belt");
    }

    private static bool IsSkin(VisualModelMesh mesh) => mesh.Texture?.Name == IconMannequinMaterial.TextureName;

    internal static IconArmorBelt? Load(BotwActorResources actor, CancellationToken cancellation)
    {
        if (!actor.ActorName.StartsWith("Armor_", StringComparison.Ordinal) || !actor.ActorName.EndsWith("_Lower", StringComparison.Ordinal)) return null;
        if (File.Exists(OverridePath))
        {
            cancellation.ThrowIfCancellationRequested();
            var custom = new ToolboxBfresDocument(File.ReadAllBytes(OverridePath));
            if (custom.Models.Length != 1) throw new InvalidDataException("The icon belt override must contain one model.");
            var customTextures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
            custom.ReadTextures(customTextures, actor.Warnings);
            if (actor.IsTotk) TexToGoTexture.Load(custom, actor, customTextures, cancellation);
            else foreach (string resource in new[] { "Armor_001", "Link" })
                if (actor.ReadModel(resource, actor.IsSwitch ? ".Tex" : ".Tex1") is { } data)
                    new ToolboxBfresDocument(data).ReadTextures(customTextures, actor.Warnings);
            var appearance = new Dictionary<(string Material, string Sampler), string>();
            IconMannequinMaterial.Apply(custom, customTextures, appearance);
            var skinned = new ToolboxActorModel(custom, (string)custom.Models[0].Name, customTextures, appearance, actor.Warnings);
            var belts = skinned.RestPose.Meshes.Where(m => !IsSkin(m)).ToArray();
            if (!skinned.RestPose.Bones.Any(b => b.Name == "Waist") || belts.Length != 1 ||
                !belts[0].Name.Contains("Belt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The icon belt override must contain one waist-bound belt and optional skin meshes.");
            if (belts[0].Texture == null) throw new InvalidDataException("The icon belt texture is missing.");
            return new(skinned);
        }
        var sources = new PlayerEquipmentSources(actor);
        if (sources.Find("Mannequin_001_Lower") is not { } mannequinSource || sources.Find("Armor_001_Lower") is not { } armorSource) return null;
        var mannequin = mannequinSource.Open(actor.Roots); mannequin.PrepareTotkStrings();
        var armor = armorSource.Open(actor.Roots); armor.PrepareTotkStrings();
        var reference = armor.Models.First();
        var original = new ToolboxActorModel(new ToolboxBfresDocument(armor.ReadModel(reference.Resource)!), reference.Model, new());
        var bones = original.RestPose.Bones;
        Matrix4x4 World(int i)
        {
            var b = bones[i]; var inverse = Matrix4x4.Identity;
            if (b.ParentIndex >= 0) Matrix4x4.Invert(bones[b.ParentIndex].WorldMatrix, out inverse);
            var local = b.WorldMatrix * inverse;
            if (IconRecoveredPose.TryLocal(original.Name, b.Name, local, out var recovered)) local = recovered;
            return local * (b.ParentIndex >= 0 ? World(b.ParentIndex) : Matrix4x4.Identity);
        }
        var waist = bones.FirstOrDefault(b => b.Name == "Waist");
        if (waist == null || !Matrix4x4.Invert(World(waist.Index), out var inverseWaist)) return null;
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        var unit = mannequin.Models.First();
        var document = new ToolboxBfresDocument(mannequin.ReadModel(unit.Resource)!);
        document.ReadTextures(textures, actor.Warnings);
        if (actor.IsTotk) TexToGoTexture.Load(document, mannequin, textures, cancellation);
        else
        {
            foreach (string resource in new[] { unit.Resource, "Armor_001", "Link" })
                if (mannequin.ReadModel(resource, document.IsSwitch ? ".Tex" : ".Tex1") is { } bytes)
                    new ToolboxBfresDocument(bytes).ReadTextures(textures, actor.Warnings);
        }
        var model = new ToolboxActorModel(document, unit.Model, textures, warnings: actor.Warnings);
        var belt = model.RestPose.Meshes.FirstOrDefault(m => m.Name.EndsWith("__Mt_Belt_A", StringComparison.Ordinal));
        if (belt == null || belt.Texture == null) return null;
        // Native mannequin geometry is baked at 1.2x. Remove that display scale,
        // then store the belt in the recovered waist joint's local frame.
        return new(belt, Matrix4x4.CreateScale(1 / 1.2f) * inverseWaist);
    }
}
