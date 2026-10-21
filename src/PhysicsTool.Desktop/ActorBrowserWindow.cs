using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using HKCLTool;
using System.Text.Json;

namespace PhysicsTool.Desktop;

internal sealed class ActorBrowserPaths
{
    public string Botw { get; set; } = "";
    public string Totk { get; set; } = "";
    public bool TotkSelected { get; set; }

    internal static ActorBrowserPaths Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            if (json.RootElement.ValueKind != JsonValueKind.Array)
                return json.RootElement.Deserialize<ActorBrowserPaths>() ?? new();
            var result = new ActorBrowserPaths();
            foreach (var folder in json.RootElement.Deserialize<string[]>() ?? [])
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                if (File.Exists(Path.Combine(folder, "Pack", "ZsDic.pack.zs")) ||
                    File.Exists(Path.Combine(folder, "romfs", "Pack", "ZsDic.pack.zs")))
                { if (result.Totk.Length == 0) result.Totk = folder; }
                else if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.bfres").Any() &&
                    !Directory.Exists(Path.Combine(folder, "Actor")))
                { continue; }
                else if (result.Botw.Length == 0) result.Botw = folder;
            }
            result.TotkSelected = result.Botw.Length == 0 && result.Totk.Length > 0;
            return result;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { return new(); }
    }
}

