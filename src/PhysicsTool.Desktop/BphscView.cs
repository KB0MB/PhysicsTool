using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

internal sealed class BphscView : UserControl
{
    private readonly TextBlock _status = new() { Text = "No static compound loaded", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _name = new() { Name = "CompoundName" };
    private readonly TextBox _search = new() { PlaceholderText = "Filter entries" };
    private readonly ListBox _list = new() { Name = "CompoundEntries" };
    private readonly StackPanel _details = new() { Spacing = 12, Margin = new Thickness(16) };
    private readonly TabControl _tabs = new() { ItemsSource = new[] { "Bodies", "Actor links", "Water", "Sections" }.Select(x => new TabItem { Header = x }).ToArray(), SelectedIndex = 0 };
    private readonly StackPanel _toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly Grid _editor;
    private readonly Button _undo, _redo, _save;
    private readonly List<byte[]> _past = [], _future = [];
    private byte[]? _saved;
    private string _source = "";
    private bool _busy;
    internal bool Busy => _busy;
    internal string SourcePath => _source;
    private readonly Window _owner;
    private Window OwnerWindow => _owner;
    private IStorageProvider StorageProvider => OwnerWindow.StorageProvider;
    internal BphscDocument? Document { get; private set; }

    internal BphscView(Window owner)
    {
        _owner = owner;
        var shell = new DockPanel { Margin = new Thickness(12) };
        _toolbar.Children.Add(Command("Open template...", Open));
        _save = Command("Save copy...", Save); _toolbar.Children.Add(_save);
        var check = Command("Check BANC...", CheckBanc); _toolbar.Children.Add(check);
        ToolTip.SetTip(check, "Compare GroundControl actor IDs and group bindings with a BANC file. Read-only; no scene files are changed.");
        _undo = ViewportTools.Icon("Undo", "Undo", Undo); _toolbar.Children.Add(_undo);
        _redo = ViewportTools.Icon("Redo", "Redo", Redo); _toolbar.Children.Add(_redo);
        DockPanel.SetDock(_toolbar, Dock.Top); shell.Children.Add(_toolbar);
        _status.Margin = new Thickness(0, 10, 0, 0); DockPanel.SetDock(_status, Dock.Bottom); shell.Children.Add(_status);
        var content = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var nameRow = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        nameRow.Children.Add(new TextBlock { Text = "Compound name", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        Grid.SetColumn(_name, 1); nameRow.Children.Add(_name);
        var rename = Command("Apply name", () => { Edit(d => d.SetName(_name.Text ?? "")); return Task.CompletedTask; });
        rename.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(rename, 2); nameRow.Children.Add(rename);
        ToolTip.SetTip(rename, "Changes the internal compound name only. BANC paths and references are not renamed.");
        DockPanel.SetDock(nameRow, Dock.Top); content.Children.Add(nameRow);
        DockPanel.SetDock(_tabs, Dock.Top); content.Children.Add(_tabs);
        _editor = new Grid { ColumnDefinitions = new("320,5,*"), Margin = new Thickness(0, 8, 0, 0) };
        var left = new DockPanel(); _search.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(_search, Dock.Top); left.Children.Add(_search); left.Children.Add(_list); _editor.Children.Add(left);
        var divider = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(divider, 1); _editor.Children.Add(divider);
        var scroll = new ScrollViewer { Content = _details }; Grid.SetColumn(scroll, 2); _editor.Children.Add(scroll);
        content.Children.Add(_editor); shell.Children.Add(content); Content = shell;
        _tabs.SelectionChanged += (_, e) => { if (e.Source == _tabs) { _search.Text = ""; Refresh(); } };
        _search.TextChanged += (_, _) => Refresh(); _list.SelectionChanged += (_, _) => Details();
        Actions();
    }

    internal Task<bool> CanCloseAsync() => _busy ? Task.FromResult(false) : Dirty ? Discard() : Task.FromResult(true);

    private bool Dirty => Document != null && (_saved == null || !Document.Snapshot().AsSpan().SequenceEqual(_saved));
    private Button Command(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) => { try { await action(); } catch (Exception ex) { _status.Text = ex.Message; } };
        return button;
    }
    private async Task<bool> Discard()
    {
        var dialog = new Window { Title = "Unsaved changes", Width = 420, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "Discard the unsaved BPHSC edits?", TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => dialog.Close(false);
        var discard = new Button { Content = "Discard" }; discard.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancel); buttons.Children.Add(discard); panel.Children.Add(buttons); dialog.Content = panel;
        return await dialog.ShowDialog<bool>(OwnerWindow);
    }
    private async Task Open()
    {
        if (Dirty && !await Discard()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Open BPHSC template", FileTypeFilter = [new("Static compound") { Patterns = ["*.bphsc", "*.bphsc.zs"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await LoadPathAsync(path);
    }
    internal async Task LoadPathAsync(string path)
    {
        _busy = true; Actions(); _status.Text = "Reading static compound...";
        try
        {
            var document = await Task.Run(() => BphscDocument.Load(path, ResourceRoots()));
            Document = document; _source = path; _saved = document.Snapshot(); _past.Clear(); _future.Clear();
            _name.Text = document.Name; Refresh();
            _status.Text = $"{document.CompoundCount} compound shapes; {document.ActorLinks.Count:N0} actor links; {document.Water.Count} water volumes. Geometry is preserved. Changes are not game-validated.";
        }
        finally { _busy = false; Actions(); }
    }
    private async Task Save()
    {
        if (Document == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Save BPHSC copy", SuggestedFileName = Document.Name + ".Nin_NX_NVN.bphsc.zs", ShowOverwritePrompt = true,
            FileTypeChoices = [new("Compressed static compound") { Patterns = ["*.bphsc.zs"] }, new("Static compound") { Patterns = ["*.bphsc"] }] });
        if (file?.TryGetLocalPath() is not { } path) return;
        _busy = true; Actions();
        try
        {
            await Task.Run(() => Document.SaveCopy(path, _source)); _saved = Document.Snapshot();
            _status.Text = "Saved and reopened " + Path.GetFileName(path) + ". Update matching BANC references separately; test in-game.";
        }
        finally { _busy = false; Actions(); }
    }
    private async Task CheckBanc()
    {
        if (Document == null) { _status.Text = "Open a BPHSC template first."; return; }
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Check GroundControl bindings", FileTypeFilter = [new("BANC") { Patterns = ["*.bcett.byml", "*.bcett.byml.zs"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        _busy = true; Actions();
        string report;
        try { report = await Task.Run(() => BphscBancCheck.Report(Document, BphscBancCheck.Read(path, ResourceRoots()))); }
        finally { _busy = false; Actions(); }
        var dialog = new Window { Title = "GroundControl bindings - " + Path.GetFileName(path), Width = 820, Height = 620, MinWidth = 500, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dialog.Content = new ScrollViewer { Content = new SelectableTextBlock { Text = report, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20) } };
        await dialog.ShowDialog(OwnerWindow);
    }
    internal void Edit(Action<BphscDocument> change)
    {
        if (Document == null || _busy) return;
        var before = Document.Snapshot();
        try { change(Document); }
        catch { Document = BphscDocument.Parse(before); throw; }
        if (before.AsSpan().SequenceEqual(Document.Snapshot())) return;
        _past.Add(before);
        // Limit history memory even when a template contains large embedded collision.
        while (_past.Count > 1 && _past.Sum(x => (long)x.Length) > 128 * 1024 * 1024) _past.RemoveAt(0);
        _future.Clear(); Refresh(); Actions(); _status.Text = "Changed. BANC references are not modified automatically.";
    }
    internal void Undo() => Restore(_past, _future);
    internal void Redo() => Restore(_future, _past);
    private void Restore(List<byte[]> from, List<byte[]> to)
    {
        if (_busy || Document == null || from.Count == 0) return;
        to.Add(Document.Snapshot()); Document = BphscDocument.Parse(from[^1]); from.RemoveAt(from.Count - 1);
        _name.Text = Document.Name; Refresh(); Actions();
    }
    private void Actions()
    {
        _toolbar.IsEnabled = !_busy; _editor.IsEnabled = _name.IsEnabled = Document != null && !_busy;
        _save.IsEnabled = Document != null; _undo.IsEnabled = _past.Count > 0; _redo.IsEnabled = _future.Count > 0;
    }
    private sealed record Row(int Index, string Label) { public override string ToString() => Label; }
    private void Refresh()
    {
        if (Document == null) return;
        int selected = (_list.SelectedItem as Row)?.Index ?? -1;
        IEnumerable<Row> rows = _tabs.SelectedIndex switch
        {
            0 => Document.Bodies.Select(b => new Row(b.Index, $"Body {b.Index} / Group {b.GroupId}")),
            1 => Document.ActorLinks.Select(l => new Row(l.Index, $"{l.Index}: 0x{l.Hash:x16}")),
            2 => Document.Water.Select(w => new Row(w.Index, $"{w.Kind} {w.Index} / Body {w.BodyIndex}")),
            _ => Document.Sections.Select(s => new Row(s.Index, $"{s.Index}: {s.Kind} ({s.Size:N0} B)"))
        };
        var filtered = rows.Where(r => r.Label.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        _list.ItemsSource = filtered; _list.SelectedItem = filtered.FirstOrDefault(r => r.Index == selected) ?? filtered.FirstOrDefault(); Details();
    }
    private void Details()
    {
        _details.Children.Clear();
        if (Document == null) return;
        if (_list.SelectedItem is not Row row)
        {
            Label(string.IsNullOrWhiteSpace(_search.Text) ? "No entries in this section." : "No matching entries.");
            return;
        }
        Label(row.Label, 19);
        switch (_tabs.SelectedIndex)
        {
            case 0:
                var body = Document.Bodies.Single(b => b.Index == row.Index);
                var position = VectorField("Position", body.Position);
                var rotation = VectorField("Rotation (radians)", body.Rotation);
                _details.Children.Add(Command("Apply transform", () => { Edit(d => d.SetBody(body.Index, Value(position), Value(rotation))); return Task.CompletedTask; }));
                var group = TextField("GroupID", body.GroupId.ToString(CultureInfo.InvariantCulture));
                var guid = TextField("GroupGUID", $"0x{body.PlacementGroupId:x16}");
                ToolTip.SetTip(group, "Matches Phive / Placement / GroupID in BANC. Reassigns an existing group, not individual shapes.");
                ToolTip.SetTip(guid, "Matches Phive / Placement / GroupGUID in BANC. Accepts decimal or 0x hexadecimal.");
                var apply = Command("Apply group binding", () => { Edit(d => d.SetGroup(body.Index, checked((ushort)Integer(group)), Integer(guid))); return Task.CompletedTask; });
                apply.IsEnabled = body.PlacementGroupId != 0 && body.GroupId != 0; _details.Children.Add(apply);
                Label($"{Document.ActorLinks.Count(l => l.BodyIndex == body.Index)} actor links");
                break;
            case 1:
                var link = Document.ActorLinks.Single(l => l.Index == row.Index);
                var hash = TextField("Actor hash / BANC placement ID", $"0x{link.Hash:x16}");
                _details.Children.Add(Command("Apply actor hash", () => { Edit(d => d.SetActorHash(link.Index, Integer(hash))); return Task.CompletedTask; }));
                Label($"Body {link.BodyIndex} / Group {link.GroupId}");
                Label(link.FirstInstance == uint.MaxValue ? "No baked shape instances" : $"Instance range: {link.FirstInstance} to {link.EndInstance}");
                Label($"Baked collision link: {(link.Valid ? "Yes" : "No")}");
                break;
            case 2:
                var water = Document.Water.Single(w => w.Index == row.Index);
                var wp = VectorField("Position", water.Position);
                var wr = VectorField("Rotation (radians)", water.Rotation);
                NumericUpDown[]? dimensions = water.Kind == "Waterfall" ? null : VectorField("Stored dimensions", water.Size);
                var flow = Number(water.Kind == "Waterfall" ? 0 : water.FlowSpeed); if (dimensions != null) Field("Flow speed", flow);
                var fluid = TextField("Fluid type ID", water.FluidType.ToString(CultureInfo.InvariantCulture));
                _details.Children.Add(Command("Apply water", () => { Edit(d => d.SetWater(water.Index, Value(wp), Value(wr), dimensions == null ? water.Size : Value(dimensions), (float)(flow.Value ?? 0), checked((uint)Integer(fluid)))); return Task.CompletedTask; }));
                break;
            default:
                var section = Document.Sections.Single(s => s.Index == row.Index);
                Label($"Offset: 0x{section.Offset:x}\nLength: {section.Size:N0} bytes\nEmbedded data is preserved unchanged.");
                break;
        }
    }
    private void Label(string value, double size = 14) => _details.Children.Add(new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap });
    private void Field(string label, Control control) { var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(new TextBlock { Text = label }); panel.Children.Add(control); _details.Children.Add(panel); }
    private TextBox TextField(string label, string value) { var text = new TextBox { Text = value }; Field(label, text); return text; }
    private NumericUpDown[] VectorField(string label, Vector3 value)
    {
        var values = new[] { Number(value.X), Number(value.Y), Number(value.Z) };
        var grid = new Grid { ColumnDefinitions = new("*,*,*") };
        for (int i = 0; i < 3; i++) { Grid.SetColumn(values[i], i); values[i].Margin = new Thickness(0, 0, i == 2 ? 0 : 6, 0); ToolTip.SetTip(values[i], new[] { "X", "Y", "Z" }[i]); grid.Children.Add(values[i]); }
        Field(label, grid); return values;
    }
    private static NumericUpDown Number(float value) => new() { Value = float.IsFinite(value) && Math.Abs(value) <= 1e20f ? (decimal)value : null,
        Minimum = -100000000000000000000m, Maximum = 100000000000000000000m, ShowButtonSpinner = false, FormatString = "0.######", Increment = .1m, HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Vector3 Value(NumericUpDown[] values) => new((float)(values[0].Value ?? throw new ArgumentException("Complete X.")), (float)(values[1].Value ?? throw new ArgumentException("Complete Y.")), (float)(values[2].Value ?? throw new ArgumentException("Complete Z.")));
    private static ulong Integer(TextBox box)
    {
        string text = box.Text?.Trim() ?? "";
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ulong.Parse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) : ulong.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }
    private static IEnumerable<string> ResourceRoots()
    {
        string root = ActorBrowserPaths.Read(AppDataPaths.SettingsFile("actor-folders.json")).Totk;
        if (string.IsNullOrWhiteSpace(root)) return [];
        return [root, Path.Combine(root, "romfs")];
    }
}
