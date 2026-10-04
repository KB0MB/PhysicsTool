using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using HKCLTool;
using PhysicsTool.Core;

namespace PhysicsTool.Desktop;

public sealed class DocumentOverview : UserControl
{
    private readonly ListBox _current = new(), _donorList = new(), _bones = new();
    private readonly TextBlock _currentFile = FileLabel(), _donorFile = FileLabel(), _bonesTitle = Heading("Bones");
    private readonly TextBlock _currentEmpty = Empty("No physics file loaded"), _donorEmpty = Empty("No donor file loaded"), _bonesEmpty = Empty("No bones selected");
    private readonly Button _edit = new() { Content = "Edit selected" };
    private EditorSession? _session;
    private HkclService? _donor;
    private bool _refreshing;
    public event Action<int>? ClothSelected;
    public event Action? EditRequested;
    public event Action? OpenRequested;
    public event Action? DonorRequested;

    public DocumentOverview()
    {
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,5,340"), Margin = new Thickness(12, 4, 12, 12) };
        layout.ColumnDefinitions[0].MinWidth = 340;
        layout.ColumnDefinitions[2].MinWidth = 260;
        var files = new Grid { RowDefinitions = new RowDefinitions("*,5,*") };
        var currentActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var open = new Button { Content = "Open Physics" }; open.Click += (_, _) => OpenRequested?.Invoke(); currentActions.Children.Add(open);
        _edit.Click += (_, _) => EditRequested?.Invoke(); currentActions.Children.Add(_edit);
        files.Children.Add(Section("Current file", _currentFile, _current, _currentEmpty, currentActions));
        var rowSplit = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(rowSplit, 1); files.Children.Add(rowSplit);
        var donorActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var donorOpen = new Button { Content = "Open Donor" }; donorOpen.Click += (_, _) => DonorRequested?.Invoke(); donorActions.Children.Add(donorOpen);
        var donorClear = new Button { Content = "Clear donor" }; donorClear.Click += (_, _) => { _donor = null; _donorFile.Text = "No file"; _donorList.ItemsSource = Array.Empty<string>(); _donorEmpty.IsVisible = true; ShowCurrentBones(); }; donorActions.Children.Add(donorClear);
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
        _donorList.SelectionChanged += (_, _) => ShowDonorBones();
    }
    public void Update(EditorSession session)
    {
        _session = session; _refreshing = true;
        try
        {
            _currentFile.Text = session.Document.HasDocument ? (session.Path == null ? "Untitled" : System.IO.Path.GetFileName(session.Path)) : "No file";
            var rows = session.Document.GetClothSummaries(); _current.ItemsSource = rows; _current.SelectedIndex = rows.Count > 0 ? session.ClothIndex : -1;
            _currentEmpty.IsVisible = rows.Count == 0; _edit.IsEnabled = rows.Count > 0;
            ShowCurrentBones();
        }
        finally { _refreshing = false; }
    }
    public void LoadDonor(string path)
    {
        var donor = new HkclService(); donor.Load(path); var rows = donor.GetClothSummaries();
        _donor = donor; _donorFile.Text = System.IO.Path.GetFileName(path); _donorList.ItemsSource = rows; _donorList.SelectedIndex = rows.Count > 0 ? 0 : -1; _donorEmpty.IsVisible = rows.Count == 0;
    }
    private void ShowCurrentBones()
    {
        if (_session != null && _session.Document.GetClothSummaries().Count > 0) ShowBones(_session.Document, _session.ClothIndex, "Current bones");
        else { _bones.ItemsSource = Array.Empty<BoneEditRow>(); _bonesEmpty.IsVisible = true; _bonesTitle.Text = "Bones"; }
    }
    public void ShowDonorBones()
    {
        if (_donor != null && _donorList.SelectedIndex >= 0) ShowBones(_donor, _donorList.SelectedIndex, "Donor bones");
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
        dock.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.Parse("#484B4D")), BorderThickness = new Thickness(1), Child = content }); return dock;
    }
}
