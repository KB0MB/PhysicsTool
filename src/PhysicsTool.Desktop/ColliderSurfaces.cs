using Avalonia.Media;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class PhysicsViewport
{
    public bool ShowColliderSurfaces { get; set; } = true;
    private void DrawColliderSurfaces(DrawingContext context)
    {
        if (!ShowColliderSurfaces) return;
        var view = ViewDirection;
        var light = Vector3.Normalize(new Vector3(-.4f, 1, .6f));
        // Translucent x-ray overlay, like the existing guides: keep volumes visible inside the model.
        foreach (var shape in VisibleColliders.OrderBy(c => Vector3.Dot(c.Start + c.End, view)))
        {
            var surfaces = ColliderGeometry.Surface(shape)
                .Select(t => (Triangle: t, Normal: Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A))))
                .Where(item => shape.Kind == ColliderPreviewKind.Plane || Vector3.Dot(item.Normal, view) > 0)
                .GroupBy(item => (int)(Math.Max(0, Vector3.Dot(item.Normal, light)) * 7));
            // Batch lighting bands to avoid one draw call and brush per triangle.
            foreach (var band in surfaces)
            {
                var shade = .65f + .35f * band.Key / 7;
                var selected = SelectedColliders.Contains(shape.Index);
                var brush = new SolidColorBrush(selected ? IsLightViewport ? Color.FromArgb(55, 35, 100, 140) : Color.FromArgb(65, 180, 224, 245)
                    : Color.FromArgb(48, (byte)(75 * shade), (byte)(205 * shade), (byte)(173 * shade)));
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                    foreach (var item in band)
                    {
                        var t = item.Triangle;
                        path.BeginFigure(Project(t.A), true); path.LineTo(Project(t.B)); path.LineTo(Project(t.C)); path.EndFigure(true);
                    }
                context.DrawGeometry(brush, null, geometry);
            }
        }
    }
}
