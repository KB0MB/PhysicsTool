namespace HKCLTool;

/// <summary>Collects the two external paths a BotW BPHYSICS sidecar needs.</summary>
internal sealed class BphysicsExportDialog : Form
{
    private readonly TextBox _hkclPath = new();
    private readonly CheckBox _useSupportBone = new();
    private readonly TextBox _supportBonePath = new();

    private BphysicsExportDialog(string suggestedHkclPath)
    {
        Text = "BPHYSICS export";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(560, 210);
        BackColor = Color.FromArgb(48, 48, 48);
        ForeColor = Color.Gainsboro;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 5,
            BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(510, 0),
            Text = "BPHYSICS tells BotW which HKCL to load and provides runtime wind/base-bone profiles. It does not contain the Havok cloth itself."
        };
        layout.SetColumnSpan(note, 2);
        layout.Controls.Add(note, 0, 0);

        layout.Controls.Add(new Label { Text = "HKCL game path", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 1);
        _hkclPath.Dock = DockStyle.Fill;
        _hkclPath.Text = suggestedHkclPath;
        layout.Controls.Add(_hkclPath, 1, 1);

        _useSupportBone.Text = "Use support bone sidecar";
        _useSupportBone.AutoSize = true;
        _useSupportBone.CheckedChanged += (_, _) => _supportBonePath.Enabled = _useSupportBone.Checked;
        layout.SetColumnSpan(_useSupportBone, 2);
        layout.Controls.Add(_useSupportBone, 0, 2);

        layout.Controls.Add(new Label { Text = "BPHYSSB game path", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 3);
        _supportBonePath.Dock = DockStyle.Top;
        _supportBonePath.Enabled = false;
        layout.Controls.Add(_supportBonePath, 1, 3);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var export = new Button { Text = "Export", AutoSize = true };
        export.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_hkclPath.Text))
            {
                MessageBox.Show(this, "Enter the HKCL path as it will appear inside the BotW resource pack.", "BPHYSICS export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_useSupportBone.Checked && string.IsNullOrWhiteSpace(_supportBonePath.Text))
            {
                MessageBox.Show(this, "Enter the BPHYSSB path or turn off support bones.", "BPHYSICS export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(export);
        layout.SetColumnSpan(buttons, 2);
        layout.Controls.Add(buttons, 0, 4);

        Controls.Add(layout);
        AcceptButton = export;
        CancelButton = cancel;
    }

    public static bool TryConfigure(IWin32Window owner, string suggestedHkclPath, out string hkclPath, out string? supportBonePath)
    {
        using var dialog = new BphysicsExportDialog(suggestedHkclPath);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            hkclPath = string.Empty;
            supportBonePath = null;
            return false;
        }

        hkclPath = dialog._hkclPath.Text.Trim();
        supportBonePath = dialog._useSupportBone.Checked ? dialog._supportBonePath.Text.Trim() : null;
        return true;
    }
}
