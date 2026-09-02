using System.Drawing;
using System.Numerics;
using System.Windows.Forms;

namespace HKCLTool;

public sealed class VisualMaterialDialog : Form
{
    private readonly VisualModelData _model;
    private readonly Action _previewChanged;
    private readonly Dictionary<int, VisualModelMaterialSettings> _working;
    private readonly Dictionary<int, VisualModelTexture?[]> _workingTextures;
    private readonly ListBox _meshList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox _shaderModel = MakeCombo();
    private readonly CheckBox _useAlbedo = new() { Text = "Albedo", AutoSize = true };
    private readonly CheckBox _useSpm = new() { Text = "SPM", AutoSize = true };
    private readonly CheckBox _useNormal = new() { Text = "Normal", AutoSize = true };
    private readonly CheckBox _useAo = new() { Text = "AO", AutoSize = true };
    private readonly CheckBox _useAuxiliary = new() { Text = "Auxiliary", AutoSize = true };
    private readonly CheckBox _useEyeGlint = new() { Text = "Enabled", AutoSize = true };
    private readonly ComboBox _albedoUv = MakeCombo();
    private readonly ComboBox _spmUv = MakeCombo();
    private readonly ComboBox _normalUv = MakeCombo();
    private readonly ComboBox _aoUv = MakeCombo();
    private readonly ComboBox _auxiliaryUv = MakeCombo();
    private readonly Label _albedoTexture = MakeTextureLabel();
    private readonly Label _spmTexture = MakeTextureLabel();
    private readonly Label _normalTexture = MakeTextureLabel();
    private readonly Label _aoTexture = MakeTextureLabel();
    private readonly Label _auxiliaryTexture = MakeTextureLabel();
    private readonly Label _eyeGlintTexture = MakeTextureLabel();
    private readonly ComboBox _metalChannel = MakeCombo();
    private readonly ComboBox _smoothnessChannel = MakeCombo();
    private readonly NumericUpDown _metalScale = MakeNumber(0m, 2m, 0.05m);
    private readonly NumericUpDown _smoothnessScale = MakeNumber(0m, 2m, 0.05m);
    private readonly NumericUpDown _normalStrength = MakeNumber(0m, 2m, 0.05m);
    private readonly NumericUpDown _aoStrength = MakeNumber(0m, 1m, 0.05m);
    private readonly NumericUpDown _auxiliaryStrength = MakeNumber(0m, 2m, 0.05m);
    private readonly NumericUpDown _eyeGlintStrength = MakeNumber(0m, 2m, 0.05m);
    private readonly Button _tintButton = new() { Text = "Choose...", Width = 120, Height = 26 };
    private Vector4 _tint = Vector4.One;
    private int _loadedIndex = -1;
    private bool _loading;
    public bool HasApplied { get; private set; }

    public VisualMaterialDialog(VisualModelData model, Action previewChanged)
    {
        _model = model;
        _previewChanged = previewChanged;
        _working = model.Meshes
            .Select((mesh, index) => (index, settings: mesh.Material.Clone()))
            .ToDictionary(item => item.index, item => item.settings);
        _workingTextures = model.Meshes
            .Select((mesh, index) => (index, textures: new[]
            {
                mesh.Texture,
                mesh.SpecularTexture,
                mesh.NormalTexture,
                mesh.AmbientOcclusionTexture,
                mesh.AuxiliaryTexture,
                mesh.Material.ShaderModel == VisualModelShaderModel.Eye
                    ? VisualModelImporter.GetPackagedEyeGlintTexture()
                    : mesh.EyeGlintTexture
            }))
            .ToDictionary(item => item.index, item => item.textures);

        Text = "Visual Model Materials";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 700);
        ClientSize = new Size(980, 760);
        BackColor = Color.FromArgb(45, 45, 45);
        ForeColor = Color.Gainsboro;
        Font = SystemFonts.MessageBoxFont;
        ShowInTaskbar = false;

        _metalChannel.Items.AddRange(Enum.GetValues<VisualTextureChannel>().Cast<object>().ToArray());
        _smoothnessChannel.Items.AddRange(Enum.GetValues<VisualTextureChannel>().Cast<object>().ToArray());
        _shaderModel.Items.AddRange(Enum.GetValues<VisualModelShaderModel>().Cast<object>().ToArray());

        foreach (var mesh in model.Meshes)
            _meshList.Items.Add(mesh.Name);
        StyleList(_meshList);

