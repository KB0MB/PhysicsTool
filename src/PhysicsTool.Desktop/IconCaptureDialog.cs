using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

internal sealed class IconCaptureDialog : Window
{
    private readonly IconImageFormat[] _formats;
    private readonly ComboBox _format;
    private readonly ComboBox _size;
    private readonly CheckBox _allDyes;

    internal IconImageFormat Format => _formats[Math.Max(0, _format.SelectedIndex)];
    internal bool Native => Format == IconImageFormat.Sbitemico;
    internal bool ToMod { get; private set; }
    internal int CaptureSize => (int)(_size.SelectedItem ?? 256);
    internal bool ExportAllDyes => _allDyes.IsEnabled && _allDyes.IsChecked == true;

    internal IconCaptureDialog(bool nativeAvailable, bool dyesAvailable, int size = 256, bool bntxAvailable = false, IconImageFormat lastFormat = IconImageFormat.Sbitemico)
    {
        Title = "Capture icon";
        Width = 520; SizeToContent = SizeToContent.Height;
        CanResize = false; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _formats = Enum.GetValues<IconImageFormat>().Where(f => f == IconImageFormat.Png || f == IconImageFormat.Sbitemico && nativeAvailable || f is IconImageFormat.Bntx or IconImageFormat.BntxZs && bntxAvailable).ToArray();
        _format = new ComboBox
        {
            Name = "IconExportFormat", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = _formats.Select(f => f.Label()).ToArray(), SelectedIndex = Math.Max(0, Array.IndexOf(_formats, lastFormat))
        };
        _allDyes = new CheckBox { Name = "IconExportAllDyes", Content = "Export all Dyes", IsEnabled = dyesAvailable };
        _size = new ComboBox
        {
            Name = "IconCaptureSize", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { 144, 256, 512, 576, 1024, 2048 }, SelectedItem = size
        };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Format" });
        panel.Children.Add(_format);
        panel.Children.Add(new TextBlock { Text = "Size" });
        panel.Children.Add(_size);
        panel.Children.Add(_allDyes);
        var actions = new Grid { ColumnDefinitions = new("*,*,*"), ColumnSpacing = 8 };
        Button Action(string name, string text, int column)
        {
            var button = new Button { Name = name, Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(10, 10) };
            Grid.SetColumn(button, column); actions.Children.Add(button); return button;
        }
        var cancel = Action("CancelIconCapture", "Cancel", 0); cancel.IsCancel = true;
        var folder = Action("FolderIconCapture", "Export to Folder", 1);
        var capture = Action("ConfirmIconCapture", "Export to Mod", 2); capture.IsDefault = true;
        cancel.Click += (_, _) => Close(false);
        folder.Click += (_, _) => { ToMod = false; Close(true); };
        capture.Click += (_, _) => { ToMod = true; Close(true); };
        panel.Children.Add(actions);
        Content = panel;
        Opened += (_, _) => _format.Focus();
    }
}
