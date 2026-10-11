using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop;

internal sealed class HelperCurvePlot : Control
{
    public IReadOnlyList<Vector4> Keys { get; set; } = [];
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Keys.Count == 0 || Keys.Any(k => !float.IsFinite(k.X + k.Y + k.Z + k.W))) return;
        var rect = new Rect(8, 8, Math.Max(1, Bounds.Width - 16), Math.Max(1, Bounds.Height - 16));
        var minX = Keys.Min(k => k.X); var maxX = Keys.Max(k => k.X); if (maxX - minX < 1e-6f) { minX -= .5f; maxX += .5f; }
        var samples = Enumerable.Range(0, 161).Select(i => new Vector2(minX + (maxX - minX) * i / 160f, 0)).ToArray();
        for (var i = 0; i < samples.Length; i++) samples[i].Y = SidecarCurveRow.Sample(Keys, samples[i].X);
        if (samples.Any(p => !float.IsFinite(p.Y))) return;
        var minY = Math.Min(0, samples.Min(p => p.Y)); var maxY = Math.Max(0, samples.Max(p => p.Y));
        var pad = Math.Max(.05f, (maxY - minY) * .1f); minY -= pad; maxY += pad;
        Point Map(float x, float y) => new(rect.Left + (x - minX) / (maxX - minX) * rect.Width, rect.Bottom - (y - minY) / (maxY - minY) * rect.Height);
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(55, 128, 128, 128)), 1);
        for (var i = 0; i <= 4; i++)
        {
            var x = rect.Left + rect.Width * i / 4; var y = rect.Top + rect.Height * i / 4;
            context.DrawLine(grid, new(x, rect.Top), new(x, rect.Bottom)); context.DrawLine(grid, new(rect.Left, y), new(rect.Right, y));
        }
        var axis = new Pen(Brushes.Gray, 1);
        context.DrawLine(axis, Map(minX, 0), Map(maxX, 0));
        if (minX <= 0 && maxX >= 0) context.DrawLine(axis, Map(0, minY), Map(0, maxY));
        var line = new Pen(Brushes.DeepSkyBlue, 2);
        for (var i = 1; i < samples.Length; i++) context.DrawLine(line, Map(samples[i - 1].X, samples[i - 1].Y), Map(samples[i].X, samples[i].Y));
        foreach (var key in Keys) context.DrawEllipse(Brushes.Gold, new Pen(Brushes.DimGray, 1), Map(key.X, key.Y), 4, 4);
    }
}