        var meshGroup = new GroupBox
        {
            Text = "Meshes",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        meshGroup.Controls.Add(_meshList);

        var materialGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 17,
            AutoScroll = true,
            Padding = new Padding(16, 10, 16, 10)
        };
        materialGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        materialGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        materialGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        materialGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        AddValueRow(materialGrid, 0, "Shader", _shaderModel);
        AddTextureRow(materialGrid, 1, _useAlbedo, _albedoUv, _albedoTexture, MakeImportButton(TextureSlot.Albedo));
        AddTextureRow(materialGrid, 2, _useSpm, _spmUv, _spmTexture, MakeImportButton(TextureSlot.Spm));
        AddTextureRow(materialGrid, 3, _useNormal, _normalUv, _normalTexture, MakeImportButton(TextureSlot.Normal));
        AddTextureRow(materialGrid, 4, _useAo, _aoUv, _aoTexture, MakeImportButton(TextureSlot.AmbientOcclusion));
        AddTextureRow(materialGrid, 5, _useAuxiliary, _auxiliaryUv, _auxiliaryTexture, MakeImportButton(TextureSlot.Auxiliary));
        AddValueRow(materialGrid, 6, "Metal channel", _metalChannel);
        AddValueRow(materialGrid, 7, "Smoothness channel", _smoothnessChannel);
        AddValueRow(materialGrid, 8, "Metal scale", _metalScale);
        AddValueRow(materialGrid, 9, "Smoothness scale", _smoothnessScale);
        AddValueRow(materialGrid, 10, "Normal strength", _normalStrength);
        AddValueRow(materialGrid, 11, "AO strength", _aoStrength);
        AddValueRow(materialGrid, 12, "Auxiliary strength", _auxiliaryStrength);
        AddEyeGlintRow(materialGrid, 13);
        AddValueRow(materialGrid, 14, "Eye glint strength", _eyeGlintStrength);
        AddValueRow(materialGrid, 15, "Tint", _tintButton);
        materialGrid.SetColumnSpan(_tintButton, 3);

        materialGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var note = new Label
        {
            Text = "Shader family, texture slots, and UV assignments are independent for each mesh.",
            Dock = DockStyle.Fill,
            ForeColor = Color.Silver,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Padding = new Padding(0, 6, 0, 4)
        };
        materialGrid.Controls.Add(note, 0, 16);
        materialGrid.SetColumnSpan(note, 4);

        var materialGroup = new GroupBox
        {
            Text = "Selected Material",
            Dock = DockStyle.Fill,
            Padding = new Padding(6)
        };
        materialGroup.Controls.Add(materialGrid);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        split.Panel1.Controls.Add(meshGroup);
        split.Panel2.Controls.Add(materialGroup);

