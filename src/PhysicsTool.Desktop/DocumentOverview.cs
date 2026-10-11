using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

public sealed class DocumentOverview : UserControl
{
    private readonly ListBox _current = new(), _donorList = new(), _bones = new();
    private readonly TextBlock _currentFile = FileLabel(), _donorFile = FileLabel(), _bonesTitle = Heading("Bones");
    private readonly TextBlock _currentEmpty = Empty("No physics file loaded"), _donorEmpty = Empty("No donor file loaded"), _bonesEmpty = Empty("No bones selected");
    private readonly Button _remove = new() { Content = "Remove selected" }, _duplicate = new() { Content = "Duplicate selected" }, _merge = new() { Content = "Merge selected (M)" }, _clear = new() { Content = "Clear donor" };
    private readonly Button _openDonor = new() { Name = "OpenDonor", Content = "Open Donor" };
    internal Button SwapButton { get; } = new() { Name = "SwapFiles", Content = "Swap files" };
    private readonly MenuItem _renameMenu = new() { Header = "Rename" }, _duplicateMenu = new() { Header = "Duplicate" }, _removeMenu = new() { Header = "Remove" };
    private EditorSession? _session;
    private EditorSession? _donorSession;
    private bool _refreshing;
    public event Action<int>? ClothSelected;
    public event Action? EditRequested;
    public event Action? RemoveRequested;
    public event Action? DuplicateRequested;
    public event Action? RenameRequested;
    public event Action? MergeRequested;
    public event Action? ClearDonorRequested;
    public event Action? OpenDonorRequested;
    public event Action? SwapRequested;

