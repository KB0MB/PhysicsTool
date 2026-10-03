using System.Drawing;
using System.Globalization;
using System.Numerics;

namespace HKCLTool;

internal sealed class BotwCollisionDialog : LocalizedForm
{
    private readonly TextBox _sourcePath = new();
    private readonly TextBox _outputPath = new();
    private readonly DataGridView _meshGrid = new();
    private readonly NumericUpDown _scale = CreateNumber(1, 0.0001m, 100000m, 0.01m);
    private readonly NumericUpDown _offsetX = CreateNumber(0, -1000000m, 1000000m, 0.01m);
    private readonly NumericUpDown _offsetY = CreateNumber(0, -1000000m, 1000000m, 0.01m);
    private readonly NumericUpDown _offsetZ = CreateNumber(0, -1000000m, 1000000m, 0.01m);
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _platform = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _rigidBodyName = new();
    private readonly ComboBox _coordinates = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private Label? _bodyLabel;
    private bool _busy;
    private readonly CheckBox _compress = new();
    private readonly Label _description = new();
    private readonly Label _status = new();
    private readonly Button _createButton = CreateButton("Create collision", 130);
    private readonly Button _previewButton = CreateButton("Preview", 100);
    private readonly Button _exportButton = CreateButton("Export model...", 130);
    private IReadOnlyList<BotwCollisionMesh> _meshes = Array.Empty<BotwCollisionMesh>();
    private bool _staticCompression = true;

    public BotwCollisionDialog()
    {
        Text = "Create collision";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(920, 720);
        BackColor = Color.FromArgb(48, 48, 48);
        ForeColor = Color.Gainsboro;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 6
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        _description.Dock = DockStyle.Fill;
        _description.ForeColor = Color.Silver;
        _description.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_description, 0, 0);

        var sourceGroup = new GroupBox
        {
            Text = "Source",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            Padding = new Padding(10)
        };
        var sourceRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _sourcePath.Dock = DockStyle.Fill;
        _sourcePath.ReadOnly = true;
        _sourcePath.BackColor = Color.FromArgb(58, 58, 58);
        _sourcePath.ForeColor = Color.Gainsboro;
        var import = CreateButton("Import model...", 120);
        import.Dock = DockStyle.Fill;
        import.Click += async (_, _) => await ImportModelAsync();
        var openCollision = CreateButton("Open collision...", 130);
        openCollision.Dock = DockStyle.Fill;
        openCollision.Click += async (_, _) => await ImportCollisionAsync();
        sourceRow.Controls.Add(_sourcePath, 0, 0);
        sourceRow.Controls.Add(import, 1, 0);
        sourceRow.Controls.Add(openCollision, 2, 0);
        sourceGroup.Controls.Add(sourceRow);
        layout.Controls.Add(sourceGroup, 0, 1);

        var meshesGroup = new GroupBox
        {
            Text = "Collision meshes",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            Padding = new Padding(8)
        };
        ConfigureMeshGrid();
        meshesGroup.Controls.Add(_meshGrid);
        layout.Controls.Add(meshesGroup, 0, 2);

