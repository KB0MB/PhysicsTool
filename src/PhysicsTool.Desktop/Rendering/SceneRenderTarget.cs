using Silk.NET.OpenGL;

namespace PhysicsTool.Desktop.Rendering;

// Avalonia's framebuffer can have only 16 depth bits. Map scenes and layered
// character meshes need more precision; the compositor still owns its target.
internal sealed class SceneRenderTarget(GL gl) : IDisposable
{
    private uint _framebuffer, _color, _depth;
    private int _width, _height;
    private bool _sampleable;
    internal uint ColorTexture => _sampleable ? _color : throw new InvalidOperationException("Target is not sampleable.");
    internal uint DepthTexture => _sampleable ? _depth : throw new InvalidOperationException("Target is not sampleable.");

    internal unsafe void Bind(int width, int height, bool sampleable = false)
    {
        if (_framebuffer != 0 && width == _width && height == _height && sampleable == _sampleable)
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            return;
        }
        Dispose();
        _width = width; _height = height;
        _sampleable = sampleable;
        _framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        try
        {
            if (sampleable)
            {
                uint Texture(InternalFormat format, PixelFormat pixels, PixelType type, FramebufferAttachment attachment)
                {
                    uint texture = gl.GenTexture(); gl.BindTexture(TextureTarget.Texture2D, texture);
                    gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)width, (uint)height, 0, pixels, type, null);
                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
                    gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
                    gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, attachment, TextureTarget.Texture2D, texture, 0);
                    return texture;
                }
                _color = Texture(InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, FramebufferAttachment.ColorAttachment0);
                _depth = Texture(InternalFormat.DepthComponent24, PixelFormat.DepthComponent, PixelType.UnsignedInt, FramebufferAttachment.DepthAttachment);
            }
            else
            {
            _color = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _color);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)width, (uint)height);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _color);
            _depth = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)width, (uint)height);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depth);
            }
            if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
                throw new InvalidOperationException("The viewport depth framebuffer could not be created.");
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
        if (_depth != 0) { if (_sampleable) gl.DeleteTexture(_depth); else gl.DeleteRenderbuffer(_depth); }
        if (_color != 0) { if (_sampleable) gl.DeleteTexture(_color); else gl.DeleteRenderbuffer(_color); }
        if (_framebuffer != 0) gl.DeleteFramebuffer(_framebuffer);
        _depth = _color = _framebuffer = 0;
    }
}
