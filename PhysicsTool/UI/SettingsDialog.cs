using System.Drawing;
using System.Windows.Forms;

namespace HKCLTool;

internal sealed class SettingsDialog : LocalizedForm
{
    private const int BaseClientWidth = 500;
    private const int BaseClientHeight = 262;
    private const int BaseLabelColumnWidth = 160;
    private const int BaseNoteRowHeight = 44;

    private readonly TableLayoutPanel _layout = new();
    private readonly TableLayoutPanel _fields = new();
    private readonly Label _languageLabel = new();
    private readonly Label _animationFrameRateLabel = new();
    private readonly Label _note = new();
    private readonly ComboBox _language = new();
    private readonly ComboBox _animationFrameRate = new();
    private readonly Label _status = new();

    public SettingsDialog()
    {
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(BaseClientWidth, BaseClientHeight);
        BackColor = Color.FromArgb(48, 48, 48);
        ForeColor = Color.Gainsboro;

        _layout.Dock = DockStyle.Fill;
        _layout.Padding = new Padding(14);
        _layout.ColumnCount = 1;
        _layout.RowCount = 3;
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var general = new GroupBox
        {
            Text = "General",
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ForeColor = Color.Gainsboro
        };
        _fields.Dock = DockStyle.Fill;
        _fields.ColumnCount = 2;
        _fields.RowCount = 3;
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, BaseLabelColumnWidth));
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _fields.RowStyles.Add(new RowStyle(SizeType.Absolute, BaseNoteRowHeight));

        _languageLabel.Text = "Interface language";
        _languageLabel.Dock = DockStyle.Fill;
        _languageLabel.TextAlign = ContentAlignment.MiddleLeft;
        _fields.Controls.Add(_languageLabel, 0, 0);

        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.Dock = DockStyle.Fill;
        _language.BackColor = Color.FromArgb(60, 60, 60);
        _language.ForeColor = Color.Gainsboro;
        _language.Items.AddRange(AppLocalization.Languages.Cast<object>().ToArray());
        _language.SelectedItem = AppLocalization.Languages.First(language =>
            string.Equals(language.Code, AppLocalization.CurrentLanguage, StringComparison.OrdinalIgnoreCase));
        _fields.Controls.Add(_language, 1, 0);

        _animationFrameRateLabel.Text = "Animation frame rate";
        _animationFrameRateLabel.Dock = DockStyle.Fill;
        _animationFrameRateLabel.TextAlign = ContentAlignment.MiddleLeft;
        _fields.Controls.Add(_animationFrameRateLabel, 0, 1);

        _animationFrameRate.DropDownStyle = ComboBoxStyle.DropDownList;
        _animationFrameRate.Dock = DockStyle.Fill;
        _animationFrameRate.BackColor = Color.FromArgb(60, 60, 60);
        _animationFrameRate.ForeColor = Color.Gainsboro;
        _animationFrameRate.Items.AddRange(["30 FPS (0.5x)", "60 FPS (1x)"]);
        _animationFrameRate.SelectedIndex =
            AppLocalization.AnimationFramesPerSecond == 60 ? 1 : 0;
        _fields.Controls.Add(_animationFrameRate, 1, 1);

        _note.Text = "Changes apply immediately and are saved in settings.json.";
        _note.Dock = DockStyle.Fill;
        _note.ForeColor = Color.Silver;
        _note.TextAlign = ContentAlignment.MiddleLeft;
        _fields.Controls.Add(_note, 0, 2);
        _fields.SetColumnSpan(_note, 2);
        general.Controls.Add(_fields);
        _layout.Controls.Add(general, 0, 0);

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(142, 198, 255);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _layout.Controls.Add(_status, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 0)
        };
        var close = CreateButton("Close");
        close.Click += (_, _) => Close();
        var apply = CreateButton("Apply");
        apply.Click += (_, _) => ApplySettings();
        buttons.Controls.Add(close);
        buttons.Controls.Add(apply);
        _layout.Controls.Add(buttons, 0, 2);

        Controls.Add(_layout);
        AcceptButton = apply;
        CancelButton = close;
    }

    private void ApplySettings()
    {
        if (_language.SelectedItem is not AppLanguageOption selected)
            return;
        var animationFramesPerSecond = _animationFrameRate.SelectedIndex == 1 ? 60 : 30;

        if (!AppLocalization.TrySetPreferences(selected.Code, animationFramesPerSecond))
        {
            MessageBox.Show(
                this,
                AppLocalization.Translate("PhysicsTool could not save its settings. Move the application to a writable folder and try again."),
                AppLocalization.Translate("Settings"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        _status.Text = "Settings applied.";
    }

    private static Button CreateButton(string text) => new()
    {
        Text = text,
        Width = 90,
        Height = 30,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(64, 64, 64),
        ForeColor = Color.Gainsboro,
        FlatAppearance = { BorderColor = Color.FromArgb(130, 130, 130) }
    };
}
