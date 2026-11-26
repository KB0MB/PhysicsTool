using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;
using SkiaSharp;

namespace PhysicsTool.Desktop;

internal sealed partial class IconToolView : UserControl
{
    private readonly Window _owner;
    private readonly WrapPanel _toolbar = new() { Orientation = Orientation.Horizontal };
    private readonly string _botwMannequinRoot;
    private readonly string _preferencesPath;
    private readonly string _actorFoldersPath;
    private readonly IconToolPreferences _preferences;
    private readonly ActorBrowserPaths _dumpPaths;
    private bool _modBrowser, _browserTotk;
    private ComboBox? _browserGame;
    private readonly TextBox _root = new() { Watermark = "Game resource folder", Name = "IconActorRoot" };
    private IReadOnlyList<IconCapturePreset> _library = [];
    private Window? _actorBrowser;
    private readonly TextBox _search = new() { Watermark = "Search actors", Name = "IconActorSearch" };
    private readonly ListBox _actors = new() { MinHeight = 120, Name = "IconActorResults" };
    private readonly TextBlock _actorStatus = new() { Name = "IconActorStatus", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _presetStatus = new() { Name = "IconPresetStatus", TextWrapping = TextWrapping.Wrap };
    private string? _scannedRoot;
    private string? _scanNotice;
    private readonly ComboBox _presets = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 360, IsTextSearchEnabled = true };
    private readonly ComboBox _pose = new() { Name = "IconArmorPose", HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };
    private const string IconPoseLabel = "Icon Pose";
    private readonly ComboBox _poseBone = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly NumericUpDown[] _boneAngles = VectorInputs(-90, 90, .5m);
    private readonly Dictionary<string, Vector3> _poseAdjustments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Expander _poseEditor;
    private VisualModelData? _adjustedModel;
    private readonly StackPanel _cameraSettings = new() { Spacing = 8 };
    private readonly TextBlock _actorName = new() { Text = "No actor selected", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    internal PhysicsViewport Viewport => _viewport;
    private const string CurrentSettingsLabel = "Current settings";
    internal bool UsesViewport => Equals(_presets.SelectedItem, "Viewport") || _presets.SelectedItem is CustomPreset { Viewport: not null };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image _reference = new() { Stretch = Stretch.Uniform, Width = 168, Height = 168 };
    private readonly PhysicsViewport _viewport = new() { ShowPhysics = false, ShowGrid = false, CanSelect = false, ReadOnlyActorView = true, HighPrecisionDepth = true };
    internal ModelGlControl? Surface { get; }
    internal IconCaptureAsset? Asset { get; private set; }
    internal bool Busy { get; private set; }
    private IconActor[] _catalog = [];
    private readonly NumericUpDown[] _rotation = VectorInputs(-360, 360, 1);
    private readonly NumericUpDown[] _camera = VectorInputs(-10000, 10000, .01m);
    private readonly NumericUpDown[] _angles = VectorInputs(-360, 360, 1);
    private readonly NumericUpDown[] _light = VectorInputs(-1, 1, .05m);
    private readonly NumericUpDown _fov = Number(.1m, 120, .1m, 5);
    private readonly NumericUpDown _direct = Number(0, 10, .1m, 1);
    private readonly NumericUpDown _ambient = Number(0, 10, .1m, 1);
    private readonly ComboBox _environment = new() { Name = "IconEnvironment", ItemsSource = BotwLightingEnvironment.All, SelectedItem = BotwLightingEnvironment.IconCapture, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly NumericUpDown _brightness = Number(0, 4, .05m, 3);
    private readonly NumericUpDown _zoom = Number(25, 400, 5, 100);
    private readonly CheckBox _auto = new() { Content = "Animate materials", IsChecked = true };
    private readonly CheckBox _edges = new() { Content = "Edge highlights", Name = "IconEdgeHighlights", IsChecked = true };
    private readonly Avalonia.Threading.DispatcherTimer _materialTimer = ViewportFrameRate.CreateTimer();
    private long _materialTick;
    private double _materialTime;
    internal int CaptureSize { get; private set; } = 256;
    private readonly Button _export;
    private readonly StackPanel _settings = new() { Spacing = 10, IsEnabled = false };
    private bool _updating;
    private Bitmap? _referenceBitmap;
    private VisualModelData? _rotatedModel;
    private Vector3 _lastRotation;

    internal IconToolView(Window owner, bool graphics = true, string? botwMannequinRoot = null, string? preferencesPath = null, string? actorFoldersPath = null)
    {
        _owner = owner;
        _actorFoldersPath = actorFoldersPath ?? AppDataPaths.SettingsFile("actor-folders.json");
        var stored = ActorBrowserPaths.Read(_actorFoldersPath);
        _dumpPaths = stored;
        _preferencesPath = preferencesPath ?? AppDataPaths.SettingsFile("icon-tool.json");
        _preferences = IconToolPreferences.Read(_preferencesPath);
        _browserTotk = _preferences.TotkSelected;
        _botwMannequinRoot = botwMannequinRoot ?? stored.Botw;
        _root.Text = stored.Botw.Length > 0 ? stored.Botw : stored.Totk;
        try { _library = IconCaptureLibrary.Load(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { _status.Text = "Capture presets: " + ex.Message; }
        var shell = new DockPanel { Margin = new Thickness(12) };
        var toolbar = _toolbar;
        toolbar.Children.Add(Command("Open actor", () => BrowseActors(false)));
        toolbar.Children.Add(Command("Open mod actor", () => BrowseActors(true)));
        var meshes = Command("Meshes", ShowCaptureMeshes); meshes.Name = "IconMeshes"; toolbar.Children.Add(meshes);
        _export = Command("Capture icon", Export); _export.Name = "CaptureIcon";
        ToolTip.SetTip(_export, "Save the current view in the selected icon format");
        _export.IsEnabled = false;
        _cancelExport.Click += (_, _) => _batchCancellation?.Cancel(); toolbar.Children.Add(_cancelExport);
        var reset = ViewportTools.Icon("RotateCcw", "Reset capture settings", () => { _brightness.Value = 3; CaptureSize = 256; _edges.IsChecked = true; SetBeltVisible(true); ApplyPreset(); }); toolbar.Children.Add(reset);
        var fit = ViewportTools.Icon("Maximize", "Fit model", Fit); toolbar.Children.Add(fit);
        var zoomOut = ViewportTools.Icon("ZoomOut", "Zoom out", () => ZoomCapture(1 / 1.1)); zoomOut.Name = "IconZoomOut";
        var zoomIn = ViewportTools.Icon("ZoomIn", "Zoom in", () => ZoomCapture(1.1)); zoomIn.Name = "IconZoomIn";
        _zoom.Name = "IconZoom"; _zoom.Width = 90; ToolTip.SetTip(_zoom, "Capture zoom (%)");
        toolbar.Children.Add(zoomOut); toolbar.Children.Add(_zoom); toolbar.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center }); toolbar.Children.Add(zoomIn);
        _zoom.ValueChanged += (_, _) => { if (Surface != null) { Surface.ProjectionZoom = (float)(_zoom.Value ?? 100) / 100; Surface.RequestNextFrameRendering(); } };
        var toolbarRow = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 10) };
        toolbarRow.Children.Add(toolbar);
        _export.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(_export, 1); toolbarRow.Children.Add(_export);
        DockPanel.SetDock(toolbarRow, Dock.Top); shell.Children.Add(toolbarRow);
        DockPanel.SetDock(_status, Dock.Bottom); _status.Margin = new Thickness(0, 8, 0, 0); shell.Children.Add(_status);
        var layout = new Grid { ColumnDefinitions = new("360,*"), ColumnSpacing = 14 };
        var sidebar = new StackPanel { Spacing = 12 };
        sidebar.Children.Add(_actorName);
        sidebar.Children.Add(CharacterSelector());
        sidebar.Children.Add(new TextBlock { Text = "Capture", FontSize = 18 });
        sidebar.Children.Add(_presets);
        sidebar.Children.Add(new WrapPanel { Children = { Command("Save preset", SaveCustomPreset), Command("Open preset", OpenCustomPreset) } });
        sidebar.Children.Add(_presetStatus);
        for (int i = 0; i < 3; i++) { _angles[i].Name = "IconCameraAngle" + i; _camera[i].Name = "IconCameraPosition" + i; _light[i].Name = "IconLightDirection" + i; }
        _fov.Name = "IconFov"; _direct.Name = "IconDirectLight"; _ambient.Name = "IconAmbientLight"; _auto.Name = "IconAutoMaterials";
        _presets.Name = "IconCaptureMode";
        FilterPresets();
        for (int i = 0; i < 3; i++) { _rotation[i].Name = "IconActorRotation" + i; _position[i].Name = "IconActorPosition" + i; }
        _settings.Children.Add(Section("Actor", new StackPanel { Spacing = 8, Children = { _pose, _simulateGravity, VectorRow("Position", _position), VectorRow("Rotation (degrees)", _rotation) } }));
        ToolTip.SetTip(_simulateGravity, "Settle cloth and hair under gravity with a fixed pose and no wind");
        _simulateGravity.IsCheckedChanged += (_, _) => { if (!_updating) { QueueSettlement(); _rotatedModel = null; UpdatePreview(); } };
        var poseSettings = new StackPanel { Spacing = 8 };
        poseSettings.Children.Add(_poseBone);
        for (int i = 0; i < 3; i++)
        {
            var input = _boneAngles[i];
            var slider = new Slider { Minimum = -90, Maximum = 90, TickFrequency = .5, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => { if (input.Value != (decimal)slider.Value) input.Value = (decimal)slider.Value; };
            input.ValueChanged += (_, _) => slider.Value = (double)(input.Value ?? 0);
            var row = new Grid { ColumnDefinitions = new("*,85"), ColumnSpacing = 6 };
            row.Children.Add(slider); Grid.SetColumn(input, 1); row.Children.Add(input);
            poseSettings.Children.Add(Row("XYZ"[i] + " rotation", row));
            input.ValueChanged += (_, _) =>
            {
                if (!_updating && _poseBone.SelectedItem is string bone) SetPoseAdjustment(bone, Get(_boneAngles));
            };
        }
        var poseButtons = new WrapPanel();
        poseButtons.Children.Add(ViewportTools.Icon("RotateCcw", "Reset pose adjustments", ResetPoseAdjustments));
        poseButtons.Children.Add(Command("Import BFSKA", ImportBfska));
        poseSettings.Children.Add(poseButtons);
        _poseFrame.Name = "IconPoseFrame";
        _poseFrameSlider.Name = "IconPoseFrameSlider";
        var frameRow = new Grid { ColumnDefinitions = new("*,85"), ColumnSpacing = 6 };
        frameRow.Children.Add(_poseFrameSlider); Grid.SetColumn(_poseFrame, 1); frameRow.Children.Add(_poseFrame);
        _poseFrameRow = Row("Frame", frameRow); _poseFrameRow.IsVisible = false;
        poseSettings.Children.Add(_poseFrameRow);
        _poseFrameSlider.ValueChanged += (_, _) => _poseFrame.Value = (decimal)_poseFrameSlider.Value;
        _poseFrame.ValueChanged += (_, _) => { _poseFrameSlider.Value = (double)(_poseFrame.Value ?? 0); if (!_updating) { RebuildPose(); _rotatedModel = null; UpdatePreview(); } };
        _poseEditor = Section("Pose adjustments", poseSettings, false); _poseEditor.IsVisible = false;
        _settings.Children.Add(_poseEditor);
        _cameraSettings.Children.Add(VectorRow("Position", _camera));
        _cameraSettings.Children.Add(VectorRow("Angles (pitch / yaw / roll)", _angles));
        _cameraSettings.Children.Add(Row("Vertical FOV", _fov));
        _settings.Children.Add(Section("Camera", _cameraSettings));
        var lighting = new StackPanel { Spacing = 8 };
        lighting.Children.Add(Row("Lighting", _environment));
        _environment.SelectionChanged += (_, _) => { if (!_updating) UpdatePreview(); };
        _brightness.Name = "IconBrightness";
        var brightnessSlider = new Slider { Name = "IconBrightnessSlider", Minimum = 0, Maximum = 4, Value = 3, TickFrequency = .05, IsSnapToTickEnabled = true };
        brightnessSlider.ValueChanged += (_, _) =>
        {
            decimal value = Math.Round((decimal)brightnessSlider.Value, 2);
            if (_brightness.Value != value) _brightness.Value = value;
        };
        _brightness.ValueChanged += (_, _) => brightnessSlider.Value = (double)(_brightness.Value ?? 3);
        var brightnessRow = new Grid { ColumnDefinitions = new("*,85"), ColumnSpacing = 6 };
        brightnessRow.Children.Add(brightnessSlider); Grid.SetColumn(_brightness, 1); brightnessRow.Children.Add(_brightness);
        lighting.Children.Add(Row("Brightness", brightnessRow));
        lighting.Children.Add(VectorRow("Ray direction", _light));
        lighting.Children.Add(Row("Direct light", _direct));
        lighting.Children.Add(Row("Ambient light", _ambient));
        var emission = _emission;
        _viewport.ShowEmission = true;
        emission.IsCheckedChanged += (_, _) => { _viewport.ShowEmission = emission.IsChecked == true; Surface?.RequestNextFrameRendering(); };
        lighting.Children.Add(emission);
        lighting.Children.Add(_castShadows);
        _castShadows.IsCheckedChanged += (_, _) => { if (!_updating) UpdatePreview(); };
        _settings.Children.Add(Section("Lighting", lighting));
        _settings.Children.Add(_auto);
        _dye.SelectionChanged += async (_, _) =>
        {
            if (!_updating && (!Busy || _changingDye) && _dye.SelectedItem is IconDye choice)
            {
                try { await SelectDyeAsync(choice); }
                catch (Exception ex) { await ShowErrorAsync("Change dye", ex); }
            }
        };
        _dyeRow = Row("Dye", _dye); _dyeRow.IsVisible = false; _settings.Children.Add(_dyeRow);
        _settings.Children.Add(_edges);
        ToolTip.SetTip(_auto, "Play _auto material animations, including glowing effects and scrolling textures");
        ToolTip.SetTip(_edges, "Screen-space edge highlights; an approximation, not the native game post-process");
        _edges.IsCheckedChanged += (_, _) => UpdatePreview();
        _settings.Children.Add(Section("Game icon", _reference, false));
        sidebar.Children.Add(_settings);
        var scroll = new ScrollViewer { Content = sidebar, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        layout.Children.Add(scroll);
        var square = new Grid { Width = 576, Height = 576, Background = new SolidColorBrush(Color.Parse("#242829")) };
        if (graphics) { Surface = new ModelGlControl(_viewport); square.Children.Add(Surface); Surface.RenderFailed += message => _status.Text = message; }
        // Fixed projection controls own the camera; orbit gestures must not silently diverge from the exported image.
        _viewport.IsHitTestVisible = false;
        square.Children.Add(_viewport);
        var preview = new Viewbox { Name = "IconCapturePreview", Stretch = Stretch.Uniform, Child = square };
        ConfigureModelInteraction(preview);
        preview.AddHandler(Avalonia.Input.InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            if (Busy || Asset == null || _viewport.TransformActive) return;
            ZoomCapture(Math.Pow(1.1, e.Delta.Y)); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Grid.SetColumn(preview, 1); layout.Children.Add(preview);
        shell.Children.Add(layout); Content = shell;
        _search.TextChanged += (_, _) => Filter();
        _actors.DoubleTapped += async (_, _) => await LoadSelectedActor();
        _actors.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; await LoadSelectedActor(); } };
        _presets.SelectionChanged += async (_, _) => { if (!_updating) { try { if (_presets.SelectedItem is CustomPreset custom) { await EnsurePresetCharacter(custom); _updating = true; _presets.SelectedItem = custom; _updating = false; } ApplyPreset(); } catch (Exception ex) { await ShowErrorAsync("Apply preset", ex); } } };
        _pose.SelectionChanged += (_, _) =>
        {
            if (_updating) return;
            RebuildPose();
            _rotatedModel = null;
            UpdatePreview();
        };
        _poseBone.SelectionChanged += (_, _) => SyncPoseAngles();
        foreach (var input in _rotation.Concat(_position).Concat(_camera).Concat(_angles).Concat(_light).Concat(new[] { _fov, _direct, _ambient, _brightness }))
            input.ValueChanged += (_, _) => { if (!_updating) UpdatePreview(); };
        AttachedToVisualTree += (_, _) => { _materialTick = System.Diagnostics.Stopwatch.GetTimestamp(); _materialTimer.Start(); };
        DetachedFromVisualTree += (_, _) => { _materialTimer.Stop(); _settlementCancellation?.Cancel(); _batchCancellation?.Cancel(); };
        _materialTimer.Tick += (_, _) =>
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double seconds = System.Diagnostics.Stopwatch.GetElapsedTime(_materialTick, now).TotalSeconds; _materialTick = now;
            if (Busy || _auto.IsChecked != true || Asset == null || !Asset.Materials.Any(m => m.HasTracks)) return;
            _materialTime += Math.Min(seconds, .25);
            foreach (var material in Asset.Materials) material.Sample(_materialTime);
            Surface?.RequestNextFrameRendering();
        };
    }

