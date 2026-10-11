using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private int _helperCurveIndex = -1;
    private bool _helperCurvesExpanded;
    private void ShowHelperCurves()
    {
        var curves = Session.Document.GetHelperCurves(Session.ClothIndex);
        var panel = new StackPanel { Spacing = 8 };
        AddNewHelperCurve(panel);
        var selector = new ComboBox { Name = "HelperCurve", ItemsSource = curves, HorizontalAlignment = HorizontalAlignment.Stretch };
        selector.ItemTemplate = new FuncDataTemplate<SidecarCurveRow>((curve, _) => new TextBlock { Text = curve?.ToString(), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(selector);
        var fields = new StackPanel { Spacing = 8 }; panel.Children.Add(fields);
        var expander = new Expander { Name = "HelperCurves", Header = $"Response curves ({curves.Count})", Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = _helperCurvesExpanded };
        expander.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _helperCurvesExpanded = expander.IsExpanded; };
        ToolTip.SetTip(expander, "Curves map an input bone's movement to a helper response. Editing a shared curve affects every connection that uses it. Create a new curve for an independent response.");
        _details.Children.Add(expander);
        selector.SelectionChanged += (_, _) => {
            fields.Children.Clear(); if (selector.SelectedItem is not SidecarCurveRow curve) return;
            _helperCurveIndex = curve.Index;
            if (!curve.Editable) { fields.Children.Add(new TextBlock { Text = "This curve has an unsupported key layout.", TextWrapping = TextWrapping.Wrap }); return; }
            var plot = new HelperCurvePlot { Name = "HelperCurvePlot", Height = 180, Keys = curve.Keys };
            ToolTip.SetTip(plot, "Input runs left to right; output runs bottom to top. Gold points are authored keys. The plotted Hermite response covers the key range.");
            fields.Children.Add(plot);
            var extent = new TextBlock { Name = "HelperCurveRange", TextWrapping = TextWrapping.Wrap }; fields.Children.Add(extent);
            var presets = new ComboBox { Name = "HelperCurvePreset", ItemsSource = new[] { "Linear", "Smooth", "Zero response" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            fields.Children.Add(presets);
            var setPreset = new Button { Name = "HelperCurveSetPreset", Content = "Set response", HorizontalAlignment = HorizontalAlignment.Stretch }; fields.Children.Add(setPreset);
            ToolTip.SetTip(setPreset, "Change the draft curve, keeping its input positions. Apply curve commits the edit; undo restores it.");
            var inputs = new List<NumericUpDown[]>();
            for (var i = 0; i < curve.Keys.Count; i++)
            {
                fields.Children.Add(new TextBlock { Text = $"Key {i}", FontWeight = FontWeight.SemiBold });
                var k = curve.Keys[i]; var values = new[] { k.X, k.Y, k.Z, k.W }; var labels = new[] { "Input", "Output", "In tangent", "Out tangent" };
                var row = values.Select((v, c) => DetailNumber(fields, $"HelperKey{i}_{c}", labels[c], v)).ToArray();
                foreach (var input in row) input.IsEnabled = curve.Editable;
                ToolTip.SetTip(row[0], "Driver attribute value; angular attributes use radians.");
                ToolTip.SetTip(row[2], "Incoming derivative (output change per input unit)."); ToolTip.SetTip(row[3], "Outgoing derivative (output change per input unit).");
                inputs.Add(row);
            }
            Vector4[] ReadKeys() => inputs.Select(k => new Vector4(DetailValue(k[0]), DetailValue(k[1]), DetailValue(k[2]), DetailValue(k[3]))).ToArray();
            void Redraw()
            {
                try
                {
                    plot.Keys = ReadKeys(); plot.InvalidateVisual();
                    extent.Text = $"Input {plot.Keys.Min(k => k.X):G4} to {plot.Keys.Max(k => k.X):G4}  |  Key outputs {plot.Keys.Min(k => k.Y):G4} to {plot.Keys.Max(k => k.Y):G4}";
                }
                catch (ArgumentException) { }
            }
            foreach (var input in inputs.SelectMany(k => k)) input.ValueChanged += (_, _) => Redraw();
            setPreset.Click += (_, _) => {
                try
                {
                    var keys = ReadKeys();
                    float Slope(int a, int b) => keys[a].X == keys[b].X ? 0 : (keys[b].Y - keys[a].Y) / (keys[b].X - keys[a].X);
                    var draft = keys.Select((k, i) => new Vector4(k.X, presets.SelectedIndex == 2 ? 0 : k.Y,
                        presets.SelectedIndex == 0 ? Slope(Math.Max(0, i - 1), i) : 0,
                        presets.SelectedIndex == 0 ? Slope(i, Math.Min(keys.Length - 1, i + 1)) : 0)).ToArray();
                    if (draft.Any(k => !float.IsFinite(k.Z) || !float.IsFinite(k.W) || Math.Abs(k.Z) > 1000000 || Math.Abs(k.W) > 1000000))
                        throw new ArgumentException("These key inputs are too close for a bounded linear tangent.");
                    for (var i = 0; i < inputs.Count; i++)
                    {
                        inputs[i][1].Value = (decimal)draft[i].Y; inputs[i][2].Value = (decimal)draft[i].Z; inputs[i][3].Value = (decimal)draft[i].W;
                    }
                    Redraw();
                }
                catch (ArgumentException ex) { _status.Text = ex.Message; }
            };
            var gain = DetailNumber(fields, "HelperCurveGain", "Response scale", 1);
            var scaleResponse = new Button { Name = "HelperScaleResponse", Content = "Scale response", HorizontalAlignment = HorizontalAlignment.Stretch }; fields.Children.Add(scaleResponse);
            ToolTip.SetTip(scaleResponse, "Multiply outputs and both tangents together. Negative values reverse the response; zero disables it. Input positions and connections are unchanged.");
            scaleResponse.Click += (_, _) => {
                if (gain.Value is not { } factor) return;
                try
                {
                    var scaled = inputs.Select(k => new[] { k[1].Value * factor, k[2].Value * factor, k[3].Value * factor }).ToArray();
                    if (scaled.SelectMany(k => k).Any(v => !v.HasValue || v < gain.Minimum || v > gain.Maximum))
                    { _status.Text = "Response scale exceeds the supported field range."; return; }
                    for (var i = 0; i < inputs.Count; i++) for (var c = 1; c < 4; c++) inputs[i][c].Value = scaled[i][c - 1];
                }
                catch (OverflowException) { _status.Text = "Response scale is too large."; }
                Redraw();
            };
            Redraw();
            var error = new TextBlock { Name = "HelperCurveError", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed }; fields.Children.Add(error);
            var apply = new Button { Name = "ApplyHelperCurve", Content = "Apply curve", IsEnabled = curve.Editable, HorizontalAlignment = HorizontalAlignment.Stretch }; fields.Children.Add(apply);
            apply.Click += (_, _) => {
                try
                {
                    if (!CanEditHelper) throw new InvalidOperationException("Pause playback before editing curves.");
                    var keys = ReadKeys();
                    Edit((d, c) => d.UpdateHelperCurve(c, curve.Index, keys));
                }
                catch (Exception ex) { error.Text = ex.Message; _status.Text = ex.Message; }
            };
        };
        selector.SelectedItem = curves.FirstOrDefault(c => c.Index == _helperCurveIndex) ?? curves.FirstOrDefault();
    }
}
