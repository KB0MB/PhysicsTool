using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using System.Numerics;

namespace PhysicsTool.Desktop;

internal sealed class MatchModelBonesWindow : Window
{
    internal sealed record Choice(int Index, string Name, VisualModelBone? Target);

    private sealed record BoneRow(Choice Choice, CheckBox Check, Grid Panel)
    {
        public bool IsSelected => Check.IsEnabled && Check.IsChecked == true;
    }

    private readonly List<BoneRow> _rows = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
    private readonly Button _match = new() { Content = "Match selected bones", Name = "ApplyModelBoneMatch", IsDefault = true };

    internal static Choice[] Choices(IEnumerable<BonePreviewPoint> bones, VisualModelData model)
    {
        static string UnqualifiedName(string value) => value[(value.LastIndexOf(':') + 1)..];

        var targets = model.Bones
            .GroupBy(bone => UnqualifiedName(bone.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return bones.Select(bone => new Choice(bone.Index, bone.Name,
            targets.GetValueOrDefault(UnqualifiedName(bone.Name)))).ToArray();
    }

    internal static Matrix4x4 Matrix(VisualModelBone bone) =>
        Matrix4x4.CreateScale(bone.Scale) *
        Matrix4x4.CreateFromQuaternion(bone.Rotation) *
        Matrix4x4.CreateTranslation(bone.Position);

    internal MatchModelBonesWindow(Choice[] choices, Action<IReadOnlyDictionary<int, Matrix4x4>> apply)
    {
        Title = "Match bones to model";
        Width = 650;
        Height = 580;
        MinWidth = 460;
        MinHeight = 300;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var layout = new Grid
        {
            RowDefinitions = new("Auto,Auto,*,Auto,Auto"),
            Margin = new Thickness(16),
            RowSpacing = 12
        };
        layout.Children.Add(CreateSearchBar());

        var headings = CreateHeadings();
        Grid.SetRow(headings, 1);
        layout.Children.Add(headings);

        var list = new StackPanel { Spacing = 4 };
        foreach (var choice in choices)
        {
            var row = CreateBoneRow(choice);
            _rows.Add(row);
            list.Children.Add(row.Panel);
        }

        var scroll = new ScrollViewer { Content = list };
        Grid.SetRow(scroll, 2);
        layout.Children.Add(scroll);
        Grid.SetRow(_error, 3);
        layout.Children.Add(_error);

        var actions = CreateActions(apply);
        Grid.SetRow(actions, 4);
        layout.Children.Add(actions);
        Content = layout;
        UpdateSelection();
    }

    private Grid CreateSearchBar()
    {
        var bar = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        var search = new TextBox { PlaceholderText = "Search bones" };
        var all = new Button { Content = "Select all" };
        var none = new Button { Content = "Select none" };
        Grid.SetColumn(all, 1);
        Grid.SetColumn(none, 2);
        bar.Children.Add(search);
        bar.Children.Add(all);
        bar.Children.Add(none);

        search.TextChanged += (_, _) => FilterRows(search.Text ?? "");
        all.Click += (_, _) => SelectVisibleRows(true);
        none.Click += (_, _) => SelectVisibleRows(false);
        return bar;
    }

    private static Grid CreateHeadings()
    {
        var headings = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 12 };
        headings.Children.Add(new TextBlock { Text = "Cloth bone", FontWeight = FontWeight.SemiBold });
        var modelHeading = new TextBlock { Text = "Model bone", FontWeight = FontWeight.SemiBold };
        Grid.SetColumn(modelHeading, 1);
        headings.Children.Add(modelHeading);
        return headings;
    }

    private BoneRow CreateBoneRow(Choice choice)
    {
        var panel = new Grid { ColumnDefinitions = new("*,*"), ColumnSpacing = 12 };
        var check = new CheckBox
        {
            Content = new TextBlock { Text = choice.Name, TextWrapping = TextWrapping.Wrap },
            IsEnabled = choice.Target != null,
            IsChecked = choice.Target != null,
            Name = "MatchBone" + choice.Index
        };
        check.IsCheckedChanged += (_, _) => UpdateSelection();
        panel.Children.Add(check);

        var target = new TextBlock
        {
            Text = choice.Target?.Name ?? "No matching model bone",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(target, 1);
        panel.Children.Add(target);
        if (choice.Target is { } bone)
            ToolTip.SetTip(target, $"X: {bone.Position.X:G6}   Y: {bone.Position.Y:G6}   Z: {bone.Position.Z:G6}");

        return new BoneRow(choice, check, panel);
    }

    private StackPanel CreateActions(Action<IReadOnlyDictionary<int, Matrix4x4>> apply)
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();
        _match.Click += (_, _) => ApplySelection(apply);
        ToolTip.SetTip(_match, "Copy model rest poses to the selected cloth bones and reshape particles, constraints and attached colliders. The BFRES is not changed. Undo restores the cloth. HKCL only.");
        actions.Children.Add(cancel);
        actions.Children.Add(_match);
        return actions;
    }

    private void UpdateSelection() => _match.IsEnabled = _rows.Any(row => row.IsSelected);

    private void FilterRows(string search)
    {
        foreach (var row in _rows)
            row.Panel.IsVisible = row.Choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void SelectVisibleRows(bool selected)
    {
        foreach (var row in _rows.Where(row => row.Panel.IsVisible))
        {
            if (!selected || row.Check.IsEnabled)
                row.Check.IsChecked = selected;
        }
    }

    private void ApplySelection(Action<IReadOnlyDictionary<int, Matrix4x4>> apply)
    {
        try
        {
            var targets = _rows.Where(row => row.IsSelected)
                .ToDictionary(row => row.Choice.Index, row => Matrix(row.Choice.Target!));
            apply(targets);
            Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.InvalidDataException or NotSupportedException)
        {
            _error.Text = ex.Message;
        }
    }
}
