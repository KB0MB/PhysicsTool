using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HKCLTool;
using Silk.NET.OpenGL;

namespace PhysicsTool.Desktop.Rendering;

internal sealed unsafe class TotkNativeRenderer(GL gl, bool es) : IDisposable
{
    private sealed record ProgramState(uint Id, Dictionary<string, int> Locations,
        List<(string Name, int Unit, TextureTarget Target, uint Texture)> Globals)
    {
        internal TotkPreviewMaterial? Material;
        internal int ParametersHash;
    }
    private readonly Dictionary<string, ProgramState> _programs = new(StringComparer.Ordinal);
    private sealed class Variant { internal int Hash; internal string? Lit, Emissive; }
    private readonly ConditionalWeakTable<TotkPreviewMaterial, Variant> _variants = new();
    private readonly Dictionary<(string Type, uint Color), uint> _fallbacks = new();
    private ProgramState? _program;
    internal int CompiledVariants => _programs.Count;
    internal void ReleaseMaterials() { foreach (var p in _programs.Values) p.Material = null; _variants.Clear(); }

    internal void Bind(TotkPreviewMaterial material, uint[] textures, Matrix4x4 camera, Vector3 direction,
        Vector3 center, float radius, Vector3 keyLight, int width, int height, bool emission,
        float directIntensity, float ambientIntensity, float brightness, BotwLightingEnvironment? environment, IconShadowMap? shadow = null)
    {
        var optionHash = new HashCode();
        foreach (var p in material.Options) { optionHash.Add(p.Key, StringComparer.Ordinal); optionHash.Add(p.Value); }
        var variant = _variants.GetOrCreateValue(material); int nextHash = optionHash.ToHashCode();
        if (variant.Hash != nextHash) { variant.Hash = nextHash; variant.Lit = variant.Emissive = null; }
        string defines = emission ? variant.Emissive ??= TotkShaderSource.Defines(material, true) : variant.Lit ??= TotkShaderSource.Defines(material, false);
        if (!_programs.TryGetValue(defines, out var program))
        {
            if (_programs.Count >= 256) { var oldest = _programs.First(); gl.DeleteProgram(oldest.Value.Id); _programs.Remove(oldest.Key); }
            _programs.Add(defines, program = Create(defines));
        }
        _program = program; gl.UseProgram(program.Id);
        var hash = new HashCode();
        foreach (var parameter in material.Parameters) { hash.Add(parameter.Key, StringComparer.Ordinal); foreach (float value in parameter.Value) hash.Add(value); }
        int parameterHash = hash.ToHashCode();
        if (!ReferenceEquals(program.Material, material) || program.ParametersHash != parameterHash)
        {
            foreach (var field in TotkShaderSource.Fields)
            {
                float[] values = field.Default;
                if (field.Name.StartsWith("Mat.", StringComparison.Ordinal) && material.Parameters.TryGetValue(field.Name[4..], out var authored)) values = authored;
                Set(field.Name, field.Type, values);
            }
            program.Material = material; program.ParametersHash = parameterHash;
        }
        Scalar("ptBrightness", brightness);
        var up = Vector3.Normalize(new Vector3(camera.M12, camera.M22, camera.M32));
        var view = Matrix4x4.CreateLookAt(center + direction * Math.Max(1, radius * 2), center, up);
        bool perspective = Math.Abs(camera.M44 - 1) > 1e-5f || Math.Abs(camera.M14) + Math.Abs(camera.M24) + Math.Abs(camera.M34) > 1e-5f;
        if (perspective)
        {
            var x = new Vector3(camera.M11, camera.M21, camera.M31);
            var y = new Vector3(camera.M12, camera.M22, camera.M32);
            float sx = x.Length(), sy = y.Length(); x /= sx; y /= sy;
            var z = -new Vector3(camera.M14, camera.M24, camera.M34);
            view = new(x.X, y.X, z.X, 0, x.Y, y.Y, z.Y, 0, x.Z, y.Z, z.Z, 0, camera.M41/sx, camera.M42/sy, -camera.M44, 1);
        }
        Matrix4x4.Invert(view, out var invView);
        var projection = invView * camera;
        Matrix("Context.cView", view, true); Matrix("Context.cViewInv", invView, true);
        Matrix("Context.cViewProj", camera, false); Matrix("Context.cProj", projection, false);
        Matrix("ShpMtx.cTransform", Matrix4x4.Identity, true);
        Vector("Context.cFrameBuffer", width, height, 1f/width, 1f/height);
        Vector("Context.cNearFar", .01f, 10000, 100, .0001f);
        Vector("Context.cFovParams", perspective ? 1 / Math.Abs(projection.M11) : 1,
            perspective ? 1 / Math.Abs(projection.M22) : 1, 1, 1);
        var light = Vector3.TransformNormal(-keyLight, view);
        Vector("Env.LightDirection0", light.X, light.Y, light.Z, 0);
        Vector("Env.LightDirection0World", -keyLight.X, -keyLight.Y, -keyLight.Z, 0);
        environment ??= BotwLightingEnvironment.Preview;
        var direct = environment.Direct * directIntensity;
        // Reference-calibrated icon key normalization for TotK's forward pass.
        // Keep the editor brightness and the shared BotW palette unchanged.
        if (environment == BotwLightingEnvironment.IconCapture) direct *= new Vector3(1.3f, 1.3f, 1);
        Vector("Env.LightColor0", direct.X, direct.Y, direct.Z, 1);
        var sky = environment.Sky * ambientIntensity; var ground = environment.Ground * ambientIntensity;
        if (environment == BotwLightingEnvironment.IconCapture)
        {
            // These slots are analyzed screen-space ambient colors, not the raw
            // hemisphere endpoints. Use integrated preview fill until the native
            // analyzer is available; feeding raw sky here paints the icon blue.
            sky = ground = (environment.Sky + environment.Ground) * (.5f * 80f / 255 * ambientIntensity);
        }
        Scalar("ptSceneShading[20]", 1); Scalar("ptSceneShading[21]", shadow == null ? 0 : 1);
        Scalar("ptSceneShading[36]", sky.X); Scalar("ptSceneShading[37]", sky.Y); Scalar("ptSceneShading[38]", sky.Z);
        Scalar("ptSceneShading[40]", ground.X); Scalar("ptSceneShading[41]", ground.Y); Scalar("ptSceneShading[42]", ground.Z);
        // The icon preview supplies one actor shadow cascade, but no world shadow maps.
        foreach (string channel in new[] { "depth_shadow", "proj_shadow", "world_shadow", "skyocclusion" })
            Scalar("SceneMat.p_dynamic_" + channel + "_off", 1);
        Scalar("SceneMat.p_dynamic_depth_shadow_off", shadow == null ? 1 : 0);
        Scalar("SceneMat.p_dynamic_depth_shadow_scale", 1);
        Vector("Context.cShadowParam1", 100000, 100000, 100000, 0);
        var shadowTransform = shadow == null ? Matrix4x4.Identity : invView * shadow.Camera *
            Matrix4x4.CreateScale(.5f) * Matrix4x4.CreateTranslation(.5f, .5f, .5f);
        for (int i = 0; i < 4; i++) Matrix($"Context.cCascadeMtx[{i}]", shadowTransform, false);
        Vector("Env.DepthShadowFade0", 0, 0, 0, 0);
        Vector("Env.DepthShadowPcf", 1f / IconShadowMap.Size, 1f / IconShadowMap.Size, 0, 0);
        gl.Uniform4(Location("Env.DepthShadowCascadeInfo"), 1, 1, 0, 0);
        Matrix("Context.cProjectionTexMtx0", Matrix4x4.Identity, true);
        Vector("Env.EnvironmentMaskParam0", 0, 0, 0, 0);
        Vector("Env.EnvironmentMaskParam2", 1, 0, 0, 0);
        Vector("Env.MinusFieldTint", 0, 0, 1, 0);
        Vector("Env.MinusFieldRect", 0, 0, 0, 0);
        Vector("Env.InscatterDist", 0, 0, 0, 0);
        Vector("Env.InscatterParam", 1, 1, 0, 0);
        Vector("Env.HazeDist", 0, 0, 0, 0);
        Vector("Env.HazeColor", 0, 0, 0, 0);
        Vector("Env.GroundFog", 0, 0, 0, 0);
        Vector("Env.Fog2Color", 0, 0, 0, 0); Vector("Env.Fog3Color", 0, 0, 0, 0);
        for (int i = 0; i < 8; i++)
        {
            gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + i));
            gl.BindTexture(TextureTarget.Texture2D, textures[i] == 0 ? Fallback("sampler2D", i == 2 ? 0xffff8080u : 0xffffffffu) : textures[i]);
            gl.Uniform1(Location($"cTexture{i}"), i);
        }
        foreach (var binding in program.Globals)
        {
            gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + binding.Unit));
            gl.BindTexture(binding.Target, binding.Name == "cTex_DepthShadowCascade" && shadow != null ? shadow.Texture : binding.Texture);
            gl.Uniform1(Location(binding.Name), binding.Unit);
        }
    }
    private int Location(string name)
    {
        if (!_program!.Locations.TryGetValue(name, out int value)) _program.Locations[name] = value = gl.GetUniformLocation(_program.Id, name);
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
                case "int": gl.Uniform1(location, (int)v[0]); break;
                case "ivec4" when v.Length >= 4: gl.Uniform4(location, (int)v[0], (int)v[1], (int)v[2], (int)v[3]); break;
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
        value = Matrix4x4.Transpose(value);
        if (affine) gl.UniformMatrix3x4(Location(name), 1, false, (float*)&value);
        else gl.UniformMatrix4(Location(name), 1, false, (float*)&value);
    }
    private ProgramState Create(string defines)
    {
        string vsSource = TotkShaderSource.Build("Material.vert", defines, es), fsSource = TotkShaderSource.Build("Material.frag", defines, es);
        uint vs = Compile(ShaderType.VertexShader, vsSource), fs = 0, id = 0;
        try
        {
            fs = Compile(ShaderType.FragmentShader, fsSource);
            id = gl.CreateProgram(); gl.AttachShader(id, vs); gl.AttachShader(id, fs);
            string[] attrs = ["aPosition", "aNormal", "aTexCoord0", "aTexCoord1", "unused2", "unused3", "unused4", "aTangent", "aColor"];
            for (uint i = 0; i < attrs.Length; i++) gl.BindAttribLocation(id, i, attrs[i]);
            gl.LinkProgram(id); gl.GetProgram(id, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException("TotK native shader link: " + gl.GetProgramInfoLog(id));
            var globals = new List<(string, int, TextureTarget, uint)>(); int unit = 8;
            foreach (var group in Regex.Matches(vsSource + fsSource, @"uniform\s+(sampler\w+)\s+(\w+)\s*;").Cast<Match>().GroupBy(m => m.Groups[2].Value))
            {
                string name = group.Key, type = group.First().Groups[1].Value;
                if (Regex.IsMatch(name, @"^cTexture[0-7]$") || gl.GetUniformLocation(id, name) < 0) continue;
                if (unit >= gl.GetInteger(GLEnum.MaxTextureImageUnits)) throw new NotSupportedException("TotK material exceeds GPU sampler limit.");
                uint color = name.Contains("White") || name.Contains("Shadow") ? 0xffffffffu : 0u;
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
        throw new InvalidOperationException($"TotK native {type}: {error}");
    }
    private static TextureTarget Target(string type) => type switch
    {
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
        else gl.TexImage2D(target, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, &color);
        gl.TexParameter(target, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(target, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(target, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(target, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return id;
    }
    public void Dispose()
    {
        foreach (var p in _programs.Values) gl.DeleteProgram(p.Id);
        foreach (uint t in _fallbacks.Values) gl.DeleteTexture(t);
        _programs.Clear(); _fallbacks.Clear();
    }
}