internal sealed class ActorBrowserWindow : Window
{
    private sealed class GameTab(bool totk)
    {
        public bool Totk { get; } = totk;
        public TextBox Root { get; } = new() { Name = totk ? "TotkActorRoot" : "BotwActorRoot", PlaceholderText = totk ? "TOTK romfs folder" : "BOTW game resource folder", HorizontalAlignment = HorizontalAlignment.Stretch };
        public TextBox Search { get; } = new() { Name = totk ? "TotkActorSearch" : "BotwActorSearch", PlaceholderText = "Search actors" };
        public ListBox Actors { get; } = new() { Name = totk ? "TotkActors" : "BotwActors" };
        public TextBlock Status { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        public Button Scan { get; } = new() { Content = "Scan" };
        public Button Browse { get; } = new() { Content = "Browse" };
        public Button Open { get; } = new() { Content = "Open actor", IsEnabled = false };
        public IReadOnlyList<BotwActorCatalogEntry> Entries { get; set; } = [];
        public bool Busy { get; set; }
        public bool Attempted { get; set; }
        public int WarningCount { get; set; }
        public string[] ResourceRoots => new[] { Root.Text }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private readonly GameTab _botw = new(false), _totk = new(true);
    private readonly TabControl _tabs = new() { Name = "ActorGameTabs" };
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _settings;
    private readonly bool _clothReactionsOnly;
    private readonly Func<string[], bool, IProgress<string>, CancellationToken, Task<BotwActorCatalogResult>> _scan;
    private bool _opened;
    private GameTab Selected => _clothReactionsOnly || _tabs.SelectedIndex == 1 ? _totk : _botw;
    internal string[] ResourceRoots => Selected.ResourceRoots;

    internal ActorBrowserWindow(string? settingsPath = null,
        Func<string[], bool, IProgress<string>, CancellationToken, Task<BotwActorCatalogResult>>? scan = null,
        bool clothReactionsOnly = false)
    {
        _clothReactionsOnly = clothReactionsOnly;
        _settings = settingsPath ?? AppDataPaths.SettingsFile("actor-folders.json");
        _scan = scan ?? ((roots, totk, progress, token) => Task.Run(() => new BotwActorCatalog(
            AppDataPaths.CacheFile(totk ? "actor-catalog-totk.json" : "actor-catalog-botw.json")).Scan(roots, progress, token), token));
        var paths = ActorBrowserPaths.Read(_settings);
        _botw.Root.Text = paths.Botw; _totk.Root.Text = paths.Totk;
        Title = "Actors with cloth or helper bones"; Width = 800; Height = 660; MinWidth = 560; MinHeight = 420;
        if (_clothReactionsOnly) { Title = "TotK cloth reactions"; _totk.Open.Content = "Read TotK actor..."; }
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (!_clothReactionsOnly) _tabs.Items.Add(new TabItem { Header = "BOTW", Content = Build(_botw) });
        _tabs.Items.Add(new TabItem { Header = "TOTK", Content = Build(_totk) });
        _tabs.SelectedIndex = !_clothReactionsOnly && paths.TotkSelected ? 1 : 0;
        _tabs.Margin = new Thickness(16, 8, 16, 16); Content = _tabs;
        _tabs.SelectionChanged += async (_, e) => { if (e.Source == _tabs && _opened) await ScanIfNeeded(Selected); };
        Opened += async (_, _) => { _opened = true; await ScanIfNeeded(Selected); };
        Closed += (_, _) => { _cancellation.Cancel(); SavePaths(Selected); };
    }

    private Control Build(GameTab tab)
    {
        var layout = new Grid { RowDefinitions = new("Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 12, 0, 0), RowSpacing = 12 };
        var folders = new StackPanel { Spacing = 8 };
        var row = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 8 };
        row.Children.Add(tab.Root); Grid.SetColumn(tab.Browse, 1); row.Children.Add(tab.Browse);
        Grid.SetColumn(tab.Scan, 2); row.Children.Add(tab.Scan); folders.Children.Add(row);
        ToolTip.SetTip(tab.Root, tab.Totk ? "TOTK game resources, including Pack/Actor and Pack/ZsDic.pack.zs." : "BOTW game resources. Both Wii U and Switch dumps are supported.");
        layout.Children.Add(folders);
        tab.Browse.Click += async (_, _) =>
        {
            var path = await PickFolder(tab.Totk ? "TOTK romfs folder" : "BOTW game resource folder");
            if (path != null) { tab.Root.Text = path; await Scan(tab); }
        };
        tab.Scan.Click += async (_, _) => await Scan(tab);
        tab.Root.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBox.TextProperty) return;
            tab.Attempted = false; tab.Entries = []; tab.WarningCount = 0; tab.Actors.SelectedItem = null; Filter(tab);
        };
        Grid.SetRow(tab.Search, 1); layout.Children.Add(tab.Search);
        tab.Actors.ItemTemplate = new FuncDataTemplate<BotwActorCatalogEntry>((entry, _) => new TextBlock
        {
            Text = entry == null ? "" : $"{entry.Name}  |  {entry.Platform}  |  {(entry.HasCloth ? "Cloth" : "")}{(entry.HasCloth && entry.HasSupport ? " + " : "")}{(entry.HasSupport ? "Helper bones" : "")}  |  {entry.Source}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        Grid.SetRow(tab.Actors, 2); layout.Children.Add(tab.Actors);
        Grid.SetRow(tab.Status, 3); layout.Children.Add(tab.Status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var close = new Button { Content = "Close", IsCancel = true }; close.Click += (_, _) => Close();
        tab.Open.Click += (_, _) => Choose(tab); actions.Children.Add(close); actions.Children.Add(tab.Open);
        Grid.SetRow(actions, 4); layout.Children.Add(actions);
        tab.Actors.DoubleTapped += (_, _) => Choose(tab);
        tab.Actors.SelectionChanged += (_, _) => tab.Open.IsEnabled = tab.Actors.SelectedItem != null && !tab.Busy;
        tab.Search.TextChanged += (_, _) => Filter(tab);
        return layout;
    }

    private async Task<string?> PickFolder(string title)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new() { Title = title, AllowMultiple = false });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }
    private void Choose(GameTab tab)
    {
        if (tab == Selected && tab.Open.IsEnabled && tab.Actors.SelectedItem is BotwActorCatalogEntry entry) Close(entry);
    }
    private void Filter(GameTab tab)
    {
        var selected = tab.Actors.SelectedItem as BotwActorCatalogEntry;
        var entries = tab.Entries.Where(entry => entry.Name.Contains(tab.Search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        tab.Actors.ItemsSource = entries;
        if (selected != null && entries.Contains(selected)) tab.Actors.SelectedItem = selected;
        if (!tab.Busy) tab.Status.Text = $"{tab.Actors.ItemCount} actors" + (tab.WarningCount > 0 ? $" ({tab.WarningCount} resource warnings)" : "");
    }
    private void SavePaths(GameTab tab)
    {
        try
        {
            var paths = ActorBrowserPaths.Read(_settings);
            paths.Totk = _totk.Root.Text?.Trim() ?? "";
            if (!_clothReactionsOnly) { paths.Botw = _botw.Root.Text?.Trim() ?? ""; paths.TotkSelected = Selected.Totk; }
            AtomicFile.WriteAllBytes(_settings, JsonSerializer.SerializeToUtf8Bytes(paths));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { tab.Status.Text += " Folder settings could not be saved."; }
    }
    private Task ScanIfNeeded(GameTab tab) => !tab.Attempted && Directory.Exists(tab.Root.Text?.Trim()) ? Scan(tab) : Task.CompletedTask;
    private async Task Scan(GameTab tab)
    {
        if (tab.Busy || _cancellation.IsCancellationRequested) return;
        if (!Directory.Exists(tab.Root.Text?.Trim())) { tab.Status.Text = "Select an available game resource folder."; return; }
        tab.Busy = tab.Attempted = true;
        tab.Scan.IsEnabled = tab.Browse.IsEnabled = tab.Root.IsEnabled = tab.Open.IsEnabled = false;
        tab.Status.Text = "Scanning actor packs...";
        try
        {
            var root = Path.GetFullPath(tab.Root.Text!.Trim());
            var progress = new Progress<string>(message => { if (!_cancellation.IsCancellationRequested && tab.Busy) tab.Status.Text = "Scanning " + message; });
            var result = await _scan([root], tab.Totk, progress, _cancellation.Token);
            if (_cancellation.IsCancellationRequested) return;
            tab.Entries = result.Actors.Where(a => a.IsTotk == tab.Totk && (!_clothReactionsOnly || a.HasCloth)).ToArray();
            tab.WarningCount = result.Warnings.Count; tab.Busy = false; Filter(tab); SavePaths(tab);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { tab.Status.Text = UserFacingExceptionFormatter.Format(ex); }
        finally
        {
            tab.Busy = false; tab.Scan.IsEnabled = tab.Browse.IsEnabled = tab.Root.IsEnabled = true;
            tab.Open.IsEnabled = tab.Actors.SelectedItem != null;
        }
    }
}
