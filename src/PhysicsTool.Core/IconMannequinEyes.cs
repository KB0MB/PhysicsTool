using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Core;

internal static class IconMannequinEyes
{
    internal static bool IsModel(ToolboxActorModel model) => model.Name == "Link" &&
        model.RestPose.Meshes.Count == 2 && model.RestPose.Meshes.All(m => m.Name.StartsWith("Eye_", StringComparison.Ordinal));
    internal static bool IsMesh(string name) => name.StartsWith(IconArmorProps.Prefix + "Icon_Head_Eye_", StringComparison.Ordinal);
    internal static ToolboxActorModel Load(List<string> warnings)
    {
        var document = new ToolboxBfresDocument(IconBuiltinProps.Model("LinkEyes.bfres"));
        dynamic link = document.Models.First(m => (string)m.Name == "Link");
        var keep = new HashSet<string> { "Mt_Eyeball_L", "Mt_Eyeball_R" };
        var hidden = ToolboxBfresDocument.Items((object)link.Materials).Select(m => (string)m.Name).Where(n => !keep.Contains(n)).ToHashSet();
        var textures = new Dictionary<string, VisualModelTexture>(StringComparer.OrdinalIgnoreCase);
        IconBuiltinProps.LoadTextures(textures);
        textures[IconMannequinMaterial.TextureName] = IconMannequinMaterial.Texture();
        var appearance = keep.ToDictionary(n => (n, "_a0"), _ => IconMannequinMaterial.TextureName);
        return new(document, "Link", textures, appearance, warnings, hidden);
    }

    internal static VisualModelMaterialSettings Surface(VisualModelMaterialSettings source, Vector4[]? colors = null)
    {
        var material = source.Clone();
        if (source.Totk is {} totk)
        {
            material.Totk = totk.Clone();
            material.Totk.VertexColors = colors ?? [];
        }
        if (source.Botw is { } original)
        {
            material.Botw = original.Clone();
            material.Botw.VertexColors = colors ?? [];
        }
        return material;
    }
}
