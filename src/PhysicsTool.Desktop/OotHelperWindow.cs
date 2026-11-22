using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BymlLibrary;
using PhysicsTool.Core;
using System.Globalization;

namespace PhysicsTool.Desktop;

internal sealed partial class OotHelperWindow : Window
{
    private readonly OotHelperDocument _document;
    private readonly ListBox _groups = new();
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "Driven bones", "Drivers" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _search = new() { Watermark = "Search bones" };
    private readonly StackPanel _fields = new() { Spacing = 10, Margin = new Thickness(16, 0, 8, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _undo = new() { Content = "Undo" }, _redo = new() { Content = "Redo" };
    private readonly Button _apply = new() { Content = "Apply" };
    private Func<bool>? _applyAction;
    private bool _closing;
    private sealed record Choice(int Index, string Name) { public override string ToString() => Name; }

    internal OotHelperWindow(byte[] bytes, string filename)
    {
        _document = new(bytes);
        Title = "OoT helper bones - " + filename; Width = 940; Height = 760; MinWidth = 660; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { Margin = new Thickness(16), RowDefinitions = new("*,Auto,Auto"), RowSpacing = 12 };
        var body = new Grid { ColumnDefinitions = new("260,*") };
        var list = new DockPanel(); DockPanel.SetDock(_kind, Dock.Top); list.Children.Add(_kind);
        DockPanel.SetDock(_search, Dock.Top); _search.Margin = new Thickness(0, 8); list.Children.Add(_search); list.Children.Add(_groups);
        body.Children.Add(list); var scroll = new ScrollViewer { Content = _fields }; Grid.SetColumn(scroll, 1); body.Children.Add(scroll); layout.Children.Add(body);
        Grid.SetRow(_status, 1); layout.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var save = new Button { Content = "Save As" }; var close = new Button { Content = "Close" };
        actions.Children.Add(_undo); actions.Children.Add(_redo); actions.Children.Add(_apply); actions.Children.Add(save); actions.Children.Add(close);
        _apply.Click += (_, _) => _applyAction?.Invoke();
        Grid.SetRow(actions, 2); layout.Children.Add(actions); Content = layout;
        _groups.ItemTemplate = new FuncDataTemplate<OotHelperGroup>((g, _) => new TextBlock { Text = g?.Name, TextWrapping = TextWrapping.Wrap });
        _groups.SelectionChanged += (_, _) => ShowFields(); _kind.SelectionChanged += (_, _) => Filter(); _search.TextChanged += (_, _) => Filter();
        _undo.Click += (_, _) => { _document.Undo(); Filter(); ShowFields(); };
        _redo.Click += (_, _) => { _document.Redo(); Filter(); ShowFields(); };
        save.Click += async (_, _) =>
        {
            try
            {
                if (_applyAction?.Invoke() == false) return;
                var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "Save helper parameters", SuggestedFileName = filename, DefaultExtension = "bgyml", FileTypeChoices = [new("Helper parameters") { Patterns = ["*.bgyml"] }] });
                if (file?.TryGetLocalPath() is { } path) { _document.Save(path); _status.Text = "Saved helper parameters. Replace this entry in the actor pack to use it in-game."; }
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        };
        close.Click += (_, _) => Close();
        Closing += async (_, e) =>
        {
            if (_closing || !_document.IsDirty) return;
            e.Cancel = true;
            if (await Dialogs.Confirm(this, "Unsaved changes", "Discard changes to these helper parameters?")) { _closing = true; Close(); }
        };
        Filter();
    }

    private void Filter()
    {
        var previous = _groups.SelectedItem;
        var entries = _document.Groups.Where(g => g.Kind == (_kind.SelectedIndex == 0 ? "DrivenParams" : "DriverParams") && g.Name.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        _groups.ItemsSource = entries;
        _groups.SelectedItem = entries.FirstOrDefault(g => previous is OotHelperGroup old && g.Kind == old.Kind && g.Index == old.Index) ?? entries.FirstOrDefault();
    }

    private void ShowFields()
    {
        _fields.Children.Clear(); _undo.IsEnabled = _document.CanUndo; _redo.IsEnabled = _document.CanRedo;
        _apply.IsEnabled = false; _applyAction = null;
        if (_groups.SelectedItem is not OotHelperGroup group) return;
        _fields.Children.Add(new TextBlock { Text = group.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (group.Kind == "DrivenParams")
        {
            var connections = new Button { Content = "Connections and curves", Name = "OotHelperConnections" };
            connections.Click += async (_, _) =>
            {
                if (_applyAction?.Invoke() == false) return;
                // Work on a copy so Cancel never alters the main editor's history.
                var dialog = new OotHelperConnectionsWindow(_document.Bytes, group.Index);
                var result = await dialog.ShowDialog<byte[]?>(this);
                if (result != null) { _document.ApplyConnections(result, group.Index); ShowFields(); }
            };
            _fields.Children.Add(connections);
        }
        var inputs = new Dictionary<string, Func<string>>();
        foreach (var section in _document.Fields(group).Where(f => !f.Path.Contains("/InConnections/", StringComparison.Ordinal)).GroupBy(f => f.Path.StartsWith("$Rotation") ? "BaseRotation" : f.Path.Split('/')[0]))
        {
            var panel = new StackPanel { Spacing = 8 };
            foreach (var field in section)
            {
                var row = new Grid { ColumnDefinitions = new("*,180"), ColumnSpacing = 12 };
                row.Children.Add(new TextBlock { Text = field.Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                Control control;
                if (field.Type == BymlNodeType.Bool)
                { var check = new CheckBox { IsChecked = field.Value == "true" }; control = check; inputs[field.Path] = () => check.IsChecked == true ? "true" : "false"; }
                else if (field.Type == BymlNodeType.Int)
                {
                    var options = field.Path is "BoneNameId" or "BaseBoneNameId"
                        ? _document.BoneNames.Select((name, index) => new Choice(index, name)).ToArray()
                        : _document.Groups.Where(g => g.Kind == "DriverParams").Select(g => new Choice(g.Index, g.Name)).ToArray();
                    var combo = new ComboBox { ItemsSource = options, SelectedItem = options.FirstOrDefault(o => o.Index == int.Parse(field.Value)), HorizontalAlignment = HorizontalAlignment.Stretch };
                    control = combo; inputs[field.Path] = () => combo.SelectedItem is Choice choice ? choice.Index.ToString(CultureInfo.InvariantCulture) : field.Value;
                }
                else
                {
                    var value = decimal.Parse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture);
                    var number = new NumericUpDown { Value = value, Minimum = Math.Min(-1000000, value), Maximum = Math.Max(1000000, value), Increment = field.Type == BymlNodeType.Int ? 1 : .01m, FormatString = field.Type == BymlNodeType.Int ? "0" : "0.######" };
                    control = number; inputs[field.Path] = () => number.Value?.ToString(CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("Enter every value.");
                    if (field.Path.Contains("DrivenCurve/Data")) ToolTip.SetTip(number, "Native curve data. Hermit2DSplit stores X, value, incoming tangent and outgoing tangent for each key. Angles use radians.");
                }
                Grid.SetColumn(control, 1); row.Children.Add(control); panel.Children.Add(row);
            }
            _fields.Children.Add(new Expander { Header = OotHelperDocument.Label(section.Key), Content = panel, IsExpanded = section.Key == "BaseTranslate", HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        _apply.IsEnabled = inputs.Count > 0;
        _applyAction = () =>
        {
            try { _document.Apply(group, inputs.ToDictionary(p => p.Key, p => p.Value())); _status.Text = "Updated " + group.Name; Filter(); ShowFields(); return true; }
            catch (Exception ex) { _status.Text = ex.Message; return false; }
        };
    }
}
