using HKCLTool;

namespace PhysicsTool.Core;

internal static class IconArmorProps
{
    internal static string OverridePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhysicsTool", "IconTool", "Armor_Icon.sbfres");
    internal const string Prefix = "Props / ";
    internal static bool IsRaulSkin(string name) => name.StartsWith(Prefix + "Upper_Skin_RaulSkin_", StringComparison.Ordinal);
    internal static bool IsBelt(string name) => name == Prefix + "Icon_Lower_Belt" || name.StartsWith("Icon belt", StringComparison.Ordinal);
    internal static bool IsWaistSkin(string name) => name == Prefix + "Icon_Lower_Skin" || name.StartsWith("Icon waist skin", StringComparison.Ordinal);
    internal static bool IsProp(string name) => name.StartsWith(Prefix, StringComparison.Ordinal) ||
        name.StartsWith("Icon belt", StringComparison.Ordinal) || name.StartsWith("Icon waist skin", StringComparison.Ordinal);
    private static bool IsSkinMaterial(string name) => name is "Icon_Head_Face" or "Icon_Head_Skull" or "Icon_Head_Torso" or "Icon_Lower_Skin" or "Icon_Upper_Neck" ||
        name.StartsWith("Icon_Head_Eye_", StringComparison.Ordinal) || name.StartsWith("Icon_Upper_Fingers_", StringComparison.Ordinal) || name.StartsWith("Upper_Skin_RaulSkin_", StringComparison.Ordinal);
    internal static bool IsModel(ToolboxActorModel model) => model.Name == "Armor_Icon" || model.Name.StartsWith("Armor_Icon_", StringComparison.Ordinal);
    internal static string MeshName(ToolboxActorModel model, string name) => IsModel(model) ? Prefix + name.Split("__")[0] :
        IconMannequinEyes.IsModel(model) ? Prefix + "Icon_Head_" + name.Split("__")[0] : name;

    internal static HashSet<string> HiddenRaulMaterials(BotwActorResources actor)
    {
        if (!actor.IsTotk || !actor.ActorName.StartsWith("Armor_", StringComparison.Ordinal) ||
            !(actor.ActorName.EndsWith("_Head", StringComparison.Ordinal) || actor.ActorName.EndsWith("_Upper", StringComparison.Ordinal))) return [];
        var source = new PlayerEquipmentSources(actor).Find("PlayerRaulHand");
        if (source == null)
        {
            actor.Warnings.Add("PlayerRaulHand is unavailable; native skin visibility could not be resolved.");
            return [];
        }
        var hand = source.Open(actor.Roots).ReadEquipmentVisibility();
        return hand.HiddenBy([actor.ReadEquipmentVisibility()]);
    }

    internal static bool HiddenByDefault(string actor, bool totk, string mesh, IReadOnlySet<string> hiddenRaulMaterials)
    {
        if (IsProp(mesh) && !MatchesArmorSlot(actor, mesh)) return true;
        bool hylianUpper = actor == "Armor_001_Upper";
        if (IsRaulSkin(mesh)) return !totk || hiddenRaulMaterials.Contains("Mt_" + mesh[Prefix.Length..]);
        // TotK's right-hand coverage comes from RaulSkin or the armor itself.
        if (totk && actor.EndsWith("_Upper", StringComparison.Ordinal) && mesh == Prefix + "Icon_Upper_Fingers_R") return true;
        return hylianUpper && !IsProp(mesh) && mesh.EndsWith("__Mt_Upper_Skin", StringComparison.Ordinal);
    }

    internal static bool MatchesArmorSlot(string actor, string mesh)
    {
        if (!mesh.StartsWith(Prefix, StringComparison.Ordinal)) return true;
        string slot = actor.Split('_').Last();
        return IsRaulSkin(mesh) ? slot is "Head" or "Upper" :
            mesh.StartsWith(Prefix + "Icon_" + slot + "_", StringComparison.Ordinal);
    }

    internal static ToolboxActorModel? Load(BotwActorResources actor, CancellationToken cancellation, BotwActorResources? textureSource = null, IconCharacterProfile? character = null)
        => Load(actor, cancellation, out _, textureSource, character);