    public DocumentOverview()
    {
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,340"), Margin = new Thickness(12, 4, 12, 12) };
        layout.ColumnDefinitions[0].MinWidth = 340;
        layout.ColumnDefinitions[2].MinWidth = 260;
        var files = new Grid { RowDefinitions = new RowDefinitions("*,5,*") };
        var currentActions = new WrapPanel();
        _remove.Margin = _duplicate.Margin = new Thickness(0, 0, 8, 4);
        _remove.Click += (_, _) => RemoveRequested?.Invoke(); currentActions.Children.Add(_remove);
        _duplicate.Click += (_, _) => DuplicateRequested?.Invoke(); currentActions.Children.Add(_duplicate);
        files.Children.Add(Section("Current file", _currentFile, _current, _currentEmpty, currentActions));
        var rowSplit = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(rowSplit, 1); files.Children.Add(rowSplit);
        var donorActions = new WrapPanel();
        _openDonor.Margin = SwapButton.Margin = new Thickness(0, 0, 8, 4);
        _openDonor.Click += (_, _) => OpenDonorRequested?.Invoke(); donorActions.Children.Add(_openDonor);
        _merge.Margin = _clear.Margin = new Thickness(0, 0, 8, 4);
        _merge.Click += (_, _) => MergeRequested?.Invoke(); donorActions.Children.Add(_merge);
        _clear.Click += (_, _) => ClearDonorRequested?.Invoke(); donorActions.Children.Add(_clear);
        SwapButton.Click += (_, _) => SwapRequested?.Invoke(); donorActions.Children.Add(SwapButton);
        var donor = Section("Donor file", _donorFile, _donorList, _donorEmpty, donorActions); Grid.SetRow(donor, 2); files.Children.Add(donor);
        layout.Children.Add(files);
        var split = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetColumn(split, 1); layout.Children.Add(split);
        var boneHost = new DockPanel { Margin = new Thickness(14, 0, 0, 0) };
        DockPanel.SetDock(_bonesTitle, Dock.Top); boneHost.Children.Add(_bonesTitle);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("45,*,55"), Margin = new Thickness(8, 8) };
        header.Children.Add(new TextBlock { Text = "Index", Opacity = .65 });
        var name = new TextBlock { Text = "Name", Opacity = .65 }; Grid.SetColumn(name, 1); header.Children.Add(name);
        var parent = new TextBlock { Text = "Parent", Opacity = .65 }; Grid.SetColumn(parent, 2); header.Children.Add(parent);
        DockPanel.SetDock(header, Dock.Top); boneHost.Children.Add(header);
        var table = new Grid(); table.Children.Add(_bones); table.Children.Add(_bonesEmpty); boneHost.Children.Add(table);
        Grid.SetColumn(boneHost, 2); layout.Children.Add(boneHost); Content = layout;
        _current.ItemTemplate = _donorList.ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6) });
        _bones.ItemTemplate = new FuncDataTemplate<BoneEditRow>((bone, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("45,*,55") };
            row.Children.Add(new TextBlock { Text = bone?.Index.ToString(), Opacity = .65 });
            var label = new TextBlock { Text = bone?.Name, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(label, 1); row.Children.Add(label);
            var value = new TextBlock { Text = bone?.ParentIndex.ToString(), Opacity = .65 }; Grid.SetColumn(value, 2); row.Children.Add(value); return row;
        });
        _current.SelectionChanged += (_, _) => { if (!_refreshing && _current.SelectedIndex >= 0) { ClothSelected?.Invoke(_current.SelectedIndex); ShowCurrentBones(); } };
        _current.DoubleTapped += (_, _) => { if (_current.SelectedIndex >= 0) EditRequested?.Invoke(); };
        _current.GotFocus += (_, _) => ShowCurrentBones();
        _donorList.GotFocus += (_, _) => ShowDonorBones();
        _donorList.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _donorSession == null) return;
            _donorSession.ClothIndex = Math.Max(0, _donorList.SelectedIndex); ShowDonorBones(); UpdateActions();
        };
        var editMenu = new MenuItem { Header = "Editor" }; editMenu.Click += (_, _) => EditRequested?.Invoke();
        _renameMenu.Click += (_, _) => RenameRequested?.Invoke(); _duplicateMenu.Click += (_, _) => DuplicateRequested?.Invoke(); _removeMenu.Click += (_, _) => RemoveRequested?.Invoke();
        _current.ContextMenu = new ContextMenu { ItemsSource = new object[] { editMenu, _renameMenu, _duplicateMenu, _removeMenu } };
        _current.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete && _remove.IsEnabled) { RemoveRequested?.Invoke(); e.Handled = true; }
            else if (e.Key == Key.F2 && _renameMenu.IsEnabled) { RenameRequested?.Invoke(); e.Handled = true; }
            else if (e.Key == Key.Enter && _current.SelectedIndex >= 0) { EditRequested?.Invoke(); e.Handled = true; }
        };
        _donorList.KeyDown += (_, e) => { if (e.Key == Key.M && _merge.IsEnabled) { MergeRequested?.Invoke(); e.Handled = true; } };
    }
    public void Update(EditorSession session, EditorSession donor)
    {
        _session = session; _donorSession = donor; _refreshing = true;
        try
        {
            _currentFile.Text = FileName(session);
            var rows = session.Document.GetClothSummaries();
            // Keep item containers alive between the two presses of a double-click.
            if (!_current.Items.Cast<string>().SequenceEqual(rows)) _current.ItemsSource = rows;
            _current.SelectedIndex = rows.Count > 0 ? session.ClothIndex : -1;
            _currentEmpty.IsVisible = rows.Count == 0;
            _donorFile.Text = FileName(donor);
            var donorRows = donor.Document.GetClothSummaries();
            if (!_donorList.Items.Cast<string>().SequenceEqual(donorRows)) _donorList.ItemsSource = donorRows;
            _donorList.SelectedIndex = donorRows.Count > 0 ? donor.ClothIndex : -1;
            _donorEmpty.IsVisible = donorRows.Count == 0; UpdateActions();
            ShowCurrentBones();
        }
        finally { _refreshing = false; }
    }
    private static string FileName(EditorSession session) => !session.Document.HasDocument ? "No file" :
        (session.Path == null ? "Untitled" : System.IO.Path.GetFileName(session.Path)) + (session.IsDirty ? " *" : "");
    private void UpdateActions()
    {
        _remove.IsEnabled = _removeMenu.IsEnabled = _session?.CanRemoveEntry == true;
        _duplicate.IsEnabled = _duplicateMenu.IsEnabled = _renameMenu.IsEnabled = _session?.CanManageCloth == true;
        _merge.IsEnabled = _session?.Document.HasDocument == true && _donorSession?.HasSelectedEntry == true && !_session.Document.IsHkrg && !_donorSession.Document.IsHkrg;
        _clear.IsEnabled = _donorSession?.Document.HasDocument == true;
        var currentLoaded = _session?.Document.HasDocument == true;
        var donorLoaded = _donorSession?.Document.HasDocument == true;
        _openDonor.IsVisible = currentLoaded && !donorLoaded;
        _merge.IsVisible = _clear.IsVisible = SwapButton.IsVisible = currentLoaded && donorLoaded;
        SwapButton.IsEnabled = currentLoaded && donorLoaded;
    }
    private void ShowCurrentBones()
    {
        if (_session != null && _session.Document.GetClothSummaries().Count > 0) ShowBones(_session.Document, _session.ClothIndex, "Current bones");
        else { _bones.ItemsSource = Array.Empty<BoneEditRow>(); _bonesEmpty.IsVisible = true; _bonesTitle.Text = "Bones"; }
    }
    public void ShowDonorBones()
    {
        if (_donorSession?.HasSelectedEntry == true) ShowBones(_donorSession.Document, _donorSession.ClothIndex, "Donor bones");
    }
    private void ShowBones(HkclService document, int cloth, string title)
    { var rows = document.GetBoneRows(cloth); _bones.ItemsSource = rows; _bonesEmpty.IsVisible = rows.Count == 0; _bonesTitle.Text = title; }
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 10) };
    private static TextBlock FileLabel() => new() { Text = "No file", TextWrapping = TextWrapping.Wrap, Opacity = .65, Margin = new Thickness(0, 0, 0, 12) };
    private static TextBlock Empty(string text) => new() { Text = text, Opacity = .45, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private static Control Section(string title, TextBlock file, ListBox list, TextBlock empty, Control actions)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
        var heading = Heading(title); DockPanel.SetDock(heading, Dock.Top); dock.Children.Add(heading);
        DockPanel.SetDock(file, Dock.Top); dock.Children.Add(file);
        actions.Margin = new Thickness(0, 10, 0, 10); DockPanel.SetDock(actions, Dock.Bottom); dock.Children.Add(actions);
        var content = new Grid(); content.Children.Add(list); content.Children.Add(empty);
        var border = new Border { BorderThickness = new Thickness(1), Child = content };
        border.Bind(Border.BorderBrushProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("PhysicsBorder"));
        dock.Children.Add(border); return dock;
    }
}
