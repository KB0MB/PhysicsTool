using System.Diagnostics;
using System.Numerics;
using Avalonia.Input;
using Avalonia.Threading;

namespace PhysicsTool.Desktop;

public sealed partial class PhysicsViewport
{
    internal bool FlyNavigation { get; set; }
    internal bool TranslationOnly { get; set; }
    internal double CameraSpeed { get; set; } = 12;
    internal Vector3 FlyPosition { get; private set; }
    private bool _looking;
    private readonly HashSet<Key> _flyKeys = new();
    private readonly DispatcherTimer _flyTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private long _flyTime;
    private void ConfigureFlyCamera()
    {
        _flyTimer.Tick += (_, _) =>
        {
            long now = Stopwatch.GetTimestamp();
            float elapsed = (float)Math.Clamp((now - _flyTime) / (double)Stopwatch.Frequency, 0, .05); _flyTime = now;
            StepFly(elapsed);
        };
        LostFocus += (_, _) => StopFly();
        DetachedFromVisualTree += (_, _) => StopFly();
    }
    internal void StepFly(float elapsed)
    {
        if (!FlyNavigation || !IsFocused || TransformActive) return;
        float Axis(Key positive, Key negative) => (_flyKeys.Contains(positive) ? 1 : 0) - (_flyKeys.Contains(negative) ? 1 : 0);
        var direction = new Vector3(Axis(Key.D, Key.A), Axis(Key.E, Key.Q), Axis(Key.S, Key.W));
        if (direction.LengthSquared() == 0) return;
        direction = Vector3.TransformNormal(Vector3.Normalize(direction), Matrix4x4.Transpose(Rotation));
        FlyPosition += direction * elapsed * (float)CameraSpeed * (_flyKeys.Contains(Key.LeftShift) || _flyKeys.Contains(Key.RightShift) ? 3 : 1);
        Changed();
    }
    private void StopFly() { _looking = false; _flyKeys.Clear(); _flyTimer.Stop(); }
    private bool FlyKey(KeyEventArgs e)
    {
        if (!FlyNavigation || TransformActive || e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return false;
        if (e.Key is not (Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E or Key.LeftShift or Key.RightShift)) return false;
        _flyKeys.Add(e.Key);
        if (!_flyTimer.IsEnabled) { _flyTime = Stopwatch.GetTimestamp(); _flyTimer.Start(); }
        return true;
    }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_flyKeys.Remove(e.Key)) e.Handled = true;
        if (_flyKeys.Count == 0) _flyTimer.Stop();
        base.OnKeyUp(e);
    }
    internal void StartFlyAt(Vector3 position)
    {
        StopFly(); CancelCameraTransition(); FlyPosition = _center = position;
        _yaw = 0; _pitch = 0; Changed();
    }
    private Matrix4x4 FlyMatrix
    {
        get
        {
            // A practical near clip preserves depth precision between distant wall layers.
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3,
                (float)(Math.Max(1, Bounds.Width) / Math.Max(1, Bounds.Height)), .25f, Math.Max(10000, _extent * 4));
            // System.Numerics uses [0,1] depth. OpenGL expects [-1,1]; leaving
            // it unconverted wastes half the depth range and moves the near clip.
            projection *= Matrix4x4.CreateScale(1, 1, 2) * Matrix4x4.CreateTranslation(0, 0, -1);
            return Matrix4x4.CreateTranslation(-FlyPosition) * Rotation * projection;
        }
    }
    private void FrameFly(Vector3 center, float extent)
    {
        CancelCameraTransition();
        _center = center;
        float aspect = (float)(Math.Max(1, Bounds.Width) / Math.Max(1, Bounds.Height));
        FlyPosition = center + ViewDirection * Math.Max(1, extent / Math.Min(1, aspect));
        Changed();
    }
    private Vector3 Unproject(Vector3 ndc, Matrix4x4 inverse)
    {
        var value = Vector4.Transform(new Vector4(ndc, 1), inverse);
        return new Vector3(value.X, value.Y, value.Z) / value.W;
    }
}
