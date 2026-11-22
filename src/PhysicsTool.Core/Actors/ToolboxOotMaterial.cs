using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using Res = Syroot.NintenTools.NSW.Bfres;

namespace HKCLTool;

// Material graph selection and UV routing mirror Switch Toolbox's BFRES_Oot renderer.
internal static class ToolboxOotMaterial
{
    internal static bool IsOot(Res.Material material) => material.ShaderParams?.Any(p => p.Name == "ns_tex_mtx0") == true;

    internal static void Apply(object material, VisualModelMesh mesh, Dictionary<string, VisualModelTexture> textures)
    {
        if (material is not Res.Material m || !IsOot(m) || m.ShaderAssign is not { } shader) return;
        var options = Enumerable.Range(0, shader.ShaderOptions.Count).ToDictionary(i => shader.ShaderOptionDict.GetKey(i), i => shader.ShaderOptions[i].Trim().Trim('(', ')'));
        int O(string key, int fallback = 0) => options.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        int B(string key) => options.TryGetValue(key, out var text) && bool.TryParse(text, out var b) ? b ? 1 : 0 : O(key);
        string R(string key, string fallback) => m.RenderInfos.FirstOrDefault(r => r.Name == key) is { Type: Res.RenderInfoType.String } r ? r.GetValueStrings().FirstOrDefault() ?? fallback : fallback;
        var expression = options.GetValueOrDefault("ns_expression_evaluate_graph");
        int graph = options.GetValueOrDefault("ns_overwrite_surface_graph") == "2937883399" ? 1 : expression switch
        { "1256081718" => 2, "2979515529" => 3, "2331044082" => 4, "2221926229" => 5, _ => 0 };
        var result = new TotkPreviewMaterial { IsOot = true, Additive = graph == 2,
            IsTranslucent = graph is 2 or 4 || R("gsys_render_state_mode", "opaque") is "translucent" or "custom",
            DepthWrite = R("gsys_depth_test_write", "true") is "true" or "1", DisplayFace = R("gsys_render_state_display_face", "both") };
        mesh.Material.Totk = result;
        mesh.Material.UseNormal = B("ns_enable_normal_map") != 0 || graph is 1 or 2;
        mesh.Material.UseSpm = true;
        string[] roles = graph == 1 ? ["_a0", "_n0", "_a1", "_rgh0", "_hgt0", "_gn3", "_n1", "_gn1"]
            : ["_a0", "_n0", "_ao0", "_rgh0", "_th0", "_gn1", "_gn0", "_n1"];
        if (graph == 2) roles[0] = "_e0";
        if (graph == 4) roles[0] = "_gn3";
        string[] inputs = ["_a0", "_n0", "_r0", "_m0", "_ao0", "_s0", "_a1", "_n1"];
        string Assigned(string semantic)
        {
            int i = Enumerable.Range(0, shader.SamplerAssigns.Count).FirstOrDefault(i => shader.SamplerAssignDict.GetKey(i) == semantic, -1);
            return i < 0 ? "" : shader.SamplerAssigns[i];
        }
        for (int uv = 0; uv < 4; uv++)
        {
            int i = Enumerable.Range(0, shader.AttribAssigns.Count).FirstOrDefault(i => shader.AttribAssignDict.GetKey(i) == $"_u{uv}", -1);
            if (i >= 0) result.UvSets[uv] = shader.AttribAssigns[i];
        }
        for (int i = 0; i < 8; i++)
        {
            int physical = Enumerable.Range(0, m.SamplerDict.Count).FirstOrDefault(n => m.SamplerDict.GetKey(n) == roles[i], -1);
            if (physical >= 0 && physical < m.TextureRefs.Count) result.Textures[i] = textures.GetValueOrDefault(m.TextureRefs[physical]);
            result.Integers[$"ootPresent[{i}]"] = result.Textures[i] == null ? 0 : 1;
            result.Integers[$"ootSrgb[{i}]"] = result.Textures[i]?.IsSrgb == true ? 1 : 0;
            result.Vectors[$"ootChannels[{i}]"] = new(0, 1, 2, 3);
            int slot = Enumerable.Range(0, 8).FirstOrDefault(n => O($"ns_texture{n}_sampler", -1) is >= 0 and < 8 && Assigned(inputs[O($"ns_texture{n}_sampler")]) == roles[i], -1);
            if (graph == 3 && i == 5) slot = 5;
            int coord = O($"ns_texture{slot}_texcoord");
            int mapping = O($"ns_texcoord{coord}_mapping");
            result.Integers[$"ootUv[{i}]"] = mapping is >= 0 and < 4 ? mapping : 0;
            int srtIndex = O($"ns_texcoord{coord}_srt", -1);
            var data = Parameter(m, $"ns_tex_mtx{srtIndex}");
            var srt = new Vector4(1, 1, 0, 0); float rotation = 0;
            if (srtIndex >= 0 && data.Length >= 24)
            {
                srt = new(F(data, 4), F(data, 8), F(data, 16), F(data, 20)); rotation = F(data, 12);
                if (BinaryPrimitives.ReadUInt32LittleEndian(data) == (uint)Res.TexSrtMode.Mode3dsMax)
                { float c = MathF.Cos(rotation), s = MathF.Sin(rotation); srt.Z += .5f - .5f * (c * srt.X + s * srt.Y); srt.W += .5f - .5f * (-s * srt.X + c * srt.Y); }
            }
            result.Vectors[$"ootSrt[{i}]"] = srt; result.Scalars[$"ootRotation[{i}]"] = rotation;
            var scalar = Parameter(m, $"ns_param_scalar{i}"); result.Scalars[$"ootScalar[{i}]"] = scalar.Length >= 4 ? F(scalar, 0) : 1;
        }
        int color = O("ns_albedo_select");
        result.Integers["ootGraph"] = graph; result.Integers["ootAlbedoSelect"] = color;
        result.Vectors["ootColor"] = color is >= 110 and < 116 ? Color(m, $"ns_param_color{color - 110}", Vector4.One) : Vector4.One;
        result.Integers["ootAlpha"] = B("ns_enable_alpha"); result.Integers["ootNormal"] = B("ns_enable_normal_map"); result.Integers["ootAo"] = B("ns_enable_ao");
        result.Integers["ootOpaque"] = result.IsTranslucent ? 0 : 1;
        result.Integers["ootAlphaTest"] = R("gsys_alpha_test_enable", "false") is "true" or "1" ? 1 : 0;
        result.Integers["ootAlphaFunc"] = R("gsys_alpha_test_func", "gequal") switch { "greater" => 1, "equal" => 2, "less" => 3, "lequal" => 4, "notequal" => 5, "never" => 6, "always" => 7, _ => 0 };
        var alpha = Parameter(m, "gsys_alpha_test_ref_value"); result.Scalars["ootAlphaRef"] = alpha.Length >= 4 ? F(alpha, 0) : .5f;
        for (int i = 0; i < 4; i++) result.Vectors[$"ootParamColor[{i}]"] = Color(m, $"ns_param_color{i}", Vector4.One);
        result.Vectors["ootVector"] = Color(m, "ns_param_vector0", Vector4.Zero);
    }

    private static ReadOnlySpan<byte> Parameter(Res.Material m, string name)
    {
        var p = m.ShaderParams.FirstOrDefault(p => p.Name == name);
        return p == null || m.ShaderParamData == null || (long)p.DataOffset + p.DataSize > m.ShaderParamData.Length ? [] : m.ShaderParamData.AsSpan(p.DataOffset, (int)p.DataSize);
    }
    private static float F(ReadOnlySpan<byte> bytes, int offset)
    { float value = BinaryPrimitives.ReadSingleLittleEndian(bytes[offset..]); return float.IsFinite(value) ? value : 0; }
    private static Vector4 Color(Res.Material m, string name, Vector4 fallback)
    { var bytes = Parameter(m, name); return bytes.Length >= 16 ? new(F(bytes, 0), F(bytes, 4), F(bytes, 8), F(bytes, 12)) : fallback; }
}
