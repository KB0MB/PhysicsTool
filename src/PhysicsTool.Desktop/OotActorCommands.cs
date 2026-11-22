using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    internal async Task OpenOotActor(BotwActorCatalogEntry entry)
    {
        _status.Text = "Reading OoT actor physics...";
        var resources = await Task.Run(() => OotActorResources.Load(entry));
        var window = new Window { Title = entry.Name + " - Physics", Width = 760, Height = 420, MinWidth = 520, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        var status = new TextBlock { Text = resources.Warnings.Count == 0 ? "" : string.Join("\n", resources.Warnings), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(status, Dock.Bottom); panel.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var open = new Button { Name = "OpenOotPhysics", Content = "View actor", IsDefault = true, IsEnabled = false };
        var export = new Button { Content = "Export", IsEnabled = false };
        var close = new Button { Content = "Close", IsCancel = true };
        buttons.Children.Add(export); buttons.Children.Add(open); buttons.Children.Add(close);
        var loadModel = new CheckBox { Content = "Load model", IsChecked = resources.ModelPath != null, IsEnabled = resources.ModelPath != null, Margin = new Thickness(0, 0, 12, 0) };
        buttons.Children.Insert(0, loadModel);
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var assets = new ListBox { ItemsSource = resources.Assets };
        panel.Children.Add(assets); window.Content = panel;
        assets.SelectionChanged += (_, _) => open.IsEnabled = export.IsEnabled = assets.SelectedItem != null;
        close.Click += (_, _) => window.Close();
        open.Click += (_, _) => { if (assets.SelectedItem is OotPhysicsAsset selected) window.Close(selected); };
        assets.DoubleTapped += (_, _) => { if (assets.SelectedItem is OotPhysicsAsset selected) window.Close(selected); };
        export.Click += async (_, _) =>
        {
            if (assets.SelectedItem is not OotPhysicsAsset asset) return;
            try
            {
                var path = await window.StorageProvider.SaveFilePickerAsync(new() { Title = "Export physics", SuggestedFileName = System.IO.Path.GetFileName(asset.Path), DefaultExtension = asset.IsCloth ? "bphcl" : "bgyml" });
                if (path?.TryGetLocalPath() is { } file) { AtomicFile.WriteAllBytes(file, asset.Bytes); status.Text = "Exported " + System.IO.Path.GetFileName(file); }
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        if (resources.Assets.Count > 0) assets.SelectedIndex = 0;
        var selected = await window.ShowDialog<OotPhysicsAsset?>(this);
        if (selected == null) return;
        PauseEditorAnimation(); StopSimulation(); ExitActor(); ClearEditorAnimation();
        _savedOotSession = Session; _savedOotRig = _importedModel;
        var actorSession = Session = new EditorSession { IsReadOnly = true };
        _modelRequest++; _importedModel = null;
        if (selected.IsCloth) Session.ImportOotCloth(selected.Bytes);
        _ootScene = new OotActorPreview(resources.Assets);
        _viewport.SetModel(null); Refresh(true); ShowEditor();
        EnterOotViewer(entry.Name);
        var notices = new List<string>(resources.Warnings);
        notices.AddRange(_ootScene.Warnings);
        if (loadModel.IsChecked == true && resources.ModelPath is { } modelPath)
        {
            try
            {
                await LoadModelAsync(() => PortableModelImporter.LoadRig(modelPath), modelPath, exitActor: false);
                if (!ReferenceEquals(Session, actorSession)) return;
                var helpers = new List<OotHelperEvaluator>();
                foreach (var asset in resources.Assets.Where(a => !a.IsCloth))
                    try { helpers.Add(new OotHelperEvaluator(asset.Bytes, _importedModel!.Rest.Bones)); }
                    catch (InvalidDataException ex) { notices.Add("Helper preview: " + ex.Message); }
                _ootHelpers = helpers.ToArray();
                notices.AddRange(_ootHelpers.SelectMany(h => h.Warnings));
                if (resources.AnimationPaths.Count > 0)
                    await LoadAnimationsAsync(() => resources.AnimationPaths.SelectMany(path =>
                    {
                        try { return ModelAnimationClip.Load(path, allowEmptyBfres: true); }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or NotSupportedException)
                        { notices.Add(System.IO.Path.GetFileName(path) + ": " + ex.Message); return Array.Empty<ModelAnimationClip>(); }
                    }).ToArray(), entry.Name);
            }
            catch (Exception ex) { notices.Add("Model/animation: " + UserFacingExceptionFormatter.Format(ex)); }
        }
        if (!ReferenceEquals(Session, actorSession)) return;
        UpdateImportedModelPose(); UpdateActions();
        _status.Text = entry.Name + $": {_ootScene!.ClothCount} cloths, read-only actor preview"
            + (_importedModel == null ? "." : $", {_importedModel.Rest.Meshes.Count} meshes, {_editorClips.Count} animations.")
            + (notices.Count == 0 ? "" : " " + string.Join(" ", notices));
    }
}
