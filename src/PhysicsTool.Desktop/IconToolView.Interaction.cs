using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PhysicsTool.Desktop;

internal sealed partial class IconToolView
{
    private sealed record MeshId(string Name, int Occurrence);
    private readonly HashSet<MeshId> _hiddenMeshes = [];
    private readonly NumericUpDown[] _position = VectorInputs(-10000, 10000, .01m);
    private Vector3 _lastPosition, _startPosition, _startRotation;
    internal bool BeltVisible => Asset?.ArmorPose?.HasBelt == true && !_hiddenMeshes.Any(m => PhysicsTool.Core.IconArmorProps.IsBelt(m.Name));
    internal void SetBeltVisible(bool visible)
    {
        if (_viewport.Model == null) return;
        for (int i = 0; i < _viewport.Model.Meshes.Count; i++)
            if (PhysicsTool.Core.IconArmorProps.IsBelt(_viewport.Model.Meshes[i].Name)) SetCaptureMeshVisible(i, visible);
    }

    private MeshId MeshIdentity(int index)
    {
        var meshes = _viewport.Model!.Meshes;
        string name = meshes[index].Name;
        return new(name, meshes.Take(index).Count(m => m.Name == name));
    }

    internal void SetCaptureMeshVisible(int index, bool visible)
    {
        if (_viewport.Model == null || index < 0 || index >= _viewport.Model.Meshes.Count) return;
        if (visible) _hiddenMeshes.Remove(MeshIdentity(index)); else _hiddenMeshes.Add(MeshIdentity(index));
        _viewport.SetMeshVisible(index, visible);
        Surface?.RequestNextFrameRendering();
    }

    private void SyncMeshVisibility()
    {
        _viewport.HiddenMeshes.Clear();
        for (int i = 0; i < _viewport.Model!.Meshes.Count; i++)
            if (_hiddenMeshes.Contains(MeshIdentity(i))) _viewport.HiddenMeshes.Add(i);
    }

    private async Task ShowCaptureMeshes()
    {
        if (Busy || Asset == null) return;
        _viewport.FinishTransform(false);
        await MeshVisibilityDialog.Show(_owner, _viewport, SetCaptureMeshVisible,
            Asset.Name.StartsWith("Armor_", StringComparison.Ordinal) ? PhysicsTool.Core.IconArmorProps.IsProp : null);
    }

    private void ConfigureModelInteraction(Control preview)
    {
        preview.Focusable = true;
        IPointer? pointer = null;
        _viewport.ModelTransformContext = () =>
        {
            if (Busy || _viewport.Model is not { } model || _viewport.HiddenMeshes.Count == model.Meshes.Count) return null;
            var pivot = (model.BoundsMinimum + model.BoundsMaximum) * .5f;
            var rotation = UsesViewport ? _viewport.Rotation : PhysicsTool.Core.IconCaptureAsset.View(Get(_camera), Get(_angles));
            rotation.Translation = Vector3.Zero;
            var camera = Surface?.FixedCamera ?? _viewport.CameraMatrix;
            var clip = Vector4.Transform(new Vector4(pivot, 1), camera);
            double scale = new Vector3(camera.M12, camera.M22, camera.M32).Length() * Math.Max(1, _viewport.Bounds.Height) * .5
                * (double)(_zoom.Value ?? 100) / 100 / Math.Max(.0001, Math.Abs(clip.W));
            return (pivot, rotation, Math.Max(.0001, scale));
        };
        _viewport.ModelTransformProjection = point =>
        {
            var p = Vector4.Transform(new Vector4(point, 1), Surface?.FixedCamera ?? _viewport.CameraMatrix);
            if (Math.Abs(p.W) < .000001f) return new Point(-100000, -100000);
            double zoom = (double)(_zoom.Value ?? 100) / 100;
            return new((_viewport.Bounds.Width * .5) * (1 + p.X / p.W * zoom), (_viewport.Bounds.Height * .5) * (1 - p.Y / p.W * zoom));
        };
        _viewport.TransformStarted += () =>
        {
            _startRotation = Get(_rotation); _startPosition = Get(_position);
            pointer?.Capture(preview);
        };
        _viewport.TransformPreview += delta =>
        {
            var r = _startRotation * (MathF.PI / 180);
            var matrix = Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z)
                * Matrix4x4.CreateTranslation(_startPosition) * delta;
            // Decompose the same XYZ convention used by the Actor fields, including the gimbal pole.
            float y = MathF.Asin(Math.Clamp(-matrix.M13, -1, 1));
            float x = Math.Abs(MathF.Cos(y)) > .0001f ? MathF.Atan2(matrix.M23, matrix.M33) : MathF.Atan2(-matrix.M32, matrix.M22);
            float z = Math.Abs(MathF.Cos(y)) > .0001f ? MathF.Atan2(matrix.M12, matrix.M11) : 0;
            SetModelPlacement(new Vector3(x, y, z) * (180 / MathF.PI), matrix.Translation);
        };
        _viewport.TransformFinished += delta =>
        {
            if (delta == null) SetModelPlacement(_startRotation, _startPosition);
            pointer?.Capture(null);
        };
        preview.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!e.Handled && _viewport.TransformKey(e)) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        preview.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            pointer = e.Pointer;
            // In Viewport mode its own handler tracks idle movement for orbit/pan.
            if (_viewport.TransformActive || !UsesViewport)
            {
                bool active = _viewport.TransformActive;
                _viewport.TrackTransformPointer(e.GetPosition(_viewport));
                if (active) e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        preview.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            pointer = e.Pointer;
            if (_viewport.TransformActive)
            {
                _viewport.FinishTransform(!e.GetCurrentPoint(preview).Properties.IsRightButtonPressed);
                e.Handled = true;
            }
            else if (!UsesViewport)
            {
                preview.Focus(); _viewport.TrackTransformPointer(e.GetPosition(_viewport));
            }
        }, RoutingStrategies.Tunnel);
        preview.PointerCaptureLost += (_, _) => _viewport.FinishTransform(false);
        preview.LostFocus += (_, _) => { if (!preview.IsKeyboardFocusWithin) _viewport.FinishTransform(false); };
        DetachedFromVisualTree += (_, _) => _viewport.FinishTransform(false);
    }

    private void SetModelPlacement(Vector3 rotation, Vector3 position)
    {
        _updating = true;
        try { Set(_rotation, rotation); Set(_position, position); }
        finally { _updating = false; }
        UpdatePreview();
    }
}
