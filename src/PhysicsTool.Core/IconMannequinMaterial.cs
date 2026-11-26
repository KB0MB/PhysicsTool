using HKCLTool;

namespace PhysicsTool.Core;

internal static class IconMannequinMaterial
{
    internal const string TextureName = "IconMannequinCharcoal_Alb";
    // Albedo, not a final display color: the material's lighting and occlusion remain active.
    internal static VisualModelTexture Texture() => new() { Name = TextureName, Width = 1, Height = 1, IsSrgb = true, Pixels = [40, 39, 37, 255] };

    internal static void ConfigureShading(VisualModelData model)
    {
        foreach (var mesh in model.Meshes.Where(m => m.Texture?.Name == TextureName))
        {
            bool eyes = IconMannequinEyes.IsMesh(mesh.Name);
            if (eyes && model.Meshes.FirstOrDefault(m => m.Name == IconArmorProps.Prefix + "Icon_Head_Face") is { } face)
                mesh.Material = IconMannequinEyes.Surface(face.Material, mesh.Material.Totk?.VertexColors ?? mesh.Material.Botw?.VertexColors);
            mesh.Material.UseSpm = false;
            mesh.Material.UseAmbientOcclusion = false;
            mesh.AmbientOcclusionTexture = null;
            mesh.SpecularTexture = null;
            bool geometryNormal = mesh.Name == IconArmorProps.Prefix + "Icon_Upper_Neck" || eyes;
            // The new neck cap and blank eyes have no corresponding skin-normal texture region.
            if (geometryNormal) { mesh.Material.UseNormal = false; mesh.NormalTexture = null; }
            if (eyes) { mesh.Material.UseEyeGlint = false; mesh.EyeGlintTexture = null; }
            if (mesh.Material.Botw is { } botw)
            {
                // Match original armor skin and added props: neither should retain Link's damage tint.
                botw.Options["uking_albedo_color"] = 0;
                for (int i = 0; i < 4; i++) botw.Options["uking_enable_calc_color" + i] = 0;
                if (geometryNormal) botw.Options["uking_enable_normalmap"] = 0;
                botw.Options["uking_specmask_scaler"] = 400; // Native constant-zero source.
                botw.Options["uking_specular_channel"] = 0;
                botw.Options["uking_specular_hair"] = 400;
                botw.Options["uking_specular_hair_channel"] = 0;
                botw.Options["uking_enable_ao"] = 0;
            }
            if (mesh.Material.Totk is { } totk)
            {
                int albedo = Array.FindIndex(totk.Textures, t => t?.Name == TextureName);
                if (albedo >= 0) totk.Options["o_albedo_color"] = albedo;
                totk.Options["o_specmask_scaler"] = 400;
                totk.Options["o_specular_channel"] = 0;
                totk.Options["o_specular_hair"] = 400;
                totk.Options["o_specular_hair_channel"] = 0;
                totk.Options["o_enable_ao"] = 0;
                if (geometryNormal) totk.Options["o_enable_normalmap"] = 0;
                totk.Integers["totkSpecularSource"] = -1;
                totk.Integers["totkHairSpecularEnabled"] = 0;
                totk.Integers["totkAoSource"] = -1;
                totk.Integers["totkAoOutput"] = 300;
                totk.Integers["totkAoChannel"] = 10;
            }
        }
    }

    internal static void Apply(ToolboxBfresDocument document, Dictionary<string, VisualModelTexture> textures,
        Dictionary<(string Material, string Sampler), string> appearance)
    {
        textures[TextureName] = Texture();
        foreach (dynamic model in document.Models)
        foreach (dynamic material in ToolboxBfresDocument.Items((object)model.Materials))
        {
            string name = material.Name;
            if (name.EndsWith("_Skin", StringComparison.Ordinal) || name.Contains("_Skin_", StringComparison.Ordinal))
            {
                string sampler = "_a0";
                if ((object)material is Syroot.NintenTools.NSW.Bfres.Material nx)
                {
                    var assignment = nx.ShaderAssign;
                    int slot = Enumerable.Range(0, assignment.SamplerAssigns.Count).FirstOrDefault(i => assignment.SamplerAssignDict.GetKey(i) == "_a0", -1);
                    if (slot >= 0) sampler = assignment.SamplerAssigns[slot];
                }
                else if ((object)material is Syroot.NintenTools.Bfres.Material wii && wii.ShaderAssign.SamplerAssigns.ContainsKey("_a0"))
                    sampler = wii.ShaderAssign.SamplerAssigns["_a0"].String;
                appearance[(name, sampler)] = TextureName;
            }
        }
    }
}
