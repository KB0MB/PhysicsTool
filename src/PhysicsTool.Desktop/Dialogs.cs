using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;
using System.Numerics;

namespace PhysicsTool.Desktop;

internal static class Dialogs
{
    private static Window Window(string title, Control content, double width = 460) => new()
    {
        Title = title, Content = content, Width = width, SizeToContent = SizeToContent.Height,
        CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
    };
    public static async Task Show(Window owner, string title, StackPanel panel)
    {
        var window = Window(title, panel); var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close(); panel.Children.Add(close); await window.ShowDialog(owner);
    }
    public static async Task<bool> Confirm(Window owner, string title, string question)
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 18 };
        panel.Children.Add(new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap } });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var window = Window(title, panel); var cancel = new Button { Content = "Cancel", IsCancel = true }; var yes = new Button { Content = "Confirm", IsDefault = true };
        cancel.Click += (_, _) => window.Close(false); yes.Click += (_, _) => window.Close(true);
        buttons.Children.Add(cancel); buttons.Children.Add(yes); panel.Children.Add(buttons); return await window.ShowDialog<bool>(owner);
    }
    public static async Task<string?> Prompt(Window owner, string title, string label, string value, string action = "Rename")
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        var input = new TextBox { Text = value };
        panel.Children.Add(new TextBlock { Text = label }); panel.Children.Add(input);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var accept = new Button { Content = action, IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        actions.Children.Add(cancel); actions.Children.Add(accept); panel.Children.Add(actions);
        var window = Window(title, panel);
        input.TextChanged += (_, _) => accept.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
        accept.Click += (_, _) => window.Close(input.Text!.Trim()); cancel.Click += (_, _) => window.Close();
        window.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return await window.ShowDialog<string?>(owner);
    }
    public static async Task<(string Name, int Format)?> NewCloth(Window owner)
    {
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        var name = new TextBox { Text = "NewCloth", PlaceholderText = "Name" };
        var format = new ComboBox { ItemsSource = new[] { "BotW Wii U (.hkcl)", "BotW Switch (.hkcl)", "TotK (.bphcl)" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(new TextBlock { Text = "Name" }); panel.Children.Add(name); panel.Children.Add(format);
        panel.Children.Add(new TextBlock { Text = "Unbound draft", Foreground = Brushes.LightSteelBlue });
        var create = new Button { Content = "Create", HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(create);
        var window = Window("New cloth", panel);
        name.TextChanged += (_, _) => create.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
        create.Click += (_, _) => window.Close(((name.Text ?? "NewCloth").Trim(), format.SelectedIndex));
        return await window.ShowDialog<(string, int)?>(owner);
    }
    public static async Task<(int[] Indices, ClothLinkOptions Options)?> Constraint(Window owner, IReadOnlyList<ParticleEditRow> particles, int[] selected)
    {
        if (selected.Length != 2 || selected[0] == selected[1] || selected.Any(i => !particles.Any(p => p.Index == i)))
            throw new ArgumentException("Select two different particles before adding a constraint.", nameof(selected));
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        var kind = new ComboBox { ItemsSource = ClothConstraintCatalog.Kinds, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var description = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 480, Foreground = Brushes.LightSteelBlue };
        panel.Children.Add(kind); panel.Children.Add(description);
        NumericUpDown Number(string label, decimal value, decimal maximum, int decimals = 6)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*") };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var number = new NumericUpDown { Value = value, Minimum = 0, Maximum = maximum, FormatString = decimals == 0 ? "0" : "0.######", Increment = decimals == 0 ? 1 : .01m };
            Grid.SetColumn(number, 1); row.Children.Add(number); panel.Children.Add(row); return number;
        }
        var a = Number("Particle A", selected[0], particles.Count - 1, 0); a.Name = "ConstraintParticleA";
        var b = Number("Particle B", selected[1], particles.Count - 1, 0); b.Name = "ConstraintParticleB";
        var length = Number("Rest / maximum length", 0, 1000000); var min = Number("Minimum length", 0, 1000000);
        var stiffness = Number("Stiffness (native)", 0, 1000000); var stretch = Number("Stretch stiffness", 0, 1000000);
        stiffness.Name = "ConstraintStiffness";
        var error = new TextBlock { Name = "ConstraintValidation", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSalmon }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        var cancel = new Button { Content = "Cancel", IsCancel = true }; buttons.Children.Add(cancel);
        var add = new Button { Content = "Add", IsDefault = true }; buttons.Children.Add(add);
        var window = Window("Add cloth constraint", panel, 560);
        cancel.Click += (_, _) => window.Close();
        ParticleEditRow? Particle(NumericUpDown input) => input.Value is { } value && value == decimal.Truncate(value)
            ? particles.FirstOrDefault(p => p.Index == value) : null;
        void Validate()
        {
            var k = (ClothConstraintKind)kind.SelectedItem!;
            var pa = Particle(a); var pb = Particle(b);
            var invalid = !k.CanCreate ? "Inspect-only: creation is not supported." : pa == null || pb == null ? "Choose valid whole-number particle indices."
                : a.Value == b.Value ? "Choose different particles." : length.Value is null or <= 0 ? "Length must be positive."
                : stiffness.Value == null || stretch.IsEnabled && stretch.Value == null ? "Enter a stiffness value."
                : min.IsEnabled && (min.Value == null || min.Value > length.Value) ? "Minimum must be specified and cannot exceed maximum." : "";
            add.IsEnabled = invalid.Length == 0;
            error.Text = invalid.Length > 0 ? invalid
                : pa!.Fixed && pb!.Fixed ? "Both particles are fixed, so this constraint will not move them."
                : k.ClassName == "hclStretchLinkConstraintSet" && pb!.Fixed ? "Particle B is fixed. This directed constraint cannot move it."
                : stiffness.Value == 0 && (!stretch.IsEnabled || stretch.Value == 0) ? "Zero stiffness: this constraint will have no effect." : "";
        }
        void Defaults()
        {
            var k = (ClothConstraintKind)kind.SelectedItem!; description.Text = k.Description;
            min.IsEnabled = stretch.IsEnabled = k.ClassName == "hclBendLinkConstraintSet";
            var pa = Particle(a); var pb = Particle(b);
            if (pa == null || pb == null) { Validate(); return; }
            length.Value = (decimal)Vector3.Distance(new(pa.X, pa.Y, pa.Z), new(pb.X, pb.Y, pb.Z)); min.Value = length.Value;
            var inv = (pa.Fixed ? 0 : pa.InverseMass) + (pb.Fixed ? 0 : pb.InverseMass);
            stiffness.Value = k.ClassName == "hclStretchLinkConstraintSet" || inv <= 0 ? 1 : (decimal)(1 / inv); stretch.Value = stiffness.Value;
            Validate();
        }
        kind.SelectionChanged += (_, _) => Defaults(); a.ValueChanged += (_, _) => Defaults(); b.ValueChanged += (_, _) => Defaults();
        length.ValueChanged += (_, _) => Validate(); min.ValueChanged += (_, _) => Validate();
        stiffness.ValueChanged += (_, _) => Validate(); stretch.ValueChanged += (_, _) => Validate();
        add.Click += (_, _) => window.Close((new[] { (int)(a.Value ?? 0), (int)(b.Value ?? 0) }, new ClothLinkOptions(((ClothConstraintKind)kind.SelectedItem!).ClassName,
            (float)(length.Value ?? 0), (float)(stiffness.Value ?? 0), min.IsEnabled ? (float)(min.Value ?? 0) : 0, stretch.IsEnabled ? (float)(stretch.Value ?? 0) : 0)));
        Defaults(); return await window.ShowDialog<(int[], ClothLinkOptions)?>(owner);
    }
}

public sealed class DocumentationWindow : Window
{
    public DocumentationWindow()
    {
        Title = "Documentation"; Width = 1000; Height = 700; MinWidth = 740; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), ColumnDefinitions = new ColumnDefinitions("260,*"), Margin = new Thickness(16) };
        var search = new TextBox { Name = "DocumentationSearch", PlaceholderText = "Search documentation", Margin = new Thickness(0, 0, 0, 12) }; Grid.SetColumnSpan(search, 2); layout.Children.Add(search);
        var list = new ListBox { Name = "DocumentationTopics",
            ItemTemplate = new FuncDataTemplate<ClothHelpTopic>((topic, _) => new TextBlock { Text = topic?.Title, TextWrapping = TextWrapping.Wrap }) };
        Grid.SetRow(list, 1); layout.Children.Add(list);
        var heading = new TextBlock { Name = "DocumentationHeading", FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var body = new TextBlock { Name = "DocumentationBody", TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 24 };
        var article = new StackPanel { Margin = new Thickness(24, 10), Spacing = 22 }; article.Children.Add(heading); article.Children.Add(body);
        var scroll = new ScrollViewer { Content = article }; Grid.SetColumn(scroll, 1); Grid.SetRow(scroll, 1); layout.Children.Add(scroll); Content = layout;
        List<ClothHelpTopic> matches = new();
        void Filter()
        {
            var previous = list.SelectedIndex >= 0 && list.SelectedIndex < matches.Count ? matches[list.SelectedIndex].Title : null;
            var terms = (search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            matches = ClothHelp.Topics.Where(t => terms.All(s => (UiLanguage.Text(t.Title) + " " + UiLanguage.Text(t.Body) + " " + t.Title).Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
            list.ItemsSource = matches;
            list.SelectedIndex = matches.Count > 0 ? Math.Max(0, matches.FindIndex(t => t.Title == previous)) : -1;
            if (matches.Count > 0) { heading.Text = matches[list.SelectedIndex].Title; body.Text = matches[list.SelectedIndex].Body; scroll.Offset = default; }
            if (matches.Count == 0) { heading.Text = "No matching topics"; body.Text = ""; }
        }
        list.SelectionChanged += (_, _) => { if (list.SelectedIndex < 0 || list.SelectedIndex >= matches.Count) return; var topic = matches[list.SelectedIndex]; heading.Text = topic.Title; body.Text = topic.Body; scroll.Offset = default; };
        search.TextChanged += (_, _) => Filter(); Filter();
        UiLanguage.Changed += Filter;
        Closed += (_, _) => UiLanguage.Changed -= Filter;
    }
}
