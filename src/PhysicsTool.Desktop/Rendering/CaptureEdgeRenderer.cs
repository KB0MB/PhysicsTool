using System.Numerics;
using Silk.NET.OpenGL;

namespace PhysicsTool.Desktop.Rendering;

// Optional screen-space approximation. It does not replace the game's material shaders.
internal sealed unsafe class CaptureEdgeRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _program, _vao;
    private readonly SceneRenderTarget _output;

    internal CaptureEdgeRenderer(GL gl, bool es)
    {
        _gl = gl; _output = new(gl);
        string version = es ? "#version 300 es\nprecision highp float;\nprecision highp sampler2D;\n" : "#version 150\n";
        uint vertex = Compile(ShaderType.VertexShader, version + "out vec2 uv; void main(){ vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2); uv=p; gl_Position=vec4(p*2.0-1.0,0,1); }");
        uint fragment = 0;
        try
        {
            fragment = Compile(ShaderType.FragmentShader, version + """
                in vec2 uv;
                out vec4 result;
                uniform sampler2D colorImage;
                uniform sampler2D depthImage;
                uniform mat4 inverseCamera;
                uniform float worldWidth;
                uniform vec3 lightDirection;
                uniform float lightStrength;
                vec3 position(vec2 at, float depth) {
                    vec4 p=inverseCamera*vec4(at*2.0-1.0,depth*2.0-1.0,1);
                    return p.xyz/p.w;
                }
                float boundary(vec2 offset) {
                    vec2 at=uv+offset;
                    if(any(lessThan(at,vec2(0))) || any(greaterThan(at,vec2(1)))) return 0.0;
                    float d=texture(depthImage,at).r;
                    // Highlight the combined cutout, not internal depth or material boundaries.
                    return d>=1.0 ? 1.0 : 0.0;
                }
                void main() {
                    vec4 color=texture(colorImage,uv);
                    float depth=texture(depthImage,uv).r;
                    if(depth>=1.0 || color.a==0.0) { result=color; return; }
                    vec3 p=position(uv,depth);
                    vec3 dx=position(uv+vec2(1,0),depth)-p;
                    vec3 dy=position(uv+vec2(0,1),depth)-p;
                    vec2 radius=worldWidth/max(vec2(length(dx),length(dy)),vec2(0.000001));
                    float line=0.0;
                    for(int i=0;i<12;i++) {
                        float angle=float(i)*6.2831853/12.0;
                        vec2 dir=vec2(cos(angle),sin(angle));
                        vec3 outward=normalize(dx*dir.x/length(dx)+dy*dir.y/length(dy));
                        float lit=pow(max(dot(outward,lightDirection),0.0),1.5);
                        float band=0.0;
                        for(int j=1;j<=3;j++) {
                            float distance=float(j)/3.0;
                            band+=boundary(dir*radius*distance)*(1.0-distance*0.65);
                        }
                        line+=band*lit;
                    }
                    line=1.0-exp(-line*0.4*lightStrength);
                    // Tint only existing coverage, preserving the exported alpha exactly.
                    result=vec4(mix(color.rgb,vec3(1.0,0.98,0.90)*color.a,line*0.6),color.a);
                }
                """);
            _program = gl.CreateProgram(); gl.AttachShader(_program, vertex); gl.AttachShader(_program, fragment);
            gl.LinkProgram(_program); gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException("Capture edge shader: " + gl.GetProgramInfoLog(_program));
            _vao = gl.GenVertexArray();
        }
        catch { if (_program != 0) gl.DeleteProgram(_program); throw; }
        finally { gl.DeleteShader(vertex); if (fragment != 0) gl.DeleteShader(fragment); }
    }

    internal SceneRenderTarget Apply(SceneRenderTarget source, int width, int height, Matrix4x4 camera, float modelSize, Vector3 light, float strength)
    {
        if (!Matrix4x4.Invert(camera, out var inverse)) throw new InvalidOperationException("Invalid capture camera.");
        _output.Bind(width, height);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Disable(EnableCap.DepthTest); _gl.Disable(EnableCap.Blend); _gl.Disable(EnableCap.CullFace); _gl.Disable(EnableCap.ScissorTest);
        _gl.ColorMask(true, true, true, true); _gl.UseProgram(_program);
        _gl.ActiveTexture(TextureUnit.Texture0); _gl.BindTexture(TextureTarget.Texture2D, source.ColorTexture);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "colorImage"), 0);
        _gl.ActiveTexture(TextureUnit.Texture1); _gl.BindTexture(TextureTarget.Texture2D, source.DepthTexture);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "depthImage"), 1);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "worldWidth"), Math.Max(.000001f, modelSize * .0035f));
        light = light.LengthSquared() > 1e-12f ? Vector3.Normalize(light) : Vector3.Zero;
        _gl.Uniform3(_gl.GetUniformLocation(_program, "lightDirection"), light.X, light.Y, light.Z);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "lightStrength"), Math.Clamp(strength, 0, 1));
        _gl.UniformMatrix4(_gl.GetUniformLocation(_program, "inverseCamera"), 1, false, (float*)&inverse);
        _gl.BindVertexArray(_vao); _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        return _output;
    }

    private uint Compile(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type); _gl.ShaderSource(shader, source); _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled != 0) return shader;
        string error = _gl.GetShaderInfoLog(shader); _gl.DeleteShader(shader);
        throw new InvalidOperationException("Capture edge shader: " + error);
    }
    public void Dispose() { _output.Dispose(); _gl.DeleteVertexArray(_vao); _gl.DeleteProgram(_program); }
}
