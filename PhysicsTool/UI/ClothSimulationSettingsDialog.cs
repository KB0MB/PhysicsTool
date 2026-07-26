using System;
using System.Drawing;
using System.Windows.Forms;

namespace HKCLTool;

internal sealed class ClothSimulationSettingsDialog : Form
{
    private readonly NumericUpDown _damping;
    private readonly NumericUpDown _gravityX;
    private readonly NumericUpDown _gravityY;
    private readonly NumericUpDown _gravityZ;
    private readonly NumericUpDown _collisionTolerance;
    private readonly CheckBox _transferTranslation;
    private readonly NumericUpDown _minTranslationSpeed;
    private readonly NumericUpDown _maxTranslationSpeed;
    private readonly NumericUpDown _minTranslationBlend;
    private readonly NumericUpDown _maxTranslationBlend;
    private readonly CheckBox _transferRotation;
    private readonly NumericUpDown _minRotationSpeed;
    private readonly NumericUpDown _maxRotationSpeed;
    private readonly NumericUpDown _minRotationBlend;
    private readonly NumericUpDown _maxRotationBlend;

    private ClothSimulationSettingsDialog(ClothSimulationSettings settings)
    {
        Text = "Cloth simulation settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(640, 565);
        BackColor = Color.FromArgb(48, 48, 48);
        ForeColor = Color.Gainsboro;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        layout.Controls.Add(new Label
        {
            Text = "Motion transfer blends animated character movement into the cloth. Lower blends reduce turn-driven flinging.",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var simulationGroup = new GroupBox
        {
            Text = "Simulation",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            Padding = new Padding(10)
        };
        var simulationGrid = CreateFieldGrid();
        _damping = AddNumber(simulationGrid, "Damping per second", settings.DampingPerSecond, 0.0m, 100.0m, 0.01m, 0);
        _gravityX = AddNumber(simulationGrid, "Gravity X", settings.GravityX, -100.0m, 100.0m, 0.01m, 1);
        _gravityY = AddNumber(simulationGrid, "Gravity Y", settings.GravityY, -100.0m, 100.0m, 0.01m, 2);
        _gravityZ = AddNumber(simulationGrid, "Gravity Z", settings.GravityZ, -100.0m, 100.0m, 0.01m, 3);
        _collisionTolerance = AddNumber(simulationGrid, "Collision tolerance", settings.CollisionTolerance, 0.0m, 10.0m, 0.001m, 4);
        simulationGroup.Controls.Add(simulationGrid);
        layout.Controls.Add(simulationGroup, 0, 1);

        var transferGroup = new GroupBox
        {
            Text = "Motion transfer",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            Padding = new Padding(10)
        };
        var transferGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        transferGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        transferGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _transferTranslation = AddMotionGroup(
            transferGrid, 0, "Translation", settings.TransferTranslationMotion,
            settings.MinTranslationSpeed, settings.MaxTranslationSpeed,
            settings.MinTranslationBlend, settings.MaxTranslationBlend,
            out _minTranslationSpeed, out _maxTranslationSpeed, out _minTranslationBlend, out _maxTranslationBlend);
        _transferRotation = AddMotionGroup(
            transferGrid, 1, "Rotation", settings.TransferRotationMotion,
            settings.MinRotationSpeed, settings.MaxRotationSpeed,
            settings.MinRotationBlend, settings.MaxRotationBlend,
            out _minRotationSpeed, out _maxRotationSpeed, out _minRotationBlend, out _maxRotationBlend);
        transferGroup.Controls.Add(transferGrid);
        layout.Controls.Add(transferGroup, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var apply = CreateButton("Apply");
        apply.DialogResult = DialogResult.OK;
        var cancel = CreateButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);

        AcceptButton = apply;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    public static bool TryEdit(IWin32Window owner, ClothSimulationSettings settings, out ClothSimulationSettings edited)
    {
        using var dialog = new ClothSimulationSettingsDialog(settings);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            edited = settings;
            return false;
        }

        edited = new ClothSimulationSettings
        {
            DampingPerSecond = (float)dialog._damping.Value,
            GravityX = (float)dialog._gravityX.Value,
            GravityY = (float)dialog._gravityY.Value,
            GravityZ = (float)dialog._gravityZ.Value,
            CollisionTolerance = (float)dialog._collisionTolerance.Value,
            TransferTranslationMotion = dialog._transferTranslation.Checked,
            MinTranslationSpeed = (float)dialog._minTranslationSpeed.Value,
            MaxTranslationSpeed = (float)dialog._maxTranslationSpeed.Value,
            MinTranslationBlend = (float)dialog._minTranslationBlend.Value,
            MaxTranslationBlend = (float)dialog._maxTranslationBlend.Value,
            TransferRotationMotion = dialog._transferRotation.Checked,
            MinRotationSpeed = (float)dialog._minRotationSpeed.Value,
            MaxRotationSpeed = (float)dialog._maxRotationSpeed.Value,
            MinRotationBlend = (float)dialog._minRotationBlend.Value,
            MaxRotationBlend = (float)dialog._maxRotationBlend.Value
        };
        return true;
    }

    private static TableLayoutPanel CreateFieldGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        for (var index = 0; index < 5; index++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        return grid;
    }

    private static CheckBox AddMotionGroup(
        TableLayoutPanel host,
        int column,
        string title,
        bool enabled,
        float minSpeed,
        float maxSpeed,
        float minBlend,
        float maxBlend,
        out NumericUpDown minSpeedInput,
        out NumericUpDown maxSpeedInput,
        out NumericUpDown minBlendInput,
        out NumericUpDown maxBlendInput)
    {
        var group = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            Padding = new Padding(8)
        };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        for (var index = 0; index < 5; index++)
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var checkBox = new CheckBox
        {
            Text = $"Transfer {title.ToLowerInvariant()} motion",
            Checked = enabled,
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro
        };
        grid.Controls.Add(checkBox, 0, 0);
        grid.SetColumnSpan(checkBox, 2);
        minSpeedInput = AddNumber(grid, "Min speed", minSpeed, 0.0m, 1000.0m, 0.01m, 1);
        maxSpeedInput = AddNumber(grid, "Max speed", maxSpeed, 0.0m, 1000.0m, 0.01m, 2);
        minBlendInput = AddNumber(grid, "Min blend", minBlend, 0.0m, 1.0m, 0.01m, 3);
        maxBlendInput = AddNumber(grid, "Max blend", maxBlend, 0.0m, 1.0m, 0.01m, 4);
        group.Controls.Add(grid);
        host.Controls.Add(group, column, 0);
        return checkBox;
    }

    private static NumericUpDown AddNumber(TableLayoutPanel layout, string label, float value, decimal minimum, decimal maximum, decimal increment, int row)
    {
        layout.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Gainsboro
        }, 0, row);

        var input = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            DecimalPlaces = 4,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            Value = Math.Clamp((decimal)value, minimum, maximum),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.Gainsboro,
            BorderStyle = BorderStyle.FixedSingle
        };
        layout.Controls.Add(input, 1, row);
        return input;
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
