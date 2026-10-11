using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using Res = Syroot.NintenTools.NSW.Bfres;

namespace HKCLTool;

public sealed class TotkPreviewMaterial
{
    public VisualModelTexture?[] Textures { get; } = new VisualModelTexture?[8];
    public string[] UvSets { get; } = ["_u0", "_u1", "_u2", "_u3"];
    public Vector4[] VertexColors { get; set; } = [];
    public Dictionary<string, int> Integers { get; } = new();
    public Dictionary<string, float> Scalars { get; } = new();
    public Dictionary<string, Vector4> Vectors { get; } = new();
}

internal static class ToolboxTotkMaterial
{
    private static ReadOnlySpan<byte> Parameter(Res.Material material, string name)
    {
        var p = material.ShaderParams?.FirstOrDefault(p => p.Name == name);
        if (p == null || material.ShaderParamData == null || (long)p.DataOffset + p.DataSize > material.ShaderParamData.Length) return [];
        return material.ShaderParamData.AsSpan(p.DataOffset, (int)p.DataSize);
    }
    private static float Float(Res.Material m, string name, float fallback = 1)
    {
        var bytes = Parameter(m, name);
        if (bytes.Length < 4) return fallback;
        var value = BinaryPrimitives.ReadSingleLittleEndian(bytes);
        return float.IsFinite(value) ? value : fallback;
    }
    internal static int SurfaceIndex(object material)
    {
        if (material is not Res.Material m) return 0;
        var p = m.ShaderParams?.FirstOrDefault(p => p.Name == "p_texture_array_index0");
        var bytes = Parameter(m, "p_texture_array_index0");
        if (bytes.Length < 4) return 0;
        return p!.Type.ToString().StartsWith("Float", StringComparison.Ordinal)
            ? (int)Math.Clamp(MathF.Round(Float(m, p.Name, 0)), 0, 2047)
            : Math.Clamp(BinaryPrimitives.ReadInt32LittleEndian(bytes), 0, 2047);
    }

