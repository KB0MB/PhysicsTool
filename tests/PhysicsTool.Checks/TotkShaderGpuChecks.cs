using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using PhysicsTool.Desktop.Rendering;
using Silk.NET.OpenGL;

internal sealed class TotkShaderGpuChecks : Application
{
    internal static string ToolboxShader = "";
    internal static int Result = 1;
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var view = new CheckSurface();
        lifetime.MainWindow = new Window { Width = 160, Height = 160, ShowInTaskbar = false, Content = view };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) => Finish("Shader GPU test timed out"); timer.Start();
        view.Finished += Finish;
        void Finish(string? error)
        {
            timer.Stop(); Result = error == null ? 0 : 1;
            if (error != null) Console.Error.WriteLine(error);
            lifetime.Shutdown(Result);
        }
        base.OnFrameworkInitializationCompleted();
    }
    private sealed class CheckSurface : OpenGlControlBase
    {
        public event Action<string?>? Finished;
        private bool _done;
        protected override unsafe void OnOpenGlRender(GlInterface context, int fb)
        {
            if (_done) return; _done = true;
            try
            {
                using var gl = GL.GetApi(context.GetProcAddress);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
                gl.Viewport(0, 0, 16, 16); gl.Disable(EnableCap.DepthTest); gl.Disable(EnableCap.Blend);
                var assembly = typeof(ModelGlRenderer).Assembly;
                using var resource = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(".TOTK.frag")))!;
                var sources = new[] { ("PhysicsTool", new StreamReader(resource).ReadToEnd()), ("Toolbox", File.ReadAllText(ToolboxShader)) };
                bool es = context.Version?.Contains("OpenGL ES") == true;
                string Version(string text) => text.Replace("#version 330 core", "#version 330").Replace("#version 330", es ? "#version 300 es\nprecision highp float;\nprecision highp int;" : "#version 150");
                string vertex = """
                    #version 330
                    out vec2 f_raw_texcoord0; out vec2 f_raw_texcoord1; out vec2 f_raw_texcoord2; out vec2 f_raw_texcoord3;
                    out vec3 normal; out vec3 tangent; out vec3 bitangent; out vec4 vertexColor;
                    void main() {
                        vec2 p = gl_VertexID == 0 ? vec2(-1,-1) : gl_VertexID == 1 ? vec2(3,-1) : vec2(-1,3);
                        gl_Position = vec4(p,0,1); f_raw_texcoord0=vec2(.5); f_raw_texcoord1=vec2(.5);
                        f_raw_texcoord2=vec2(.5); f_raw_texcoord3=vec2(.5); normal=vec3(0,0,1);
                        tangent=vec3(1,0,0); bitangent=vec3(0,1,0); vertexColor=vec4(1);
                    }
                    """;
                foreach (var (name, fragment) in sources)
                {
                    uint Compile(ShaderType type, string text)
                    {
                        var shader = gl.CreateShader(type); gl.ShaderSource(shader, Version(text)); gl.CompileShader(shader);
                        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
                        if (ok == 0) throw new Exception(name + ": " + gl.GetShaderInfoLog(shader)); return shader;
                    }
                    uint vs = Compile(ShaderType.VertexShader, vertex), fs = Compile(ShaderType.FragmentShader, fragment);
                    uint program = gl.CreateProgram(); gl.AttachShader(program, vs); gl.AttachShader(program, fs); gl.LinkProgram(program);
                    gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
                    if (linked == 0) throw new Exception(name + ": " + gl.GetProgramInfoLog(program));
                    gl.UseProgram(program);
                    void I(string key, int value) => gl.Uniform1(gl.GetUniformLocation(program, key), value);
                    void F(string key, float value) => gl.Uniform1(gl.GetUniformLocation(program, key), value);
                    void V4(string key, float x, float y, float z, float w) => gl.Uniform4(gl.GetUniformLocation(program, key), x, y, z, w);
                    uint vao = gl.GenVertexArray(), tex = gl.GenTexture(); gl.BindVertexArray(vao);
                    gl.ActiveTexture(TextureUnit.Texture0); gl.BindTexture(TextureTarget.Texture2D, tex);
                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
                    I("irradianceMap", 8); I("totkTextureCount", 1); I("totkTextureEnabled0", 1); I("totkTextureSrgb0", 1);
                    V4("totkTextureSrt0", 1,1,0,0); V4("totkTextureChannels0", 0,1,2,3);
                    I("totkAlbedoSource", 0); I("totkAlbedoOutput", 0); I("totkTransparencySource", 0); I("totkTransparencyChannel", 10);
                    I("totkLayeredAlbedoSource", -1); I("totkEyeShadowSource", -1); I("totkNormalSource", -1);
                    I("totkAoOutput", 300); I("totkAoChannel", 10); I("totkMaterialBehavior", 2);
                    I("totkAlphaTestEnabled", 1); F("gsys_alpha_test_ref_value", .5f); I("totkForceOpaqueOutput", 1); I("renderDiffuse", 1);
                    gl.Uniform3(gl.GetUniformLocation(program, "specLightDirection"), 0f,0f,1f);
                    gl.Uniform3(gl.GetUniformLocation(program, "uViewDirection"), 0f,0f,1f);
                    foreach (var (output, alpha, visible) in new[] { (203,255,true), (0,255,false), (203,0,false), (500,255,false) })
                    {
                        byte[] pixel = [2, 2, 2, (byte)alpha];
                        fixed (byte* p = pixel) gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 1,1,0,PixelFormat.Rgba,PixelType.UnsignedByte,p);
                        I("totkTransparencyOutput", output); F("p_const_value0", 0);
                        gl.ClearColor(0,0,0,0); gl.Clear(ClearBufferMask.ColorBufferBit);
                        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
                        byte[] sample = new byte[4]; fixed(byte* p = sample) gl.ReadPixels(4,4,1,1,PixelFormat.Rgba,PixelType.UnsignedByte,p);
                        if (gl.GetError() != GLEnum.NoError) throw new Exception(name + " OpenGL error");
                        if ((sample[3] != 0) != visible) throw new Exception($"{name}: wrong opacity for output {output}, alpha {alpha}: {sample[3]}");
                    }
                    if (name == "PhysicsTool")
                    {
                        byte[] grayscale = [128, 0, 0, 255];
                        fixed (byte* p = grayscale) gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, 1,1,0,PixelFormat.Rgba,PixelType.UnsignedByte,p);
                        I("totkTextureSrgb0", 0); I("totkTextureLuminance0", 1); I("totkAlphaTestEnabled", 0);
                        I("totkMaterialBehavior", 0); I("totkAlbedoOutput", 0);
                        byte[] Sample()
                        {
                            gl.ClearColor(0,0,0,0); gl.Clear(ClearBufferMask.ColorBufferBit); gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
                            var value = new byte[4]; fixed (byte* p = value) gl.ReadPixels(4,4,1,1,PixelFormat.Rgba,PixelType.UnsignedByte,p);
                            return value;
                        }
                        void Neutral(byte[] value)
                        {
                            if (value[0] < 80 || Math.Abs(value[0]-value[1]) > 1 || Math.Abs(value[0]-value[2]) > 1) throw new Exception("BC4 color was not grayscale");
                        }
                        Neutral(Sample());
                        I("totkAlbedoOutput", 300); I("totkEyeShadowSource", 0); Neutral(Sample());
                        I("totkEyeShadowSource", -1); I("totkAlbedoOutput", 400); V4("p_const_color0", .5f,0,0,1);
                        var constant = Sample();
                        if (constant[0] < 80 || constant[1] > 1 || constant[2] > 1) throw new Exception("Grayscale sampler changed an RGB material constant");
                        I("totkAlphaTestEnabled", 1); I("totkTransparencyOutput", 0); I("totkTransparencyChannel", 20);
                        if (Sample()[3] != 0) throw new Exception("Color luminance expansion altered raw green-channel opacity");
                        if (gl.GetError() != GLEnum.NoError) throw new Exception("Grayscale test OpenGL error");
                        Console.WriteLine("PASS: grayscale albedo/eye shadow, unchanged RGB constants and raw scalar opacity.");
                    }
                    gl.DeleteTexture(tex); gl.DeleteVertexArray(vao); gl.DeleteProgram(program); gl.DeleteShader(vs); gl.DeleteShader(fs);
                    Console.WriteLine("PASS: " + name + " exact fragment: generated opacity remains visible; direct RGB, alpha cutout and constant opacity still discard.");
                }
                Dispatcher.UIThread.Post(() => Finished?.Invoke(null));
            }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => Finished?.Invoke(ex.ToString())); }
        }
    }
}
