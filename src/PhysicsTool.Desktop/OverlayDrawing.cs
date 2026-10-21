using Avalonia;
using Avalonia.Media;
using HKCLTool;
using System.Numerics;
using SkiaSharp;
using PhysicsTool.Desktop.Rendering;

namespace PhysicsTool.Desktop;

public sealed partial class PhysicsViewport
{
    private readonly Dictionary<int, Point> _projectedParticles = new();
    private readonly HashSet<(int, int)> _drawnEdges = new();

    private void DrawParticleEdges(DrawingContext context, IPen pen, bool triangles)
    {
        _drawnEdges.Clear();
        var points = new List<SKPoint>();
        {
            void Edge(int a, int b)
            {
                if (!_projectedParticles.TryGetValue(a, out var x) || !_projectedParticles.TryGetValue(b, out var y)) return;
                if (!_drawnEdges.Add(a < b ? (a, b) : (b, a))) return;
                points.Add(new((float)x.X, (float)x.Y)); points.Add(new((float)y.X, (float)y.Y));
            }
            if (triangles)
                foreach (var t in Data.Triangles) { Edge(t.ParticleA, t.ParticleB); Edge(t.ParticleB, t.ParticleC); Edge(t.ParticleC, t.ParticleA); }
            else
                foreach (var link in Data.Links) Edge(link.ParticleA, link.ParticleB);
        }
        context.Custom(new OverlayPrimitiveBatch(new Rect(Bounds.Size), points.ToArray(), ((ISolidColorBrush)pen.Brush!).Color, (float)pen.Thickness));
    }

    private readonly record struct ColliderDrawKey(ColliderPreviewKind Kind, Vector3 Start, Vector3 End,
        float Radius, float EndRadius, Vector3 Normal)
    {
        public static ColliderDrawKey From(ColliderPreviewShape c) => new(c.Kind, c.Start, c.End, c.Radius, c.EndRadius, c.PlaneNormal);
    }
    private sealed class ColliderDrawMesh
    {
        public readonly IReadOnlyList<ColliderLine> Lines;
        private (ColliderTriangle Triangle, Vector3 Normal)[][]? _bands;
        public (ColliderTriangle Triangle, Vector3 Normal)[][] GetBands(ColliderPreviewShape shape)
        {
            if (_bands != null) return _bands;
            return _bands = BuildBands(shape);
        }
        public ColliderDrawMesh(ColliderPreviewShape shape)
        {
            Lines = ColliderGeometry.Create(shape);
        }
        private static (ColliderTriangle Triangle, Vector3 Normal)[][] BuildBands(ColliderPreviewShape shape)
        {
            var light = Vector3.Normalize(new Vector3(-.4f, 1, .6f));
            var bands = Enumerable.Range(0, 8).Select(_ => new List<(ColliderTriangle, Vector3)>()).ToArray();
            foreach (var t in ColliderGeometry.Surface(shape))
            {
                var normal = Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A));
                int band = Math.Clamp((int)(Math.Max(0, Vector3.Dot(normal, light)) * 7), 0, 7);
                bands[band].Add((t, normal));
            }
            return bands.Select(b => b.ToArray()).ToArray();
        }
    }
    private Dictionary<ColliderDrawKey, ColliderDrawMesh> _colliderMeshes = new();
    private Dictionary<ColliderDrawKey, ColliderDrawMesh> _nextColliderMeshes = new();
    private readonly List<(ColliderPreviewShape Shape, ColliderDrawMesh Mesh)> _drawColliders = new();
    private void ClearColliderDrawing()
    {
        _colliderMeshes.Clear(); _nextColliderMeshes.Clear(); _drawColliders.Clear();
    }
    private void PrepareColliderDrawing()
    {
        _drawColliders.Clear(); _nextColliderMeshes.Clear();
        foreach (var shape in VisibleColliders)
        {
            var key = ColliderDrawKey.From(shape);
            if (_nextColliderMeshes.TryGetValue(key, out var mesh))
            {
                // Actor overlays repeat shared collider volumes for multiple cloths.
                // Editors retain each independently selectable collider.
                if (ReadOnlyActorView) continue;
            }
            else
            {
                if (!_colliderMeshes.TryGetValue(key, out mesh)) mesh = new ColliderDrawMesh(shape);
                _nextColliderMeshes.Add(key, mesh);
            }
            _drawColliders.Add((shape, mesh));
        }
        (_colliderMeshes, _nextColliderMeshes) = (_nextColliderMeshes, _colliderMeshes);
        _nextColliderMeshes.Clear(); // Retain only current poses, never an animation history.
    }
}
