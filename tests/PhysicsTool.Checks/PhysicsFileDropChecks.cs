using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;

internal static class PhysicsFileDropChecks
{
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Finish(Task task) { Dispatcher.UIThread.RunJobs(); task.GetAwaiter().GetResult(); }
    private static void Click(Window host, string label)
    {
        host.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    public static void Run(string output)
    {
        var path = Path.GetFullPath(Path.Combine(output, "manual-0.hkcl"));
        var window = new MainWindow(graphics: false); window.Show();
        Require(DragDrop.GetAllowDrop(window), "Window does not accept file drops");
        foreach (var extension in new[] { ".HKCL", ".bphcl", ".bphyssb", ".bphhb", ".hkrg" })
            Require(MainWindow.IsPhysicsFilePath("test" + extension), "Physics extension rejected");
        Require(!MainWindow.IsPhysicsFilePath("test.bfres") && !MainWindow.IsPhysicsFilePath("test.hkcl.exe"), "Unrelated file accepted");
        var file = window.StorageProvider.TryGetFileFromPathAsync(path).GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Local file storage unavailable in the test host.");
        using var transfer = new DataTransfer(); transfer.Add(DataTransferItem.CreateFile(file));
        Require(MainWindow.GetPhysicsDropPath(transfer) == path, "Native file transfer path");
        transfer.Add(DataTransferItem.CreateFile(file));
        Require(MainWindow.GetPhysicsDropPath(transfer) == null, "Multi-file drop must not silently discard files");
        using var text = new DataTransfer(); text.Add(DataTransferItem.CreateText(path));
        Require(MainWindow.GetPhysicsDropPath(text) == null, "Plain text treated as a file drop");
        Finish(window.OpenDroppedPhysicsAsync(path));
        Require(window.Session.Path == path, "Startup drop did not load physics");
        window.ShowEditor(); window.Session.RenameSelected("Unsaved drop test"); window.Refresh();
        var before = window.Session.Document.CaptureState();
        var cancel = window.OpenDroppedPhysicsAsync(path); Dispatcher.UIThread.RunJobs();
        Require(window.OwnedWindows.Single().Title == "Unsaved changes", "Drop bypassed unsaved changes prompt");
        var repeated = window.OpenDroppedPhysicsAsync(path); Finish(repeated);
        Require(window.OwnedWindows.Count == 1, "Repeated drop opened multiple confirmation dialogs");
        Click(window.OwnedWindows.Single(), "Cancel"); Finish(cancel);
        Require(window.Session.Document.CaptureState() == before && window.Session.IsDirty, "Canceled drop lost edits");
        var confirm = window.OpenDroppedPhysicsAsync(path); Dispatcher.UIThread.RunJobs();
        Click(window.OwnedWindows.Single(), "Confirm"); Finish(confirm);
        Require(!window.Session.IsDirty && window.Session.Path == path, "Confirmed editor drop did not load file");
        before = window.Session.Document.CaptureState();
        var broken = Path.Combine(output, "drop-corrupt.hkcl"); File.WriteAllText(broken, "not a physics file");
        var rejected = false;
        try { Finish(window.OpenDroppedPhysicsAsync(broken)); }
        catch (Exception) { rejected = true; }
        Require(rejected, "Corrupt drop accepted");
        Require(window.Session.Document.CaptureState() == before, "Failed drop replaced current document");
        Finish(window.OpenDroppedPhysicsAsync(path));
        window.Close();
        Console.WriteLine("PASS: physics file drops, native file transfer, format/count filters, startup/editor loading, cancellation, reentrancy, and failure preservation.");
    }

}
