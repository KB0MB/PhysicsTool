using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;

internal static class OverviewChecks
{
    public static void Run(MainWindow window)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static Button Button(Window host, string name) => host.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, name));
        static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        var before = window.Session.Document.CaptureState();
        var currentList = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.ContextMenu != null);
        var rename = currentList.ContextMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Rename"));
        rename.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var prompt = window.OwnedWindows.Single();
        prompt.GetVisualDescendants().OfType<TextBox>().Single().Text = "UI_Renamed";
        Click(Button(prompt, "Rename"));
        Require(window.Session.Document.GetClothName(0) == "UI_Renamed", "Rename dialog was not applied");
        window.Session.Undo(); window.Refresh();
        Click(Button(window, "Remove selected")); Click(Button(window.OwnedWindows.Single(), "Cancel"));
        Require(window.Session.Document.CaptureState() == before, "Canceled remove changed document");
        Click(Button(window, "Merge selected (M)")); Click(Button(window.OwnedWindows.Single(), "Cancel"));
        Require(window.Session.Document.CaptureState() == before, "Canceled merge changed document");
        Click(Button(window, "Merge selected (M)")); Click(Button(window.OwnedWindows.Single(), "Confirm"));
        Require(window.Session.Document.GetClothSummaries().Count == 2, "Merge confirmation not wired");
        window.Session.Undo(); window.Refresh();
        window.Session.RenameSelected("DirtyCurrent"); window.SwapFiles();
        Click(Button(window, "Clear donor"));
        Require(window.OwnedWindows.Single().Title == "Unsaved changes", "Dirty donor cleared without warning");
        Click(Button(window.OwnedWindows.Single(), "Cancel"));
        Require(window.DonorSession.IsDirty, "Cancel discarded donor edits");
        window.SwapFiles(); window.Session.Undo(); window.Refresh();
        Require(window.Session.Document.CaptureState() == before && window.OwnedWindows.Count == 0, "Overview tests left a changed document or open dialog");
        Console.WriteLine("PASS: overview dialogs, canceled removal/merge, confirmed merge, and dirty-donor warning.");
    }
}
