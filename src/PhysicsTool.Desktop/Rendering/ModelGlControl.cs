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
    private CaptureEdgeRenderer? _edges;
    internal bool EdgeHighlights { get; set; }
    private bool _failed;
    private readonly DispatcherTimer _frameTimer = ViewportFrameRate.CreateTimer();
    private bool _framePending;
    public string? Failure { get; private set; }
    public event Action<string>? RenderFailed;
    public event Action<byte[], int, int>? FrameCaptured;
    public bool CaptureNextFrame { get; set; }
    internal bool CaptureTransparentBackground { get; set; }
    internal System.Numerics.Matrix4x4? FixedCamera { get; set; }
    internal System.Numerics.Vector3? KeyLightDirection { get; set; }
    internal System.Numerics.Vector3? CameraPosition { get; set; }
    internal System.Numerics.Vector3? FixedViewDirection { get; set; }
    internal float DirectIntensity { get; set; } = 1;
    internal float AmbientIntensity { get; set; } = 1;
    internal float Brightness { get; set; } = 3;
    internal BotwLightingEnvironment? LightingEnvironment { get; set; }
    internal bool CastShadows { get; set; }
    internal float ProjectionZoom { get; set; } = 1;
    internal int? CaptureResolution { get; set; }
    internal int LastVisibleMeshes => _renderer?.LastVisibleMeshes ?? 0;
    internal int LastMaterialUpdates => _renderer?.LastMaterialUpdates ?? 0;
    internal double LastRenderMilliseconds { get; private set; }
    internal long RenderedFrames { get; private set; }
    internal int DepthBits { get; private set; }
    public ModelGlControl(PhysicsViewport view)
    {
        _view = view; IsHitTestVisible = false;
        view.CameraChanged += () => { _framePending = true; if (!_frameTimer.IsEnabled) _frameTimer.Start(); };
        _frameTimer.Tick += (_, _) =>
        {
            if (!_framePending) { _frameTimer.Stop(); return; }
            _framePending = false;
            RequestNextFrameRendering();
        };
        DetachedFromVisualTree += (_, _) => { _frameTimer.Stop(); _framePending = false; };
    }
    protected override void OnOpenGlInit(GlInterface gl)
    {
        try { _gl = GL.GetApi(gl.GetProcAddress); _renderer = new ModelGlRenderer(_gl, gl.Version?.Contains("OpenGL ES", StringComparison.Ordinal) == true); }
        catch (Exception ex) { Fail(ex.Message); }
    }
    protected override void OnOpenGlLost() { _edges = null; _sceneTarget = null; _renderer = null; _gl = null; _failed = false; _view.GpuReady = false; }
    protected override void OnOpenGlDeinit(GlInterface gl) { _edges?.Dispose(); _edges = null; _sceneTarget?.Dispose(); _sceneTarget = null; _renderer?.Dispose(); _renderer = null; _gl?.Dispose(); _gl = null; _view.GpuReady = false; }
    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_failed || _renderer == null || _gl == null) return;
        try
        {
            var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            int width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scaling)), height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scaling));
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);
            bool separateDepth = _view.FlyNavigation || _view.HighPrecisionDepth || EdgeHighlights;
            if (separateDepth) (_sceneTarget ??= new(_gl)).Bind(width, height, EdgeHighlights);
            var background = _view.ViewportBackground;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var zoom = float.IsFinite(ProjectionZoom) ? Math.Clamp(ProjectionZoom, .25f, 4) : 1;
            var camera = (FixedCamera ?? _view.CameraMatrix) * System.Numerics.Matrix4x4.CreateScale(zoom, zoom, 1);
            void Draw(int w, int h, bool transparent) => _renderer.Draw(_view.Model, _view.HiddenMeshes, camera,
                FixedViewDirection ?? _view.ViewDirection, w, h, transparent || _view.FlyNavigation ? null : _view.GridLines,
                transparent ? System.Numerics.Vector3.Zero : new System.Numerics.Vector3(background.R / 255f, background.G / 255f, background.B / 255f),
                _view.ShowEmission, _view.DisplayMode, _view.FlyNavigation, KeyLightDirection, CameraPosition, transparent ? 0 : 1, DirectIntensity, AmbientIntensity, Brightness, LightingEnvironment, CastShadows);
            Draw(width, height, false);
            SceneRenderTarget ProcessEdges(SceneRenderTarget target, int w, int h) =>
                (_edges ??= new(_gl, gl.Version?.Contains("OpenGL ES") == true)).Apply(target, w, h, camera,
                    _view.Model is { } model ? System.Numerics.Vector3.Distance(model.BoundsMinimum, model.BoundsMaximum) : 1,
                    KeyLightDirection ?? new System.Numerics.Vector3(.35f, .8f, .45f), DirectIntensity * Math.Clamp(Brightness, 0, 1));
            if (separateDepth) (EdgeHighlights ? ProcessEdges(_sceneTarget!, width, height) : _sceneTarget!).Present((uint)fb);
            LastRenderMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            RenderedFrames++;
            if (!_view.GpuReady) Dispatcher.UIThread.Post(() => { _view.GpuReady = true; _view.InvalidateVisual(); });
            if (CaptureNextFrame)
            {
                using var exportTarget = CaptureTransparentBackground || CaptureResolution.HasValue || separateDepth ? new SceneRenderTarget(_gl) : null;
                if (exportTarget != null)
                {
                    if (CaptureResolution.HasValue) width = height = Math.Clamp(CaptureResolution.Value, 32, 4096);
                    exportTarget.Bind(width, height, EdgeHighlights); Draw(width, height, CaptureTransparentBackground || CaptureResolution.HasValue);
                    if (EdgeHighlights) ProcessEdges(exportTarget, width, height);
                }
                DepthBits = _gl.GetInteger((GetPName)0x0D56); // GL_DEPTH_BITS; diagnostics only.
                CaptureNextFrame = false; var pixels = new byte[checked(width * height * 4)];
                fixed (byte* p = pixels) _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
                Dispatcher.UIThread.Post(() => FrameCaptured?.Invoke(pixels, width, height));
            }
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
