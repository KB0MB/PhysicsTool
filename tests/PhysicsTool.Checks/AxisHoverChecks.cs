using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using PhysicsTool.Desktop;
using System.Reflection;

internal static class AxisHoverChecks
{
    internal static void Run()
    {
        var view = new PhysicsViewport();
        var axes = new ViewportAxes(view);
        var window = new Window { Width = 360, Height = 240, Content = new Grid { Children = { view, axes } } };
        window.Show(); view.SetView(CameraView.Front, false); Dispatcher.UIThread.RunJobs();
        var field = typeof(ViewportAxes).GetField("_hovered", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string? Hover() => field.GetValue(axes)?.GetType().GetProperty("Label")?.GetValue(field.GetValue(axes)) as string;
        byte[] Capture()
        {
            using var bitmap = window.CaptureRenderedFrame()!; using var stream = new MemoryStream(); bitmap.Save(stream); return stream.ToArray();
        }
        var before = Capture();
        var top = axes.TranslatePoint(new Point(54, 20), window)!.Value;
        window.MouseMove(top); Dispatcher.UIThread.RunJobs();
        var hovered = Capture();
        if (Hover() != "Y" || before.SequenceEqual(hovered)) throw new Exception("Axis bubble did not highlight on hover");
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PhysicsTool-axes-hover-check"));
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "PhysicsTool-axes-hover-check", "hover.png"), hovered);
        window.MouseMove(new Point(5, 200)); Dispatcher.UIThread.RunJobs();
        if (Hover() != null) throw new Exception("Axis highlight stuck after pointer exit");
        var right = axes.TranslatePoint(new Point(88, 54), window)!.Value;
        window.MouseMove(right); Dispatcher.UIThread.RunJobs();
        if (Hover() != "X") throw new Exception("Axis hover and hit target disagree");
        var previous = view.CameraState;
        window.MouseDown(right, MouseButton.Left); window.MouseUp(right, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        view.AdvanceCameraTransition(.3);
        if (view.CameraState == previous) throw new Exception("Axis hover broke camera selection");
        window.Close();
        Console.WriteLine("PASS: axis hover pixels, correct hit target, exit cleanup and click navigation.");
    }
}
