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
        // Translucent x-ray overlay, like the existing guides: keep volumes visible inside the model.
        foreach (var (shape, mesh) in _drawColliders.OrderBy(c => Vector3.Dot(c.Shape.Start + c.Shape.End, view)))
        {
            var bands = mesh.GetBands(shape);
            // Batch lighting bands to avoid one draw call and brush per triangle.
            for (int band = 0; band < bands.Length; band++)
            {
                var shade = .65f + .35f * band / 7;
                var selected = SelectedColliders.Contains(shape.Index);
                var brush = new SolidColorBrush(selected ? IsLightViewport ? Color.FromArgb(55, 35, 100, 140) : Color.FromArgb(65, 180, 224, 245)
                    : Color.FromArgb(48, (byte)(75 * shade), (byte)(205 * shade), (byte)(173 * shade)));
                var points = new List<SkiaSharp.SKPoint>();
                    foreach (var item in bands[band])
                    {
                        if (shape.Kind != ColliderPreviewKind.Plane && Vector3.Dot(item.Normal, view) <= 0) continue;
                        var t = item.Triangle;
                        var a = Project(t.A); var b = Project(t.B); var c = Project(t.C);
                        points.Add(new((float)a.X, (float)a.Y)); points.Add(new((float)b.X, (float)b.Y)); points.Add(new((float)c.X, (float)c.Y));
                    }
                if (points.Count > 0) context.Custom(new Rendering.OverlayPrimitiveBatch(new Avalonia.Rect(Bounds.Size), points.ToArray(), brush.Color));
            }
        }
    }
}
