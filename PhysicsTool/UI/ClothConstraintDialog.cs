using System.Numerics;

namespace HKCLTool;

internal sealed class ClothConstraintDialog : LocalizedForm
{
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _a = Number(0), _b = Number(0), _length = Number(6), _minimum = Number(6), _stiffness = Number(6), _stretch = Number(6);
    private readonly Button _add = new() { Text = "Add", AutoSize = true, DialogResult = DialogResult.OK };
    private readonly ToolTip _tips = new() { AutoPopDelay = 30000 };
    private readonly Label _validation = new() { AutoSize = true, MaximumSize = new Size(465, 0),
        MinimumSize = new Size(0, 36), ForeColor = Color.LightSteelBlue, Margin = new Padding(3, 8, 3, 0) };
    private readonly IReadOnlyList<ParticleEditRow> _particles;
    public int[] ParticleIndices => new[] { (int)_a.Value, (int)_b.Value };
    public ClothLinkOptions Options => new(((ClothConstraintKind)_kind.SelectedItem!).ClassName,
        (float)_length.Value, (float)_stiffness.Value, _minimum.Enabled ? (float)_minimum.Value : 0,
        _stretch.Enabled ? (float)_stretch.Value : 0);

    public ClothConstraintDialog(IReadOnlyList<ParticleEditRow> particles, IReadOnlyList<int> selected)
    {
        _particles = particles;
        Text = "Add cloth constraint"; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = Color.FromArgb(48, 48, 48); ForeColor = Color.Gainsboro;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; MinimumSize = new Size(510, 0);
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            Padding = new Padding(16), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285));
        void Row(string label, Control control)
        {
            var row = layout.RowCount++; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 18, 8) }, 0, row);
            control.Margin = new Padding(3, 5, 3, 5); control.Dock = DockStyle.Fill; layout.Controls.Add(control, 1, row);
        }
        Row("Constraint", _kind); Row("Particle A", _a); Row("Particle B", _b);
        Row("Rest / maximum length", _length); Row("Minimum length", _minimum); Row("Stiffness (native)", _stiffness); Row("Stretch stiffness (native)", _stretch);
        layout.Controls.Add(_validation, 0, layout.RowCount++); layout.SetColumnSpan(_validation, 2);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
        var close = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(close); buttons.Controls.Add(_add);
        layout.Controls.Add(buttons, 0, layout.RowCount++); layout.SetColumnSpan(buttons, 2); Controls.Add(layout);
        AcceptButton = _add; CancelButton = close;
        _kind.Items.AddRange(ClothConstraintCatalog.Kinds.Cast<object>().ToArray());
        _a.Maximum = _b.Maximum = particles.Count - 1;
        _a.Value = selected.FirstOrDefault();
        _b.Value = selected.Count > 1 ? selected[1] : Enumerable.Range(0, particles.Count).FirstOrDefault(i => i != (int)_a.Value);
        _a.ValueChanged += (_, _) => ResetDefaults(); _b.ValueChanged += (_, _) => ResetDefaults();
        _kind.SelectedIndexChanged += (_, _) => ResetDefaults(); _kind.SelectedIndex = 0;
        _length.ValueChanged += (_, _) => ValidateChoice(); _minimum.ValueChanged += (_, _) => ValidateChoice();
        _stiffness.ValueChanged += (_, _) => ValidateChoice(); _stretch.ValueChanged += (_, _) => ValidateChoice();
        _tips.SetToolTip(_a, "For a directed stretch limit this endpoint is the anchor; it is not moved by this constraint.");
        _tips.SetToolTip(_b, "For a directed stretch limit only this endpoint moves. Particle indices remain in the order entered.");
        _tips.SetToolTip(_length, "Defaults to the current distance between the selected particles, in model units.");
        _tips.SetToolTip(_minimum, "Compression limit. Used only by bend/stretch range constraints.");
        _tips.SetToolTip(_stiffness, "Stored native coefficient, not a percentage. Distance/bend defaults use 1 / (inverseMassA + inverseMassB); stretch uses 1. Existing coefficients are never rescaled.");
        _tips.SetToolTip(_stretch, "Independent native coefficient used when a bend link exceeds its maximum length.");
        FormClosed += (_, _) => _tips.Dispose();
    }

    private static NumericUpDown Number(int decimals) => new() { DecimalPlaces = decimals, Maximum = 1000000, Increment = decimals == 0 ? 1 : .01m, ThousandsSeparator = true };
    private void ResetDefaults()
    {
        if (_kind.SelectedItem is not ClothConstraintKind kind) return;
        _tips.SetToolTip(_kind, kind.Description + (kind.CanCreate ? "" : " Authoring this family is not available yet; existing records are preserved."));
        var a = _particles[(int)_a.Value]; var b = _particles[(int)_b.Value];
        var distance = Vector3.Distance(new(a.X, a.Y, a.Z), new(b.X, b.Y, b.Z));
        _length.Value = Math.Clamp((decimal)distance, 0, _length.Maximum);
        _minimum.Value = _length.Value;
        var inverseMass = (a.Fixed ? 0 : a.InverseMass) + (b.Fixed ? 0 : b.InverseMass);
        _stiffness.Value = kind.ClassName == "hclStretchLinkConstraintSet" || inverseMass <= 0 ? 1 : Math.Clamp((decimal)(1 / inverseMass), 0, _stiffness.Maximum);
        _stretch.Value = _stiffness.Value;
        _minimum.Enabled = _stretch.Enabled = kind.ClassName == "hclBendLinkConstraintSet";
        ValidateChoice();
    }
    private void ValidateChoice()
    {
        var error = _kind.SelectedItem is not ClothConstraintKind { CanCreate: true }
            ? "This constraint family is inspect-only; creation is not supported yet."
            : _a.Value == _b.Value ? "Choose two different particles."
            : _length.Value <= 0 ? "Rest / maximum length must be greater than zero."
            : _minimum.Enabled && _minimum.Value > _length.Value ? "Minimum length cannot exceed maximum length."
            : null;
        _add.Enabled = error == null;
        var warning = _particles[(int)_a.Value].Fixed && _particles[(int)_b.Value].Fixed
            ? "Both particles are fixed, so this constraint will not move them."
            : (_kind.SelectedItem as ClothConstraintKind)?.ClassName == "hclStretchLinkConstraintSet" && _particles[(int)_b.Value].Fixed
                ? "Particle B is fixed. This directed constraint cannot move it."
                : _stiffness.Value == 0 && (!_stretch.Enabled || _stretch.Value == 0)
                    ? "Zero stiffness: this constraint will have no effect." : "";
        _validation.Text = error ?? warning;
        _tips.SetToolTip(_validation, _validation.Text);
    }
}
