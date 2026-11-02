using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;

internal static class WorkspaceNavigationChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        PlacementScreen primary = new(new(0, 0, 1920, 1040), 1, true);
        PlacementScreen secondary = new(new(1920, 0, 2560, 1400), 1.5, false);
        PlacementScreen left = new(new(-1920, 0, 1920, 1040), 1, false);
        var saved = new SavedWindowPlacement(2200, 100, 1200, 750);
        Require(WindowPlacement.Fit(saved, [primary, secondary]) == saved, "Available secondary placement lost");
        var restored = WindowPlacement.Fit(saved, [primary]);
        Require(restored.X >= 0 && restored.Y >= 0 && restored.X + restored.Width <= 1920 && restored.Y + restored.Height + 40 <= 1040, "Disconnected secondary leaves off-screen window");
        var negative = new SavedWindowPlacement(-1800, 80, 1100, 700);
        Require(WindowPlacement.Fit(negative, [primary, left]) == negative, "Negative monitor coordinates lost");
        Require(WindowPlacement.Fit(negative, [primary]).X >= 0, "Disconnected left monitor not handled");
        foreach (var invalid in new[] { new SavedWindowPlacement(int.MaxValue, int.MaxValue, double.NaN, 0), new(-99999, -99999, 100000, 100000), new(0, 1010, 1000, 700) })
        {
            var fitted = WindowPlacement.Fit(invalid, [primary]);
            Require(fitted.X >= 0 && fitted.Y >= 0 && fitted.Width <= 1920 && fitted.Height <= 1000, "Invalid placement escaped working area");
        }
        var small = WindowPlacement.Fit(saved, [new(new(0, 0, 1280, 680), 1.5, true)]);
        Require(small.Width <= 1280 / 1.5 && small.Height + 40 <= 680 / 1.5, "High-DPI small screen not accommodated");
        string prefs = Path.Combine(output, "preferences.json");
        (new DesktopPreferences(MaximizeOnStartup: true)).Save(prefs);
        Require(DesktopPreferences.Load(prefs).MaximizeOnStartup, "Maximize preference not saved");
        (DesktopPreferences.Load(prefs) with { MaximizeOnStartup = false }).Save(prefs);
        Require(!DesktopPreferences.Load(prefs).MaximizeOnStartup, "Maximize preference not cleared");
        var window = new MainWindow(graphics: false); window.Show(); Dispatcher.UIThread.RunJobs();
        var original = window.Content;
        window.ShowCollision(false); Dispatcher.UIThread.RunJobs();
        Require(window.IsToolWorkspaceVisible && window.GetVisualDescendants().OfType<CollisionView>().Count() == 1, "Collision not embedded");
        var view = window.CollisionWorkspace;
        Require(view!.Viewport.GetVisualAncestors().OfType<Control>().Any(c => !c.IsVisible), "Empty collision viewport is visible");
        string mesh = Path.Combine(output, "collision.obj");
        File.WriteAllText(mesh, "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 1\nf 1 3 2\nf 1 2 4\nf 2 3 4\nf 3 1 4\n");
        var load = view.LoadPathAsync(mesh, true);
        while (!load.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        load.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        Require(view.Viewport.Model != null && view.Viewport.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible), "Loaded collision viewport stays hidden");
        view!.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "CollisionOptions").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "CollisionScale").Value = 2;
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WorkspaceBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ReferenceEquals(window.Content, original), "Back lost original workspace");
        window.ShowCollision(false); Require(ReferenceEquals(view, window.CollisionWorkspace), "Collision state not retained");
        Dispatcher.UIThread.RunJobs();
        Require(view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "CollisionScale").Value == 2, "Back lost collision settings");
        window.ShowStaticCompoundAsync(null).GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        Require(window.GetVisualDescendants().OfType<BphscView>().Count() == 1, "BPHSC still opens separately");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WorkspaceBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(window.GetVisualDescendants().OfType<CollisionView>().Single() == view, "BPHSC Back lost collision view");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "collision-embedded.png"));
        window.Close();
        string placement = Path.Combine(output, "placement.json");
        var host = new Window { Width = 1050, Height = 700 };
        WindowPlacement.Attach(host, placement, false); host.Show(); Dispatcher.UIThread.RunJobs(); host.Position = new(100, 80);
        Dispatcher.UIThread.RunJobs(); host.Close();
        Require(JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllBytes(placement)) is { X: 100, Y: 80 }, "Normal placement did not persist");
        var normal = JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllBytes(placement))!;
        var maximized = new Window { Width = 1200, Height = 850 };
        WindowPlacement.Attach(maximized, placement, true); maximized.Show(); Dispatcher.UIThread.RunJobs();
        Require(maximized.WindowState == WindowState.Maximized, "Maximize on startup not applied");
        maximized.Close();
        var afterMax = JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllBytes(placement))!;
        Require(afterMax.Width == normal.Width && afterMax.Height == normal.Height, "Maximized bounds overwrote normal restore size");
        var minimized = new Window(); WindowPlacement.Attach(minimized, placement, false); minimized.Show(); Dispatcher.UIThread.RunJobs();
        minimized.WindowState = WindowState.Minimized; minimized.Close();
        Require(JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllBytes(placement)) == afterMax, "Minimized bounds overwrote placement");
        Console.WriteLine("PASS: embedded collision/BPHSC navigation, retained workspace, placement persistence, disconnected monitors, negative screen coordinates, DPI, corrupt bounds and maximize preference.");
    }
}
