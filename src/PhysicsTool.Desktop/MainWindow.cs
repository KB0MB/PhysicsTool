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
    private readonly Button _swap, _file;
    private readonly MenuItem _save, _saveAs, _export;
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
    private readonly CheckBox _hideToggle, _collidersToggle;
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
    private readonly Button _simulate, _undo, _redo, _add, _delete, _constraint, _removeConstraint;
    private readonly List<Control> _editControls = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1d / 60) };
    private readonly Stopwatch _clock = new();
    private HkclPreviewSimulator? _solver;
    private readonly PreviewStepClock _previewStepClock = new();
    public PreviewOptions SimulationOptions { get; private set; } = new();
    private readonly Button _massScale, _clothSettings, _options, _editorUndo, _editorRedo, _attach;
    private double _lastTick;
    private bool _refreshing, _closingApproved;
    private HkclService? _selectionDocument;
    private int _selectionCloth = -1, _selectionMode = -1;
    private readonly CheckBox _wind = new() { Content = "Wind" }, _follow = new() { Content = "Particles follow bones", IsChecked = true };
    private sealed record Item(int Index, string Label, object Data) { public override string ToString() => Label; }
    private sealed record Relation(ParticleRelationshipRow Data) { public override string ToString() => $"{Data.Name}  {Data.Particles}"; }

    public MainWindow(string? file = null, bool graphics = true)
    {
        Title = "PhysicsTool"; Width = 1440; Height = 900; MinWidth = 980; MinHeight = 640;
        AnimationPlaybackFps = DesktopPreferences.Load().AnimationFps;
        _mode.Styles.Add(new Style(s => s.OfType<TabStripItem>())
        {
            Setters = { new Setter(TemplatedControl.FontSizeProperty, 14d), new Setter(TemplatedControl.PaddingProperty, new Thickness(12, 8)) }
        });
        _cloths.ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        _items.ItemTemplate = new FuncDataTemplate<Item>((item, _) => {
            var label = new TextBlock { Text = item?.Label, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(label, item?.Label); return label;
        });
        _relationships.ItemTemplate = new FuncDataTemplate<Relation>((item, _) => new TextBlock { Text = item?.ToString(), TextWrapping = TextWrapping.Wrap });
        var shell = new DockPanel();
        var topbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 6) };
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        using (var logo = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PhysicsTool/Assets/PhysicsTool.png")))
            toolbar.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(logo), Width = 32, Height = 32, Margin = new Thickness(6, 3, 14, 3) });
        toolbar.Children.Add(new TextBlock { Text = "PhysicsTool", FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        Add(toolbar, "New", async () => { if (await CanReplace()) await NewDocument(); });
        Add(toolbar, "Open Physics", async () => await OpenDocument());
        Add(toolbar, "Open Actor", async () => await OpenActorBrowser());
        Add(toolbar, "Collision", async () => { PauseEditorAnimation(); _actorPlaying = false; StopSimulation(); await new CollisionWindow(graphics, _actorScene?.Resources.SourcePath, _actorScene?.Resources.SourceEntry).ShowDialog(this); });
        _swap = _overview.SwapButton;
        _save = MenuCommand("Save", () => SaveDocument()); _save.Name = "SavePhysics";
        _saveAs = MenuCommand("Save as...", () => SaveDocument(true));
        _export = new MenuItem { Header = "Export", Name = "ExportPhysics" };
        ConfigureConversion();
        _file = Add(toolbar, "File", OpenFileMenu); _file.Name = "FileMenu";
        _file.ContextMenu = new ContextMenu { ItemsSource = new object[] { _save, _saveAs, new Separator(), _export, _convert } };
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
        _clearModel = Add(modelbar, "Clear model", () => { _modelRequest++; PauseEditorAnimation(); StopSimulation(); ExitActor(); _importedModel = null; ClearEditorAnimation(); _viewport.SetModel(null); UpdateModelActions(); });
        _meshes = Add(modelbar, "Meshes", async () => await ShowMeshes());
        _importAnimation = Add(modelbar, "Import animation", OpenEditorAnimation);
        DockPanel.SetDock(modelbar, Dock.Top); center.Children.Add(modelbar);
        var viewbar = new WrapPanel { Margin = new Thickness(5) };
        _simulate = Add(viewbar, "Run simulation", ToggleSimulation);
        ToolTip.SetShowOnDisabled(_simulate, true);
        _wind.Margin = new Thickness(10, 4); viewbar.Children.Add(_wind);
        _options = Add(viewbar, "Options", async () => await ShowPreviewOptions());
        var emission = new CheckBox { Name = "ViewportEmission", Content = "Emission", IsChecked = false, Margin = new Thickness(10, 4) };
        emission.IsCheckedChanged += (_, _) => { _viewport.ShowEmission = emission.IsChecked == true; ModelSurface?.RequestNextFrameRendering(); };
        viewbar.Children.Add(emission);
        var hide = _hideToggle = new CheckBox { Content = "Hide cloth", Margin = new Thickness(10, 4) };
        hide.IsCheckedChanged += (_, _) => { _viewport.ShowPhysics = hide.IsChecked != true; _viewport.InvalidateVisual(); }; viewbar.Children.Add(hide);
        var bones = _bonesToggle = new CheckBox { Content = "Bones", IsChecked = true, Margin = new Thickness(10, 4) }; bones.IsCheckedChanged += (_, _) => { _viewport.ShowBones = bones.IsChecked == true; _viewport.InvalidateVisual(); }; viewbar.Children.Add(bones);
        var colliders = _collidersToggle = new CheckBox { Content = "Colliders", IsChecked = true, Margin = new Thickness(10, 4) }; colliders.IsCheckedChanged += (_, _) => { _viewport.ShowColliders = colliders.IsChecked == true; _viewport.InvalidateVisual(); }; viewbar.Children.Add(colliders);
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
        var values = new Grid { ColumnDefinitions = new ColumnDefinitions("180,5,*") };
        values.ColumnDefinitions[0].MinWidth = 120; values.ColumnDefinitions[2].MinWidth = 160;
        var itemHost = new DockPanel();
        ConfigureRagdollSkeletonPicker(itemHost);
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
        DockPanel.SetDock(_relationshipActions, Dock.Bottom); _relationshipPane.Children.Add(_relationshipActions);
        var relationLayout = new Grid { ColumnDefinitions = new ColumnDefinitions("180,5,*") };
        relationLayout.ColumnDefinitions[0].MinWidth = 100; relationLayout.ColumnDefinitions[2].MinWidth = 160;
        relationLayout.Children.Add(_relationships);
        var relationSplit = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(relationSplit, 1); relationLayout.Children.Add(relationSplit);
        var relationScroll = new ScrollViewer { Content = _relationshipDetails }; Grid.SetColumn(relationScroll, 2); relationLayout.Children.Add(relationScroll);
        _relationshipPane.Children.Add(relationLayout);
        Grid.SetRow(_relationshipPane, 5); right.Children.Add(_relationshipPane);
        Grid.SetColumn(right, 2); workspace.Children.Add(right);
        var editorActions = _editorActions = new WrapPanel { Margin = new Thickness(5, 0, 5, 5) };
        _editorUndo = ViewportTools.Icon("Undo2", "Undo (Ctrl+Z)", () => Try(() => { Session.Undo(); Refresh(); })); editorActions.Children.Add(_editorUndo);
        _editorRedo = ViewportTools.Icon("Redo2", "Redo", () => Try(() => { Session.Redo(); Refresh(); })); editorActions.Children.Add(_editorRedo);
        _massScale = Add(editorActions, "Mass scale", async () => await ShowMassScale());
        _clothSettings = Add(editorActions, "Cloth settings", async () => await ShowClothSettings());
        _attach = Add(editorActions, "Attach to Bone", AttachSelectionToBone);
        ConfigureHelperEditor(editorActions);
        ConfigureSelectionTools(editorActions);
        Grid.SetRow(editorActions, 1); Grid.SetColumnSpan(editorActions, 3); workspace.Children.Add(editorActions);
        _body.Content = _overview; shell.Children.Add(_body); Content = shell;
        _overview.RemoveRequested += async () => await RunCommand(RemoveCloth);
        _overview.DuplicateRequested += () => Try(() => { Session.DuplicateSelected(); Refresh(true); _status.Text = "Duplicated cloth."; });
        _overview.RenameRequested += async () => await RunCommand(RenameCloth);
        _overview.MergeRequested += async () => await RunCommand(MergeDonor);
        _overview.ClearDonorRequested += async () => await RunCommand(ClearDonor);
        _overview.OpenDonorRequested += async () => await RunCommand(OpenDonor);
        _overview.SwapRequested += () => Try(SwapFiles);
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
        _relationships.SelectionChanged += (_, _) => { ShowRelationshipDetails(); UpdateActions(); };
        _viewport.SelectionRequested += (indices, operation) =>
        {
            if (_timer.IsEnabled || IsActorMode) return;
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
        ConfigureTransforms();
        _viewport.LostFocus += (_, _) => _viewport.FinishTransform(false);
        _timer.Tick += (_, _) =>
        {
            if (_solver == null && _ragdollSolver == null && !_editorPlaying) return;
            try
            {
                var now = _clock.Elapsed.TotalSeconds;
                var steps = _previewStepClock.Advance(now - _lastTick, SimulationOptions.PlaybackSpeed); _lastTick = now;
                var wasPlaying = _editorPlaying;
                for (var step = 0; step < steps; step++) AdvanceEditorPreview((float)PreviewStepClock.FixedStep);
                UpdateEditorPlayback(); if (wasPlaying != _editorPlaying) UpdateActions();
            }
            catch (Exception ex) { _editorPlaying = false; _timer.Stop(); _solver = null; _ragdollSolver = null; SelectEditorAnimation(-1); UpdateActions(); _status.Text = UserFacingExceptionFormatter.Format(ex); }
        };
        KeyDown += async (_, e) =>
        {
            var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (!e.Handled && command && e.Key == Key.S && _save.IsEnabled)
            { await RunCommand(() => SaveDocument(e.KeyModifiers.HasFlag(KeyModifiers.Shift))); e.Handled = true; return; }
            if (e.Handled || e.Source is TextBox or NumericUpDown) return;
            if (e.Key == Key.F1 && !command) { new DocumentationWindow().Show(this); e.Handled = true; return; }
            if (command && e.Key == Key.O) { await RunCommand(OpenDocument); e.Handled = true; return; }
            if (IsActorMode)
            {
                if (e.Key == Key.Space && _viewport.IsFocused) { ToggleActorPlayback(); e.Handled = true; }
                return;
            }
            if (command && e.Key == Key.Z && !_timer.IsEnabled)
            { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Session.Redo(); else Session.Undo(); Refresh(); e.Handled = true; return; }
            if (command && e.Key == Key.Y && !_timer.IsEnabled) { Session.Redo(); Refresh(); e.Handled = true; return; }
            if (!IsEditorVisible)
            {
                if (e.Key == Key.M && !command && DonorSession.HasSelectedEntry) { await RunCommand(MergeDonor); e.Handled = true; }
                return;
            }
            if (command && (_items.IsFocused || _viewport.IsFocused) && e.Key is Key.C or Key.V)
            {
                Try(() => { if (e.Key == Key.C) CopySelection(); else PasteSelection(e.KeyModifiers.HasFlag(KeyModifiers.Shift)); });
                e.Handled = true; return;
            }
            if (e.Key == Key.Space && _viewport.IsFocused) { if (EditorClip != null) ToggleEditorPlayback(); else ToggleSimulation(); e.Handled = true; }
            else if (!command && !_timer.IsEnabled && e.Key == Key.F && _mode.SelectedIndex == 0) { await AddConstraint(); e.Handled = true; }
            else if (!_timer.IsEnabled && e.Key == Key.Delete && (_items.IsFocused || _viewport.IsFocused)) { await DeleteItems(); e.Handled = true; }
        };
        Closing += async (_, e) => { if (_conversionBusy) { e.Cancel = true; return; } if (_closingApproved) return; if (Session.IsDirty || DonorSession.IsDirty) { e.Cancel = true; if (await Dialogs.Confirm(this, "Unsaved changes", "Discard unsaved changes in the current and donor files and close?")) { _closingApproved = true; Close(); } } else StopSimulation(); };
        Closed += (_, _) => { _modelRequest++; _animationRequest++; _editorPlaying = false; _actorLoad?.Cancel(); _actorTimer.Stop(); _actorScene?.Dispose(); _actorScene = null; StopSimulation(); };
        Opened += (_, _) => { if (file != null) Try(() => LoadPath(file)); };
        ConfigurePhysicsFileDrop();
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
        PauseEditorAnimation(); StopSimulation(); ExitActor(); _overview.Update(Session, DonorSession); _body.Content = _overview; _navigation.Content = "Editor"; UpdateActions();
    }
    public void LoadDonorPath(string path) { DonorSession.Load(path); ShowOverview(); _overview.ShowDonorBones(); _status.Text = "Donor: " + System.IO.Path.GetFileName(path); }
    private async Task OpenDonor()
    {
        if (!Session.Document.HasDocument) return;
        if (!await CanReplace(DonorSession)) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open donor physics", AllowMultiple = false,
            FileTypeFilter = PhysicsFileTypes });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) LoadDonorPath(path);
    }
    public void LoadPath(string path) { PauseEditorAnimation(); StopSimulation(); Session.Load(path); ExitActor(); Refresh(true); _status.Text = $"Opened {System.IO.Path.GetFileName(path)}"; }
    public void Refresh(bool fit = false)
    {
        if (IsActorMode) { UpdateActions(); return; }
        var sameSelectionContext = ReferenceEquals(_selectionDocument, Session.Document)
            && _selectionCloth == Session.ClothIndex && _selectionMode == _mode.SelectedIndex;
        var selected = sameSelectionContext ? _items.SelectedItems!.Cast<Item>().Select(i => i.Index).ToHashSet() : new HashSet<int>();
        var active = sameSelectionContext ? (_items.SelectedItem as Item)?.Index : null;
        if (!sameSelectionContext) _relationships.SelectedItem = null;
        _refreshing = true;
        try
        {
            UpdateRagdollTabs();
            _cloths.ItemsSource = Enumerable.Range(0, Session.Document.GetClothSummaries().Count).Select(i => Session.Document.IsHelperBoneSidecar
                ? $"{i}: {Session.Document.GetSidecarDriverGroup(i)?.DisplayName}" : Session.Document.GetClothName(i)).ToArray(); _cloths.SelectedIndex = Session.ClothIndex;
            if (Session.Document.IsHelperBoneSidecar) _mode.SelectedIndex = 1;
            _items.ItemsSource = Array.Empty<Item>(); _viewport.Data = new();
            if (Session.Document.HasDocument && _cloths.SelectedIndex >= 0)
            {
                var doc = Session.Document; var cloth = Session.ClothIndex;
                IEnumerable<Item> rows = _mode.SelectedIndex switch
                {
                    0 when doc.IsHkrg => doc.GetHkrgConstraintRows().Select(c => new Item(c.Index, $"{c.Index}: {c.Name} ({c.ConstraintType})", c)),
                    1 => (EditingRagdollModel ? doc.GetHkrgModelBoneRows() : doc.GetBoneRows(cloth)).Select(b => new Item(b.Index + (EditingRagdollModel ? 100000 : 0), doc.IsHelperBoneSidecar ? $"[{HelperRole(doc.GetSidecarDriverGroup(cloth), b.Index)}] {b.Index}: {b.Name}" : $"{b.Index}: {b.Name}", b)),
                    2 => doc.GetColliderRows(cloth).Select(c => new Item(c.Index, $"{c.Index}: {c.Name} ({c.ShapeType})", c)),
                    _ => doc.GetParticleRows(cloth).Select(p => new Item(p.Index, $"{p.Index}  {(p.Fixed ? "Fixed" : "Free")}", p))
                };
                var array = rows.ToArray(); _items.ItemsSource = array;
                var retained = array.Where(i => selected.Contains(i.Index)).ToArray();
                if (retained.Length > 0)
                {
                    _items.SelectedItem = retained.FirstOrDefault(i => i.Index == active) ?? retained[0];
                    foreach (var item in retained) if (!_items.SelectedItems!.Contains(item)) _items.SelectedItems.Add(item);
                }
                else if (!sameSelectionContext || selected.Count > 0) _items.SelectedItem = array.FirstOrDefault();
                _viewport.Data = doc.GetParticlePreview(cloth);
            }
            _selectionDocument = Session.Document; _selectionCloth = Session.ClothIndex; _selectionMode = _mode.SelectedIndex;
            Title = $"PhysicsTool - {(Session.Path == null ? "Untitled" : System.IO.Path.GetFileName(Session.Path))}{(Session.IsDirty ? " *" : "")}";
        }
        finally { _refreshing = false; }
        ShowDetails(); SelectViewport(); UpdateImportedModelPose(true);
        if (fit) { if (IsEditorVisible) _viewport.Fit(); else _fitPending = true; }
        if (!IsEditorVisible) _overview.Update(Session, DonorSession);
        _viewport.InvalidateVisual(); UpdateActions();
    }
    private void SelectViewport()
    {
        _viewport.Selected.Clear(); if (_mode.SelectedIndex == 0 && !Session.Document.IsHkrg) foreach (var item in _items.SelectedItems?.Cast<Item>() ?? []) _viewport.Selected.Add(item.Index);
        _viewport.SelectedBones.Clear(); _viewport.SelectedColliders.Clear();
        _viewport.SelectedRagdollConstraints.Clear();
        foreach (var item in _items.SelectedItems?.Cast<Item>() ?? [])
        {
            if (item.Data is HkrgConstraintEditRow joint)
            {
                _viewport.SelectedRagdollConstraints.Add(joint.Index);
                if (joint.BodyA >= 0) _viewport.SelectedColliders.Add(joint.BodyA);
                if (joint.BodyB >= 0) _viewport.SelectedColliders.Add(joint.BodyB);
            }
            if (_mode.SelectedIndex == 1) _viewport.SelectedBones.Add(item.Index);
            if (_mode.SelectedIndex == 2) _viewport.SelectedColliders.Add(item.Index);
        }
        _viewport.InvalidateVisual();
    }
    private void ShowDetails()
    {
        var selectedRelationship = (_relationships.SelectedItem as Relation)?.Data;
        _details.Children.Clear(); _relationships.ItemsSource = Array.Empty<Relation>();
        _relationshipDetails.Children.Clear();
        if (_items.SelectedItem is not Item item) return;
        _details.Children.Add(new TextBlock { Text = item.Label, FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (Session.Document.IsHelperBoneSidecar && item.Data is BoneEditRow helper) { ShowHelperDetails(helper); return; }
        if (Session.Document.IsHkrg) { ShowRagdollDetails(item.Data); return; }
        if (item.Data is BoneEditRow bone) { ShowBoneDetails(bone); return; }
        if (item.Data is ColliderEditRow collider) { ShowColliderDetails(collider); return; }
        if (item.Data is ParticleEditRow particle)
        {
            ShowParticleDetails(particle);
            var relations = Session.Document.GetParticleRelationships(Session.ClothIndex, item.Index).Select(r => new Relation(r)).ToArray();
            _relationships.ItemsSource = relations;
            if (selectedRelationship != null) _relationships.SelectedItem = relations.FirstOrDefault(r => RelationshipEdits.Same(r.Data, selectedRelationship));
        }
    }
    private void Edit(Action<HkclService, int> action)
    {
        if (IsActorMode) throw new InvalidOperationException("Actor view is read-only.");
        if (_timer.IsEnabled) throw new InvalidOperationException("Pause animation and stop simulation before editing.");
        try { Session.Edit(action); } finally { Refresh(); }
        _status.Text = "Updated document.";
    }
    private void UpdateActions()
    {
        var ready = Session.Document.HasDocument && _cloths.SelectedIndex >= 0;
        var editing = ready && !_timer.IsEnabled && !Session.Document.IsHelperBoneSidecar && !Session.Document.IsHkrg;
        _relationshipDetails.IsEnabled = editing && !IsActorMode;
        foreach (var c in _editControls) c.IsEnabled = !_timer.IsEnabled;
        if (Session.Document.IsHkrg) _details.IsEnabled = !_timer.IsEnabled && !_editorPlaying && !IsActorMode;
        _mode.IsEnabled = !_timer.IsEnabled && !Session.Document.IsHelperBoneSidecar;
        _ragdollSkeleton.IsEnabled = !_timer.IsEnabled && !_editorPlaying;
        _save.IsEnabled = Session.Document.HasDocument && !_timer.IsEnabled;
        _saveAs.IsEnabled = _export.IsEnabled = _save.IsEnabled;
        UpdateConversion();
        _swap.IsEnabled = !IsEditorVisible && Session.Document.HasDocument && DonorSession.Document.HasDocument;
        _undo.IsEnabled = !_timer.IsEnabled && Session.CanUndo; _redo.IsEnabled = !_timer.IsEnabled && Session.CanRedo;
        _editorUndo.IsEnabled = _undo.IsEnabled; _editorRedo.IsEnabled = _redo.IsEnabled;
        _editorUndo.IsVisible = _editorRedo.IsVisible = IsEditorVisible;
        _undo.IsVisible = _redo.IsVisible = !IsEditorVisible;
        _massScale.IsEnabled = editing && _mode.SelectedIndex == 0 && Session.CanScaleMass;
        _clothSettings.IsEnabled = !_timer.IsEnabled && Session.CanEditClothSettings;
        _options.IsEnabled = _solver != null || Session.CanSimulate || ready && Session.Document.IsHkrg;
        _wind.IsEnabled = _options.IsEnabled;
        ToolTip.SetTip(_massScale, "Scale selected dynamic particles, or all dynamic particles when none are selected. Fixed particles are unchanged.");
        ToolTip.SetTip(_clothSettings, Session.Document.IsBphcl ? "Native BPHCL cloth settings are currently read-only." : "Edit saved gravity, damping, collision tolerance, and motion transfer.");
        ToolTip.SetTip(_options, "Preview-only wind, gravity, playback speed, and solver passes.");
        _add.IsEnabled = editing && (_mode.SelectedIndex == 0 || !Session.Document.IsBphcl);
        _add.Content = _mode.SelectedIndex switch { 1 => "Add Bone", 2 => "Add Collider", _ => "Add Particle" };
        _follow.IsEnabled = editing;
        _attach.IsEnabled = editing && !IsActorMode && _items.SelectedItems?.Count > 0 && Session.Document.GetBoneRows(Session.ClothIndex).Count > 0
            && (_mode.SelectedIndex != 2 || !Session.Document.IsBphcl);
        ToolTip.SetTip(_attach, _mode.SelectedIndex switch {
            0 => "Snap selected particles to a bone using the active particle as a fixed anchor. Existing skin weights are retained.",
            1 => "Snap the active bone to another bone while preserving group spacing and the existing hierarchy.",
            _ => Session.Document.IsBphcl ? "Native BPHCL collider rebinding is not available yet." : "Snap and bind selected colliders to a bone, preserving group spacing." });
        var relationshipsVisible = _mode.SelectedIndex == 0 && !Session.Document.IsHelperBoneSidecar && !Session.Document.IsHkrg;
        if (_relationshipPane.IsVisible != relationshipsVisible)
        {
            _relationshipPane.IsVisible = _relationshipSplit.IsVisible = relationshipsVisible;
            _inspector.RowDefinitions[4].Height = new GridLength(relationshipsVisible ? 5 : 0);
            _inspector.RowDefinitions[5].Height = new GridLength(relationshipsVisible ? 200 : 0);
        }
        _delete.IsEnabled = editing && _items.SelectedItem != null && (_mode.SelectedIndex != 1 || !Session.Document.IsBphcl);
        ToolTip.SetTip(_delete, _mode.SelectedIndex == 1 && Session.Document.IsBphcl
            ? "Native BPHCL bone deletion is not supported yet; packed skin and transform trackers need rebuilding."
            : "Delete selected items. Bones used by skinning, constraints or colliders must be unbound first.");
        var selectedParticleCount = _mode.SelectedIndex == 0 ? _items.SelectedItems?.Count ?? 0 : 0;
        _constraint.IsEnabled = editing && selectedParticleCount is 2 or 3;
        ToolTip.SetTip(_constraint, selectedParticleCount == 3
            ? "Create a triangle and its edge links from the three selected particles (F)."
            : "Select exactly two particles for a constraint, or three for a triangle (F).");
        _removeConstraint.IsEnabled = editing && _relationships.SelectedItem is Relation r && Session.Document.CanDeleteParticleRelationship(Session.ClothIndex, r.Data);
        _simulate.IsEnabled = _solver != null || ready && Session.CanSimulate; _simulate.Content = _solver != null ? "Stop simulation" : "Run simulation";
        if (_solver == null && Session.Document.IsBphcl) _simulate.Content = "Run TotK preview";
        if (Session.Document.IsHkrg && !IsActorMode)
        {
            _simulate.IsEnabled = ready;
            _simulate.Content = _ragdollSolver == null ? "Run stress preview" : "Stop preview";
        }
        ToolTip.SetTip(_simulate, Session.Document.IsBphcl
            ? string.Join(" ", Session.Document.GetTotkSimulationSupport(Session.ClothIndex).Blockers.Concat(
                Session.Document.GetTotkSimulationSupport(Session.ClothIndex).Warnings)) : null);
        if (Session.Document.IsHkrg) ToolTip.SetTip(_simulate, "Approximate glide/stress preview from the original tool. It does not reproduce Havok or change saved poses.");
        _viewport.CanSelect = editing || ready && !_timer.IsEnabled && !_editorPlaying && Session.Document.IsHkrg && _mode.SelectedIndex is 1 or 2;
        _viewport.SelectionKind = (PhysicsSelectionKind)_mode.SelectedIndex;
        _hideToggle.Content = Session.Document.IsHkrg && !IsActorMode ? "Hide ragdoll" : "Hide cloth";
        _collidersToggle.Content = Session.Document.IsHkrg && !IsActorMode ? "Rigid bodies" : "Colliders";
        _loadModel.IsVisible = _clearModel.IsVisible = !IsActorMode;
        _importAnimation.IsVisible = !IsActorMode;
        if (IsActorMode)
        {
            _save.IsEnabled = _saveAs.IsEnabled = _export.IsEnabled = _undo.IsEnabled = _redo.IsEnabled = false;
            _add.IsEnabled = _delete.IsEnabled = _constraint.IsEnabled = _removeConstraint.IsEnabled = false;
            _viewport.CanSelect = false;
            _simulate.IsEnabled = _actorScene!.HasCloth;
            _simulate.Content = _actorSimulating ? "Stop simulation" : _actorScene.Resources.IsTotk ? "Run TotK preview" : "Run simulation";
            ToolTip.SetTip(_simulate, _actorScene.Resources.IsTotk
                ? "Experimental native TotK cloth preview. Game cloth reactions, world collision and state transitions are not reproduced."
                : null);
            _wind.IsEnabled = _options.IsEnabled = _actorScene.HasCloth;
        }
        UpdateHelperActions();
        UpdateSelectionTools();
    }
    private void AddItem() => Edit((doc, cloth) => { if (_mode.SelectedIndex == 0) doc.AddParticle(cloth); else if (_mode.SelectedIndex == 1) doc.AddBone(cloth); else doc.AddCollider(cloth); });
    private async Task DeleteItems()
    {
        if (!_delete.IsEnabled || Session.Document.IsHelperBoneSidecar || Session.Document.IsHkrg || IsActorMode) return;
        if (_timer.IsEnabled || _items.SelectedItem is not Item selected) return;
        var indices = _items.SelectedItems!.Cast<Item>().Select(i => i.Index).ToArray();
        var mode = _mode.SelectedIndex;
        if (!await Dialogs.Confirm(this, "Delete selection", mode == 1
            ? "Delete selected bones? Surviving children keep their world positions. Bones still used by cloth bindings cannot be removed."
            : "Delete the selected items and their dependent relationships?")) return;
        try
        {
            Edit((doc, cloth) => {
                if (mode == 0) doc.DeleteParticles(cloth, indices);
                else if (mode == 1) doc.DeleteBones(cloth, indices);
                else foreach (var index in indices.OrderDescending()) doc.DeleteCollider(cloth, index);
            });
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
            panel.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap });
            await Dialogs.Show(this, "Cannot delete selection", panel);
        }
    }
    private async Task AddConstraint()
    {
        if (!_constraint.IsEnabled || _timer.IsEnabled || _mode.SelectedIndex != 0) return;
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
        if (_solver != null) { StopSimulation(); return; }
        if (Session.Document.IsHkrg) { ToggleRagdollPreview(); return; }
        if (!Session.Document.HasDocument || !Session.CanSimulate) return;
        try { _solver = Session.CreateSimulator(); }
        catch (Exception ex) { _status.Text = UserFacingExceptionFormatter.Format(ex); return; }
        SimulationOptions.ApplyTo(_solver); _previewStepClock.Reset();
        if (Session.Document.IsBphcl) _status.Text = "Experimental TotK cloth preview. World collision, game actions, and state transitions are not yet reproduced.";
        if (EditorClip != null) PoseEditorAnimation(0, true, false);
        _clock.Restart(); _lastTick = 0; _timer.Start(); UpdateActions();
    }
    public void StopSimulation() { _actorSimulating = false; if (_actorScene != null) { PoseActor(true, 0); UpdateActorPlayback(); } _timer.Stop(); _previewStepClock.Reset(); _solver = null; _ragdollSolver = null; _viewport.Simulated = null; _viewport.SimulatedBones = null; _viewport.SimulatedColliders = null; UpdateImportedModelPose(); UpdateEditorPlayback(); _viewport.InvalidateVisual(); UpdateActions(); }
    private Task<bool> CanReplace() => CanReplace(Session);
    private async Task<bool> CanReplace(EditorSession session) => !session.IsDirty || await Dialogs.Confirm(this, "Unsaved changes", "Discard unsaved changes in " + (System.IO.Path.GetFileName(session.Path) ?? "Untitled") + "?");
    private async Task NewDocument()
    {
        var result = await Dialogs.NewCloth(this); if (result == null) return;
        PauseEditorAnimation(); StopSimulation(); ExitActor(); Session.New(result.Value.Name, result.Value.Format); Refresh(true); ShowEditor(); _status.Text = "New unbound cloth draft.";
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
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
        void Persist()
        {
            try { new DesktopPreferences(UiLanguage.Current, Application.Current?.RequestedThemeVariant != ThemeVariant.Light, _animationFps).Save(); error.Text = ""; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            { error.Text = "Settings could not be saved. Check that the application folder is writable."; }
        }
        var dark = new CheckBox { Content = "Dark theme", IsChecked = Application.Current?.RequestedThemeVariant != Avalonia.Styling.ThemeVariant.Light };
        dark.IsCheckedChanged += (_, _) => { Application.Current!.RequestedThemeVariant = dark.IsChecked == true ? ThemeVariant.Dark : ThemeVariant.Light; Persist(); };
        var format = new ComboBox { ItemsSource = new[] { "Wii U HKCL", "Switch HKCL" }, SelectedIndex = Session.Platform == HkclPlatform.WiiU ? 0 : 1, IsEnabled = !Session.Document.IsBphcl };
        format.SelectionChanged += (_, _) => Session.Platform = format.SelectedIndex == 0 ? HkclPlatform.WiiU : HkclPlatform.Switch;
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24) }; panel.Children.Add(dark); panel.Children.Add(new TextBlock { Text = "HKCL save platform" }); panel.Children.Add(format);
        var fps = new ComboBox { ItemsSource = new[] { "30 fps (half speed)", "60 fps (normal speed)" }, SelectedIndex = _animationFps == 30 ? 0 : 1 };
        fps.SelectionChanged += (_, _) => { AnimationPlaybackFps = fps.SelectedIndex == 0 ? 30 : 60; Persist(); };
        panel.Children.Add(new TextBlock { Text = "Animation playback" }); panel.Children.Add(fps);
        var language = new ComboBox { Name = "InterfaceLanguage", ItemsSource = UiLanguage.Languages, SelectedItem = UiLanguage.Languages.Single(l => l.Code == UiLanguage.Current) };
        language.SelectionChanged += (_, _) => { if (language.SelectedItem is LanguageChoice choice) { UiLanguage.Set(choice.Code); Persist(); } };
        panel.Children.Add(new TextBlock { Text = "Interface language" }); panel.Children.Add(language); panel.Children.Add(error);
        await Dialogs.Show(this, "Settings", panel);
    }

    public async Task LoadModelPathAsync(string path)
    {
        RequireBfres(path);
        await LoadModelAsync(() => PortableModelImporter.LoadRig(path), path);
    }
    internal static void RequireBfres(string path)
    {
        if (System.IO.Path.GetExtension(path).ToLowerInvariant() is not (".bfres" or ".sbfres"))
            throw new NotSupportedException("Select a BFRES or SBFRES resource. Other mesh formats are available in Collision only.");
    }
    internal async Task LoadModelAsync(Func<ImportedModelRig> read, string path)
    {
        var request = ++_modelRequest; _modelLoading = true; UpdateModelActions();
        _status.Text = "Loading " + System.IO.Path.GetFileName(path) + "...";
        try
        {
            var rig = await Task.Run(read);
            if (request != _modelRequest) return;
            PauseEditorAnimation(); StopSimulation();
            ExitActor();
            var model = rig.Rest; _importedModel = rig;
            ClearEditorAnimation();
            ShowEditor();
            _viewport.SetModel(model);
            UpdateImportedModelPose();
            _status.Text = $"{System.IO.Path.GetFileName(path)}: {model.Meshes.Count} meshes, {model.TriangleCount:N0} triangles, {model.EmbeddedTextureCount + model.ExternalTextureCount} textures."
                + (model.Warnings.Count == 0 ? "" : " " + string.Join(" ", model.Warnings.Take(3)));
        }
        finally { if (request == _modelRequest) { _modelLoading = false; UpdateModelActions(); } }
    }
    private void UpdateModelActions()
    {
        _evaluateHelpers.IsEnabled = _importedModel != null && !_modelLoading;
        _loadModel.IsEnabled = !_modelLoading; _clearModel.IsEnabled = !_modelLoading && _viewport.Model != null;
        _meshes.IsEnabled = !_modelLoading && _viewport.Model != null;
        _importAnimation.IsEnabled = !_modelLoading && !_animationLoading && _importedModel != null && !IsActorMode;
    }
    private async Task OpenModel()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Load model", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("BFRES models") { Patterns = new[] { "*.bfres", "*.sbfres" } } } });
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
