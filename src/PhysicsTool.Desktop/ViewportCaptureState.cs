using System.Numerics;
using Avalonia;

namespace PhysicsTool.Desktop;

internal sealed record ViewportCaptureState(Vector3 Center, float Extent, float Yaw, float Pitch, double Zoom, double PanX, double PanY)
{
    internal void Validate()
    {
        if (!float.IsFinite(Center.LengthSquared()) || !float.IsFinite(Extent) || Extent <= 0 ||
            !float.IsFinite(Yaw) || !float.IsFinite(Pitch) || !double.IsFinite(Zoom) || Zoom <= 0 ||
            !double.IsFinite(PanX) || !double.IsFinite(PanY)) throw new InvalidDataException("Invalid viewport camera.");
    }
}

public sealed partial class PhysicsViewport
{
    internal ViewportCaptureState SaveCaptureCamera() => new(_center, _extent, _yaw, _pitch, _zoom, _pan.X, _pan.Y);
    internal void RestoreCaptureCamera(ViewportCaptureState state)
    {
        state.Validate(); CancelCameraTransition();
        _center = state.Center; _extent = state.Extent; _yaw = state.Yaw; _pitch = state.Pitch;
        _zoom = state.Zoom; _pan = new Point(state.PanX, state.PanY); Changed();
    }
}
