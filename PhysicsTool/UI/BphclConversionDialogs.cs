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
        MinimumSize = new Size(780, 360);
        Size = new Size(1120, 540);
        BackColor = Color.FromArgb(42, 42, 42);
        ForeColor = Color.Gainsboro;

        var description = new Label
        {
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(12, 10, 12, 6),
            Text = "Mass scale multiplies dynamic particle mass and regenerates inverse mass. Constraint scale " +
                   "changes the soft standard/bend coefficients. It starts at the same suggested value, but can " +
                   "be adjusted separately for an unmatched cloth.\r\n" +
                   "A verified vanilla match is used when one exists; otherwise the converter uses a topology fallback. " +
                   "Watch Max Inv: larger values react faster and can become unstable.",
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(42, 42, 42)
        };

        ConfigureGrid();
        _grid.CellEndEdit += (_, e) =>
        {
            if (e.ColumnIndex == 9)
                UpdateMassDiagnostics(e.RowIndex);
        };
        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox textBox)
            {
                textBox.TextChanged -= MassScaleTextBoxChanged;
                if (_grid.CurrentCell?.ColumnIndex == 9)
                    textBox.TextChanged += MassScaleTextBoxChanged;
            }
        };
        foreach (var scale in scales)
        {
            var suggestedMass = scale.DefaultMassScale.ToString("G7", CultureInfo.InvariantCulture);
            var suggestedConstraints = scale.DefaultConstraintScale.ToString("G7", CultureInfo.InvariantCulture);
            _grid.Rows.Add(
                scale.ClothIndex,
                scale.ClothName,
                scale.SuggestionBasis,
                scale.DynamicParticleCount,
                scale.SourceDynamicMass.ToString("G7", CultureInfo.InvariantCulture),
                scale.SuggestedBotwDynamicMass.ToString("G7", CultureInfo.InvariantCulture),
                scale.SuggestedMaxInverseMass.ToString("G7", CultureInfo.InvariantCulture),
                suggestedMass,
                suggestedConstraints,
                suggestedMass,
                suggestedConstraints);
        }

        var useDefaults = CreateButton("Use suggested scales");
        useDefaults.Click += (_, _) =>
        {
            for (var row = 0; row < _grid.Rows.Count; row++)
            {
                _grid.Rows[row].Cells[9].Value = _grid.Rows[row].Cells[7].Value;
                _grid.Rows[row].Cells[10].Value = _grid.Rows[row].Cells[8].Value;
                UpdateMassDiagnostics(row);
            }
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
        out IReadOnlyDictionary<int, float> massValues,
        out IReadOnlyDictionary<int, float> constraintValues)
    {
        using var dialog = new BphclConversionScaleDialog(scales);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            massValues = new Dictionary<int, float>();
            constraintValues = new Dictionary<int, float>();
            return false;
        }

        var parsedMass = new Dictionary<int, float>();
        var parsedConstraints = new Dictionary<int, float>();
        for (var row = 0; row < dialog._grid.Rows.Count; row++)
        {
            if (!TryReadScale(dialog._grid.Rows[row].Cells[9].Value, out var massScale))
            {
                MessageBox.Show(owner, $"Enter a positive finite mass scale for cloth {row}.", "Invalid conversion scale", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                massValues = new Dictionary<int, float>();
                constraintValues = new Dictionary<int, float>();
                return false;
            }

            if (!TryReadScale(dialog._grid.Rows[row].Cells[10].Value, out var constraintScale))
            {
                MessageBox.Show(owner, $"Enter a positive finite constraint scale for cloth {row}.", "Invalid conversion scale", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                massValues = new Dictionary<int, float>();
                constraintValues = new Dictionary<int, float>();
                return false;
            }

            parsedMass[scales[row].ClothIndex] = massScale;
            parsedConstraints[scales[row].ClothIndex] = constraintScale;
        }

        massValues = parsedMass;
        constraintValues = parsedConstraints;
        return true;
    }

    private static bool TryReadScale(object? cellValue, out float scale)
    {
        var text = Convert.ToString(cellValue, CultureInfo.InvariantCulture) ?? string.Empty;
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) &&
               float.IsFinite(scale) && scale > 0.0f;
    }
    private void MassScaleTextBoxChanged(object? sender, EventArgs e)
    {
        if (_grid.CurrentCell?.ColumnIndex == 9 && sender is TextBox textBox)
            UpdateMassDiagnostics(_grid.CurrentCell.RowIndex, textBox.Text);
    }

    private void UpdateMassDiagnostics(int rowIndex, object? massScaleValue = null)
    {
        if (rowIndex < 0 || rowIndex >= _scales.Count || rowIndex >= _grid.Rows.Count)
            return;

        var row = _grid.Rows[rowIndex];
        var scaleInfo = _scales[rowIndex];
        if (!TryReadScale(massScaleValue ?? row.Cells[9].Value, out var massScale))
        {
            row.Cells[5].Value = "invalid";
            row.Cells[6].Value = "invalid";
            row.Cells[5].Style.ForeColor = Color.LightCoral;
            row.Cells[6].Style.ForeColor = Color.LightCoral;
            return;
        }

        var botwMass = scaleInfo.SourceDynamicMass * massScale;
        var maxInverse = scaleInfo.DefaultMassScale > float.Epsilon
            ? scaleInfo.SuggestedMaxInverseMass * (scaleInfo.DefaultMassScale / massScale)
            : 0.0f;

        row.Cells[5].Value = botwMass.ToString("G7", CultureInfo.InvariantCulture);
        row.Cells[6].Value = maxInverse.ToString("G7", CultureInfo.InvariantCulture);

        var diagnosticColor = maxInverse switch
        {
            > 50.0f => Color.LightCoral,
            > 20.0f => Color.Khaki,
            _ => Color.Gainsboro
        };
        row.Cells[5].Style.ForeColor = Color.Gainsboro;
        row.Cells[6].Style.ForeColor = diagnosticColor;
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Basis", ReadOnly = true, Width = 190 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Dyn", ReadOnly = true, Width = 48 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Source Mass", ReadOnly = true, Width = 92 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "BotW Mass", ReadOnly = true, Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Max Inv", ReadOnly = true, Width = 82 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Suggested Mass", ReadOnly = true, Width = 112 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Suggested Constraints", ReadOnly = true, Width = 136 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mass Scale", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Constraint Scale", Width = 116 });
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
