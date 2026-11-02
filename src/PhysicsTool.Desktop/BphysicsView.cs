using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

internal sealed class BphysicsView : UserControl
{
    private readonly Window _owner;
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _preserved = new() { TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    private readonly TextBlock _slotTitle = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _search = new() { Name = "BphysicsSearch", Watermark = "Search cloth slots" };
    private readonly ListBox _slots = new() { Name = "BphysicsSlots" };
    private readonly StackPanel _slotFields = new() { Spacing = 12 };
    private readonly StackPanel _windFields = new() { Spacing = 12 };
    private readonly Dictionary<string, TextBox> _texts = [];
    private readonly Dictionary<string, NumericUpDown> _numbers = [];
    private readonly Dictionary<string, CheckBox> _flags = [];
    private readonly Button _undo, _redo, _apply, _duplicate, _delete, _up, _down;
    private readonly List<(byte[] Bytes, int Selected)> _past = [], _future = [];
    private byte[] _saved = [], _diskBytes = [];
    private bool _binding, _pending;
    private int _selected = -1;
    internal string SourcePath { get; private set; } = "";
    internal BphysicsEditingDocument Document { get; private set; } = null!;
    internal bool Dirty => _pending || !Document.Snapshot().AsSpan().SequenceEqual(_saved);

    internal BphysicsView(Window owner, string path)
    {
        _owner = owner;
        _slots.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Slot>((slot, _) =>
        {
            var label = new TextBlock { Text = slot?.ToString(), TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(label, slot?.Name); return label;
        });
        Document = BphysicsEditingDocument.Load(path);
        SourcePath = path; _diskBytes = File.ReadAllBytes(path); _saved = Document.Snapshot();
        var shell = new DockPanel { Margin = new Thickness(16) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        tools.Children.Add(Command("Open...", Open));
        tools.Children.Add(Command("Save", () => SaveAsync(false), "BphysicsSave"));
        tools.Children.Add(Command("Save as...", () => SaveAsync(true)));
        _undo = ViewportTools.Icon("Undo", "Undo (Ctrl+Z)", Undo); tools.Children.Add(_undo);
        _redo = ViewportTools.Icon("Redo", "Redo (Ctrl+Shift+Z)", Redo); tools.Children.Add(_redo);
        DockPanel.SetDock(tools, Dock.Right); top.Children.Add(tools); top.Children.Add(_title);
        DockPanel.SetDock(top, Dock.Top); shell.Children.Add(top);
        _status.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(_status, Dock.Bottom); shell.Children.Add(_status);
        var grid = new Grid { ColumnDefinitions = new("260,8,*") };
        var sidebar = new DockPanel();
        _search.Margin = new Thickness(0, 0, 0, 10); DockPanel.SetDock(_search, Dock.Top); sidebar.Children.Add(_search);
        var slotTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        slotTools.Children.Add(ViewportTools.Icon("Plus", "Add cloth slot", () => Change(d => _selected = d.AddSlot())));
        _duplicate = ViewportTools.Icon("Copy", "Duplicate selected slot", () => Change(d => _selected = d.AddSlot(_selected))); slotTools.Children.Add(_duplicate);
        _delete = ViewportTools.Icon("Trash2", "Remove selected slot", () => _ = DeleteAsync()); slotTools.Children.Add(_delete);
        _up = ViewportTools.Icon("ArrowUp", "Move slot up", () => Change(d => { d.MoveSlot(_selected, _selected - 1); _selected--; })); slotTools.Children.Add(_up);
        _down = ViewportTools.Icon("ArrowDown", "Move slot down", () => Change(d => { d.MoveSlot(_selected, _selected + 1); _selected++; })); slotTools.Children.Add(_down);
        DockPanel.SetDock(slotTools, Dock.Bottom); sidebar.Children.Add(slotTools); sidebar.Children.Add(_slots); grid.Children.Add(sidebar);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(splitter, 1); grid.Children.Add(splitter);
        var details = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        var bottom = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0) };
        _apply = Command("Apply changes", () => { ApplyDraft(); return Task.CompletedTask; }, "BphysicsApply");
        _apply.HorizontalAlignment = HorizontalAlignment.Left;
        bottom.Children.Add(_apply); bottom.Children.Add(_preserved); DockPanel.SetDock(bottom, Dock.Bottom); details.Children.Add(bottom);
        var tabs = new TabControl { Name = "BphysicsTabs" };
        _slotFields.Children.Add(_slotTitle);
        TextField(_slotFields, "SlotName", "Cloth name", "Must exactly match the cloth name in the linked HKCL. Renaming this slot does not rename the HKCL cloth.");
        TextField(_slotFields, "BaseBone", "Base bone", "The model bone used as this cloth's runtime attachment/reference. Use the exact bone name.");
        Flag(_slotFields, "Writeback", "Write back to local pose", "Writes the cloth result back to the local pose. Keep the original setting unless the actor needs a different pose-writing mode.");
        Flag(_slotFields, "WindEnabled", "Wind enabled", "Allows wind to affect this cloth. This does not disable cloth simulation.");
        _slotFields.Children.Add(_windFields);
        var strength = Pair(_windFields);
        Number(strength.Left, "Drag", "Wind drag", "How strongly the cloth follows the airflow. BotW divides this response by total cloth mass; light cloths usually need lower drag.");
        Number(strength.Right, "Frequency", "Gust frequency", "Controls how quickly the main wind oscillates. This is not the wind's strength.");
        var speeds = Pair(_windFields);
        Number(speeds.Left, "MinSpeed", "Minimum air speed", "Lower end of the main airflow range. Negative values allow the airflow to reverse.", true);
        Number(speeds.Right, "MaxSpeed", "Maximum air speed", "Upper end of the main airflow range. Must be at least the minimum speed.", true);
        var factors = Pair(_windFields);
        Number(factors.Left, "MainFactor", "Sub-wind: main factor", "Scales this cloth's shared sub-wind pulses according to environmental wind.", true);
        Number(factors.Right, "AddFactor", "Sub-wind: additive factor", "Adds sub-wind pulses independently of environmental wind, including calm conditions.", true);
        var shared = new StackPanel { Spacing = 12 };
        Heading(shared, "Shared sub-wind");
        Number(shared, "SubSpeed", "Pulse speed", "Shared pulse strength before each cloth's main and additive factors are applied.");
        Number(shared, "SubFrequency", "Pulse frequency", "How quickly the shared sub-wind pulses repeat.");
        Heading(shared, "Direction");
        Number(shared, "DirectionX", "X", "Actor-local sub-wind direction, X component.", true);
        Number(shared, "DirectionY", "Y", "Actor-local sub-wind direction, Y component.", true);
        Number(shared, "DirectionZ", "Z", "Actor-local sub-wind direction, Z component.", true);
        var links = new StackPanel { Spacing = 12 };
        Heading(links, "Cloth");
        Flag(links, "UseCloth", "Enable cloth", "Loads the linked HKCL for this actor. Disabling it retains the path and slots for later use.");
        TextField(links, "HkclPath", "HKCL resource path", "Game-relative path, for example Armor_999/Armor_999_Head.hkcl. Not a path on your computer.");
        Heading(links, "Support bones");
        Flag(links, "UseSupport", "Enable support bones", "Loads the linked support-bone configuration. Disabling it retains the path.");
        TextField(links, "SupportPath", "BPHYSSB resource path", "Game-relative path to the support-bone file. This editor does not modify that file.");
        tabs.ItemsSource = new[] { Tab("Cloth slot", _slotFields), Tab("Shared wind", shared), Tab("File links", links) };
        tabs.SelectedIndex = 0; details.Children.Add(tabs); Grid.SetColumn(details, 2); grid.Children.Add(details); shell.Children.Add(grid); Content = shell;
        _slots.SelectionChanged += (_, _) =>
        {
            if (_binding) return;
            var next = (_slots.SelectedItem as Slot)?.Index ?? -1;
            if (!ApplyDraft(false)) { _binding = true; _slots.SelectedItem = (_slots.ItemsSource as Slot[])?.FirstOrDefault(s => s.Index == _selected); _binding = false; return; }
            _selected = next; LoadFields();
        };
        _search.TextChanged += (_, _) => { if (!_binding && ApplyDraft(false)) Refresh(); };
        KeyDown += async (_, e) =>
        {
            bool command = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            if (command && e.Key == Key.S) { e.Handled = true; await Guard(() => SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift))); }
            else if (command && e.Key == Key.Z && e.Source is not TextBox) { e.Handled = true; if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); }
            else if (e.Key == Key.Delete && _slots.IsKeyboardFocusWithin) { e.Handled = true; await DeleteAsync(); }
        };
        Refresh(); _status.Text = "Opened " + Path.GetFileName(path);
    }

    private sealed record Slot(int Index, string Name) { public override string ToString() => $"{Index + 1}. {Name}"; }
    private static TabItem Tab(string title, Control content) => new() { Header = title, Content = new ScrollViewer { Content = content, Margin = new Thickness(0, 12, 8, 0) } };
    private static void Heading(StackPanel panel, string title) => panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
    private static (StackPanel Left, StackPanel Right) Pair(StackPanel parent)
    {
        var grid = new Grid { ColumnDefinitions = new("*,12,*") };
        var left = new StackPanel { Spacing = 8 }; var right = new StackPanel { Spacing = 8 };
        grid.Children.Add(left); Grid.SetColumn(right, 2); grid.Children.Add(right); parent.Children.Add(grid);
        return (left, right);
    }
    private void Field(StackPanel panel, string label, Control input, string tip)
    {
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        ToolTip.SetTip(input, tip); panel.Children.Add(input);
    }
    private void TextField(StackPanel panel, string key, string label, string tip)
    {
        var input = new TextBox { Name = "Bphysics" + key }; _texts[key] = input;
        Field(panel, label, input, tip);
        input.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Pending(); };
    }
    private void Number(StackPanel panel, string key, string label, string tip, bool signed = false)
    {
        var input = new NumericUpDown { Name = "Bphysics" + key, Minimum = signed ? decimal.MinValue : 0, Maximum = decimal.MaxValue,
            Increment = .1m, FormatString = "0.######", ShowButtonSpinner = false, HorizontalAlignment = HorizontalAlignment.Stretch };
        _numbers[key] = input; Field(panel, label, input, tip); input.ValueChanged += (_, _) => Pending();
    }
    private void Flag(StackPanel panel, string key, string label, string tip)
    {
        var input = new CheckBox { Name = "Bphysics" + key, Content = label }; ToolTip.SetTip(input, tip); _flags[key] = input; panel.Children.Add(input);
        input.IsCheckedChanged += (_, _) => { Pending(); UpdateWindEnabled(); };
    }
    private Button Command(string label, Func<Task> action, string? name = null)
    {
        var button = new Button { Content = label, Name = name }; button.Click += async (_, _) => await Guard(action); return button;
    }
    private async Task Guard(Func<Task> action) { try { await action(); } catch (Exception ex) { _status.Text = ex.Message; } }
    private void Pending() { if (_binding) return; _pending = true; Actions(); }
    private void UpdateWindEnabled() => _windFields.IsEnabled = _flags.GetValueOrDefault("WindEnabled")?.IsChecked == true;
    private void Actions()
    {
        _title.Text = Path.GetFileName(SourcePath) + (Dirty ? " *" : "");
        _apply.IsEnabled = _pending; _undo.IsEnabled = _pending || _past.Count > 0; _redo.IsEnabled = !_pending && _future.Count > 0;
        _duplicate.IsEnabled = _delete.IsEnabled = _selected >= 0;
        _up.IsEnabled = _selected > 0; _down.IsEnabled = _selected >= 0 && _selected < Document.Settings.Cloths.Count - 1;
        _preserved.Text = Document.PreservedSections > 0 ? $"{Document.PreservedSections} other physics sections preserved unchanged." : "";
    }
    private void Refresh()
    {
        _binding = true;
        var rows = Document.Settings.Cloths.Select((p, i) => new Slot(i, p.Name))
            .Where(s => s.Name.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        _slots.ItemsSource = rows;
        var selected = rows.FirstOrDefault(s => s.Index == _selected) ?? rows.FirstOrDefault();
        _slots.SelectedItem = selected; _selected = selected?.Index ?? -1;
        _binding = false; LoadFields();
    }
    private void LoadFields()
    {
        _binding = true;
        var d = Document.Settings;
        _texts["HkclPath"].Text = d.HkclPath; _texts["SupportPath"].Text = d.SupportBonePath ?? "";
        _flags["UseCloth"].IsChecked = d.UseCloth; _flags["UseSupport"].IsChecked = d.UseSupportBone;
        Set("SubSpeed", d.SubWindSpeed); Set("SubFrequency", d.SubWindFrequency);
        Set("DirectionX", d.SubWindDirection.X); Set("DirectionY", d.SubWindDirection.Y); Set("DirectionZ", d.SubWindDirection.Z);
        _slotFields.IsEnabled = _selected >= 0;
        _slotTitle.Text = _selected >= 0 ? $"Cloth slot {_selected + 1}" : "No cloth slot selected";
        var p = _selected >= 0 ? d.Cloths[_selected] : new BphysicsClothProfile("", "");
        _texts["SlotName"].Text = p.Name; _texts["BaseBone"].Text = p.BaseBone;
        _flags["Writeback"].IsChecked = p.WritebackToLocal; _flags["WindEnabled"].IsChecked = p.WindEnabled;
        Set("Drag", p.WindDrag); Set("Frequency", p.WindFrequency); Set("MinSpeed", p.WindMinSpeed); Set("MaxSpeed", p.WindMaxSpeed);
        Set("MainFactor", p.SubWindFactorMain); Set("AddFactor", p.SubWindFactorAdd);
        _binding = false; _pending = false; UpdateWindEnabled(); Actions();
    }
    private void Set(string key, float value) => _numbers[key].Value = float.IsFinite(value) && value <= (float)decimal.MaxValue && value >= (float)decimal.MinValue ? (decimal)value : null;
    private float Value(string key) => (float)(_numbers[key].Value ?? throw new InvalidOperationException("Enter a valid value for " + key + "."));
    private string Text(string key) => _texts[key].Text?.Trim() ?? "";
    private bool Flag(string key) => _flags[key].IsChecked == true;

    internal bool ApplyDraft(bool refresh = true)
    {
        if (!_pending) return true;
        var before = Document.Snapshot();
        try
        {
            var d = Document.Settings;
            d.HkclPath = Text("HkclPath");
            d.SupportBonePath = Text("SupportPath").Length > 0 || d.SupportBonePath != null ? Text("SupportPath") : null;
            d.UseCloth = Flag("UseCloth"); d.UseSupportBone = Flag("UseSupport");
            d.SubWindSpeed = Value("SubSpeed"); d.SubWindFrequency = Value("SubFrequency");
            d.SubWindDirection = new(Value("DirectionX"), Value("DirectionY"), Value("DirectionZ"));
            if (_selected >= 0) d.Cloths[_selected] = new(Text("SlotName"), Text("BaseBone"), Value("Frequency"), Value("Drag"), Value("MinSpeed"), Value("MaxSpeed"),
                Value("MainFactor"), Value("AddFactor"), Flag("WindEnabled"), Flag("Writeback"));
            Document.Validate();
            Remember(before); _pending = false;
            if (refresh) Refresh(); else Actions();
            _status.Text = "Changes applied."; return true;
        }
        catch (Exception ex) { Document = BphysicsEditingDocument.Parse(before); _status.Text = ex.Message; return false; }
    }
    private void Remember(byte[] before)
    {
        if (before.AsSpan().SequenceEqual(Document.Snapshot())) return;
        _past.Add((before, _selected)); _future.Clear();
        while (_past.Count > 100 || (_past.Count > 1 && _past.Sum(s => (long)s.Bytes.Length) > 32 * 1024 * 1024)) _past.RemoveAt(0);
    }
    private void Change(Action<BphysicsEditingDocument> action)
    {
        if (!ApplyDraft()) return;
        var before = Document.Snapshot(); var selected = _selected;
        try { action(Document); var next = _selected; _selected = selected; Remember(before); _selected = next; Refresh(); }
        catch (Exception ex) { Document = BphysicsEditingDocument.Parse(before); _selected = selected; Refresh(); _status.Text = ex.Message; }
    }
    private async Task DeleteAsync()
    {
        if (_selected < 0 || !ApplyDraft()) return;
        try
        {
            if (!await Dialogs.Confirm(_owner, "Remove cloth slot", $"Remove {Document.Settings.Cloths[_selected].Name}? The HKCL itself will not be changed.")) return;
            Change(d => { d.RemoveSlot(_selected); _selected = Math.Min(_selected, d.Settings.Cloths.Count - 1); });
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsEffectivelyVisible) return;
                if (_slots.SelectedIndex >= 0) _slots.ScrollIntoView(_slots.SelectedIndex);
                _slots.Focus();
            }, DispatcherPriority.Input);
        }
    }
    internal void Undo()
    {
        if (_pending) { LoadFields(); _status.Text = "Unapplied changes discarded."; return; }
        Restore(_past, _future);
    }
    internal void Redo() { if (!_pending) Restore(_future, _past); }
    private void Restore(List<(byte[] Bytes, int Selected)> from, List<(byte[] Bytes, int Selected)> to)
    {
        if (from.Count == 0) return;
        to.Add((Document.Snapshot(), _selected)); var state = from[^1]; from.RemoveAt(from.Count - 1);
        Document = BphysicsEditingDocument.Parse(state.Bytes); _selected = state.Selected; Refresh(); _status.Text = "Restored edits.";
    }
    internal async Task<bool> CanCloseAsync()
    {
        if (!Dirty) return true;
        return await Dialogs.Confirm(_owner, "Unsaved BPHYSICS changes", "Discard unsaved BPHYSICS edits?");
    }
    private async Task Open()
    {
        if (!await CanCloseAsync()) return;
        var files = await _owner.StorageProvider.OpenFilePickerAsync(new() { Title = "Open BPHYSICS", FileTypeFilter = [new("BPHYSICS") { Patterns = ["*.bphysics"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        var next = BphysicsEditingDocument.Load(path);
        Document = next; SourcePath = path; _diskBytes = File.ReadAllBytes(path); _saved = Document.Snapshot(); _past.Clear(); _future.Clear();
        _binding = true; _search.Text = ""; _binding = false; _selected = -1; Refresh(); _status.Text = "Opened " + Path.GetFileName(path);
    }
    internal async Task SaveAsync(bool saveAs)
    {
        if (!ApplyDraft()) return;
        var path = SourcePath;
        if (saveAs)
        {
            var file = await _owner.StorageProvider.SaveFilePickerAsync(new() { Title = "Save BPHYSICS", SuggestedFileName = Path.GetFileName(path), DefaultExtension = "bphysics",
                ShowOverwritePrompt = true, FileTypeChoices = [new("BPHYSICS") { Patterns = ["*.bphysics"] }] });
            if (file?.TryGetLocalPath() is not { } chosen) return;
            path = chosen;
        }
        if (Path.GetFullPath(path) == Path.GetFullPath(SourcePath) && File.Exists(path) && !File.ReadAllBytes(path).AsSpan().SequenceEqual(_diskBytes))
            throw new InvalidOperationException("This file changed outside PhysicsTool. Reopen it or use Save as to keep both versions.");
        Document.Save(path); SourcePath = path; _diskBytes = File.ReadAllBytes(path); _saved = Document.Snapshot(); Actions();
        _status.Text = "Saved " + Path.GetFileName(path);
    }
}
