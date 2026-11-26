using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop.Rendering;
using Silk.NET.OpenGL;
using System.Numerics;

internal sealed class BotwShaderGpuChecks : Application
{
    internal static int Result = 1;
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var surface = new Surface();
        lifetime.MainWindow = new Window { Width = 160, Height = 160, ShowInTaskbar = false, Content = surface };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        timer.Tick += (_, _) => Finish("BotW shader GPU check timed out."); timer.Start();
        surface.Finished += Finish;
        void Finish(string? error)
        {
            timer.Stop(); Result = error == null ? 0 : 1;
            if (error != null) Console.Error.WriteLine(error);
            lifetime.Shutdown(Result);
        }
        base.OnFrameworkInitializationCompleted();
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
                var native = new BotwPreviewMaterial();
                native.Options["uking_albedo_color"] = 100;
                native.Options["uking_enable_normalmap"] = 0;
                native.Parameters["const_color0"] = [1, .1f, .05f, 1];
                var mesh = new VisualModelMesh {
                    Vertices = [-1,-1,0, 0,0,1, 0,0, 3,-1,0, 0,0,1, 1,0, -1,3,0, 0,0,1, 0,1],
                    Indices = [0,1,2], Material = new() { Botw = native }
                };
                var model = new VisualModelData { BoundsMinimum = new(-1,-1,0), BoundsMaximum = new(3,3,0) };
                model.Meshes.Add(mesh);
                byte[] Sample(bool emission = false)
                {
                    renderer.Draw(model, new HashSet<int>(), Matrix4x4.Identity, Vector3.UnitZ, 32, 32, background: Vector3.Zero, emission: emission, brightness: 1);
                    var pixel = new byte[4]; fixed(byte* p = pixel) gl.ReadPixels(8,8,1,1,PixelFormat.Rgba,PixelType.UnsignedByte,p);
                    var error = gl.GetError(); if (error != GLEnum.NoError) throw new Exception("BotW GPU error: " + error);
                    return pixel;
                }
                foreach (int behavior in new[] { 0, 2, 100, 101, 104 })
                {
                    native.Options["uking_material_behave"] = behavior;
                    byte[] red = Sample();
                    if (red.Take(3).Max() < 10) throw new Exception($"BotW behavior {behavior} rendered blank: {string.Join(',',red)}");
                    if (behavior == 104) { Console.WriteLine("PASS: native eye material compiles and renders."); continue; }
                    native.Parameters["const_color0"] = [.05f, .1f, 1, 1];
                    byte[] blue = Sample();
                    if (red.SequenceEqual(blue)) throw new Exception("BotW authored material constants did not affect pixels.");
                    native.Parameters["const_color0"] = [1, .1f, .05f, 1];
                    if (!red.SequenceEqual(Sample())) throw new Exception("BotW material restoration changed pixels.");
                    Console.WriteLine($"PASS: BotW behavior {behavior}, nonblank GPU render, live constants and exact restoration ({string.Join(',',red)}).");
                }
                native.Options["uking_material_behave"] = 0;
                native.Options["gsys_alpha_test_enable"] = 1;
                native.Options["gsys_alpha_test_func"] = 6;
                native.Options["uking_transparency_color"] = 104;
                native.Options["uking_transparency_channel"] = 0;
                native.Parameters["gsys_alpha_test_ref_value"] = [.5f];
                native.Parameters["const_value0"] = [0];
                if (Sample().Take(3).Any(v => v > 0)) throw new Exception("Alpha test failed to discard.");
                native.Parameters["const_value0"] = [1];
                if (Sample().Take(3).Max() < 10) throw new Exception("Alpha test discarded opaque material.");
                Console.WriteLine("PASS: native alpha cutout and opaque restoration.");
                native.Options["gsys_alpha_test_enable"] = 0;
                native.Options["uking_albedo_color"] = 0;
                native.Options["uking_texcoord0_srt"] = 0;
                native.Textures[0] = new() { Width = 2, Height = 1, Pixels = [255,0,0,255, 0,0,255,255] };
                mesh.UvSets.Add(new() { Name = "_u0", Coordinates = [.25f,.5f, .25f,.5f, .25f,.5f] });
                mesh.UvSets.Add(new() { Name = "_u1", Coordinates = [.75f,.5f, .75f,.5f, .75f,.5f] });
                renderer.Draw(null, new HashSet<int>(), Matrix4x4.Identity, Vector3.UnitZ, 32, 32);
                var uv0 = Sample();
                native.Options["uking_texcoord0_mapping"] = 1;
                var uv1 = Sample();
                if (uv0[0] <= uv0[2] || uv1[2] <= uv1[0]) throw new Exception("Native UV selection failed.");
                native.Options["uking_texcoord0_mapping"] = 0;
                native.Parameters["tex_srt0"] = [1,0,0,1,.5f,0,0,0];
                var transformed = Sample();
                if (!transformed.SequenceEqual(uv1)) throw new Exception("Native SRT did not match UV1 sampling.");
                native.Parameters["tex_srt0"] = [1,0,0,1,0,0,0,0];
                if (!Sample().SequenceEqual(uv0)) throw new Exception("Native UV restoration changed pixels.");
                Console.WriteLine("PASS: native sampler, UV0/UV1 routing and texture SRT pixel equivalence.");
                native.Options["uking_enable_emission"] = 1;
                native.Options["uking_emission_color"] = 101;
                native.Parameters["const_color1"] = [0,1,0,1];
                if (Sample(true)[1] <= Sample()[1] + 20) throw new Exception("Emission toggle did not change pixels.");
                Console.WriteLine("PASS: native emission toggle changes rendered pixels.");
                native.Options["uking_enable_emission"] = 0;
                native.Options["uking_enable_specular"] = 0;
                native.Options["uking_albedo_color"] = 100;
                float encoded = 128f / 255;
                float linear = MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
                native.Parameters["const_color0"] = [linear, linear, linear, 1];
                var reference = Sample();
                byte[] TextureSample(bool srgb)
                {
                    native.Options["uking_albedo_color"] = 0;
                    native.Textures[0] = new() { Width = 1, Height = 1, Pixels = [128,128,128,255], IsSrgb = srgb };
                    renderer.Draw(null, new HashSet<int>(), Matrix4x4.Identity, Vector3.UnitZ, 32, 32);
                    return Sample();
                }
                var srgbPixel = TextureSample(true);
                var linearPixel = TextureSample(false);
                if (Enumerable.Range(0, 3).Any(i => Math.Abs(srgbPixel[i] - reference[i]) > 2))
                    throw new Exception("sRGB texture does not match its decoded linear material constant.");
                if (linearPixel[0] < srgbPixel[0] + 30)
                    throw new Exception("Linear data texture was incorrectly sRGB-decoded.");
                Console.WriteLine($"PASS: sRGB midgray matches linear reference ({string.Join(',', srgbPixel)}); linear data remains unchanged ({string.Join(',', linearPixel)}).");
                CaptureEdgeChecks.Run(gl, context.Version?.Contains("OpenGL ES") == true);
                Dispatcher.UIThread.Post(() => Finished?.Invoke(null));
            }
            catch (Exception e) { Dispatcher.UIThread.Post(() => Finished?.Invoke(e.ToString())); }
        }
    }
}