    internal static ToolboxActorModel? Load(BotwActorResources actor, CancellationToken cancellation, out ToolboxActorAnimation? iconPose,
        BotwActorResources? textureSource = null, IconCharacterProfile? character = null)
    {
        iconPose = null;
        character ??= IconCharacterProfile.Link;
        if (!actor.ActorName.StartsWith("Armor_", StringComparison.Ordinal)) return null;
        if (!File.Exists(character.ModelPath))
        {
            if (character.IsLink) return null;
            throw new FileNotFoundException("Character mannequin is unavailable: " + character.Name, character.ModelPath);
        }
        string slot = actor.ActorName.Split('_').Last();
        if (slot is not ("Head" or "Upper" or "Lower")) return null;
        cancellation.ThrowIfCancellationRequested();
        var document = new ToolboxBfresDocument(File.ReadAllBytes(character.ModelPath));
        dynamic model = document.Models.Single(m => ((string)m.Name).StartsWith("Armor_Icon", StringComparison.Ordinal));
        var materials = ToolboxBfresDocument.Items((object)model.Materials).ToArray();
        var names = materials.Select(m => (string)m.Name).ToArray();
        var keep = names.ToHashSet(StringComparer.Ordinal);
        var usedMaterials = new HashSet<string>(StringComparer.Ordinal);
        foreach (dynamic shape in ToolboxBfresDocument.Items((object)model.Shapes))
        {
            string shapeName = (string)shape.Name;
            bool eye = shapeName.StartsWith("Eye_", StringComparison.Ordinal);
            int index = Array.IndexOf(names, eye ? "Icon_Head_Face" : shapeName.Split("__")[0]);
            if (index < 0) throw new InvalidDataException("Icon prop has no matching material: " + shape.Name);
            shape.MaterialIndex = (ushort)index;
            if (eye) shape.Name = "Icon_Head_" + shapeName.Split("__")[0];
            usedMaterials.Add(names[index]);
        }
        keep.IntersectWith(usedMaterials);
        if (keep.Count == 0)
        {
            actor.Warnings.Add(character.Name + " has no mannequin meshes yet.");
            return null;
        }
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        var appearance = new Dictionary<(string Material, string Sampler), string>();
        document.ReadTextures(textures, actor.Warnings);
        textureSource ??= actor.IsTotk ? null : actor;
        if (!character.IsLink)
        {
            if (!Directory.Exists(character.TextureRoot)) throw new DirectoryNotFoundException("Character textures are unavailable: " + character.TextureRoot);
            byte[]? ReadCharacterTexture(string name)
            {
                string path = Path.Combine(IconCaptureAsset.Root(character.TextureRoot), "TexToGo", name + ".txtg");
                // Mods leave unchanged textures in the base game. Never substitute a
                // differently named texture; resolve the profile's exact reference.
                return File.Exists(path) ? File.ReadAllBytes(path) : actor.IsTotk ? actor.Read("TexToGo/" + name + ".txtg") : null;
            }
            foreach (var material in materials.Where(m => keep.Contains((string)m.Name)))
                foreach (var reference in ToolboxBfresDocument.Items((object)material.TextureRefs))
                {
                    cancellation.ThrowIfCancellationRequested();
                    string name = reference is string text ? text : (string)reference.Name;
                    string mapped = character.TextureName(name);
                    if (ReadCharacterTexture(mapped) is { } bytes) textures[name] = TexToGoTexture.Decode(bytes, mapped);
                }
            textureSource = null;
            foreach (var material in character.MaterialTextures.Where(m => keep.Contains(m.Key)))
            foreach (var binding in material.Value)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!textures.ContainsKey(binding.Value)) textures[binding.Value] = TexToGoTexture.Decode(
                    ReadCharacterTexture(binding.Value) ?? throw new FileNotFoundException("Missing character texture: " + binding.Value), binding.Value);
                appearance[(material.Key, binding.Key)] = binding.Value;
            }
        }
        foreach (string resource in new[] { "Link", "Armor_001" })
            if (textureSource?.ReadModel(resource, textureSource.IsSwitch ? ".Tex" : ".Tex1") is { } bytes)
                new ToolboxBfresDocument(bytes).ReadTextures(textures, actor.Warnings);
        textures[IconMannequinMaterial.TextureName] = IconMannequinMaterial.Texture();
        foreach (string material in keep.Where(IsSkinMaterial)) appearance[(material, "_a0")] = IconMannequinMaterial.TextureName;
        var hidden = names.Where(n => !keep.Contains(n)).ToHashSet(StringComparer.Ordinal);
        var props = new ToolboxActorModel(document, (string)model.Name, textures, appearance, actor.Warnings, hidden);
        var materialSources = new Dictionary<string, IconCharacterProfile.MaterialSource>(character.MaterialSources);
        if (character.IsLink)
        {
            string player = actor.IsTotk ? "Player" : "GameROMPlayer";
            materialSources.TryAdd("Icon_Head_Face", new(player, "Mt_Face"));
            materialSources.TryAdd("Icon_Head_Skull", new(player, "Mt_Head"));
        }
        foreach (var group in materialSources.Where(p => keep.Contains(p.Key)).GroupBy(p => p.Value.Actor))
        {
            cancellation.ThrowIfCancellationRequested();
            BotwActorResources source;
            if (character.IsLink)
                source = (new PlayerEquipmentSources(actor).Find(group.Key)
                    ?? throw new FileNotFoundException("Native mannequin material actor is unavailable: " + group.Key)).Open(actor.Roots);
            else
            {
                string root = IconCaptureAsset.Root(character.TextureRoot);
                source = new BotwActorResources(Path.Combine(root, "Pack/Actor", group.Key + ".pack.zs"), new[] { root }.Concat(actor.Roots));
            }
            source.PrepareTotkStrings();
            var wanted = group.Select(p => p.Value.Material).ToHashSet(StringComparer.Ordinal);
            var donors = new Dictionary<string, VisualModelMesh>(StringComparer.Ordinal);
            foreach(var reference in source.Models)
            {
                cancellation.ThrowIfCancellationRequested();
                var nativeDocument=new ToolboxBfresDocument(source.ReadModel(reference.Resource) ?? throw new FileNotFoundException(reference.Resource));
                var nativeTextures=new Dictionary<string,VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
                nativeDocument.ReadTextures(nativeTextures,actor.Warnings);
                if (source.IsTotk) TexToGoTexture.Load(nativeDocument,source,nativeTextures,cancellation);
                else if (source.ReadModel(reference.Resource, nativeDocument.IsSwitch ? ".Tex" : ".Tex1") is {} textureBytes)
                    new ToolboxBfresDocument(textureBytes).ReadTextures(nativeTextures, actor.Warnings);
                var sourceModel=new ToolboxActorModel(nativeDocument,reference.Model,nativeTextures,warnings:actor.Warnings);
                foreach (string name in wanted)
                    if (sourceModel.RestPose.Meshes.FirstOrDefault(m => m.Name.EndsWith("__" + name, StringComparison.Ordinal)) is {} donor)
                        donors.TryAdd(name, donor);
                if (donors.Count == wanted.Count) break;
            }
            foreach (var binding in group)
            {
                if (!donors.TryGetValue(binding.Value.Material, out var native))
                    throw new InvalidDataException("Missing character material: " + binding.Value.Material);
                var appearanceMesh = new VisualModelMesh { Texture = native.Texture, NormalTexture = native.NormalTexture,
                    SpecularTexture = native.SpecularTexture, AmbientOcclusionTexture = native.AmbientOcclusionTexture,
                    AuxiliaryTexture = native.AuxiliaryTexture, EyeGlintTexture = native.EyeGlintTexture, Material = native.Material.Clone() };
                appearanceMesh.Material.Totk = native.Material.Totk?.Clone();
                appearanceMesh.Material.Botw = native.Material.Botw?.Clone();
                if (IsSkinMaterial(binding.Key))
                {
                    var charcoal = textures[IconMannequinMaterial.TextureName];
                    var slots = appearanceMesh.Material.Totk?.Textures ?? appearanceMesh.Material.Botw?.Textures;
                    if (slots != null)
                        for (int i = 0; i < slots.Length; i++)
                            if (ReferenceEquals(slots[i], native.Texture)) slots[i] = charcoal;
                    appearanceMesh.Texture = charcoal;
                }
                props.OverrideMaterial(binding.Key, appearanceMesh);
            }
        }
        if (props.RestPose.Meshes.Any(m => m.Texture == null)) throw new InvalidDataException("An icon prop texture could not be loaded.");
        if (!character.IsLink)
        {
            var clip = document.SkeletalAnimations.FirstOrDefault(a => string.Equals((string)a.Name, "Icon_Pose", StringComparison.OrdinalIgnoreCase));
            if (clip != null) iconPose = new ToolboxActorAnimation((object)clip, character.ModelPath);
        }
        return props;
    }
}
