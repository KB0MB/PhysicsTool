using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

internal sealed partial class IconToolView
{
    private async Task<string?> ExportDirectory(bool toMod, IconImageFormat format)
    {
        bool totk = Asset!.CaptureGame == "TotK";
        string folder = toMod ? _preferences.Mod(totk) : "";
        if (!toMod || !Directory.Exists(folder))
        {
            var picked = await _owner.StorageProvider.OpenFolderPickerAsync(new() { Title = toMod ? (totk ? "TOTK mod folder" : "BOTW mod folder") : "Export icons to folder", AllowMultiple = false });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is not { } selected) return null;
            folder = selected;
        }
        if (!toMod) return folder;
        var destination = IconToolPreferences.ModExportDirectory(folder, totk, Asset.IsSwitch, format, new[] { _dumpPaths.Botw, _dumpPaths.Totk }.Concat(Asset.ResourceRoots.Where(r => !r.StartsWith(folder, StringComparison.OrdinalIgnoreCase))));
        _preferences.SetMod(totk, folder); _preferences.Save(_preferencesPath);
        return destination;
    }

    private async Task<bool> ConfirmIconReplace(int count, string directory)
    {
        var dialog = new Window { Title = "Replace icons?", Width = 480, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = $"Replace {count} existing icon file(s) in {directory}?", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var buttons = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 8 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        var replace = new Button { Content = "Replace", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => dialog.Close(false); replace.Click += (_, _) => dialog.Close(true);
        Grid.SetColumn(replace, 1); buttons.Children.Add(cancel); buttons.Children.Add(replace); panel.Children.Add(buttons); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(_owner);
    }
}
