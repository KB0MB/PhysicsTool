using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;

namespace PhysicsTool.Desktop;

internal sealed record HelperPoseEdit(int Driver, int Channel, IReadOnlyList<HelperPoseResponse> Responses, bool Replace);

internal sealed class HelperPoseWindow : Window
{
    private readonly HkclService _source;
    private readonly ImportedModelRig _rig;
    private readonly int _group;
    private readonly string _original;
    private readonly PhysicsViewport _viewport = new() { ShowGrid = true, ShowPhysics = true, ShowBones = true, ShowParticles = false, ShowColliders = false, CanSelect = false };
    private readonly ComboBox _driver = new() { Name = "PoseDriver", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _movement = new() { Name = "PoseDriverMovement", ItemsSource = new[] { "Twist", "Bend horizontally", "Bend vertically", "Move X", "Move Y", "Move Z" }, SelectedIndex = 1 };
    private readonly ComboBox _helper = new() { Name = "PoseHelper", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider _input = new() { Name = "PoseDriverValue", Minimum = -1.5, Maximum = 1.5, Height = 32, MinHeight = 32 };
    private readonly NumericUpDown _inputNumber = Number("PoseDriverNumber");
    private readonly ListBox _poses = new() { Name = "CapturedHelperPoses", Height = 88 };
    private readonly CheckBox _smooth = new() { Name = "PoseSmooth", Content = "Smooth response", IsChecked = true };
    private readonly CheckBox _replace = new() { Name = "PoseReplace", Content = "Replace selected responses" };
    private readonly CheckBox _test = new() { Name = "PoseTest", Content = "Test captured poses" };
    private readonly TextBlock _status = new() { Name = "PoseStatus", TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _fields = new() { Spacing = 4 };
    private readonly List<HelperPoseExample> _examples = [];
    private readonly Dictionary<int, HashSet<int>> _channels = [];
    private Dictionary<int, float[]> _values = [];
    private VisualModelData _driverPose = new();
    private VisualModelData _evaluatedPose = new();
    private readonly HelperPoseFrame[] _helpers;
    private bool _updating;
    internal HelperPoseEdit? Result { get; private set; }

    internal HelperPoseWindow(HkclService source, int group, ImportedModelRig rig)
    {
        _source = source; _group = group; _rig = rig; _original = source.CaptureState();
        var drivers = source.GetHelperPoseFrames(group, true).ToArray();
        _helpers = source.GetHelperPoseFrames(group, false).ToArray();
        if (drivers.Length == 0 || _helpers.Length == 0) throw new InvalidOperationException("This group needs an input bone and a pose-based helper.");
        foreach (var frame in drivers.Concat(_helpers)) frame.Measure(rig.Rest.Bones);
        Title = "Pose responses"; Width = 1180; Height = 800; MinWidth = 850; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _driver.ItemsSource = drivers; _driver.SelectedIndex = 0;
        _helper.ItemsSource = _helpers; _helper.SelectedIndex = 0;
        var layout = new Grid { ColumnDefinitions = new("340,*"), RowDefinitions = new("*,Auto"), Margin = new(12), ColumnSpacing = 12 };
        var controls = new StackPanel { Spacing = 6 };
        void Label(string text) => controls.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 16 });
        Label("Driver"); controls.Children.Add(_driver); controls.Children.Add(_movement);
        var inputRow = new Grid { ColumnDefinitions = new("*,90"), ColumnSpacing = 8 };
        inputRow.Children.Add(_input); Grid.SetColumn(_inputNumber, 1); inputRow.Children.Add(_inputNumber); controls.Children.Add(inputRow);
        ToolTip.SetTip(inputRow, "Driver displacement from the file's reference pose. Angles are radians; translations use model units.");
        var restart = new Button { Content = "Reset to model rest pose", Name = "ResetPoseExamples" }; controls.Children.Add(restart);
        restart.Click += (_, _) => Guard(ResetExamples);
        Label("Affected bone"); controls.Children.Add(_helper); controls.Children.Add(_fields);
        Label("Captured poses"); controls.Children.Add(_poses);
        var actions = new WrapPanel(); controls.Children.Add(actions);
        Button Action(string name, string text, Action handler) { var b = new Button { Name = name, Content = text, Margin = new(0, 0, 6, 0) }; b.Click += (_, _) => Guard(handler); actions.Children.Add(b); return b; }
        Action("CaptureHelperPose", "Capture pose", Capture);
        Action("RemoveHelperPose", "Remove", () => { if (_poses.SelectedIndex >= 0) { _examples.RemoveAt(_poses.SelectedIndex); RefreshExamples(); } });
        ToolTip.SetTip(actions, "Capture two or three different driver positions. Capturing an existing driver value updates that pose.");
        var previewOptions = new WrapPanel(); _smooth.Margin = new(0, 0, 12, 0);
        previewOptions.Children.Add(_smooth); previewOptions.Children.Add(_test); controls.Children.Add(previewOptions);
        ToolTip.SetTip(_replace, "Replace only the selected movements on affected bones. Other bones and shared curves are preserved.");
        layout.Children.Add(new ScrollViewer { Content = controls, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var stage = new Grid(); var surface = new ModelGlControl(_viewport); stage.Children.Add(surface); stage.Children.Add(_viewport);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new(8) };
        tools.Children.Add(ViewportTools.Icon("Maximize", "Frame model", _viewport.Fit));
        var bones = new CheckBox { Content = "Bones", IsChecked = true, Margin = new(8, 0) };
        bones.IsCheckedChanged += (_, _) => { _viewport.ShowBones = bones.IsChecked == true; _viewport.InvalidateVisual(); };
        tools.Children.Add(bones); stage.Children.Add(tools);
        Grid.SetColumn(stage, 1); layout.Children.Add(stage);
        var footer = new Grid { ColumnDefinitions = new("*,Auto,Auto,Auto"), Margin = new(0, 12, 0, 0) };
        footer.Children.Add(_status);
        Grid.SetColumn(_replace, 1); footer.Children.Add(_replace);
        var apply = new Button { Name = "ApplyHelperPoses", Content = "Apply responses", Margin = new(8, 0) };
        apply.Click += (_, _) => Guard(() => {
            var edit = BuildEdit(); var validation = new HkclService(); validation.RestoreState(_original);
            validation.ApplyHelperPoses(_group, edit.Driver, edit.Channel, edit.Responses, edit.Replace);
            Result = edit; Close();
        });
        Grid.SetColumn(apply, 2); footer.Children.Add(apply);
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => Close(); Grid.SetColumn(cancel, 3); footer.Children.Add(cancel);
        Grid.SetRow(footer, 1); Grid.SetColumnSpan(footer, 2); layout.Children.Add(footer); Content = layout;
        surface.RenderFailed += message => _status.Text = message;
        _driver.SelectionChanged += (_, _) => { if (!_updating) Guard(ResetExamples); };
        _movement.SelectionChanged += (_, _) => { if (!_updating) Guard(ResetExamples); };
        _helper.SelectionChanged += (_, _) => { if (!_updating) Guard(() => { ShowFields(); HighlightBones(); }); };
        _input.ValueChanged += (_, _) => { if (!_updating) ChangeInput(_input.Value); };
        _inputNumber.ValueChanged += (_, _) => { if (!_updating && _inputNumber.Value is { } v) ChangeInput((double)v); };
        _test.IsCheckedChanged += (_, _) => { if (!_updating) Guard(() => { RefreshPose(); ShowFields(); }); };
        _smooth.IsCheckedChanged += (_, _) => { if (!_updating && _test.IsChecked == true) Guard(RefreshPose); };
        _poses.SelectionChanged += (_, _) => {
            if (_updating || _poses.SelectedIndex < 0) return;
            Guard(() => {
                var pose = _examples[_poses.SelectedIndex]; SetInput(pose.Input); RefreshPose();
                if (_test.IsChecked != true) { _values = pose.Helpers.ToDictionary(p => p.Key, p => p.Value.ToArray()); RenderValues(); }
                ShowFields();
            });
        };
        _viewport.SetModel(rig.Rest); ResetExamples();
    }

    private static NumericUpDown Number(string name) => new() { Name = name, Minimum = -1000, Maximum = 1000, Increment = .01m, FormatString = "0.###", ShowButtonSpinner = false, Height = 32, MinHeight = 32 };
    private void Guard(Action action) { try { action(); } catch (Exception ex) { _status.Text = ex.Message; } }
    private HelperPoseFrame Driver => (HelperPoseFrame)_driver.SelectedItem!;
    private void SetInput(float value)
    {
        _updating = true;
        _input.Minimum = Math.Min(_input.Minimum, value); _input.Maximum = Math.Max(_input.Maximum, value);
        _input.Value = value; _inputNumber.Value = (decimal)value; _updating = false;
    }
    private void ChangeInput(double value) => Guard(() => { SetInput((float)value); RefreshPose(); ShowFields(); });
    private void ResetExamples()
    {
        _updating = true; _test.IsChecked = false; _updating = false;
        _examples.Clear(); _channels.Clear();
        SetInput(Driver.Measure(_rig.Rest.Bones)[_movement.SelectedIndex]);
        RefreshPose(); Capture(); ShowFields();
        _status.Text = "Reference pose captured. No file changes applied.";
    }
    private void RefreshPose()
    {
        var input = Driver.Measure(_rig.Rest.Bones); input[_movement.SelectedIndex] = (float)_input.Value;
        var driverOverride = Driver.Pose(_rig.Rest.Bones, input);
        _driverPose = _rig.Sample([driverOverride]);
        var document = _source;
        if (_test.IsChecked == true)
        {
            var edit = BuildEdit(); document = new HkclService(); document.RestoreState(_original);
            document.ApplyHelperPoses(_group, edit.Driver, edit.Channel, edit.Responses, true);
        }
        var evaluated = HelperPoseEvaluation.Evaluate(document, _driverPose.Bones, _rig.Rest.Bones);
        var model = _rig.Sample(new[] { driverOverride }.Concat(evaluated.Poses).ToArray());
        _evaluatedPose = model;
        _values = _helpers.ToDictionary(h => h.Bone, h => h.Measure(model.Bones));
        Display(model);
        if (evaluated.Missing.Count > 0) _status.Text = "Missing reference bones: " + string.Join(", ", evaluated.Missing);
    }
    private void RenderValues()
    {
        var input = Driver.Measure(_rig.Rest.Bones); input[_movement.SelectedIndex] = (float)_input.Value;
        var affected = _helpers.Select(h => SidecarPreviewEvaluator.NormalizeBoneName(h.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var otherHelpers = _source.GetSidecarDriverGroups().SelectMany(g => g.DrivenBoneIndices).ToHashSet();
        var otherNames = _source.GetSidecarBoneRows().Where(b => otherHelpers.Contains(b.Index))
            .Select(b => SidecarPreviewEvaluator.NormalizeBoneName(b.Name)).Where(n => !affected.Contains(n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overrides = _evaluatedPose.Bones.Where(b => otherNames.Contains(SidecarPreviewEvaluator.NormalizeBoneName(b.Name)))
            .Select(b => new VisualBoneOverride(b.Name, b.Position, b.Rotation, b.Scale)).ToList();
        overrides.Add(Driver.Pose(_rig.Rest.Bones, input));
        // Sample in skeleton order so descendants resolve against their posed parent.
        var current = _rig.Sample(overrides);
        foreach (var frame in _helpers.OrderBy(h => Depth(h.Name)))
        {
            overrides.Add(frame.Pose(current.Bones, _values[frame.Bone]));
            current = _rig.Sample(overrides);
        }
        Display(current);
        int Depth(string name) { var bone = _rig.Rest.Bones.First(b => SidecarPreviewEvaluator.NormalizeBoneName(b.Name) == SidecarPreviewEvaluator.NormalizeBoneName(name)); var depth = 0;
            while (bone.ParentIndex >= 0 && depth < _rig.Rest.Bones.Count) { bone = _rig.Rest.Bones.First(b => b.Index == bone.ParentIndex); depth++; } return depth; }
    }
    private void Display(VisualModelData model)
    {
        _viewport.SetModelPose(model);
        var data = new ParticlePreviewData();
        foreach (var bone in model.Bones)
            data.Bones.Add(new BonePreviewPoint { Index = bone.Index, ParentIndex = bone.ParentIndex, EditorIndex = bone.Index,
                Name = bone.Name, Position = bone.Position, AxisX = bone.AxisX, AxisY = bone.AxisY, AxisZ = bone.AxisZ });
        _viewport.Data = data; HighlightBones();
    }
    private void HighlightBones()
    {
        var names = new[] { Driver.Name, (_helper.SelectedItem as HelperPoseFrame)?.Name ?? "" }
            .Select(SidecarPreviewEvaluator.NormalizeBoneName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _viewport.SelectedBones.Clear();
        foreach (var bone in _viewport.Data.Bones.Where(b => names.Contains(SidecarPreviewEvaluator.NormalizeBoneName(b.Name))))
            _viewport.SelectedBones.Add(bone.Index);
        _viewport.InvalidateVisual();
    }
    private void ShowFields()
    {
        _fields.Children.Clear();
        if (_helper.SelectedItem is not HelperPoseFrame frame || !_values.ContainsKey(frame.Bone)) return;
        if (!_channels.TryGetValue(frame.Bone, out var selected)) _channels[frame.Bone] = selected = [];
        var rotation = new CheckBox { Content = "Rotation", IsChecked = selected.Contains(0), IsEnabled = _test.IsChecked != true };
        _fields.Children.Add(rotation);
        rotation.IsCheckedChanged += (_, _) => { foreach (var c in new[] { 0, 1, 2 }) { if (rotation.IsChecked == true) selected.Add(c); else selected.Remove(c); } ShowFields(); };
        ToolTip.SetTip(rotation, "Capture the complete rotation response: twist and both bends. Position channels are separate.");
        var labels = new[] { "Twist", "Horizontal", "Vertical", "Position X", "Position Y", "Position Z" };
        for (int c = 0; c < 6; c++)
        {
            var channel = c;
            var row = new Grid { ColumnDefinitions = new("108,*,76"), ColumnSpacing = 6 };
            _fields.Children.Add(row);
            if (c >= 3)
            {
                var check = new CheckBox { Content = labels[c], IsChecked = selected.Contains(c), IsEnabled = _test.IsChecked != true };
                row.Children.Add(check);
                check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) selected.Add(channel); else selected.Remove(channel); ShowFields(); };
            }
            else row.Children.Add(new TextBlock { Text = labels[c], VerticalAlignment = VerticalAlignment.Center });
            var value = _values[frame.Bone][c];
            var enabled = selected.Contains(c) && _test.IsChecked != true;
            var slider = new Slider { Name = "HelperPoseSlider" + c, Minimum = Math.Min(c < 3 ? -3 : -1, value), Maximum = Math.Max(c < 3 ? 3 : 1, value), Value = value, IsEnabled = enabled, Height = 32, MinHeight = 32 };
            var number = Number("HelperPoseValue" + c); number.Value = (decimal)value; number.IsEnabled = enabled;
            Grid.SetColumn(slider, 1); row.Children.Add(slider); Grid.SetColumn(number, 2); row.Children.Add(number);
            bool changing = false;
            void Changed(float next) { if (changing) return; changing = true; slider.Minimum = Math.Min(slider.Minimum, next); slider.Maximum = Math.Max(slider.Maximum, next); slider.Value = next; number.Value = (decimal)next; changing = false; _values[frame.Bone][channel] = next; Guard(RenderValues); }
            slider.ValueChanged += (_, _) => Changed((float)slider.Value);
            number.ValueChanged += (_, _) => { if (number.Value is { } n) Changed((float)n); };
        }
    }
    private void Capture()
    {
        if (_test.IsChecked == true) throw new InvalidOperationException("Turn off Test captured poses before capturing a new pose.");
        var sample = new HelperPoseExample((float)_input.Value, _values.ToDictionary(p => p.Key, p => p.Value.ToArray()));
        var i = _examples.FindIndex(p => Math.Abs(p.Input - sample.Input) < .00001f);
        if (i >= 0) _examples[i] = sample;
        else { if (_examples.Count == 3) throw new InvalidOperationException("Three poses are already captured. Remove a pose or update one at its driver value."); _examples.Add(sample); }
        RefreshExamples(); _status.Text = $"{_examples.Count} captured poses. No file changes applied.";
    }
    private void RefreshExamples()
    {
        _updating = true; _poses.ItemsSource = _examples.Select((p, i) => $"Pose {i + 1}  |  Driver {p.Input:0.###}").ToArray(); _updating = false;
    }
    private HelperPoseEdit BuildEdit() => new(Driver.Record, _movement.SelectedIndex,
        HelperPoseFitting.Fit(_examples, _channels.Where(p => p.Value.Count > 0).ToDictionary(p => p.Key, p => p.Value.ToArray()), _smooth.IsChecked == true), _replace.IsChecked == true);
}
