using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HKCLTool;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

internal sealed class ActorPhysicsWindow : Window
{
    internal ActorPhysicsWindow(string actorName, IReadOnlyList<ActorExportEntry> entries)
    {
        Title = actorName + " - Physics";
        Width = 760; Height = 420; MinWidth = 520; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var export = new Button { Name = "ExportActorPhysics", Content = "Export", IsEnabled = false };
        var view = new Button { Name = "ViewActorPhysics", Content = "View actor", IsDefault = true };
        var close = new Button { Content = "Close", IsCancel = true };
        buttons.Children.Add(export); buttons.Children.Add(view); buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var list = new ListBox { Name = "ActorPhysicsFiles", ItemsSource = entries,
            ItemTemplate = new FuncDataTemplate<ActorExportEntry>((asset, _) => new TextBlock { Text = asset?.FileName }) };
        panel.Children.Add(list); Content = panel;
        list.SelectionChanged += (_, _) => export.IsEnabled = list.SelectedItem is ActorExportEntry;
        view.Click += (_, _) => Close(true);
        list.DoubleTapped += (_, _) => { if (list.SelectedItem != null) Close(true); };
        close.Click += (_, _) => Close(false);
        export.Click += async (_, _) =>
        {
            if (list.SelectedItem is not ActorExportEntry asset) return;
            export.IsEnabled = view.IsEnabled = list.IsEnabled = false;
            try
            {
                var extension = Path.GetExtension(asset.FileName);
                var selected = await StorageProvider.SaveFilePickerAsync(new() { Title = "Export physics", SuggestedFileName = asset.FileName,
                    DefaultExtension = extension.TrimStart('.'), ShowOverwritePrompt = true,
                    FileTypeChoices = [new("Physics") { Patterns = ["*" + extension] }] });
                if (selected?.TryGetLocalPath() is { } path)
                {
                    await Task.Run(() => ActorResourceExport.Save(asset, path));
                    status.Text = "Exported " + Path.GetFileName(path);
                }
            }
            catch (Exception ex) { status.Text = UserFacingExceptionFormatter.Format(ex); }
            finally { view.IsEnabled = list.IsEnabled = true; export.IsEnabled = list.SelectedItem is ActorExportEntry; }
        };
        if (entries.Count > 0) list.SelectedIndex = 0;
        else status.Text = "No cloth or helper/support bone files are linked to this actor.";
    }
}
