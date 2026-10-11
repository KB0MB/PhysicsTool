using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;
using System.Globalization;

namespace PhysicsTool.Desktop;

internal sealed class ConversionWindow : Window
{
    private readonly List<(BphclConversionScale Source, TextBox Mass, TextBox Constraint, TextBlock Diagnostics)> _rows = [];
    private readonly ComboBox _platform = new() { Name = "ConversionPlatform", ItemsSource = new[] { "Wii U", "Switch" }, SelectedIndex = 0, MinWidth = 150 };
    private readonly Button _convert = new() { Name = "ConfirmConversion", Content = "Continue", IsDefault = true };
    private readonly TextBlock _error = new() { Name = "ConversionValidation", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
    public ConversionWindow(PhysicsConversion conversion, HkclPlatform platform)
    {
        Title = "Convert to " + conversion.Format; Width = conversion.Scales.Count > 0 ? 860 : 560; Height = conversion.Scales.Count > 0 ? 540 : 280;
        MinWidth = conversion.Scales.Count > 0 ? 760 : 460; MinHeight = 260; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") }; Content = root;
        var heading = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 0, 16) }; root.Children.Add(heading);
        heading.Children.Add(new TextBlock { Text = conversion.Target switch {
            PhysicsConversionTarget.Hkcl => "BPHCL to HKCL", PhysicsConversionTarget.Bphcl => "HKCL to BPHCL",
            PhysicsConversionTarget.Bphyssb => "BPHHB to BPHYSSB", _ => "BPHYSSB to BPHHB" }, FontSize = 22 });
        var body = new StackPanel { Spacing = 14 }; var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        if (conversion.Target == PhysicsConversionTarget.Hkcl)
        {
            var platformRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            platformRow.Children.Add(new TextBlock { Text = "Target platform", VerticalAlignment = VerticalAlignment.Center });
            _platform.SelectedIndex = platform == HkclPlatform.Switch ? 1 : 0; platformRow.Children.Add(_platform); heading.Children.Add(platformRow);
            body.Children.Add(new TextBlock { Text = "Measured profiles where available; unmatched cloths retain source values.\nMass and constraint scales are independent. All cloths will be converted.", TextWrapping = TextWrapping.Wrap });
            var table = new StackPanel { Spacing = 8 }; body.Children.Add(table);
            Grid Row() => new() { ColumnDefinitions = new ColumnDefinitions("*,105,105,170"), ColumnSpacing = 10 };
            var header = Row(); table.Children.Add(header);
            string[] titles = ["Cloth / profile", "Mass scale", "Constraint scale", "Output mass / max inverse"];
            for (var i = 0; i < titles.Length; i++) { var label = new TextBlock { Text = titles[i], TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(label, i); header.Children.Add(label); }
            foreach (var s in conversion.Scales)
            {
                var row = Row(); table.Children.Add(row);
                var name = new TextBlock { Text = $"{s.ClothIndex}: {s.ClothName}\n{s.SuggestionBasis}", TextWrapping = TextWrapping.Wrap };
                ToolTip.SetTip(name, $"{s.DynamicParticleCount} dynamic particles; source mass {s.SourceDynamicMass:G7}"); row.Children.Add(name);
                TextBox Input(string id, float value, int column)
                {
                    var box = new TextBox { Name = id + s.ClothIndex, Text = value.ToString("G9", CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Top };
                    Grid.SetColumn(box, column); row.Children.Add(box); return box;
                }
                var mass = Input("ConversionMass", s.DefaultMassScale, 1); var constraint = Input("ConversionConstraint", s.DefaultConstraintScale, 2);
                ToolTip.SetTip(mass, "Multiplies dynamic particle mass and regenerates inverse mass. Fixed anchors remain fixed.");
                ToolTip.SetTip(constraint, "Independent multiplier for supported soft standard/bend coefficients. Local-range settings retain their native conversion policy.");
                var diagnostics = new TextBlock { Name = "ConversionDiagnostics" + s.ClothIndex, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(diagnostics, 3); row.Children.Add(diagnostics);
                _rows.Add((s, mass, constraint, diagnostics)); mass.TextChanged += (_, _) => Validate(); constraint.TextChanged += (_, _) => Validate();
            }
            var defaults = new Button { Content = "Use suggested scales", Name = "ConversionDefaults" }; body.Children.Add(defaults);
            defaults.Click += (_, _) => { foreach (var row in _rows) { row.Mass.Text = row.Source.DefaultMassScale.ToString("G9", CultureInfo.InvariantCulture); row.Constraint.Text = row.Source.DefaultConstraintScale.ToString("G9", CultureInfo.InvariantCulture); } };
        }
        else body.Children.Add(new TextBlock { Text = conversion.Target == PhysicsConversionTarget.Bphcl
            ? "All cloths will be written to a new native BPHCL file. No donor or template is required."
            : conversion.Target == PhysicsConversionTarget.Bphhb
                ? "The support graph will be converted to TotK helper bones. Parent-space references use the loaded model skeleton when required. Unsupported graph connections are rejected."
                : "The helper graph will be converted to a BotW support-bone sidecar. Unsupported graph layouts are rejected.", TextWrapping = TextWrapping.Wrap });
        if (conversion.FidelityWarnings.Count > 0)
            body.Children.Add(new TextBlock { Name = "ConversionFidelityWarnings",
                Text = string.Join("\n\n", conversion.FidelityWarnings), TextWrapping = TextWrapping.Wrap });
        Grid.SetRow(_error, 2); _error.Margin = new Thickness(0, 12); root.Children.Add(_error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Grid.SetRow(actions, 3); root.Children.Add(actions);
        var cancel = new Button { Content = "Cancel", IsCancel = true }; actions.Children.Add(cancel); actions.Children.Add(_convert);
        cancel.Click += (_, _) => Close();
        _convert.Click += (_, _) => { Validate(); if (_convert.IsEnabled) Close(new PhysicsConversionOptions(_platform.SelectedIndex == 1 ? HkclPlatform.Switch : HkclPlatform.WiiU,
            _rows.ToDictionary(r => r.Source.ClothIndex, r => Parse(r.Mass.Text)), _rows.ToDictionary(r => r.Source.ClothIndex, r => Parse(r.Constraint.Text)))); };
        Validate();
    }
    private static float Parse(string? text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : float.NaN;
    private void Validate()
    {
        var valid = true;
        foreach (var r in _rows)
        {
            var mass = Parse(r.Mass.Text); var constraint = Parse(r.Constraint.Text);
            var output = r.Source.SourceDynamicMass * mass;
            var inverse = r.Source.SuggestedMaxInverseMass * (r.Source.DefaultMassScale / mass);
            var rowValid = float.IsFinite(mass) && mass > 0 && float.IsFinite(constraint) && constraint > 0 && float.IsFinite(output) && float.IsFinite(inverse);
            valid &= rowValid; r.Diagnostics.Text = rowValid ? $"{output:G7}\n{inverse:G7}" : "Invalid scale";
        }
        _convert.IsEnabled = valid; _error.Text = valid ? "" : "Enter positive finite scales for every cloth (decimal point: .).";
    }
}
