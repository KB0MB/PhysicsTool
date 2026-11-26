using System.Globalization;
using System.Text.RegularExpressions;
using HKCLTool;

namespace PhysicsTool.Desktop.Rendering;

internal static class TotkShaderSource
{
    internal sealed record Field(string Name, string Type, float[] Default);
    internal static readonly IReadOnlyList<Field> Fields = ReadFields();
    internal static bool Supports(TotkPreviewMaterial material)
    {
        if (!material.Native || material.IsOot) return false;
        bool usesGraph = material.Options.Any(p => p.Value is >= 200 and < 300 &&
            (p.Key.EndsWith("_color", StringComparison.Ordinal) || p.Key is "o_specmask_scaler" or "o_specular_hair"));
        long pre = material.Options.GetValueOrDefault("o_expression_pre_normal");
        long post = material.Options.GetValueOrDefault("o_expression_post_normal");
        if (usesGraph && (pre != 0 ? pre is not (2197096204 or 2583175971 or 2679514330 or 763730240 or 2207006048)
            : post != 3965060848)) return false;
        for (int i = 0; i < 6; i++)
            if (material.Options.GetValueOrDefault($"o_enable_proctexture{i}") != 0 && material.Options.GetValueOrDefault($"o_proctexture{i}_type") != 100) return false;
        return true;
    }
    internal static string Read(string name)
    {
        var assembly = typeof(TotkShaderSource).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(".TotkNative." + name, StringComparison.Ordinal)))!;
        return new StreamReader(stream).ReadToEnd();
    }
    private static IReadOnlyList<Field> ReadFields()
    {
        var fields = new List<Field>();
        foreach (Match block in Regex.Matches(Read("Uniforms.glsl"), @"uniform\s+\w+[^\{]*\{(?<body>.*?)\}\s*(?<instance>\w+)\s*;", RegexOptions.Singleline))
        foreach (Match field in Regex.Matches(block.Groups["body"].Value,
            @"\b(?<type>float|int|ivec[234]|vec[234]|mat[234](?:x[234])?)\s+(?<name>\w+)\s*;(?<comment>[^\r\n]*)"))
        {
            var defaults = Regex.Match(field.Groups["comment"].Value, "default_value=\"([^\"]+)\"");
            var values = defaults.Success ? defaults.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray() : [];
            fields.Add(new(block.Groups["instance"].Value + "." + field.Groups["name"].Value, field.Groups["type"].Value, values));
        }
        return fields;
    }
    internal static string Defines(TotkPreviewMaterial material, bool emission)
    {
        var options = new SortedDictionary<string, long>(material.Options, StringComparer.Ordinal)
        {
            ["BLENDER_RENDER"] = 0, ["gsys_weight"] = 0, ["gsys_assign_type"] = 1
        };
        if (!emission) options["o_enable_emission"] = 0;
        return string.Join("\n", options.Where(p => Regex.IsMatch(p.Key, @"^[A-Za-z_]\w*$"))
            .Select(p => $"#define {p.Key} {p.Value.ToString(CultureInfo.InvariantCulture)}")) + "\n";
    }
    internal static string Build(string name, string defines, bool es)
    {
        string Expand(string file)
        {
            if (file == "Storage.glsl") return "uniform float ptSceneShading[108];\n";
            string text = Read(file);
            if (file == "Material.vert")
            {
                // Geometry, normals and tangents have already been skinned by the scene.
                text = Regex.Replace(text, @"vec3 BonePos.*?(?=// ---------------- position modify)", "", RegexOptions.Singleline);
                text = text.Replace("SKIN(BonePos, rest)", "rest").Replace("SKIN(BoneDir, aNormal.xyz)", "aNormal.xyz")
                    .Replace("SKIN(BoneDir, aTangent.xyz)", "aTangent.xyz");
            }
            if (file == "Uniforms.glsl")
                text = Regex.Replace(text, @"layout\s*\([^)]*\)\s*uniform\s+_(?:Mtx|Instancing|UltraHandVoxelInfoUBO)\b[^\{]*\{.*?\}\s*\w+\s*;", "", RegexOptions.Singleline);
            if (file == "Common.glsl") text = text.Replace("uintBitsToFloat(SceneShadingInfo.data[i])", "ptSceneShading[i]");
            // Avoid aliases named exactly like struct fields. NVIDIA rescans them
            // when they are nested inside the GLSL-150 fma compatibility macro.
            if (file != "Uniforms.glsl")
                foreach (string alias in new[] { "cFovParams", "cNearFarParam2", "cTime" })
                    text = Regex.Replace(text, @"(?<!\.)\b" + alias + @"\b", "ptAlias_" + alias);
            if (file == "Shadow.glsl")
                text = Regex.Replace(text, @"textureLod\(cTex_DepthShadowCascade, (vec4\([^;\r\n]*?\)), lod\)", "texture(cTex_DepthShadowCascade, $1)");
            return Regex.Replace(text, "#include\\s+\"([^\"]+)\"", m => Expand(m.Groups[1].Value));
        }
        string source = Expand(name);
        source = Regex.Replace(source, @"^\s*#version[^\r\n]*", "", RegexOptions.Multiline);
        source = Regex.Replace(source, @"layout\s*\([^)]*\)\s*", "");
        source = Regex.Replace(source, @"uniform\s+(\w+)([^\{;]*)\{(.*?)\}\s*(\w+)\s*;",
            m => $"struct {m.Groups[1]} {{{m.Groups[3]}}}; uniform {m.Groups[1]} {m.Groups[4]};", RegexOptions.Singleline);
        // Share absent scene textures so the eight authored textures fit ES sampler limits.
        var globals = new Dictionary<string, (string Type, string Name)>();
        source = Regex.Replace(source, @"uniform\s+(sampler\w+)\s+(\w+)\s*;", m =>
        {
            string type = m.Groups[1].Value, sampler = m.Groups[2].Value;
            if (Regex.IsMatch(sampler, @"^cTexture[0-7]$") || sampler == "cTex_DepthShadowCascade") return m.Value;
            bool white = sampler.Contains("Shadow") || sampler.Contains("Projection") || sampler.Contains("MinusField");
            globals[sampler] = (type, "ptGlobal" + (white ? "White" : "Black") + "_" + type);
            return "";
        });
        foreach (var (sampler, global) in globals)
            source = Regex.Replace(source, @"\b" + Regex.Escape(sampler) + @"\b", global.Name);
        source = string.Join("\n", globals.Values.Distinct().Select(g => $"uniform {g.Type} {g.Name};")) + "\n" + source;
        if (name.EndsWith(".frag", StringComparison.Ordinal))
        {
            source = Regex.Replace(source, @"\bvoid\s+main\s*\(", "void TotkMain(");
            source += "\nuniform float ptBrightness;\nvoid main() { TotkMain(); vec3 c=max(oColor.rgb,vec3(0))*ptBrightness; oColor.rgb=mix(12.92*c,1.055*pow(c,vec3(1.0/2.4))-0.055,step(vec3(0.0031308),c)); }\n";
        }
        return (es ? "#version 300 es\nprecision highp float;\nprecision highp int;\nprecision highp sampler2DArray;\nprecision highp sampler2DArrayShadow;\nprecision highp sampler3D;\n" : "#version 150\n#extension GL_ARB_shader_bit_encoding : require\n")
            + "#define fma(a,b,c) ((a)*(b)+(c))\n" + defines + source;
    }
}
