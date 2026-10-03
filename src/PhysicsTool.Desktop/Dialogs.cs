using Avalonia;
using Avalonia.Controls;
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
        panel.Children.Add(new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var window = Window(title, panel); var cancel = new Button { Content = "Cancel" }; var yes = new Button { Content = "Confirm" };
        cancel.Click += (_, _) => window.Close(false); yes.Click += (_, _) => window.Close(true);
        buttons.Children.Add(cancel); buttons.Children.Add(yes); panel.Children.Add(buttons); return await window.ShowDialog<bool>(owner);
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
        var a = Number("Particle A", selected.FirstOrDefault(), particles.Count - 1, 0);
        var b = Number("Particle B", selected.Length > 1 ? selected[1] : (a.Value == 0 ? 1 : 0), particles.Count - 1, 0);
        var length = Number("Rest / maximum length", 0, 1000000); var min = Number("Minimum length", 0, 1000000);
        var stiffness = Number("Stiffness (native)", 0, 1000000); var stretch = Number("Stretch stiffness", 0, 1000000);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSalmon }; panel.Children.Add(error);
        var add = new Button { Content = "Add", HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(add);
        var window = Window("Add cloth constraint", panel, 560);
        void Validate()
        {
            var k = (ClothConstraintKind)kind.SelectedItem!;
            error.Text = !k.CanCreate ? "Inspect-only: creation is not supported." : a.Value == b.Value ? "Choose different particles." : length.Value is null or <= 0 ? "Length must be positive." : min.IsEnabled && min.Value > length.Value ? "Minimum exceeds maximum." : "";
            add.IsEnabled = error.Text.Length == 0;
        }
        void Defaults()
        {
            var k = (ClothConstraintKind)kind.SelectedItem!; description.Text = k.Description;
            var pa = particles[(int)(a.Value ?? 0)]; var pb = particles[(int)(b.Value ?? 0)];
            length.Value = (decimal)Vector3.Distance(new(pa.X, pa.Y, pa.Z), new(pb.X, pb.Y, pb.Z)); min.Value = length.Value;
            var inv = (pa.Fixed ? 0 : pa.InverseMass) + (pb.Fixed ? 0 : pb.InverseMass);
            stiffness.Value = k.ClassName == "hclStretchLinkConstraintSet" || inv <= 0 ? 1 : (decimal)(1 / inv); stretch.Value = stiffness.Value;
            min.IsEnabled = stretch.IsEnabled = k.ClassName == "hclBendLinkConstraintSet"; Validate();
        }
        kind.SelectionChanged += (_, _) => Defaults(); a.ValueChanged += (_, _) => Defaults(); b.ValueChanged += (_, _) => Defaults();
        length.ValueChanged += (_, _) => Validate(); min.ValueChanged += (_, _) => Validate();
        add.Click += (_, _) => window.Close((new[] { (int)(a.Value ?? 0), (int)(b.Value ?? 0) }, new ClothLinkOptions(((ClothConstraintKind)kind.SelectedItem!).ClassName,
            (float)(length.Value ?? 0), (float)(stiffness.Value ?? 0), min.IsEnabled ? (float)(min.Value ?? 0) : 0, stretch.IsEnabled ? (float)(stretch.Value ?? 0) : 0)));
        Defaults(); return await window.ShowDialog<(int[], ClothLinkOptions)?>(owner);
    }
}

public sealed class DocumentationWindow : Window
{
    public DocumentationWindow()
    {
        Title = "Cloth documentation"; Width = 1000; Height = 700; MinWidth = 740; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#303435"));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), ColumnDefinitions = new ColumnDefinitions("260,*"), Margin = new Thickness(16) };
        var search = new TextBox { PlaceholderText = "Search documentation", Margin = new Thickness(0, 0, 0, 12) }; Grid.SetColumnSpan(search, 2); layout.Children.Add(search);
        var list = new ListBox(); Grid.SetRow(list, 1); layout.Children.Add(list);
        var heading = new TextBlock { FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 24 };
        var article = new StackPanel { Margin = new Thickness(24, 10), Spacing = 22 }; article.Children.Add(heading); article.Children.Add(body);
        var scroll = new ScrollViewer { Content = article }; Grid.SetColumn(scroll, 1); Grid.SetRow(scroll, 1); layout.Children.Add(scroll); Content = layout;
        List<ClothHelpTopic> matches = new();
        void Filter()
        {
            var terms = (search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            matches = ClothHelp.Topics.Where(t => terms.All(s => (t.Title + " " + t.Body).Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
            list.ItemsSource = matches.Select(t => t.Title).ToArray(); list.SelectedIndex = matches.Count > 0 ? 0 : -1;
            if (matches.Count == 0) { heading.Text = "No matching topics"; body.Text = ""; }
        }
        list.SelectionChanged += (_, _) => { if (list.SelectedIndex < 0 || list.SelectedIndex >= matches.Count) return; var topic = matches[list.SelectedIndex]; heading.Text = topic.Title; body.Text = topic.Body; scroll.Offset = default; };
        search.TextChanged += (_, _) => Filter(); Filter();
    }
}
