using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using Silk.NET.OpenGL;
using System.Numerics;

internal sealed class TotkNativeGpuChecks : Application
{
    internal static string Root = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() { Styles.Add(new FluentTheme()); RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; }
    public override void OnFrameworkInitializationCompleted()
    {
        TotkMaterialChecks.Run();
        BindingChecks();
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var surface = new Surface();
        var testWindow = new Window { Width = 160, Height = 160, ShowInTaskbar = false, Content = surface };
        lifetime.MainWindow = testWindow;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(240) };
        timeout.Tick += (_, _) => Finish("TotK native GPU test timeout"); timeout.Start();
        void Finish(string? error)
        {
            timeout.Stop(); Result = error == null ? 0 : 1;
            if (error != null) Console.Error.WriteLine(error);
            lifetime.Shutdown(Result);
        }
        surface.Finished += async error =>
        {
            if (error != null || Root.Length == 0) { Finish(error); return; }
            var window = new MainWindow { Width = 1000, Height = 850, ShowInTaskbar = false };
            window.ModelSurface!.RenderFailed += Finish;
            lifetime.MainWindow = window; window.Show(); testWindow.Close();
            try
            {
                Directory.CreateDirectory(Output);
                foreach (string actor in new[] { "Player", "Armor_001_Upper", "Weapon_Sword_070", "Weapon_Shield_030", "Enemy_Bokoblin_Junior" })
                {
                    await window.LoadActorPathAsync(Path.Combine(Root, "Pack", "Actor", actor + ".pack.zs"), [Root]);
                    var model = window.Viewport.Model ?? throw new Exception(actor + " has no model");
                    var materials = model.Meshes.Select(m => m.Material.Totk).Where(m => m != null).Distinct().ToArray();
                    File.WriteAllText(Path.Combine(Output, actor + "-materials.json"), System.Text.Json.JsonSerializer.Serialize(model.Meshes.Select(m => new {
                        m.Name, Options = m.Material.Totk?.Options, Parameters = m.Material.Totk?.Parameters,
                        Textures = m.Material.Totk?.Textures.Select(t => t == null ? null : new { t.Name, t.IsSrgb, t.IsSingleChannel })
                    }), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"{actor}: {materials.Length} materials, {materials.Count(m => TotkShaderSource.Supports(m!))} native, {materials.Count(m => !TotkShaderSource.Supports(m!))} compatibility fallbacks.");
                    if (!materials.Any(m => TotkShaderSource.Supports(m!))) throw new Exception(actor + " did not select Killz's shader");
                    window.Viewport.ShowEmission = true;
                    window.Viewport.SetView(CameraView.Front, false); window.Viewport.FrameModel();
                    if (actor.StartsWith("Weapon_Sword_", StringComparison.Ordinal))
                    { window.Viewport.Orbit(.65f, 1.1f); window.Viewport.FrameModel(); }
                    await Capture(actor + ".png");
                    if (actor == "Player")
                    {
                        window.Viewport.Orbit(.6f, .1f); await Capture(actor + "-angle.png");
                        var face = model.Meshes.First(m => m.Name.StartsWith("Face__", StringComparison.Ordinal));
                        var points = Enumerable.Range(0, face.Vertices.Length / 8).Select(i => new Vector3(face.Vertices[i*8], face.Vertices[i*8+1], face.Vertices[i*8+2])).ToArray();
                        window.Viewport.ModelSelectionBounds = (points.Aggregate(Vector3.Min), points.Aggregate(Vector3.Max));
                        window.Viewport.FrameSelected(); await Task.Delay(400); await Capture(actor + "-face.png");
                    }
                    Console.WriteLine("PASS GPU: " + actor);
                }
                Finish(null);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
            async Task Capture(string name)
            {
                var complete = new TaskCompletionSource<byte[]>();
                void Receive(byte[] pixels, int width, int height)
                {
                    window.ModelSurface.FrameCaptured -= Receive;
                    complete.TrySetResult(IconToolView.Encode(pixels, width, height, true));
                }
                window.ModelSurface.FrameCaptured += Receive; window.ModelSurface.CaptureNextFrame = true;
                window.ModelSurface.RequestNextFrameRendering();
                byte[] png = await complete.Task;
                File.WriteAllBytes(Path.Combine(Output, name), png);
                using var bitmap = SkiaSharp.SKBitmap.Decode(png);
                int colored = bitmap.Pixels.Count(p => Math.Max(p.Red, Math.Max(p.Green, p.Blue)) - Math.Min(p.Red, Math.Min(p.Green, p.Blue)) > 15);
                if (colored < 100) throw new Exception(name + " is blank/desaturated");
            }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static void BindingChecks()
    {
        var source = new Syroot.NintenTools.NSW.Bfres.Material {
            ShaderAssign = new() { ShaderArchiveName = "material.Product.110", ShaderOptionDict = new(), ShaderOptions = [] },
            ShaderParams = [], ShaderParamData = [], TextureRefs = [], SamplerDict = new()
        };
        void Option(string name, string value) { source.ShaderAssign.ShaderOptionDict.Add(name); source.ShaderAssign.ShaderOptions.Add(value); }
        Option("o_albedo_color", "200"); Option("o_expression_pre_normal", "(2583175971)");
        Option("o_enable_normalmap", "true");
        var mesh = new VisualModelMesh();
        ToolboxTotkMaterial.Apply(source, mesh, new());
        var material = mesh.Material.Totk!;
        if (!material.Native || material.Options["o_expression_pre_normal"] != 2583175971L || material.Options["o_enable_normalmap"] != 1)
            throw new Exception("TotK native option parsing lost unsigned graph IDs or boolean options");
        if (!material.ColorTextures[0] || material.ColorTextures[2] || !TotkShaderSource.Supports(material))
            throw new Exception("TotK native color/normal classification or known graph selection failed");
        material.Options["o_expression_pre_normal"] = 2933587635;
        if (TotkShaderSource.Supports(material)) throw new Exception("Unknown consumed graph must use compatibility rendering");
        material.Options["o_albedo_color"] = 0;
        if (!TotkShaderSource.Supports(material)) throw new Exception("Unused graph disabled native rendering");
        if (TotkShaderSource.Supports(new() { Native = true, IsOot = true })) throw new Exception("OoT selected TotK native rendering");
        Console.WriteLine("PASS: TotK native binding, unsigned graph IDs, color roles and compatibility routing.");
    }
    private sealed class Surface : OpenGlControlBase
    {
        internal event Action<string?>? Finished;
        private bool _done;
        protected override unsafe void OnOpenGlRender(GlInterface context, int framebuffer)
        {
            if (_done) return; _done = true;
            try
            {
                using var gl = GL.GetApi(context.GetProcAddress);
                using var renderer = new ModelGlRenderer(gl, context.Version?.Contains("OpenGL ES") == true);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);
                var native = new TotkPreviewMaterial { Native = true };
                native.Options["o_albedo_color"] = 0;
                native.Options["o_enable_texcoord0"] = 1;
                native.Options["o_texcoord0_srt"] = 0;
                native.Textures[0] = new() { Width = 2, Height = 1, Pixels = [255,0,0,255, 0,0,255,255] };
                native.Samplers[0] = new(0, 0, false, false, 0);
                var mesh = new VisualModelMesh {
                    Vertices = [-1,-1,0, 0,0,1, .25f,.5f, 3,-1,0, 0,0,1, .25f,.5f, -1,3,0, 0,0,1, .25f,.5f],
                    Indices = [0,1,2], Material = new() { Totk = native }
                };
                mesh.UvSets.Add(new() { Name = "_u0", Coordinates = [.25f,.5f,.25f,.5f,.25f,.5f] });
                mesh.UvSets.Add(new() { Name = "_u1", Coordinates = [.75f,.5f,.75f,.5f,.75f,.5f] });
                var model = new VisualModelData { BoundsMinimum = new(-1,-1,0), BoundsMaximum = new(3,3,0) }; model.Meshes.Add(mesh);
                byte[] Sample(bool emission = false)
                {
                    renderer.Draw(model, new HashSet<int>(), Matrix4x4.Identity, Vector3.UnitZ, 32, 32, background: Vector3.Zero, emission: emission, brightness: 1);
                    var pixel = new byte[4]; fixed(byte* p = pixel) gl.ReadPixels(8,8,1,1,PixelFormat.Rgba,PixelType.UnsignedByte,p);
                    var error = gl.GetError(); if (error != GLEnum.NoError) throw new Exception("TotK GPU: " + error);
                    return pixel;
                }
                foreach (int behavior in new[] { 0, 1, 2, 100, 101, 104 })
                {
                    native.Options["o_material_behave"] = behavior;
                    var red = Sample();
                    if (red[0] < 20 || red[0] <= red[2]) throw new Exception($"Behavior {behavior} wrong color: {string.Join(',',red)}");
                    native.Options["o_texcoord0_mapping"] = 1;
                    var blue = Sample();
                    if (blue[2] < 20 || blue[2] <= blue[0]) throw new Exception("UV1 failed");
                    native.Options["o_texcoord0_mapping"] = 0;
                    native.Parameters["p_tex_srt0"] = [1,0,0,1,.5f,0,0,0];
                    if (!Sample().SequenceEqual(blue)) throw new Exception("Native SRT mismatch");
                    native.Parameters.Remove("p_tex_srt0");
                    if (!Sample().SequenceEqual(red)) throw new Exception("Default SRT restoration failed");
                }
                native.Options["o_material_behave"] = 0;
                native.Options["o_enable_emission"] = 1; native.Options["o_emission_color"] = 402;
                if (Sample(true)[1] < Sample()[1] + 30) throw new Exception("Emission toggle failed");
                native.Options["gsys_alpha_test_enable"] = 1; native.Options["gsys_alpha_test_func"] = 6;
                native.Options["o_transparency_color"] = 400;
                if (Sample().Take(3).Any(v => v > 0)) throw new Exception("Alpha cutout failed");
                native.Options["o_transparency_color"] = 402;
                if (Sample()[0] < 20) throw new Exception("Alpha restoration failed");
                native.Options["gsys_alpha_test_enable"] = 0;
                native.Options["o_enable_emission"] = 0;
                native.Options["o_albedo_color"] = 0;
                native.Options["o_metal_color"] = 400;
                native.Options["o_transmission_color"] = 400;
                native.Textures[0] = new() { Width = 1, Height = 1, Pixels = [80,40,20,255] };
                var blocker = new VisualModelMesh {
                    Vertices = [-.2f,-.2f,.4f, 0,0,1, 0,0, .5f,-.2f,.4f, 0,0,1, 0,0, -.2f,.5f,.4f, 0,0,1, 0,0],
                    Indices = [0,1,2], Material = new() { Totk = native }
                };
                model = new VisualModelData { BoundsMinimum = new(-1,-1,0), BoundsMaximum = new(3,3,1) };
                model.Meshes.Add(mesh); model.Meshes.Add(blocker);
                byte[] ShadowSample(bool shadows, bool hideBlocker = false, BotwLightingEnvironment? palette = null, float direct = 1)
                {
                    renderer.Draw(model, hideBlocker ? new HashSet<int> { 1 } : new(), Matrix4x4.Identity, Vector3.UnitZ, 32,32,
                        background: Vector3.Zero, brightness: 1, keyLightOverride: Vector3.Normalize(new(1,1,1)), castShadows: shadows, environment: palette, directIntensity: direct);
                    var pixels = new byte[32*32*4]; fixed(byte* p = pixels) gl.ReadPixels(0,0,32,32,PixelFormat.Rgba,PixelType.UnsignedByte,p);
                    if (gl.GetError() != GLEnum.NoError) throw new Exception("Native shadow GPU error");
                    return pixels;
                }
                var unshadowed = ShadowSample(false); var shadowed = ShadowSample(true);
                int darker = Enumerable.Range(0,32*32).Count(i => shadowed[i*4] + 10 < unshadowed[i*4]);
                Console.WriteLine($"Shadow check: darker={darker}, changed={unshadowed.Zip(shadowed).Count(p => p.First != p.Second)}, max={unshadowed.Max()}/{shadowed.Max()}");
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "totk-shadow-off.png"), IconToolView.Encode(unshadowed,32,32,true));
                File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "totk-shadow-on.png"), IconToolView.Encode(shadowed,32,32,true));
                if (darker < 3) throw new Exception("TotK native cast shadow did not darken the receiver");
                if (!ShadowSample(false,true).SequenceEqual(ShadowSample(true,true))) throw new Exception("Hidden blocker still casts a shadow or receiver self-shadows");
                if (unshadowed.SequenceEqual(ShadowSample(false, palette: BotwLightingEnvironment.IconCapture))) throw new Exception("TotK lighting palette did not affect pixels");
                native.Options["o_specmask_scaler"] = 400;
                var iconFill = ShadowSample(false, true, BotwLightingEnvironment.IconCapture, direct: 0);
                for (int channel = 0; channel < 3; channel++)
                    if (Math.Abs(iconFill[(4*32+4)*4+channel] - iconFill[(24*32+4)*4+channel]) > 1)
                        throw new Exception("TotK icon palette painted a screen-space ambient gradient");
                Console.WriteLine("PASS: TotK icon fill is independent of vertical screen position.");
                Console.WriteLine("PASS: TotK native shadows, hidden casters and lighting palette.");
                Console.WriteLine("PASS: Killz TotK six material behaviors, UV routing, native SRT/default restoration, emission and alpha test.");
                Dispatcher.UIThread.Post(() => Finished?.Invoke(null));
            }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => Finished?.Invoke(ex.ToString())); }
        }
    }
}
