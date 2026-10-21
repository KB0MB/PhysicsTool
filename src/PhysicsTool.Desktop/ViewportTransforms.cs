using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using PhysicsTool.Core;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class PhysicsViewport
{
    public PhysicsSelectionKind SelectionKind { get; set; }
    public event Action? TransformStarted;
    public event Action<Matrix4x4>? TransformPreview;
    public event Action<Matrix4x4?>? TransformFinished;
    internal bool TransformActive => _transformKey != Key.None;
    internal bool IsScaleTransform => _transformKey == Key.S;
    internal Vector3 TransformPivot => _transformPivot;
    private Key _transformKey, _transformAxis;
    private bool _transformPlane;
    private Point _transformStart;
    private Vector3 _transformPivot;
    private Matrix4x4 _transformDelta = Matrix4x4.Identity;

    private IEnumerable<(int Index, Vector3 Position)> SelectionPoints() => SelectionKind switch {
        PhysicsSelectionKind.Bones => Data.Bones.Where(b => ShowBones && b.IsSelectable && !b.IsReference).Select(b => (b.Index, BonePosition(b))),
        PhysicsSelectionKind.Colliders => VisibleColliders.Where(_ => ShowColliders).Select(c => (c.Index, (c.Start + c.End) * .5f)),
        _ => Data.Particles.Where(_ => ShowParticles).Select(p => (p.Index, Position(p))) };
    private HashSet<int> ActiveSelection => SelectionKind switch {
        PhysicsSelectionKind.Bones => SelectedBones, PhysicsSelectionKind.Colliders => SelectedColliders, _ => Selected };

    internal bool TransformKey(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            if (TransformActive && e.Key is Key.Z or Key.Y) FinishTransform(false);
            return false;
        }
        if (TransformActive)
        {
            if (e.Key == Key.Escape) FinishTransform(false);
            else if (e.Key == Key.Enter) FinishTransform(true);
            else if (e.Key is Key.X or Key.Y or Key.Z && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                var plane = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                _transformAxis = _transformAxis == e.Key && _transformPlane == plane ? Key.None : e.Key;
                _transformPlane = _transformAxis != Key.None && plane;
                MoveTransform(_last);
            }
            return true;
        }
        if (!CanSelect || !ShowPhysics || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.G or Key.R or Key.S)) return false;
        var points = SelectionPoints().Where(p => ActiveSelection.Contains(p.Index)).Select(p => p.Position).ToArray();
        if (points.Length == 0) return false;
        EndGesture(); CancelCameraTransition();
        _transformPivot = points.Aggregate(Vector3.Zero, (a, b) => a + b) / points.Length;
        _transformStart = _last; _transformAxis = Key.None; _transformPlane = false; _transformKey = e.Key; _transformDelta = Matrix4x4.Identity;
        TransformStarted?.Invoke(); return true;
    }
    private void MoveTransform(Point point)
    {
        var d = point - _transformStart;
        var axis = _transformAxis switch { Key.X => Vector3.UnitX, Key.Y => Vector3.UnitY, Key.Z => Vector3.UnitZ, _ => ViewDirection };
        Matrix4x4 transform;
        if (_transformKey == Key.G)
        {
            var movement = Vector3.TransformNormal(new((float)(d.X / PixelsPerUnit), (float)(-d.Y / PixelsPerUnit), 0), Matrix4x4.Transpose(Rotation));
            if (_transformAxis != Key.None)
            {
                if (_transformPlane)
                {
                    // Intersect the orthographic pointer ray with the constraint plane.
                    // Near edge-on views use the visible planar component to avoid huge jumps.
                    var facing = Vector3.Dot(ViewDirection, axis);
                    movement = Math.Abs(facing) > .1f
                        ? movement - ViewDirection * (Vector3.Dot(movement, axis) / facing)
                        : movement - axis * Vector3.Dot(movement, axis);
                    movement -= axis * Vector3.Dot(movement, axis);
                }
                else
                {
                    var screenAxis = Vector3.TransformNormal(axis, Rotation);
                    var length = screenAxis.X * screenAxis.X + screenAxis.Y * screenAxis.Y;
                    var distance = length > .01f
                        ? (float)((d.X * screenAxis.X - d.Y * screenAxis.Y) / (PixelsPerUnit * length))
                        : (float)(-d.Y / PixelsPerUnit);
                    movement = axis * distance;
                }
            }
            transform = Matrix4x4.CreateTranslation(movement);
        }
        else if (_transformKey == Key.R) transform = Matrix4x4.CreateFromAxisAngle(axis, (float)(d.X - d.Y) * .01f);
        else
        {
            var amount = (float)Math.Exp(Math.Clamp((d.X - d.Y) * .01, -6, 6));
            var scale = _transformAxis == Key.None ? new Vector3(amount) : Vector3.One + (_transformPlane ? Vector3.One - axis : axis) * (amount - 1);
            transform = Matrix4x4.CreateScale(scale);
        }
        _transformDelta = Matrix4x4.CreateTranslation(-_transformPivot) * transform * Matrix4x4.CreateTranslation(_transformPivot);
        TransformPreview?.Invoke(_transformDelta); InvalidateVisual();
    }
    internal IEnumerable<Key> TransformGuideAxes => !TransformActive || _transformAxis == Key.None
        ? [] : new[] { Key.X, Key.Y, Key.Z }.Where(key => _transformPlane ? key != _transformAxis : key == _transformAxis);

    private void DrawTransformGuides(DrawingContext context)
    {
        var pivot = Project(_transformPivot);
        var length = Math.Sqrt(Bounds.Width * Bounds.Width + Bounds.Height * Bounds.Height) +
            Math.Abs(pivot.X) + Math.Abs(pivot.Y);
        foreach (var key in TransformGuideAxes)
        {
            var axis = key switch { Key.X => Vector3.UnitX, Key.Y => Vector3.UnitY, _ => Vector3.UnitZ };
            var color = key switch { Key.X => ViewportColor("#EF7378", "#B33745"), Key.Y => ViewportColor("#79CD8F", "#237841"), _ => ViewportColor("#74ACF0", "#226EAC") };
            var pen = new Pen(new SolidColorBrush(color), 1.5);
            var direction = Project(_transformPivot + axis) - pivot;
            var projectedLength = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (projectedLength > .01)
            {
                direction *= length / projectedLength;
                context.DrawLine(pen, pivot - direction, pivot + direction);
            }
            else context.DrawEllipse(null, pen, pivot, 5, 5);
        }
    }
    internal void FinishTransform(bool commit)
    {
        if (!TransformActive) return;
        _transformKey = Key.None;
        TransformFinished?.Invoke(commit ? _transformDelta : null);
        InvalidateVisual();
    }
}
