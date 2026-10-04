using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class NavigationChecks
{
    public static void Run(MainWindow window)
    {
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        var view = window.Viewport;
        view.Pan(60, 40); view.ZoomAt(2, new(100, 150));
        Require(view.GridLines.All(line => line.Start.Y == 0 && line.End.Y == 0), "Grid moved off the world ground plane");
        foreach (var line in view.GridLines.Take(4))
        {
            var cpu = view.Project(line.Start);
            var clip = Vector3.Transform(line.Start, view.CameraMatrix);
            var gpu = new Point((clip.X + 1) * view.Bounds.Width / 2, (1 - clip.Y) * view.Bounds.Height / 2);
            Require(Math.Abs(cpu.X - gpu.X) + Math.Abs(cpu.Y - gpu.Y) < .01, "Grid and GPU model use different camera transforms");
        }
        var ground = view.Project(Vector3.Zero); view.Pan(30, 20);
        var movedGround = view.Project(Vector3.Zero);
        Require(Math.Abs(movedGround.X - ground.X - 30) + Math.Abs(movedGround.Y - ground.Y - 20) < .01, "Grid origin stayed attached to the camera");
        view.ResetView();
        var origin = view.TranslatePoint(default, window)!.Value;
        var p = origin + new Point(view.Bounds.Width * .45, view.Bounds.Height * .55);
        var initial = view.CameraState;
        window.MouseDown(p, MouseButton.Middle); window.MouseMove(p + new Point(35, 20), RawInputModifiers.MiddleMouseButton); window.MouseUp(p + new Point(35, 20), MouseButton.Middle);
        Require(view.CameraState.Yaw != initial.Yaw && view.CameraState.Center == initial.Center, "Middle drag must orbit");
        initial = view.CameraState;
        window.MouseDown(p, MouseButton.Middle, RawInputModifiers.Shift); window.MouseMove(p + new Point(40, 0), RawInputModifiers.MiddleMouseButton | RawInputModifiers.Shift); window.MouseUp(p + new Point(40, 0), MouseButton.Middle, RawInputModifiers.Shift);
        Require(view.CameraState.Yaw == initial.Yaw && view.CameraState.Center != initial.Center, "Shift-middle must pan");
        view.SetView(CameraView.Front); view.Fit();
        var particle = view.Data.Particles[0]; var anchor = view.Project(particle.Position);
        view.ZoomAt(2, anchor); var after = view.Project(particle.Position);
        Require(Math.Abs(anchor.X - after.X) < .001 && Math.Abs(anchor.Y - after.Y) < .001, "Cursor zoom moved anchor");
        foreach (var direction in Enum.GetValues<CameraView>())
        {
            view.SetView(direction); var matrix = view.CameraMatrix;
            Require(float.IsFinite(matrix.M11) && Math.Abs(view.ViewDirection.Length() - 1) < .001, "Invalid axis view");
        }
        view.SetView(CameraView.Front); view.Fit();
        view.SelectRectangle(new Rect(0, 0, view.Bounds.Width, view.Bounds.Height), SelectionOperation.Replace);
        Require(view.Selected.Count == 3, "Box selection did not select all particles");
        var first = view.Project(view.Data.Particles[0].Position);
        view.SelectRectangle(new Rect(first.X - 4, first.Y - 4, 8, 8), SelectionOperation.Toggle);
        Require(view.Selected.Count == 2 && !view.Selected.Contains(0), "Ctrl box selection toggle");
        view.SelectRectangle(new Rect(first.X - 4, first.Y - 4, 8, 8), SelectionOperation.Add);
        Require(view.Selected.Count == 3, "Shift box selection add");
        var corner = origin + first - new Point(8, 8);
        window.MouseDown(corner, MouseButton.Left);
        window.MouseMove(corner + new Point(16, 16), RawInputModifiers.LeftMouseButton);
        window.MouseUp(corner + new Point(16, 16), MouseButton.Left);
        Require(view.Selected.SetEquals(new[] { 0 }), "Pointer drag did not box-select a particle");
        window.MouseDown(p, MouseButton.Left);
        window.MouseMove(p + new Point(30, 30), RawInputModifiers.LeftMouseButton);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.MouseUp(p + new Point(30, 30), MouseButton.Left);
        Require(view.Selected.SetEquals(new[] { 0 }), "Escape committed a canceled box selection");
        view.Selected.Clear(); view.Selected.Add(0); view.FrameSelected();
        after = view.Project(particle.Position);
        Require(Math.Abs(after.X - view.Bounds.Width / 2) < .001 && Math.Abs(after.Y - view.Bounds.Height / 2) < .001, "Frame selected not centered");
        view.Fit(); view.CanSelect = false;
        view.SelectRectangle(new Rect(0, 0, 1, 1), SelectionOperation.Replace);
        Require(view.Selected.Contains(0), "Read-only selection was not blocked");
        view.CanSelect = true; view.ResetView(); Dispatcher.UIThread.RunJobs();
        Console.WriteLine("PASS: original camera gestures, anchored zoom, axis views, box selection, and frame selected.");
    }
}
