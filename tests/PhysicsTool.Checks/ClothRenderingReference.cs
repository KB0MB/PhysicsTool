using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Desktop;
using System.Numerics;

// Reference for the original unbatched dark-theme overlay, independent of renderer caches.
internal static class ClothRenderingReference
{
    public static void Draw(PhysicsViewport viewport, DrawingContext context)
    {
        if (!viewport.ShowPhysics) return;
        var data = viewport.Data;
        var view = viewport.ViewDirection;
        if (viewport.ShowColliders && viewport.ShowColliderSurfaces)
        {
            var light = Vector3.Normalize(new Vector3(-.4f, 1, .6f));
            foreach (var shape in data.Colliders.OrderBy(c => Vector3.Dot(c.Start + c.End, view)))
            {
                var surfaces = ColliderGeometry.Surface(shape)
                    .Select(t => (Triangle: t, Normal: Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A))))
                    .Where(item => shape.Kind == ColliderPreviewKind.Plane || Vector3.Dot(item.Normal, view) > 0)
                    .GroupBy(item => (int)(Math.Max(0, Vector3.Dot(item.Normal, light)) * 7));
                foreach (var band in surfaces)
                {
                    var shade = .65f + .35f * band.Key / 7;
                    var brush = new SolidColorBrush(Color.FromArgb(48, (byte)(75 * shade), (byte)(205 * shade), (byte)(173 * shade)));
                    var geometry = new StreamGeometry();
                    using (var path = geometry.Open())
                        foreach (var item in band)
                        {
                            var t = item.Triangle;
                            path.BeginFigure(viewport.Project(t.A), true); path.LineTo(viewport.Project(t.B)); path.LineTo(viewport.Project(t.C)); path.EndFigure(true);
                        }
                    context.DrawGeometry(brush, null, geometry);
                }
            }
        }
        var points = data.Particles.ToDictionary(p => p.Index, p => p.Position);
        void Edge(int a, int b, IPen pen)
        {
            if (points.TryGetValue(a, out var x) && points.TryGetValue(b, out var y))
                context.DrawLine(pen, viewport.Project(x), viewport.Project(y));
        }
        var face = new Pen(new SolidColorBrush(Color.Parse("#59656A")), 1);
        foreach (var t in data.Triangles) { Edge(t.ParticleA, t.ParticleB, face); Edge(t.ParticleB, t.ParticleC, face); Edge(t.ParticleC, t.ParticleA, face); }
        var link = new Pen(new SolidColorBrush(Color.Parse("#92AAB6")), 1.3);
        foreach (var l in data.Links) Edge(l.ParticleA, l.ParticleB, link);
        if (viewport.ShowColliders)
        {
            var pen = new Pen(new SolidColorBrush(Color.Parse("#55C8AE")), 1);
            foreach (var c in data.Colliders)
                foreach (var line in ColliderGeometry.Create(c)) context.DrawLine(pen, viewport.Project(line.Start), viewport.Project(line.End));
        }
        foreach (var p in data.Particles)
            context.DrawEllipse(new SolidColorBrush(Color.Parse(p.Fixed ? "#EF7C81" : "#70B6EC")), null, viewport.Project(p.Position), 3.5, 3.5);
    }
}
