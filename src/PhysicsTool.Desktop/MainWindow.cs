using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Styling;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow : Window
{
    public EditorSession Session { get; } = new();
    public EditorSession DonorSession { get; } = new();
    private readonly Button _swap, _export;
    private readonly PhysicsViewport _viewport = new();
    public PhysicsViewport Viewport => _viewport;
    public ModelGlControl? ModelSurface { get; }
    private readonly Button _loadModel, _clearModel, _meshes;
    private int _modelRequest;
    private bool _modelLoading;
    private readonly ComboBox _cloths = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
    private readonly ListBox _items = new() { SelectionMode = SelectionMode.Multiple };
    private readonly TabStrip _mode = new() { ItemsSource = new[] { "Particles", "Bones", "Colliders" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ContentControl _body = new();
    private readonly DocumentOverview _overview = new();
    private readonly Grid _workspace;
    private readonly GridSplitter _workspaceSplit;
    private readonly WrapPanel _editorActions;
    private readonly CheckBox _bonesToggle;
    private readonly Button _navigation;
    private bool _fitPending = true;
    public bool IsEditorVisible => ReferenceEquals(_body.Content, _workspace);
    private readonly StackPanel _details = new() { Spacing = 8, Margin = new Thickness(14) };
    private readonly ListBox _relationships = new();
    private readonly Grid _inspector;
    private readonly DockPanel _relationshipPane;
    private readonly GridSplitter _relationshipSplit;
    private readonly WrapPanel _relationshipActions = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(12, 7), Text = "Ready", TextWrapping = TextWrapping.Wrap };
    private readonly Button _simulate, _save, _undo, _redo, _add, _delete, _constraint, _removeConstraint;
    private readonly List<Control> _editControls = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1d / 60) };
    private readonly Stopwatch _clock = new();
    private HkclPreviewSimulator? _solver;
    private readonly PreviewStepClock _previewStepClock = new();
    public PreviewOptions SimulationOptions { get; private set; } = new();
    private readonly Button _massScale, _clothSettings, _options, _editorUndo, _editorRedo;
    private double _lastTick;
    private bool _refreshing, _closingApproved;
    private readonly CheckBox _wind = new() { Content = "Wind" }, _follow = new() { Content = "Particles follow bones", IsChecked = true };
    private sealed record Item(int Index, string Label, object Data) { public override string ToString() => Label; }
    private sealed record Relation(ParticleRelationshipRow Data) { public override string ToString() => $"{Data.Name}  {Data.Particles}"; }

    public MainWindow(string? file = null, bool graphics = true)
    {
        Title = "PhysicsTool Avalonia"; Width = 1440; Height = 900; MinWidth = 980; MinHeight = 640;
        Background = new SolidColorBrush(Color.Parse("#303234"));
        _mode.Styles.Add(new Style(s => s.OfType<TabStripItem>())
        {
            Setters = { new Setter(TemplatedControl.FontSizeProperty, 14d), new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 8)) }
        });
        _cloths.ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        _items.ItemTemplate = new FuncDataTemplate<Item>((item, _) => new TextBlock { Text = item?.Label, TextWrapping = TextWrapping.Wrap });
        _relationships.ItemTemplate = new FuncDataTemplate<Relation>((item, _) => new TextBlock { Text = item?.ToString(), TextWrapping = TextWrapping.Wrap });
        var shell = new DockPanel();
        var topbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 6) };
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        using (var logo = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PhysicsTool.Desktop/Assets/PhysicsTool.png")))
            toolbar.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(logo), Width = 32, Height = 32, Margin = new Thickness(6, 3, 14, 3) });
        toolbar.Children.Add(new TextBlock { Text = "PhysicsTool", FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        Add(toolbar, "New", async () => { if (await CanReplace()) await NewDocument(); });
        Add(toolbar, "Open Physics", async () => await OpenDocument());
        Add(toolbar, "Actor", async () => await OpenActorBrowser());
        Add(toolbar, "Open Donor", async () => await OpenDonor());
        _swap = Add(toolbar, "Swap files", SwapFiles);
        _export = Add(toolbar, "Export", () => OpenExportMenu());
        _save = Add(toolbar, "Save", async () => await SaveDocument());
        _save.ContextMenu = new ContextMenu { ItemsSource = new[] { MenuCommand("Save as...", () => SaveDocument(true)) } };
        _undo = ViewportTools.Icon("Undo2", "Undo (Ctrl+Z)", () => Try(() => { Session.Undo(); Refresh(); })); toolbar.Children.Add(_undo);
        _redo = ViewportTools.Icon("Redo2", "Redo", () => Try(() => { Session.Redo(); Refresh(); })); toolbar.Children.Add(_redo);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        Add(navigation, "Documentation", async () => await new DocumentationWindow().ShowDialog(this));
        Add(navigation, "Settings", async () => await ShowSettings());
        _navigation = Add(navigation, "Editor", () => { if (IsEditorVisible) ShowOverview(); else ShowEditor(); }); _navigation.MinWidth = 76;
        topbar.Children.Add(toolbar); Grid.SetColumn(navigation, 1); topbar.Children.Add(navigation);
        DockPanel.SetDock(topbar, Dock.Top); shell.Children.Add(topbar);
        DockPanel.SetDock(_status, Dock.Bottom); shell.Children.Add(_status);
        var workspace = _workspace = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,490"), RowDefinitions = new RowDefinitions("*,Auto") };
        workspace.ColumnDefinitions[0].MinWidth = 420;
        workspace.ColumnDefinitions[2].MinWidth = 300;
        var center = new DockPanel();
        var modelbar = new WrapPanel { Margin = new Thickness(5) };
        _add = Add(modelbar, "Add Particle", AddItem);
        _loadModel = Add(modelbar, "Load model", async () => await OpenModel());
        _clearModel = Add(modelbar, "Clear model", () => { _modelRequest++; ExitActor(); _viewport.SetModel(null); UpdateModelActions(); });
        _meshes = Add(modelbar, "Meshes", async () => await ShowMeshes());
        DockPanel.SetDock(modelbar, Dock.Top); center.Children.Add(modelbar);
        var viewbar = new WrapPanel { Margin = new Thickness(5) };
        _simulate = Add(viewbar, "Run simulation", ToggleSimulation);
        _wind.Margin = new Thickness(10, 4); viewbar.Children.Add(_wind);
        _options = Add(viewbar, "Options", async () => await ShowPreviewOptions());
        var hide = new CheckBox { Content = "Hide cloth", Margin = new Thickness(10, 4) };
        hide.IsCheckedChanged += (_, _) => { _viewport.ShowPhysics = hide.IsChecked != true; _viewport.InvalidateVisual(); }; viewbar.Children.Add(hide);
        var bones = _bonesToggle = new CheckBox { Content = "Bones", IsChecked = true, Margin = new Thickness(10, 4) }; bones.IsCheckedChanged += (_, _) => { _viewport.ShowBones = bones.IsChecked == true; _viewport.InvalidateVisual(); }; viewbar.Children.Add(bones);
        var colliders = new CheckBox { Content = "Colliders", IsChecked = true, Margin = new Thickness(10, 4) }; colliders.IsCheckedChanged += (_, _) => { _viewport.ShowColliders = colliders.IsChecked == true; _viewport.InvalidateVisual(); }; viewbar.Children.Add(colliders);
        DockPanel.SetDock(viewbar, Dock.Bottom); center.Children.Add(viewbar);
        ConfigureActorBar(center);
        var scene = new Grid();
        if (graphics)
        {
            ModelSurface = new ModelGlControl(_viewport);
            ModelSurface.RenderFailed += message => _status.Text = "GPU preview unavailable; using wireframe. " + message;
            scene.Children.Add(ModelSurface);
        }
        scene.Children.Add(_viewport); scene.Children.Add(ViewportTools.CameraToolbar(_viewport)); scene.Children.Add(new ViewportAxes(_viewport));
        center.Children.Add(scene); workspace.Children.Add(center);
        var split2 = _workspaceSplit = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(split2, 1); workspace.Children.Add(split2);
        var right = _inspector = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,5,200"), Margin = new Thickness(8) };
        right.RowDefinitions[3].MinHeight = 120;
        right.Children.Add(_cloths);
        _follow.HorizontalAlignment = HorizontalAlignment.Right; _follow.Margin = new Thickness(0, 0, 0, 4);
        Grid.SetRow(_follow, 1); right.Children.Add(_follow);
        Grid.SetRow(_mode, 2); right.Children.Add(_mode);
        var values = new Grid { ColumnDefinitions = new ColumnDefinitions("120,5,*") };
        values.ColumnDefinitions[0].MinWidth = 90; values.ColumnDefinitions[2].MinWidth = 160;
        var itemHost = new DockPanel();
        var actions = new WrapPanel(); _delete = Add(actions, "Delete", async () => await DeleteItems());
        DockPanel.SetDock(actions, Dock.Bottom); itemHost.Children.Add(actions); itemHost.Children.Add(_items); values.Children.Add(itemHost);
        var valueSplit = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(valueSplit, 1); values.Children.Add(valueSplit);
        var scroller = new ScrollViewer { Content = _details }; Grid.SetColumn(scroller, 2); values.Children.Add(scroller);
        Grid.SetRow(values, 3); right.Children.Add(values);
        _relationshipSplit = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(_relationshipSplit, 4); right.Children.Add(_relationshipSplit);
        _relationshipPane = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var relationshipTitle = new TextBlock { Text = "Selected particle relationships", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(relationshipTitle, Dock.Top); _relationshipPane.Children.Add(relationshipTitle);
        DockPanel.SetDock(_relationshipActions, Dock.Bottom); _relationshipPane.Children.Add(_relationshipActions); _relationshipPane.Children.Add(_relationships);
        Grid.SetRow(_relationshipPane, 5); right.Children.Add(_relationshipPane);
        Grid.SetColumn(right, 2); workspace.Children.Add(right);
        var editorActions = _editorActions = new WrapPanel { Margin = new Thickness(5, 0, 5, 5) };
        _editorUndo = ViewportTools.Icon("Undo2", "Undo (Ctrl+Z)", () => Try(() => { Session.Undo(); Refresh(); })); editorActions.Children.Add(_editorUndo);
        _editorRedo = ViewportTools.Icon("Redo2", "Redo", () => Try(() => { Session.Redo(); Refresh(); })); editorActions.Children.Add(_editorRedo);
        _massScale = Add(editorActions, "Mass scale", async () => await ShowMassScale());
        _clothSettings = Add(editorActions, "Cloth settings", async () => await ShowClothSettings());
        Grid.SetRow(editorActions, 1); Grid.SetColumnSpan(editorActions, 3); workspace.Children.Add(editorActions);
        _body.Content = _overview; shell.Children.Add(_body); Content = shell;
        _overview.RemoveRequested += async () => await RunCommand(RemoveCloth);
        _overview.DuplicateRequested += () => Try(() => { Session.DuplicateSelected(); Refresh(true); _status.Text = "Duplicated cloth."; });
        _overview.RenameRequested += async () => await RunCommand(RenameCloth);
        _overview.MergeRequested += async () => await RunCommand(MergeDonor);
        _overview.ClearDonorRequested += async () => await RunCommand(ClearDonor);
        _overview.ClothSelected += index => { Session.ClothIndex = index; Refresh(true); };
        _overview.EditRequested += ShowEditor;
        _constraint = new Button { Content = "Add constraint" }; _constraint.Click += async (_, _) => await AddConstraint();
        _removeConstraint = new Button { Content = "Remove constraint" }; _removeConstraint.Click += async (_, _) => await RemoveConstraint();
        _relationshipActions.Children.Add(_constraint); _relationshipActions.Children.Add(_removeConstraint);
        _constraint.Margin = new Thickness(0, 4, 6, 0); _removeConstraint.Margin = new Thickness(0, 4, 0, 0);
        _editControls.AddRange(new Control[] { _cloths, _mode, _items, _details, _save, _undo, _redo, _add, _delete });
        _cloths.SelectionChanged += (_, _) => { if (_refreshing || _cloths.SelectedIndex < 0) return; StopSimulation(); Session.ClothIndex = _cloths.SelectedIndex; Refresh(true); };
        _mode.SelectionChanged += (_, _) => { if (!_refreshing) Refresh(); };
        _items.SelectionChanged += (_, _) => { if (_refreshing) return; ShowDetails(); SelectViewport(); UpdateActions(); };
        _relationships.SelectionChanged += (_, _) => UpdateActions();
        _viewport.SelectionRequested += (indices, operation) =>
        {
            if (_timer.IsEnabled || _mode.SelectedIndex != 0) return;
            _refreshing = true;
            try
            {
                if (operation == SelectionOperation.Replace) _items.SelectedItems?.Clear();
                foreach (var item in _items.Items.Cast<Item>().Where(i => indices.Contains(i.Index)))
                    if (_items.SelectedItems?.Contains(item) == true) { if (operation == SelectionOperation.Toggle) _items.SelectedItems.Remove(item); }
                    else _items.SelectedItems?.Add(item);
            }
            finally { _refreshing = false; }
            ShowDetails(); SelectViewport(); UpdateActions();
        };
        _timer.Tick += (_, _) =>
        {
            if (_solver == null) return;
            try
            {
                var now = _clock.Elapsed.TotalSeconds; _solver.WindEnabled = _wind.IsChecked == true;
                var steps = _previewStepClock.Advance(now - _lastTick, SimulationOptions.PlaybackSpeed); _lastTick = now;
                for (var step = 0; step < steps; step++) _solver.Step((float)PreviewStepClock.FixedStep);
                _viewport.Simulated = _solver.GetPositions(); _viewport.InvalidateVisual();
            }
            catch (Exception ex) { StopSimulation(); _status.Text = ex.Message; }
        };
        KeyDown += async (_, e) =>
        {
            var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (!e.Handled && command && e.Key == Key.S && _save.IsEnabled)
            { await RunCommand(() => SaveDocument(e.KeyModifiers.HasFlag(KeyModifiers.Shift))); e.Handled = true; return; }
            if (e.Handled || e.Source is TextBox or NumericUpDown) return;
            if (IsActorMode)
            {
                if (e.Key == Key.Space && _viewport.IsFocused) { ToggleActorPlayback(); e.Handled = true; }
                return;
            }
            if (command && e.Key == Key.Z && !_timer.IsEnabled)
            { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Session.Redo(); else Session.Undo(); Refresh(true); e.Handled = true; return; }
            if (command && e.Key == Key.Y && !_timer.IsEnabled) { Session.Redo(); Refresh(true); e.Handled = true; return; }
            if (!IsEditorVisible)
            {
                if (e.Key == Key.M && !command && DonorSession.HasSelectedEntry) { await RunCommand(MergeDonor); e.Handled = true; }
                return;
            }
            if (e.Key == Key.Space && _viewport.IsFocused) { ToggleSimulation(); e.Handled = true; }
            else if (!_timer.IsEnabled && e.Key == Key.F && _mode.SelectedIndex == 0) { await AddConstraint(); e.Handled = true; }
            else if (!_timer.IsEnabled && e.Key == Key.Delete && _items.IsFocused) { await DeleteItems(); e.Handled = true; }
        };
        Closing += async (_, e) => { if (_closingApproved) return; if (Session.IsDirty || DonorSession.IsDirty) { e.Cancel = true; if (await Dialogs.Confirm(this, "Unsaved changes", "Discard unsaved changes in the current and donor files and close?")) { _closingApproved = true; Close(); } } else StopSimulation(); };
        Closed += (_, _) => { _modelRequest++; _actorLoad?.Cancel(); _actorTimer.Stop(); _actorScene?.Dispose(); _actorScene = null; StopSimulation(); };
        Opened += (_, _) => { if (file != null) Try(() => LoadPath(file)); };
        Refresh(); UpdateModelActions();
    }
    private Button Add(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(12, 7) };
        button.Click += (_, _) => Try(action); panel.Children.Add(button); return button;
    }
    private Button Add(Panel panel, string text, Func<Task> action)
    {
        var button = new Button { Content = text, Margin = new Thickness(3), Padding = new Thickness(12, 7) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { _status.Text = ex.Message; } }; panel.Children.Add(button); return button;
    }
    private void Try(Action action) { try { action(); } catch (Exception ex) { _status.Text = ex.Message; } }
    public void ShowEditor()
    {
        if (IsEditorVisible) return;
        _body.Content = _workspace; _navigation.Content = "Back"; UpdateActions();
        Dispatcher.UIThread.Post(() => { if (!IsEditorVisible) return; if (_fitPending) { _viewport.Fit(); _fitPending = false; } _viewport.Focus(); }, DispatcherPriority.Loaded);
    }
    public void ShowOverview()
    {
        StopSimulation(); ExitActor(); _overview.Update(Session, DonorSession); _body.Content = _overview; _navigation.Content = "Editor"; UpdateActions();
    }
    public void LoadDonorPath(string path) { DonorSession.Load(path); ShowOverview(); _overview.ShowDonorBones(); _status.Text = "Donor: " + System.IO.Path.GetFileName(path); }
    private async Task OpenDonor()
    {
        if (!await CanReplace(DonorSession)) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open donor physics", AllowMultiple = false,
            FileTypeFilter = PhysicsFileTypes });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) LoadDonorPath(path);
    }
    public void LoadPath(string path) { Session.Load(path); StopSimulation(); ExitActor(); Refresh(true); _status.Text = $"Opened {System.IO.Path.GetFileName(path)}"; }
    public void Refresh(bool fit = false)
    {
        if (IsActorMode) { UpdateActions(); return; }
        var selected = (_items.SelectedItem as Item)?.Index ?? 0;
        _refreshing = true;
        try
        {
            _cloths.ItemsSource = Enumerable.Range(0, Session.Document.GetClothSummaries().Count).Select(Session.Document.GetClothName).ToArray(); _cloths.SelectedIndex = Session.ClothIndex;
            if (Session.Document.IsHelperBoneSidecar) _mode.SelectedIndex = 1;
            _items.ItemsSource = Array.Empty<Item>(); _viewport.Data = new();
            if (Session.Document.HasDocument && _cloths.SelectedIndex >= 0)
            {
                var doc = Session.Document; var cloth = Session.ClothIndex;
                IEnumerable<Item> rows = _mode.SelectedIndex switch
                {
                    1 => doc.GetBoneRows(cloth).Select(b => new Item(b.Index, $"{b.Index}: {b.Name}", b)),
                    2 => doc.GetColliderRows(cloth).Select(c => new Item(c.Index, $"{c.Index}: {c.Name} ({c.ShapeType})", c)),
                    _ => doc.GetParticleRows(cloth).Select(p => new Item(p.Index, $"{p.Index}  {(p.Fixed ? "Fixed" : "Free")}", p))
                };
                var array = rows.ToArray(); _items.ItemsSource = array; _items.SelectedItem = array.FirstOrDefault(i => i.Index == selected) ?? array.FirstOrDefault();
                _viewport.Data = doc.GetParticlePreview(cloth);
            }
            Title = $"PhysicsTool Avalonia - {(Session.Path == null ? "Untitled" : System.IO.Path.GetFileName(Session.Path))}{(Session.IsDirty ? " *" : "")}";
        }
        finally { _refreshing = false; }
        ShowDetails(); SelectViewport();
        if (fit) { if (IsEditorVisible) _viewport.Fit(); else _fitPending = true; }
        if (!IsEditorVisible) _overview.Update(Session, DonorSession);
        _viewport.InvalidateVisual(); UpdateActions();
    }
    private void SelectViewport()
    {
        _viewport.Selected.Clear(); if (_mode.SelectedIndex == 0) foreach (var item in _items.SelectedItems?.Cast<Item>() ?? []) _viewport.Selected.Add(item.Index);
        _viewport.InvalidateVisual();
    }
    private void ShowDetails()
    {
        _details.Children.Clear(); _relationships.ItemsSource = Array.Empty<Relation>();
        if (_items.SelectedItem is not Item item) return;
        _details.Children.Add(new TextBlock { Text = item.Label, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (Session.Document.IsHelperBoneSidecar || Session.Document.IsHkrg) { _details.Children.Add(new TextBlock { Text = "Editing this format has not been ported yet.", TextWrapping = TextWrapping.Wrap }); return; }
        if (item.Data is ParticleEditRow particle)
        {
            var fixedBox = new CheckBox { Content = "Fixed", IsChecked = particle.Fixed };
            fixedBox.IsCheckedChanged += (_, _) => Try(() => Edit((doc, cloth) => { var row = doc.GetParticleRows(cloth).Single(p => p.Index == item.Index); row.Fixed = fixedBox.IsChecked == true; ParticleEdits.Apply(doc, cloth, new[] { row }); })); _details.Children.Add(fixedBox);
        }
        var fields = item.Data switch { ParticleEditRow => new[] { "X", "Y", "Z", "Mass", "Radius", "Friction" }, BoneEditRow => new[] { "X", "Y", "Z", "ScaleX", "ScaleY", "ScaleZ" }, _ => new[] { "StartX", "StartY", "StartZ", "EndX", "EndY", "EndZ", "Radius" } };
        var inputs = new Dictionary<PropertyInfo, NumericUpDown>();
        foreach (var name in fields)
        {
            var property = item.Data.GetType().GetProperty(name)!;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
            grid.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var number = new NumericUpDown { Name = "Editor" + name, Value = Convert.ToDecimal(property.GetValue(item.Data), CultureInfo.InvariantCulture), Increment = .01m, FormatString = "0.######", Minimum = name is "Mass" or "Radius" or "Friction" ? 0 : -1000000, Maximum = 1000000 };
            Grid.SetColumn(number, 1); grid.Children.Add(number); _details.Children.Add(grid); inputs.Add(property, number);
        }
        var apply = new Button { Content = "Apply", HorizontalAlignment = HorizontalAlignment.Stretch };
        apply.Click += (_, _) => Try(() => Edit((doc, cloth) =>
        {
            foreach (var (property, input) in inputs) property.SetValue(item.Data, Convert.ToSingle(input.Value ?? 0));
            switch (item.Data)
            {
                case ParticleEditRow p: p.InverseMass = p.Fixed || p.Mass <= 0 ? 0 : 1 / p.Mass; ParticleEdits.Apply(doc, cloth, new[] { p }); break;
                case BoneEditRow b: doc.UpdateBoneRows(cloth, new[] { b }, _follow.IsChecked == true); break;
                case ColliderEditRow c: doc.UpdateColliderRows(new[] { c }); break;
            }
        })); _details.Children.Add(apply);
        if (item.Data is ParticleEditRow)
        {
            _relationships.ItemsSource = Session.Document.GetParticleRelationships(Session.ClothIndex, item.Index).Select(r => new Relation(r)).ToArray();
        }
    }
    private void Edit(Action<HkclService, int> action)
    {
        if (IsActorMode) throw new InvalidOperationException("Actor view is read-only.");
        if (_timer.IsEnabled) throw new InvalidOperationException("Stop simulation before editing.");
        try { Session.Edit(action); } finally { Refresh(); }
        _status.Text = "Updated document.";
    }
    private void UpdateActions()
    {
        var ready = Session.Document.HasDocument && _cloths.SelectedIndex >= 0;
        var editing = ready && !_timer.IsEnabled && !Session.Document.IsHelperBoneSidecar && !Session.Document.IsHkrg;
        foreach (var c in _editControls) c.IsEnabled = !_timer.IsEnabled;
        _mode.IsEnabled = !_timer.IsEnabled && !Session.Document.IsHelperBoneSidecar;
        _save.IsEnabled = Session.Document.HasDocument && !_timer.IsEnabled;
        _export.IsEnabled = _save.IsEnabled;
        _swap.IsEnabled = !IsEditorVisible && Session.Document.HasDocument && DonorSession.Document.HasDocument;
        _undo.IsEnabled = !_timer.IsEnabled && Session.CanUndo; _redo.IsEnabled = !_timer.IsEnabled && Session.CanRedo;
        _editorUndo.IsEnabled = _undo.IsEnabled; _editorRedo.IsEnabled = _redo.IsEnabled;
        _editorUndo.IsVisible = _editorRedo.IsVisible = IsEditorVisible;
        _undo.IsVisible = _redo.IsVisible = !IsEditorVisible;
        _massScale.IsEnabled = editing && _mode.SelectedIndex == 0 && Session.CanScaleMass;
        _clothSettings.IsEnabled = !_timer.IsEnabled && Session.CanEditClothSettings;
        _options.IsEnabled = Session.CanEditClothSettings;
        _wind.IsEnabled = Session.CanEditClothSettings;
        ToolTip.SetTip(_massScale, "Scale selected dynamic particles, or all dynamic particles when none are selected. Fixed particles are unchanged.");
        ToolTip.SetTip(_clothSettings, Session.Document.IsBphcl ? "Native BPHCL cloth settings are currently read-only." : "Edit saved gravity, damping, collision tolerance, and motion transfer.");
        ToolTip.SetTip(_options, "Preview-only wind, gravity, playback speed, and solver passes.");
        _add.IsEnabled = editing && (_mode.SelectedIndex == 0 || !Session.Document.IsBphcl);
        _add.Content = _mode.SelectedIndex switch { 1 => "Add Bone", 2 => "Add Collider", _ => "Add Particle" };
        _follow.IsEnabled = editing;
        var relationshipsVisible = _mode.SelectedIndex == 0 && !Session.Document.IsHelperBoneSidecar && !Session.Document.IsHkrg;
        if (_relationshipPane.IsVisible != relationshipsVisible)
        {
            _relationshipPane.IsVisible = _relationshipSplit.IsVisible = relationshipsVisible;
            _inspector.RowDefinitions[4].Height = new GridLength(relationshipsVisible ? 5 : 0);
            _inspector.RowDefinitions[5].Height = new GridLength(relationshipsVisible ? 200 : 0);
        }
        _delete.IsEnabled = editing && _items.SelectedItem != null && (_mode.SelectedIndex != 1 || !Session.Document.IsBphcl);
        _constraint.IsEnabled = editing && Session.Document.GetParticleRows(Session.ClothIndex).Count >= 2;
        _removeConstraint.IsEnabled = editing && _relationships.SelectedItem is Relation r && Session.Document.CanDeleteParticleRelationship(Session.ClothIndex, r.Data);
        _simulate.IsEnabled = _timer.IsEnabled || ready && Session.CanSimulate; _simulate.Content = _timer.IsEnabled ? "Stop simulation" : "Run simulation";
        _viewport.CanSelect = editing && _mode.SelectedIndex == 0;
        _add.IsVisible = !IsActorMode;
        _loadModel.IsVisible = _clearModel.IsVisible = !IsActorMode;
        if (IsActorMode)
        {
            _save.IsEnabled = _export.IsEnabled = _undo.IsEnabled = _redo.IsEnabled = false;
            _add.IsEnabled = _delete.IsEnabled = _constraint.IsEnabled = _removeConstraint.IsEnabled = false;
            _viewport.CanSelect = false;
            _simulate.IsEnabled = _actorScene!.HasCloth;
            _simulate.Content = _actorSimulating ? "Stop simulation" : "Run simulation";
            _wind.IsEnabled = _options.IsEnabled = _actorScene.HasCloth;
        }
    }
    private void AddItem() => Edit((doc, cloth) => { if (_mode.SelectedIndex == 0) doc.AddParticle(cloth); else if (_mode.SelectedIndex == 1) doc.AddBone(cloth); else doc.AddCollider(cloth); });
    private async Task DeleteItems()
    {
        if (_timer.IsEnabled || _items.SelectedItem is not Item selected) return;
        var indices = _items.SelectedItems!.Cast<Item>().Select(i => i.Index).ToArray();
        if (!await Dialogs.Confirm(this, "Delete selection", "Delete the selected items and their dependent relationships?")) return;
        Try(() => Edit((doc, cloth) => { if (_mode.SelectedIndex == 0) doc.DeleteParticles(cloth, indices); else if (_mode.SelectedIndex == 1) doc.DeleteBone(cloth, selected.Index); else doc.DeleteCollider(cloth, selected.Index); }));
    }
    private async Task AddConstraint()
    {
        if (!_constraint.IsEnabled || _timer.IsEnabled) return;
        var indices = _items.SelectedItems?.Cast<Item>().Select(i => i.Index).ToArray() ?? [];
        if (indices.Length == 3) { Try(() => Edit((doc, cloth) => doc.LinkParticles(cloth, indices))); return; }
        var result = await Dialogs.Constraint(this, Session.Document.GetParticleRows(Session.ClothIndex), indices);
        if (result != null) Try(() => Edit((doc, cloth) => doc.AddClothConstraint(cloth, result.Value.Indices, result.Value.Options)));
    }
    private async Task RemoveConstraint()
    {
        if (!_removeConstraint.IsEnabled || _relationships.SelectedItem is not Relation r) return;
        if (await Dialogs.Confirm(this, "Remove constraint", $"Remove {r.Data.Name}?")) Try(() => Edit((doc, cloth) => doc.DeleteParticleRelationship(cloth, r.Data)));
    }
    public void ToggleSimulation()
    {
        if (_actorScene != null)
        {
            if (!_actorScene.HasCloth) return;
            _actorSimulating = !_actorSimulating; PoseActor(true, 0); UpdateActorPlayback(); UpdateActions(); return;
        }
        if (_timer.IsEnabled) { StopSimulation(); return; }
        if (!Session.Document.HasDocument || !Session.CanSimulate) return;
        _solver = new HkclPreviewSimulator(Session.Document.GetParticlePreview(Session.ClothIndex));
        SimulationOptions.ApplyTo(_solver); _previewStepClock.Reset();
        _clock.Restart(); _lastTick = 0; _timer.Start(); UpdateActions();
    }
    public void StopSimulation() { _actorSimulating = false; if (_actorScene != null) { PoseActor(true, 0); UpdateActorPlayback(); } _timer.Stop(); _previewStepClock.Reset(); _solver = null; _viewport.Simulated = null; _viewport.InvalidateVisual(); UpdateActions(); }
    private Task<bool> CanReplace() => CanReplace(Session);
    private async Task<bool> CanReplace(EditorSession session) => !session.IsDirty || await Dialogs.Confirm(this, "Unsaved changes", "Discard unsaved changes in " + (System.IO.Path.GetFileName(session.Path) ?? "Untitled") + "?");
    private async Task NewDocument()
    {
        var result = await Dialogs.NewCloth(this); if (result == null) return;
        StopSimulation(); ExitActor(); Session.New(result.Value.Name, result.Value.Format); Refresh(true); ShowEditor(); _status.Text = "New unbound cloth draft.";
    }
    private async Task OpenDocument()
    {
        if (!await CanReplace()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open physics", AllowMultiple = false,
            FileTypeFilter = PhysicsFileTypes });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) Try(() => LoadPath(path));
    }
    private async Task SaveDocument(bool saveAs = false)
    {
        if (!_save.IsEnabled) return;
        if (!saveAs && Session.Path is { } existing) { Session.Save(existing); Refresh(); _status.Text = $"Saved {System.IO.Path.GetFileName(existing)}"; return; }
        var extension = Session.Document.CurrentExtension;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save physics", SuggestedFileName = System.IO.Path.GetFileName(Session.Path) ?? "NewCloth" + extension,
            DefaultExtension = extension.TrimStart('.'), ShowOverwritePrompt = true, FileTypeChoices = new[] { new FilePickerFileType("Physics") { Patterns = new[] { "*" + extension } } } });
        if (file?.TryGetLocalPath() is { } path) { Session.Save(path); Refresh(); _status.Text = $"Saved {System.IO.Path.GetFileName(path)}"; }
    }
    private async Task ShowSettings()
    {
        var dark = new CheckBox { Content = "Dark theme", IsChecked = Application.Current?.RequestedThemeVariant != Avalonia.Styling.ThemeVariant.Light };
        dark.IsCheckedChanged += (_, _) => Application.Current!.RequestedThemeVariant = dark.IsChecked == true ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        var format = new ComboBox { ItemsSource = new[] { "Wii U HKCL", "Switch HKCL" }, SelectedIndex = Session.Platform == HkclPlatform.WiiU ? 0 : 1, IsEnabled = !Session.Document.IsBphcl };
        format.SelectionChanged += (_, _) => Session.Platform = format.SelectedIndex == 0 ? HkclPlatform.WiiU : HkclPlatform.Switch;
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24) }; panel.Children.Add(dark); panel.Children.Add(new TextBlock { Text = "HKCL save platform" }); panel.Children.Add(format);
        var fps = new ComboBox { ItemsSource = new[] { "30 fps (half speed)", "60 fps (normal speed)" }, SelectedIndex = _animationFps == 30 ? 0 : 1 };
        fps.SelectionChanged += (_, _) => _animationFps = fps.SelectedIndex == 0 ? 30 : 60;
        panel.Children.Add(new TextBlock { Text = "Animation playback" }); panel.Children.Add(fps);
        await Dialogs.Show(this, "Settings", panel);
    }

    public async Task LoadModelPathAsync(string path)
    {
        var request = ++_modelRequest; _modelLoading = true; UpdateModelActions();
        _status.Text = "Loading " + System.IO.Path.GetFileName(path) + "...";
        try
        {
            var model = await Task.Run(() => PortableModelImporter.Load(path));
            if (request != _modelRequest) return;
            ExitActor();
            ShowEditor();
            _viewport.SetModel(model);
            _status.Text = $"{System.IO.Path.GetFileName(path)}: {model.Meshes.Count} meshes, {model.TriangleCount:N0} triangles, {model.EmbeddedTextureCount + model.ExternalTextureCount} textures."
                + (model.Warnings.Count == 0 ? "" : " " + string.Join(" ", model.Warnings.Take(3)));
        }
        finally { if (request == _modelRequest) { _modelLoading = false; UpdateModelActions(); } }
    }
    private void UpdateModelActions()
    {
        _loadModel.IsEnabled = !_modelLoading; _clearModel.IsEnabled = !_modelLoading && _viewport.Model != null;
        _meshes.IsEnabled = !_modelLoading && _viewport.Model != null;
    }
    private async Task OpenModel()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Load model", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Models") { Patterns = PortableModelImporter.Extensions.Select(e => "*" + e).ToArray() } } });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await LoadModelPathAsync(path);
    }
    private async Task ShowMeshes()
    {
        if (_viewport.Model is not { } model) return;
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        for (int i = 0; i < model.Meshes.Count; i++)
        {
            var index = i;
            var check = new CheckBox { Content = new TextBlock { Text = model.Meshes[i].Name, TextWrapping = TextWrapping.Wrap }, IsChecked = !_viewport.HiddenMeshes.Contains(i) };
            check.IsCheckedChanged += (_, _) => _viewport.SetMeshVisible(index, check.IsChecked == true); panel.Children.Add(check);
        }
        var content = new StackPanel(); content.Children.Add(new ScrollViewer { Content = panel, MaxHeight = 500 });
        await Dialogs.Show(this, "Meshes", content);
    }
}
