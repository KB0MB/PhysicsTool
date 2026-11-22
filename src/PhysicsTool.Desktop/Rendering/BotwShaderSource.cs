using System.Globalization;
using System.Text.RegularExpressions;
using HKCLTool;

namespace PhysicsTool.Desktop.Rendering;

// Keep Killz's source intact. Only adapt the host API and the preview render pass here.
internal static class BotwShaderSource
{
    internal sealed record Field(string Name, string Type, float[] Default);
    internal static readonly IReadOnlyList<Field> Fields = ReadFields();
    internal static string Read(string name)
    {
        var assembly = typeof(BotwShaderSource).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(".BotwNative." + name, StringComparison.Ordinal)))!;
        return new StreamReader(stream).ReadToEnd();
    }
    private static IReadOnlyList<Field> ReadFields()
    {
        var fields = new List<Field>();
        foreach (Match block in Regex.Matches(Read("Uniforms.glsl"), @"uniform\s+\w+[^\{]*\{(?<body>.*?)\}\s*(?<instance>\w+)\s*;", RegexOptions.Singleline))
        foreach (Match field in Regex.Matches(block.Groups["body"].Value,
            @"\b(?<type>float|vec[234]|mat[234](?:x[234])?)\s+(?<name>\w+)\s*;(?<comment>[^\r\n]*)"))
        {
            var defaults = Regex.Match(field.Groups["comment"].Value, "default_value=\"([^\"]+)\"");
            var values = defaults.Success ? defaults.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray() : [];
            fields.Add(new(block.Groups["instance"].Value + "." + field.Groups["name"].Value, field.Groups["type"].Value, values));
        }
        return fields;
    }
    internal static string Defines(BotwPreviewMaterial material, bool emission)
    {
        var options = new SortedDictionary<string, int>(material.Options, StringComparer.Ordinal)
        {
            ["BLENDER_RENDER"] = 0,
            ["gsys_weight"] = 0, // Vertex positions and normals have already been CPU skinned.
            ["gsys_assign_type"] = 1,
            ["gsys_assign_variation"] = 0
        };
        if (!emission) options["uking_enable_emission"] = 0;
        return string.Join("\n", options.Where(p => Regex.IsMatch(p.Key, @"^[A-Za-z_]\w*$"))
            .Select(p => $"#define {p.Key} {p.Value.ToString(CultureInfo.InvariantCulture)}")) + "\n";
    }
    internal static string Build(string name, string defines, bool es)
    {
        string Expand(string file) => Regex.Replace(Read(file), "#include\\s+\"([^\"]+)\"", m => Expand(m.Groups[1].Value));
        string source = Expand(name);
        // GLSL 150/ES lack C token-pasting. Expand the source's two indexed macros
        // before handing the remaining preprocessing to the driver.
        source = Regex.Replace(source, @"\\\r?\n", " ");
        foreach (string macro in new[] { "FETCH_ONCE", "CALC_COLOR" })
        {
            var definition = Regex.Match(source, @"(?m)^#define " + macro + @"\(n\) (?<body>[^\r\n]*)");
            if (!definition.Success) continue;
            source = source.Remove(definition.Index, definition.Length);
            source = Regex.Replace(source, @"\b" + macro + @"\((\d+)\)", m =>
                Regex.Replace(definition.Groups["body"].Value, @"\bn\b", m.Groups[1].Value).Replace("##", ""));
        }
        source = Regex.Replace(source, @"^\s*#version[^\r\n]*", "", RegexOptions.Multiline);
        source = Regex.Replace(source, @"layout\s*\([^)]*\)\s*", "");
        source = Regex.Replace(source, @"uniform\s+(\w+)([^\{;]*)\{(.*?)\}\s*(\w+)\s*;",
            m => $"struct {m.Groups[1]} {{{m.Groups[3]}}}; uniform {m.Groups[1]} {m.Groups[4]};", RegexOptions.Singleline);
        if (name.EndsWith(".frag", StringComparison.Ordinal))
        {
            source = Regex.Replace(source, @"\bvoid\s+main\s*\(", "void BotwMain(");
            // The game applies coverage in its z/gbuffer pass. This forward viewport
            // needs that same alpha test in both its depth and color draws.
            source += "\nvoid main() { BotwMain(); AlphaTest(GetSourceChannel(uking_transparency_color,uking_transparency_channel).w); vec3 c=max(oColor.rgb,vec3(0)); oColor.rgb=mix(12.92*c,1.055*pow(c,vec3(1.0/2.4))-0.055,step(vec3(0.0031308),c)); }\n";
        }
        return (es ? "#version 300 es\nprecision highp float;\nprecision highp int;\nprecision highp sampler2DArray;\nprecision highp sampler2DArrayShadow;\nprecision highp sampler3D;\n" : "#version 150\n#extension GL_ARB_shader_bit_encoding : require\n")
            + "#define fma(a,b,c) ((a)*(b)+(c))\n" + defines + source;
    }
}
