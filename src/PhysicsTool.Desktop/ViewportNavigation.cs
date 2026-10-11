using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Numerics;

namespace PhysicsTool.Desktop;

public enum SelectionOperation { Replace, Add, Toggle }
public enum CameraView { Front, Back, Left, Right, Top, Bottom }
public sealed partial class PhysicsViewport
{
    private bool _selecting;
    private Point _down;
    private Rect _selectionRect;
    private KeyModifiers _selectionModifiers;
    private IPointer? _gesturePointer;
    public bool CanSelect { get; set; } = true;
    public event Action<IReadOnlyList<int>, SelectionOperation>? SelectionRequested;
    public (Vector3 Center, float Yaw, float Pitch, double Zoom) CameraState => (_center, _yaw, _pitch, _zoom);
    private double PixelsPerUnit => Math.Max(1, Math.Min(Bounds.Width, Bounds.Height)) * .78 / _extent * _zoom;

    private void ConfigureNavigation()
    {
        MenuItem Command(string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); return item; }
        var surfaces = new MenuItem { Header = "Translucent colliders", ToggleType = MenuItemToggleType.CheckBox, IsChecked = ShowColliderSurfaces };
        surfaces.Click += (_, _) => { ShowColliderSurfaces = surfaces.IsChecked; InvalidateVisual(); };
        ContextMenu = new ContextMenu { ItemsSource = new object[] {
            Command("Frame all", Fit), Command("Frame selected", FrameSelected), Command("Frame model", FrameModel),
            Command("Reset view", ResetView), new Separator(),
            Command("Front", () => SetView(CameraView.Front)), Command("Back", () => SetView(CameraView.Back)),
            Command("Right", () => SetView(CameraView.Right)), Command("Left", () => SetView(CameraView.Left)),
            Command("Top", () => SetView(CameraView.Top)), Command("Bottom", () => SetView(CameraView.Bottom)), new Separator(), surfaces } };
    }
    internal static IEnumerable<Vector3> BoundsCorners(Vector3 min, Vector3 max)
    {
        for (int i = 0; i < 8; i++) yield return new((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
    }
    private void Frame(IEnumerable<Vector3> input)
    {
        CancelCameraTransition();
        var points = input.Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)).ToArray();
        if (points.Length == 0) points = [new(-.5f), new(.5f)];
        var min = points.Aggregate(Vector3.Min); var max = points.Aggregate(Vector3.Max);
        _center = (min + max) / 2; _extent = Math.Max(.01f, (max - min).Length()); _pan = default;
        var rotated = points.Select(p => Vector3.TransformNormal(p - _center, Rotation)).ToArray();
        var size = rotated.Aggregate(Vector3.Max) - rotated.Aggregate(Vector3.Min);
        var w = Math.Max(1, Bounds.Width); var h = Math.Max(1, Bounds.Height);
        var desired = Math.Min(w * .82 / Math.Max(size.X, _extent * .05), h * .82 / Math.Max(size.Y, _extent * .05));
        _zoom = desired * _extent / (Math.Min(w, h) * .78); Changed();
    }
    public void FrameSelected()
    {
        var points = Data.Particles.Where(p => Selected.Contains(p.Index))
            .SelectMany(p => BoundsCorners(Position(p) - new Vector3(Math.Max(.025f, p.Radius)), Position(p) + new Vector3(Math.Max(.025f, p.Radius))))
            .Concat(Data.Bones.Where(b => SelectedBones.Contains(b.Index)).SelectMany(b => BoundsCorners(BonePosition(b) - new Vector3(.025f), BonePosition(b) + new Vector3(.025f))))
            .Concat(VisibleColliders.Where(c => SelectedColliders.Contains(c.Index)).SelectMany(ColliderGeometry.Create).SelectMany(l => new[] { l.Start, l.End })).ToArray();
        if (points.Length == 0) return;
        var min = points.Aggregate(Vector3.Min); var max = points.Aggregate(Vector3.Max);
        var center = (min + max) / 2;
        var rotated = points.Select(p => Vector3.TransformNormal(p - center, Rotation)).ToArray();
        var size = rotated.Aggregate(Vector3.Max) - rotated.Aggregate(Vector3.Min);
        var pixels = Math.Min(Math.Max(1, Bounds.Width) * .65 / Math.Max(.1f, size.X), Math.Max(1, Bounds.Height) * .65 / Math.Max(.1f, size.Y));
        var zoom = Math.Clamp(pixels * _extent / (Math.Max(1, Math.Min(Bounds.Width, Bounds.Height)) * .78), .00001, 1000000);
        BeginCameraTransition(center, _yaw, _pitch, zoom);
    }
    public void FrameModel() { if (Model != null) Frame(BoundsCorners(Model.BoundsMinimum, Model.BoundsMaximum)); else Fit(); }
    public void ResetView() { CancelCameraTransition(); _yaw = -.55f; _pitch = .35f; Fit(); }
    public void SetView(CameraView view, bool animated = true)
    {
        var (yaw, pitch) = view switch {
            CameraView.Back => (MathF.PI, 0f), CameraView.Left => (MathF.PI / 2, 0f),
            CameraView.Right => (-MathF.PI / 2, 0f), CameraView.Top => (0f, MathF.PI / 2),
            CameraView.Bottom => (0f, -MathF.PI / 2), _ => (0f, 0f) };
        if (animated) BeginCameraTransition(_center, yaw, pitch, _zoom);
        else { CancelCameraTransition(); _yaw = yaw; _pitch = pitch; Changed(); }
    }
    public void Pan(double x, double y)
    {
        CancelCameraTransition();
        _center += Vector3.TransformNormal(new((float)(-x / PixelsPerUnit), (float)(y / PixelsPerUnit), 0), Matrix4x4.Transpose(Rotation)); Changed();
    }
    public void ZoomAt(double steps, Point anchor)
    {
        CancelCameraTransition();
        var before = PixelsPerUnit;
        _zoom = Math.Clamp(_zoom * Math.Pow(1 / .88, Math.Clamp(steps, -20, 20)), .00001, 1000000);
        var after = PixelsPerUnit;
        var offset = new Vector3((float)((anchor.X - Bounds.Width / 2) * (1 / before - 1 / after)), (float)((Bounds.Height / 2 - anchor.Y) * (1 / before - 1 / after)), 0);
        _center += Vector3.TransformNormal(offset, Matrix4x4.Transpose(Rotation)); Changed();
    }
    public void SelectRectangle(Rect rectangle, SelectionOperation operation)
    {
        if (!CanSelect || !ShowPhysics) return;
        SelectionRequested?.Invoke(SelectionPoints().Where(p => rectangle.Contains(Project(p.Position))).Select(p => p.Index).ToArray(), operation);
    }
    private static SelectionOperation Operation(KeyModifiers modifiers) => modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta)
        ? SelectionOperation.Toggle : modifiers.HasFlag(KeyModifiers.Shift) ? SelectionOperation.Add : SelectionOperation.Replace;
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (TransformActive) { FinishTransform(!e.GetCurrentPoint(this).Properties.IsRightButtonPressed); e.Handled = true; return; }
        Focus(); _last = _down = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift); var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        _orbit = (properties.IsMiddleButtonPressed || alt && properties.IsLeftButtonPressed) && !shift;
        _panning = (properties.IsMiddleButtonPressed || alt && properties.IsLeftButtonPressed) && shift;
        _selecting = properties.IsLeftButtonPressed && !alt && CanSelect && ShowPhysics;
        _selectionModifiers = e.KeyModifiers; _selectionRect = default;
        if (_orbit || _panning || _selecting) { _gesturePointer = e.Pointer; e.Pointer.Capture(this); e.Handled = true; }
        Cursor = new Cursor(_panning ? StandardCursorType.SizeAll : _orbit ? StandardCursorType.Hand : StandardCursorType.Arrow);
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this); var delta = p - _last; _last = p;
        if (TransformActive) { MoveTransform(p); e.Handled = true; return; }
        if (_orbit) Orbit((float)delta.X * .01f, (float)delta.Y * .01f);
        else if (_panning) Pan(delta.X, delta.Y);
        else if (_selecting) { _selectionRect = new Rect(_down, p).Normalize(); InvalidateVisual(); }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_selecting && CanSelect)
        {
            var p = e.GetPosition(this); var delta = p - _down; var operation = Operation(_selectionModifiers);
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 4) SelectRectangle(new Rect(_down, p).Normalize(), operation);
            else
            {
                var candidates = SelectionPoints().ToArray();
                var hit = candidates.Select(point => { var d = Project(point.Position) - p; return (point.Index, Distance: Math.Sqrt(d.X * d.X + d.Y * d.Y)); }).OrderBy(t => t.Distance).FirstOrDefault();
                if (candidates.Length > 0 && hit.Distance <= 10) SelectionRequested?.Invoke([hit.Index], operation);
                else if (operation == SelectionOperation.Replace) SelectionRequested?.Invoke([], operation);
            }
            e.Handled = true;
        }
        EndGesture(); e.Pointer.Capture(null);
    }
    private void EndGesture()
    {
        _orbit = _panning = _selecting = false; _selectionRect = default;
        var pointer = _gesturePointer; _gesturePointer = null;
        if (pointer?.Captured == this) pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Arrow); InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { EndGesture(); base.OnPointerCaptureLost(e); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { if (!TransformActive) ZoomAt(e.Delta.Y, e.GetPosition(this)); e.Handled = true; }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (TransformKey(e)) { e.Handled = true; return; }
        if (e.Key == Key.A && CanSelect && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Alt)
        { SelectionRequested?.Invoke(e.KeyModifiers == KeyModifiers.Alt ? [] : SelectionPoints().Select(p => p.Index).ToArray(), SelectionOperation.Replace); e.Handled = true; }
        else if (e.Key == Key.Home) { Fit(); e.Handled = true; }
        else if (e.Key is Key.Decimal or Key.OemPeriod) { FrameSelected(); e.Handled = true; }
        else if (e.Key == Key.NumPad1) { SetView(e.KeyModifiers.HasFlag(KeyModifiers.Control) ? CameraView.Back : CameraView.Front); e.Handled = true; }
        else if (e.Key == Key.NumPad3) { SetView(e.KeyModifiers.HasFlag(KeyModifiers.Control) ? CameraView.Left : CameraView.Right); e.Handled = true; }
        else if (e.Key == Key.NumPad7) { SetView(e.KeyModifiers.HasFlag(KeyModifiers.Control) ? CameraView.Bottom : CameraView.Top); e.Handled = true; }
        else if (e.Key == Key.Escape) { EndGesture(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
