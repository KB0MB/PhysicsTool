using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;

namespace PhysicsTool.Desktop;

internal static class MeshVisibilityDialog
{
    internal static async Task Show(Window owner, PhysicsViewport viewport, Action<int, bool>? change = null, Func<string, bool>? isProp = null)
    {
        if (viewport.Model == null) return;
        await Create(viewport, change, isProp).ShowDialog(owner);
    }

    internal static Window Create(PhysicsViewport viewport, Action<int, bool>? change = null, Func<string, bool>? isProp = null)
    {
        var model = viewport.Model ?? throw new InvalidOperationException("No model loaded.");
        var window = new Window { Title = "Meshes", Width = 520, SizeToContent = SizeToContent.Height,
            CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Control List(bool? props)
        {
            var panel = new StackPanel { Spacing = 4, Margin = new Thickness(12) };
            for (int i = 0; i < model.Meshes.Count; i++)
            {
                if (props != null && isProp!(model.Meshes[i].Name) != props) continue;
                var index = i;
                var name = model.Meshes[i].Name;
                if (props == true && name.StartsWith("Props / ", StringComparison.Ordinal)) name = name[8..];
                var check = new CheckBox { Tag = index, Padding = new Thickness(10, 7), HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap }, IsChecked = !viewport.HiddenMeshes.Contains(i) };
                check.IsCheckedChanged += (_, _) =>
                {
                    if (change != null) change(index, check.IsChecked == true);
                    else viewport.SetMeshVisible(index, check.IsChecked == true);
                };
                panel.Children.Add(check);
            }
            return new ScrollViewer { Content = panel, MaxHeight = 480, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        }
        var content = new StackPanel();
        content.Children.Add(isProp == null ? List(null) : new TabControl
        {
            Name = "ArmorMeshTabs", Margin = new Thickness(8, 8, 8, 0),
            ItemsSource = new[] { new TabItem { Header = "Armor", Content = List(false) }, new TabItem { Header = "Props", Content = List(true) } }, SelectedIndex = 0
        });
        var close = new Button { Content = "Close", IsCancel = true, Padding = new Thickness(18, 8), Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => window.Close(); content.Children.Add(close); window.Content = content;
        return window;
    }
}
