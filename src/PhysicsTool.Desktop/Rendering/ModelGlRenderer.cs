using HKCLTool;
using Silk.NET.OpenGL;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PhysicsTool.Desktop.Rendering;

public sealed unsafe class ModelGlRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _standardProgram, _botwProgram, _totkProgram, _wireProgram;
    private uint _program;
    private readonly GridGlRenderer _grid;
    private readonly Dictionary<(uint Program, string Name), int> _locations = new();
    private readonly Dictionary<VisualModelTexture, uint> _textures = new();
    private readonly List<GpuMesh> _meshes = new();
    private sealed record GpuMesh(uint Vao, uint Vbo, uint Ebo, VisualModelMesh Source, uint[] Textures, uint WireEbo = 0, int WireIndexCount = 0);
    private VisualModelData? _model;
    public ModelGlRenderer(GL gl, bool es)
    {
        _gl = gl;
        _standardProgram = CreateProgram("Model.frag", es);
        try
        {
            _botwProgram = CreateProgram("BOTW.frag", es);
            _totkProgram = CreateProgram("TOTK.frag", es);
            _wireProgram = CreateProgram("Wireframe.frag", es);
            _grid = new GridGlRenderer(gl, es);
        }
        catch
        {
            gl.DeleteProgram(_standardProgram);
            if (_botwProgram != 0) gl.DeleteProgram(_botwProgram);
            if (_totkProgram != 0) gl.DeleteProgram(_totkProgram);
            if (_wireProgram != 0) gl.DeleteProgram(_wireProgram);
            throw;
        }
    }
    private uint CreateProgram(string fragment, bool es)
    {
        var gl = _gl;
        uint vs = Compile(ShaderType.VertexShader, Source(fragment == "TOTK.frag" ? "TOTK.vert" : "Model.vert", es));
        uint fs = 0, program = 0;
        try
        {
            fs = Compile(ShaderType.FragmentShader, Source(fragment, es));
            program = gl.CreateProgram(); gl.AttachShader(program, vs); gl.AttachShader(program, fs);
            string[] names = ["aPosition", "aNormal", "aAlbedoUv", "aSpmUv", "aNormalUv", "aAmbientOcclusionUv", "aAuxiliaryUv", "aTangent", "aVertexColor"];
            for (uint i = 0; i < names.Length; i++) gl.BindAttribLocation(program, i, names[i]);
            gl.LinkProgram(program); gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
            if (linked == 0) throw new InvalidOperationException(fragment + " link failed: " + gl.GetProgramInfoLog(program));
            return program;
        }
        catch { if (program != 0) gl.DeleteProgram(program); throw; }
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
    private int Location(string name) { var key = (_program, name); if (!_locations.TryGetValue(key, out var value)) _locations[key] = value = _gl.GetUniformLocation(_program, name); return value; }
    private void I(string name, int value) => _gl.Uniform1(Location(name), value);
    private void F(string name, float value) => _gl.Uniform1(Location(name), value);
    private void V(string name, Vector3 value) => _gl.Uniform3(Location(name), value.X, value.Y, value.Z);
    private void M(string name, Matrix4x4 value) => _gl.UniformMatrix4(Location(name), 1, false, (float*)&value);

    public void Draw(VisualModelData? model, ISet<int> hidden, Matrix4x4 camera, Vector3 direction, int width, int height, IReadOnlyList<GridLine>? grid = null, Vector3? background = null, bool emission = false, ModelDisplayMode displayMode = ModelDisplayMode.Textured)
    {
        var gl = _gl;
        gl.Viewport(0, 0, (uint)width, (uint)height); gl.Disable(EnableCap.ScissorTest);
        gl.ColorMask(true, true, true, true); gl.DepthMask(true);
        var clear = background ?? new Vector3(36 / 255f, 40 / 255f, 41 / 255f);
        gl.ClearColor(clear.X, clear.Y, clear.Z, 1); gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        if (grid != null) _grid.Draw(grid, camera);
        if (!ReferenceEquals(_model, model))
        {
            if (model != null && CanUpdatePose(model)) UpdatePose(model);
            else { ReleaseMeshes(); if (model != null) Upload(model); }
            _model = model;
        }
        if (model == null) return;
        gl.Enable(EnableCap.DepthTest); gl.Disable(EnableCap.CullFace); gl.Disable(EnableCap.Blend);
        if (displayMode == ModelDisplayMode.Wireframe)
        {
            DrawWireframe(hidden, camera, clear);
            return;
        }
        var right = Vector3.Cross(Vector3.UnitY, direction);
        right = right.LengthSquared() > 1e-8f ? Vector3.Normalize(right) : Vector3.UnitX;
        var up = Vector3.Cross(direction, right);
        var keyLight = Vector3.Normalize(Vector3.Normalize(new(.35f, .8f, .45f)) * .28f + Vector3.Normalize(direction * .62f + right * .34f + up * .7f) * .72f);
        var fillLight = Vector3.Normalize(direction * .45f - right * .72f + up * .22f);
        _program = 0;
        // Match the original complete-model depth prepass to avoid hair/cloth shell artifacts.
        var edgedFaces = displayMode == ModelDisplayMode.EdgedFaces;
        if (edgedFaces)
        {
            // Bias filled surfaces behind their edges to avoid flicker. Unlike
            // polygon line mode, fill offset also works on OpenGL ES.
            gl.Enable(EnableCap.PolygonOffsetFill);
            gl.PolygonOffset(1f, 1f);
        }
        try
        {
            gl.DepthFunc(DepthFunction.Less); gl.ColorMask(false, false, false, false);
            DrawMeshes();
            gl.ColorMask(true, true, true, true); gl.DepthMask(false); gl.DepthFunc(DepthFunction.Lequal);
            gl.Enable(EnableCap.Blend); gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            DrawMeshes();
        }
        finally
        {
            if (edgedFaces) gl.Disable(EnableCap.PolygonOffsetFill);
        }
        if (edgedFaces)
        {
            gl.Disable(EnableCap.Blend);
            DrawWireframe(hidden, camera, clear, overlay: true);
        }
        gl.DepthMask(true); gl.Disable(EnableCap.Blend); gl.Disable(EnableCap.DepthTest); gl.BindVertexArray(0); gl.UseProgram(0);

        void DrawMeshes()
        {
            for (int index = 0; index < _meshes.Count; index++)
            {
                if (hidden.Contains(index)) continue;
                var gpu = _meshes[index]; var m = gpu.Source.Material;
                var program = m.Totk != null ? _totkProgram : m.UseBotwPreviewShader ? _botwProgram : _standardProgram;
                if (_program != program)
                {
                    _program = program; gl.UseProgram(program);
                    M("uMvp", camera); M("uModel", Matrix4x4.Identity); F("uOpacity", 1);
                    V("uViewDirection", direction); V("uKeyLight", keyLight); V("uFillLight", fillLight);
                }
                if (m.Totk is { } totk)
                {
                    V("specLightDirection", keyLight);
                    I("useNormalMap", m.UseNormal ? 1 : 0); I("renderVertColor", 1);
                    I("renderDiffuse", 1); I("renderSpecular", m.UseSpm ? 1 : 0); I("renderFresnel", 1); I("colorOverride", 0);
                    foreach (var pair in totk.Integers) I(pair.Key, pair.Value);
                    if (!emission) I("totkEmissionEnabled", 0);
                    foreach (var pair in totk.Scalars) F(pair.Key, pair.Value);
                    foreach (var pair in totk.Vectors) gl.Uniform4(Location(pair.Key), pair.Value.X, pair.Value.Y, pair.Value.Z, pair.Value.W);
                    for (int t = 0; t < 8; t++)
                    {
                        gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + t));
                        gl.BindTexture(TextureTarget.Texture2D, gpu.Textures[t]); I($"Texture{t}", t);
                    }
                    gl.BindVertexArray(gpu.Vao); gl.DrawElements(PrimitiveType.Triangles, (uint)gpu.Source.Indices.Length, DrawElementsType.UnsignedInt, null);
                    continue;
                }
                string[] sampler = ["uTexture", "uSpecularTexture", "uNormalTexture", "uAmbientOcclusionTexture", "uAuxiliaryTexture", "uEyeGlintTexture"];
                string[] uses = ["uUseTexture", "uUseSpecularTexture", "uUseNormalTexture", "uUseAmbientOcclusionTexture", "uUseAuxiliaryTexture", "uUseEyeGlintTexture"];
                bool[] enabled = [m.UseAlbedo || gpu.Source.Texture == null && gpu.Source.UseMissingTextureFallback, m.UseSpm, m.UseNormal, m.UseAmbientOcclusion, m.UseAuxiliary, m.UseEyeGlint];
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

    private void DrawWireframe(ISet<int> hidden, Matrix4x4 camera, Vector3 background, bool overlay = false)
    {
        var gl = _gl;
        _program = _wireProgram;
        gl.UseProgram(_program);
        M("uMvp", camera);
        M("uModel", Matrix4x4.Identity);
        V("uWireColor", overlay ? new Vector3(.06f, .08f, .1f) :
            background.X > .5f ? new Vector3(.2f, .38f, .34f) : new Vector3(.55f, .75f, .7f));
        gl.DepthFunc(DepthFunction.Lequal);

        for (var index = 0; index < _meshes.Count; index++)
        {
            if (hidden.Contains(index)) continue;
            var gpu = _meshes[index];
            gl.BindVertexArray(gpu.Vao);
            if (gpu.WireEbo == 0)
            {
                // Cache topology separately from the vertex buffer so animation
                // updates reuse the same edges on desktop OpenGL and OpenGL ES.
                var edges = ModelWireframe.Edges(gpu.Source.Indices);
                gpu = gpu with { WireEbo = gl.GenBuffer(), WireIndexCount = edges.Length };
                _meshes[index] = gpu;
                gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, gpu.WireEbo);
                fixed (uint* data = edges)
                    gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(edges.Length * sizeof(uint)), data, BufferUsageARB.StaticDraw);
            }
            else gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, gpu.WireEbo);

            gl.DrawElements(PrimitiveType.Lines, (uint)gpu.WireIndexCount, DrawElementsType.UnsignedInt, null);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, gpu.Ebo);
        }

        gl.Disable(EnableCap.DepthTest);
        gl.BindVertexArray(0);
        gl.UseProgram(0);
    }

    private bool CanUpdatePose(VisualModelData model) => _meshes.Count == model.Meshes.Count &&
        _meshes.Select((gpu, i) => (gpu.Source, Next: model.Meshes[i])).All(pair =>
            ReferenceEquals(pair.Source.Indices, pair.Next.Indices) && ReferenceEquals(pair.Source.Material, pair.Next.Material) &&
            ReferenceEquals(pair.Source.Texture, pair.Next.Texture) && ReferenceEquals(pair.Source.SpecularTexture, pair.Next.SpecularTexture) &&
            ReferenceEquals(pair.Source.NormalTexture, pair.Next.NormalTexture) && ReferenceEquals(pair.Source.AmbientOcclusionTexture, pair.Next.AmbientOcclusionTexture) &&
            ReferenceEquals(pair.Source.AuxiliaryTexture, pair.Next.AuxiliaryTexture) && ReferenceEquals(pair.Source.EyeGlintTexture, pair.Next.EyeGlintTexture));
    private void UpdatePose(VisualModelData model)
    {
        for (int i = 0; i < _meshes.Count; i++)
        {
            var gpu = _meshes[i]; var source = model.Meshes[i];
            var vertices = ModelVertices.Build(source);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, gpu.Vbo);
            fixed (float* v = vertices) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * 4), v, BufferUsageARB.DynamicDraw);
            _meshes[i] = gpu with { Source = source };
        }
    }
    private void Upload(VisualModelData model)
    {
        foreach (var source in model.Meshes)
        {
            var gl = _gl; var vao = gl.GenVertexArray(); var vbo = gl.GenBuffer(); var ebo = gl.GenBuffer();
            var gpu = new GpuMesh(vao, vbo, ebo, source, new uint[8]); _meshes.Add(gpu);
            gl.BindVertexArray(vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            var vertices = ModelVertices.Build(source);
            fixed (float* v = vertices) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * 4), v, BufferUsageARB.StaticDraw);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
            fixed (uint* i = source.Indices) gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(source.Indices.Length * 4), i, BufferUsageARB.StaticDraw);
            int[] sizes = source.Material.Totk == null ? [3, 3, 2, 2, 2, 2, 2, 4] : [3, 3, 2, 2, 2, 2, 2, 4, 4]; int offset = 0;
            uint stride = source.Material.Totk == null ? 80u : 96u;
            for (uint a = 0; a < sizes.Length; a++) { gl.VertexAttribPointer(a, sizes[a], VertexAttribPointerType.Float, false, stride, (void*)(offset * 4)); gl.EnableVertexAttribArray(a); offset += sizes[a]; }
            VisualModelTexture?[] textures = source.Material.Totk?.Textures ?? [source.Texture ?? (source.UseMissingTextureFallback ? ModelTextures.Default : null), source.SpecularTexture, source.NormalTexture, source.AmbientOcclusionTexture, source.AuxiliaryTexture, source.EyeGlintTexture];
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
        foreach (var m in _meshes) { _gl.DeleteVertexArray(m.Vao); _gl.DeleteBuffer(m.Vbo); _gl.DeleteBuffer(m.Ebo); if (m.WireEbo != 0) _gl.DeleteBuffer(m.WireEbo); }
        foreach (var t in _textures.Values) _gl.DeleteTexture(t);
        _meshes.Clear(); _textures.Clear();
    }
    public void Dispose() { ReleaseMeshes(); _grid.Dispose(); _gl.DeleteProgram(_standardProgram); _gl.DeleteProgram(_botwProgram); _gl.DeleteProgram(_totkProgram); _gl.DeleteProgram(_wireProgram); }
}
