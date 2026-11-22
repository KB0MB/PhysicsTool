using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HKCLTool;
using Silk.NET.OpenGL;

namespace PhysicsTool.Desktop.Rendering;

internal sealed unsafe class BotwNativeRenderer(GL gl, bool es) : IDisposable
{
    private sealed record ProgramState(uint Id, Dictionary<string, int> Locations, List<(string Name, int Unit, TextureTarget Target, uint Texture)> Globals)
    {
        internal BotwPreviewMaterial? Material;
        internal int ParametersHash;
    }
    private sealed class MaterialVariant { internal int Hash; internal string? Lit, Emissive; }
    private readonly ConditionalWeakTable<BotwPreviewMaterial, MaterialVariant> _variants = new();
    private readonly Dictionary<string, ProgramState> _programs = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Type, uint Color), uint> _fallbacks = new();
    private ProgramState? _program;
    private readonly bool _diagnostics = Environment.GetEnvironmentVariable("PHYSICSTOOL_SHADER_DIAGNOSTICS") == "1";
    private void Check(string phase)
    {
        if (_diagnostics && gl.GetError() is var error && error != GLEnum.NoError)
            throw new InvalidOperationException("BotW GL " + phase + ": " + error);
    }
    internal int CompiledVariants => _programs.Count;
    internal void ReleaseMaterials()
    {
        foreach (var p in _programs.Values) p.Material = null;
        _variants.Clear();
    }

    internal void Bind(BotwPreviewMaterial material, uint[] textures, Matrix4x4 camera, Vector3 direction,
        Vector3 center, float radius, Vector3 keyLight, int width, int height, bool emission)
    {
        var hash = new HashCode();
        foreach (var option in material.Options) { hash.Add(option.Key, StringComparer.Ordinal); hash.Add(option.Value); }
        int optionHash = hash.ToHashCode();
        var variant = _variants.GetOrCreateValue(material);
        if (variant.Hash != optionHash) { variant.Hash = optionHash; variant.Lit = variant.Emissive = null; }
        string defines = emission ? variant.Emissive ??= BotwShaderSource.Defines(material, true)
            : variant.Lit ??= BotwShaderSource.Defines(material, false);
        if (!_programs.TryGetValue(defines, out var program))
        {
            // Bound GPU memory while browsing many unrelated actors in one session.
            if (_programs.Count >= 256) { var oldest = _programs.First(); gl.DeleteProgram(oldest.Value.Id); _programs.Remove(oldest.Key); }
            _programs.Add(defines, program = Create(defines));
        }
        _program = program;
        Check("program creation");
        gl.UseProgram(program.Id);
        hash = new HashCode();
        foreach (var parameter in material.Parameters) { hash.Add(parameter.Key, StringComparer.Ordinal); foreach (float value in parameter.Value) hash.Add(value); }
        int parameterHash = hash.ToHashCode();
        if (!ReferenceEquals(program.Material, material) || program.ParametersHash != parameterHash)
        {
            foreach (var f in BotwShaderSource.Fields)
            {
                float[] values = f.Default;
                if (f.Name.StartsWith("mat.", StringComparison.Ordinal) && material.Parameters.TryGetValue(f.Name[4..], out var authored)) values = authored;
                Set(f.Name, f.Type, values);
                Check(f.Name);
            }
            program.Material = material; program.ParametersHash = parameterHash;
        }
        // Preserve the editor's exact clip transform while supplying view-space lighting.
        var up = Vector3.Normalize(new Vector3(camera.M12, camera.M22, camera.M32));
        var eye = center + direction * Math.Max(1, radius * 2);
        var view = Matrix4x4.CreateLookAt(eye, center, up);
        if (Math.Abs(camera.M44 - 1) > 1e-5f || Math.Abs(camera.M14) + Math.Abs(camera.M24) + Math.Abs(camera.M34) > 1e-5f)
        {
            var x = new Vector3(camera.M11, camera.M21, camera.M31);
            var y = new Vector3(camera.M12, camera.M22, camera.M32);
            float sx = x.Length(), sy = y.Length(); x /= sx; y /= sy;
            var z = -new Vector3(camera.M14, camera.M24, camera.M34);
            view = new(x.X, y.X, z.X, 0, x.Y, y.Y, z.Y, 0, x.Z, y.Z, z.Z, 0, camera.M41/sx, camera.M42/sy, -camera.M44, 1);
        }
        Matrix4x4.Invert(view, out var invView);
        Matrix("context.cView", view, true); Matrix("context.cViewInv", invView, true);
        Matrix("context.cViewProj", camera, false); Matrix("context.cProj", invView * camera, false);
        Matrix("shape.cTransform", Matrix4x4.Identity, true);
        Matrix("env.cViewToWorldPos", invView, true);
        Vector("context.cScreen", width, height, 1f/width, 1f/height);
        Vector("context.cf", width, height, 1f/width, 1f/height);
        Vector("context.cNearFar", .01f, 10000, 100, .0001f);
        Vector("context.cFovParams", 1, 1, 1, 1);
        Vector("shape.cShapeParam", 1, 1, 1, 1);
        var light = Vector3.TransformNormal(-keyLight, view);
        Vector("env.cLightDirection0", light.X, light.Y, light.Z, 0);
        Vector("env.LightDirection0World", -keyLight.X, -keyLight.Y, -keyLight.Z, 0);
        Vector("env.cLightColor", 1.1f, 1.05f, 1, 1);
        Vector("env.cLightSpecColor", 1, 1, 1, 1);
        Vector("env.cHemiSkyColor", .3f, .32f, .36f, 1);
        Vector("env.cHemiGroundColor", .18f, .18f, .18f, 1);
        Vector("sceneMat.uking_dynamic_toon_light_adjust_for_demo", 1, 1, 1, 1);
        foreach (var name in new[] { "depth_shadow", "proj_shadow", "world_shadow", "skyocclusion" })
            Scalar("sceneMat.uking_dynamic_" + name + "_off", 1);
        // Captured game weather and fog do not belong to an isolated actor preview.
        Vector("env.cSkyFogDistance", 0, 0, 0, 0);
        Vector("env.cSkyHeightParam", 0, 0, 0, 0);
        Vector("env.cSkyParam", 1, 1, 1, 0);
        Vector("env.cSkyInscatterParam", 1, 1, 0, 0);
        Vector("env.cSkyColor", 0, 0, 0, 0);
        Vector("env.Fog2_Param", 0, 1, 0, 0);
        Vector("env.cWindParam", 0, 0, 0, 0);
        Vector("env.cWindParam2", 0, 0, 0, 0);
        Check("scene uniforms");
        for (int i = 0; i < 6; i++)
        {
            gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + i));
            gl.BindTexture(TextureTarget.Texture2D, textures[i] == 0 ? Fallback("sampler2D", i == 2 ? 0xffff8080u : 0xffffffffu) : textures[i]);
            gl.Uniform1(Location($"cTexture{i}"), i);
        }
        foreach (var binding in program.Globals)
        {
            gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + binding.Unit));
            gl.BindTexture(binding.Target, binding.Texture);
            gl.Uniform1(Location(binding.Name), binding.Unit);
        }
        Check("samplers");
        if (_diagnostics)
        {
            gl.ValidateProgram(program.Id); gl.GetProgram(program.Id, ProgramPropertyARB.ValidateStatus, out int valid);
            if (valid == 0) throw new InvalidOperationException("BotW validation: " + gl.GetProgramInfoLog(program.Id));
        }
    }
    private int Location(string name)
    {
        if (!_program!.Locations.TryGetValue(name, out int value))
            _program.Locations[name] = value = gl.GetUniformLocation(_program.Id, name);
        return value;
    }
    private void Scalar(string name, float value) => gl.Uniform1(Location(name), value);
    private void Vector(string name, float x, float y, float z, float w) => gl.Uniform4(Location(name), x, y, z, w);
    private void Set(string name, string type, float[] v)
    {
        int location = Location(name); if (location < 0 || v.Length == 0) return;
        fixed (float* p = v)
        {
            switch (type)
            {
                case "float": gl.Uniform1(location, v[0]); break;
                case "vec2" when v.Length >= 2: gl.Uniform2(location, 1, p); break;
                case "vec3" when v.Length >= 3: gl.Uniform3(location, 1, p); break;
                case "vec4" when v.Length >= 4: gl.Uniform4(location, 1, p); break;
                case "mat2x4" when v.Length >= 8: gl.UniformMatrix2x4(location, 1, false, p); break;
                case "mat3x4" when v.Length >= 12: gl.UniformMatrix3x4(location, 1, false, p); break;
            }
        }
    }
    private void Matrix(string name, Matrix4x4 value, bool affine)
    {
        // Native GLSL multiplies row vectors, unlike the older preview shaders.
        value = Matrix4x4.Transpose(value);
        if (affine) gl.UniformMatrix3x4(Location(name), 1, false, (float*)&value);
        else gl.UniformMatrix4(Location(name), 1, false, (float*)&value);
    }
    private ProgramState Create(string defines)
    {
        Check("before compilation");
        string vsSource = BotwShaderSource.Build("Material.vert", defines, es);
        string fsSource = BotwShaderSource.Build("Material.frag", defines, es);
        uint vs = Compile(ShaderType.VertexShader, vsSource), fs = 0, id = 0;
        try
        {
            fs = Compile(ShaderType.FragmentShader, fsSource);
            id = gl.CreateProgram(); gl.AttachShader(id, vs); gl.AttachShader(id, fs);
            string[] attrs = ["aPosition", "aNormal", "aTexCoord0", "aTexCoord1", "aTexCoord2", "aTexCoord3", "unused", "aTangent", "aColor", "aColor1"];
            for (uint i = 0; i < attrs.Length; i++) gl.BindAttribLocation(id, i, attrs[i]);
            gl.LinkProgram(id); gl.GetProgram(id, ProgramPropertyARB.LinkStatus, out int linked);
            Check("link");
            if (linked == 0) throw new InvalidOperationException("BotW native shader link: " + gl.GetProgramInfoLog(id));
            var globals = new List<(string, int, TextureTarget, uint)>();
            int unit = 6;
            foreach (var group in Regex.Matches(vsSource + fsSource, @"uniform\s+(sampler\w+)\s+(\w+)\s*;").Cast<Match>().GroupBy(m => m.Groups[2].Value))
            {
                string name = group.Key, type = group.First().Groups[1].Value;
                if (Regex.IsMatch(name, @"^cTexture[0-5]$") || gl.GetUniformLocation(id, name) < 0) continue;
                if (unit >= gl.GetInteger(GLEnum.MaxCombinedTextureImageUnits)) throw new NotSupportedException("BotW material exceeds GPU sampler limit.");
                uint color = name.Contains("LightAnalyzed", StringComparison.Ordinal) ? 0xff505050u :
                    name.Contains("Shadow", StringComparison.Ordinal) || name.Contains("Occlusion", StringComparison.Ordinal) ? 0xffffffffu : 0x00000000u;
                globals.Add((name, unit++, Target(type), Fallback(type, color)));
            }
            return new(id, new(), globals);
        }
        catch { if (id != 0) gl.DeleteProgram(id); throw; }
        finally { gl.DeleteShader(vs); if (fs != 0) gl.DeleteShader(fs); }
    }
    private uint Compile(ShaderType type, string source)
    {
        uint id = gl.CreateShader(type); gl.ShaderSource(id, source); gl.CompileShader(id);
        gl.GetShader(id, ShaderParameterName.CompileStatus, out int ok);
        if (ok != 0) return id;
        string error = gl.GetShaderInfoLog(id); gl.DeleteShader(id);
        throw new InvalidOperationException($"BotW native {type}: {error}");
    }
    private static TextureTarget Target(string type) => type switch
    {
        "samplerCube" => TextureTarget.TextureCubeMap,
        "sampler3D" => TextureTarget.Texture3D,
        "sampler2DArray" or "sampler2DArrayShadow" => TextureTarget.Texture2DArray,
        _ => TextureTarget.Texture2D
    };
    private uint Fallback(string type, uint color)
    {
        if (_fallbacks.TryGetValue((type, color), out uint id)) return id;
        id = gl.GenTexture(); _fallbacks.Add((type, color), id);
        var target = Target(type); gl.BindTexture(target, id);
        if (type == "sampler2DArrayShadow")
        {
            float[] depth = [1, 1, 1, 1];
            fixed (float* p = depth) gl.TexImage3D(target, 0, (int)InternalFormat.DepthComponent32f, 1, 1, 4, 0, PixelFormat.DepthComponent, PixelType.Float, p);
            gl.TexParameter(target, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
            gl.TexParameter(target, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        }
        else if (target is TextureTarget.Texture3D or TextureTarget.Texture2DArray)
            gl.TexImage3D(target, 0, (int)InternalFormat.Rgba8, 1, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, &color);
        else if (target == TextureTarget.TextureCubeMap)
            for (int i = 0; i < 6; i++) gl.TexImage2D((TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + i), 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, &color);
        else gl.TexImage2D(target, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, &color);
        gl.TexParameter(target, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(target, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(target, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(target, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        if (target is TextureTarget.Texture3D or TextureTarget.TextureCubeMap) gl.TexParameter(target, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);
        Check("fallback " + type);
        return id;
    }
    public void Dispose()
    {
        foreach (var p in _programs.Values) gl.DeleteProgram(p.Id);
        foreach (uint t in _fallbacks.Values) gl.DeleteTexture(t);
        _programs.Clear(); _fallbacks.Clear();
    }
}
