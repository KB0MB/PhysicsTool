using Avalonia;
using Avalonia.Threading;
using System.Diagnostics;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class PhysicsViewport
{
    private readonly DispatcherTimer _cameraTimer = new() { Interval = TimeSpan.FromSeconds(1d / 60) };
    private readonly Stopwatch _cameraClock = new();
    private (Vector3 Center, float Yaw, float Pitch, double Zoom) _cameraFrom, _cameraTo;
    internal bool CameraTransitionActive => _cameraTimer.IsEnabled;
    private void ConfigureCameraTransitions()
    {
        _cameraTimer.Tick += (_, _) => AdvanceCameraTransition(_cameraClock.Elapsed.TotalSeconds);
        DetachedFromVisualTree += (_, _) => CancelCameraTransition();
    }
    private void BeginCameraTransition(Vector3 center, float yaw, float pitch, double zoom)
    {
        CancelCameraTransition();
        _cameraFrom = CameraState;
        _cameraTo = (center, _yaw + MathF.IEEERemainder(yaw - _yaw, MathF.Tau), pitch, zoom);
        _cameraClock.Restart(); _cameraTimer.Start();
    }
    internal void AdvanceCameraTransition(double seconds)
    {
        if (!_cameraTimer.IsEnabled) return;
        var t = (float)Math.Clamp(seconds / .24, 0, 1);
        var ease = t * t * (3 - 2 * t);
        _center = Vector3.Lerp(_cameraFrom.Center, _cameraTo.Center, ease);
        _yaw = float.Lerp(_cameraFrom.Yaw, _cameraTo.Yaw, ease);
        _pitch = float.Lerp(_cameraFrom.Pitch, _cameraTo.Pitch, ease);
        _zoom = Math.Exp(double.Lerp(Math.Log(_cameraFrom.Zoom), Math.Log(_cameraTo.Zoom), ease));
        _pan = default;
        if (t >= 1) CancelCameraTransition();
        Changed();
    }
    private void CancelCameraTransition() { _cameraTimer.Stop(); _cameraClock.Stop(); }
}
