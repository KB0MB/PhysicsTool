using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

internal class PhysicsSettingsWindow : Window
{
    protected readonly StackPanel Fields = new() { Spacing = 10, Margin = new Thickness(20) };
    protected readonly StackPanel Actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(20, 12) };
    protected readonly TextBlock Error = new() { Foreground = Brushes.LightSalmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 0), IsVisible = false };
    protected PhysicsSettingsWindow(string title, double height)
    {
        Title = title; Width = 620; Height = height; MinWidth = 460; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var content = new DockPanel(); DockPanel.SetDock(Actions, Dock.Bottom); content.Children.Add(Actions);
        DockPanel.SetDock(Error, Dock.Bottom); content.Children.Add(Error);
        content.Children.Add(new ScrollViewer { Content = Fields }); Content = content;
    }
    protected Button Action(string label, Action action, bool cancel = false)
    {
        var button = new Button { Content = label, IsCancel = cancel, MinWidth = 85 };
        button.Click += (_, _) => action(); Actions.Children.Add(button); return button;
    }
    protected void Heading(string text) => Fields.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 4) });
    protected NumericUpDown Number(string id, string label, float value, decimal min, decimal max, decimal increment = .01m, string? tip = null, StackPanel? host = null)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions(host == null ? "*,210" : "*,140") };
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        var original = (decimal)value;
        var input = new NumericUpDown { Name = id, Minimum = Math.Min(min, original), Maximum = Math.Max(max, original), Value = original, Increment = increment, FormatString = "0.######" };
        if (tip != null) ToolTip.SetTip(row, tip);
        Grid.SetColumn(input, 1); row.Children.Add(input); (host ?? Fields).Children.Add(row); return input;
    }
    protected CheckBox Check(string id, string label, bool value, StackPanel? host = null)
    {
        var check = new CheckBox { Name = id, Content = label, IsChecked = value }; (host ?? Fields).Children.Add(check); return check;
    }
    protected static float Value(NumericUpDown input) => input.Value is { } value ? (float)value : throw new InvalidOperationException("Enter a value for every numeric field.");
    protected void TryApply(Action action)
    {
        try { action(); Error.IsVisible = false; }
        catch (Exception ex) { Error.Text = ex.Message; Error.IsVisible = true; }
    }
}

internal sealed class MassScaleWindow : PhysicsSettingsWindow
{
    public MassScaleWindow(int count, bool selected, Action<float> apply) : base("Scale particle mass", 300)
    {
        Fields.Children.Add(new TextBlock { Text = $"{count} {(selected ? "selected" : "total")} dynamic particle(s)" });
        var scale = Number("MassScale", "Scale", 1, .000001m, 1000000m, .1m, "Fixed particles are unchanged. Dynamic inverse mass is recalculated as 1 / mass.");
        Action("Cancel", () => Close(), true);
        Action("Apply", () => TryApply(() => { apply(Value(scale)); Close(); }));
    }
}

internal sealed class ClothSettingsWindow : PhysicsSettingsWindow
{
    public ClothSettingsWindow(ClothSimulationSettings settings, Action<ClothSimulationSettings> apply) : base("Cloth simulation settings", 660)
    {
        Heading("Simulation");
        var damping = Number("Damping", "Damping per second", settings.DampingPerSecond, 0, 1, .01m, "Native fraction of velocity removed per second, from 0 to 1.");
        var gx = Number("GravityX", "Gravity X", settings.GravityX, -100, 100);
        var gy = Number("GravityY", "Gravity Y", settings.GravityY, -100, 100);
        var gz = Number("GravityZ", "Gravity Z", settings.GravityZ, -100, 100);
        var tolerance = Number("CollisionTolerance", "Collision tolerance", settings.CollisionTolerance, 0, 10, .001m, "Native contact tolerance, separate from particle radius.");
        Heading("Motion transfer");
        var motion = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        var translationFields = new StackPanel { Spacing = 10 }; var rotationFields = new StackPanel { Spacing = 10 };
        motion.Children.Add(translationFields); Grid.SetColumn(rotationFields, 2); motion.Children.Add(rotationFields); Fields.Children.Add(motion);
        var translation = Check("TransferTranslation", "Translation", settings.TransferTranslationMotion, translationFields);
        var minTS = Number("MinTranslationSpeed", "Minimum speed", settings.MinTranslationSpeed, 0, 1000, host: translationFields);
        var maxTS = Number("MaxTranslationSpeed", "Maximum speed", settings.MaxTranslationSpeed, 0, 1000, host: translationFields);
        var minTB = Number("MinTranslationBlend", "Minimum blend", settings.MinTranslationBlend, 0, 1, host: translationFields);
        var maxTB = Number("MaxTranslationBlend", "Maximum blend", settings.MaxTranslationBlend, 0, 1, host: translationFields);
        var rotation = Check("TransferRotation", "Rotation", settings.TransferRotationMotion, rotationFields);
        var minRS = Number("MinRotationSpeed", "Minimum speed", settings.MinRotationSpeed, 0, 1000, host: rotationFields);
        var maxRS = Number("MaxRotationSpeed", "Maximum speed", settings.MaxRotationSpeed, 0, 1000, host: rotationFields);
        var minRB = Number("MinRotationBlend", "Minimum blend", settings.MinRotationBlend, 0, 1, host: rotationFields);
        var maxRB = Number("MaxRotationBlend", "Maximum blend", settings.MaxRotationBlend, 0, 1, host: rotationFields);
        Action("Cancel", () => Close(), true);
        Action("Apply", () => TryApply(() =>
        {
            apply(new ClothSimulationSettings
            {
                DampingPerSecond = Value(damping), GravityX = Value(gx), GravityY = Value(gy), GravityZ = Value(gz), CollisionTolerance = Value(tolerance),
                TransferTranslationMotion = translation.IsChecked == true, MinTranslationSpeed = Value(minTS), MaxTranslationSpeed = Value(maxTS), MinTranslationBlend = Value(minTB), MaxTranslationBlend = Value(maxTB),
                TransferRotationMotion = rotation.IsChecked == true, MinRotationSpeed = Value(minRS), MaxRotationSpeed = Value(maxRS), MinRotationBlend = Value(minRB), MaxRotationBlend = Value(maxRB)
            });
            Close();
        }));
    }
}

