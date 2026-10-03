using System.Drawing;

namespace HKCLTool;

internal sealed class BotwActorBrowserDialog : LocalizedForm
{
    private readonly BotwActorCatalog _catalog;
    private readonly List<string> _folders;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = AppLocalization.Translate("Search actors") };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _roots = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DataGridView _list = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = Color.FromArgb(45, 45, 45), BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 6, 0, 6) };
    private readonly Button _open = MakeButton("Open actor");
    private readonly Button _refresh = MakeButton("Refresh");
    private readonly Button _add = MakeButton("Add game folder");
    private readonly Button _remove = MakeButton("Remove folder");
    private readonly ToolTip _tips = new();
    private CancellationTokenSource? _scan;
    private IReadOnlyList<BotwActorCatalogEntry> _actors = Array.Empty<BotwActorCatalogEntry>();
    private IReadOnlyList<string> _warnings = Array.Empty<string>();
    private bool _busy;
    internal BotwActorCatalogEntry? SelectedActor => _list.SelectedRows.Count == 1 ? _list.SelectedRows[0].Tag as BotwActorCatalogEntry : null;
    internal IReadOnlyList<string> ResourceFolders => _folders;
    internal IReadOnlyList<string> MatchingResourceRoots => _actors.Where(a => a.IsSwitch == SelectedActor?.IsSwitch)
        .Select(a => a.ResourceRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public BotwActorBrowserDialog(BotwActorCatalog catalog, IEnumerable<string> folders, bool scanOnShow = true)
    {
        _catalog = catalog;
        _folders = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Text = "Actors with physics"; ClientSize = new Size(850, 600); MinimumSize = new Size(620, 380);
        StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(48, 48, 48); ForeColor = Color.Gainsboro;
        AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 5, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var style in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize })
            layout.RowStyles.Add(new RowStyle(style, style == SizeType.Percent ? 100 : 0));
        var foldersBar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, Margin = new Padding(0, 0, 0, 8) };
        foldersBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 3; i++) foldersBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foldersBar.Controls.Add(_roots, 0, 0); foldersBar.Controls.Add(_add, 1, 0); foldersBar.Controls.Add(_remove, 2, 0); foldersBar.Controls.Add(_refresh, 3, 0);
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.Controls.Add(_search, 0, 0); filters.Controls.Add(_kind, 1, 0);
        _kind.Items.AddRange(new[] { "All physics", "Cloth", "Support bones", "Cloth + support bones" }.Select(AppLocalization.Translate).ToArray()); _kind.SelectedIndex = 0;
        _list.EnableHeadersVisualStyles = false;
        _list.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        _list.ColumnHeadersDefaultCellStyle = new() { BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.Gainsboro };
        _list.DefaultCellStyle = new() { BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.Gainsboro, SelectionBackColor = Color.FromArgb(0, 105, 155), SelectionForeColor = Color.White };
        _list.Columns.Add(new DataGridViewTextBoxColumn { Name = "Actor", HeaderText = "Actor", FillWeight = 48, MinimumWidth = 180 });
        _list.Columns.Add(new DataGridViewTextBoxColumn { Name = "Platform", HeaderText = "Platform", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 70 });
        _list.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Cloth", HeaderText = "Cloth", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 55 });
        _list.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Support", HeaderText = "Support bones", AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, MinimumWidth = 100 });
        _list.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Source", FillWeight = 35, MinimumWidth = 100 });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var close = MakeButton("Close"); close.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange(new Control[] { close, _open });
        layout.Controls.Add(foldersBar, 0, 0); layout.Controls.Add(filters, 0, 1); layout.Controls.Add(_list, 0, 2);
        layout.Controls.Add(_status, 0, 3); layout.Controls.Add(buttons, 0, 4); Controls.Add(layout);
        AcceptButton = _open; CancelButton = close;
        _roots.AccessibleName = AppLocalization.Translate("Resource folders");
        _search.AccessibleName = AppLocalization.Translate("Search actors");
        _kind.AccessibleName = AppLocalization.Translate("Physics type");
        _search.TextChanged += (_, _) => Filter(); _kind.SelectedIndexChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) => _open.Enabled = !_busy && SelectedActor != null;
        _list.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelected(); };
        _open.Click += (_, _) => OpenSelected();
        _refresh.Click += async (_, _) => await ScanAsync();
        _add.Click += async (_, _) => await AddFolderAsync();
        _remove.Click += async (_, _) => { if (_roots.SelectedItem is string folder) { _folders.Remove(folder); SaveFolders(); UpdateFolders(); await ScanAsync(); } };
        if (scanOnShow) Shown += async (_, _) => await ScanAsync();
        UpdateFolders(); Filter();
    }

    private static Button MakeButton(string text) => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(80, 30), FlatStyle = FlatStyle.Flat, Padding = new Padding(6, 2, 6, 2) };
    private void OpenSelected() { if (!_busy && SelectedActor != null) DialogResult = DialogResult.OK; }
    private void UpdateFolders()
    {
        _roots.Items.Clear(); _roots.Items.AddRange(_folders.ToArray());
        if (_roots.Items.Count > 0) _roots.SelectedIndex = 0;
        _remove.Enabled = !_busy && _folders.Count > 0;
    }
    private void SaveFolders()
    {
        if (!AppLocalization.TrySetActorResourceFolders(_folders))
            MessageBox.Show(this, AppLocalization.Translate("Game folders could not be saved."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    private async Task AddFolderAsync()
    {
        using var picker = new FolderBrowserDialog { Description = AppLocalization.Translate("Select the matching game content or romfs folder"), UseDescriptionForTitle = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (!_folders.Contains(picker.SelectedPath, StringComparer.OrdinalIgnoreCase)) _folders.Add(picker.SelectedPath);
        SaveFolders(); UpdateFolders(); await ScanAsync();
    }
    internal static IEnumerable<BotwActorCatalogEntry> Match(IEnumerable<BotwActorCatalogEntry> entries, string query, int kind) =>
        entries.Where(a => (kind switch { 1 => a.HasCloth, 2 => a.HasSupport, 3 => a.HasCloth && a.HasSupport, _ => true }) &&
            (a.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Platform.Contains(query, StringComparison.OrdinalIgnoreCase) || a.Source.Contains(query, StringComparison.OrdinalIgnoreCase)));
    internal void SetResults(BotwActorCatalogResult result)
    { _actors = result.Actors; _warnings = result.Warnings; Filter(); }
    private void Filter()
    {
        var selected = SelectedActor;
        _list.Rows.Clear();
        foreach (var actor in Match(_actors, _search.Text.Trim(), _kind.SelectedIndex))
        {
            var row = _list.Rows[_list.Rows.Add(actor.Name, actor.Platform, actor.HasCloth, actor.HasSupport, actor.Source)];
            row.Tag = actor;
            row.Cells["Source"].ToolTipText = actor.Path + (actor.ArchiveEntry == null ? "" : "\n" + actor.ArchiveEntry);
            if (actor == selected) _list.CurrentCell = row.Cells[0];
        }
        _open.Enabled = !_busy && SelectedActor != null;
        if (!_busy)
            _status.Text = _folders.Count == 0 ? AppLocalization.Translate("No game folders configured.") :
                string.Format(AppLocalization.Translate("{0} of {1} actors | {2} scan warnings"), _list.Rows.Count, _actors.Count, _warnings.Count);
        _tips.SetToolTip(_status, string.Join(Environment.NewLine, _warnings.Take(10)));
    }
    private async Task ScanAsync()
    {
        if (_busy) return;
        _scan?.Dispose(); _scan = new CancellationTokenSource(); var token = _scan.Token;
        _busy = true; _open.Enabled = _refresh.Enabled = _add.Enabled = _remove.Enabled = false;
        _status.Text = AppLocalization.Translate("Scanning actors...");
        var folders = _folders.ToArray();
        var progress = new Progress<string>(value => { if (!IsDisposed && !token.IsCancellationRequested) _status.Text = AppLocalization.Translate("Scanning actors...") + " " + value; });
        try
        {
            var result = await Task.Run(() => _catalog.Scan(folders, progress, token), token);
            if (!IsDisposed && !token.IsCancellationRequested) SetResults(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!IsDisposed && !token.IsCancellationRequested) SetResults(new(Array.Empty<BotwActorCatalogEntry>(), new[] { UserFacingExceptionFormatter.Format(ex) })); }
        finally
        {
            _busy = false;
            if (!IsDisposed && !token.IsCancellationRequested) { _refresh.Enabled = _add.Enabled = true; _remove.Enabled = _folders.Count > 0; Filter(); }
        }
    }
    protected override void OnFormClosing(FormClosingEventArgs e) { _scan?.Cancel(); base.OnFormClosing(e); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _scan?.Cancel(); _scan?.Dispose(); _tips.Dispose(); }
        base.Dispose(disposing);
    }
}
