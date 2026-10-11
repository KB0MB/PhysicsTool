using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;

namespace PhysicsTool.Desktop;

internal sealed class CollisionWindow : Window
{
    private readonly PhysicsViewport _view = new() { ShowPhysics = false, CanSelect = false };
    private readonly ComboBox _target = new() { Name = "CollisionTarget", ItemsSource = new[] { "BotW - Wii U actor (.hkrb)", "BotW - Switch actor (.hkrb)", "BotW - Wii U static (.hksc)", "TotK - Polytope (.bgyml)" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _coordinates = new() { ItemsSource = new[] { "Game / Y up", "Blender / Z up" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly NumericUpDown _scale = Number("CollisionScale", 1, .000001m);
    private readonly NumericUpDown[] _offset = [Number("CollisionX", 0), Number("CollisionY", 0), Number("CollisionZ", 0)];
    private readonly TextBox _bodyName = new() { Text = "RigidBody_0" };
    private readonly CheckBox _compress = new() { Content = "Yaz0 compression (.shksc)" };
    private readonly StackPanel _meshRows = new() { Spacing = 8 };
    private readonly TextBlock _source = new() { Text = "No source", TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _summary = new() { Text = "No collision loaded", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Name = "CollisionStatus", Text = "Ready", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 6) };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false, Height = 3 };
    private readonly Button _preview, _generate, _export;
    private readonly Control _inputs;
    private readonly StackPanel _bodyField;
    private readonly Button _import, _open;
    private IReadOnlyList<BotwCollisionMesh> _meshes = [];
    private CollisionPreview? _prepared;
    private CollisionSettings? _preparedSettings;
    private string _path = "";
    private bool _busy, _updating;
    internal PhysicsViewport Viewport => _view;
    internal ModelGlControl? ModelSurface { get; }
    internal bool Busy => _busy;

    internal CollisionWindow(bool graphics = true, string? actorPath = null, string? actorEntry = null)
    {
        Title = "Collision"; Width = 1180; Height = 780; MinWidth = 900; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var shell = new DockPanel();
        var bar = new WrapPanel { Margin = new Thickness(8) };
        _open = Button("Open actor / collision", async () => await Open(false)); bar.Children.Add(_open);
        _import = Button("Import mesh", async () => await Open(true)); bar.Children.Add(_import);
        if (actorPath != null && !actorPath.EndsWith(".pack.zs", StringComparison.OrdinalIgnoreCase))
            bar.Children.Add(Button("Current actor", async () => await LoadPathAsync(actorPath, false, actorEntry)));
        var wire = new CheckBox { Content = "Wireframe", Margin = new Thickness(12, 0, 0, 0) };
        wire.IsCheckedChanged += (_, _) => { _view.Wireframe = wire.IsChecked == true; _view.InvalidateVisual(); }; bar.Children.Add(wire);
        ToolTip.SetTip(_open, "BotW actor packs, HKRB/HKSC, or TotK polytope ShapeParam.");
        ToolTip.SetTip(_import, "Source geometry: FBX, DAE, OBJ, glTF, GLB or BFRES. This creates new collision, not a lossless actor-pack edit.");
        DockPanel.SetDock(bar, Dock.Top); shell.Children.Add(bar);
        var footer = new StackPanel(); footer.Children.Add(_progress); footer.Children.Add(_status); DockPanel.SetDock(footer, Dock.Bottom); shell.Children.Add(footer);
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,370") }; split.ColumnDefinitions[0].MinWidth = 400; split.ColumnDefinitions[2].MinWidth = 320;
        var scene = new Grid();
        if (graphics) { var gl = ModelSurface = new ModelGlControl(_view); gl.RenderFailed += _ => _status.Text = "GPU preview unavailable; showing wireframe."; scene.Children.Add(gl); }
        var camera = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        camera.Children.Add(ViewportTools.Icon("Maximize", "Frame collision (Home)", _view.Fit));
        camera.Children.Add(ViewportTools.Icon("ZoomIn", "Zoom in", () => _view.ZoomAt(1, new(_view.Bounds.Width / 2, _view.Bounds.Height / 2))));
        camera.Children.Add(ViewportTools.Icon("ZoomOut", "Zoom out", () => _view.ZoomAt(-1, new(_view.Bounds.Width / 2, _view.Bounds.Height / 2))));
        scene.Children.Add(_view); scene.Children.Add(camera); scene.Children.Add(new ViewportAxes(_view)); split.Children.Add(scene);
        var separator = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns }; Grid.SetColumn(separator, 1); split.Children.Add(separator);
        var right = new DockPanel { Margin = new Thickness(12) };
        var commands = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        commands.Children.Add(_summary);
        _preview = Button("Update preview", async () => await PrepareAsync()); _preview.Name = "CollisionPreview";
        _generate = Button("Generate collision...", async () => await Save(false)); _generate.Name = "CollisionGenerate";
        _export = Button("Export mesh...", async () => await Save(true)); _export.Name = "CollisionExport";
        commands.Children.Add(_preview); commands.Children.Add(_generate); commands.Children.Add(_export); DockPanel.SetDock(commands, Dock.Bottom); right.Children.Add(commands);
        var inputs = new DockPanel(); _inputs = inputs;
        var header = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 0, 12) }; header.Children.Add(_source); header.Children.Add(Field("Output", _target));
        DockPanel.SetDock(header, Dock.Top); inputs.Children.Add(header);
        var transform = new StackPanel { Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        transform.Children.Add(Field("Coordinates", _coordinates));
        ToolTip.SetTip(_coordinates, "Z up maps (X, Y, Z) to (X, Z, -Y). Leave imported game collision on Y up.");
        transform.Children.Add(Field("Scale", _scale)); ToolTip.SetTip(_scale, "Applied once to source coordinates before the offset. Preview and export use the same geometry.");
        var offset = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        for (var i = 0; i < 3; i++) { var f = Field(new[] { "X", "Y", "Z" }[i], _offset[i]); f.Margin = new Thickness(0, 0, i == 2 ? 0 : 6, 0); Grid.SetColumn(f, i); offset.Children.Add(f); }
        transform.Children.Add(Field("Offset", offset)); _bodyField = Field("Rigid body name", _bodyName); transform.Children.Add(_bodyField); transform.Children.Add(_compress);
        _meshRows.Margin = new Thickness(0, 8, 0, 0);
        inputs.Children.Add(new TabControl { Name = "CollisionOptions", ItemsSource = new[] {
            new TabItem { Header = "Meshes", FontSize = 14, Content = new ScrollViewer { Content = _meshRows } },
            new TabItem { Header = "Transform", FontSize = 14, Content = new ScrollViewer { Content = transform } } } });
        right.Children.Add(inputs); Grid.SetColumn(right, 2); split.Children.Add(right); shell.Children.Add(split); Content = shell;
        _target.SelectionChanged += (_, _) => { UpdateTarget(); InvalidatePreview(); PopulateMeshes(); };
        _coordinates.SelectionChanged += (_, _) => InvalidatePreview(); _scale.ValueChanged += (_, _) => InvalidatePreview();
        foreach (var number in _offset) number.ValueChanged += (_, _) => InvalidatePreview();
        _compress.IsCheckedChanged += (_, _) => InvalidatePreview(); _bodyName.TextChanged += (_, _) => InvalidatePreview();
        Closing += (_, e) => { if (_busy) e.Cancel = true; };
        UpdateTarget(); UpdateActions();
    }
    private static NumericUpDown Number(string name, decimal value, decimal minimum = -1000000) => new()
    { Name = name, Value = value, Minimum = minimum, Maximum = 1000000, Increment = .01m, FormatString = "0.######", ShowButtonSpinner = false, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static StackPanel Field(string title, Control control)
    { var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(new TextBlock { Text = title }); panel.Children.Add(control); return panel; }
    private Button Button(string title, Func<Task> action)
    { var b = new Button { Content = title, Margin = new Thickness(0, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Stretch }; b.Click += async (_, _) => { try { await action(); } catch (Exception ex) { Error(ex); } }; return b; }
    private void Error(Exception ex) => _status.Text = UserFacingExceptionFormatter.Format(ex);
    private void UpdateTarget()
    {
        _compress.IsVisible = _target.SelectedIndex == 2; _bodyField.IsVisible = _target.SelectedIndex is 0 or 1;
        ToolTip.SetTip(_target, _target.SelectedIndex == 3 ? "One convex hull per connected mesh component. Concave gaps may be filled; inspect the hull preview before generating. Material preset names must exist in your game resources." : "Standalone collision output. Actor-pack registration is not changed. Switch HKSC generation is not supported.");
    }
    private CollisionSettings Settings()
    {
        float Value(NumericUpDown n) => n.Value.HasValue ? (float)n.Value.Value : throw new InvalidOperationException("Complete all numeric fields.");
        return new((CollisionOutput)_target.SelectedIndex, Value(_scale), new(Value(_offset[0]), Value(_offset[1]), Value(_offset[2])), _coordinates.SelectedIndex == 1, _compress.IsChecked == true, _bodyName.Text ?? "");
    }
    private void InvalidatePreview()
    {
        if (_updating) return;
        _prepared = null; _preparedSettings = null;
        if (_meshes.Count > 0) { _summary.Text = "Preview out of date"; _status.Text = "Update preview to validate the changed collision."; }
        UpdateActions();
    }
    private void UpdateActions()
    {
        _open.IsEnabled = _import.IsEnabled = _inputs.IsEnabled = !_busy;
        _preview.IsEnabled = !_busy && _meshes.Any(m => m.Included);
        _generate.IsEnabled = _export.IsEnabled = !_busy && _prepared != null;
        _progress.IsVisible = _busy;
    }
    private async Task Work(string status, Func<Task> action)
    {
        if (_busy) return;
        _busy = true; _status.Text = status; UpdateActions();
        try { await action(); } catch (Exception ex) { Error(ex); } finally { _busy = false; UpdateActions(); }
    }
    private async Task Open(bool model)
    {
        var extensions = model ? PortableModelImporter.Extensions.Select(e => "*" + e).ToArray() : new[] { "*.sbactorpack", "*.hkrb", "*.hksc", "*.shksc", "*.bgyml", "*.yaml", "*.yml" };
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = model ? "Import collision source mesh" : "Open actor or collision", FileTypeFilter = [new(model ? "Source meshes" : "Collision resources") { Patterns = extensions }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await LoadPathAsync(path, model);
    }
    internal async Task LoadPathAsync(string path, bool model, string? actorEntry = null)
    {
        await Work("Loading collision source...", async () =>
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var polytope = extension is ".bgyml" or ".yaml" or ".yml";
            var meshes = await Task.Run(() => model ? ImportModel(path) : polytope ? TotkPolytopeImporter.Import(path) : BotwCollisionFileImporter.Import(path, actorEntry));
            var switchSource = !model && !polytope && await Task.Run(() => BotwCollisionFileImporter.DetectPlatform(path, actorEntry)) == BotwCollisionPlatform.Switch;
            _updating = true;
            try
            {
                _meshes = meshes; _path = path; _source.Text = Path.GetFileName(actorEntry ?? path); ToolTip.SetTip(_source, _source.Text);
                _scale.Value = 1; foreach (var value in _offset) value.Value = 0; _coordinates.SelectedIndex = 0;
                if (!model) _target.SelectedIndex = polytope ? 3 : switchSource ? 1 : extension is ".hksc" or ".shksc" ? 2 : 0;
                _compress.IsChecked = extension == ".shksc"; _bodyName.Text = Path.GetFileNameWithoutExtension(path);
                _prepared = null; _preparedSettings = null; PopulateMeshes(); UpdateTarget();
            }
            finally { _updating = false; }
            await PrepareCore();
            if (switchSource && extension is ".hksc" or ".shksc") _status.Text = "Switch HKSC preview loaded. Switch output is HKRB only; HKSC generation is not supported.";
        });
    }
    private static IReadOnlyList<BotwCollisionMesh> ImportModel(string path)
    {
        if (Path.GetExtension(path).ToLowerInvariant() is not (".bfres" or ".sbfres")) return BotwCollisionMeshImporter.Import(path);
        var model = PortableModelImporter.Load(path);
        return model.Meshes.Select(m => new BotwCollisionMesh { Name = m.Name, Indices = m.Indices.Select(i => checked((int)i)).ToArray(),
            Vertices = Enumerable.Range(0, m.Vertices.Length / 8).Select(i => new Vector3(m.Vertices[i * 8], m.Vertices[i * 8 + 1], m.Vertices[i * 8 + 2])).ToArray() }).ToArray();
    }
    private void PopulateMeshes()
    {
        _meshRows.Children.Clear();
        foreach (var mesh in _meshes)
        {
            var row = new StackPanel { Spacing = 4 };
            var check = new CheckBox { IsChecked = mesh.Included, Content = new TextBlock { Text = $"{mesh.Name} ({mesh.TriangleCount:N0} triangles)", TextWrapping = TextWrapping.Wrap } };
            check.IsCheckedChanged += (_, _) => { mesh.Included = check.IsChecked == true; InvalidatePreview(); }; row.Children.Add(check);
            if (_target.SelectedIndex == 3)
            {
                var material = new TextBox { Text = mesh.Material, Watermark = "Material preset" }; ToolTip.SetTip(material, "TotK material preset names, comma-separated. Names are preserved verbatim.");
                material.TextChanged += (_, _) => { mesh.Material = material.Text ?? ""; InvalidatePreview(); }; row.Children.Add(material);
            }
            else
            {
                var material = new ComboBox { ItemsSource = BotwCollisionMaterials.Names, SelectedItem = mesh.Material, HorizontalAlignment = HorizontalAlignment.Stretch };
                ToolTip.SetTip(material, "BotW surface material. New meshes default to Wood.");
                material.SelectionChanged += (_, _) => { if (material.SelectedItem is string value) { mesh.Material = value; InvalidatePreview(); } }; row.Children.Add(material);
            }
            _meshRows.Children.Add(row);
        }
    }
    internal Task PrepareAsync() => Work("Building collision preview...", PrepareCore);
    private async Task PrepareCore()
    {
        _prepared = null; _preparedSettings = null; _view.SetModel(null);
        var settings = Settings(); var source = CollisionWorkflow.Snapshot(_meshes);
        var preview = await Task.Run(() => CollisionWorkflow.Prepare(source, settings));
        var model = await Task.Run(() => BotwCollisionVisualModel.Create(preview.Meshes, _path));
        _prepared = preview; _preparedSettings = settings; _view.SetModel(model);
        var size = model.BoundsMaximum - model.BoundsMinimum;
        _summary.Text = $"{preview.Meshes.Count:N0} {(preview.Polytope != null ? "hulls" : "meshes")} / {model.TriangleCount:N0} triangles\nSize: {size.X:G5} x {size.Y:G5} x {size.Z:G5}";
        _status.Text = preview.Polytope != null ? "Hull preview ready. Convex hulls can fill concave gaps." : "Collision preview ready.";
    }
    private async Task Save(bool meshExport)
    {
        if (_prepared == null || _preparedSettings == null) return;
        var extension = meshExport ? ".dae" : CollisionWorkflow.Extension(_preparedSettings);
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = meshExport ? "Export collision mesh" : "Generate collision", ShowOverwritePrompt = true,
            SuggestedFileName = Path.GetFileNameWithoutExtension(_path) + "_collision" + extension, DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = meshExport ? [new("Collada") { Patterns = ["*.dae"] }, new("FBX") { Patterns = ["*.fbx"] }] : [new("Collision") { Patterns = ["*" + extension] }] });
        if (file?.TryGetLocalPath() is { } path) await WriteAsync(path, meshExport);
    }
    internal Task WriteAsync(string path, bool meshExport = false) => Work("Writing and validating collision...", async () =>
    {
        var preview = _prepared ?? throw new InvalidOperationException("Update the preview first.");
        var settings = _preparedSettings ?? throw new InvalidOperationException("Update the preview first.");
        await Task.Run(() => CollisionWorkflow.Write(preview, settings, path, _path, meshExport));
        _status.Text = "Saved " + Path.GetFileName(path);
    });
}