        var apply = new Button { Text = "Apply", Width = 90, Height = 28 };
        var close = new Button { Text = "Close", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        var savePreset = new Button { Text = "Save Preset", Width = 112, Height = 28 };
        StyleButton(apply);
        StyleButton(close);
        StyleButton(savePreset);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 16, 12)
        };
        actions.Controls.Add(close);
        actions.Controls.Add(apply);
        actions.Controls.Add(savePreset);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.Controls.Add(split, 0, 0);
        root.Controls.Add(actions, 0, 1);
        Controls.Add(root);
        AcceptButton = apply;
        CancelButton = close;
        Shown += (_, _) =>
        {
            const int leftMinimum = 190;
            const int rightMinimum = 430;
            var available = split.ClientSize.Width - split.SplitterWidth;
            if (available <= leftMinimum + rightMinimum)
                return;

            split.Panel1MinSize = leftMinimum;
            split.Panel2MinSize = rightMinimum;
            split.SplitterDistance = Math.Clamp(245, leftMinimum, available - rightMinimum);
        };

        _meshList.SelectedIndexChanged += (_, _) =>
        {
            SaveCurrent();
            LoadSelected();
        };
        foreach (var checkBox in new[] { _useAlbedo, _useSpm, _useNormal, _useAo, _useAuxiliary, _useEyeGlint })
            checkBox.CheckedChanged += (_, _) => UpdateSlotAvailability();
        _shaderModel.SelectedIndexChanged += (_, _) =>
        {
            if (_loading)
                return;
            EnsureEyeGlintTexture();
            UpdateSlotAvailability();
        };
        _tintButton.Click += (_, _) => ChooseTint();
        savePreset.Click += (_, _) => SavePreset();
        apply.Click += (_, _) =>
        {
            SaveCurrent();
            ApplyWorkingSettings();
        };

        if (_meshList.Items.Count > 0)
            _meshList.SelectedIndex = 0;
    }

    private void LoadSelected()
    {
        _loadedIndex = _meshList.SelectedIndex;
        if (_loadedIndex < 0)
            return;

        _loading = true;
        try
        {
            var mesh = _model.Meshes[_loadedIndex];
            var settings = _working[_loadedIndex];
            var textures = _workingTextures[_loadedIndex];
            var uvNames = mesh.UvSets.Select(uv => uv.Name).ToArray();
            SetUvItems(_albedoUv, uvNames, settings.AlbedoUvSet);
            SetUvItems(_spmUv, uvNames, settings.SpmUvSet);
            SetUvItems(_normalUv, uvNames, settings.NormalUvSet);
            SetUvItems(_aoUv, uvNames, settings.AmbientOcclusionUvSet);
            SetUvItems(_auxiliaryUv, uvNames, settings.AuxiliaryUvSet);
            _shaderModel.SelectedItem = settings.ShaderModel;
            _useAlbedo.Checked = settings.UseAlbedo;
            _useSpm.Checked = settings.UseSpm;
            _useNormal.Checked = settings.UseNormal;
            _useAo.Checked = settings.UseAmbientOcclusion;
            _useAuxiliary.Checked = settings.UseAuxiliary;
            _useEyeGlint.Checked = settings.UseEyeGlint;
            _metalChannel.SelectedItem = settings.MetalChannel;
            _smoothnessChannel.SelectedItem = settings.SmoothnessChannel;
            _metalScale.Value = Clamp(settings.MetalScale, _metalScale);
            _smoothnessScale.Value = Clamp(settings.SmoothnessScale, _smoothnessScale);
            _normalStrength.Value = Clamp(settings.NormalStrength, _normalStrength);
            _aoStrength.Value = Clamp(settings.AmbientOcclusionStrength, _aoStrength);
            _auxiliaryStrength.Value = Clamp(settings.AuxiliaryStrength, _auxiliaryStrength);
            _eyeGlintStrength.Value = Clamp(settings.EyeGlintStrength, _eyeGlintStrength);
            _tint = settings.Tint;
            UpdateTintButton();
            SetTextureLabel(_albedoTexture, textures[(int)TextureSlot.Albedo]);
            SetTextureLabel(_spmTexture, textures[(int)TextureSlot.Spm]);
            SetTextureLabel(_normalTexture, textures[(int)TextureSlot.Normal]);
            SetTextureLabel(_aoTexture, textures[(int)TextureSlot.AmbientOcclusion]);
            SetTextureLabel(_auxiliaryTexture, textures[(int)TextureSlot.Auxiliary]);
            SetTextureLabel(_eyeGlintTexture, textures[(int)TextureSlot.EyeGlint]);
        }
        finally
        {
            _loading = false;
            EnsureEyeGlintTexture();
            UpdateSlotAvailability();
        }
    }

    private void SaveCurrent()
    {
        if (_loading || _loadedIndex < 0)
            return;

        var settings = _working[_loadedIndex];
        settings.ShaderModel = _shaderModel.SelectedItem is VisualModelShaderModel shader
            ? shader
            : VisualModelShaderModel.Standard;
        settings.UseAlbedo = _useAlbedo.Checked;
        settings.UseSpm = _useSpm.Checked;
        settings.UseNormal = _useNormal.Checked;
        settings.UseAmbientOcclusion = _useAo.Checked;
        settings.UseAuxiliary = _useAuxiliary.Checked;
        settings.UseEyeGlint = _useEyeGlint.Checked;
        settings.AlbedoUvSet = _albedoUv.SelectedItem?.ToString() ?? string.Empty;
        settings.SpmUvSet = _spmUv.SelectedItem?.ToString() ?? string.Empty;
        settings.NormalUvSet = _normalUv.SelectedItem?.ToString() ?? string.Empty;
        settings.AmbientOcclusionUvSet = _aoUv.SelectedItem?.ToString() ?? string.Empty;
        settings.AuxiliaryUvSet = _auxiliaryUv.SelectedItem?.ToString() ?? string.Empty;
        settings.MetalChannel = _metalChannel.SelectedItem is VisualTextureChannel metal
            ? metal
            : VisualTextureChannel.Green;
        settings.SmoothnessChannel = _smoothnessChannel.SelectedItem is VisualTextureChannel smoothness
            ? smoothness
            : VisualTextureChannel.Red;
        settings.MetalScale = (float)_metalScale.Value;
        settings.SmoothnessScale = (float)_smoothnessScale.Value;
        settings.NormalStrength = (float)_normalStrength.Value;
        settings.AmbientOcclusionStrength = (float)_aoStrength.Value;
        settings.AuxiliaryStrength = (float)_auxiliaryStrength.Value;
        settings.EyeGlintStrength = (float)_eyeGlintStrength.Value;
        settings.Tint = _tint;
    }

    private void ApplyWorkingSettings()
    {
        for (var index = 0; index < _model.Meshes.Count; index++)
        {
            var mesh = _model.Meshes[index];
            var textures = _workingTextures[index];
            mesh.Texture = textures[(int)TextureSlot.Albedo];
            mesh.SpecularTexture = textures[(int)TextureSlot.Spm];
            mesh.NormalTexture = textures[(int)TextureSlot.Normal];
            mesh.AmbientOcclusionTexture = textures[(int)TextureSlot.AmbientOcclusion];
            mesh.AuxiliaryTexture = textures[(int)TextureSlot.Auxiliary];
            mesh.EyeGlintTexture = _working[index].ShaderModel == VisualModelShaderModel.Eye
                ? VisualModelImporter.GetPackagedEyeGlintTexture()
                : textures[(int)TextureSlot.EyeGlint];
            _model.Meshes[index].Material = _working[index].Clone();
        }
        HasApplied = true;
        _previewChanged();
    }

    private Button MakeImportButton(TextureSlot slot)
    {
        var button = new Button
        {
            Text = "Import",
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 5, 4, 5)
        };
        StyleButton(button);
        button.Click += (_, _) => ImportTexture(slot);
        return button;
    }

    private void ImportTexture(TextureSlot slot)
    {
        if (_loadedIndex < 0)
            return;

        using var dialog = new OpenFileDialog
        {
            Title = "Import material texture",
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
            CheckFileExists = true,
            InitialDirectory = Path.GetDirectoryName(_model.SourcePath)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var texture = VisualModelImporter.LoadTexture(dialog.FileName);
            _workingTextures[_loadedIndex][(int)slot] = texture;
            var (enabled, label) = GetSlotControls(slot);
            _loading = true;
            enabled.Checked = true;
            _loading = false;
            SetTextureLabel(label, texture);
            UpdateSlotAvailability();
        }
        catch (Exception ex)
        {
            _loading = false;
            MessageBox.Show(this, ex.Message, "Texture import error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void EnsureEyeGlintTexture()
    {
        if (_loadedIndex < 0 ||
            _shaderModel.SelectedItem is not VisualModelShaderModel.Eye)
            return;

        var texture = VisualModelImporter.GetPackagedEyeGlintTexture();
        if (texture == null)
            return;

        _workingTextures[_loadedIndex][(int)TextureSlot.EyeGlint] = texture;
        _loading = true;
        _useEyeGlint.Checked = true;
        _loading = false;
        SetTextureLabel(_eyeGlintTexture, texture);
    }

    private (CheckBox Enabled, Label Label) GetSlotControls(TextureSlot slot) => slot switch
    {
        TextureSlot.Albedo => (_useAlbedo, _albedoTexture),
        TextureSlot.Spm => (_useSpm, _spmTexture),
        TextureSlot.Normal => (_useNormal, _normalTexture),
        TextureSlot.AmbientOcclusion => (_useAo, _aoTexture),
        TextureSlot.Auxiliary => (_useAuxiliary, _auxiliaryTexture),
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    private void SavePreset()
    {
        SaveCurrent();
        try
        {
            ApplyWorkingSettings();
            var path = VisualMaterialPresetStore.Save(_model, _working);
            MessageBox.Show(
                this,
                $"Saved the material preset. It will load automatically for this FBX filename and matching mesh names.\n\n{path}",
                "Material preset",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Material preset error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChooseTint()
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            Color = Color.FromArgb(
                ToByte(_tint.X),
                ToByte(_tint.Y),
                ToByte(_tint.Z))
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _tint = new Vector4(
            dialog.Color.R / 255.0f,
            dialog.Color.G / 255.0f,
            dialog.Color.B / 255.0f,
            _tint.W);
        UpdateTintButton();
    }

    private void UpdateSlotAvailability()
    {
        if (_loadedIndex < 0)
            return;

        var textures = _workingTextures[_loadedIndex];
        _useAlbedo.Enabled = textures[(int)TextureSlot.Albedo] != null;
        _useSpm.Enabled = textures[(int)TextureSlot.Spm] != null;
        _useNormal.Enabled = textures[(int)TextureSlot.Normal] != null;
        _useAo.Enabled = textures[(int)TextureSlot.AmbientOcclusion] != null;
        _useAuxiliary.Enabled = textures[(int)TextureSlot.Auxiliary] != null;
        // Eye materials always use the packaged glint map. The checkbox is
        // display-only so the built-in texture cannot be replaced or disabled.
        _useEyeGlint.Enabled = false;
        _albedoUv.Enabled = _useAlbedo.Enabled && _useAlbedo.Checked;
        _spmUv.Enabled = _useSpm.Enabled && _useSpm.Checked;
        _normalUv.Enabled = _useNormal.Enabled && _useNormal.Checked;
        _aoUv.Enabled = _useAo.Enabled && _useAo.Checked;
        _auxiliaryUv.Enabled = _useAuxiliary.Enabled && _useAuxiliary.Checked;
        _metalChannel.Enabled = _spmUv.Enabled;
        _smoothnessChannel.Enabled = _spmUv.Enabled;
        _metalScale.Enabled = _spmUv.Enabled;
        _smoothnessScale.Enabled = _spmUv.Enabled;
        _normalStrength.Enabled = _normalUv.Enabled;
        _aoStrength.Enabled = _aoUv.Enabled;
        _auxiliaryStrength.Enabled = _auxiliaryUv.Enabled;
        _eyeGlintStrength.Enabled = _shaderModel.SelectedItem is VisualModelShaderModel.Eye &&
            textures[(int)TextureSlot.EyeGlint] != null;
    }

    private void UpdateTintButton()
    {
        _tintButton.BackColor = Color.FromArgb(ToByte(_tint.X), ToByte(_tint.Y), ToByte(_tint.Z));
        var luminance = _tint.X * 0.299f + _tint.Y * 0.587f + _tint.Z * 0.114f;
        _tintButton.ForeColor = luminance > 0.55f ? Color.Black : Color.White;
    }

    private static void SetUvItems(ComboBox combo, IEnumerable<string> names, string selected)
    {
        combo.Items.Clear();
        combo.Items.AddRange(names.Cast<object>().ToArray());
        var match = combo.Items.Cast<object>().FirstOrDefault(item =>
            item.ToString()?.Equals(selected, StringComparison.OrdinalIgnoreCase) == true);
        combo.SelectedItem = match ?? (combo.Items.Count > 0 ? combo.Items[0] : null);
    }

    private static void SetTextureLabel(Label label, VisualModelTexture? texture)
    {
        label.Text = texture?.Name ?? "(none)";
        label.ForeColor = texture == null ? Color.Gray : Color.Gainsboro;
    }

    private static ComboBox MakeCombo() => new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };

    private static NumericUpDown MakeNumber(decimal minimum, decimal maximum, decimal increment) => new()
    {
        Dock = DockStyle.Fill,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        DecimalPlaces = 2
    };

    private static Label MakeTextureLabel() => new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.Silver
    };

    private static decimal Clamp(float value, NumericUpDown control) =>
        Math.Clamp((decimal)value, control.Minimum, control.Maximum);

    private static int ToByte(float value) => (int)Math.Round(Math.Clamp(value, 0.0f, 1.0f) * 255.0f);

    private static void AddTextureRow(
        TableLayoutPanel grid,
        int row,
        CheckBox enabled,
        ComboBox uv,
        Label texture,
        Button import)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        enabled.Dock = DockStyle.Fill;
        enabled.TextAlign = ContentAlignment.MiddleLeft;
        uv.Margin = new Padding(4, 5, 4, 5);
        texture.Margin = new Padding(8, 5, 4, 5);
        grid.Controls.Add(enabled, 0, row);
        grid.Controls.Add(uv, 1, row);
        grid.Controls.Add(texture, 2, row);
        grid.Controls.Add(import, 3, row);
    }

    private void AddEyeGlintRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        grid.Controls.Add(new Label
        {
            Text = "Eye glint",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        _useEyeGlint.Dock = DockStyle.Fill;
        _useEyeGlint.Margin = new Padding(4, 5, 4, 5);
        _eyeGlintTexture.Margin = new Padding(8, 5, 4, 5);
        grid.Controls.Add(_useEyeGlint, 1, row);
        grid.Controls.Add(_eyeGlintTexture, 2, row);
        grid.SetColumnSpan(_eyeGlintTexture, 2);
    }

    private static void AddValueRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        grid.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        control.Margin = new Padding(4, 5, 4, 5);
        grid.Controls.Add(control, 1, row);
    }

    private static void StyleList(ListBox list)
    {
        list.BackColor = Color.FromArgb(52, 52, 52);
        list.ForeColor = Color.Gainsboro;
        list.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(105, 105, 105);
        button.BackColor = Color.FromArgb(64, 64, 64);
        button.ForeColor = Color.Gainsboro;
        button.UseVisualStyleBackColor = false;
    }

    private enum TextureSlot
    {
        Albedo,
        Spm,
        Normal,
        AmbientOcclusion,
        Auxiliary,
        EyeGlint
    }
}
