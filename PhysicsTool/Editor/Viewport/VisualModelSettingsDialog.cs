using System.Drawing;
using System.Numerics;
using System.Windows.Forms;

namespace HKCLTool;

public sealed class VisualModelSettingsDialog : Form
{
    private readonly CheckBox _visible = new() { Text = "Visible", AutoSize = true };
    private readonly CheckBox _textures = new() { Text = "Textures", AutoSize = true };
    private readonly CheckBox _wireframe = new() { Text = "Wireframe", AutoSize = true };
    private readonly CheckBox _mirrorX = new() { Text = "Mirror X", AutoSize = true };
    private readonly NumericUpDown _opacity = MakeNumber(0.05m, 1.0m, 0.05m, 2);
    private readonly NumericUpDown _scale = MakeNumber(0.0001m, 10000m, 0.1m, 4);
    private readonly NumericUpDown _offsetX = MakeNumber(-10000m, 10000m, 0.01m, 4);
    private readonly NumericUpDown _offsetY = MakeNumber(-10000m, 10000m, 0.01m, 4);
    private readonly NumericUpDown _offsetZ = MakeNumber(-10000m, 10000m, 0.01m, 4);
    private readonly NumericUpDown _rotationX = MakeNumber(-360m, 360m, 1m, 2);
    private readonly NumericUpDown _rotationY = MakeNumber(-360m, 360m, 1m, 2);
    private readonly NumericUpDown _rotationZ = MakeNumber(-360m, 360m, 1m, 2);

    public VisualModelSettingsDialog(VisualModelDisplaySettings current)
    {
        Text = "Visual Model Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(560, 430);
        BackColor = Color.FromArgb(45, 45, 45);
        ForeColor = Color.Gainsboro;
        Font = SystemFonts.MessageBoxFont;

        _visible.Checked = current.Visible;
        _textures.Checked = current.ShowTextures;
        _wireframe.Checked = current.Wireframe;
        _mirrorX.Checked = current.MirrorX;
        _opacity.Value = ClampDecimal((decimal)current.Opacity, _opacity);
        _scale.Value = ClampDecimal((decimal)current.UniformScale, _scale);
        _offsetX.Value = ClampDecimal((decimal)current.Offset.X, _offsetX);
        _offsetY.Value = ClampDecimal((decimal)current.Offset.Y, _offsetY);
        _offsetZ.Value = ClampDecimal((decimal)current.Offset.Z, _offsetZ);
        _rotationX.Value = ClampDecimal((decimal)current.RotationDegrees.X, _rotationX);
        _rotationY.Value = ClampDecimal((decimal)current.RotationDegrees.Y, _rotationY);
        _rotationZ.Value = ClampDecimal((decimal)current.RotationDegrees.Z, _rotationZ);

        var flags = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        flags.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        flags.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        flags.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        flags.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        foreach (var checkBox in new[] { _visible, _textures, _wireframe, _mirrorX })
        {
            checkBox.Dock = DockStyle.Fill;
            checkBox.Margin = new Padding(0, 1, 12, 1);
        }
        flags.Controls.Add(_visible, 0, 0);
        flags.Controls.Add(_textures, 1, 0);
        flags.Controls.Add(_wireframe, 0, 1);
        flags.Controls.Add(_mirrorX, 1, 1);
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            Padding = new Padding(16, 12, 16, 4)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(grid, 0, "Display", flags);
        AddRow(grid, 1, "Opacity", _opacity);
        AddRow(grid, 2, "Uniform Scale", _scale);
        AddRow(grid, 3, "Offset X", _offsetX);
        AddRow(grid, 4, "Offset Y", _offsetY);
        AddRow(grid, 5, "Offset Z", _offsetZ);
        AddRow(grid, 6, "Rotation X", _rotationX);
        AddRow(grid, 7, "Rotation Y", _rotationY);
        AddRow(grid, 8, "Rotation Z", _rotationZ);

        var ok = new Button { Text = "Apply", DialogResult = DialogResult.OK, Width = 90, Height = 28 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };
        StyleButton(ok);
        StyleButton(cancel);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 16, 10)
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(ok);

        Controls.Add(grid);
        Controls.Add(actions);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public VisualModelDisplaySettings Settings => new()
    {
        Visible = _visible.Checked,
        ShowTextures = _textures.Checked,
        Wireframe = _wireframe.Checked,
        MirrorX = _mirrorX.Checked,
        Opacity = (float)_opacity.Value,
        UniformScale = (float)_scale.Value,
        Offset = new Vector3((float)_offsetX.Value, (float)_offsetY.Value, (float)_offsetZ.Value),
        RotationDegrees = new Vector3((float)_rotationX.Value, (float)_rotationY.Value, (float)_rotationZ.Value)
    };

    private static NumericUpDown MakeNumber(decimal minimum, decimal maximum, decimal increment, int decimals) => new()
    {
        Dock = DockStyle.Fill,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        DecimalPlaces = decimals,
        ThousandsSeparator = true
    };

    private static decimal ClampDecimal(decimal value, NumericUpDown control) =>
        Math.Clamp(value, control.Minimum, control.Maximum);

    private static void AddRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, row == 0 ? 58 : 36));
        grid.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        control.Margin = new Padding(4, 5, 4, 5);
        grid.Controls.Add(control, 1, row);
    }

    private static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(105, 105, 105);
        button.BackColor = Color.FromArgb(64, 64, 64);
        button.ForeColor = Color.Gainsboro;
        button.UseVisualStyleBackColor = false;
    }
}