internal sealed class PreviewOptionsWindow : PhysicsSettingsWindow
{
    private NumericUpDown SliderNumber(string id, string label, float value, decimal min, decimal max, decimal increment, string? tip = null)
    {
        var input = Number(id, label, value, min, max, increment, tip);
        var row = (Grid)input.Parent!; row.ColumnDefinitions = new ColumnDefinitions("160,*,140"); Grid.SetColumn(input, 2);
        var slider = new Slider { Name = id + "Slider", Minimum = (double)min, Maximum = (double)max, Value = value,
            TickFrequency = (double)increment, IsSnapToTickEnabled = true, Height = 48, MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        input.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(slider, 1); row.Children.Add(slider);
        var syncing = false;
        slider.ValueChanged += (_, _) => { if (syncing) return; syncing = true; try { input.Value = (decimal)slider.Value; } finally { syncing = false; } };
        input.ValueChanged += (_, _) => { if (syncing || input.Value == null) return; syncing = true; try { slider.Value = (double)input.Value.Value; } finally { syncing = false; } };
        return input;
    }
    public PreviewOptionsWindow(PreviewOptions initial, Action<PreviewOptions> update) : base("Simulation options", 530)
    {
        var accepted = false;
        Heading("Wind direction");
        var direction = new ComboBox { Name = "WindDirection", ItemsSource = new[] { "Random", "+X", "-X", "+Y", "-Y", "+Z", "-Z" }, SelectedIndex = (int)initial.Direction, HorizontalAlignment = HorizontalAlignment.Stretch };
        Fields.Children.Add(direction);
        var wind = SliderNumber("WindSpeed", "Wind strength", initial.WindSpeed, 0, 50, .1m);
        var gust = SliderNumber("Gustiness", "Wind gustiness", initial.Gustiness, 0, 1, .01m);
        var gravity = SliderNumber("GravityScale", "Gravity scale", initial.GravityScale, 0, 3, .01m, "Preview multiplier only; does not change the cloth's saved gravity.");
        var speed = SliderNumber("PlaybackSpeed", "Playback speed", initial.PlaybackSpeed, .1m, 4, .01m);
        var passes = SliderNumber("SolverPasses", "Solver pass multiplier", initial.SolverPasses, 1, 24, 1);
        PreviewOptions Read() => new() { Direction = (WindAxis)direction.SelectedIndex, WindSpeed = Value(wind), Gustiness = Value(gust), GravityScale = Value(gravity), PlaybackSpeed = Value(speed), SolverPasses = ReadPasses() };
        int ReadPasses() { var value = Value(passes); if (value != MathF.Truncate(value)) throw new InvalidOperationException("Solver passes must be a whole number."); return (int)value; }
        void Changed() => TryApply(() => { var options = Read(); options.Validate(); update(options); });
        foreach (var input in new[] { wind, gust, gravity, speed, passes }) input.ValueChanged += (_, _) => Changed();
        direction.SelectionChanged += (_, _) => Changed();
        var reset = ViewportTools.Icon("RotateCcw", "Reset preview options", () =>
        {
            direction.SelectedIndex = 0; wind.Value = (decimal)HkclPreviewSimulator.DefaultWindSpeed;
            gust.Value = .35m; gravity.Value = speed.Value = passes.Value = 1;
        });
        Actions.Children.Add(reset);
        Action("Cancel", () => Close(), true);
        Action("Close", () => TryApply(() => { var options = Read(); options.Validate(); update(options); accepted = true; Close(); }));
        Closed += (_, _) => { if (!accepted) update(initial); };
    }
}
