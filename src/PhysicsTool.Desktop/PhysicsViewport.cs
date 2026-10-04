using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed class PhysicsViewport : Control
{
    public VisualModelData? Model { get; private set; }
    public HashSet<int> HiddenMeshes { get; } = new();
    public bool GpuReady { get; set; }
    public bool ShowPhysics { get; set; } = true;
    public event Action? CameraChanged;
    public void SetModel(VisualModelData? model) { Model = model; HiddenMeshes.Clear(); Fit(); }
    public void SetMeshVisible(int index, bool visible)
    {
        if (Model == null || index < 0 || index >= Model.Meshes.Count) return;
        if (visible) HiddenMeshes.Remove(index); else HiddenMeshes.Add(index);
        Changed();
    }
    public Matrix4x4 CameraMatrix
    {
        get
        {
            var w = Math.Max(1, Bounds.Width); var h = Math.Max(1, Bounds.Height);
            var scale = Math.Min(w, h) * .78 / _extent * _zoom;
            return Matrix4x4.CreateTranslation(-_center) * Rotation * Matrix4x4.CreateScale((float)(2 * scale / w), (float)(2 * scale / h), -1 / (_extent * 100))
                * Matrix4x4.CreateTranslation((float)(2 * _pan.X / w), (float)(-2 * _pan.Y / h), 0);
        }
    }
    private Matrix4x4 Rotation => Matrix4x4.CreateRotationY(_yaw) * Matrix4x4.CreateRotationX(_pitch);
    public Vector3 ViewDirection => Vector3.TransformNormal(Vector3.UnitZ, Matrix4x4.Transpose(Rotation));
    public void Orbit(float yaw, float pitch) { _yaw += yaw; _pitch = Math.Clamp(_pitch + pitch, -1.5f, 1.5f); Changed(); }
    private void Changed() { InvalidateVisual(); CameraChanged?.Invoke(); }
    public ParticlePreviewData Data { get; set; } = new();
    public IReadOnlyDictionary<int, Vector3>? Simulated { get; set; }
    public HashSet<int> Selected { get; } = new();
    public bool ShowBones { get; set; } = true;
    public bool ShowColliders { get; set; } = true;
    public event Action<int, bool>? ParticlePicked;
    private Vector3 _center;
    private float _extent = 1, _yaw = .3f, _pitch = -.12f;
    private double _zoom = 1;
    private Point _last;
    private Point _pan;
    private bool _orbit, _panning;
    public PhysicsViewport() { ClipToBounds = true; Focusable = true; }
    public void Fit()
    {
        var points = Data.Particles.Select(p => p.Position).Concat(Data.Bones.Select(b => b.Position))
            .Concat(Data.Colliders.SelectMany(c => new[] { c.Start - new Vector3(c.Radius), c.End + new Vector3(c.Radius) })).ToArray();
        if (Model != null) points = points.Concat(new[] { Model.BoundsMinimum, Model.BoundsMaximum }).ToArray();
        if (points.Length == 0) { _center = Vector3.Zero; _extent = 1; }
        else
        {
            var min = points.Aggregate(Vector3.Min); var max = points.Aggregate(Vector3.Max);
            _center = (min + max) / 2; _extent = Math.Max(.1f, (max - min).Length());
        }
        _zoom = 1; _pan = default; Changed();
    }
    private Point Project(Vector3 p)
    {
        var v = Vector3.Transform(p - _center, Matrix4x4.CreateRotationY(_yaw) * Matrix4x4.CreateRotationX(_pitch));
        var scale = Math.Min(Bounds.Width, Bounds.Height) * .78 / _extent * _zoom;
        return new Point(Bounds.Width / 2 + v.X * scale + _pan.X, Bounds.Height / 2 - v.Y * scale + _pan.Y);
    }
    private Vector3 Position(ParticlePreviewPoint p) => Simulated?.GetValueOrDefault(p.Index, p.Position) ?? p.Position;
    public override void Render(DrawingContext context)
    {
        if (Model == null || !GpuReady) context.FillRectangle(new SolidColorBrush(Color.Parse("#242829")), Bounds.WithX(0).WithY(0));
        else context.FillRectangle(Brushes.Transparent, Bounds.WithX(0).WithY(0));
        var grid = new Pen(new SolidColorBrush(Color.Parse("#3B4142")), 1);
        var step = MathF.Pow(10, MathF.Floor(MathF.Log10(_extent))) / 5;
        for (var i = -10; i <= 10 && (Model == null || !GpuReady); i++)
        {
            context.DrawLine(grid, Project(_center + new Vector3(i * step, -_extent / 2, -10 * step)), Project(_center + new Vector3(i * step, -_extent / 2, 10 * step)));
            context.DrawLine(grid, Project(_center + new Vector3(-10 * step, -_extent / 2, i * step)), Project(_center + new Vector3(10 * step, -_extent / 2, i * step)));
        }
        if (Model != null && !GpuReady)
        {
            var wire = new Pen(new SolidColorBrush(Color.Parse("#8CADA6")), .7);
            var stride = Math.Max(1, (int)Math.Ceiling(Model.TriangleCount / 30000d)) * 3;
            for (int index = 0; index < Model.Meshes.Count; index++)
            {
                if (HiddenMeshes.Contains(index)) continue;
                var mesh = Model.Meshes[index];
                Point Vertex(uint i) { var offset = checked((int)i * 8); return Project(new(mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2])); }
                for (int i = 0; i + 2 < mesh.Indices.Length; i += stride)
                { var a = Vertex(mesh.Indices[i]); var b = Vertex(mesh.Indices[i + 1]); var c = Vertex(mesh.Indices[i + 2]); context.DrawLine(wire, a, b); context.DrawLine(wire, b, c); context.DrawLine(wire, c, a); }
            }
        }
        if (!ShowPhysics) return;
        var points = Data.Particles.ToDictionary(p => p.Index, Position);
        void Edge(int a, int b, IPen pen) { if (points.TryGetValue(a, out var x) && points.TryGetValue(b, out var y)) context.DrawLine(pen, Project(x), Project(y)); }
        var face = new Pen(new SolidColorBrush(Color.Parse("#59656A")), 1);
        foreach (var t in Data.Triangles) { Edge(t.ParticleA, t.ParticleB, face); Edge(t.ParticleB, t.ParticleC, face); Edge(t.ParticleC, t.ParticleA, face); }
        var link = new Pen(new SolidColorBrush(Color.Parse("#92AAB6")), 1.3);
        foreach (var l in Data.Links) Edge(l.ParticleA, l.ParticleB, link);
        if (ShowColliders)
        {
            var pen = new Pen(new SolidColorBrush(Color.Parse("#55C8AE")), 1);
            foreach (var c in Data.Colliders)
            {
                if (c.Kind == ColliderPreviewKind.Plane)
                {
                    var normal = Vector3.Normalize(c.PlaneNormal); var u = Vector3.Cross(normal, Math.Abs(normal.Y) > .9 ? Vector3.UnitX : Vector3.UnitY);
                    u = Vector3.Normalize(u) * _extent / 3; var v = Vector3.Cross(normal, u);
                    var corners = new[] { c.Start + u + v, c.Start - u + v, c.Start - u - v, c.Start + u - v };
                    for (int i = 0; i < 4; i++) context.DrawLine(pen, Project(corners[i]), Project(corners[(i + 1) % 4]));
                    continue;
                }
                foreach (var center in new[] { c.Start, c.End })
                    for (int axis = 0; axis < 3; axis++)
                        for (int i = 0; i < 32; i++)
                        {
                            Vector3 Ring(double angle) => axis switch { 0 => new(0, (float)Math.Cos(angle), (float)Math.Sin(angle)), 1 => new((float)Math.Cos(angle), 0, (float)Math.Sin(angle)), _ => new((float)Math.Cos(angle), (float)Math.Sin(angle), 0) };
                            context.DrawLine(pen, Project(center + Ring(i * Math.PI / 16) * c.Radius), Project(center + Ring((i + 1) * Math.PI / 16) * c.Radius));
                        }
                context.DrawLine(pen, Project(c.Start), Project(c.End));
            }
        }
        if (ShowBones)
        {
            var bones = Data.Bones.GroupBy(b => b.Index).ToDictionary(g => g.Key, g => g.First());
            var pen = new Pen(new SolidColorBrush(Color.Parse("#D5B866")), 1.5);
            foreach (var b in Data.Bones) if (bones.TryGetValue(b.ParentIndex, out var parent)) context.DrawLine(pen, Project(b.Position), Project(parent.Position));
        }
        foreach (var p in Data.Particles)
        {
            var selected = Selected.Contains(p.Index);
            context.DrawEllipse(selected ? Brushes.White : p.Fixed ? new SolidColorBrush(Color.Parse("#EF7C81")) : new SolidColorBrush(Color.Parse("#70B6EC")), null,
                Project(Position(p)), selected ? 5 : 3.5, selected ? 5 : 3.5);
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus(); _last = e.GetPosition(this); _orbit = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
        _panning = e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed;
        if (_orbit || _panning) e.Pointer.Capture(this);
        else if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (!ShowPhysics) return;
            var hit = Data.Particles.Select(p => { var delta = Project(Position(p)) - _last; return (p.Index, Distance: Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y)); }).OrderBy(p => p.Distance).FirstOrDefault();
            if (Data.Particles.Count > 0 && hit.Distance < 12) ParticlePicked?.Invoke(hit.Index, e.KeyModifiers.HasFlag(KeyModifiers.Control));
        }
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_orbit && !_panning) return;
        var p = e.GetPosition(this); var delta = p - _last; _last = p;
        if (_panning) { _pan += delta; Changed(); return; }
        Orbit((float)delta.X * .008f, (float)delta.Y * .008f);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { _orbit = _panning = false; e.Pointer.Capture(null); }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e) { _zoom = Math.Clamp(_zoom * Math.Pow(1.15, e.Delta.Y), .02, 100); Changed(); e.Handled = true; }
}
