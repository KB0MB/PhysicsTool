using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private Button _actorExport = null!;
    private bool _actorExportBusy;

    private void UpdateActorExport()
    {
        _actorExport.IsVisible = IsActorMode;
        _actorExport.IsEnabled = IsActorMode && !_modelLoading && !_actorExportBusy;
    }

    internal ContextMenu CreateActorExportMenu()
    {
        var entries = _actorScene == null ? [] : ActorResourceExport.List(_actorScene.ExportResources);
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<ActorExportKind>())
        {
            if (kind == ActorExportKind.Bphysics && _actorScene?.Resources.IsTotk != false) continue;
            var label = kind switch { ActorExportKind.Bfres => "BFRES", ActorExportKind.Cloth => "Cloth", ActorExportKind.Bphysics => "BPHYSICS", _ => _actorScene?.Resources.IsTotk == true ? "Helper bones" : "Support bones" };
            var available = entries.Where(e => e.Kind == kind).ToArray();
            var item = new MenuItem { Header = label, IsEnabled = available.Length > 0 };
            if (available.Length == 1) item.Click += async (_, _) => await RunCommand(() => ExportActorResource(available[0]));
            else item.ItemsSource = available.Select(entry => MenuCommand(entry.Label, () => ExportActorResource(entry))).ToArray();
            menu.Items.Add(item);
        }
        return menu;
    }

    private void ShowActorExport()
    {
        if (_actorScene == null || _actorExportBusy) return;
        _actorExport.ContextMenu = CreateActorExportMenu(); _actorExport.ContextMenu.Open(_actorExport);
    }

    private async Task ExportActorResource(ActorExportEntry entry)
    {
        if (_actorExportBusy) return;
        _actorExportBusy = true; UpdateActorExport();
        try
        {
            var extension = Path.GetExtension(entry.FileName);
            var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Export " + entry.FileName, SuggestedFileName = entry.FileName,
                DefaultExtension = extension.TrimStart('.'), ShowOverwritePrompt = true,
                FileTypeChoices = [new(entry.Kind == ActorExportKind.Bfres ? "BFRES model" : "Physics") { Patterns = ["*" + extension] }] });
            if (file?.TryGetLocalPath() is not { } path) return;
            await Task.Run(() => ActorResourceExport.Save(entry, path));
            _status.Text = "Exported " + Path.GetFileName(path) + (entry.Kind == ActorExportKind.Bfres ? ". Original model resource; external textures remain separate." : ". Original physics resource, without conversion.");
        }
        finally { _actorExportBusy = false; UpdateActorExport(); }
    }
}
