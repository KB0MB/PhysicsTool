using System.Numerics;
using Silk.NET.OpenGL;

namespace PhysicsTool.Desktop.Rendering;

internal sealed unsafe class IconShadowMap : IDisposable
{
    internal const int Size = 2048;
    private readonly GL _gl;
    private readonly uint _framebuffer;
    internal uint Texture { get; }
    internal Matrix4x4 Camera { get; private set; }

    internal IconShadowMap(GL gl)
    {
        _gl = gl;
        Texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2DArray, Texture);
        gl.TexImage3D(TextureTarget.Texture2DArray, 0, (int)InternalFormat.DepthComponent32f, Size, Size, 1, 0, PixelFormat.DepthComponent, PixelType.Float, null);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
        gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        _framebuffer = gl.GenFramebuffer();
    }

    internal void Begin(Vector3 min, Vector3 max, Vector3 light)
    {
        var center = (min + max) * .5f;
        float radius = Math.Max(.01f, Vector3.Distance(min, max) * .5f);
        var view = Matrix4x4.CreateLookAt(center + light * radius * 2, center, Math.Abs(light.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY);
        Vector3 low = new(float.MaxValue), high = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = Vector3.Transform(new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z), view);
            low = Vector3.Min(low, corner); high = Vector3.Max(high, corner);
        }
        float margin = radius * .02f;
        Camera = view * Matrix4x4.CreateOrthographicOffCenter(low.X-margin, high.X+margin, low.Y-margin, high.Y+margin, -high.Z-margin, -low.Z+margin);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, Texture, 0, 0);
        var none = GLEnum.None;
        _gl.DrawBuffers(1, &none); _gl.ReadBuffer(ReadBufferMode.None);
        if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("Icon shadow framebuffer is incomplete.");
        _gl.Viewport(0, 0, Size, Size);
        _gl.ColorMask(false, false, false, false); _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less); _gl.Clear(ClearBufferMask.DepthBufferBit);
    }

    public void Dispose() { _gl.DeleteFramebuffer(_framebuffer); _gl.DeleteTexture(Texture); }
}
