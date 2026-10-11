using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class PhysicsViewport : Control
{
    public VisualModelData? Model { get; private set; }
    public HashSet<int> HiddenMeshes { get; } = new();
    public bool GpuReady { get; set; }
    public bool Wireframe { get; set; }
    public bool ShowPhysics { get; set; } = true;
    public bool ShowEmission { get; set; }
    public event Action? CameraChanged;
    public void SetModel(VisualModelData? model) { Model = model; HiddenMeshes.Clear(); Fit(); }
    public void SetModelPose(VisualModelData model) { Model = model; Changed(); }
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
    public Matrix4x4 Rotation => Matrix4x4.CreateRotationY(_yaw) * Matrix4x4.CreateRotationX(_pitch);
    public Vector3 ViewDirection => Vector3.TransformNormal(Vector3.UnitZ, Matrix4x4.Transpose(Rotation));
    public void Orbit(float yaw, float pitch) { CancelCameraTransition(); _yaw = MathF.IEEERemainder(_yaw + yaw, MathF.Tau); _pitch = Math.Clamp(_pitch + pitch, -1.56f, 1.56f); Changed(); }
    private void Changed() { InvalidateVisual(); CameraChanged?.Invoke(); }
    public ParticlePreviewData Data { get; set; } = new();
    public IReadOnlyDictionary<int, Vector3>? Simulated { get; set; }
    public IReadOnlyDictionary<int, SimulatedBonePreviewPose>? SimulatedBones { get; set; }
    public IReadOnlyDictionary<int, ColliderPreviewShape>? SimulatedColliders { get; set; }
    private IEnumerable<ColliderPreviewShape> VisibleColliders => Data.Colliders.Select(c => SimulatedColliders?.GetValueOrDefault(c.Index, c) ?? c);
    private Vector3 BonePosition(BonePreviewPoint bone) => SimulatedBones?.GetValueOrDefault(bone.Index)?.Position ?? bone.Position;
    public HashSet<int> Selected { get; } = new();
    public HashSet<int> SelectedBones { get; } = new();
    public HashSet<int> SelectedColliders { get; } = new();
    public HashSet<int> SelectedRagdollConstraints { get; } = new();
    public bool ShowBones { get; set; } = true;
    public bool ShowColliders { get; set; } = true;
    private Vector3 _center;
    private float _extent = 1, _yaw = -.55f, _pitch = .35f;
    private double _zoom = 1;
    private Point _last;
    private Point _pan;
    private bool _orbit, _panning;
    public PhysicsViewport() { ClipToBounds = true; Focusable = true; ConfigureNavigation(); ConfigureCameraTransitions(); ActualThemeVariantChanged += (_, _) => Changed(); }
    public void Fit()
    {
        var points = Data.Particles.Select(p => p.Position).Concat(Data.Bones.Select(b => b.Position))
            .Concat(VisibleColliders.SelectMany(ColliderGeometry.Create).SelectMany(line => new[] { line.Start, line.End })).ToArray();
        if (Model != null) points = points.Concat(BoundsCorners(Model.BoundsMinimum, Model.BoundsMaximum)).ToArray();
        Frame(points);
    }
    public Point Project(Vector3 p)
    {
        var v = Vector3.Transform(p - _center, Matrix4x4.CreateRotationY(_yaw) * Matrix4x4.CreateRotationX(_pitch));
        var scale = Math.Min(Bounds.Width, Bounds.Height) * .78 / _extent * _zoom;
        return new Point(Bounds.Width / 2 + v.X * scale + _pan.X, Bounds.Height / 2 - v.Y * scale + _pan.Y);
    }
    private Vector3 Position(ParticlePreviewPoint p) => Simulated?.GetValueOrDefault(p.Index, p.Position) ?? p.Position;
    public override void Render(DrawingContext context)
    {
        if (Model == null || !GpuReady || Wireframe) context.FillRectangle(new SolidColorBrush(ViewportBackground), Bounds.WithX(0).WithY(0));
        else context.FillRectangle(Brushes.Transparent, Bounds.WithX(0).WithY(0));
        if (Model == null || !GpuReady || Wireframe)
        foreach (var line in GridLines)
        {
            var color = Color.FromRgb((byte)(line.Color.X * 255), (byte)(line.Color.Y * 255), (byte)(line.Color.Z * 255));
            context.DrawLine(new Pen(new SolidColorBrush(color), 1), Project(line.Start), Project(line.End));
        }
        if (Model != null && (!GpuReady || Wireframe))
        {
            var wire = new Pen(new SolidColorBrush(ViewportColor("#8CADA6", "#4B6B65")), .7);
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
        if (_selecting && _selectionRect.Width > 3 && _selectionRect.Height > 3)
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 98, 174, 235)), new Pen(new SolidColorBrush(Color.Parse("#78B7E8")), 1), _selectionRect);
        if (!ShowPhysics) return;
        if (ShowColliders) DrawColliderSurfaces(context);
        var points = Data.Particles.ToDictionary(p => p.Index, Position);
        void Edge(int a, int b, IPen pen) { if (points.TryGetValue(a, out var x) && points.TryGetValue(b, out var y)) context.DrawLine(pen, Project(x), Project(y)); }
        var face = new Pen(new SolidColorBrush(ViewportColor("#59656A", "#87959C")), 1);
        foreach (var t in Data.Triangles) { Edge(t.ParticleA, t.ParticleB, face); Edge(t.ParticleB, t.ParticleC, face); Edge(t.ParticleC, t.ParticleA, face); }
        var link = new Pen(new SolidColorBrush(ViewportColor("#92AAB6", "#506E7E")), 1.3);
        foreach (var l in Data.Links) Edge(l.ParticleA, l.ParticleB, link);
        if (ShowColliders)
        {
            if (Data.RagdollConstraints.Count > 0)
            {
                var bodies = VisibleColliders.ToDictionary(c => c.Index);
                foreach (var joint in Data.RagdollConstraints)
                    if (bodies.TryGetValue(joint.BodyA, out var a) && bodies.TryGetValue(joint.BodyB, out var b))
                        context.DrawLine(new Pen(SelectedRagdollConstraints.Contains(joint.Index) ? SelectionBrush : new SolidColorBrush(ViewportColor("#9EA4AC", "#69717B")), 1.5),
                            Project((a.Start + a.End) * .5f), Project((b.Start + b.End) * .5f));
            }
            var pen = new Pen(new SolidColorBrush(ViewportColor("#55C8AE", "#147D69")), 1);
            foreach (var c in VisibleColliders)
                foreach (var line in ColliderGeometry.Create(c))
                    context.DrawLine(SelectedColliders.Contains(c.Index) ? new Pen(SelectionBrush, 1.5) : pen, Project(line.Start), Project(line.End));
        }
        if (ShowBones) DrawBones(context);
        foreach (var p in Data.Particles)
        {
            var selected = Selected.Contains(p.Index);
            context.DrawEllipse(selected ? SelectionBrush : p.Fixed ? new SolidColorBrush(ViewportColor("#EF7C81", "#B33745")) : new SolidColorBrush(ViewportColor("#70B6EC", "#226EAC")), null,
                Project(Position(p)), selected ? 5 : 3.5, selected ? 5 : 3.5);
        }
        DrawTransformGuides(context);
    }
}
