using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Silk.NET.OpenGL;

namespace PhysicsTool.Desktop.Rendering;

public sealed class ModelGlControl : OpenGlControlBase
{
    private readonly PhysicsViewport _view;
    private GL? _gl;
    private ModelGlRenderer? _renderer;
    private SceneRenderTarget? _sceneTarget;
    private bool _failed;
    public string? Failure { get; private set; }
    public event Action<string>? RenderFailed;
    public event Action<byte[], int, int>? FrameCaptured;
    public bool CaptureNextFrame { get; set; }
    internal int LastVisibleMeshes => _renderer?.LastVisibleMeshes ?? 0;
    internal int LastMaterialUpdates => _renderer?.LastMaterialUpdates ?? 0;
    internal double LastRenderMilliseconds { get; private set; }
    internal int DepthBits { get; private set; }
    public ModelGlControl(PhysicsViewport view)
    {
        _view = view; IsHitTestVisible = false;
        view.CameraChanged += RequestNextFrameRendering;
    }
    protected override void OnOpenGlInit(GlInterface gl)
    {
        try { _gl = GL.GetApi(gl.GetProcAddress); _renderer = new ModelGlRenderer(_gl, gl.Version?.Contains("OpenGL ES", StringComparison.Ordinal) == true); }
        catch (Exception ex) { Fail(ex.Message); }
    }
    protected override void OnOpenGlLost() { _sceneTarget = null; _renderer = null; _gl = null; _failed = false; _view.GpuReady = false; }
    protected override void OnOpenGlDeinit(GlInterface gl) { _sceneTarget?.Dispose(); _sceneTarget = null; _renderer?.Dispose(); _renderer = null; _gl?.Dispose(); _gl = null; _view.GpuReady = false; }
    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_failed || _renderer == null || _gl == null) return;
        try
        {
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scaling)), height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scaling));
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
            if (_view.FlyNavigation) (_sceneTarget ??= new(_gl)).Bind(width, height);
            var background = _view.ViewportBackground;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            _renderer.Draw(_view.Model, _view.HiddenMeshes, _view.CameraMatrix, _view.ViewDirection, width, height, _view.FlyNavigation ? null : _view.GridLines,
                new System.Numerics.Vector3(background.R / 255f, background.G / 255f, background.B / 255f), _view.ShowEmission, _view.DisplayMode, _view.FlyNavigation);
            LastRenderMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (!_view.GpuReady) Dispatcher.UIThread.Post(() => { _view.GpuReady = true; _view.InvalidateVisual(); });
            if (CaptureNextFrame)
            {
                DepthBits = _gl.GetInteger((GetPName)0x0D56); // GL_DEPTH_BITS; diagnostics only.
                CaptureNextFrame = false; var pixels = new byte[checked(width * height * 4)];
                fixed (byte* p = pixels) _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                Dispatcher.UIThread.Post(() => FrameCaptured?.Invoke(pixels, width, height));
            }
            if (_view.FlyNavigation) _sceneTarget!.Present((uint)fb);
        }
        catch (Exception ex) { Fail(ex.Message); }
        finally { _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb); }
    }
    private void Fail(string message)
    {
        _failed = true; Failure = message;
        Dispatcher.UIThread.Post(() => { _view.GpuReady = false; _view.InvalidateVisual(); RenderFailed?.Invoke(message); });
    }
}
