using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;

internal static class DeletionFocusChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        if (window.IsVisible) window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Delete(Window window, bool confirm = true)
    {
        Press(window, Key.Delete);
        Check(window.OwnedWindows.Count == 1, "Delete did not open confirmation without refocusing the list");
        Press(window.OwnedWindows.Single(), confirm ? Key.Enter : Key.Escape);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Check(window.OwnedWindows.Count == 0, "Keyboard confirmation did not close");
    }
    internal static void Run(string output, string? nativeBphcl = null)
    {
        Directory.CreateDirectory(output);
        if (nativeBphcl != null)
        {
            var window = new MainWindow(graphics: false); window.Show(); window.LoadPath(nativeBphcl);
            window.ShowOverview(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.ContextMenu != null);
            list.SelectedIndex = 1; window.UpdateLayout(); list.ContainerFromIndex(1)!.Focus();
            var count = list.ItemCount;
            for (var i = 0; i < 3; i++)
            {
                Delete(window);
                Check(list.ItemCount == --count && list.SelectedIndex == 1 && window.Session.ClothIndex == 1,
                    "Native BPHCL deletion did not retain the next cloth selection");
                Check(list.ContainerFromIndex(1)?.IsKeyboardFocusWithin == true,
                    "Native BPHCL next row did not retain keyboard focus");
            }
            window.Session.Clear(); window.Close();
        }
        foreach (var format in new[] { 0, 2 })
        {
            var window = new MainWindow(graphics: false); window.Show();
            window.Session.New("First", format); window.Session.DuplicateSelected(); window.Session.DuplicateSelected();
            window.Session.ClothIndex = 1; window.Refresh(); window.ShowOverview(); Dispatcher.UIThread.RunJobs();
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.ContextMenu != null);
            list.ContainerFromIndex(1)!.Focus();
            Delete(window);
            Check(list.ContainerFromIndex(list.SelectedIndex)?.IsKeyboardFocusWithin == true,
                $"Selected cloth row did not receive keyboard focus after deletion (format {format}, list focusable: {list.Focusable}, focused: {window.FocusManager?.GetFocusedElement()?.GetType().Name})");
            Check(list.SelectedIndex == 1 && window.Session.ClothIndex == 1 && list.ItemCount == 2, "Middle deletion did not select next cloth");
            Delete(window);
            Check(list.SelectedIndex == 0 && window.Session.ClothIndex == 0 && list.ItemCount == 1, "Last deletion did not select preceding cloth");
            Delete(window, false);
            Check(list.ItemCount == 1, "Cancel removed cloth");
            if (format == 0)
            {
                Delete(window);
                Check(list.ItemCount == 0 && list.SelectedIndex == -1, "Final HKCL deletion left stale selection");
                Press(window, Key.Delete); Check(window.OwnedWindows.Count == 0, "Empty list opened delete dialog");
            }
            window.Session.Clear(); window.Close();
        }
        var parameters = new BphysicsDocument { HkclPath = "Test/Test.hkcl" };
        foreach (var name in new[] { "First", "Middle", "Last" }) parameters.Cloths.Add(new(name, "Head"));
        var path = Path.Combine(output, "delete-focus.bphysics"); BphysicsService.Save(path, parameters);
        var owner = new MainWindow(graphics: false); owner.Show(); owner.LoadPath(path); Dispatcher.UIThread.RunJobs();
        var slots = owner.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "BphysicsSlots");
        slots.SelectedIndex = 1; slots.ContainerFromIndex(1)!.Focus();
        Delete(owner); Check(slots.SelectedIndex == 1 && slots.ItemCount == 2, "BPHYSICS next selection incorrect");
        Delete(owner); Check(slots.SelectedIndex == 0 && slots.ItemCount == 1, "BPHYSICS preceding selection incorrect");
        Delete(owner, false); Check(slots.ItemCount == 1, "BPHYSICS cancel deleted slot");
        owner.BphysicsWorkspace!.SaveAsync(false).GetAwaiter().GetResult(); owner.Close(); Dispatcher.UIThread.RunJobs();
        Console.WriteLine("PASS repeated Delete/Enter and cancellation: HKCL, BPHCL and BPHYSICS; middle/last/final entries and empty-list guard");
    }
}
