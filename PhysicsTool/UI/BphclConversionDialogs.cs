using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace HKCLTool;

internal sealed class BphclConversionScaleDialog : Form
{
    private readonly DataGridView _grid = new();
    private readonly IReadOnlyList<BphclConversionScale> _scales;

    private BphclConversionScaleDialog(IReadOnlyList<BphclConversionScale> scales)
    {
        _scales = scales;
        Text = "BPHCL to HKCL conversion scales";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(660, 360);
        Size = new Size(760, 520);
        BackColor = Color.FromArgb(42, 42, 42);
        ForeColor = Color.Gainsboro;

        var description = new Label
        {
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(12, 10, 12, 6),
            Text = "Choose a solver scale for each cloth. It multiplies dynamic particle mass and divides " +
                   "inverse mass by the same amount. Constraint stiffness uses the same scale.\r\n" +
                   "A verified vanilla match is used when one exists; otherwise the converter uses a topology fallback.",
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(42, 42, 42)
        };

        ConfigureGrid();
        foreach (var scale in scales)
            _grid.Rows.Add(scale.ClothIndex, scale.ClothName, scale.SuggestionBasis, scale.DefaultScale.ToString("G7", CultureInfo.InvariantCulture), scale.DefaultScale.ToString("G7", CultureInfo.InvariantCulture));

        var useDefaults = CreateButton("Use suggested scales");
        useDefaults.Click += (_, _) =>
        {
            for (var row = 0; row < _grid.Rows.Count; row++)
                _grid.Rows[row].Cells[4].Value = _grid.Rows[row].Cells[3].Value;
        };

        var cancel = CreateButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        var export = CreateButton("Continue to export");
        export.DialogResult = DialogResult.OK;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8),
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Color.FromArgb(42, 42, 42)
        };
        buttons.Controls.Add(export);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(useDefaults);

        Controls.Add(_grid);
        Controls.Add(description);
        Controls.Add(buttons);
        AcceptButton = export;
        CancelButton = cancel;
    }

    public static bool TryGetScales(
        IWin32Window owner,
        IReadOnlyList<BphclConversionScale> scales,
        out IReadOnlyDictionary<int, float> values)
    {
        using var dialog = new BphclConversionScaleDialog(scales);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            values = new Dictionary<int, float>();
            return false;
        }

        var parsed = new Dictionary<int, float>();
        for (var row = 0; row < dialog._grid.Rows.Count; row++)
        {
            var text = Convert.ToString(dialog._grid.Rows[row].Cells[4].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) ||
                !float.IsFinite(scale) || scale <= 0.0f)
            {
                MessageBox.Show(owner, $"Enter a positive finite scale for cloth {row}.", "Invalid conversion scale", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                values = new Dictionary<int, float>();
                return false;
            }

            parsed[scales[row].ClothIndex] = scale;
        }

        values = parsed;
        return true;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoGenerateColumns = false;
        _grid.BackgroundColor = Color.FromArgb(34, 34, 34);
        _grid.GridColor = Color.FromArgb(82, 82, 82);
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(58, 58, 58), ForeColor = Color.Gainsboro, SelectionBackColor = Color.FromArgb(58, 58, 58)
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(46, 46, 46), ForeColor = Color.Gainsboro, SelectionBackColor = Color.FromArgb(76, 96, 120), SelectionForeColor = Color.White
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Index", ReadOnly = true, Width = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Cloth", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 240 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Basis", ReadOnly = true, Width = 170 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Suggested", ReadOnly = true, Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "BotW scale", Width = 110 });
    }

    private static Button CreateButton(string text)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(64, 64, 64), ForeColor = Color.Gainsboro };
        button.FlatAppearance.BorderColor = Color.FromArgb(115, 115, 115);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(82, 82, 82);
        return button;
    }
}

internal sealed class ParticleMassScaleDialog : Form
{
    private readonly TextBox _scaleText = new() { Text = "1" };

    private ParticleMassScaleDialog(int targetCount, bool useSelection)
    {
        Text = "Scale particle mass";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(410, 174);
        BackColor = Color.FromArgb(42, 42, 42);
        ForeColor = Color.Gainsboro;

        var targetText = useSelection
            ? $"Scale {targetCount} selected dynamic particle(s)."
            : $"Scale all {targetCount} dynamic particles (no selection).";
        var label = new Label { Text = targetText, Left = 14, Top = 15, Width = 382, ForeColor = Color.Gainsboro };
        var note = new Label
        {
            Text = useSelection
                ? "Relative mass changes affect the chain. A larger mass produces a smaller inverse mass."
                : "Uniformly scaling every particle usually changes solver stability more than visible weight.",
            Left = 14,
            Top = 41,
            Width = 382,
            Height = 36,
            ForeColor = Color.LightSteelBlue
        };
        var scaleLabel = new Label { Text = "Mass scale:", Left = 14, Top = 84, Width = 120, ForeColor = Color.Gainsboro };
        _scaleText.SetBounds(14, 107, 382, 25);

        var cancel = BphclConversionScaleDialogButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        cancel.SetBounds(232, 140, 78, 28);
        var apply = BphclConversionScaleDialogButton("Apply");
        apply.DialogResult = DialogResult.OK;
        apply.SetBounds(318, 140, 78, 28);

        Controls.AddRange(new Control[] { label, note, scaleLabel, _scaleText, cancel, apply });
        AcceptButton = apply;
        CancelButton = cancel;
    }

    public static bool TryGetScale(IWin32Window owner, int targetCount, bool useSelection, out float scale)
    {
        using var dialog = new ParticleMassScaleDialog(targetCount, useSelection);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            scale = 1.0f;
            return false;
        }

        if (!float.TryParse(dialog._scaleText.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) ||
            !float.IsFinite(scale) || scale <= 0.0f)
        {
            MessageBox.Show(owner, "Enter a positive finite mass scale.", "Invalid mass scale", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private static Button BphclConversionScaleDialogButton(string text)
    {
        var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(64, 64, 64), ForeColor = Color.Gainsboro };
        button.FlatAppearance.BorderColor = Color.FromArgb(115, 115, 115);
        return button;
    }
}
