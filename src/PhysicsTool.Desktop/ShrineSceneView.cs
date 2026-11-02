using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace PhysicsTool.Desktop;

internal sealed partial class ShrineSceneView : UserControl
{
    private readonly Window _owner;
    private readonly PhysicsViewport _view = new() { ShowBones = false, ShowColliders = false, ShowParticles = false, CanTransform = false, FlyNavigation = true, ContextMenu = null };
    private readonly TextBox _search = new() { Name = "ShrineActorSearch", PlaceholderText = "Search actor, name, hash or group" };
    private readonly CheckBox _groundOnly = new() { Content = "GroundControl only" };
    private readonly ListBox _actors = new() { Name = "ShrineActors", SelectionMode = SelectionMode.Multiple };
    private readonly StackPanel _details = new() { Spacing = 10, Margin = new Thickness(12) };
    private readonly TextBlock _status = new() { Text = "No BANC scene loaded", TextWrapping = TextWrapping.Wrap };
    private readonly Button _open, _compound;
    private readonly Grid _pages = new();
    private readonly Control _browser, _editor;
    private readonly TextBox _root = new() { Name = "ShrineRoot", PlaceholderText = "TOTK romfs folder" };
    private readonly TextBox _sceneSearch = new() { Name = "ShrineSceneSearch", PlaceholderText = "Search BANC scenes" };
    private readonly ListBox _scenes = new() { Name = "ShrineScenes" };
    private readonly TextBlock _catalogStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _scan, _browse, _load, _return;
    private readonly Button _cancel;
    private CancellationTokenSource? _cancellation;
    private IReadOnlyList<ShrineSceneEntry> _entries = [];
    private ShrineSceneAssets? _assets;
    private ShrineModelPicking? _picking;
    internal ShrineSceneAssets? Assets => _assets;
    internal ModelGlControl? ModelSurface { get; }
    private ShrineSceneDocument? _document;
    private ShrinePlacementEditor? _editing;
    internal ShrinePlacementEditor? Editing => _editing;
    private int[] _moving = [];
    private readonly Button _save, _undo, _redo, _duplicate, _delete;
    private bool _selecting;
    internal bool Busy { get; private set; }
    internal PhysicsViewport Viewport => _view;
    internal Func<string?, Task>? OpenCompound { get; set; }