    private void SyncPoseAngles()
    {
        bool previous = _updating; _updating = true;
        Set(_boneAngles, _poseBone.SelectedItem is string bone ? _poseAdjustments.GetValueOrDefault(bone) : Vector3.Zero);
        _updating = previous;
    }
    internal void SetPoseAdjustment(string bone, Vector3 angles)
    {
        if (Asset?.ArmorPose is not { } pose || !pose.Bones.Contains(bone, StringComparer.OrdinalIgnoreCase)) return;
        if (!float.IsFinite(angles.LengthSquared()) || Math.Max(Math.Max(Math.Abs(angles.X), Math.Abs(angles.Y)), Math.Abs(angles.Z)) > 90)
            throw new InvalidDataException("Pose rotation must be between -90 and 90 degrees.");
        if (angles == Vector3.Zero) _poseAdjustments.Remove(bone); else _poseAdjustments[bone] = angles;
        RebuildPose();
        _rotatedModel = null; UpdatePreview();
    }
    private void ResetPoseAdjustments()
    {
        _poseAdjustments.Clear(); RebuildPose(); _rotatedModel = null;
        SyncPoseAngles(); UpdatePreview();
    }
    private void RebuildPose()
    {
        bool imported = UsesImportedPose;
        _poseFrameRow.IsVisible = imported;
        _adjustedModel = Asset?.ArmorPose is { } pose
            ? pose.Apply(_poseAdjustments, imported: imported ? _importedPose : null,
                frame: imported ? (double)(_poseFrame.Value ?? 0) : 0,
                recoveredPose: !imported && pose.UsesRecoveredIconPose, addBelt: true) : null;
        QueueSettlement();
    }
    private sealed record SavedPose(string BasePose, Dictionary<string, Vector3> Rotations, byte[]? Bfska = null, double Frame = 0, bool AddBelt = false);

