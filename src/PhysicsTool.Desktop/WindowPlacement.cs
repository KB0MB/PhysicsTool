using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using HKCLTool;

namespace PhysicsTool.Desktop;

internal sealed record SavedWindowPlacement(int X, int Y, double Width, double Height);
internal sealed record PlacementScreen(PixelRect WorkingArea, double Scaling, bool Primary);

internal static class WindowPlacement
{
    internal static SavedWindowPlacement Fit(SavedWindowPlacement saved, IReadOnlyList<PlacementScreen> screens)
    {
        if (screens.Count == 0) return new(0, 0, 1280, 800);
        if (!double.IsFinite(saved.Width) || !double.IsFinite(saved.Height) || saved.Width < 100 || saved.Height < 100)
            saved = saved with { Width = 1440, Height = 900 };
        // Require a usable part of the title bar to be on-screen; otherwise use the primary monitor.
        var screen = screens.Where(s => saved.Y >= s.WorkingArea.Y && (long)saved.Y + 32 <= s.WorkingArea.Bottom &&
                (long)saved.X + 100 <= s.WorkingArea.Right && (long)saved.X + saved.Width * s.Scaling >= s.WorkingArea.X + 100)
            .OrderByDescending(s => Intersection(saved, s)).FirstOrDefault();
        bool relocate = screen == null;
        screen ??= screens.FirstOrDefault(s => s.Primary) ?? screens[0];
        var area = screen.WorkingArea;
        double scale = double.IsFinite(screen.Scaling) && screen.Scaling > 0 ? screen.Scaling : 1;
        double width = Math.Min(Math.Max(saved.Width, 980), area.Width / scale);
        // Leave room for the native window frame/title bar, which is outside Avalonia's client size.
        double height = Math.Min(Math.Max(saved.Height, 640), Math.Max(100, area.Height / scale - 40));
        int pw = (int)Math.Ceiling(width * scale), ph = (int)Math.Ceiling((height + 40) * scale);
        int x = relocate ? area.X + Math.Max(0, (area.Width - pw) / 2) : saved.X;
        int y = relocate ? area.Y + Math.Max(0, (area.Height - ph) / 2) : saved.Y;
        return new(Math.Clamp(x, area.X, Math.Max(area.X, area.Right - pw)), Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - ph)), width, height);
    }

    private static double Intersection(SavedWindowPlacement saved, PlacementScreen screen)
    {
        var a = screen.WorkingArea;
        return Math.Max(0, Math.Min((double)a.Right, saved.X + saved.Width * screen.Scaling) - Math.Max(a.X, saved.X)) *
            Math.Max(0, Math.Min((double)a.Bottom, saved.Y + saved.Height * screen.Scaling) - Math.Max(a.Y, saved.Y));
    }

    internal static void Attach(Window window, string? path = null, bool? maximize = null)
    {
        path ??= AppDataPaths.SettingsFile("window-placement.json");
        SavedWindowPlacement? last = null;
        try { if (File.Exists(path)) last = JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        PlacementScreen[] Screens() => window.Screens.All.Where(s => s.WorkingArea.Width > 0 && s.WorkingArea.Height > 0)
            .Select(s => new PlacementScreen(s.WorkingArea, s.Scaling, s.IsPrimary)).ToArray();
        if (Screens().Length > 0)
        {
            var primary = Screens().FirstOrDefault(s => s.Primary) ?? Screens()[0];
            last ??= new(primary.WorkingArea.X + 60, primary.WorkingArea.Y + 40, window.Width, window.Height);
            last = Fit(last, Screens());
            window.MinWidth = Math.Min(window.MinWidth, last.Width); window.MinHeight = Math.Min(window.MinHeight, last.Height);
            window.Width = last.Width; window.Height = last.Height;
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Position = new(last.X, last.Y);
        }
        bool opened = false;
        void Capture()
        {
            if (!opened || window.WindowState != WindowState.Normal || window.Bounds.Width < 100 || window.Bounds.Height < 100) return;
            last = new(window.Position.X, window.Position.Y, window.Bounds.Width, window.Bounds.Height);
        }
        window.Opened += (_, _) =>
        {
            opened = true; Capture();
            if (maximize ?? DesktopPreferences.Load().MaximizeOnStartup) window.WindowState = WindowState.Maximized;
        };
        window.PositionChanged += (_, _) => Dispatcher.UIThread.Post(Capture, DispatcherPriority.Background);
        window.SizeChanged += (_, _) => Dispatcher.UIThread.Post(Capture, DispatcherPriority.Background);
        PlacementScreen? closingScreen = null;
        bool closingMaximized = false;
        window.Closing += (_, _) =>
        {
            Capture(); closingMaximized = window.WindowState == WindowState.Maximized;
            var current = window.Screens.ScreenFromWindow(window);
            closingScreen = current == null ? null : new(current.WorkingArea, current.Scaling, true);
        };
        window.Closed += (_, _) =>
        {
            opened = false;
            if (last == null) return;
            // A maximized window can be moved to another screen without changing its restore size.
            if (closingMaximized && closingScreen != null) last = Fit(last, [closingScreen]);
            try { AtomicFile.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(last)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
    }
}
