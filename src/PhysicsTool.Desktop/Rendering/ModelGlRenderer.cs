using HKCLTool;
using Silk.NET.OpenGL;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PhysicsTool.Desktop.Rendering;

public sealed unsafe class ModelGlRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly bool _es;
    private readonly uint _standardProgram, _botwProgram, _totkProgram, _ootProgram, _wireProgram, _particleProgram;
    private uint _program;
    private readonly GridGlRenderer _grid;
    private readonly BotwNativeRenderer _botwNative;
    private readonly TotkNativeRenderer _totkNative;
    private IconShadowMap? _iconShadow;
    private readonly Dictionary<(uint Program, string Name), int> _locations = new();
    private readonly Dictionary<(VisualModelTexture Texture, bool Srgb, BotwPreviewSampler? Sampler), uint> _textures = new();
    private readonly List<GpuMesh> _meshes = new();
    private sealed record GpuMesh(uint Vao, uint Vbo, uint Ebo, VisualModelMesh Source, uint[] Textures, uint WireEbo = 0, int WireIndexCount = 0)
    {
        internal Vector3 Minimum { get; init; }
        internal Vector3 Maximum { get; init; }
        internal float[]? PackedPose { get; set; }
        internal uint ShadowVbo { get; set; }
    }
    private int[] _sceneOrder = [];
    public int LastVisibleMeshes { get; private set; }
    public int LastMaterialUpdates { get; private set; }
    private static GpuMesh WithBounds(GpuMesh mesh)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        var v = mesh.Source.Vertices;
        for (int i = 0; i < v.Length; i += 8) { var p = new Vector3(v[i], v[i + 1], v[i + 2]); min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        return mesh with { Minimum = min, Maximum = max };
    }
    private static bool Outside(GpuMesh mesh, Matrix4x4 camera)
    {
        int common = 63;
        for (int i = 0; i < 8; i++)
        {
            var p = Vector4.Transform(new Vector4((i & 1) == 0 ? mesh.Minimum.X : mesh.Maximum.X,
                (i & 2) == 0 ? mesh.Minimum.Y : mesh.Maximum.Y, (i & 4) == 0 ? mesh.Minimum.Z : mesh.Maximum.Z, 1), camera);
            int mask = (p.X < -p.W ? 1 : 0) | (p.X > p.W ? 2 : 0) | (p.Y < -p.W ? 4 : 0) | (p.Y > p.W ? 8 : 0) | (p.Z < -p.W ? 16 : 0) | (p.Z > p.W ? 32 : 0);
            common &= mask;
            if (common == 0) return false;
        }
        return common != 0;
    }
    private VisualModelData? _model;
    public ModelGlRenderer(GL gl, bool es)
    {
        _gl = gl; _es = es;
        _botwNative = new(gl, es);
        _totkNative = new(gl, es);
        _standardProgram = CreateProgram("Model.frag", es);
        try
        {
            _botwProgram = CreateProgram("BOTW.frag", es);
            _totkProgram = CreateProgram("TOTK.frag", es);
            _ootProgram = CreateProgram("OOT.frag", es);
            _wireProgram = CreateProgram("Wireframe.frag", es);
            _particleProgram = CreateProgram("BotwParticle.frag", es);
            _grid = new GridGlRenderer(gl, es);
        }
        catch
        {
            gl.DeleteProgram(_standardProgram);
            if (_botwProgram != 0) gl.DeleteProgram(_botwProgram);
            if (_totkProgram != 0) gl.DeleteProgram(_totkProgram);
            if (_ootProgram != 0) gl.DeleteProgram(_ootProgram);
            if (_wireProgram != 0) gl.DeleteProgram(_wireProgram);
            if (_particleProgram != 0) gl.DeleteProgram(_particleProgram);
            throw;
        }
    }
    private uint CreateProgram(string fragment, bool es)
    {
        var gl = _gl;
        uint vs = Compile(ShaderType.VertexShader, Source(fragment is "TOTK.frag" or "OOT.frag" ? "TOTK.vert" : "Model.vert", es));
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
        if (name is "Model.frag" or "BOTW.frag" or "TOTK.frag" or "OOT.frag")
        {
            string output = name is "TOTK.frag" or "OOT.frag" ? "fragColor" : "FragColor";
            source = Regex.Replace(source, @"\bvoid\s+main\s*\(", "void MaterialMain(");
            // Apply exposure in linear space without changing alpha or the default render.
            source += "\nuniform float ptBrightness;\nvoid main() { MaterialMain(); if(ptBrightness != 1.0) { vec3 c=max(" + output + ".rgb,vec3(0)); " +
                "c=mix(c/12.92,pow((c+0.055)/1.055,vec3(2.4)),step(vec3(0.04045),c))*ptBrightness; " +
                output + ".rgb=mix(12.92*c,1.055*pow(c,vec3(1.0/2.4))-0.055,step(vec3(0.0031308),c)); } }\n";
        }
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

    internal void Draw(VisualModelData? model, ISet<int> hidden, Matrix4x4 camera, Vector3 direction, int width, int height, IReadOnlyList<GridLine>? grid = null, Vector3? background = null, bool emission = false, ModelDisplayMode displayMode = ModelDisplayMode.Textured, bool staticScene = false, Vector3? keyLightOverride = null, Vector3? eyeOverride = null, float clearAlpha = 1, float directIntensity = 1, float ambientIntensity = 1, float brightness = 3, BotwLightingEnvironment? environment = null, bool castShadows = false)
    {
        var gl = _gl;
        brightness = float.IsFinite(brightness) ? Math.Clamp(brightness, 0, 4) : 3;
        gl.Viewport(0, 0, (uint)width, (uint)height); gl.Disable(EnableCap.ScissorTest);
        if (_es) { gl.DepthRange(0f, 1f); gl.ClearDepth(1f); }
        else { gl.DepthRange(0d, 1d); gl.ClearDepth(1d); }
        gl.ColorMask(true, true, true, true); gl.DepthMask(true);
        var clear = background ?? new Vector3(36 / 255f, 40 / 255f, 41 / 255f);
        gl.ClearColor(clear.X, clear.Y, clear.Z, clearAlpha); gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        if (grid != null) _grid.Draw(grid, camera);
        if (!ReferenceEquals(_model, model))
        {
            if (model != null && CanUpdatePose(model)) UpdatePose(model);
            else { ReleaseMeshes(); if (model != null) Upload(model); }
            _model = model;
        }
        if (model == null) return;
        var excluded = new HashSet<int>(hidden);
        for (int i = 0; i < _meshes.Count; i++) if (!excluded.Contains(i) && Outside(_meshes[i], camera)) excluded.Add(i);
        LastVisibleMeshes = _meshes.Count - excluded.Count; LastMaterialUpdates = 0;
        gl.Enable(EnableCap.DepthTest); gl.Disable(EnableCap.CullFace); gl.Disable(EnableCap.Blend);
        if (displayMode == ModelDisplayMode.Wireframe)
        {
            DrawWireframe(excluded, camera, clear);
            return;
        }
        var right = Vector3.Cross(Vector3.UnitY, direction);
        right = right.LengthSquared() > 1e-8f ? Vector3.Normalize(right) : Vector3.UnitX;
        var up = Vector3.Cross(direction, right);
        var keyLight = Vector3.Normalize(Vector3.Normalize(new(.35f, .8f, .45f)) * .28f + Vector3.Normalize(direction * .62f + right * .34f + up * .7f) * .72f);
        if (keyLightOverride is { } suppliedLight && suppliedLight.LengthSquared() > 1e-8f) keyLight = Vector3.Normalize(suppliedLight);
        var fillLight = Vector3.Normalize(direction * .45f - right * .72f + up * .22f);
        IconShadowMap? shadow = null;
        if (castShadows && _meshes.Any(m => m.Source.Material.Botw != null || m.Source.Material.Totk is { IsOot: false }))
        {
            var target = _iconShadow ??= new IconShadowMap(gl);
            int framebuffer = gl.GetInteger(GLEnum.FramebufferBinding);
            var originalCamera = camera; var originalDirection = direction;
            var originalExcluded = excluded;
            int originalWidth = width, originalHeight = height;
            try
            {
                target.Begin(model.BoundsMinimum, model.BoundsMaximum, keyLight);
                // The comparison filter spans neighbouring texels. Offset caster
                // depth by its raster slope so grazing faces do not shadow themselves.
                gl.Enable(EnableCap.PolygonOffsetFill);
                gl.PolygonOffset(1, 1);
                camera = target.Camera; direction = keyLight; width = height = IconShadowMap.Size;
                SetShadowPositions(true);
                excluded = new HashSet<int>(hidden);
                _program = 0;
                // Reuse each material's alpha test, including compatibility materials.
                // Keep shadow null here to avoid sampling the render attachment.
                DrawMeshes(Enumerable.Range(0, _meshes.Count).Where(i => !SceneTranslucent(_meshes[i].Source)), true);
            }
            finally
            {
                SetShadowPositions(false);
                camera = originalCamera; direction = originalDirection; width = originalWidth; height = originalHeight;
                excluded = originalExcluded;
                gl.Disable(EnableCap.PolygonOffsetFill);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
                gl.Viewport(0, 0, (uint)width, (uint)height);
                gl.ColorMask(true, true, true, true); gl.Disable(EnableCap.CullFace);
            }
            shadow = target;
        }
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
            if (staticScene)
            {
                // Seals are coplanar decorative layers, not occluders. Including
                // them in the character depth prepass punches holes in walls.
                gl.DepthFunc(DepthFunction.Lequal);
                DrawMeshes(_sceneOrder.Where(i => _meshes[i].Source.Material.Totk is not { IsSeal: true } && !SceneTranslucent(_meshes[i].Source)), true);
                gl.DepthMask(false); gl.Enable(EnableCap.Blend);
                gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                gl.Enable(EnableCap.PolygonOffsetFill); gl.PolygonOffset(-1f, -2f);
                DrawMeshes(Enumerable.Range(0, _meshes.Count).Where(i => _meshes[i].Source.Material.Totk?.IsSeal == true)
                    .OrderBy(i => _meshes[i].Source.Material.Totk!.Priority));
                gl.Disable(EnableCap.PolygonOffsetFill);
                DrawMeshes(Enumerable.Range(0, _meshes.Count).Where(i => _meshes[i].Source.Material.Totk is not { IsSeal: true } && SceneTranslucent(_meshes[i].Source))
                    .OrderBy(i => _meshes[i].Source.Material.Totk?.Priority ?? 0).ThenByDescending(SceneDepth));
            }
            else
            {
                gl.DepthFunc(DepthFunction.Less); gl.ColorMask(false, false, false, false);
                DrawMeshes(Enumerable.Range(0, _meshes.Count).Where(i => _meshes[i].Source.Material.Particle == null && _meshes[i].Source.Material.Totk is not { IsTranslucent: true } && _meshes[i].Source.Material.Botw is not { Translucent: true }));
                gl.ColorMask(true, true, true, true); gl.DepthMask(false); gl.DepthFunc(DepthFunction.Lequal);
                gl.Enable(EnableCap.Blend); gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                if (clearAlpha == 0) gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                DrawMeshes(Enumerable.Range(0, _meshes.Count).OrderBy(i => _meshes[i].Source.Material.Particle != null ? 2 : _meshes[i].Source.Material.Totk is { IsTranslucent: true } || _meshes[i].Source.Material.Botw is { Translucent: true } ? 1 : 0));
            }
        }
        finally
        {
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Disable(EnableCap.CullFace);
        }
        if (edgedFaces)
        {
            gl.Disable(EnableCap.Blend);
            DrawWireframe(excluded, camera, clear, overlay: true);
        }
        gl.DepthMask(true); gl.Disable(EnableCap.Blend); gl.Disable(EnableCap.DepthTest); gl.BindVertexArray(0); gl.UseProgram(0);

        float SceneDepth(int index)
        {
            var mesh = _meshes[index];
            var clip = Vector4.Transform(new Vector4((mesh.Minimum + mesh.Maximum) * .5f, 1), camera);
            return clip.W;
        }

        void DrawMeshes(IEnumerable<int>? drawOrder = null, bool writeDepth = false)
        {
            object? lastTotkMaterial = null;
            IEnumerable<int> order = drawOrder ?? Enumerable.Range(0, _meshes.Count);
            foreach (int index in order)
            {
                if (excluded.Contains(index)) continue;
                var gpu = _meshes[index]; var m = gpu.Source.Material;
                if (staticScene)
                {
                    gl.DepthMask(writeDepth && (m.Totk?.DepthWrite ?? true));
                    if (m.Totk?.DisplayFace is "front" or "back")
                    {
                        gl.Enable(EnableCap.CullFace); gl.FrontFace(FrontFaceDirection.Ccw);
                        gl.CullFace(m.Totk.DisplayFace == "front" ? TriangleFace.Back : TriangleFace.Front);
                    }
                    else gl.Disable(EnableCap.CullFace);
                }
                if (m.Totk is { IsOot: true }) gl.BlendFunc(m.Totk.Additive ? BlendingFactor.One : BlendingFactor.SrcAlpha,
                    m.Totk.Additive ? BlendingFactor.One : BlendingFactor.OneMinusSrcAlpha);
                else if (clearAlpha == 0) gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                else gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                if (m.Particle is { } particle)
                {
                    if (particle.Alpha <= 0) continue;
                    _program = _particleProgram; gl.UseProgram(_program);
                    M("uMvp", camera); M("uModel", Matrix4x4.Identity);
                    gl.Disable(EnableCap.CullFace);
                    void Four(string name, Vector4 value) => gl.Uniform4(Location(name), value.X, value.Y, value.Z, value.W);
                    Four("uParticleColor0", particle.Color0); Four("uParticleColor1", particle.Color1);
                    Four("uTexture1Srt", particle.Texture1Srt); F("uParticleAlpha", particle.Alpha);
                    for (int t = 0; t < 2; t++)
                    {
                        gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + t));
                        gl.BindTexture(TextureTarget.Texture2D, gpu.Textures[t]); I(t == 0 ? "uMask" : "uNoise", t);
                    }
                    gl.BindVertexArray(gpu.Vao); gl.DrawElements(PrimitiveType.Triangles, (uint)gpu.Source.Indices.Length, DrawElementsType.UnsignedInt, null);
                    lastTotkMaterial = null; LastMaterialUpdates++;
                    continue;
                }
                if (m.Totk is {} nativeTotk && TotkShaderSource.Supports(nativeTotk))
                {
                    if (nativeTotk.DisplayFace is "front" or "back")
                    {
                        gl.Enable(EnableCap.CullFace); gl.FrontFace(FrontFaceDirection.Ccw);
                        gl.CullFace(nativeTotk.DisplayFace == "front" ? TriangleFace.Back : TriangleFace.Front);
                    }
                    else gl.Disable(EnableCap.CullFace);
                    _totkNative.Bind(nativeTotk, gpu.Textures, camera, direction, (model.BoundsMinimum + model.BoundsMaximum) * .5f,
                        Vector3.Distance(model.BoundsMinimum, model.BoundsMaximum), keyLight, width, height, emission,
                        directIntensity, ambientIntensity, brightness, environment, shadow);
                    gl.BindVertexArray(gpu.Vao); gl.DrawElements(PrimitiveType.Triangles, (uint)gpu.Source.Indices.Length, DrawElementsType.UnsignedInt, null);
                    _program = 0; lastTotkMaterial = null; LastMaterialUpdates++;
                    continue;
                }
                if (m.Botw is {} botw && m.Totk == null)
                {
                    if (botw.DisplayFace is "front" or "back")
                    {
                        gl.Enable(EnableCap.CullFace); gl.FrontFace(FrontFaceDirection.Ccw);
                        gl.CullFace(botw.DisplayFace == "front" ? TriangleFace.Back : TriangleFace.Front);
                    }
                    else gl.Disable(EnableCap.CullFace);
                    _botwNative.Bind(botw, gpu.Textures, camera, direction, (model.BoundsMinimum + model.BoundsMaximum) * .5f,
                        Vector3.Distance(model.BoundsMinimum, model.BoundsMaximum), keyLight, width, height, emission, directIntensity, ambientIntensity, brightness, environment, shadow);
                    gl.VertexAttrib4(9, 1f, 1f, 1f, 1f);
                    gl.BindVertexArray(gpu.Vao); gl.DrawElements(PrimitiveType.Triangles, (uint)gpu.Source.Indices.Length, DrawElementsType.UnsignedInt, null);
                    gl.Disable(EnableCap.CullFace);
                    _program = 0; lastTotkMaterial = null; LastMaterialUpdates++;
                    continue;
                }
                var program = m.Totk is { IsOot: true } ? _ootProgram : m.Totk != null ? _totkProgram : m.UseBotwPreviewShader ? _botwProgram : _standardProgram;
                if (_program != program)
                {
                    lastTotkMaterial = null;
                    _program = program; gl.UseProgram(program);
                    F("ptBrightness", brightness);
                    M("uMvp", camera); M("uModel", Matrix4x4.Identity); F("uOpacity", 1);
                    V("uViewDirection", direction); V("uKeyLight", keyLight); V("uFillLight", fillLight);
                    var center = (model.BoundsMinimum + model.BoundsMaximum) * .5f;
                    var eye = eyeOverride ?? center + direction * Math.Max(1, Vector3.Distance(model.BoundsMinimum, model.BoundsMaximum) * 2);
                    V("cameraPosition", eye); M("ootView", Matrix4x4.CreateLookAt(eye, center, Math.Abs(direction.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY));
                }
                if (m.Totk is { } totk)
                {
                    if (!ReferenceEquals(lastTotkMaterial, m))
                    {
                    LastMaterialUpdates++;
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
                        gl.BindTexture(TextureTarget.Texture2D, gpu.Textures[t]); I(totk.IsOot ? $"ootTex{t}" : $"Texture{t}", t);
                    }
                    lastTotkMaterial = m;
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

    internal static bool SceneTranslucent(VisualModelMesh mesh) => mesh.Material.Particle != null || (mesh.Material.Totk?.IsTranslucent ?? mesh.Material.Botw?.Translucent
        ?? (mesh.Texture?.AlphaMode == VisualModelAlphaMode.Blend || mesh.Material.Tint.W < 1));

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
            if (ReferenceEquals(gpu.Source, source)) continue;
            var vertices = gpu.PackedPose;
            if (vertices == null || !ModelVertices.UpdateOotPose(gpu.Source, source, vertices)) vertices = ModelVertices.Build(source);
            gpu.PackedPose = source.Material.Totk?.IsOot == true ? vertices : null;
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, gpu.Vbo);
            fixed (float* v = vertices) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * 4), v, BufferUsageARB.DynamicDraw);
            _meshes[i] = WithBounds(gpu with { Source = source });
            UploadShadowPositions(_meshes[i]);
        }
    }
    private void Upload(VisualModelData model)
    {
        foreach (var source in model.Meshes)
        {
            var gl = _gl; var vao = gl.GenVertexArray(); var vbo = gl.GenBuffer(); var ebo = gl.GenBuffer();
            var gpu = WithBounds(new GpuMesh(vao, vbo, ebo, source, new uint[8])); _meshes.Add(gpu);
            gl.BindVertexArray(vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
            var vertices = ModelVertices.Build(source);
            gpu.PackedPose = source.Material.Totk?.IsOot == true ? vertices : null;
            fixed (float* v = vertices) gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * 4), v, BufferUsageARB.StaticDraw);
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, ebo);
            fixed (uint* i = source.Indices) gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(source.Indices.Length * 4), i, BufferUsageARB.StaticDraw);
            bool extended = source.Material.Totk != null || source.Material.Botw != null;
            int[] sizes = !extended ? [3, 3, 2, 2, 2, 2, 2, 4] : [3, 3, 2, 2, 2, 2, 2, 4, 4]; int offset = 0;
            uint stride = !extended ? 80u : 96u;
            for (uint a = 0; a < sizes.Length; a++) { gl.VertexAttribPointer(a, sizes[a], VertexAttribPointerType.Float, false, stride, (void*)(offset * 4)); gl.EnableVertexAttribArray(a); offset += sizes[a]; }
            UploadShadowPositions(gpu);
            VisualModelTexture?[] textures = source.Material.Totk?.Textures ?? source.Material.Botw?.Textures ?? [source.Texture ?? (source.UseMissingTextureFallback ? ModelTextures.Default : null), source.SpecularTexture, source.NormalTexture, source.AmbientOcclusionTexture, source.AuxiliaryTexture, source.EyeGlintTexture];
            for (int t = 0; t < textures.Length; t++) if (textures[t] is { } texture)
            {
                var native = source.Material.Totk == null ? source.Material.Botw : null;
                var nativeTotk = source.Material.Totk is {} totk && TotkShaderSource.Supports(totk) ? totk : null;
                gpu.Textures[t] = UploadTexture(texture, nativeTotk != null ? texture.IsSrgb || (nativeTotk.ColorTextures[t] && !texture.IsSingleChannel) : native != null && texture.IsSrgb,
                    native?.Samplers[t] ?? nativeTotk?.Samplers[t] ?? (source.Material.Particle is { } particle && t < particle.Samplers.Length ? particle.Samplers[t] : null));
            }
        }
        _sceneOrder = Enumerable.Range(0, _meshes.Count).GroupBy(i => _meshes[i].Source.Material, ReferenceEqualityComparer.Instance).SelectMany(g => g).ToArray();
    }
    private void UploadShadowPositions(GpuMesh gpu)
    {
        var offsets = gpu.Source.ShadowOffsets;
        if (offsets.Length == 0)
        {
            if (gpu.ShadowVbo != 0) _gl.DeleteBuffer(gpu.ShadowVbo);
            gpu.ShadowVbo = 0;
            return;
        }
        if (offsets.Length != gpu.Source.Vertices.Length / 8) throw new InvalidDataException("Shadow vertex count mismatch: " + gpu.Source.Name);
        var positions = new float[offsets.Length * 3];
        for (int i = 0; i < offsets.Length; i++)
        {
            positions[i * 3] = gpu.Source.Vertices[i * 8] + offsets[i].X;
            positions[i * 3 + 1] = gpu.Source.Vertices[i * 8 + 1] + offsets[i].Y;
            positions[i * 3 + 2] = gpu.Source.Vertices[i * 8 + 2] + offsets[i].Z;
        }
        if (gpu.ShadowVbo == 0) gpu.ShadowVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, gpu.ShadowVbo);
        fixed (float* p = positions) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(positions.Length * 4), p, BufferUsageARB.DynamicDraw);
    }
    private void SetShadowPositions(bool shadow)
    {
        foreach (var gpu in _meshes.Where(m => m.ShadowVbo != 0))
        {
            _gl.BindVertexArray(gpu.Vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, shadow ? gpu.ShadowVbo : gpu.Vbo);
            uint stride = shadow ? 12u : gpu.Source.Material.Totk != null || gpu.Source.Material.Botw != null ? 96u : 80u;
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, null);
        }
    }
    private uint UploadTexture(VisualModelTexture texture, bool srgb = false, BotwPreviewSampler? sampler = null)
    {
        if (_textures.TryGetValue((texture, srgb, sampler), out var existing)) return existing;
        var gl = _gl; var id = gl.GenTexture(); _textures.Add((texture, srgb, sampler), id);
        gl.BindTexture(TextureTarget.Texture2D, id); gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* pixels = texture.Pixels) gl.TexImage2D(TextureTarget.Texture2D, 0, srgb ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba, (uint)texture.Width, (uint)texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        if (sampler != null)
        {
            GLEnum Wrap(int mode) => mode == 0 ? GLEnum.Repeat : mode == 1 ? GLEnum.MirroredRepeat : GLEnum.ClampToEdge;
            var min = sampler.MipFilter == 0 ? (sampler.MinLinear ? GLEnum.Linear : GLEnum.Nearest) :
                sampler.MipFilter == 1 ? (sampler.MinLinear ? GLEnum.LinearMipmapNearest : GLEnum.NearestMipmapNearest) :
                sampler.MinLinear ? GLEnum.LinearMipmapLinear : GLEnum.NearestMipmapLinear;
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)min);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)(sampler.MagLinear ? GLEnum.Linear : GLEnum.Nearest));
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)Wrap(sampler.WrapU));
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)Wrap(sampler.WrapV));
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinLod, sampler.MinLod);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLod, sampler.MaxLod);
        }
        gl.GenerateMipmap(TextureTarget.Texture2D); return id;
    }
    private void ReleaseMeshes()
    {
        _botwNative.ReleaseMaterials();
        _totkNative.ReleaseMaterials();
        foreach (var m in _meshes) { _gl.DeleteVertexArray(m.Vao); _gl.DeleteBuffer(m.Vbo); _gl.DeleteBuffer(m.Ebo); if (m.WireEbo != 0) _gl.DeleteBuffer(m.WireEbo); if (m.ShadowVbo != 0) _gl.DeleteBuffer(m.ShadowVbo); }
        foreach (var t in _textures.Values) _gl.DeleteTexture(t);
        _meshes.Clear(); _textures.Clear();
    }
    public void Dispose() { ReleaseMeshes(); _iconShadow?.Dispose(); _grid.Dispose(); _botwNative.Dispose(); _totkNative.Dispose(); _gl.DeleteProgram(_standardProgram); _gl.DeleteProgram(_botwProgram); _gl.DeleteProgram(_totkProgram); _gl.DeleteProgram(_ootProgram); _gl.DeleteProgram(_wireProgram); _gl.DeleteProgram(_particleProgram); }
}