    internal static void Apply(object material, VisualModelMesh mesh, Dictionary<string, VisualModelTexture> textures)
    {
        if (material is not Res.Material m || m.ShaderAssign is not { } shader) return;
        var options = Enumerable.Range(0, shader.ShaderOptions?.Count ?? 0)
            .ToDictionary(i => shader.ShaderOptionDict.GetKey(i), i => shader.ShaderOptions![i]);
        if (!options.Keys.Any(k => k is "o_albedo_color" or "o_emission_color" or "o_material_behave" || k.StartsWith("o_texcoord") || k.StartsWith("o_texture"))) return;
        var result = new TotkPreviewMaterial(); mesh.Material.Totk = result;
        int O(string key, int fallback) => options.TryGetValue(key, out var text) && int.TryParse(text.Trim().Trim('(', ')'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
        bool B(string key, bool fallback) => options.TryGetValue(key, out var text) && bool.TryParse(text.Trim().Trim('(', ')'), out bool b) ? b : O(key, fallback ? 1 : 0) != 0;
        void I(string key, int value) => result.Integers["totk" + key] = value;
        string R(string name, string fallback) => m.RenderInfos?.FirstOrDefault(r => r.Name == name) is { } r
            ? r.Type == Res.RenderInfoType.String ? r.GetValueStrings().FirstOrDefault() ?? fallback
            : r.Type == Res.RenderInfoType.Int32 ? r.GetValueInt32s().FirstOrDefault().ToString(CultureInfo.InvariantCulture) : fallback : fallback;
        var semantic = Enumerable.Range(0, shader.SamplerAssigns?.Count ?? 0).Select(shader.SamplerAssignDict.GetKey).ToArray();
        var assigned = shader.SamplerAssigns?.ToArray() ?? [];
        int slots = Math.Min(8, assigned.Length > 0 ? assigned.Length : m.TextureRefs.Count);
        string Assigned(int slot) => slot >= 0 && slot < assigned.Length ? assigned[slot] : "";
        int Find(params string[] names) => Enumerable.Range(0, slots).FirstOrDefault(i => names.Contains(i < semantic.Length ? semantic[i] : m.SamplerDict.GetKey(i), StringComparer.OrdinalIgnoreCase) && result.Textures[i] != null, -1);
        int Resolve(string key, int fallback) { int n = O(key, fallback); return n is >= 0 and <= 7 ? n : n == 300 ? -1 : fallback; }
        for (int uv = 0; uv < 4; uv++)
        {
            int i = Enumerable.Range(0, shader.AttribAssigns?.Count ?? 0).FirstOrDefault(i => shader.AttribAssignDict.GetKey(i) == $"_u{uv}", -1);
            if (i >= 0) result.UvSets[uv] = shader.AttribAssigns![i];
        }
        for (int slot = 0; slot < 8; slot++)
        {
            int physical = assigned.Length > 0 ? Enumerable.Range(0, m.SamplerDict.Count).FirstOrDefault(i => m.SamplerDict.GetKey(i) == Assigned(slot), -1) : slot;
            if (slot < slots && physical >= 0 && physical < m.TextureRefs.Count)
                result.Textures[slot] = textures.GetValueOrDefault(TexToGoTexture.Key(m.TextureRefs[physical], SurfaceIndex(m)));
            I($"TextureEnabled{slot}", result.Textures[slot] == null ? 0 : 1);
            // Match Toolbox's RGB preview decode, including UNORM color assets.
            // Only color-output sampling applies this; normal/scalar inputs stay raw.
            I($"TextureSrgb{slot}", result.Textures[slot] is { IsSingleChannel: false } ? 1 : 0);
            I($"TextureLuminance{slot}", result.Textures[slot]?.UseRedAsLuminance == true ? 1 : 0);
            int texcoord = O($"o_texture{slot}_texcoord", 0);
            I($"TextureMapping{slot}", O($"o_texcoord{texcoord}_mapping", 0));
            var srt = new Vector4(1, 1, 0, 0); float rotation = 0;
            int srtIndex = O($"o_texcoord{texcoord}_srt", -1);
            var data = Parameter(m, $"p_tex_srt{srtIndex}");
            if (data.IsEmpty) data = Parameter(m, $"tex_srt{srtIndex}");
            if (data.IsEmpty) data = Parameter(m, $"texsrt{srtIndex}");
            if (data.IsEmpty && srtIndex == 0) data = Parameter(m, "p_tex_srt");
            if (data.IsEmpty && srtIndex == 0) data = Parameter(m, "tex_srt");
            if (data.IsEmpty && srtIndex == 0) data = Parameter(m, "texsrt");
            if (srtIndex >= 0 && data.Length >= 24)
            {
                srt = new(BinaryPrimitives.ReadSingleLittleEndian(data[4..]), BinaryPrimitives.ReadSingleLittleEndian(data[8..]),
                    BinaryPrimitives.ReadSingleLittleEndian(data[16..]), BinaryPrimitives.ReadSingleLittleEndian(data[20..]));
                rotation = BinaryPrimitives.ReadSingleLittleEndian(data[12..]);
                if (BinaryPrimitives.ReadUInt32LittleEndian(data) == (uint)Res.TexSrtMode.Mode3dsMax)
                {
                    float c = MathF.Cos(rotation), s = MathF.Sin(rotation);
                    srt.Z += .5f - .5f * (c * srt.X + s * srt.Y);
                    srt.W += .5f - .5f * (-s * srt.X + c * srt.Y);
                }
            }
            result.Vectors[$"totkTextureSrt{slot}"] = srt;
            result.Vectors[$"totkTextureChannels{slot}"] = new(0, 1, 2, 3); // Decoded channels already remapped.
            result.Scalars[$"totkTextureRotation{slot}"] = rotation;
        }
        I("TextureCount", slots);
        int albedo = Find("_a0", "Albedo0", "_albedo0"), normal = Find("_n0", "Normal0", "_normal0"), spec = Find("_s0", "_sp0", "Specular0"), ao = Find("_a1", "_ao0", "_b0", "Bake0");
        int behavior = O("o_material_behave", 0); I("MaterialBehavior", behavior);
        I("AlbedoSource", Resolve("o_albedo_color", albedo)); I("AlbedoOutput", O("o_albedo_color", albedo));
        int layer = Find("_s1"); I("LayeredAlbedoSource", behavior == 101 && Assigned(layer) == "_gn2" ? layer : -1);
        I("EyeShadowSource", behavior == 104 ? Find("_e0") : -1); I("EyeGlintEnabled", 1);
        int nrm = B("o_enable_normalmap", normal >= 0) ? Resolve("o_normal_color", normal) : -1;
        I("NormalSource", nrm);
        I("SpecularSource", Resolve("o_specmask_scaler", spec)); I("SpecularOutput", O("o_specmask_scaler", spec));
        I("MetalSource", Resolve("o_metal_color", spec)); I("MetalOutput", O("o_metal_color", spec));
        int aoSource = B("o_enable_ao", ao >= 0) ? Resolve("o_ao_color", ao) : -1;
        I("AoSource", aoSource); I("AoOutput", aoSource < 0 ? 300 : O("o_ao_color", aoSource));
        I("TransparencySource", Resolve("o_transparency_color", albedo)); I("TransparencyOutput", O("o_transparency_color", albedo));
        I("MetalChannel", O("o_metal_channel", 20)); I("SpecularChannel", O("o_specular_channel", 10));
        I("AoChannel", O("o_ao_channel", 10)); I("TransparencyChannel", O("o_transparency_channel", 40));
        int hair = O("o_specular_hair", 300); I("HairSpecularOutput", hair);
        I("HairSpecularEnabled", behavior == 100 && hair != 300 ? 1 : 0); I("HairSpecularChannel", O("o_specular_hair_channel", 10));
        string alphaTest = R("gsys_alpha_test_enable", B("gsys_alpha_test_enable", false) ? "true" : "false");
        I("AlphaTestEnabled", alphaTest is "true" or "1" ? 1 : 0);
        I("AlphaTestFunc", R("gsys_alpha_test_func", "") switch { "gequal" => 0, "greater" => 1, "equal" => 2, "less" => 3, "lequal" => 4, "notequal" => 5, "never" => 6, "always" => 7, _ => O("gsys_alpha_test_func", 0) });
        I("ForceOpaqueOutput", R("gsys_render_state_mode", "opaque") is "opaque" or "mask" ? 1 : 0);
        result.Scalars["gsys_alpha_test_ref_value"] = Float(m, "gsys_alpha_test_ref_value", .1f);
        int emission = B("o_enable_emission", false) ? Resolve("o_emission_color", Find("_e0", "Emission0", "_em0")) : -1;
        int expression = Find("_n1");
        if (O("o_emission_color", 0) >= 200 && emission >= 0 && expression >= 0 && Assigned(emission) == Assigned(expression) && O($"o_texture{expression}_texcoord", -1) >= 0) emission = expression;
        bool mask = Assigned(emission) == "_e0";
        I("EmissionSource", emission); I("EmissionEnabled", emission >= 0 ? 1 : 0); I("EmissionIsMask", mask ? 1 : 0);
        int secondary = Enumerable.Range(0, slots).FirstOrDefault(i => Assigned(i) == "_e1" && result.Textures[i] != null, -1);
        I("SecondaryEmissionSource", mask && O("o_emission_color", -1) is >= 200 and <= 207 &&
            !Parameter(m, "p_const_color1").IsEmpty && !assigned.Contains("_e2") && secondary != emission ? secondary : -1);
        result.Scalars["totkEmissionStrength"] = mask ? Float(m, "p_const_value1") : 0;
        for (int i = 0; i < 8; i++) result.Scalars[$"p_const_value{i}"] = Float(m, $"p_const_value{i}");
        for (int i = 0; i < 6; i++)
        {
            var bytes = Parameter(m, $"p_const_color{i}");
            result.Vectors[$"p_const_color{i}"] = bytes.Length >= 16 ? new(BinaryPrimitives.ReadSingleLittleEndian(bytes), BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[12..])) : Vector4.One;
        }
        mesh.Texture = albedo >= 0 ? result.Textures[albedo] : null;
        mesh.NormalTexture = nrm >= 0 ? result.Textures[nrm] : null;
        mesh.Material.UseAlbedo = mesh.Texture != null;
        mesh.Material.UseNormal = mesh.NormalTexture != null;
        mesh.Material.UseSpm = spec >= 0;
        mesh.Material.NormalUvSet = result.UvSets[Math.Clamp(nrm < 0 ? 0 : result.Integers[$"totkTextureMapping{nrm}"], 0, 3)];
    }
}
