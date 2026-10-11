using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lucide.Avalonia;
using System.Globalization;
using System.Numerics;

namespace PhysicsTool.Desktop;

public static class ViewportTools
{
    public static Button Icon(string name, string tooltip, Action action)
    {
        var button = new Button { Content = new LucideIcon { Kind = Enum.Parse<LucideIconKind>(name), Size = 18 }, Width = 32, Height = 32, Padding = new Thickness(6), Margin = new Thickness(2), Focusable = false };
        Avalonia.Automation.AutomationProperties.SetName(button, tooltip);
        ToolTip.SetTip(button, tooltip); button.Click += (_, _) => action(); return button;
    }
    public static Control CameraToolbar(PhysicsViewport view)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        bar.Children.Add(Icon("Maximize", "Frame all (Home)", view.Fit));
        bar.Children.Add(Icon("Focus", "Frame selected (.)", view.FrameSelected));
        bar.Children.Add(Icon("ZoomIn", "Zoom in", () => view.ZoomAt(1, new(view.Bounds.Width / 2, view.Bounds.Height / 2))));
        bar.Children.Add(Icon("ZoomOut", "Zoom out", () => view.ZoomAt(-1, new(view.Bounds.Width / 2, view.Bounds.Height / 2))));
        foreach (var button in bar.Children.OfType<Button>())
        {
            button.Background = new SolidColorBrush(Color.Parse("#3C4245"));
            button.Foreground = Brushes.White;
        }
        return bar;
    }
}

public sealed class ViewportAxes : Control
{
    private readonly PhysicsViewport _view;
    private record Axis(Vector3 Direction, CameraView View, string Label, Color Color);
    private static readonly Axis[] Axes = [new(Vector3.UnitX, CameraView.Right, "X", Color.Parse("#ED7676")), new(-Vector3.UnitX, CameraView.Left, "-X", Color.Parse("#984B4B")),
        new(Vector3.UnitY, CameraView.Top, "Y", Color.Parse("#7CC889")), new(-Vector3.UnitY, CameraView.Bottom, "-Y", Color.Parse("#4D8057")),
        new(Vector3.UnitZ, CameraView.Front, "Z", Color.Parse("#7AAFE8")), new(-Vector3.UnitZ, CameraView.Back, "-Z", Color.Parse("#4E729B"))];
    public ViewportAxes(PhysicsViewport view) { _view = view; Width = Height = 108; Margin = new Thickness(8); HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Top; view.CameraChanged += InvalidateVisual; }
    private (Axis Axis, Point Point, float Depth)[] Points() => Axes.Select(axis => { var p = Vector3.TransformNormal(axis.Direction, _view.Rotation); return (axis, new Point(54 + p.X * 34, 54 - p.Y * 34), p.Z); }).OrderBy(a => a.Z).ToArray();
    public override void Render(DrawingContext context)
    {
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, 42, 43, 44)), null, new Point(54, 54), 51, 51);
        foreach (var (axis, point, _) in Points())
        {
            var brush = new SolidColorBrush(axis.Color); context.DrawLine(new Pen(brush, 2), new Point(54, 54), point);
            context.DrawEllipse(brush, null, point, 10, 10);
            var text = new FormattedText(axis.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 10, Brushes.Black);
            context.DrawText(text, new Point(point.X - text.Width / 2, point.Y - text.Height / 2));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this); var hit = Points().Reverse().FirstOrDefault(a => Distance(a.Point, point) <= 13);
        if (hit.Axis != null) { _view.SetView(hit.Axis.View); e.Handled = true; }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var point = e.GetPosition(this); var hit = Points().Reverse().FirstOrDefault(a => Distance(a.Point, point) <= 13);
        ToolTip.SetTip(this, hit.Axis == null ? "Axis views" : hit.Axis.View + " view");
    }
    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
