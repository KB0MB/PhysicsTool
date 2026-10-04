using System.Numerics;
using Silk.NET.OpenGL;

namespace PhysicsTool.Desktop.Rendering;

internal sealed unsafe class GridGlRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _program, _vao, _buffer;
    private readonly int _camera;
    public GridGlRenderer(GL gl, bool es)
    {
        _gl = gl;
        var version = es ? "#version 300 es\nprecision highp float;\n" : "#version 150\n";
        uint Compile(ShaderType type, string source)
        {
            var shader = gl.CreateShader(type); gl.ShaderSource(shader, version + source); gl.CompileShader(shader);
            gl.GetShader(shader, ShaderParameterName.CompileStatus, out var ok);
            if (ok == 0) { var log = gl.GetShaderInfoLog(shader); gl.DeleteShader(shader); throw new InvalidOperationException(log); }
            return shader;
        }
        var vertex = Compile(ShaderType.VertexShader, "in vec3 position; in vec3 color; uniform mat4 camera; out vec3 tint; void main(){gl_Position=camera*vec4(position,1);tint=color;}");
        uint fragment = 0;
        try
        {
            fragment = Compile(ShaderType.FragmentShader, "in vec3 tint; out vec4 result; void main(){result=vec4(tint,1);}");
            _program = gl.CreateProgram(); gl.AttachShader(_program, vertex); gl.AttachShader(_program, fragment);
            gl.BindAttribLocation(_program, 0, "position"); gl.BindAttribLocation(_program, 1, "color"); gl.LinkProgram(_program);
            gl.GetProgram(_program, ProgramPropertyARB.LinkStatus, out var ok);
            if (ok == 0) { var log = gl.GetProgramInfoLog(_program); gl.DeleteProgram(_program); throw new InvalidOperationException(log); }
        }
        finally { gl.DeleteShader(vertex); if (fragment != 0) gl.DeleteShader(fragment); }
        _camera = gl.GetUniformLocation(_program, "camera");
        _vao = gl.GenVertexArray(); _buffer = gl.GenBuffer();
        gl.BindVertexArray(_vao); gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 24, null); gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 24, (void*)12); gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);
    }
    public void Draw(IReadOnlyList<GridLine> lines, Matrix4x4 camera)
    {
        var data = new float[lines.Count * 12]; var offset = 0;
        foreach (var line in lines)
            foreach (var point in new[] { line.Start, line.End })
            { data[offset++] = point.X; data[offset++] = point.Y; data[offset++] = point.Z; data[offset++] = line.Color.X; data[offset++] = line.Color.Y; data[offset++] = line.Color.Z; }
        _gl.UseProgram(_program); _gl.UniformMatrix4(_camera, 1, false, (float*)&camera);
        _gl.Disable(EnableCap.Blend); _gl.Enable(EnableCap.DepthTest); _gl.DepthFunc(DepthFunction.Less);
        _gl.BindVertexArray(_vao); _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
        fixed (float* p = data) _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * 4), p, BufferUsageARB.StreamDraw);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(lines.Count * 2));
        _gl.BindVertexArray(0); _gl.UseProgram(0);
    }
    public void Dispose() { _gl.DeleteBuffer(_buffer); _gl.DeleteVertexArray(_vao); _gl.DeleteProgram(_program); }
}
