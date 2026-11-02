using Silk.NET.OpenGL;

namespace PhysicsTool.Desktop.Rendering;

// Avalonia's control framebuffer can have only 16 depth bits. Large perspective
// scenes need a separate depth attachment; the compositor still owns its target.
internal sealed class SceneRenderTarget(GL gl) : IDisposable
{
    private uint _framebuffer, _color, _depth;
    private int _width, _height;

    internal void Bind(int width, int height)
    {
        if (_framebuffer != 0 && width == _width && height == _height)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            return;
        }
        Dispose();
        _width = width; _height = height;
        _framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        try
        {
            _color = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _color);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)width, (uint)height);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _color);
            _depth = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)width, (uint)height);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depth);
            if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
                throw new InvalidOperationException("The shrine depth framebuffer could not be created.");
        }
        catch { Dispose(); throw; }
        finally { gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0); }
    }

    internal void Present(uint destination)
    {
        gl.Disable(EnableCap.ScissorTest);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebuffer);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, destination);
        gl.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, destination);
    }

    public void Dispose()
    {
        if (_depth != 0) gl.DeleteRenderbuffer(_depth);
        if (_color != 0) gl.DeleteRenderbuffer(_color);
        if (_framebuffer != 0) gl.DeleteFramebuffer(_framebuffer);
        _depth = _color = _framebuffer = 0;
    }
}
