using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

namespace PhysicsTool.Desktop;

internal sealed partial class ShrineSceneView
{
    private sealed record ActorDestination(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileName(Path).Contains("_Dynamic.", StringComparison.Ordinal)
            ? "Dynamic" : System.IO.Path.GetFileName(Path).Contains("_Static.", StringComparison.Ordinal) ? "Static" : System.IO.Path.GetFileName(Path);
    }

    private async Task AddActorAsync()
    {
        if (Busy || _editing == null) return;
        var editing = _editing;
        if (!editing.PlacementOnly)
        {
            if (!editing.CanChangeEditMode)
            { _status.Text = "Save and reopen this scene in Placement only mode before adding actors."; return; }
            if (!await Dialogs.Confirm(_owner, "Add actor placement", "Switch to Placement only? New actors will be saved in the BANC. Baked collision will stay unchanged.")) return;
            editing.SetPlacementOnly(true);
            _changingEditMode = true; _placementOnly.IsChecked = true; _collisionNotice.IsVisible = true; _changingEditMode = false;
        }
        var folder = Path.Combine(editing.ResourceRoot, "Pack", "Actor");
        var names = await Task.Run(() => Directory.EnumerateFiles(folder, "*.pack.zs")
            .Select(p => Path.GetFileName(p)[..^8])
            .Where(n => !n.StartsWith("GroundControlActor", StringComparison.Ordinal) && !n.StartsWith("FldObj_Tar_MergeParts_", StringComparison.Ordinal))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray());
        var window = new Window { Title = "Add Actor", Width = 650, Height = 680, MinWidth = 440, MinHeight = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Icon = _owner.Icon };
        var panel = new DockPanel { Margin = new Thickness(18) };
        var top = new StackPanel { Spacing = 10 };
        var destinations = editing.AdditionSources.Select(p => new ActorDestination(p)).OrderBy(p => p.ToString() == "Static" ? 0 : 1).ToArray();
        var destination = new ComboBox { ItemsSource = destinations, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var search = new TextBox { PlaceholderText = "Search actors" };
        top.Children.Add(destination); top.Children.Add(search);
        DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var list = new ListBox { ItemsSource = names, Margin = new Thickness(0, 12) };
        var bottom = new StackPanel { Spacing = 10 };
        var position = SelectedActors().FirstOrDefault()?.Position ?? _view.FlyPosition - _view.ViewDirection * 5;
        var coordinates = new Grid { ColumnDefinitions = new("*,*,*") };
        var numbers = new List<NumericUpDown>();
        foreach (var (label, value) in new[] { ("X", position.X), ("Y", position.Y), ("Z", position.Z) })
        {
            var field = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 8, 0) };
            field.Children.Add(new TextBlock { Text = label });
            var number = new NumericUpDown { Value = (decimal)value, ShowButtonSpinner = false, FormatString = "0.###" };
            numbers.Add(number); field.Children.Add(number); Grid.SetColumn(field, numbers.Count - 1); coordinates.Children.Add(field);
        }
        bottom.Children.Add(coordinates);
        var notice = new TextBlock { Text = "Collision unchanged", TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(notice);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel" }; cancel.Click += (_, _) => window.Close(); actions.Children.Add(cancel);
        var add = new Button { Content = "Add", IsEnabled = false }; actions.Children.Add(add); bottom.Children.Add(actions);
        DockPanel.SetDock(bottom, Dock.Bottom); panel.Children.Add(bottom); panel.Children.Add(list); window.Content = panel;
        search.TextChanged += (_, _) => list.ItemsSource = names.Where(n => n.Contains(search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        list.SelectionChanged += (_, _) => add.IsEnabled = list.SelectedItem is string && destination.SelectedItem != null;
        bool loading = false;
        window.Closing += (_, e) => { if (loading) e.Cancel = true; };
        add.Click += async (_, _) =>
        {
            if (loading || list.SelectedItem is not string name || destination.SelectedItem is not ActorDestination target) return;
            if (numbers.Any(n => n.Value == null)) { notice.Text = "Enter X, Y and Z coordinates."; return; }
            loading = true; add.IsEnabled = cancel.IsEnabled = list.IsEnabled = search.IsEnabled = destination.IsEnabled = false;
            try
            {
                _view.FinishTransform(false);
                var warnings = new List<string>();
                VisualModelData model;
                try { model = await Task.Run(() => ShrineSceneAssets.LoadModel(Path.Combine(folder, name + ".pack.zs"), editing.ResourceRoot, new(), warnings, CancellationToken.None)); }
                catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
                { model = new(); warnings.Add(ex.Message); }
                int index = editing.AddActor(name, target.Path, new Vector3((float)numbers[0].Value!.Value, (float)numbers[1].Value!.Value, (float)numbers[2].Value!.Value), model);
                _search.Text = ""; _groundOnly.IsChecked = false;
                RefreshEdit(); SetSelection([index]);
                _status.Text = $"Added {name} to {target}. Collision unchanged." + (model.Meshes.Count == 0 ? " Model unavailable; enable Actor origins to see its placement." : "");
                ToolTip.SetTip(_status, string.Join("\n", warnings));
                loading = false; window.Close();
            }
            catch (Exception ex) { notice.Text = ex.Message; }
            finally { loading = false; add.IsEnabled = cancel.IsEnabled = list.IsEnabled = search.IsEnabled = destination.IsEnabled = true; }
        };
        await window.ShowDialog(_owner);
    }
}