    internal ShrineSceneView(Window owner, bool graphics = true)
    {
        _owner = owner;
        var shell = new DockPanel { Margin = new Thickness(12) };
        var bar = new WrapPanel();
        _open = Command("Scenes", Open); bar.Children.Add(_open);
        _compound = Command("Open source BPHSC", async () => { if (FindCompound() is { } path && OpenCompound != null) await OpenCompound(path); }); bar.Children.Add(_compound);
        ToolTip.SetTip(_compound, "Inspect the original compound. Save shrine exports your placement edits with their rebuilt collision.");
        _save = Command("Save shrine", Save); bar.Children.Add(_save);
        _undo = Command("Undo", () => { _view.FinishTransform(false); _editing?.Undo(); RefreshEdit(); return Task.CompletedTask; }); bar.Children.Add(_undo);
        _redo = Command("Redo", () => { _view.FinishTransform(false); _editing?.Redo(); RefreshEdit(); return Task.CompletedTask; }); bar.Children.Add(_redo);
        _duplicate = Command("Duplicate", DuplicateActors); bar.Children.Add(_duplicate);
        _delete = Command("Delete", DeleteActors); bar.Children.Add(_delete);
        var origins = new CheckBox { Name = "SceneOrigins", Content = "Actor origins", Margin = new Thickness(8, 0) };
        origins.IsCheckedChanged += (_, _) => { _view.ShowParticles = origins.IsChecked == true; _view.InvalidateVisual(); }; bar.Children.Add(origins);
        bar.Children.Add(Command("Hide selected", () => { if (_picking != null) foreach (var actor in SelectedActors()) foreach (int mesh in _picking.Meshes(actor.Index)) _view.SetMeshVisible(mesh, false); return Task.CompletedTask; }));
        bar.Children.Add(Command("Hide filtered", () =>
        {
            if (_picking != null && _actors.ItemsSource is IEnumerable<ShrineActor> actors)
                foreach (var actor in actors) foreach (int mesh in _picking.Meshes(actor.Index)) _view.HiddenMeshes.Add(mesh);
            if (_view.Model != null) _view.SetModelPose(_view.Model); return Task.CompletedTask;
        }));
        bar.Children.Add(Command("Show all", () => { _view.HiddenMeshes.Clear(); if (_view.Model != null) _view.SetModelPose(_view.Model); return Task.CompletedTask; }));
        bar.Children.Add(Command("Asset notices", () => { _details.Children.Clear(); foreach (var warning in _assets?.Warnings ?? []) Label(warning); return Task.CompletedTask; }));
        var cameraSpeed = new StackPanel { Orientation = Orientation.Horizontal };
        cameraSpeed.Children.Add(new TextBlock { Text = "Camera speed", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) });
        var speed = new Slider { Name = "SceneCameraSpeed", Minimum = 1, Maximum = 100, Value = 12, Width = 130, Margin = new Thickness(6, 0) };
        var speedText = new TextBlock { Text = "12", Width = 36, VerticalAlignment = VerticalAlignment.Center };
        speed.ValueChanged += (_, _) => { _view.CameraSpeed = speed.Value; speedText.Text = speed.Value.ToString("0"); };
        ToolTip.SetTip(speed, "Hold right mouse to look. W/A/S/D move; Q/E move vertically; Shift moves faster.");
        cameraSpeed.Children.Add(speed); cameraSpeed.Children.Add(speedText); bar.Children.Add(cameraSpeed);
        DockPanel.SetDock(bar, Dock.Top); shell.Children.Add(bar);
        _status.Margin = new Thickness(0, 10, 0, 0); DockPanel.SetDock(_status, Dock.Bottom); shell.Children.Add(_status);
        var grid = new Grid { ColumnDefinitions = new("300,5,*,5,300"), Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions[0].MinWidth = 180; grid.ColumnDefinitions[2].MinWidth = 280; grid.ColumnDefinitions[4].MinWidth = 180;
        var left = new DockPanel(); var filters = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        filters.Children.Add(_search); filters.Children.Add(_groundOnly); DockPanel.SetDock(filters, Dock.Top); left.Children.Add(filters); left.Children.Add(_actors); grid.Children.Add(left);
        _actors.ItemTemplate = new FuncDataTemplate<ShrineActor>((a, _) => new TextBlock { Text = a?.ToString(), TextTrimming = TextTrimming.CharacterEllipsis });
        foreach (int column in new[] { 1, 3 }) { var split = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(split, column); grid.Children.Add(split); }
        var scene = new Grid();
        if (graphics) { ModelSurface = new ModelGlControl(_view); ModelSurface.RenderFailed += message => _status.Text = "Model renderer: " + message; scene.Children.Add(ModelSurface); }
        scene.Children.Add(_view); scene.Children.Add(ViewportTools.CameraToolbar(_view)); scene.Children.Add(new ViewportAxes(_view)); Grid.SetColumn(scene, 2); grid.Children.Add(scene);
        var detail = new ScrollViewer { Content = _details }; Grid.SetColumn(detail, 4); grid.Children.Add(detail); shell.Children.Add(grid);
        _editor = shell; _editor.IsVisible = false;
        var browser = new DockPanel { Margin = new Thickness(20), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };
        var heading = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = "BANC scenes", FontSize = 24 });
        var rootRow = new Grid { ColumnDefinitions = new("*,Auto,Auto") }; rootRow.Children.Add(_root);
        _browse = Command("Browse", BrowseRoot); Grid.SetColumn(_browse, 1); rootRow.Children.Add(_browse);
        _scan = Command("Scan", Scan); Grid.SetColumn(_scan, 2); rootRow.Children.Add(_scan);
        heading.Children.Add(rootRow); heading.Children.Add(_sceneSearch); DockPanel.SetDock(heading, Dock.Top); browser.Children.Add(heading);
        var actions = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(_catalogStatus);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _load = Command("Open scene", LoadSelected); _load.IsEnabled = false; buttons.Children.Add(_load);
        _cancel = Command("Cancel", () => { _cancellation?.Cancel(); return Task.CompletedTask; }); _cancel.IsVisible = false; buttons.Children.Add(_cancel);
        _return = Command("Return to scene", () => { if (!Busy && _document != null) ShowBrowser(false); return Task.CompletedTask; });
        _return.IsEnabled = false; buttons.Children.Add(_return);
        actions.Children.Add(buttons); DockPanel.SetDock(actions, Dock.Bottom); browser.Children.Add(actions); browser.Children.Add(_scenes);
        _scenes.ItemTemplate = new FuncDataTemplate<ShrineSceneEntry>((entry, _) => new TextBlock { Text = entry?.ToString(), TextTrimming = TextTrimming.CharacterEllipsis });
        _scenes.SelectionChanged += (_, _) => _load.IsEnabled = !Busy && _scenes.SelectedItem != null;
        _scenes.DoubleTapped += async (_, _) => { try { await LoadSelected(); } catch (Exception ex) { _catalogStatus.Text = ex.Message; } };
        _scenes.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; try { await LoadSelected(); } catch (Exception ex) { _catalogStatus.Text = ex.Message; } } };
        _sceneSearch.TextChanged += (_, _) => FilterScenes();
        _root.Text = ActorBrowserPaths.Read(AppDataPaths.SettingsFile("actor-folders.json")).Totk;
        _browser = browser; _pages.Children.Add(_browser); _pages.Children.Add(_editor); Content = _pages;
        bool attempted = false;
        AttachedToVisualTree += async (_, _) => { if (!attempted && !string.IsNullOrWhiteSpace(_root.Text)) { attempted = true; try { await Scan(); } catch (Exception ex) { _catalogStatus.Text = ex.Message; } } };
        _search.TextChanged += (_, _) => Filter(); _groundOnly.IsCheckedChanged += (_, _) => Filter();
        _actors.SelectionChanged += (_, _) => Selected(); _actors.DoubleTapped += (_, _) => _view.FrameSelected();
        _view.SelectionRequested += (ids, operation) =>
        {
            if (_document == null) return;
            if (_actors.ItemsSource is IEnumerable<ShrineActor> visible && ids.Except(visible.Select(a => a.Index)).Any()) { _search.Text = ""; _groundOnly.IsChecked = false; }
            var selected = operation == SelectionOperation.Replace ? new HashSet<int>() : SelectedActors().Select(a => a.Index).ToHashSet();
            foreach (int id in ids) if (operation != SelectionOperation.Toggle || !selected.Remove(id)) selected.Add(id);
            SetSelection(selected);
            if (_actors.SelectedItem != null) _actors.ScrollIntoView(_actors.SelectedItem);
        };
        _compound.IsEnabled = false;
        _save.IsEnabled = _undo.IsEnabled = _redo.IsEnabled = _duplicate.IsEnabled = _delete.IsEnabled = false;
        _view.TransformStarted += () => _moving = SelectedActors().Select(a => a.Index).ToArray();
        _view.TransformPreview += delta =>
        {
            if (_editing != null && _moving.Length > 0) _view.SetModelPose(_editing.PreviewTransform(_moving, delta));
        };
        _view.TransformFinished += delta =>
        {
            try
            {
                if (_editing != null && _moving.Length > 0 && delta is { } transform)
                    _editing.TransformTogether(_moving, transform);
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _moving = []; RefreshEdit(); }
        };
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            bool sceneFocus = e.Source is Control source && (source == _view || source == _actors || source.GetVisualAncestors().Contains(_actors));
            if (!Busy && !e.Handled && sceneFocus && !_view.TransformActive)
            {
                if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control && _duplicate.IsEnabled)
                { e.Handled = true; try { await DuplicateActors(); } catch (Exception ex) { _status.Text = ex.Message; } return; }
                if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None && _delete.IsEnabled)
                { e.Handled = true; try { await DeleteActors(); } catch (Exception ex) { _status.Text = ex.Message; } return; }
            }
            if (!Busy && e.Key is Key.G or Key.R or Key.S && !e.Handled && e.KeyModifiers == KeyModifiers.None &&
                e.Source is Control control && (control == _actors || control.GetVisualAncestors().Contains(_actors)))
            { _view.Focus(); e.Handled = _view.TransformKey(e); return; }
            if (Busy || _editing == null || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            if (e.Key == Key.S) { e.Handled = true; await Save(); }
            else if (e.Key == Key.Z && e.Source is not TextBox)
            { e.Handled = true; _view.FinishTransform(false); if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _editing.Redo(); else _editing.Undo(); RefreshEdit(); }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }
    private Button Command(string text, Func<Task> action)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
        b.Click += async (_, _) => { try { await action(); } catch (Exception ex) { _catalogStatus.Text = _status.Text = ex.Message; } }; return b;
    }
    private Task Open()
    {
        _view.FinishTransform(false); ShowBrowser(true); return Task.CompletedTask;
    }
    private Task DuplicateActors()
    {
        if (Busy || _editing == null) return Task.CompletedTask;
        _view.FinishTransform(false);
        var added = _editing.Duplicate(SelectedActors().Select(a => a.Index));
        RefreshEdit(); SetSelection(added);
        if (_actors.SelectedItem != null) _actors.ScrollIntoView(_actors.SelectedItem);
        _status.Text = $"Duplicated {added.Count} actor(s), including their supported collision. The copies are selected.";
        _view.Focus(); return Task.CompletedTask;
    }
    private async Task DeleteActors()
    {
        if (Busy || _editing == null) return;
        _view.FinishTransform(false);
        var selected = SelectedActors(); if (selected.Length == 0) return;
        if (!await Dialogs.Confirm(_owner, "Delete actors", $"Delete {selected.Length} selected actor(s) and their collision? You can undo this change.")) return;
        _editing.Delete(selected.Select(a => a.Index)); RefreshEdit();
        SetSelection(_editing.Scene.Actors.Skip(Math.Min(selected.Min(a => a.Index), Math.Max(0, _editing.Scene.Actors.Count - 1))).Take(1).Select(a => a.Index));
        _status.Text = $"Deleted {selected.Length} actor(s) and their supported collision."; _view.Focus();
    }
    private void ShowBrowser(bool browser) { _browser.IsVisible = browser; _editor.IsVisible = !browser; }
    private async Task BrowseRoot()
    {
        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new() { Title = "TOTK romfs folder" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) { _root.Text = path; await Scan(); }
    }
    internal async Task Scan()
    {
        if (Busy) return;
        SetBusy(true); _catalogStatus.Text = "Finding BANC scenes...";
        try
        {
            var root = ShrineSceneCatalog.Root(_root.Text?.Trim() ?? "");
            _entries = await Task.Run(() => ShrineSceneCatalog.Scan(root, _cancellation!.Token));
            var settings = AppDataPaths.SettingsFile("actor-folders.json"); var paths = ActorBrowserPaths.Read(settings); paths.Totk = root;
            HKCLTool.AtomicFile.WriteAllBytes(settings, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(paths));
            FilterScenes();
        }
        catch (OperationCanceledException) { _catalogStatus.Text = "Scan cancelled."; }
        finally { SetBusy(false); }
    }
    private void FilterScenes()
    {
        string query = _sceneSearch.Text?.Trim() ?? "";
        var entries = _entries.Where(e => e.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _scenes.ItemsSource = entries; _catalogStatus.Text = $"{entries.Length:N0} of {_entries.Count:N0} BANC scenes";
    }
    private async Task LoadSelected() { if (!Busy && _scenes.SelectedItem is ShrineSceneEntry entry) await LoadPathAsync(entry.Path); }
    private void SetBusy(bool busy)
    {
        Busy = busy; _open.IsEnabled = _scan.IsEnabled = _browse.IsEnabled = _root.IsEnabled = !busy;
        _load.IsEnabled = !busy && _scenes.SelectedItem != null; _cancel.IsVisible = busy;
        _return.IsEnabled = !busy && _document != null;
        _view.IsEnabled = _actors.IsEnabled = _details.IsEnabled = !busy;
        _save.IsEnabled = !busy && _editing != null; _undo.IsEnabled = !busy && _editing?.CanUndo == true; _redo.IsEnabled = !busy && _editing?.CanRedo == true;
        if (busy) _duplicate.IsEnabled = _delete.IsEnabled = false;
        else UpdatePopulationActions();
        if (busy) _cancellation = new(); else { _cancellation?.Dispose(); _cancellation = null; }
    }
    internal async Task LoadPathAsync(string path)
    {
        if (Busy) return;
        if (!await CanCloseAsync()) return;
        SetBusy(true); _compound.IsEnabled = false;
        try
        {
            string root;
            try { root = ShrineSceneCatalog.Root(path); } catch (DirectoryNotFoundException) { root = ShrineSceneCatalog.Root(_root.Text ?? ""); }
            var progress = new Progress<string>(text => { _status.Text = text; _catalogStatus.Text = text; });
            var assets = await Task.Run(() => ShrineSceneAssets.Load(path, root, progress, _cancellation!.Token));
            var editing = await Task.Run(() => new ShrinePlacementEditor(assets, root));
            _editing = editing;
            _assets = assets; _document = assets.Scene;
            _picking = new(assets.Model);
            _view.ModelItemPicker = (origin, direction) => _picking.Pick(origin, direction, _view.HiddenMeshes);
            _view.SetModel(assets.Model); Filter(); ShowBrowser(false);
            var center = (assets.Model.BoundsMinimum + assets.Model.BoundsMaximum) * .5f;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _view.StartFlyAt(center + Vector3.UnitY * 1.7f), Avalonia.Threading.DispatcherPriority.Loaded);
            bool paired = _document.Actors.Any(a => a.SourceScene.Contains("_Static.bcett.")) && _document.Actors.Any(a => a.SourceScene.Contains("_Dynamic.bcett."));
            _status.Text = $"{Path.GetFileName(path)} ({(paired ? "static + dynamic, " : "")}{editing.SourceCount} source files) / {assets.RenderedActors:N0} modeled actors / {_document.Actors.Count:N0} placements / {assets.Model.TriangleCount:N0} triangles / " +
                (assets.Compound == null ? "No matching BPHSC loaded" : $"BPHSC loaded: {assets.Compound.Bodies.Count} bodies") +
                (assets.Warnings.Count == 0 ? "" : $" / {assets.Warnings.Count} asset notices");
            ToolTip.SetTip(_status, string.Join("\n", assets.Warnings.Take(30)));
        }
        catch (OperationCanceledException) { _catalogStatus.Text = _status.Text = "Scene loading cancelled."; }
        finally { SetBusy(false); _compound.IsEnabled = FindCompound() != null; }
    }
    private string? FindCompound()
    {
        return _assets?.CompoundPath;
    }
    private void Filter()
    {
        if (_document == null) return;
        var selected = SelectedActors().Select(a => a.Index).ToHashSet(); string query = _search.Text ?? "";
        var actors = _document.Actors.Where(a => (_groundOnly.IsChecked != true || a.Name.StartsWith("GroundControlActor", StringComparison.Ordinal)) &&
            $"{a} {a.Hash:x16} {a.Hash} {a.GroupId} {a.GroupGuid:x16}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _selecting = true; _actors.ItemsSource = actors; _selecting = false;
        var data = new ParticlePreviewData();
        foreach (var actor in actors) data.Particles.Add(new() { Index = actor.Index, Position = actor.Position, Fixed = actor.Name.StartsWith("GroundControlActor", StringComparison.Ordinal), Radius = .5f });
        _view.Data = data;
        SetSelection(selected.Count == 0 ? actors.Take(1).Select(a => a.Index) : selected);
    }
    private ShrineActor[] SelectedActors() => _actors.SelectedItems?.Cast<ShrineActor>().ToArray() ?? [];
    private void SetSelection(IEnumerable<int> indices)
    {
        var ids = indices.ToHashSet();
        _selecting = true;
        _actors.SelectedItems?.Clear();
        if (_actors.ItemsSource is IEnumerable<ShrineActor> actors)
            foreach (var actor in actors) if (ids.Contains(actor.Index)) _actors.SelectedItems?.Add(actor);
        _selecting = false; Selected();
    }
    private void Selected()
    {
        if (_selecting) return;
        UpdatePopulationActions();
        _details.Children.Clear(); _view.Selected.Clear(); _view.Data.Links.Clear();
        _view.ModelSelectionBounds = null;
        _view.CanTransform = false;
        if (_actors.SelectedItem is ShrineActor actor)
        {
            var selected = SelectedActors(); bool multiple = selected.Length > 1;
            foreach (var item in selected) _view.Selected.Add(item.Index);
            bool controller = actor.Name.StartsWith("GroundControlActor_", StringComparison.Ordinal);
            Label(multiple ? $"{selected.Length} actors selected" : controller ? "GroundControl" : actor.Name, 18);
            if (!multiple && controller) Label(actor.Name["GroundControlActor_".Length..]);
            if (!multiple) Label($"Hash: 0x{actor.Hash:x16}");
            var bounds = selected.Select(a => _picking?.Bounds(a.Index)).Where(b => b != null).Select(b => b!.Value).ToArray();
            if (bounds.Length > 0) _view.ModelSelectionBounds = (bounds.Select(b => b.Item1).Aggregate(Vector3.Min), bounds.Select(b => b.Item2).Aggregate(Vector3.Max));
            if (!multiple && actor.Name.StartsWith("GroundControlActor", StringComparison.Ordinal))
            { GroundControlInspector(actor); _view.InvalidateVisual(); return; }
            if (!multiple && _editing?.GroundControl(actor.Index) is { } group)
                _details.Children.Add(Command("Edit GroundControl group", () => { ShowController(group.Controller); return Task.CompletedTask; }));
            if (multiple) Label("World offset");
            else
            Label($"World position: {actor.Position}");
            var coordinates = new NumericUpDown[3];
            for (int i = 0; i < 3; i++)
            {
                var row = new Grid { ColumnDefinitions = new("24,*") };
                row.Children.Add(new TextBlock { Text = "XYZ"[i].ToString(), VerticalAlignment = VerticalAlignment.Center });
                coordinates[i] = new NumericUpDown { Value = multiple ? 0 : (decimal)actor.LocalPosition[i], FormatString = "0.#####", ShowButtonSpinner = false, HorizontalAlignment = HorizontalAlignment.Stretch };
                Grid.SetColumn(coordinates[i], 1); row.Children.Add(coordinates[i]); _details.Children.Add(row);
            }
            var apply = Command(multiple ? "Move selected" : "Apply position", () =>
            {
                var value = new Vector3((float)(coordinates[0].Value ?? 0), (float)(coordinates[1].Value ?? 0), (float)(coordinates[2].Value ?? 0));
                if (multiple) _editing!.MoveTogether(selected.Select(a => a.Index), value);
                else _editing!.Move(actor.Index, value);
                RefreshEdit(); return Task.CompletedTask;
            });
            string? restriction = selected.Select(a => _editing?.EditRestriction(a.Index)).FirstOrDefault(r => r != null);
            apply.IsEnabled = _editing != null && restriction == null; _view.CanTransform = apply.IsEnabled;
            _details.Children.Add(apply);
            if (restriction != null) Label(restriction);
            void TransformFields(string label, string id, Vector3 initial, Action<Vector3> action)
            {
                Label(label);
                var fields = new NumericUpDown[3];
                for (int i = 0; i < 3; i++)
                {
                    var row = new Grid { ColumnDefinitions = new("24,*") };
                    row.Children.Add(new TextBlock { Text = "XYZ"[i].ToString(), VerticalAlignment = VerticalAlignment.Center });
                    fields[i] = new NumericUpDown { Name = id + "XYZ"[i], Value = (decimal)initial[i], FormatString = "0.#####",
                        ShowButtonSpinner = false, HorizontalAlignment = HorizontalAlignment.Stretch };
                    Grid.SetColumn(fields[i], 1); row.Children.Add(fields[i]); _details.Children.Add(row);
                }
                var button = Command(id == "ShrineRotation" ? "Apply rotation" : "Apply scale", () =>
                {
                    action(new((float)(fields[0].Value ?? 0), (float)(fields[1].Value ?? 0), (float)(fields[2].Value ?? 0)));
                    RefreshEdit(); return Task.CompletedTask;
                });
                button.Name = id + "Apply"; button.IsEnabled = apply.IsEnabled; _details.Children.Add(button);
            }
            var pivot = selected.Aggregate(Vector3.Zero, (sum, a) => sum + a.Position) / selected.Length;
            Matrix4x4 AroundPivot(Matrix4x4 matrix) => Matrix4x4.CreateTranslation(-pivot) * matrix * Matrix4x4.CreateTranslation(pivot);
            TransformFields(multiple ? "Rotation offset (radians)" : "Local rotation (radians)", "ShrineRotation", multiple ? Vector3.Zero : actor.Rotation, value =>
            {
                if (multiple) _editing!.TransformTogether(selected.Select(a => a.Index), AroundPivot(Matrix4x4.CreateFromQuaternion(BoneRotationAngles.FromRadians(value))));
                else _editing!.SetTransform(actor.Index, ShrineTransform.Local(actor) with { Rotation = value });
            });
            TransformFields(multiple ? "Scale factor" : "Local scale", "ShrineScale", multiple ? Vector3.One : actor.Scale, value =>
            {
                if (multiple) _editing!.TransformTogether(selected.Select(a => a.Index), AroundPivot(Matrix4x4.CreateScale(value)));
                else _editing!.SetTransform(actor.Index, ShrineTransform.Local(actor) with { Scale = value });
            });
            if (multiple) { _view.InvalidateVisual(); return; }
            Label($"Placement ID: 0x{actor.PlacementId:x16}\nGroupID: {actor.GroupId}\nGroupGUID: 0x{actor.GroupGuid:x16}");
            if (actor.MergedPath.Length > 0) Label("Merged scene: " + actor.MergedPath);
            Label("Source: " + Path.GetFileName(actor.SourceScene));
            foreach (string parameter in actor.Parameters) Label(parameter);
            foreach (var link in actor.Links)
            {
                var target = _document?.Actors.FirstOrDefault(a => a.Hash == link.Destination);
                Label($"{link.Name}: {target?.Name ?? "External actor"} / 0x{link.Destination:x16}");
                if (target != null && _view.Data.Particles.Any(p => p.Index == target.Index)) _view.Data.Links.Add(new() { ParticleA = actor.Index, ParticleB = target.Index });
            }
        }
        _view.InvalidateVisual();
    }
    private void Label(string text, double size = 14) => _details.Children.Add(new SelectableTextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap });
    private void RefreshEdit()
    {
        if (_editing == null) return;
        if (_document?.Actors.Count != _editing.Scene.Actors.Count) _view.HiddenMeshes.Clear();
        _document = _editing.Scene; _picking = new(_editing.Model); _view.SetModelPose(_editing.Model);
        Filter();
        _undo.IsEnabled = _editing.CanUndo; _redo.IsEnabled = _editing.CanRedo;
    }
    private void UpdatePopulationActions()
    {
        var selected = SelectedActors();
        foreach (var (button, delete) in new[] { (_duplicate, false), (_delete, true) })
        {
            string? reason = _editing == null || selected.Length == 0 ? "Select an actor first." :
                selected.Select(a => _editing.PopulationRestriction(a.Index, delete)).FirstOrDefault(r => r != null);
            button.IsEnabled = !Busy && reason == null;
            ToolTip.SetShowOnDisabled(button, true);
            ToolTip.SetTip(button, reason ?? (delete ? "Delete selected actors and their collision. Delete" : "Duplicate selected actors and their collision. Ctrl+D"));
        }
    }
    internal Task<bool> CanCloseAsync() => Busy ? Task.FromResult(false) : _editing?.Dirty == true
        ? Dialogs.Confirm(_owner, "Unsaved shrine", "Discard unsaved shrine placement changes?") : Task.FromResult(true);
    private async Task Save()
    {
        if (Busy || _editing == null) return;
        _view.FinishTransform(false);
        try
        {
            var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new() { Title = "Save shrine mod folder" });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            SetBusy(true);
            string output = await Task.Run(() => _editing.Save(path));
            _status.Text = "Saved placements and rebuilt collision: " + output;
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { SetBusy(false); }
    }
}