    private void FilterPresets()
    {
        var selected = _presets.SelectedItem;
        _updating = true;
        var all = IconCaptureLibrary.ForActor(_library.Concat(Asset?.Presets ?? []), Asset?.Name);
        string category = IconCaptureLibrary.Category(Asset?.Name ?? "");
        var custom = _customPresets.Where(p =>
        {
            string savedCategory = IconCaptureLibrary.Category(string.IsNullOrEmpty(p.ActorName) ? p.Name : p.ActorName);
            return category.Length == 0 || savedCategory.Length == 0 || savedCategory == category;
        });
        var grouped = new List<object> { "Viewport", CurrentSettingsLabel };
        var saved = custom.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (saved.Length > 0)
        {
            grouped.Add(PresetSection("Custom presets"));
            grouped.AddRange(saved);
        }
        foreach (var group in all.GroupBy(p => IconCaptureLibrary.ActorType(p.Name)).OrderBy(g => g.Key.Order))
        {
            grouped.Add(PresetSection(group.Key.Label));
            grouped.AddRange(group.OrderBy(p => p.ToString(), StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.Ordinal));
        }
        var choices = grouped.ToArray();
        _presets.ItemsSource = choices;
        _presets.SelectedItem = selected is IconCapturePreset native ? IconCaptureLibrary.Resolve(all, native) ?? (object)"Viewport" :
            selected != null && choices.Contains(selected) ? selected : "Viewport";
        _presetStatus.Text = all.Count == 0 ? "Capture preset library unavailable." : $"{all.Count:N0} capture presets";
        _updating = false;
    }

