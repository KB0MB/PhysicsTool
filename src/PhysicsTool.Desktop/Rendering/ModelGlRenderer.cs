using HKCLTool;
using Silk.NET.OpenGL;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PhysicsTool.Desktop.Rendering;

public sealed unsafe class ModelGlRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _program;
    private readonly Dictionary<string, int> _locations = new();
    private readonly Dictionary<VisualModelTexture, uint> _textures = new();
    private readonly List<GpuMesh> _meshes = new();
    private sealed record GpuMesh(uint Vao, uint Vbo, uint Ebo, VisualModelMesh Source, uint[] Textures);
    private VisualModelData? _model;
    public ModelGlRenderer(GL gl, bool es)
    {
        _gl = gl;
        uint vs = Compile(ShaderType.VertexShader, Source("Model.vert", es));
        uint fs = 0;
        try
        {
            fs = Compile(ShaderType.FragmentShader, Source("Model.frag", es));
            _program = gl.CreateProgram(); gl.AttachShader(_program, vs); gl.AttachShader(_program, fs);
            string[] names = ["aPosition", "aNormal", "aAlbedoUv", "aSpmUv", "aNormalUv", "aAmbientOcclusionUv", "aAuxiliaryUv", "aTangent"];
            for (uint i = 0; i < names.Length; i++) gl.BindAttribLocation(_program, i, names[i]);
            gl.LinkProgram(_program); gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var linked);
            if (linked == 0) { var log = gl.GetProgramInfoLog(_program); gl.DeleteProgram(_program); throw new InvalidOperationException("Model shader link failed: " + log); }
        }
        finally { gl.DeleteShader(vs); if (fs != 0) gl.DeleteShader(fs); }
    }
    private static string Source(string name, bool es)
    {
        var assembly = typeof(ModelGlRenderer).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + name)))!;
        var source = new StreamReader(stream).ReadToEnd();
        source = source.Replace("#version 330 core", es ? "#version 300 es\nprecision highp float;\nprecision highp int;" : "#version 150");
        return Regex.Replace(source, @"layout\(location = \d+\) ", "");
    }
    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type); _gl.ShaderSource(shader, source); _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var success);
        if (success != 0) return shader;
        var log = _gl.GetShaderInfoLog(shader); _gl.DeleteShader(shader);
        throw new InvalidOperationException("Model shader compilation failed: " + log);
    }
    private int Location(string name) { if (!_locations.TryGetValue(name, out var value)) _locations[name] = value = _gl.GetUniformLocation(_program, name); return value; }
    private void I(string name, int value) => _gl.Uniform1(Location(name), value);
    private void F(string name, float value) => _gl.Uniform1(Location(name), value);
    private void V(string name, Vector3 value) => _gl.Uniform3(Location(name), value.X, value.Y, value.Z);
    private void M(string name, Matrix4x4 value) => _gl.UniformMatrix4(Location(name), 1, false, (float*)&value);

    public void Draw(VisualModelData? model, ISet<int> hidden, Matrix4x4 camera, Vector3 direction, int width, int height)
    {
        var gl = _gl;
        gl.Viewport(0, 0, (uint)width, (uint)height); gl.Disable(EnableCap.ScissorTest);
        gl.ColorMask(true, true, true, true); gl.DepthMask(true);
        gl.ClearColor(36 / 255f, 40 / 255f, 41 / 255f, 1); gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        if (!ReferenceEquals(_model, model)) { ReleaseMeshes(); _model = model; if (model != null) Upload(model); }
        if (model == null) return;
        gl.Enable(EnableCap.DepthTest); gl.Disable(EnableCap.CullFace); gl.Disable(EnableCap.Blend);
        gl.UseProgram(_program); M("uMvp", camera); M("uModel", Matrix4x4.Identity); F("uOpacity", 1);
        V("uViewDirection", direction);
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, direction)); var up = Vector3.Cross(direction, right);
        V("uKeyLight", Vector3.Normalize(Vector3.Normalize(new(.35f, .8f, .45f)) * .28f + Vector3.Normalize(direction * .62f + right * .34f + up * .7f) * .72f));
        V("uFillLight", Vector3.Normalize(direction * .45f - right * .72f + up * .22f));
        // Match the original complete-model depth prepass to avoid hair/cloth shell artifacts.
        gl.DepthFunc(DepthFunction.Less); gl.ColorMask(false, false, false, false);
        DrawMeshes();
        gl.ColorMask(true, true, true, true); gl.DepthMask(false); gl.DepthFunc(DepthFunction.Lequal);
        gl.Enable(EnableCap.Blend); gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        DrawMeshes();
        gl.DepthMask(true); gl.Disable(EnableCap.Blend); gl.Disable(EnableCap.DepthTest); gl.BindVertexArray(0); gl.UseProgram(0);

        void DrawMeshes()
        {
            for (int index = 0; index < _meshes.Count; index++)
            {
                if (hidden.Contains(index)) continue;
                var gpu = _meshes[index]; var m = gpu.Source.Material;
                string[] sampler = ["uTexture", "uSpecularTexture", "uNormalTexture", "uAmbientOcclusionTexture", "uAuxiliaryTexture", "uEyeGlintTexture"];
                string[] uses = ["uUseTexture", "uUseSpecularTexture", "uUseNormalTexture", "uUseAmbientOcclusionTexture", "uUseAuxiliaryTexture", "uUseEyeGlintTexture"];
                bool[] enabled = [m.UseAlbedo, m.UseSpm, m.UseNormal, m.UseAmbientOcclusion, m.UseAuxiliary, m.UseEyeGlint];
                for (int t = 0; t < 6; t++) { gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + t)); gl.BindTexture(TextureTarget.Texture2D, gpu.Textures[t]); I(sampler[t], t); I(uses[t], enabled[t] && gpu.Textures[t] != 0 ? 1 : 0); }
                gl.Uniform4(Location("uColor"), m.Tint.X, m.Tint.Y, m.Tint.Z, m.Tint.W);
                I("uAlphaMode", (int)(gpu.Source.Texture?.AlphaMode ?? VisualModelAlphaMode.Opaque));
                I("uShaderModel", (int)m.ShaderModel); I("uMetalChannel", (int)m.MetalChannel); I("uSmoothnessChannel", (int)m.SmoothnessChannel);
                F("uMetalScale", m.MetalScale); F("uSmoothnessScale", m.SmoothnessScale); F("uNormalStrength", m.NormalStrength);
                F("uAmbientOcclusionStrength", m.AmbientOcclusionStrength); F("uAuxiliaryStrength", m.AuxiliaryStrength); F("uEyeGlintStrength", m.EyeGlintStrength);
                gl.BindVertexArray(gpu.Vao); gl.DrawElements(PrimitiveType.Triangles, (uint)gpu.Source.Indices.Length, DrawElementsType.UnsignedInt, null);
            }
        }
    }

    private void Upload(VisualModelData model)
    {
        foreach (var source in model.Meshes)
        {
            var gl = _gl; var vao = gl.GenVertexArray(); var vbo = gl.GenBuffer(); var ebo = gl.GenBuffer();
            var gpu = new GpuMesh(vao, vbo, ebo, source, new uint[6]); _meshes.Add(gpu);
            gl.BindVertexArray(vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            var vertices = ModelVertices.Build(source);
            fixed (float* v = vertices) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * 4), v, BufferUsageARB.StaticDraw);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
            fixed (uint* i = source.Indices) gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(source.Indices.Length * 4), i, BufferUsageARB.StaticDraw);
            int[] sizes = [3, 3, 2, 2, 2, 2, 2, 4]; int offset = 0;
            for (uint a = 0; a < sizes.Length; a++) { gl.VertexAttribPointer(a, sizes[a], VertexAttribPointerType.Float, false, 80, (void*)(offset * 4)); gl.EnableVertexAttribArray(a); offset += sizes[a]; }
            VisualModelTexture?[] textures = [source.Texture ?? (source.UseMissingTextureFallback ? ModelTextures.Default : null), source.SpecularTexture, source.NormalTexture, source.AmbientOcclusionTexture, source.AuxiliaryTexture, source.EyeGlintTexture];
            for (int t = 0; t < textures.Length; t++) if (textures[t] is { } texture) gpu.Textures[t] = UploadTexture(texture);
        }
    }
    private uint UploadTexture(VisualModelTexture texture)
    {
        if (_textures.TryGetValue(texture, out var existing)) return existing;
        var gl = _gl; var id = gl.GenTexture(); _textures.Add(texture, id);
        gl.BindTexture(TextureTarget.Texture2D, id); gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* pixels = texture.Pixels) gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)texture.Width, (uint)texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        gl.GenerateMipmap(TextureTarget.Texture2D); return id;
    }
    private void ReleaseMeshes()
    {
        foreach (var m in _meshes) { _gl.DeleteVertexArray(m.Vao); _gl.DeleteBuffer(m.Vbo); _gl.DeleteBuffer(m.Ebo); }
        foreach (var t in _textures.Values) _gl.DeleteTexture(t);
        _meshes.Clear(); _textures.Clear();
    }
    public void Dispose() { ReleaseMeshes(); _gl.DeleteProgram(_program); }
}
