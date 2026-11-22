using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

internal sealed class OotHelperConnectionsWindow : Window
{
    private readonly OotHelperDocument _document;
    private readonly int _bone;
    private readonly ComboBox _connections = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _keys = new() { Spacing = 6 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly List<NumericUpDown[]> _inputs = new();
    private OotHelperConnection? _current;
    private bool _refreshing;
    private sealed record Template(OotHelperConnection Connection, string Label) { public override string ToString() => Label; }

    internal OotHelperConnectionsWindow(byte[] bytes, int bone)
    {
        _document = new(bytes); _bone = bone;
        Title = "Helper connections and curves"; Width = 920; Height = 720; MinWidth = 720; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new("Auto,Auto,*,Auto,Auto"), RowSpacing = 12 };
        var addPanel = new Grid { ColumnDefinitions = new("140,*,*,Auto"), RowDefinitions = new("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 4 };
        var channel = new ComboBox { ItemsSource = OotHelperDocument.Channels, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        channel.ItemTemplate = new FuncDataTemplate<string>((value, _) => new TextBlock { Text = OotHelperDocument.Label(value ?? ""), TextWrapping = TextWrapping.Wrap });
        var drivers = _document.Groups.Where(g => g.Kind == "DriverParams").ToArray();
        var driver = new ComboBox { ItemsSource = drivers, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var names = _document.Groups.Where(g => g.Kind == "DrivenParams").ToDictionary(g => g.Index, g => g.Name);
        var templates = _document.Connections().GroupBy(c => c.Attribute).Select(g => g.First()).Select(c =>
            new Template(c, $"{names[c.Bone]} / {OotHelperDocument.Label(c.Channel)} (0x{c.Attribute:x8})")).ToArray();
        var template = new ComboBox { ItemsSource = templates, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(channel, "Output channel on this helper bone.");
        ToolTip.SetTip(driver, "Driver whose motion supplies the curve input.");
        ToolTip.SetTip(template, "Native input attribute, copied from this existing connection. The source bone and output channel identify the example, not the meaning of the input hash.");
        var add = new Button { Content = "Add connection", IsEnabled = templates.Length > 0 && drivers.Length > 0 };
        Control[] controls = [channel, driver, template, add];
        var headings = new[] { "Output", "Driver", "Input template" };
        for (int i = 0; i < headings.Length; i++) { var label = new TextBlock { Text = headings[i] }; Grid.SetColumn(label, i); addPanel.Children.Add(label); }
        for (int i = 0; i < controls.Length; i++) { Grid.SetColumn(controls[i], i); Grid.SetRow(controls[i], 1); addPanel.Children.Add(controls[i]); }
        layout.Children.Add(addPanel);
        var selector = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        selector.Children.Add(_connections); var remove = new Button { Content = "Remove connection" }; Grid.SetColumn(remove, 1); selector.Children.Add(remove);
        Grid.SetRow(selector, 1); layout.Children.Add(selector);
        var scroll = new ScrollViewer { Content = _keys }; Grid.SetRow(scroll, 2); layout.Children.Add(scroll);
        Grid.SetRow(_status, 3); layout.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var x = Number(0); x.Width = 130; x.Name = "OotCurveInsertX"; ToolTip.SetTip(x, "Input position for the new key. Angles use radians.");
        var insert = new Button { Content = "Insert key", Name = "OotCurveInsert" };
        var apply = new Button { Content = "Apply", Name = "OotConnectionsApply" }; var cancel = new Button { Content = "Cancel" };
        actions.Children.Add(x); actions.Children.Add(insert); actions.Children.Add(apply); actions.Children.Add(cancel);
        Grid.SetRow(actions, 4); layout.Children.Add(actions); Content = layout;
        _connections.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            var next = _connections.SelectedItem as OotHelperConnection;
            if (!SaveKeys()) { _refreshing = true; _connections.SelectedItem = _current; _refreshing = false; return; }
            _current = next; ShowKeys();
        };
        add.Click += (_, _) => Execute(() =>
        {
            if (!SaveKeys()) return;
            if (driver.SelectedItem is not OotHelperGroup selected || template.SelectedItem is not Template source) return;
            _document.AddConnection(_bone, (string)channel.SelectedItem!, selected.Index, source.Connection, bytes);
            Reload(_document.Connections(_bone).Last(c => c.Channel == (string)channel.SelectedItem!));
        });
        remove.Click += (_, _) => Execute(() => { if (_current != null) { _document.RemoveConnection(_current); _current = null; Reload(); } });
        insert.Click += (_, _) => Execute(() =>
        {
            if (_current == null || !SaveKeys()) return;
            _document.InsertCurveKey(_current, Value(x)); ShowKeys();
        });
        apply.Click += (_, _) => { if (SaveKeys()) Close(_document.Bytes); };
        cancel.Click += (_, _) => Close(null);
        Reload();
    }

    private void Execute(Action action) { try { action(); } catch (Exception ex) { _status.Text = ex.Message; } }
    private void Reload(OotHelperConnection? selected = null)
    {
        _refreshing = true;
        var entries = _document.Connections(_bone); _connections.ItemsSource = entries;
        _current = selected ?? entries.FirstOrDefault(); _connections.SelectedItem = _current;
        _refreshing = false; ShowKeys();
    }
    private static NumericUpDown Number(float value) => new()
    {
        Value = (decimal)value, Minimum = Math.Min(-1000000, (decimal)value), Maximum = Math.Max(1000000, (decimal)value), Increment = .01m,
        FormatString = "0.#########", HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static float Value(NumericUpDown value) => value.Value is { } number ? (float)number : throw new InvalidOperationException("Enter every key value.");
    private bool SaveKeys()
    {
        if (_current == null || _inputs.Count == 0) return true;
        try
        {
            _document.SetCurveKeys(_current, _inputs.Select(row => new OotCurveKey(Value(row[0]), Value(row[1]), Value(row[2]), Value(row[3]))).ToArray());
            _status.Text = ""; return true;
        }
        catch (Exception ex) { _status.Text = ex.Message; return false; }
    }
    private void ShowKeys()
    {
        _keys.Children.Clear(); _inputs.Clear();
        if (_current == null) return;
        try
        {
            var keys = _document.CurveKeys(_current);
            var drivers = _document.Groups.Where(g => g.Kind == "DriverParams").ToArray();
            var driver = new ComboBox { ItemsSource = drivers, SelectedItem = drivers.FirstOrDefault(d => d.Index == _current.Driver), HorizontalAlignment = HorizontalAlignment.Stretch };
            ToolTip.SetTip(driver, "Driver supplying this connection's input.");
            driver.SelectionChanged += (_, _) => Execute(() =>
            {
                if (driver.SelectedItem is not OotHelperGroup selected || !SaveKeys()) return;
                _document.SetConnectionDriver(_current!, selected.Index);
                Reload(_current! with { Driver = selected.Index });
            });
            _keys.Children.Add(new TextBlock { Text = "Driver" }); _keys.Children.Add(driver);
            var plot = new HelperCurvePlot { Height = 170, Keys = keys.Select(k => new System.Numerics.Vector4(k.X, k.Value, k.Incoming, k.Outgoing)).ToArray() };
            ToolTip.SetTip(plot, "Input runs left to right; output runs bottom to top. Points mark the curve keys.");
            _keys.Children.Add(plot);
            var header = new Grid { ColumnDefinitions = new("*,*,*,*,90"), ColumnSpacing = 8 };
            var labels = new[] { "Input", "Output", "Incoming tangent", "Outgoing tangent" };
            for (int i = 0; i < labels.Length; i++) { var label = new TextBlock { Text = labels[i], TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(label, i); header.Children.Add(label); }
            _keys.Children.Add(header);
            for (int i = 0; i < keys.Count; i++)
            {
                int index = i; var key = keys[i];
                var row = new Grid { ColumnDefinitions = new("*,*,*,*,90"), ColumnSpacing = 8 };
                var inputs = new[] { Number(key.X), Number(key.Value), Number(key.Incoming), Number(key.Outgoing) }; _inputs.Add(inputs);
                for (int j = 0; j < inputs.Length; j++) { Grid.SetColumn(inputs[j], j); row.Children.Add(inputs[j]); }
                var remove = new Button { Content = "Remove", IsEnabled = keys.Count > 2 }; Grid.SetColumn(remove, 4); row.Children.Add(remove);
                remove.Click += (_, _) => Execute(() =>
                {
                    if (!SaveKeys()) return;
                    var remaining = _document.CurveKeys(_current!).ToList(); remaining.RemoveAt(index);
                    _document.SetCurveKeys(_current!, remaining); ShowKeys();
                });
                _keys.Children.Add(row);
            }
            foreach (var input in _inputs.SelectMany(row => row)) input.ValueChanged += (_, _) =>
            {
                if (_inputs.Any(row => row.Any(n => n.Value == null))) return;
                plot.Keys = _inputs.Select(row => new System.Numerics.Vector4(Value(row[0]), Value(row[1]), Value(row[2]), Value(row[3]))).OrderBy(k => k.X).ToArray();
                plot.InvalidateVisual();
            };
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
}