        var outputGroup = new GroupBox
        {
            Text = "Output and transform",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            Padding = new Padding(10)
        };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 5 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        for (var row = 0; row < 5; row++) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        _target.Items.AddRange([
            AppLocalization.Translate("Static compound (.hksc)"),
            AppLocalization.Translate("Actor rigid body (.hkrb)"),
            AppLocalization.Translate("TotK polytope (.bgyml)")]);
        _target.SelectedIndex = 0;
        _target.SelectedIndexChanged += (_, _) => ConfigureTargetControls();
        _platform.Items.AddRange(["Wii U", "Switch"]);
        _platform.SelectedIndex = 0;
        AddField(fields, "Type", _target, 0, 0);
        AddField(fields, "Platform", _platform, 2, 0);
        AddField(fields, "Rigid body name", _rigidBodyName, 0, 1);
        _bodyLabel = fields.GetControlFromPosition(0, 1) as Label;
        fields.SetColumnSpan(_rigidBodyName, 5);
        _coordinates.Items.AddRange([AppLocalization.Translate("Y-up (game coordinates)"), AppLocalization.Translate("Z-up (Blender coordinates)")]);
        _coordinates.SelectedIndex = 0;
        _coordinates.Dock = DockStyle.Fill;
        _coordinates.Visible = false;
        fields.Controls.Add(_coordinates, 1, 1);
        fields.SetColumnSpan(_coordinates, 5);
        AddField(fields, "Output", _outputPath, 0, 2);
        fields.SetColumnSpan(_outputPath, 4);
        var browse = CreateButton("Browse...", 100);
        browse.Dock = DockStyle.Fill;
        browse.Click += (_, _) => BrowseOutput();
        fields.Controls.Add(browse, 5, 2);
        AddField(fields, "Scale", _scale, 0, 3);
        AddField(fields, "Offset X", _offsetX, 2, 3);
        AddField(fields, "Offset Y", _offsetY, 4, 3);
        AddField(fields, "Offset Z", _offsetZ, 0, 4);
        _compress.Text = "Yaz0 compressed (.shksc)";
        _compress.Checked = true;
        _compress.Dock = DockStyle.Fill;
        _compress.CheckedChanged += (_, _) =>
        {
            if (!IsPolytope && SelectedTarget == BotwCollisionTarget.StaticCompound)
                _staticCompression = _compress.Checked;
            UpdateOutputExtension();
        };
        fields.Controls.Add(_compress, 2, 4);
        fields.SetColumnSpan(_compress, 4);
        outputGroup.Controls.Add(fields);
        layout.Controls.Add(outputGroup, 0, 3);

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = Color.FromArgb(142, 198, 255);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_status, 0, 4);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        var close = CreateButton("Close", 90);
        close.Click += (_, _) => Close();
        _createButton.Enabled = false;
        _createButton.Click += async (_, _) => await CreateCollisionAsync();
        _previewButton.Enabled = false;
        _previewButton.Click += (_, _) => PreviewCollision();
        _exportButton.Enabled = false;
        _exportButton.Click += async (_, _) => await ExportModelAsync();
        buttons.Controls.Add(close);
        buttons.Controls.Add(_createButton);
        buttons.Controls.Add(_exportButton);
        buttons.Controls.Add(_previewButton);
        layout.Controls.Add(buttons, 0, 5);

        Controls.Add(layout);
        AcceptButton = _createButton;
        CancelButton = close;
        ConfigureTargetControls();
    }

    private void ConfigureMeshGrid()
    {
        _meshGrid.Dock = DockStyle.Fill;
        _meshGrid.AllowUserToAddRows = false;
        _meshGrid.AllowUserToDeleteRows = false;
        _meshGrid.AllowUserToResizeRows = false;
        _meshGrid.RowHeadersVisible = false;
        _meshGrid.AutoGenerateColumns = false;
        _meshGrid.BackgroundColor = Color.FromArgb(42, 42, 42);
        _meshGrid.BorderStyle = BorderStyle.FixedSingle;
        _meshGrid.GridColor = Color.FromArgb(72, 72, 72);
        _meshGrid.DefaultCellStyle.BackColor = Color.FromArgb(48, 48, 48);
        _meshGrid.DefaultCellStyle.ForeColor = Color.Gainsboro;
        _meshGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 95, 160);
        _meshGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        _meshGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(62, 62, 62);
        _meshGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Gainsboro;
        _meshGrid.EnableHeadersVisualStyles = false;
        _meshGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Use", Width = 55 });
        _meshGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mesh", HeaderText = "Mesh", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _meshGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Triangles", HeaderText = "Triangles", Width = 90, ReadOnly = true });
        _meshGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Material",
            HeaderText = "BotW material",
            Width = 285,
            FlatStyle = FlatStyle.Flat,
            DataSource = BotwCollisionMaterials.Names.ToArray()
        });
    }

    private async Task ImportModelAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = AppLocalization.Translate("Import collision model"),
            Filter = "3D models|*.fbx;*.dae;*.obj;*.gltf;*.glb;*.3ds;*.ply;*.stl|FBX files|*.fbx|Collada files|*.dae|OBJ files|*.obj|All files|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetBusy(true, "Importing and triangulating model...");
        try
        {
            _meshes = await Task.Run(() => BotwCollisionMeshImporter.Import(dialog.FileName));
            _sourcePath.Text = dialog.FileName;
            _outputPath.Text = OutputWithExtension(dialog.FileName);
            if (string.IsNullOrWhiteSpace(_rigidBodyName.Text) && _meshes.Count > 0)
                _rigidBodyName.Text = _meshes[0].Name;
            PopulateMeshes();
            var triangles = _meshes.Sum(mesh => mesh.TriangleCount);
            _status.Text = string.Format(
                CultureInfo.CurrentCulture,
                AppLocalization.Translate("Imported {0} mesh(es), {1:N0} triangle(s)."),
                _meshes.Count,
                triangles);
            _createButton.Enabled = true;
            _previewButton.Enabled = true;
            _exportButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _meshes = Array.Empty<BotwCollisionMesh>();
            _meshGrid.Rows.Clear();
            _createButton.Enabled = false;
            _previewButton.Enabled = false;
            _exportButton.Enabled = false;
            ShowError(ex, "Collision import failed");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ImportCollisionAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = AppLocalization.Translate("Open collision"),
            Filter = "Collision files|*.hkrb;*.hksc;*.shksc;*.sbactorpack;*.bgyml;*.yaml;*.yml|TotK ShapeParam|*.bgyml;*.yaml;*.yml|Actor rigid body|*.hkrb|Static compound|*.hksc;*.shksc|Actor pack|*.sbactorpack|All files|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetBusy(true, "Reading collision shapes...");
        try
        {
            var polytope = Path.GetExtension(dialog.FileName).ToLowerInvariant() is ".bgyml" or ".yaml" or ".yml";
            _meshes = await Task.Run(() => polytope ? TotkPolytopeImporter.Import(dialog.FileName) : BotwCollisionFileImporter.Import(dialog.FileName));
            if (polytope) { _target.SelectedIndex = 2; _coordinates.SelectedIndex = 0; }
            _sourcePath.Text = dialog.FileName;
            var directory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(dialog.FileName) + "_rebuilt";
            _outputPath.Text = Path.Combine(directory, stem + CurrentExtension);
            if (string.IsNullOrWhiteSpace(_rigidBodyName.Text) && _meshes.Count > 0)
                _rigidBodyName.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            PopulateMeshes();
            var triangles = _meshes.Sum(mesh => mesh.TriangleCount);
            _status.Text = string.Format(
                CultureInfo.CurrentCulture,
                AppLocalization.Translate("Opened {0} collision mesh(es), {1:N0} triangle(s)."),
                _meshes.Count,
                triangles);
            _createButton.Enabled = true;
            _previewButton.Enabled = true;
            _exportButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _meshes = Array.Empty<BotwCollisionMesh>();
            _meshGrid.Rows.Clear();
            _createButton.Enabled = false;
            _previewButton.Enabled = false;
            _exportButton.Enabled = false;
            ShowError(ex, "Collision import failed");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void PopulateMeshes()
    {
        _meshGrid.Rows.Clear();
        foreach (var mesh in _meshes)
        {
            var rowIndex = _meshGrid.Rows.Add(true, mesh.Name, mesh.TriangleCount, mesh.Material);
            _meshGrid.Rows[rowIndex].Tag = mesh;
        }
    }

    private async Task CreateCollisionAsync()
    {
        ApplyGridChanges();

        var outputPath = _outputPath.Text.Trim();
        if (outputPath.Length == 0)
        {
            BrowseOutput();
            outputPath = _outputPath.Text.Trim();
            if (outputPath.Length == 0)
                return;
        }

        var options = new BotwCollisionBuildOptions(
            outputPath,
            (float)_scale.Value,
            new Vector3((float)_offsetX.Value, (float)_offsetY.Value, (float)_offsetZ.Value),
            _compress.Checked,
            SelectedPlatform,
            SelectedTarget,
            _rigidBodyName.Text);
        if (File.Exists(outputPath) && MessageBox.Show(this, AppLocalization.Translate("Replace the existing collision file?"), AppLocalization.Translate("Collision"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;
        var polytope = IsPolytope;
        var blenderZUp = _coordinates.SelectedIndex == 1;
        SetBusy(true, "Building collision...");
        try
        {
            if (polytope)
            {
                var document = await Task.Run(() =>
                {
                    var generated = TotkPolytopeBuilder.Create(_meshes, options.Scale, options.Offset, blenderZUp);
                    TotkPolytopeBuilder.Write(generated, outputPath);
                    return generated;
                });
                _status.Text = string.Format(CultureInfo.CurrentCulture, AppLocalization.Translate("Created {0} polytope(s), {1:N0} hull vertices."), document.Meshes.Count, document.Meshes.Sum(m => m.Vertices.Count));
                MessageBox.Show(this, AppLocalization.Translate("TotK ShapeParam created successfully.") + Environment.NewLine + Path.GetFileName(outputPath), AppLocalization.Translate("Collision"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var result = await Task.Run(() => BotwCollisionBuilder.Build(_meshes, options));
            _status.Text = string.Format(
                CultureInfo.CurrentCulture,
                AppLocalization.Translate("Created {0} shape(s), {1:N0} triangle(s), {2:N0} bytes."),
                result.ShapeCount,
                result.TriangleCount,
                result.FileSize);
            MessageBox.Show(
                this,
                AppLocalization.Translate("BotW collision created successfully.") + Environment.NewLine + Path.GetFileName(result.OutputPath),
                AppLocalization.Translate("Collision"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(ex, "Collision creation failed");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void PreviewCollision()
    {
        try
        {
            ApplyGridChanges();
            var meshes = await GetPreviewMeshesAsync();
            var model = BotwCollisionVisualModel.Create(meshes, _sourcePath.Text);
            using var preview = new BotwCollisionPreviewForm(model);
            preview.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowError(ex, "Collision preview failed");
        }
    }

    private async Task ExportModelAsync()
    {
        ApplyGridChanges();
        using var dialog = new SaveFileDialog
        {
            Title = AppLocalization.Translate("Export collision model"),
            Filter = "Collada|*.dae|FBX|*.fbx",
            DefaultExt = "dae",
            AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_sourcePath.Text) + "_collision.dae"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        SetBusy(true, "Exporting collision model...");
        try
        {
            var meshes = await GetPreviewMeshesAsync();
            SetBusy(true, "Exporting collision model...");
            await Task.Run(() => BotwCollisionExporter.Export(meshes, dialog.FileName));
            _status.Text = AppLocalization.Translate("Exported collision model: {0}")
                .Replace("{0}", Path.GetFileName(dialog.FileName), StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            ShowError(ex, "Collision export failed");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyGridChanges()
    {
        _meshGrid.EndEdit();
        foreach (DataGridViewRow row in _meshGrid.Rows)
        {
            if (row.Tag is not BotwCollisionMesh mesh)
                continue;
            mesh.Included = Convert.ToBoolean(row.Cells["Include"].Value, CultureInfo.InvariantCulture);
            mesh.Material = Convert.ToString(row.Cells["Material"].Value, CultureInfo.InvariantCulture) ?? BotwCollisionMaterials.Default;
        }
    }

    private void BrowseOutput()
    {
        using var dialog = new SaveFileDialog
        {
            Title = AppLocalization.Translate("Save collision"),
            Filter = IsPolytope ? "TotK ShapeParam|*.bgyml|ShapeParam YAML|*.yaml" : SelectedTarget == BotwCollisionTarget.ActorRigidBody
                ? "BotW actor rigid body|*.hkrb"
                : _compress.Checked ? "Compressed BotW collision|*.shksc" : "BotW collision|*.hksc",
            DefaultExt = CurrentExtension.TrimStart('.'),
            AddExtension = true,
            FileName = Path.GetFileName(_outputPath.Text)
        };
        if (!string.IsNullOrWhiteSpace(_outputPath.Text))
            dialog.InitialDirectory = Path.GetDirectoryName(Path.GetFullPath(_outputPath.Text));
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _outputPath.Text = IsPolytope && dialog.FilterIndex == 2 ? Path.ChangeExtension(dialog.FileName, ".yaml") : dialog.FileName;
    }

    private void UpdateOutputExtension()
    {
        if (!string.IsNullOrWhiteSpace(_outputPath.Text))
            _outputPath.Text = OutputWithExtension(_outputPath.Text);
    }

    private string OutputWithExtension(string path)
    {
        var stem = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path));
        const string suffix = ".phive__ShapeParam";
        if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) stem = stem[..^suffix.Length];
        return stem + CurrentExtension;
    }

    private bool IsPolytope => _target.SelectedIndex == 2;

    private BotwCollisionTarget SelectedTarget => _target.SelectedIndex == 1
        ? BotwCollisionTarget.ActorRigidBody
        : BotwCollisionTarget.StaticCompound;

    private BotwCollisionPlatform SelectedPlatform => _platform.SelectedIndex == 1
        ? BotwCollisionPlatform.Switch
        : BotwCollisionPlatform.WiiU;

    private string CurrentExtension => IsPolytope ? ".phive__ShapeParam.bgyml" : SelectedTarget == BotwCollisionTarget.ActorRigidBody
        ? ".hkrb"
        : _compress.Checked ? ".shksc" : ".hksc";

    private void ConfigureTargetControls()
    {
        var actor = SelectedTarget == BotwCollisionTarget.ActorRigidBody;
        _platform.Enabled = actor;
        _rigidBodyName.Enabled = actor;
        _rigidBodyName.Visible = !IsPolytope;
        _coordinates.Visible = IsPolytope;
        _compress.Visible = !IsPolytope;
        if (Controls.Count > 0 && Controls[0] is TableLayoutPanel layout) layout.RowStyles[0].Height = IsPolytope ? 0 : 48;
        if (_bodyLabel != null) _bodyLabel.Text = AppLocalization.Translate(IsPolytope ? "Coordinates" : "Rigid body name");
        ConfigureMaterialColumn();
        if (IsPolytope)
        {
            _platform.SelectedIndex = 1;
            _compress.Checked = false;
            _compress.Enabled = false;
            _compress.Text = AppLocalization.Translate("Uncompressed ShapeParam (.bgyml)");
            _description.Text = string.Empty;
        }
        else if (actor)
        {
            _compress.Checked = false;
            _compress.Enabled = false;
            _compress.Text = AppLocalization.Translate("Actor HKRB is stored inside the actor pack.");
            _description.Text = AppLocalization.Translate("Creates BotW actor rigid-body collision. Choose Wii U or Switch to match the target actor pack.");
        }
        else
        {
            _platform.SelectedIndex = 0;
            _compress.Enabled = true;
            _compress.Checked = _staticCompression;
            _compress.Text = AppLocalization.Translate("Yaz0 compressed (.shksc)");
            _description.Text = AppLocalization.Translate("Creates BotW Wii U static-compound collision for fields and shrines. Imported meshes are triangulated automatically.");
        }
        UpdateOutputExtension();
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        UseWaitCursor = busy;
        Controls[0].Enabled = !busy;
        _createButton.Enabled = !busy && _meshes.Count > 0;
        _previewButton.Enabled = !busy && _meshes.Count > 0;
        _exportButton.Enabled = !busy && _meshes.Count > 0;
        _meshGrid.Enabled = !busy;
        if (status != null)
            _status.Text = status;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy) e.Cancel = true;
        base.OnFormClosing(e);
    }

    private async Task<IReadOnlyList<BotwCollisionMesh>> GetPreviewMeshesAsync()
    {
        if (!IsPolytope) return _meshes;
        var scale = (float)_scale.Value;
        var offset = new Vector3((float)_offsetX.Value, (float)_offsetY.Value, (float)_offsetZ.Value);
        var blender = _coordinates.SelectedIndex == 1;
        SetBusy(true, "Building collision...");
        try { return (await Task.Run(() => TotkPolytopeBuilder.Create(_meshes, scale, offset, blender))).Meshes; }
        finally { SetBusy(false); }
    }

    private void ConfigureMaterialColumn()
    {
        if (_meshGrid.Columns.Count == 0) return;
        _meshGrid.EndEdit();
        var values = _meshGrid.Rows.Cast<DataGridViewRow>().Select(r => Convert.ToString(r.Cells["Material"].Value, CultureInfo.InvariantCulture) ?? BotwCollisionMaterials.Default).ToArray();
        _meshGrid.Columns.Remove("Material");
        if (IsPolytope)
            _meshGrid.Columns.Add(new DataGridViewTextBoxColumn { Name="Material", HeaderText=AppLocalization.Translate("Material presets"), Width=285 });
        else
            _meshGrid.Columns.Add(new DataGridViewComboBoxColumn { Name="Material", HeaderText=AppLocalization.Translate("BotW material"), Width=285, FlatStyle=FlatStyle.Flat, DataSource=BotwCollisionMaterials.Names.Concat(values).Distinct(StringComparer.Ordinal).ToArray() });
        for (var i=0;i<values.Length;i++) _meshGrid.Rows[i].Cells["Material"].Value=values[i];
    }

    private void ShowError(Exception exception, string title)
    {
        MessageBox.Show(
            this,
            AppLocalization.Translate(UserFacingExceptionFormatter.Format(exception)),
            AppLocalization.Translate(title),
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void AddField(TableLayoutPanel layout, string text, Control control, int column, int row)
    {
        layout.Controls.Add(new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, column, row);
        control.Dock = DockStyle.Fill;
        if (control is TextBox textBox)
        {
            textBox.BackColor = Color.FromArgb(58, 58, 58);
            textBox.ForeColor = Color.Gainsboro;
        }
        layout.Controls.Add(control, column + 1, row);
    }

    private static NumericUpDown CreateNumber(decimal value, decimal minimum, decimal maximum, decimal increment) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        DecimalPlaces = 4,
        ThousandsSeparator = false
    };

    private static Button CreateButton(string text, int width) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(width, 30),
        Height = 30,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(64, 64, 64),
        ForeColor = Color.Gainsboro,
        FlatAppearance = { BorderColor = Color.FromArgb(130, 130, 130) }
    };
}
