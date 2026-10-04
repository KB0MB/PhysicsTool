using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using HKCLTool;
using System.Text.Json;

namespace PhysicsTool.Desktop;

internal sealed class ActorBrowserWindow : Window
{
    private readonly TextBox _root = new() { PlaceholderText = "Game resource folder", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _search = new() { PlaceholderText = "Search actors" };
    private readonly ListBox _actors = new();
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Button _scan, _open;
    private IReadOnlyList<BotwActorCatalogEntry> _entries = [];
    private readonly string _settings = Path.Combine(AppContext.BaseDirectory, "actor-folders.json");
    internal string[] ResourceRoots { get; private set; } = [];
    internal ActorBrowserWindow()
    {
        Title = "Actors with cloth or helper bones"; Width = 800; Height = 620; MinWidth = 560; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new Grid { RowDefinitions = new("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(16), RowSpacing = 12 };
        var folders = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        folders.Children.Add(_root);
        var browse = new Button { Content = "Browse" }; Grid.SetColumn(browse, 1); folders.Children.Add(browse);
        _scan = new Button { Content = "Scan" }; Grid.SetColumn(_scan, 2); folders.Children.Add(_scan);
        browse.Click += async (_, _) =>
        {
            var result = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Game resource folder", AllowMultiple = false });
            if (result.FirstOrDefault()?.TryGetLocalPath() is { } path) { _root.Text = path; await Scan(); }
        };
        _scan.Click += async (_, _) => await Scan(); layout.Children.Add(folders);
        Grid.SetRow(_search, 1); layout.Children.Add(_search);
        _actors.ItemTemplate = new FuncDataTemplate<BotwActorCatalogEntry>((entry, _) => new TextBlock
        {
            Text = entry == null ? "" : $"{entry.Name}  |  {entry.Platform}  |  {(entry.HasCloth ? "Cloth" : "")}{(entry.HasCloth && entry.HasSupport ? " + " : "")}{(entry.HasSupport ? "Helper bones" : "")}  |  {entry.Source}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        Grid.SetRow(_actors, 2); layout.Children.Add(_actors); Grid.SetRow(_status, 3); layout.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var close = new Button { Content = "Close", IsCancel = true }; close.Click += (_, _) => Close();
        _open = new Button { Content = "Open actor", IsEnabled = false }; _open.Click += (_, _) => Choose();
        actions.Children.Add(close); actions.Children.Add(_open); Grid.SetRow(actions, 4); layout.Children.Add(actions);
        Content = layout;
        _actors.DoubleTapped += (_, _) => Choose();
        _actors.SelectionChanged += (_, _) => _open.IsEnabled = _actors.SelectedItem != null && _scan.IsEnabled;
        _search.TextChanged += (_, _) => Filter();
        try { ResourceRoots = File.Exists(_settings) ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(_settings)) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        _root.Text = ResourceRoots.FirstOrDefault();
        Opened += async (_, _) => { if (Directory.Exists(_root.Text)) await Scan(); };
        Closed += (_, _) => _cancellation.Cancel();
    }
    private void Choose() { if (_open.IsEnabled && _actors.SelectedItem is BotwActorCatalogEntry entry) Close(entry); }
    private void Filter()
    {
        _actors.ItemsSource = _entries.Where(entry => entry.Name.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        _status.Text = $"{_actors.ItemCount} actors";
    }
    private async Task Scan()
    {
        if (!_scan.IsEnabled || !Directory.Exists(_root.Text)) { _status.Text = "Select an available game resource folder."; return; }
        _scan.IsEnabled = _open.IsEnabled = false; _status.Text = "Scanning actor packs...";
        try
        {
            ResourceRoots = new[] { Path.GetFullPath(_root.Text!) }.Concat(ResourceRoots).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var progress = new Progress<string>(message => { if (!_cancellation.IsCancellationRequested) _status.Text = "Scanning " + message; });
            var result = await Task.Run(() => new BotwActorCatalog().Scan(ResourceRoots, progress, _cancellation.Token));
            if (_cancellation.IsCancellationRequested) return;
            _entries = result.Actors; Filter();
            if (result.Warnings.Count > 0) _status.Text += $" ({result.Warnings.Count} resource warnings)";
            try { AtomicFile.WriteAllBytes(_settings, JsonSerializer.SerializeToUtf8Bytes(ResourceRoots)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text += " Folder settings could not be saved."; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _status.Text = UserFacingExceptionFormatter.Format(ex); }
        finally { _scan.IsEnabled = true; _open.IsEnabled = _actors.SelectedItem != null; }
    }
}
