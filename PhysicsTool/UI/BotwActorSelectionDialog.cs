using System.Drawing;

namespace HKCLTool;

internal static class BotwActorSelectionDialog
{
    private sealed record Item(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
    public static string? Choose(IWin32Window owner, IEnumerable<string> paths)
    {
        using var form = new LocalizedForm { Text = "Select actor", ClientSize = new Size(480, 520), MinimumSize = new Size(360, 320),
            StartPosition = FormStartPosition.CenterParent, BackColor = Color.FromArgb(48, 48, 48), ForeColor = Color.Gainsboro };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 3, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = AppLocalization.Translate("Search actors"), AccessibleName = AppLocalization.Translate("Search actors") };
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BackColor = Color.FromArgb(55, 55, 55), ForeColor = Color.Gainsboro };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true, FlatStyle = FlatStyle.Flat, MinimumSize = new Size(80, 30) };
        var open = new Button { Text = "Open actor", DialogResult = DialogResult.OK, AutoSize = true, FlatStyle = FlatStyle.Flat, MinimumSize = new Size(80, 30) };
        buttons.Controls.AddRange(new Control[] { close, open });
        layout.Controls.Add(filter, 0, 0); layout.Controls.Add(list, 0, 1); layout.Controls.Add(buttons, 0, 2); form.Controls.Add(layout);
        form.AcceptButton = open; form.CancelButton = close;
        var items = paths.Select(p => new Item(p)).ToArray();
        void Filter()
        {
            list.BeginUpdate(); list.Items.Clear(); list.Items.AddRange(items.Where(i => i.ToString().Contains(filter.Text, StringComparison.OrdinalIgnoreCase)).ToArray());
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            open.Enabled = list.SelectedItem != null; list.EndUpdate();
        }
        filter.TextChanged += (_, _) => Filter();
        list.DoubleClick += (_, _) => { if (list.SelectedItem != null) form.DialogResult = DialogResult.OK; };
        Filter();
        return form.ShowDialog(owner) == DialogResult.OK && list.SelectedItem is Item selected ? selected.Path : null;
    }
}
