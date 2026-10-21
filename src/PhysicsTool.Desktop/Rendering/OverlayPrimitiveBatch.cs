using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace PhysicsTool.Desktop.Rendering;

// Immutable screen-space primitives avoid re-tessellating dense intersecting paths
// on the render thread. Picking remains in PhysicsViewport, not this draw operation.
internal sealed class OverlayPrimitiveBatch(Rect bounds, SKPoint[] points, Color color, float width = 0) : ICustomDrawOperation
{
    public Rect Bounds { get; } = bounds;
    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);
    public void Dispose() { }
    public void Render(ImmediateDrawingContext context)
    {
        if (points.Length == 0) return;
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is { } feature)
        {
            using var lease = feature.Lease();
            using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true, StrokeWidth = width, StrokeCap = SKStrokeCap.Butt };
            if (width > 0) lease.SkCanvas.DrawPoints(SKPointMode.Lines, points, paint);
            else
            {
                using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, points, null);
                lease.SkCanvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
            }
            return;
        }
        var brush = new ImmutableSolidColorBrush(color);
        if (width > 0)
        {
            var pen = new ImmutablePen(brush, width);
            for (int i = 0; i + 1 < points.Length; i += 2)
                context.DrawLine(pen, new(points[i].X, points[i].Y), new(points[i + 1].X, points[i + 1].Y));
        }
        else
        {
            // Uncommon non-Skia drawing contexts still receive the same triangles.
            using var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, (int)Math.Ceiling(Bounds.Width)), Math.Max(1, (int)Math.Ceiling(Bounds.Height))));
            surface.Canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = new SKColor(color.R, color.G, color.B, color.A), IsAntialias = true };
            using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, points, null);
            surface.Canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
            using var image = surface.Snapshot(); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = encoded.AsStream(); using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            context.DrawBitmap(bitmap, Bounds);
        }
    }
}
