using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using ResS = Syroot.NintenTools.NSW.Bfres;
using ResU = Syroot.NintenTools.Bfres;

namespace HKCLTool;

public sealed record BotwPreviewSampler(int WrapU = 0, int WrapV = 0, bool MinLinear = true, bool MagLinear = true,
    int MipFilter = 2, float MinLod = 0, float MaxLod = 13);

public sealed class BotwPreviewMaterial
{
    public Dictionary<string, int> Options { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, float[]> Parameters { get; } = new(StringComparer.Ordinal);
    public VisualModelTexture?[] Textures { get; } = new VisualModelTexture?[6];
    public BotwPreviewSampler?[] Samplers { get; } = new BotwPreviewSampler?[6];
    public string[] UvSets { get; } = ["_u0", "_u1", "_u2", "_u3"];
    public Vector4[] VertexColors { get; set; } = [];
    public bool Translucent => Options.GetValueOrDefault("gsys_renderstate") != 0;
    public string DisplayFace { get; private set; } = "both";

    internal BotwPreviewMaterial Clone()
    {
        var copy = new BotwPreviewMaterial { DisplayFace = DisplayFace, VertexColors = VertexColors };
        foreach (var p in Options) copy.Options[p.Key] = p.Value;
        foreach (var p in Parameters) copy.Parameters[p.Key] = (float[])p.Value.Clone();
        Textures.CopyTo(copy.Textures, 0); Samplers.CopyTo(copy.Samplers, 0); UvSets.CopyTo(copy.UvSets, 0);
        return copy;
    }

    internal static float[] TextureMatrix(uint mode, float sx, float sy, float r, float tx, float ty)
    {
        float c = MathF.Cos(r), s = MathF.Sin(r);
        return mode switch
        {
            1 => [sx*c, -sy*s, sx*s, sy*c, -sx*c*(tx+.5f)+sx*s*(ty-.5f)+.5f, sy*s*(tx+.5f)+sy*c*(ty-.5f)+.5f, 0, 0],
            2 => [sx*c, sy*s, -sx*s, sy*c, sx*s-sx*c*tx-sx*s*ty, -sy*c-sy*s*tx+sy*c*ty+1, 0, 0],
            _ => [sx*c, -sy*s, sx*s, sy*c, -.5f*(sx*c+sx*s-sx)-sx*tx, -.5f*(sy*c-sy*s+sy)+sy*ty+1, 0, 0]
        };
    }

    internal static void Apply(object source, VisualModelMesh mesh, Dictionary<string, VisualModelTexture> textures,
        IReadOnlyDictionary<(string Material, string Sampler), string>? overrides, List<string>? warnings)
    {
        // Only uking_mat matches the supplied shading model. Other archives keep their fallback.
        dynamic m = source;
        if ((string?)m.ShaderAssign?.ShaderArchiveName != "uking_mat") return;
        bool nx = source is ResS.Material;
        Dictionary<string, string> options, samplers, attributes;
        string[] refs, physical;
        if (source is ResS.Material switchMaterial)
        {
            var a = switchMaterial.ShaderAssign;
            options = Enumerable.Range(0, a.ShaderOptions.Count).ToDictionary(a.ShaderOptionDict.GetKey, i => a.ShaderOptions[i]);
            samplers = Enumerable.Range(0, a.SamplerAssigns.Count).ToDictionary(a.SamplerAssignDict.GetKey, i => a.SamplerAssigns[i]);
            attributes = Enumerable.Range(0, a.AttribAssigns.Count).ToDictionary(a.AttribAssignDict.GetKey, i => a.AttribAssigns[i]);
            refs = switchMaterial.TextureRefs.ToArray(); physical = Enumerable.Range(0, switchMaterial.SamplerDict.Count).Select(switchMaterial.SamplerDict.GetKey).ToArray();
        }
        else
        {
            var u = (ResU.Material)source; var a = u.ShaderAssign;
            options = a.ShaderOptions.Keys.ToDictionary(k => k, k => a.ShaderOptions[k].String);
            samplers = a.SamplerAssigns.Keys.ToDictionary(k => k, k => a.SamplerAssigns[k].String);
            attributes = a.AttribAssigns.Keys.ToDictionary(k => k, k => a.AttribAssigns[k].String);
            refs = u.TextureRefs.Select(t => t.Name).ToArray(); physical = u.Samplers.Keys.ToArray();
        }
        var result = new BotwPreviewMaterial();
        foreach (var (key, text) in options)
        {
            string value = text.Trim().Trim('(', ')');
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) result.Options[key] = n;
            else if (bool.TryParse(value, out bool b)) result.Options[key] = b ? 1 : 0;
        }
        // Alpha test and raster state are render-info values on Switch, not static options.
        var render = new Dictionary<string, string>();
        foreach (dynamic r in ToolboxBfresDocument.Items((object)m.RenderInfos))
        {
            string type = r.Type.ToString();
            Array values = type == "String" ? (Array)r.GetValueStrings() : type == "Int32" ? (Array)r.GetValueInt32s() : (Array)r.GetValueSingles();
            if (values.Length > 0) render[(string)r.Name] = Convert.ToString(values.GetValue(0), CultureInfo.InvariantCulture) ?? "";
        }
        result.DisplayFace = render.GetValueOrDefault("gsys_render_state_display_face", "both");
        if (render.TryGetValue("gsys_render_state_mode", out var mode))
            result.Options["gsys_renderstate"] = mode is "translucent" or "custom" ? 1 : 0;
        if (render.TryGetValue("gsys_alpha_test_enable", out var alpha)) result.Options["gsys_alpha_test_enable"] = alpha is "true" or "1" ? 1 : 0;
        if (render.TryGetValue("gsys_alpha_test_func", out var function))
        {
            int index = Array.IndexOf(new[] { "never", "less", "equal", "lequal", "greater", "notequal", "gequal", "always" }, function);
            if (index >= 0) result.Options["gsys_alpha_test_func"] = index;
        }
        if (float.TryParse(render.GetValueOrDefault("gsys_alpha_test_value"), NumberStyles.Float, CultureInfo.InvariantCulture, out float alphaRef) && float.IsFinite(alphaRef))
            result.Parameters["gsys_alpha_test_ref_value"] = [alphaRef];
        for (int i = 0; i < 4; i++) result.UvSets[i] = attributes.GetValueOrDefault($"_u{i}", $"_u{i}");
        string[] semantics = ["_a0", "_s0", "_n0", "_e0", "_t0", "_a1"];
        for (int i = 0; i < semantics.Length; i++)
        {
            string assigned = samplers.GetValueOrDefault(semantics[i], semantics[i]);
            int slot = Array.IndexOf(physical, assigned);
            if (slot < 0 || slot >= refs.Length) continue;
            string name = overrides?.GetValueOrDefault(((string)m.Name, assigned)) ?? refs[slot];
            result.Textures[i] = textures.GetValueOrDefault(name);
            static int Wrap(string value) => value is "Wrap" or "Repeat" ? 0 : value.Contains("Mirror", StringComparison.Ordinal) && !value.Contains("Clamp", StringComparison.Ordinal) ? 1 : 2;
            if (source is ResS.Material sm)
            {
                var sampler = sm.Samplers[slot];
                result.Samplers[i] = new(Wrap(sampler.WrapModeU.ToString()), Wrap(sampler.WrapModeV.ToString()),
                    sampler.ShrinkXY == ResS.Sampler.ShrinkFilterModes.Linear, sampler.ExpandXY == ResS.Sampler.ExpandFilterModes.Linear,
                    (int)sampler.Mipmap, sampler.MinLOD, sampler.MaxLOD);
            }
            else
            {
                var sampler = ((ResU.Material)source).Samplers[assigned].TexSampler;
                result.Samplers[i] = new(Wrap(sampler.ClampX.ToString()), Wrap(sampler.ClampY.ToString()),
                    sampler.MinFilter.ToString() != "Point", sampler.MagFilter.ToString() != "Point",
                    (int)sampler.MipFilter, sampler.MinLod, sampler.MaxLod);
            }
        }
        byte[] data = m.ShaderParamData;
        foreach (dynamic p in ToolboxBfresDocument.Items((object)m.ShaderParams))
        {
            string name = p.Name, type = p.Type.ToString();
            int offset = (int)p.DataOffset, size = (int)p.DataSize;
            if (offset < 0 || size < 4 || offset > data.Length - size) continue;
            float Read(int at) => nx ? BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + at, 4))
                : BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(offset + at, 4));
            uint Word(int at) => nx ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + at, 4))
                : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + at, 4));
            float[] values;
            if (type.Contains("TexSrt", StringComparison.Ordinal) && size >= 24)
            {
                float sx = Read(4), sy = Read(8), r = Read(12), tx = Read(16), ty = Read(20);
                values = TextureMatrix(Word(0), sx, sy, r, tx, ty);
            }
            else if (type.StartsWith("Float", StringComparison.Ordinal)) values = Enumerable.Range(0, size / 4).Select(i => Read(i*4)).ToArray();
            else continue;
            if (values.All(float.IsFinite)) result.Parameters[name] = values;
            else warnings?.Add($"Material {mesh.Name}: invalid shader parameter {name} ignored.");
        }
        if (source is ResU.Material wiiU && wiiU.RenderState is {} state)
        {
            result.Options["gsys_alpha_test_enable"] = state.AlphaControl.AlphaTestEnabled ? 1 : 0;
            result.Options["gsys_alpha_test_func"] = (int)state.AlphaControl.AlphaFunc;
            result.Parameters["gsys_alpha_test_ref_value"] = [state.AlphaRefValue];
            if (!result.Options.ContainsKey("gsys_renderstate"))
                result.Options["gsys_renderstate"] = state.FlagsMode.ToString() is "Translucent" or "Custom" ? 1 : 0;
            result.DisplayFace = state.PolygonControl.CullBack ? "front" : state.PolygonControl.CullFront ? "back" : "both";
        }
        mesh.Material.Botw = result;
    }
}