    private static ComboBoxItem PresetSection(string label) => new()
    {
        Name = "IconPresetSection", IsEnabled = false, Focusable = false, IsHitTestVisible = false,
        Padding = new Thickness(12, 10, 12, 4),
        Content = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold }
    };

    private async Task BrowseActors(bool mod)
    {
        _modBrowser = mod;
        _root.Text = BrowserRoot();
        var browser = new DockPanel { Margin = new Thickness(14) };
        var header = new StackPanel { Spacing = 8 };
        var game = new ComboBox { Name = "IconActorGame", ItemsSource = new[] { "BOTW", "TOTK" }, SelectedIndex = _browserTotk ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        _browserGame = game;
        game.SelectionChanged += async (_, _) =>
        {
            try
            {
                SaveBrowserRoot(); _browserTotk = game.SelectedIndex == 1;
                _preferences.TotkSelected = _browserTotk; _preferences.Save(_preferencesPath);
                _root.Text = BrowserRoot(); _catalog = []; Filter();
                if (Directory.Exists(_root.Text)) await Scan();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _actorStatus.Text = ex.Message; }
        };
        header.Children.Add(game);
        header.Children.Add(_root);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { Command(mod ? "Mod folder" : "Resource folder", BrowseRoot), Command("Scan", Scan) } });
        header.Children.Add(_search);
        header.Children.Add(_actorStatus);
        DockPanel.SetDock(header, Dock.Top); browser.Children.Add(header);
        var open = Command("Open actor", LoadSelectedActor); open.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(open, Dock.Bottom); browser.Children.Add(open); browser.Children.Add(_actors);
        var dialog = new Window { Title = mod ? "Icon Tool - Mod Actors" : "Icon Tool - Actors", Width = 640, Height = 640, MinWidth = 450, MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = browser };
        _actorBrowser = dialog;
        dialog.Opened += async (_, _) => { if (!Directory.Exists(_root.Text)) { _catalog = []; Filter(); return; } if (_scannedRoot != _root.Text || _catalog.Length == 0) await Scan(); else Filter(); };
        try { await dialog.ShowDialog(_owner); }
        finally
        {
            try { SaveBrowserRoot(); }
            finally { dialog.Content = null; browser.Children.Clear(); header.Children.Clear(); _actorBrowser = null; _browserGame = null; }
        }
    }

    private string BrowserRoot() => _modBrowser ? _preferences.Mod(_browserTotk) : _browserTotk ? _dumpPaths.Totk : _dumpPaths.Botw;
    private void SaveBrowserRoot()
    {
        string root = _root.Text?.Trim() ?? "";
        if (!Directory.Exists(root)) return;
        if (_modBrowser) { _preferences.SetMod(_browserTotk, root); _preferences.Save(_preferencesPath); }
        else
        {
            if (_browserTotk) _dumpPaths.Totk = root; else _dumpPaths.Botw = root;
            HKCLTool.AtomicFile.WriteAllBytes(_actorFoldersPath, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_dumpPaths));
        }
    }

    private async Task LoadSelectedActor()
    {
        if (Busy || _actors.SelectedItem is not IconActor actor) return;
        var previous = Asset;
        _actorStatus.Text = "Loading " + actor.Name;
        await LoadPathAsync(actor.Path, actor.ResourceRoot, showErrors: true);
        if (!ReferenceEquals(previous, Asset)) _actorBrowser?.Close();
        else _actorStatus.Text = _status.Text;
    }

    private Button Command(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) };
        button.Click += async (_, _) => await RunCommandAsync(text, action);
        return button;
    }
    internal async Task RunCommandAsync(string operation, Func<Task> action)
    {
        if (Busy) return;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await ShowErrorAsync(operation, ex); }
    }
    private Task ShowErrorAsync(string operation, Exception error)
    {
        _status.Text = UserFacingExceptionFormatter.Format(error);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = operation + " failed", FontWeight = FontWeight.SemiBold });
        panel.Children.Add(new ScrollViewer { MaxHeight = 360, Content = new SelectableTextBlock { Text = _status.Text, TextWrapping = TextWrapping.Wrap } });
        return Dialogs.Show(_actorBrowser ?? _owner, "Icon Tool - Error", panel);
    }
    private async Task BrowseRoot()
    {
        var folders = await (_actorBrowser ?? _owner).StorageProvider.OpenFolderPickerAsync(new() { Title = "Game resource folder", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) { _root.Text = path; await Scan(); }
    }
    private async Task Scan()
    {
        if (Busy) return;
        _viewport.FinishTransform(false);
        Busy = true;
        if (_browserGame != null) _browserGame.IsEnabled = false;
        _actors.IsEnabled = false; _actorStatus.Text = "Scanning actors...";
        try
        {
            var root = (_root.Text ?? "").Trim();
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Game resource folder not found.");
            SaveBrowserRoot();
            var warnings = new List<string>();
            var catalog = await Task.Run(() => IconCaptureAsset.Catalog(root, warnings));
            _catalog = catalog.Where(a => a.Path.EndsWith(".pack.zs", StringComparison.OrdinalIgnoreCase) == _browserTotk).ToArray();
            _scannedRoot = _root.Text; _scanNotice = warnings.FirstOrDefault(); Filter();
        }
        catch (Exception ex) { _catalog = []; _actors.ItemsSource = _catalog; _scannedRoot = null; _actorStatus.Text = ex.Message; await ShowErrorAsync("Scan actors", ex); }
        finally { Busy = false; _actors.IsEnabled = true; if (_browserGame != null) _browserGame.IsEnabled = true; }
    }
    private void Filter()
    {
        var matches = _catalog.Where(a => a.Name.Contains((_search.Text ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        _actors.ItemsSource = matches;
        _actorStatus.Text = _catalog.Length == 0 ? "No actor packs found in this folder or its game-data subfolders." :
            matches.Length == 0 ? "No actors match this search." : $"{matches.Length:N0} of {_catalog.Length:N0} actors";
        if (_scanNotice != null) _actorStatus.Text += " | " + _scanNotice;
    }
    internal async Task LoadPathAsync(string path, string root, bool showErrors = false, bool preserveCapture = false)
    {
        if (Busy) return;
        Busy = true; _settings.IsEnabled = false; _export.IsEnabled = false; _status.Text = "Loading " + System.IO.Path.GetFileName(path);
        _character.IsEnabled = false;
        try
        {
            var previousPreset = _presets.SelectedItem;
            var previousPose = preserveCapture && Asset != null ? CurrentPose() : null;
            var previousDye = _selectedDye;
            bool preserveSettings = Asset != null;
            bool totk = path.EndsWith(".pack.zs", StringComparison.OrdinalIgnoreCase);
            string dump = totk ? _dumpPaths.Totk : _dumpPaths.Botw;
            var fallback = Directory.Exists(dump) ? BotwActorCatalog.FindResourceRoots([dump], new List<string>(), CancellationToken.None).ToArray() : [];
            var result = await Task.Run(() => IconCaptureAsset.Load(path, root, botwMannequinRoot: _botwMannequinRoot, fallbackRoots: fallback, character: _selectedCharacter));
            if (preserveCapture && result.WithDye != null && result.Dyes!.Choices.Contains(previousDye)) result = result.WithDye(previousDye);
            Asset = result;
            _simulateGravity.IsVisible = result.HasSimulationData;
            _hiddenMeshes.Clear(); _viewport.HiddenMeshes.Clear();
            _simulationPath = path; _simulationRoot = root;
            _environment.IsEnabled = result.Model.Meshes.Any(m => m.Material.Botw != null || m.Material.Totk is { IsOot: false });
            _castShadows.IsEnabled = _environment.IsEnabled;
            _importedPose = null; _importedBfska = null;
            _poseAdjustments.Clear(); _adjustedModel = null;
            _rotatedModel = null;
            _materialTime = 0;
            _actorName.Text = result.Name;
            _updating = true;
            _dye.ItemsSource = result.Dyes?.Choices ?? [IconDye.Original];
            _selectedDye = preserveCapture && result.Dyes?.Choices.Contains(previousDye) == true ? previousDye : result.Dyes?.Default ?? IconDye.Original; _dye.SelectedItem = _selectedDye;
            _dyeRow.IsVisible = result.Dyes is { Choices.Count: > 1 };
            _pose.IsVisible = result.PoseName != null || result.ArmorPose?.UsesRecoveredIconPose == true;
            _pose.ItemsSource = PoseChoices();
            _pose.SelectedIndex = 0;
            RebuildPose();
            if (CaptureModel!.Meshes.Any(m => IconArmorProps.IsProp(m.Name)))
                foreach (var mesh in CaptureModel.Meshes.Where(m => result.IsMeshHiddenByDefault(m.Name)))
                    _hiddenMeshes.Add(new(mesh.Name, 0));
            _poseEditor.IsVisible = result.ArmorPose != null;
            _poseBone.ItemsSource = result.ArmorPose?.Bones;
            _poseBone.SelectedIndex = 0;
            _updating = false;
            _updating = true; _presets.SelectedItem = null; _updating = false;
            FilterPresets();
            _updating = true;
            var requested = _library.FirstOrDefault(p => p.Name == result.Name) ?? result.Presets.FirstOrDefault(p => p.Name != "Manual");
            var available = _presets.Items.OfType<IconCapturePreset>().ToArray();
            var matchingPreset = IconCaptureLibrary.Resolve(available, requested);
            preserveSettings &= preserveCapture || matchingPreset == null;
            object? previousChoice = previousPreset is IconCapturePreset previousNative ? IconCaptureLibrary.Resolve(available, previousNative) :
                previousPreset != null && _presets.Items.Contains(previousPreset) ? previousPreset : null;
            _presets.SelectedItem = preserveSettings ? previousChoice ?? CurrentSettingsLabel :
                matchingPreset ?? previousChoice ?? "Viewport";
            _updating = false;
            _reference.Source = null; _referenceBitmap?.Dispose(); _referenceBitmap = null;
            if (result.Reference is { } icon)
            {
                using var png = new MemoryStream(Encode(icon.Pixels, icon.Width, icon.Height, false));
                _reference.Source = _referenceBitmap = new Bitmap(png);
            }
            if (preserveSettings)
            {
                if (previousPose != null && result.ArmorPose is { } armorPose)
                    ApplySavedPose(previousPose with { Rotations = previousPose.Rotations.Where(p => armorPose.Bones.Contains(p.Key, StringComparer.OrdinalIgnoreCase)).ToDictionary(p => p.Key, p => p.Value), AddBelt = previousPose.AddBelt && armorPose.HasBelt });
                _cameraSettings.IsEnabled = !UsesViewport; _viewport.IsHitTestVisible = UsesViewport;
                UpdatePreview();
            }
            else ApplyPreset();
            _status.Text = result.Name + (result.Warnings.Count > 0 ? " | " + string.Join("; ", result.Warnings.Distinct().Take(3)) : "");
        }
        catch (Exception ex) { _status.Text = ex.Message; if (showErrors) await ShowErrorAsync("Open actor", ex); }
        finally { _updating = false; Busy = false; _character.IsEnabled = true; _settings.IsEnabled = Asset != null; _export.IsEnabled = Asset != null && Surface != null; }
    }
    private void ApplyPreset()
    {
        if (Asset == null) return;
        if (_presets.SelectedItem is CustomPreset custom) { ApplyCustomPreset(custom); return; }
        if (Equals(_presets.SelectedItem, CurrentSettingsLabel))
        {
            _cameraSettings.IsEnabled = true; _viewport.IsHitTestVisible = false; UpdatePreview(); return;
        }
        _viewport.FinishTransform(false);
        _updating = true; Set(_position, Vector3.Zero); _updating = false;
        _zoom.Value = 100;
        _cameraSettings.IsEnabled = !UsesViewport;
        _viewport.IsHitTestVisible = UsesViewport;
        if (UsesViewport)
        {
            UpdatePreview();
            _viewport.Fit();
            return;
        }
        if (Asset == null || _presets.SelectedItem is not IconCapturePreset preset) return;
        _updating = true;
        Set(_rotation, preset.Rotation);
        Set(_angles, Vector3.Zero); _direct.Value = _ambient.Value = 1;
        var model = IconCaptureAsset.Rotate(CaptureModel!, preset.Rotation);
        var center = (model.BoundsMinimum + model.BoundsMaximum) * .5f;
        Set(_camera, preset.CameraPosition ?? new(center.X, center.Y, model.BoundsMaximum.Z + Math.Max(1, Vector3.Distance(model.BoundsMinimum, model.BoundsMaximum) * 3)));
        Set(_light, preset.LightDirection ?? Vector3.Normalize(new Vector3(-.35f, -.8f, -.45f)));
        try { _fov.Value = (decimal)(preset.FieldOfView ?? IconCaptureAsset.FitFieldOfView(model, Get(_camera))); }
        catch (InvalidDataException ex) { _fov.Value = 45; _status.Text = ex.Message; }
        _updating = false; UpdatePreview();
        if (Surface?.FixedCamera is { } captureCamera)
        {
            var framingExcluded = new HashSet<int>(_viewport.HiddenMeshes);
            // Head icons frame the headgear and face; the mannequin torso exits the bottom of the image.
            if (Asset.Name.EndsWith("_Head", StringComparison.Ordinal))
                for (int i = 0; i < _rotatedModel!.Meshes.Count; i++)
                    if (_rotatedModel.Meshes[i].Name.EndsWith("__Mt_Upper_Skin", StringComparison.Ordinal) || _rotatedModel.Meshes[i].Name == IconArmorProps.Prefix + "Icon_Head_Torso") framingExcluded.Add(i);
            _zoom.Value = (decimal)IconCaptureAsset.DefaultCaptureZoom(_rotatedModel!, captureCamera, framingExcluded, Asset.Reference, Asset.Name.EndsWith("_Head", StringComparison.Ordinal));
        }
    }
    private void Fit()
    {
        if (Asset == null) return;
        _zoom.Value = 100;
        if (UsesViewport) { _viewport.Fit(); return; }
        try { _fov.Value = (decimal)IconCaptureAsset.FitFieldOfView(IconCaptureAsset.Rotate(CaptureModel!, Get(_rotation), Get(_position)), Get(_camera), Get(_angles)); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void ZoomCapture(double factor)
    {
        if (Asset == null) return;
        _zoom.Value = Math.Clamp(Math.Round((_zoom.Value ?? 100) * (decimal)factor, 1), _zoom.Minimum, _zoom.Maximum);
    }
    private void UpdatePreview()
    {
        if (Asset == null) return;
        var rotation = Get(_rotation);
        var positionOffset = Get(_position);
        if (_rotatedModel == null || rotation != _lastRotation || positionOffset != _lastPosition)
        {
            _lastRotation = rotation; _lastPosition = positionOffset;
            _rotatedModel = IconCaptureAsset.Rotate(CaptureModel!, rotation, positionOffset);
            _viewport.SetModelPose(_rotatedModel);
            SyncMeshVisibility();
        }
        if (Surface != null)
        {
            var position = Get(_camera);
            var view = IconCaptureAsset.View(position, Get(_angles));
            var depth = IconCaptureAsset.DepthRange(_rotatedModel!, view);
            Surface.FixedCamera = UsesViewport ? null : view *
                Matrix4x4.CreatePerspectiveFieldOfView((float)(_fov.Value ?? 5) * MathF.PI / 180, 1, depth.Near, depth.Far);
            Surface.FixedViewDirection = UsesViewport ? null : new Vector3(view.M13, view.M23, view.M33);
            Surface.CameraPosition = UsesViewport ? null : position;
            Surface.KeyLightDirection = -Get(_light);
            Surface.LightingEnvironment = _environment.SelectedItem as BotwLightingEnvironment;
            Surface.CastShadows = _castShadows.IsEnabled && _castShadows.IsChecked == true;
            Surface.DirectIntensity = (float)(_direct.Value ?? 1); Surface.AmbientIntensity = (float)(_ambient.Value ?? 1);
            Surface.Brightness = (float)(_brightness.Value ?? 3);
            Surface.EdgeHighlights = _edges.IsChecked == true;
            Surface.RequestNextFrameRendering();
        }
    }
    internal VisualModelData? CaptureModel => _settledModel ?? _adjustedModel ?? Asset?.Model;
    private async Task Export()
    {
        if (Asset == null || Surface == null) return;
        var dialog = new IconCaptureDialog(Asset.IconTemplate != null, Asset.Dyes is { Choices.Count: > 1 }, CaptureSize, Asset.BntxTemplate != null, _preferences.Format);
        if (!await dialog.ShowDialog<bool>(_owner)) return;
        CaptureSize = dialog.CaptureSize;
        var format = dialog.Format; _preferences.Format = format; _preferences.Save(_preferencesPath);
        string? directory = await ExportDirectory(dialog.ToMod, format);
        if (directory == null) return;
        string stem = IconDyes.FileStem(Asset.Name, _selectedDye);
        string[] stems = dialog.ExportAllDyes && Asset.Dyes != null ? Asset.Dyes.Choices.Where(d => d.Animation.Length > 0).Select(d => IconDyes.FileStem(Asset.Name, d, Asset.Dyes.Choices.Select(c => c.Animation).Distinct().Count() > 1)).ToArray() : [stem];
        int existing = stems.Count(s => File.Exists(Path.Combine(directory, s + format.Extension())));
        if (existing > 0 && !await ConfirmIconReplace(existing, directory)) return;
        if (dialog.ExportAllDyes) { await ExportDyes(format, directory, existing > 0); return; }
        string path = Path.Combine(directory, stem + format.Extension());
        Busy = true; LockCaptureControls(true);
        try { Directory.CreateDirectory(directory); AtomicFile.WriteAllBytes(path, await CaptureIconBytesAsync(stem, format)); _status.Text = "Exported " + path; }
        finally { Busy = false; LockCaptureControls(false); }
    }
    internal async Task<byte[]> CapturePngAsync(int size)
    {
        Task settlement;
        do { settlement = Settlement; await settlement; } while (!ReferenceEquals(settlement, Settlement));
        if (Surface == null) throw new InvalidOperationException("No GPU renderer.");
        if (Surface.Failure is { } failure) throw new InvalidOperationException(failure);
        var completion = new TaskCompletionSource<byte[]>();
        void Failed(string message) => completion.TrySetException(new InvalidOperationException(message));
        void Receive(byte[] pixels, int width, int height)
        {
            try { completion.TrySetResult(Encode(pixels, width, height, true)); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }
        Surface.FrameCaptured += Receive;
        Surface.RenderFailed += Failed;
        bool previousTransparency = Surface.CaptureTransparentBackground;
        try
        {
            Surface.CaptureTransparentBackground = true;
            Surface.CaptureResolution = size; Surface.CaptureNextFrame = true; Surface.RequestNextFrameRendering();
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { Surface.FrameCaptured -= Receive; Surface.RenderFailed -= Failed; Surface.CaptureResolution = null; Surface.CaptureNextFrame = false; Surface.CaptureTransparentBackground = previousTransparency; }
    }
    internal static byte[] Encode(byte[] pixels, int width, int height, bool bottomUp)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, bottomUp ? SKAlphaType.Premul : SKAlphaType.Unpremul);
        for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (bottomUp ? height - 1 - y : y) * bitmap.RowBytes, width * 4);
        using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
    private static NumericUpDown Number(decimal min, decimal max, decimal increment, decimal value = 0) => new() { Minimum = min, Maximum = max, Increment = increment, Value = value, FormatString = "0.###", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 75 };
    private static NumericUpDown[] VectorInputs(decimal min, decimal max, decimal step) => [Number(min, max, step), Number(min, max, step), Number(min, max, step)];
    private static Vector3 Get(NumericUpDown[] input) => new((float)(input[0].Value ?? 0), (float)(input[1].Value ?? 0), (float)(input[2].Value ?? 0));
    private static void Set(NumericUpDown[] input, Vector3 value) { input[0].Value = (decimal)value.X; input[1].Value = (decimal)value.Y; input[2].Value = (decimal)value.Z; }
    private static Control Row(string label, Control control) => new StackPanel { Spacing = 4, Children = { new TextBlock { Text = label }, control } };
    private static Expander Section(string label, Control content, bool expanded = true) => new()
    { Header = label, Content = content, IsExpanded = expanded, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Control VectorRow(string label, NumericUpDown[] inputs)
    {
        var grid = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 4 };
        for (int i = 0; i < 3; i++) { var row = Row("XYZ"[i].ToString(), inputs[i]); Grid.SetColumn(row, i); grid.Children.Add(row); }
        return Row(label, grid);
    }
}
