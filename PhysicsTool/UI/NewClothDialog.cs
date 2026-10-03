namespace HKCLTool;

internal sealed class NewClothDialog : LocalizedForm
{
    private readonly TextBox _name = new() { Text = "NewCloth", Dock = DockStyle.Fill };
    private readonly ComboBox _format = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    public string ClothName => _name.Text.Trim();
    public bool IsBphcl => _format.SelectedIndex == 2;
    public HkclPlatform Platform => _format.SelectedIndex == 0 ? HkclPlatform.WiiU : HkclPlatform.Switch;
    public NewClothDialog()
    {
        Text = "New cloth"; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; MinimumSize = new Size(450, 0);
        BackColor = Color.FromArgb(48, 48, 48); ForeColor = Color.Gainsboro;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        layout.Controls.Add(new Label { Text = "Name", AutoSize = true, Margin = new Padding(3, 8, 16, 8) }, 0, 0); layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(new Label { Text = "Format", AutoSize = true, Margin = new Padding(3, 8, 16, 8) }, 0, 1); layout.Controls.Add(_format, 1, 1);
        _format.Items.AddRange(new object[] { "BotW Wii U (.hkcl)", "BotW Switch (.hkcl)", "TotK (.bphcl)" }); _format.SelectedIndex = 0;
        var status = new Label { Text = "Unbound draft", AutoSize = true, ForeColor = Color.LightSteelBlue, Margin = new Padding(3, 12, 3, 12) };
        var tips = new ToolTip { AutoPopDelay = 30000 };
        tips.SetToolTip(status, "Starts with one particle and a Root bone, without donor data. Model skinning, output-bone bindings, and actor registration are not created automatically; this is not yet a game-ready actor resource.");
        layout.Controls.Add(status, 0, 2); layout.SetColumnSpan(status, 2);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var close = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var create = new Button { Text = "Create", AutoSize = true, DialogResult = DialogResult.OK };
        buttons.Controls.Add(close); buttons.Controls.Add(create); layout.Controls.Add(buttons, 0, 3); layout.SetColumnSpan(buttons, 2);
        _name.TextChanged += (_, _) => create.Enabled = !string.IsNullOrWhiteSpace(_name.Text);
        AcceptButton = create; CancelButton = close; Controls.Add(layout);
        FormClosed += (_, _) => tips.Dispose();
    }
}
