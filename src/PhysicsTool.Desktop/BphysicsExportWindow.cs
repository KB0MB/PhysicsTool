using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using System.Globalization;

namespace PhysicsTool.Desktop;

internal sealed class BphysicsExportWindow : PhysicsSettingsWindow
{
    private sealed record Row(int Index, ComboBox Bone, NumericUpDown Frequency, NumericUpDown Drag,
        NumericUpDown Min, NumericUpDown Max, NumericUpDown Main, NumericUpDown Add, CheckBox Enabled,
        CheckBox Local, TextBlock Reaction, ComboBox Sample, Button UseFrequency);
    private readonly HkclService _source;
    private readonly BphysicsDocument _document;
    private readonly List<Row> _rows = [];
    private TotkClothReactionProfiles? _reactions;
    private readonly TextBlock _importStatus = new() { Name = "ReactionImportStatus", TextWrapping = TextWrapping.Wrap };

    public BphysicsExportWindow(HkclService source, string name) : base("BPHYSICS export", 780)
    {
        Width = 760; MinWidth = 620; _source = source;
        var parts = name.Split('_'); var folder = parts.Length > 2 ? string.Join('_', parts.Take(2)) : name;
        _document = source.CreateBphysicsDocument(folder + "/" + name + ".hkcl");
        var path = Text("HKCL game path", _document.HkclPath);
        var support = Check("UseSupportBone", "Use support bone sidecar", false);
        var supportPath = Text("BPHYSSB game path", folder + "/" + name + ".bphyssb"); supportPath.IsEnabled = false;
        support.IsCheckedChanged += (_, _) => supportPath.IsEnabled = support.IsChecked == true;
        Heading("TotK cloth reactions");
        var import = new Button { Content = "Read TotK actor...", Name = "ImportClothReactions" }; Fields.Children.Add(import);
        Fields.Children.Add(_importStatus);
        Fields.Children.Add(new TextBlock { Text = "TotK reactions vary with speed. BotW stores one wind profile per cloth. Matched base bones are copied; a sampled frequency can be used as a starting point. Drag, wind limits and sub-wind still need in-game tuning.", TextWrapping = TextWrapping.Wrap });
        import.Click += async (_, _) =>
        {
            if (!import.IsEnabled) return;
            import.IsEnabled = false;
            try
            {
                var browser = new ActorBrowserWindow(clothReactionsOnly: true);
                var actor = await browser.ShowDialog<BotwActorCatalogEntry?>(this);
                if (actor == null) return;
                var roots = browser.ResourceRoots.Prepend(actor.ResourceRoot).Distinct().ToArray();
                var profiles = await Task.Run(() => TotkClothReactionProfiles.Load(actor.Path, roots));
                Import(profiles); Error.IsVisible = false;
            }
            catch (Exception ex) { Error.Text = UserFacingExceptionFormatter.Format(ex); Error.IsVisible = true; }
            finally { import.IsEnabled = true; }
        };
        Heading("Cloths");
        for (var i = 0; i < _document.Cloths.Count; i++)
        {
            var p = _document.Cloths[i]; var fields = new StackPanel { Spacing = 8, Margin = new Thickness(8) };
            var expander = new Expander { Header = p.Name, Content = fields, IsExpanded = i == 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            Fields.Children.Add(expander);
            var bones = source.GetBoneRows(i).Select(b => TotkClothReactionProfiles.NormalizeName(b.Name)).Append(p.BaseBone).Distinct().ToArray();
            var bone = new ComboBox { ItemsSource = bones, SelectedItem = p.BaseBone, HorizontalAlignment = HorizontalAlignment.Stretch }; fields.Children.Add(bone);
            ToolTip.SetTip(bone, "Base bone");
            NumericUpDown N(string label, float value, decimal min = 0) => Number("Bphysics" + label.Replace(" ", "") + i, label, value, min, 100000, host: fields);
            var freq = N("Wind frequency", p.WindFrequency); var drag = N("Wind drag", p.WindDrag);
            var min = N("Minimum wind speed", p.WindMinSpeed, -100000); var max = N("Maximum wind speed", p.WindMaxSpeed, -100000);
            var main = N("Main sub-wind factor", p.SubWindFactorMain, -100000); var add = N("Additional sub-wind factor", p.SubWindFactorAdd, -100000);
            var enabled = Check("WindEnabled" + i, "Wind enabled", p.WindEnabled, fields);
            var local = Check("WritebackToLocal" + i, "Write back to local space", p.WritebackToLocal, fields);
            var reaction = new TextBlock { TextWrapping = TextWrapping.Wrap }; fields.Children.Add(reaction);
            var sample = new ComboBox { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch }; fields.Children.Add(sample);
            var use = new Button { Content = "Use reaction frequency", IsVisible = false }; fields.Children.Add(use);
            var row = new Row(i, bone, freq, drag, min, max, main, add, enabled, local, reaction, sample, use); _rows.Add(row);
            sample.SelectionChanged += (_, _) => UpdateReaction(row);
            use.Click += (_, _) => { if (SelectedSample(row)?.Frequency is { } f && f >= 0 && f <= 100000) row.Frequency.Value = (decimal)f; };
        }
        Heading("Shared sub-wind");
        var dx = Number("SubWindX", "Direction X", _document.SubWindDirection.X, -1, 1);
        var dy = Number("SubWindY", "Direction Y", _document.SubWindDirection.Y, -1, 1);
        var dz = Number("SubWindZ", "Direction Z", _document.SubWindDirection.Z, -1, 1);
        var frequency = Number("SubWindFrequency", "Frequency", _document.SubWindFrequency, 0, 100000);
        var speed = Number("SubWindSpeed", "Speed", _document.SubWindSpeed, 0, 100000);
        Action("Cancel", () => Close(), true);
        Action("Export", () => TryApply(() =>
        {
            if (string.IsNullOrWhiteSpace(path.Text) || support.IsChecked == true && string.IsNullOrWhiteSpace(supportPath.Text))
                throw new InvalidOperationException("Enter the game paths for the enabled sidecars.");
            _document.HkclPath = path.Text.Trim(); _document.UseSupportBone = support.IsChecked == true; _document.SupportBonePath = supportPath.Text?.Trim();
            foreach (var r in _rows)
                _document.Cloths[r.Index] = _document.Cloths[r.Index] with { BaseBone = (string)r.Bone.SelectedItem!,
                    WindFrequency = Value(r.Frequency), WindDrag = Value(r.Drag), WindMinSpeed = Value(r.Min), WindMaxSpeed = Value(r.Max),
                    SubWindFactorMain = Value(r.Main), SubWindFactorAdd = Value(r.Add), WindEnabled = r.Enabled.IsChecked == true, WritebackToLocal = r.Local.IsChecked == true };
            _document.SubWindDirection = new(Value(dx), Value(dy), Value(dz)); _document.SubWindFrequency = Value(frequency); _document.SubWindSpeed = Value(speed);
            BphysicsService.ValidateDocument(_document); Close(_document);
        }));
    }
    private TextBox Text(string label, string value)
    {
        Fields.Children.Add(new TextBlock { Text = label }); var box = new TextBox { Text = value }; Fields.Children.Add(box); return box;
    }
    internal void Import(TotkClothReactionProfiles profiles)
    {
        _reactions = profiles;
        var matched = 0; var notes = new List<string>(profiles.Warnings);
        foreach (var row in _rows)
        {
            var name = _document.Cloths[row.Index].Name;
            row.Sample.ItemsSource = null; row.UseFrequency.IsVisible = row.Sample.IsVisible = false;
            if (!profiles.Cloths.TryGetValue(name, out var reaction)) { row.Reaction.Text = "No matching TotK reaction."; continue; }
            matched++;
            if (_source.GetBoneRows(row.Index).Any(b => TotkClothReactionProfiles.NormalizeName(b.Name) == reaction.BaseBone)) row.Bone.SelectedItem = reaction.BaseBone;
            else notes.Add(name + ": base bone is absent from the loaded cloth; its current binding was kept.");
            var labels = new List<string> { "Base reaction" };
            labels.AddRange(reaction.Reactions.Select((_, i) => "Reaction " + (i + 1)));
            if (reaction.UseStrongWind == true) labels.Add("Strong wind");
            row.Sample.ItemsSource = labels; row.Sample.IsVisible = row.UseFrequency.IsVisible = true;
            row.Sample.SelectedIndex = reaction.Reactions.Count > 0 ? Math.Min(2, reaction.Reactions.Count) : 0;
            UpdateReaction(row);
        }
        _importStatus.Text = $"Matched {matched} of {_rows.Count} cloths." + (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "");
    }
    private TotkReactionSample? SelectedSample(Row row)
    {
        if (_reactions == null || !_reactions.Cloths.TryGetValue(_document.Cloths[row.Index].Name, out var p)) return null;
        var index = row.Sample.SelectedIndex;
        return index == 0 ? p.Base : index > 0 && index <= p.Reactions.Count ? p.Reactions[index - 1]
            : index == p.Reactions.Count + 1 && p.UseStrongWind == true ? p.Strong : null;
    }
    private void UpdateReaction(Row row)
    {
        if (SelectedSample(row) is not { } sample) return;
        static string F(float? v) => v?.ToString("G6", CultureInfo.InvariantCulture) ?? "not authored";
        var profile = _reactions!.Cloths[_document.Cloths[row.Index].Name];
        row.Reaction.Text = $"{profile.Resource}\nFrequency: {F(sample.Frequency)}   Drag force: {F(sample.DragForce)}\nSwing factor: {F(sample.SwingFactor)}   Reaction speed: {F(sample.ReactionSpeed)}\nSub-wind: {sample.SubWind?.ToString() ?? "not authored"}";
        row.UseFrequency.IsEnabled = sample.Frequency is >= 0 and <= 100000;
    }
}
