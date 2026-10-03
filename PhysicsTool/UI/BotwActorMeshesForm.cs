using System.Drawing;

namespace HKCLTool;

internal sealed class BotwActorMeshesForm : LocalizedForm
{
    private readonly BotwActorScene _scene;
    private readonly Action _changed;
    private readonly TextBox _search = new() { Dock = DockStyle.Fill };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
        MultiSelect = true, HideSelection = false, BackColor = Color.FromArgb(55, 55, 55), ForeColor = Color.Gainsboro };
    private readonly Button _delete = new() { Text = "\uE74D", Font = new Font("Segoe MDL2 Assets", 12), Size = new Size(36, 30), FlatStyle = FlatStyle.Flat };
    private readonly Label _count = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 0, 10, 0) };
    private readonly ToolTip _tips = new();
    private bool _refreshing;

    public BotwActorMeshesForm(BotwActorScene scene, Action changed)
    {
        _scene = scene; _changed = changed;
        Text = "Meshes"; ClientSize = new Size(660, 480); MinimumSize = new Size(480, 320);
        StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(48, 48, 48); ForeColor = Color.Gainsboro;
        _search.PlaceholderText = AppLocalization.Translate("Search meshes"); _search.AccessibleName = _search.PlaceholderText;
        _list.Columns.Add(AppLocalization.Translate("Mesh"), 470);
        _list.Columns.Add(AppLocalization.Translate("Triangles"), 100, HorizontalAlignment.Right);
        _list.AccessibleName = AppLocalization.Translate("Meshes");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), RowCount = 3, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        actions.Controls.Add(_delete);
        var close = new Button { Text = "Close", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(80, 30), FlatStyle = FlatStyle.Flat };
        footer.Controls.Add(actions, 0, 0); footer.Controls.Add(_count, 1, 0); footer.Controls.Add(close, 2, 0);
        layout.Controls.Add(_search, 0, 0); layout.Controls.Add(_list, 0, 1); layout.Controls.Add(footer, 0, 2); Controls.Add(layout);
        _delete.AccessibleName = AppLocalization.Translate("Delete meshes");
        _tips.SetToolTip(_delete, AppLocalization.Translate("Remove selected meshes from this view"));
        _search.TextChanged += (_, _) => RefreshRows();
        _list.ItemChecked += (_, e) =>
        {
            if (_refreshing || e.Item.Tag is not BotwActorMesh mesh) return;
            _scene.SetMeshVisible(mesh.Index, e.Item.Checked); _changed(); UpdateActions();
        };
        _list.SelectedIndexChanged += (_, _) => UpdateActions();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = e.SuppressKeyPress = true; } };
        _list.Resize += (_, _) => FitColumns();
        _delete.Click += (_, _) => DeleteSelected();
        close.Click += (_, _) => Close(); CancelButton = close;
        RefreshRows();
    }
    private void DeleteSelected()
    {
        var indices = _list.SelectedItems.Cast<ListViewItem>().Select(i => ((BotwActorMesh)i.Tag!).Index).ToArray();
        if (indices.Length == 0) return;
        _scene.DeleteMeshes(indices); RefreshRows(); _changed();
    }
    private void RefreshRows()
    {
        _refreshing = true; _list.BeginUpdate();
        try
        {
            var selected = _list.SelectedItems.Cast<ListViewItem>().Select(i => ((BotwActorMesh)i.Tag!).Index).ToHashSet();
            _list.Items.Clear();
            foreach (var mesh in _scene.Meshes.Where(m => !m.Deleted && m.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)))
            {
                var item = new ListViewItem(mesh.Name) { Tag = mesh, Checked = mesh.Visible, Selected = selected.Contains(mesh.Index) };
                item.SubItems.Add(mesh.Triangles.ToString("N0")); _list.Items.Add(item);
            }
        }
        finally { _list.EndUpdate(); _refreshing = false; }
        FitColumns(); UpdateActions();
    }
    private void FitColumns()
    {
        var nameWidth = _list.Items.Cast<ListViewItem>().Select(i => TextRenderer.MeasureText(i.Text, _list.Font).Width + 36).DefaultIfEmpty(150).Max();
        _list.Columns[1].Width = Math.Max(90, TextRenderer.MeasureText(_list.Columns[1].Text, _list.Font).Width + 24);
        _list.Columns[0].Width = Math.Max(nameWidth, _list.ClientSize.Width - _list.Columns[1].Width - SystemInformation.VerticalScrollBarWidth - 4);
    }
    private void UpdateActions()
    {
        _delete.Enabled = _list.SelectedItems.Count > 0;
        _count.Text = string.Format(AppLocalization.Translate("{0} / {1} visible"), _scene.Meshes.Count(m => m.Visible && !m.Deleted), _scene.Meshes.Count);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}
