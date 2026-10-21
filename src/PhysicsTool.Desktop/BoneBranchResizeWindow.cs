using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Numerics;

namespace PhysicsTool.Desktop;

internal sealed class BoneBranchResizeWindow : Window
{
    private readonly DispatcherTimer _delay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly NumericUpDown[] _size = new NumericUpDown[3];
    private readonly CheckBox _colliders;
    private readonly Button _apply;
    private readonly TextBlock _error;
    private readonly Action<Vector3, bool> _preview;
    public BoneBranchResizeWindow(string boneName, Vector3 initialScale, Action<Vector3, bool> preview)
    {
        _preview = preview;
        Title = "Resize bone branch"; Width = 360; SizeToContent = SizeToContent.Height;
        CanResize = false; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = boneName, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        for (int i = 0; i < 3; i++)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*") };
            row.Children.Add(new TextBlock { Text = "Scale " + "XYZ"[i], Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            _size[i] = new NumericUpDown { Name = "BranchResize" + "XYZ"[i], Minimum = .001m, Maximum = 1000m, Increment = .01m,
                Value = (decimal)(i == 0 ? initialScale.X : i == 1 ? initialScale.Y : initialScale.Z), FormatString = "0.000" };
            Grid.SetColumn(_size[i], 1); row.Children.Add(_size[i]); panel.Children.Add(row);
        }
        _colliders = new CheckBox { Name = "BranchResizeColliders", Content = "Resize attached colliders", IsChecked = true };
        ToolTip.SetTip(_colliders, "Resize collision shapes bound to this branch. Other body colliders are not resized.");
        panel.Children.Add(_colliders);
        _error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.OrangeRed, IsVisible = false }; panel.Children.Add(_error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var reset = new Button { Content = "Reset" }; reset.Click += (_, _) => { foreach (var input in _size) input.Value = 1; PreviewNow(); };
        var cancel = new Button { Content = "Cancel", IsCancel = true }; cancel.Click += (_, _) => Close(false);
        _apply = new Button { Name = "ApplyBranchResize", Content = "Apply", IsDefault = true }; _apply.Click += (_, _) => { if (PreviewNow()) Close(true); };
        actions.Children.Add(reset); actions.Children.Add(cancel); actions.Children.Add(_apply); panel.Children.Add(actions); Content = panel;
        void Queue() { _apply.IsEnabled = false; _delay.Stop(); _delay.Start(); }
        foreach (var input in _size) input.ValueChanged += (_, _) => Queue();
        _colliders.IsCheckedChanged += (_, _) => Queue();
        _delay.Tick += (_, _) => PreviewNow(); Closed += (_, _) => _delay.Stop();
        Opened += (_, _) => PreviewNow();
    }
    internal bool PreviewNow()
    {
        _delay.Stop();
        try
        {
            if (_size.Any(input => input.Value == null)) throw new InvalidOperationException("Enter a scale factor.");
            _preview(new Vector3((float)_size[0].Value!.Value, (float)_size[1].Value!.Value, (float)_size[2].Value!.Value), _colliders.IsChecked == true);
            _error.IsVisible = false; _apply.IsEnabled = true; return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.InvalidDataException)
        { _error.Text = ex.Message; _error.IsVisible = true; _apply.IsEnabled = false; return false; }
    }
}
