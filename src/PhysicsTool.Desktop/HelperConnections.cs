using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private int _helperChannel;
    private static ComboBox HelperChoice(string name, IReadOnlyList<HelperConnectionSource> items) => new()
    {
        Name = name, ItemsSource = items, SelectedIndex = items.Count > 0 ? 0 : -1,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemTemplate = new FuncDataTemplate<HelperConnectionSource>((s, _) => new TextBlock { Text = s?.Label, TextWrapping = TextWrapping.Wrap })
    };
    private void ShowHelperConnections(BoneEditRow bone)
    {
        var panel = new StackPanel { Spacing = 8 };
        var expander = new Expander { Name = "HelperConnections", Header = "Connections", Content = panel, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        _details.Children.Add(expander);
        IReadOnlyList<HelperConnectionRow> connections;
        try { connections = Session.Document.GetHelperConnections(bone.Index); }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.InvalidDataException)
        { panel.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap }); return; }
        var channel = new ComboBox { Name = "HelperOutputChannel", ItemsSource = SidecarConnectionEditing.Channels, SelectedIndex = _helperChannel, HorizontalAlignment = HorizontalAlignment.Stretch };
        DetailRow(panel, "Affected movement", channel);
        ToolTip.SetTip(channel, "The movement this response adds to the selected helper's base pose. Angular outputs are radians; translations use the file's model units.");
        var fields = new StackPanel { Spacing = 8 }; panel.Children.Add(fields);
        void Show()
        {
            fields.Children.Clear(); _helperChannel = Math.Clamp(channel.SelectedIndex, 0, 5);
            var row = connections[_helperChannel];
            var sources = new List<HelperConnectionSource> { new(-1, false, "Not connected") };
            sources.AddRange(Session.Document.GetSidecarDriverGroups().SelectMany(g => Session.Document.GetHelperConnectionSources(g.Index)).DistinctBy(s => s.Index));
            foreach (var id in new[] { row.First, row.Second })
                if (!sources.Any(s => s.Index == id)) sources.Add(new(id, false, $"Existing response {id}"));
            var first = HelperChoice("HelperConnectionFirst", sources); first.SelectedItem = sources.Single(s => s.Index == row.First);
            var second = HelperChoice("HelperConnectionSecond", sources); second.SelectedItem = sources.Single(s => s.Index == row.Second);
            fields.Children.Add(new TextBlock { Text = "Driver response" }); fields.Children.Add(first);
            fields.Children.Add(new TextBlock { Text = "Add response" }); fields.Children.Add(second);
            ToolTip.SetTip(first, "Input bone / measured movement / response curve. Editing a curve also changes other helpers that use that curve.");
            ToolTip.SetTip(second, "Optional second response, added to the first. Choose Not connected to remove it.");
            NumericUpDown? weight1 = null, weight2 = null;
            if (Session.Document.IsBphyssb)
            {
                weight1 = DetailNumber(fields, "HelperConnectionWeight1", "First strength", row.FirstWeight);
                weight2 = DetailNumber(fields, "HelperConnectionWeight2", "Second strength", row.SecondWeight);
                ToolTip.SetTip(weight1, "BotW output multiplier. 1 = full response; 0 = no response; negative reverses it.");
            }
            var apply = new Button { Name = "ApplyHelperConnection", Content = "Apply connection", HorizontalAlignment = HorizontalAlignment.Stretch };
            fields.Children.Add(apply);
            apply.Click += (_, _) => Try(() => {
                if (!CanEditHelper) throw new InvalidOperationException("Pause playback before editing connections.");
                var a = (HelperConnectionSource?)first.SelectedItem ?? throw new ArgumentException("Choose a driver response.");
                var b = (HelperConnectionSource?)second.SelectedItem ?? throw new ArgumentException("Choose a second response or Not connected.");
                Edit((d, _) => d.SetHelperConnection(bone.Index, row.Channel, a.Index, b.Index,
                    weight1 == null ? 1 : DetailValue(weight1), weight2 == null ? 1 : DetailValue(weight2)));
                var updatedGroup = Session.Document.GetSidecarDriverGroups().FirstOrDefault(g => g.DrivenBoneIndices.Contains(bone.Index));
                if (updatedGroup != null && updatedGroup.Index != Session.ClothIndex) { Session.ClothIndex = updatedGroup.Index; Refresh(); }
                SelectHelper(bone.Index);
                _status.Text = $"Updated {bone.Name}: {row.Name}. Other helpers' connections are unchanged.";
            });
        }
        channel.SelectionChanged += (_, _) => Show(); Show();
    }
    private void AddNewHelperCurve(StackPanel parent)
    {
        var form = new StackPanel { Spacing = 8 };
        var section = new Expander { Name = "NewHelperCurve", Header = "New response curve", Content = form, HorizontalAlignment = HorizontalAlignment.Stretch };
        parent.Children.Add(section);
        var input = HelperChoice("NewHelperCurveInput", Session.Document.GetHelperInputs(Session.ClothIndex));
        form.Children.Add(new TextBlock { Text = "Input bone" }); form.Children.Add(input);
        var attribute = new ComboBox { Name = "NewHelperCurveAttribute", ItemsSource = SidecarConnectionEditing.Channels, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        DetailRow(form, "Input movement", attribute);
        ToolTip.SetTip(attribute, "Movement measured from the input bone's rest pose, in its configured reference frame. Rotations use radians.");
        var min = DetailNumber(form, "NewHelperCurveMin", "Minimum input", -1);
        var max = DetailNumber(form, "NewHelperCurveMax", "Maximum input", 1);
        var gain = DetailNumber(form, "NewHelperCurveGain", "Response scale", 1);
        ToolTip.SetTip(gain, "Initial output = input multiplied by this value. Use zero for a neutral curve or a negative value to reverse motion.");
        var smooth = new CheckBox { Name = "NewHelperCurveSmooth", Content = "Smooth response" }; form.Children.Add(smooth);
        ToolTip.SetTip(smooth, "Zero tangents ease between the three keys. Otherwise the initial response is linear. Values clamp outside the input range.");
        var create = new Button { Name = "CreateHelperCurve", Content = "Create curve", HorizontalAlignment = HorizontalAlignment.Stretch };
        form.Children.Add(create);
        ToolTip.SetTip(create, "Create a separate three-key curve. Assign it under Connections to drive an affected bone; existing connections are not replaced.");
        create.Click += (_, _) => Try(() => {
            if (!CanEditHelper) throw new InvalidOperationException("Pause playback before creating a curve.");
            if (input.SelectedItem is not HelperConnectionSource driver || attribute.SelectedIndex < 0) throw new ArgumentException("Choose an input bone and movement.");
            var lo = DetailValue(min); var hi = DetailValue(max); var scale = DetailValue(gain);
            if (lo >= hi) throw new ArgumentException("Maximum input must be greater than minimum input.");
            var tangent = smooth.IsChecked == true ? 0 : scale;
            var keys = new[] { lo, lo + (hi - lo) / 2, hi }.Select(x => new Vector4(x, x * scale, tangent, tangent)).ToArray();
            if (keys.Any(k => !float.IsFinite(k.Y) || Math.Abs(k.Y) > 1000000))
                throw new ArgumentException("The generated output exceeds the curve editor's range. Reduce the input range or response scale.");
            _helperCurvesExpanded = true;
            Edit((d, c) => _helperCurveIndex = d.CreateHelperCurve(c, driver.Index, attribute.SelectedIndex, keys));
            _status.Text = "Created response curve. Choose it under Connections for the affected bone.";
        });
    }
    private async Task DeleteIndividualHelper()
    {
        if (!CanEditHelper || _items.SelectedItem is not Item { Data: BoneEditRow bone }) return;
        if (!await Dialogs.Confirm(this, "Delete helper", $"Remove helper behavior from {bone.Name}?\n\nThe model bone, input role, shared responses and other helpers are kept. This can be undone.")) return;
        if (!CanEditHelper) return;
        Edit((d, _) => d.DeleteHelper(bone.Index));
        _status.Text = $"Removed helper behavior from {bone.Name}.";
    }
}
