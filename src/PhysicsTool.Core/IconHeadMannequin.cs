using HKCLTool;

namespace PhysicsTool.Core;

internal static class IconHeadMannequin
{
    internal static BotwActorResources? ResolveSource(BotwActorResources actor, string? botwRoot, CancellationToken cancellation)
    {
        if (!actor.IsTotk) return actor;
        if (string.IsNullOrWhiteSpace(botwRoot)) return null;
        foreach (var root in BotwActorCatalog.FindResourceRoots([botwRoot], actor.Warnings, cancellation))
        {
            cancellation.ThrowIfCancellationRequested();
            var loose = Path.Combine(root, "Actor", "Pack", "GameROMPlayer.sbactorpack");
            if (File.Exists(loose)) return new BotwActorResources(loose, [root]);
            var title = Path.Combine(root, "Pack", "TitleBG.pack");
            if (File.Exists(title)) return new BotwActorResources(title, [root], "Actor/Pack/GameROMPlayer.sbactorpack");
        }
        return null;
    }
    internal static ToolboxActorModel? Load(BotwActorResources actor, string? botwRoot, CancellationToken cancellation)
    {
        if (!actor.ActorName.StartsWith("Armor_", StringComparison.Ordinal) || !actor.ActorName.EndsWith("_Head", StringComparison.Ordinal)) return null;
        var source = ResolveSource(actor, botwRoot, cancellation);
        if (source == null) return null;
        var bytes = source.ReadModel("Link");
        if (bytes == null) return null;
        var document = new ToolboxBfresDocument(bytes);
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        document.ReadTextures(textures, actor.Warnings);
        if (source.ReadModel("Link", document.IsSwitch ? ".Tex" : ".Tex1") is { } textureData)
            new ToolboxBfresDocument(textureData).ReadTextures(textures, actor.Warnings);
        var keep = new HashSet<string>(StringComparer.Ordinal) { "Mt_Face", "Mt_Head", "Mt_Upper_Skin" };
        dynamic link = document.Models.First(m => (string)m.Name == "Link");
        var hidden = ToolboxBfresDocument.Items((object)link.Materials).Select(m => (string)m.Name).Where(n => !keep.Contains(n)).ToHashSet(StringComparer.Ordinal);
        textures[IconMannequinMaterial.TextureName] = IconMannequinMaterial.Texture();
        var overrides = keep.ToDictionary(n => (n, "_a0"), _ => IconMannequinMaterial.TextureName);
        return new ToolboxActorModel(document, "Link", textures, overrides, actor.Warnings, hidden);
    }
}
