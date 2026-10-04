using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HKCLTool;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private static readonly FilePickerFileType[] PhysicsFileTypes =
    [new("Physics") { Patterns = ["*.hkcl", "*.bphcl", "*.bphyssb", "*.bphhb", "*.hkrg"] }];

    private async Task RunCommand(Func<Task> command)
    {
        try { await command(); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private MenuItem MenuCommand(string label, Func<Task> action)
    {
        var item = new MenuItem { Header = label };
        item.Click += async (_, _) => await RunCommand(action); return item;
    }
    public void SwapFiles()
    {
        if (IsEditorVisible || !Session.Document.HasDocument || !DonorSession.Document.HasDocument) return;
        Session.SwapWith(DonorSession); Refresh(true); _status.Text = "Swapped current and donor files.";
    }
    private async Task ClearDonor()
    {
        if (!await CanReplace(DonorSession)) return;
        DonorSession.Clear(); Refresh(); _status.Text = "Donor cleared.";
    }
    private async Task RemoveCloth()
    {
        if (!Session.CanRemoveEntry || _timer.IsEnabled) return;
        var label = Session.Document.GetClothSummaries()[Session.ClothIndex];
        if (!await Dialogs.Confirm(this, Session.Document.IsHelperBoneSidecar ? "Delete helper group" : "Remove cloth", $"Remove {label}?")) return;
        Session.RemoveSelected(); Refresh(true); _status.Text = "Removed selected entry.";
    }
    private async Task RenameCloth()
    {
        if (!Session.CanManageCloth || _timer.IsEnabled) return;
        var name = await Dialogs.Prompt(this, "Rename cloth", "Name", Session.Document.GetClothName(Session.ClothIndex));
        if (name == null) return;
        Session.RenameSelected(name); Refresh(); _status.Text = "Renamed cloth to " + name;
    }
    private async Task MergeDonor()
    {
        if (_timer.IsEnabled) return;
        var report = Session.PreviewMerge(DonorSession);
        if (!report.Allowed)
        {
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
            panel.Children.Add(new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = report.Description, TextWrapping = TextWrapping.Wrap } });
            await Dialogs.Show(this, "Merge blocked", panel); return;
        }
        if (!await Dialogs.Confirm(this, "Merge selected donor", report.Description + "\n\nContinue?")) return;
        var result = Session.MergeFrom(DonorSession); Refresh(true); _status.Text = result;
    }
    private void OpenExportMenu()
    {
        if (!_export.IsEnabled) return;
        var json = MenuCommand("JSON", () => ExportDocument(null, true));
        var wiiU = MenuCommand("Wii U HKCL", () => ExportDocument(HkclPlatform.WiiU));
        var nx = MenuCommand("Switch HKCL", () => ExportDocument(HkclPlatform.Switch));
        wiiU.IsEnabled = nx.IsEnabled = !Session.Document.IsReadOnlyExternal;
        _export.ContextMenu = new ContextMenu { ItemsSource = new object[] { MenuCommand("Save as...", () => SaveDocument(true)), new Separator(), json, wiiU, nx } };
        _export.ContextMenu.Open(_export);
    }
    private async Task ExportDocument(HkclPlatform? platform, bool json = false)
    {
        var extension = json ? ".json" : ".hkcl";
        var name = System.IO.Path.GetFileNameWithoutExtension(Session.Path) ?? "NewCloth";
        var suffix = json ? "" : platform == HkclPlatform.WiiU ? "_WiiU" : "_Switch";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export physics", SuggestedFileName = name + suffix + extension,
            DefaultExtension = extension.TrimStart('.'), ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType(json ? "JSON" : "HKCL") { Patterns = ["*" + extension] }]
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        Session.ExportCopy(path, platform); _status.Text = "Exported " + System.IO.Path.GetFileName(path);
    }
}
