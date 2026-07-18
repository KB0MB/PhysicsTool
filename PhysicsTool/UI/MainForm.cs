using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace HKCLTool;

public sealed class MainForm : Form
{
    private enum EditorPage
    {
        Particles,
        Bones,
        Colliders
    }

    private sealed class EditorSnapshot
    {
        public int ClothIndex { get; init; }
        public int SelectedIndex { get; init; }
        public EditorPage Page { get; init; }
        public List<ParticleEditRow>? Particles { get; init; }
        public List<BoneEditRow>? Bones { get; init; }
        public List<ColliderEditRow>? Colliders { get; init; }
        public string? RawState { get; init; }
    }

    private HkclService _current = new();
    private HkclService _reference = new();

    private readonly ListBox _clothList = new();
    private readonly ListBox _referenceClothList = new();
    private readonly ListBox _boneList = new();
    private readonly TextBox _detailsBox = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    private readonly Button _openReferenceButton = new();
    private readonly Button _swapFilesButton = new();
    private readonly Button _exportJsonButton = new();
    private readonly Button _saveWiiUButton = new();
    private readonly Button _saveSwitchButton = new();
    private readonly Button _removeButton = new();
    private readonly Button _mergeButton = new();
    private readonly Button _particleApplyButton = new();
    private readonly Button _particleRefreshButton = new();
    private readonly Button _directEditButton = new();
    private readonly Button _addEditorItemButton = new();
    private readonly ContextMenuStrip _exportMenu = new();
    private readonly ContextMenuStrip _clothMenu = new();
    private string? _currentSavePath;
    private HkclPlatform _currentSavePlatform = HkclPlatform.WiiU;

    private readonly DataGridView _particleGrid = new();
    private readonly ListBox _particleIndexList = new();
    private readonly DataGridView _particleDetailGrid = new();
    private readonly DataGridView _particleRelationshipGrid = new();
    private readonly ListBox _editorIndexList = new();
    private readonly DataGridView _editorDetailGrid = new();
    private readonly TabControl _editorTabs = new();
    private readonly Panel _editorContentPanel = new();
    private readonly ComboBox _particleBindBoneCombo = new();
    private readonly Button _particleBindButton = new();
    private readonly Label _particleBindStatusLabel = new();
    private readonly ParticlePreviewControl _particlePreview = new();
    private SplitContainer? _outerSplit;
    private SplitContainer? _fileSplit;
    private GroupBox? _referenceGroup;
    private GroupBox? _directEditGroup;
    private GroupBox? _editorValueGroup;
    private GroupBox? _relationshipGroup;
    private GroupBox? _particleBindGroup;
    private bool _updatingParticleGrid;
    private bool _directEditMode;
    private bool _applyingSnapshot;
    private EditorPage _editorPage = EditorPage.Particles;
    private List<ParticleEditRow> _particleRows = new();
    private List<BoneEditRow> _boneRows = new();
    private List<ColliderEditRow> _colliderRows = new();
    private readonly HashSet<int> _selectedParticleIndices = new();
    private readonly Stack<EditorSnapshot> _undoStack = new();
    private readonly Stack<EditorSnapshot> _redoStack = new();
    private EditorSnapshot? _pendingEditSnapshot;
    private EditorSnapshot? _viewportMoveSnapshot;
    private bool _viewportTransformChanged;

    public MainForm()
    {
        Text = "PhysicsTool";
        Width = 1120;
        Height = 720;
        MinimumSize = new Size(940, 580);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        AllowDrop = true;

        BuildInterface();
        ApplyTheme();
        WireEvents();
        UpdateModeLayout();
        UpdateButtons();
    }

    private void ApplyTheme()
    {
        BackColor = Color.FromArgb(54, 54, 54);
        ForeColor = Color.Gainsboro;
        ApplyThemeToControls(this);
        StyleEditorGrid(_editorDetailGrid);
        StyleEditorGrid(_particleRelationshipGrid);
        _statusStrip.BackColor = Color.FromArgb(48, 48, 48);
        _statusLabel.ForeColor = Color.Gainsboro;
    }

    private static void ApplyThemeToControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            switch (control)
            {
                case Button button:
                    StyleButton(button);
                    break;
                case TextBox:
                case ListBox:
                    control.BackColor = Color.FromArgb(42, 42, 42);
                    control.ForeColor = Color.Gainsboro;
                    break;
                case DataGridView:
                    break;
                default:
                    control.BackColor = Color.FromArgb(54, 54, 54);
                    control.ForeColor = Color.Gainsboro;
                    break;
            }

            ApplyThemeToControls(control);
        }
    }

    private static void StyleEditorGrid(DataGridView grid)
    {
        grid.BackgroundColor = Color.FromArgb(70, 70, 70);
        grid.GridColor = Color.FromArgb(95, 95, 95);
        grid.DefaultCellStyle.BackColor = Color.FromArgb(48, 48, 48);
        grid.DefaultCellStyle.ForeColor = Color.Gainsboro;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 95, 160);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(62, 62, 62);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Gainsboro;
        grid.EnableHeadersVisualStyles = false;
    }

    private void BuildInterface()
    {
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(8),
            ColumnCount = 2,
            RowCount = 1
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));

        var toolbarButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = false
        };

        var openButton = MakeButton("Open Physics", 125);
        openButton.Click += (_, _) => OpenCurrentFile();

        _openReferenceButton.Text = "Open Reference";
        _openReferenceButton.Width = 125;
        StyleButton(_openReferenceButton);
        _openReferenceButton.Click += (_, _) => OpenReferenceFile();

        _swapFilesButton.Text = "Swap files";
        _swapFilesButton.Width = 92;
        StyleButton(_swapFilesButton);
        _swapFilesButton.Click += (_, _) => SwapCurrentAndReference();

        _exportJsonButton.Text = "Export";
        _exportJsonButton.Width = 96;
        StyleButton(_exportJsonButton);
        ConfigureExportMenu();

        _saveWiiUButton.Text = "Save";
        _saveWiiUButton.Width = 86;
        StyleButton(_saveWiiUButton);

        _saveSwitchButton.Visible = false;

        toolbarButtons.Controls.Add(openButton);
        toolbarButtons.Controls.Add(_openReferenceButton);
        toolbarButtons.Controls.Add(_swapFilesButton);
        toolbarButtons.Controls.Add(new Label { Width = 18 });
        toolbarButtons.Controls.Add(_exportJsonButton);
        toolbarButtons.Controls.Add(_saveWiiUButton);

        UpdateAddButtonText();
        _directEditButton.Text = "Editor";
        _directEditButton.Width = 82;
        StyleButton(_directEditButton);
        _directEditButton.Margin = new Padding(0, 0, 0, 0);

        var editorButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        editorButtonPanel.Controls.Add(_directEditButton);

        toolbar.Controls.Add(toolbarButtons, 0, 0);
        toolbar.Controls.Add(editorButtonPanel, 1, 0);

        _outerSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        _fileSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        _fileSplit.Panel1.Controls.Add(BuildCurrentGroup());
        _referenceGroup = BuildReferenceGroup();
        _directEditGroup = BuildDirectEditGroup();
        _fileSplit.Panel2.Controls.Add(_referenceGroup);
        _fileSplit.Panel2.Controls.Add(_directEditGroup);

        _outerSplit.Panel1.Controls.Add(_fileSplit);
        _outerSplit.Panel2.Controls.Add(BuildBonesGroup());

        _statusLabel.Text = "Open a physics file.";
        _statusStrip.Items.Add(_statusLabel);

        Controls.Add(_outerSplit);
        Controls.Add(toolbar);
        Controls.Add(_statusStrip);

        Shown += (_, _) =>
        {
            if (_outerSplit.ClientSize.Width > 760)
            {
                _outerSplit.Panel1MinSize = 520;
                _outerSplit.Panel2MinSize = 180;
                var maxDistance = _outerSplit.ClientSize.Width - _outerSplit.Panel2MinSize - _outerSplit.SplitterWidth;
                if (maxDistance > _outerSplit.Panel1MinSize)
                {
                    _outerSplit.SplitterDistance = Math.Clamp(
                        _outerSplit.ClientSize.Width - 340,
                        _outerSplit.Panel1MinSize,
                        maxDistance);
                }
            }

            if (_fileSplit.ClientSize.Height > 460)
            {
                _fileSplit.Panel1MinSize = 180;
                _fileSplit.Panel2MinSize = 180;
                var maxDistance = _fileSplit.ClientSize.Height - _fileSplit.Panel2MinSize - _fileSplit.SplitterWidth;
                if (maxDistance > _fileSplit.Panel1MinSize)
                {
                    _fileSplit.SplitterDistance = Math.Clamp(
                        _fileSplit.ClientSize.Height / 2,
                        _fileSplit.Panel1MinSize,
                        maxDistance);
                }
            }
        };
    }

    private GroupBox BuildCurrentGroup()
    {
        var group = new GroupBox
        {
            Text = "Current file",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        _clothList.Dock = DockStyle.Fill;
        ConfigureClothMenu();
        _clothList.ContextMenuStrip = _clothMenu;
        layout.Controls.Add(_clothList, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false
        };

        _removeButton.Text = "Remove selected";
        _removeButton.Width = 130;
        StyleButton(_removeButton);
        buttons.Controls.Add(_removeButton);

        layout.Controls.Add(buttons, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private GroupBox BuildReferenceGroup()
    {
        var group = new GroupBox
        {
            Text = "Reference file",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        _referenceClothList.Dock = DockStyle.Fill;
        layout.Controls.Add(_referenceClothList, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false
        };

        _mergeButton.Text = "Merge selected (M)";
        _mergeButton.Width = 145;
        StyleButton(_mergeButton);
        buttons.Controls.Add(_mergeButton);

        layout.Controls.Add(buttons, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private GroupBox BuildDirectEditGroup()
    {
        var group = new GroupBox
        {
            Text = "Physics direct editing",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        layout.Controls.Add(BuildParticleEditor(), 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false
        };

        _particleApplyButton.Text = "Undo";
        _particleApplyButton.Width = 90;
        StyleButton(_particleApplyButton);

        _particleRefreshButton.Text = "Redo";
        _particleRefreshButton.Width = 90;
        StyleButton(_particleRefreshButton);

        buttons.Controls.Add(_particleApplyButton);
        buttons.Controls.Add(_particleRefreshButton);
        layout.Controls.Add(buttons, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildParticleEditor()
    {
        ConfigureEditorList();
        ConfigureEditorDetailGrid();
        ConfigureParticleRelationshipGrid();

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        var previewGroup = new GroupBox
        {
            Text = "Viewport",
            Dock = DockStyle.Fill,
            Padding = new Padding(6)
        };

        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var previewToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 4)
        };
        _addEditorItemButton.Text = "Add Particle";
        _addEditorItemButton.Width = 116;
        StyleButton(_addEditorItemButton);
        previewToolbar.Controls.Add(_addEditorItemButton);

        _particlePreview.Dock = DockStyle.Fill;
        previewLayout.Controls.Add(previewToolbar, 0, 0);
        previewLayout.Controls.Add(_particlePreview, 0, 1);
        previewGroup.Controls.Add(previewLayout);
        split.Panel1.Controls.Add(previewGroup);

        var sideSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        _editorValueGroup = new GroupBox
        {
            Text = "Editor values",
            Dock = DockStyle.Fill,
            Padding = new Padding(6)
        };

        var valueSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1
        };
        valueSplit.Panel1MinSize = 150;
        valueSplit.SplitterDistance = 170;
        valueSplit.Panel1.Controls.Add(_editorIndexList);
        valueSplit.Panel2.Controls.Add(_editorDetailGrid);

        var editorLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        editorLayout.Controls.Add(valueSplit, 0, 0);
        editorLayout.Controls.Add(BuildParticleBindingPanel(), 0, 1);

        _editorContentPanel.Dock = DockStyle.Fill;
        _editorContentPanel.Controls.Add(editorLayout);

        _editorTabs.Dock = DockStyle.Fill;
        _editorTabs.TabPages.Add(new TabPage("Particles"));
        _editorTabs.TabPages.Add(new TabPage("Bones"));
        _editorTabs.TabPages.Add(new TabPage("Colliders"));
        _editorTabs.TabPages[0].Controls.Add(_editorContentPanel);
        _editorValueGroup.Controls.Add(_editorTabs);

        _relationshipGroup = new GroupBox
        {
            Text = "Selected particle relationships",
            Dock = DockStyle.Fill,
            Padding = new Padding(6)
        };
        _relationshipGroup.Controls.Add(_particleRelationshipGrid);
        sideSplit.Panel1.Controls.Add(_editorValueGroup);
        sideSplit.Panel2.Controls.Add(_relationshipGroup);
        split.Panel2.Controls.Add(sideSplit);

        sideSplit.SizeChanged += (_, _) =>
        {
            if (sideSplit.ClientSize.Height <= 260)
                return;

            sideSplit.Panel1MinSize = 180;
            sideSplit.Panel2MinSize = 180;
            var maxDistance = sideSplit.ClientSize.Height - sideSplit.Panel2MinSize - sideSplit.SplitterWidth;
            if (maxDistance <= sideSplit.Panel1MinSize)
                return;

            sideSplit.SplitterDistance = Math.Clamp(
                (int)(sideSplit.ClientSize.Height * 0.54),
                sideSplit.Panel1MinSize,
                maxDistance);
        };

        split.SizeChanged += (_, _) =>
        {
            if (split.ClientSize.Width <= 720)
                return;

            split.Panel1MinSize = 700;
            split.Panel2MinSize = 520;
            var maxDistance = split.ClientSize.Width - split.Panel2MinSize - split.SplitterWidth;
            if (maxDistance <= split.Panel1MinSize)
                return;

            split.SplitterDistance = Math.Clamp(
                split.ClientSize.Width - 620,
                split.Panel1MinSize,
                maxDistance);
        };

        return split;
    }

    private Control BuildParticleBindingPanel()
    {
        _particleBindGroup = new GroupBox
        {
            Text = "Particle bind",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _particleBindStatusLabel.Dock = DockStyle.Fill;
        _particleBindStatusLabel.Text = "Select particle(s), then choose a bone.";

        _particleBindBoneCombo.Dock = DockStyle.Fill;
        _particleBindBoneCombo.DropDownStyle = ComboBoxStyle.DropDownList;

        _particleBindButton.Text = "Bind";
        _particleBindButton.Dock = DockStyle.Fill;
        StyleButton(_particleBindButton);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Viewport: left click selects, drag boxes, middle drag pans. Ctrl+click a bone also binds.",
            AutoEllipsis = true
        };

        layout.Controls.Add(_particleBindStatusLabel, 0, 0);
        layout.SetColumnSpan(_particleBindStatusLabel, 2);
        layout.Controls.Add(_particleBindBoneCombo, 0, 1);
        layout.Controls.Add(_particleBindButton, 1, 1);
        layout.Controls.Add(hint, 0, 2);
        layout.SetColumnSpan(hint, 2);
        _particleBindGroup.Controls.Add(layout);
        return _particleBindGroup;
    }
    private Control BuildBonesGroup()
    {
        var group = new GroupBox
        {
            Text = "Skeleton bones and details",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300
        };

        _boneList.Dock = DockStyle.Fill;
        _detailsBox.Dock = DockStyle.Fill;
        _detailsBox.Multiline = true;
        _detailsBox.ReadOnly = true;
        _detailsBox.ScrollBars = ScrollBars.Both;
        _detailsBox.WordWrap = false;

        split.Panel1.Controls.Add(_boneList);
        split.Panel2.Controls.Add(_detailsBox);
        group.Controls.Add(split);
        return group;
    }

    private void ConfigureParticleGrid()
    {
        _particleGrid.Dock = DockStyle.Fill;
        _particleGrid.AllowUserToAddRows = false;
        _particleGrid.AllowUserToDeleteRows = false;
        _particleGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _particleGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _particleGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _particleGrid.MultiSelect = false;
        _particleGrid.RowHeadersVisible = false;
        _particleGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        _particleGrid.ScrollBars = ScrollBars.Both;

        AddTextColumn("Index", "Index", true, 54);
        AddCheckColumn("Fixed", "Fixed", 58);
        AddTextColumn("X", "X", false, 92);
        AddTextColumn("Y", "Y", false, 92);
        AddTextColumn("Z", "Z", false, 92);
        AddTextColumn("Mass", "Mass", false, 88);
        AddTextColumn("InverseMass", "Inv Mass", false, 88);
        AddTextColumn("Radius", "Radius", false, 88);
        AddTextColumn("Friction", "Friction", false, 74);
        AddTextColumn("CollisionMask", "Mask", false, 68);
    }

    private void ConfigureEditorList()
    {
        _editorIndexList.Dock = DockStyle.Fill;
        _editorIndexList.IntegralHeight = false;
    }

    private void ConfigureEditorDetailGrid()
    {
        _editorDetailGrid.Dock = DockStyle.Fill;
        _editorDetailGrid.AllowUserToAddRows = false;
        _editorDetailGrid.AllowUserToDeleteRows = false;
        _editorDetailGrid.RowHeadersVisible = false;
        _editorDetailGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _editorDetailGrid.MultiSelect = false;
        _editorDetailGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _editorDetailGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _editorDetailGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Field", ReadOnly = true, FillWeight = 85 });
        _editorDetailGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 130 });
    }

    private void ConfigureParticleRelationshipGrid()
    {
        _particleRelationshipGrid.Dock = DockStyle.Fill;
        _particleRelationshipGrid.AllowUserToAddRows = false;
        _particleRelationshipGrid.AllowUserToDeleteRows = false;
        _particleRelationshipGrid.ReadOnly = true;
        _particleRelationshipGrid.RowHeadersVisible = false;
        _particleRelationshipGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _particleRelationshipGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _particleRelationshipGrid.ScrollBars = ScrollBars.Both;
        _particleRelationshipGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Kind", Width = 70 });
        _particleRelationshipGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", Width = 155 });
        _particleRelationshipGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Particles", HeaderText = "Particles", Width = 95 });
        _particleRelationshipGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Details", HeaderText = "Details", Width = 280 });
    }
    private void AddTextColumn(string name, string header, bool readOnly, int width)
    {
        _particleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            ReadOnly = readOnly,
            Width = width
        });
    }

    private void AddCheckColumn(string name, string header, int width)
    {
        _particleGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width
        });
    }

    private static Button MakeButton(string text, int width)
    {
        var button = new Button { Text = text, Width = width };
        StyleButton(button);
        return button;
    }

    private static void StyleButton(Button button)
    {
        button.Height = 30;
        button.Margin = new Padding(0, 0, 8, 0);
        button.AutoSize = false;
        button.UseCompatibleTextRendering = true;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(64, 64, 64);
        button.ForeColor = Color.Gainsboro;
        button.FlatAppearance.BorderColor = Color.FromArgb(115, 115, 115);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(78, 78, 78);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(88, 88, 88);
    }

    private void ConfigureExportMenu()
    {
        _exportMenu.BackColor = Color.FromArgb(48, 48, 48);
        _exportMenu.ForeColor = Color.Gainsboro;
        _exportMenu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable());

        var json = MakeExportMenuItem("JSON", () => ExportJson());
        var wiiU = MakeExportMenuItem("Wii U HKCL", () => SaveHkcl(HkclPlatform.WiiU));
        var switchItem = MakeExportMenuItem("Switch HKCL", () => SaveHkcl(HkclPlatform.Switch));
        _exportMenu.Items.Add(json);
        _exportMenu.Items.Add(wiiU);
        _exportMenu.Items.Add(switchItem);
    }

    private void ConfigureClothMenu()
    {
        _clothMenu.BackColor = Color.FromArgb(48, 48, 48);
        _clothMenu.ForeColor = Color.Gainsboro;
        _clothMenu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable());
        _clothMenu.Items.Add(MakeExportMenuItem("Rename", RenameSelectedCloth));
    }

    private static ToolStripMenuItem MakeExportMenuItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text)
        {
            BackColor = Color.FromArgb(48, 48, 48),
            ForeColor = Color.Gainsboro
        };
        item.Click += (_, _) => action();
        return item;
    }

    private sealed class DarkMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(48, 48, 48);
        public override Color ImageMarginGradientBegin => Color.FromArgb(48, 48, 48);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(48, 48, 48);
        public override Color ImageMarginGradientEnd => Color.FromArgb(48, 48, 48);
        public override Color MenuItemSelected => Color.FromArgb(70, 70, 70);
        public override Color MenuItemBorder => Color.FromArgb(110, 110, 110);
        public override Color MenuBorder => Color.FromArgb(90, 90, 90);
    }

    private void WireEvents()
    {
        _exportJsonButton.Click += (_, _) => _exportMenu.Show(_exportJsonButton, new Point(0, _exportJsonButton.Height));
        _saveWiiUButton.Click += (_, _) => SaveCurrent();
        _removeButton.Click += (_, _) => RemoveSelectedCloth();
        _mergeButton.Click += (_, _) => MergeSelectedReferenceCloth();
        _particleApplyButton.Click += (_, _) => UndoEditorChange();
        _particleRefreshButton.Click += (_, _) => RedoEditorChange();
        _particleBindButton.Click += (_, _) => BindSelectedParticlesToChosenBone();
        _addEditorItemButton.Click += (_, _) => AddEditorItemForCurrentTab();
        _editorIndexList.SelectedIndexChanged += (_, _) => RefreshSelectedEditorItem();
        _editorTabs.SelectedIndexChanged += (_, _) =>
        {
            if (_applyingSnapshot)
                return;

            var previousIndex = _editorIndexList.SelectedIndex;
            _editorPage = _editorTabs.SelectedIndex switch
            {
                1 => EditorPage.Bones,
                2 => EditorPage.Colliders,
                _ => EditorPage.Particles
            };
            if (_editorPage != EditorPage.Particles)
                _selectedParticleIndices.Clear();
            UpdatePreviewPickKind();
            UpdateAddButtonText();
            MoveEditorContentToSelectedTab();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: previousIndex);
        };
        _editorDetailGrid.CellBeginEdit += (_, _) =>
        {
            if (!_current.IsReadOnlyExternal)
                _pendingEditSnapshot ??= CaptureCurrentEditorSnapshot();
        };
        _editorDetailGrid.CellEndEdit += (_, _) => CommitEditorDetailChange();
        _editorDetailGrid.CellValueChanged += (_, _) => CommitEditorDetailChange();
        _editorDetailGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_editorDetailGrid.IsCurrentCellDirty)
                _editorDetailGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _editorDetailGrid.CellMouseDown += (_, e) =>
        {
            if (_current.IsReadOnlyExternal)
                return;

            var valueColumn = _editorDetailGrid.Columns["Value"];
            if (e.RowIndex >= 0 && valueColumn != null && e.ColumnIndex == valueColumn.Index)
            {
                _pendingEditSnapshot ??= CaptureCurrentEditorSnapshot();
                _editorDetailGrid.CurrentCell = _editorDetailGrid[e.ColumnIndex, e.RowIndex];
                BeginInvoke(new Action(() => _editorDetailGrid.BeginEdit(true)));
            }
        };
        _particlePreview.ItemPicked += (_, e) => HandlePreviewPick(e);
        _particlePreview.ParticlesSelected += (_, e) => SelectParticlesFromViewport(e.ParticleIndices, e.AddToSelection);
        _particlePreview.ParticleMoveStarted += (_, _) => BeginViewportParticleMove();
        _particlePreview.ParticlesMoved += (_, e) => MoveSelectedParticles(e.Delta);
        _particlePreview.ParticlesScaled += (_, e) => ScaleSelectedParticles(e.Factor, e.Axis);
        _particlePreview.ParticlesRotated += (_, e) => RotateSelectedParticles(e.Radians, e.Axis);
        _particlePreview.MirrorRequested += (_, e) => MirrorSelectedParticles(e.Axis, e.Local);
        _particlePreview.LinkRequested += (_, _) => LinkSelectedParticles();
        _particlePreview.DeleteRequested += (_, _) => DeleteSelectedEditorItem();
        _particlePreview.ParticleMoveEnded += (_, _) => EndViewportParticleMove();
        _particlePreview.ParticleMoveCanceled += (_, _) => CancelViewportParticleMove();
        _directEditButton.Click += (_, _) =>
        {
            _directEditMode = !_directEditMode;
            UpdateModeLayout();
            RefreshParticleGrid();
            UpdateButtons();
        };

        _clothList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
                return;

            var index = _clothList.IndexFromPoint(e.Location);
            if (index >= 0)
                _clothList.SelectedIndex = index;
        };
        _clothMenu.Opening += (_, e) => e.Cancel = !_current.HasDocument || _current.IsBphhb || _clothList.SelectedIndex < 0;
        _clothList.SelectedIndexChanged += (_, _) => RefreshSelectedDetails();

        DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };

        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                LoadCurrent(files[0]);
        };
    }

    private void MoveEditorContentToSelectedTab()
    {
        if (_editorTabs.SelectedTab != null && !_editorTabs.SelectedTab.Controls.Contains(_editorContentPanel))
            _editorTabs.SelectedTab.Controls.Add(_editorContentPanel);
    }

    private void HandlePreviewPick(PreviewPickEventArgs e)
    {
        if (!_directEditMode || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        if (!IsPickAllowedForCurrentTab(e.Kind))
            return;

        if (e.Index < 0)
        {
            if (!e.AddToSelection)
                ClearEditorSelection();
            return;
        }

        SelectEditorItem(e.Kind, e.Index, e.AddToSelection);
    }

    private void SelectParticlesFromViewport(IReadOnlyList<int> particleIndices, bool addToSelection)
    {
        if (!_directEditMode || !_current.HasDocument || _editorPage != EditorPage.Particles)
            return;

        if (particleIndices.Count == 0)
        {
            if (!addToSelection)
                ClearEditorSelection();
            return;
        }

        var previousListIndex = _editorIndexList.SelectedIndex;
        if (!addToSelection)
            _selectedParticleIndices.Clear();
        foreach (var index in particleIndices)
            _selectedParticleIndices.Add(index);

        var firstListIndex = addToSelection && previousListIndex >= 0
            ? previousListIndex
            : _particleRows.FindIndex(x => x.Index == particleIndices[0]);
        _editorPage = EditorPage.Particles;
        _editorTabs.SelectedIndex = 0;
        MoveEditorContentToSelectedTab();
        RefreshParticleGrid(resetCamera: false, selectedListIndex: firstListIndex);
    }

    private void ClearEditorSelection()
    {
        _selectedParticleIndices.Clear();
        _editorIndexList.ClearSelected();
        _editorDetailGrid.Rows.Clear();
        _particleRelationshipGrid.Rows.Clear();
        _particlePreview.SelectedParticleIndex = -1;
        _particlePreview.SelectedBoneIndex = -1;
        _particlePreview.SelectedColliderIndex = -1;
        _particlePreview.SetSelectedParticleIndices(Array.Empty<int>());
        RefreshParticleBindingPanel();
        _statusLabel.Text = "Selection cleared.";
    }

    private bool IsPickAllowedForCurrentTab(PreviewPickKind kind)
    {
        return _editorPage switch
        {
            EditorPage.Bones => kind == PreviewPickKind.Bone,
            EditorPage.Colliders => kind == PreviewPickKind.Collider,
            _ => kind == PreviewPickKind.Particle
        };
    }

    private void UpdatePreviewPickKind()
    {
        _particlePreview.PickKind = _editorPage switch
        {
            EditorPage.Bones => PreviewPickKind.Bone,
            EditorPage.Colliders => PreviewPickKind.Collider,
            _ => PreviewPickKind.Particle
        };
    }

    private void BeginViewportParticleMove()
    {
        if (_current.IsReadOnlyExternal || _selectedParticleIndices.Count == 0 || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        _viewportMoveSnapshot ??= CaptureEditorSnapshot(EditorPage.Particles, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
    }

    private void MoveSelectedParticles(System.Numerics.Vector3 delta)
    {
        if (_current.IsReadOnlyExternal || _selectedParticleIndices.Count == 0 || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        foreach (var particle in _particleRows.Where(p => _selectedParticleIndices.Contains(p.Index)))
        {
            particle.X += delta.X;
            particle.Y += delta.Y;
            particle.Z += delta.Z;
        }

        _viewportTransformChanged = true;
        _particlePreview.UpdateParticlePreviewRows(_particleRows);
    }

    private void ScaleSelectedParticles(float factor, System.Numerics.Vector3? axis)
    {
        if (_current.IsReadOnlyExternal || _selectedParticleIndices.Count == 0 || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        var selected = _particleRows.Where(p => _selectedParticleIndices.Contains(p.Index)).ToList();
        if (selected.Count == 0)
            return;

        var center = GetParticleSelectionCenter(selected);
        foreach (var particle in selected)
        {
            if (axis.HasValue)
            {
                var normalizedAxis = NormalizeOrDefault(axis.Value, System.Numerics.Vector3.UnitX);
                var offset = new System.Numerics.Vector3(particle.X - center.X, particle.Y - center.Y, particle.Z - center.Z);
                var alongAxis = System.Numerics.Vector3.Dot(offset, normalizedAxis);
                var delta = normalizedAxis * (alongAxis * (factor - 1.0f));
                particle.X += delta.X;
                particle.Y += delta.Y;
                particle.Z += delta.Z;
            }
            else
            {
                particle.X = center.X + (particle.X - center.X) * factor;
                particle.Y = center.Y + (particle.Y - center.Y) * factor;
                particle.Z = center.Z + (particle.Z - center.Z) * factor;
            }
        }

        _viewportTransformChanged = true;
        _particlePreview.UpdateParticlePreviewRows(_particleRows);
    }

    private void RotateSelectedParticles(float radians, System.Numerics.Vector3 axis)
    {
        if (_current.IsReadOnlyExternal || _selectedParticleIndices.Count == 0 || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        var selected = _particleRows.Where(p => _selectedParticleIndices.Contains(p.Index)).ToList();
        if (selected.Count == 0)
            return;

        var center = GetParticleSelectionCenter(selected);
        var normalizedAxis = NormalizeOrDefault(axis, System.Numerics.Vector3.UnitY);
        var rotation = System.Numerics.Matrix4x4.CreateFromAxisAngle(normalizedAxis, radians);
        foreach (var particle in selected)
        {
            var offset = new System.Numerics.Vector3(particle.X - center.X, particle.Y - center.Y, particle.Z - center.Z);
            var rotated = System.Numerics.Vector3.Transform(offset, rotation);
            particle.X = center.X + rotated.X;
            particle.Y = center.Y + rotated.Y;
            particle.Z = center.Z + rotated.Z;
        }

        _viewportTransformChanged = true;
        _particlePreview.UpdateParticlePreviewRows(_particleRows);
    }

    private void MirrorSelectedParticles(char axisName, bool local)
    {
        if (_current.IsReadOnlyExternal || _selectedParticleIndices.Count == 0 || !_current.HasDocument || _clothList.SelectedIndex < 0)
        {
            _statusLabel.Text = "Select one or more particles before mirroring.";
            return;
        }

        var selected = _particleRows.Where(p => _selectedParticleIndices.Contains(p.Index)).ToList();
        if (selected.Count == 0)
            return;

        var snapshot = CaptureEditorSnapshot(EditorPage.Particles, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
        var pivot = local ? GetParticleSelectionCenter(selected) : _particlePreview.ViewRoot;
        var axis = GetMirrorAxis(axisName, local);
        foreach (var particle in selected)
        {
            var offset = new System.Numerics.Vector3(particle.X - pivot.X, particle.Y - pivot.Y, particle.Z - pivot.Z);
            var reflected = offset - 2.0f * axis * System.Numerics.Vector3.Dot(offset, axis);
            particle.X = pivot.X + reflected.X;
            particle.Y = pivot.Y + reflected.Y;
            particle.Z = pivot.Z + reflected.Z;
        }

        _current.UpdateParticleRows(_clothList.SelectedIndex, _particleRows);
        _particlePreview.UpdateParticlePreviewRows(_particleRows);
        PushUndo(snapshot);
        _redoStack.Clear();
        RefreshSelectedEditorItem();
        _statusLabel.Text = $"Mirrored selected particles on {(local ? "local" : "global")} {axisName}.";
    }

    private System.Numerics.Vector3 GetMirrorAxis(char axisName, bool local)
    {
        var globalAxis = axisName switch
        {
            'Y' => System.Numerics.Vector3.UnitY,
            'Z' => System.Numerics.Vector3.UnitZ,
            _ => System.Numerics.Vector3.UnitX
        };

        if (!local)
            return globalAxis;

        var bone = GetActiveLocalBone();
        if (bone == null)
            return globalAxis;

        var rotation = new System.Numerics.Quaternion(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW);
        if (rotation.LengthSquared() < 0.000001f)
            return globalAxis;

        rotation = System.Numerics.Quaternion.Normalize(rotation);
        return NormalizeOrDefault(System.Numerics.Vector3.Transform(globalAxis, rotation), globalAxis);
    }

    private BoneEditRow? GetActiveLocalBone()
    {
        if (_editorPage == EditorPage.Bones && _editorIndexList.SelectedIndex >= 0 && _editorIndexList.SelectedIndex < _boneRows.Count)
            return _boneRows[_editorIndexList.SelectedIndex];

        if (_editorPage == EditorPage.Colliders && _editorIndexList.SelectedIndex >= 0 && _editorIndexList.SelectedIndex < _colliderRows.Count)
        {
            var boneIndex = _colliderRows[_editorIndexList.SelectedIndex].BoneIndex;
            return _boneRows.FirstOrDefault(b => b.Index == boneIndex);
        }

        return null;
    }

    private static System.Numerics.Vector3 NormalizeOrDefault(System.Numerics.Vector3 value, System.Numerics.Vector3 fallback)
    {
        return value.LengthSquared() < 0.000001f
            ? fallback
            : System.Numerics.Vector3.Normalize(value);
    }

    private static System.Numerics.Vector3 GetParticleSelectionCenter(IReadOnlyList<ParticleEditRow> selected)
    {
        return new System.Numerics.Vector3(
            selected.Average(p => p.X),
            selected.Average(p => p.Y),
            selected.Average(p => p.Z));
    }

    private void EndViewportParticleMove()
    {
        if (_viewportMoveSnapshot == null)
            return;

        if (_viewportTransformChanged)
        {
            _current.UpdateParticleRows(_clothList.SelectedIndex, _particleRows);
            PushUndo(_viewportMoveSnapshot);
            _redoStack.Clear();
            RefreshSelectedEditorItem();
        }
        _viewportMoveSnapshot = null;
        _viewportTransformChanged = false;
        UpdateButtons();
        _statusLabel.Text = "Transformed selected particle(s).";
    }

    private void CancelViewportParticleMove()
    {
        if (_viewportMoveSnapshot == null)
            return;

        ApplyEditorSnapshot(_viewportMoveSnapshot);
        _viewportMoveSnapshot = null;
        _viewportTransformChanged = false;
        _statusLabel.Text = "Canceled transform.";
    }

    private void BindSelectedParticlesToChosenBone()
    {
        if (_current.IsReadOnlyExternal)
            return;

        if (_particleBindBoneCombo.SelectedItem is not BoneComboItem item)
            return;

        var indices = _selectedParticleIndices.Count > 0
            ? _selectedParticleIndices.ToArray()
            : TryGetSelectedParticle(out var activeParticle)
                ? new[] { activeParticle.Index }
                : Array.Empty<int>();

        if (indices.Length == 0)
            return;

        BindParticlesToBone(indices, item.Index);
    }

    private void RefreshParticleBindingPanel()
    {
        var previousIndex = _particleBindBoneCombo.SelectedItem is BoneComboItem selected ? selected.Index : -1;
        _particleBindBoneCombo.Items.Clear();
        foreach (var bone in _boneRows)
            _particleBindBoneCombo.Items.Add(new BoneComboItem { Index = bone.Index, Name = bone.Name });

        if (_particleBindBoneCombo.Items.Count > 0)
        {
            var match = _particleBindBoneCombo.Items
                .OfType<BoneComboItem>()
                .Select((item, index) => new { item, index })
                .FirstOrDefault(x => x.item.Index == previousIndex);
            _particleBindBoneCombo.SelectedIndex = match?.index ?? 0;
        }

        var selectedCount = _selectedParticleIndices.Count;
        if (selectedCount == 0 && TryGetSelectedParticle(out _))
            selectedCount = 1;

        _particleBindStatusLabel.Text = selectedCount <= 1
            ? "Selected: 1 particle"
            : $"Selected: {selectedCount} particles";
        _particleBindButton.Enabled = !_current.IsReadOnlyExternal && selectedCount > 0 && _particleBindBoneCombo.Items.Count > 0;
    }

    private bool TryGetSelectedParticle(out ParticleEditRow particle)
    {
        particle = null!;
        if (_editorPage != EditorPage.Particles || _editorIndexList.SelectedIndex < 0 || _editorIndexList.SelectedIndex >= _particleRows.Count)
            return false;

        particle = _particleRows[_editorIndexList.SelectedIndex];
        return true;
    }

    private sealed class BoneComboItem
    {
        public int Index { get; init; }
        public string Name { get; init; } = string.Empty;

        public override string ToString()
        {
            return $"{Index}: {Name}";
        }
    }

    private void SelectEditorItem(PreviewPickKind kind, int index, bool addToSelection = false)
    {
        var page = kind switch
        {
            PreviewPickKind.Bone => EditorPage.Bones,
            PreviewPickKind.Collider => EditorPage.Colliders,
            _ => EditorPage.Particles
        };
        var clickedListIndex = page switch
        {
            EditorPage.Bones => _boneRows.FindIndex(x => x.Index == index),
            EditorPage.Colliders => _colliderRows.FindIndex(x => x.Index == index),
            _ => _particleRows.FindIndex(x => x.Index == index)
        };

        if (clickedListIndex < 0)
            return;

        var listIndex = clickedListIndex;
        if (page == EditorPage.Particles)
        {
            if (addToSelection && _editorIndexList.SelectedIndex >= 0 && _selectedParticleIndices.Count > 0)
                listIndex = _editorIndexList.SelectedIndex;
            else if (!addToSelection)
                _selectedParticleIndices.Clear();
            _selectedParticleIndices.Add(index);
        }

        _editorPage = page;
        _editorTabs.SelectedIndex = page switch
        {
            EditorPage.Bones => 1,
            EditorPage.Colliders => 2,
            _ => 0
        };
        MoveEditorContentToSelectedTab();
        RefreshParticleGrid(resetCamera: false, selectedListIndex: listIndex);
    }

    private void BindParticleToBone(int particleIndex, int boneIndex)
    {
        BindParticlesToBone(new[] { particleIndex }, boneIndex);
    }

    private void BindParticlesToBone(IReadOnlyCollection<int> particleIndices, int boneIndex)
    {
        RunGuarded(() =>
        {
            var firstIndex = _particleRows.FindIndex(x => particleIndices.Contains(x.Index));
            var snapshot = CaptureEditorSnapshot(EditorPage.Particles, _clothList.SelectedIndex, firstIndex);
            var preview = _current.GetParticlePreview(_clothList.SelectedIndex);
            var bone = preview.Bones.FirstOrDefault(x => x.Index == boneIndex);
            if (bone == null || firstIndex < 0)
                return;

            var anchorIndex = particleIndices.Contains(_particleRows.ElementAtOrDefault(_editorIndexList.SelectedIndex)?.Index ?? -1)
                ? _particleRows[_editorIndexList.SelectedIndex].Index
                : particleIndices.First();
            var anchor = _particleRows.FirstOrDefault(x => x.Index == anchorIndex);
            if (anchor == null)
                return;

            var deltaX = bone.Position.X - anchor.X;
            var deltaY = bone.Position.Y - anchor.Y;
            var deltaZ = bone.Position.Z - anchor.Z;
            foreach (var particle in _particleRows.Where(x => particleIndices.Contains(x.Index)))
            {
                particle.X += deltaX;
                particle.Y += deltaY;
                particle.Z += deltaZ;
                if (particle.Index == anchorIndex)
                {
                    particle.Fixed = true;
                    particle.Mass = 0.0f;
                    particle.InverseMass = 0.0f;
                    particle.CollisionMask = 0;
                }
            }

            _current.UpdateParticleRows(_clothList.SelectedIndex, _particleRows);
            PushUndo(snapshot);
            _redoStack.Clear();
            _selectedParticleIndices.Clear();
            foreach (var index in particleIndices)
                _selectedParticleIndices.Add(index);
            _editorPage = EditorPage.Particles;
            _editorTabs.SelectedIndex = 0;
            MoveEditorContentToSelectedTab();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: firstIndex);
            _statusLabel.Text = particleIndices.Count == 1
                ? $"Bound particle {particleIndices.First()} to bone {bone.Name}."
                : $"Snapped {particleIndices.Count} selected particles to bone {bone.Name} using particle {anchorIndex} as the anchor.";
        });
    }

    private void LinkSelectedParticles()
    {
        if (!_directEditMode || _current.IsReadOnlyExternal || !_current.HasDocument || _clothList.SelectedIndex < 0 || _editorPage != EditorPage.Particles)
            return;

        var selected = _selectedParticleIndices.OrderBy(x => x).ToList();
        if (selected.Count is not (2 or 3))
        {
            _statusLabel.Text = "Select exactly 2 particles for a link, or exactly 3 for a triangle.";
            return;
        }

        RunGuarded(() =>
        {
            var snapshot = CaptureFullEditorSnapshot(EditorPage.Particles, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            _current.LinkParticles(_clothList.SelectedIndex, selected);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: _editorIndexList.SelectedIndex);
            _statusLabel.Text = selected.Count == 2
                ? $"Linked particles {selected[0]} and {selected[1]}."
                : $"Created triangle/link set for particles {selected[0]}, {selected[1]}, {selected[2]}.";
        });
    }

    private void DeleteSelectedEditorItem()
    {
        if (!_directEditMode || _current.IsReadOnlyExternal || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        if (_editorPage == EditorPage.Particles && _selectedParticleIndices.Count == 0 && TryGetSelectedParticle(out var activeParticle))
            _selectedParticleIndices.Add(activeParticle.Index);

        if ((_editorPage == EditorPage.Particles && _selectedParticleIndices.Count == 0)
            || (_editorPage == EditorPage.Bones && _editorIndexList.SelectedIndex < 0)
            || (_editorPage == EditorPage.Colliders && _editorIndexList.SelectedIndex < 0))
        {
            _statusLabel.Text = "Nothing selected to delete.";
            return;
        }

        var label = _editorPage switch
        {
            EditorPage.Particles => _selectedParticleIndices.Count <= 1 ? "selected particle" : $"{_selectedParticleIndices.Count} selected particles",
            EditorPage.Bones => "selected bone",
            EditorPage.Colliders => "selected collider",
            _ => "selection"
        };

        if (MessageBox.Show(this, $"Are you sure you wish to delete this {label}?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        RunGuarded(() =>
        {
            var snapshot = CaptureFullEditorSnapshot(_editorPage, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            if (_editorPage == EditorPage.Particles)
            {
                foreach (var index in _selectedParticleIndices.OrderByDescending(x => x).ToArray())
                    _current.DeleteParticle(_clothList.SelectedIndex, index);
                _selectedParticleIndices.Clear();
            }
            else if (_editorPage == EditorPage.Bones)
            {
                if (_editorIndexList.SelectedIndex < 0 || _editorIndexList.SelectedIndex >= _boneRows.Count)
                    return;
                _current.DeleteBone(_clothList.SelectedIndex, _boneRows[_editorIndexList.SelectedIndex].Index);
            }
            else
            {
                if (_editorIndexList.SelectedIndex < 0 || _editorIndexList.SelectedIndex >= _colliderRows.Count)
                    return;
                _current.DeleteCollider(_clothList.SelectedIndex, _colliderRows[_editorIndexList.SelectedIndex].Index);
            }

            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false);
            _statusLabel.Text = $"Deleted {label}.";
        });
    }

    private void UpdateAddButtonText()
    {
        _addEditorItemButton.Text = _editorPage switch
        {
            EditorPage.Bones => "Add Bone",
            EditorPage.Colliders => "Add Collider",
            _ => "Add Particle"
        };
    }

    private void AddEditorItemForCurrentTab()
    {
        if (!_directEditMode || _current.IsReadOnlyExternal || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        RunGuarded(() =>
        {
            var snapshot = CaptureFullEditorSnapshot(_editorPage, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            int newIndex;
            if (_editorPage == EditorPage.Bones)
            {
                var source = _editorIndexList.SelectedIndex >= 0 && _editorIndexList.SelectedIndex < _boneRows.Count
                    ? _boneRows[_editorIndexList.SelectedIndex]
                    : null;
                newIndex = _current.AddBone(_clothList.SelectedIndex, source);
            }
            else if (_editorPage == EditorPage.Colliders)
            {
                var source = _editorIndexList.SelectedIndex >= 0 && _editorIndexList.SelectedIndex < _colliderRows.Count
                    ? _colliderRows[_editorIndexList.SelectedIndex]
                    : null;
                var targetBone = _boneRows.FirstOrDefault(b => b.Index == source?.BoneIndex)
                    ?? _boneRows.FirstOrDefault();
                newIndex = _current.AddCollider(_clothList.SelectedIndex, source, targetBone);
            }
            else
            {
                var source = _editorIndexList.SelectedIndex >= 0 && _editorIndexList.SelectedIndex < _particleRows.Count
                    ? _particleRows[_editorIndexList.SelectedIndex]
                    : null;
                newIndex = _current.AddParticle(_clothList.SelectedIndex, source);
                _selectedParticleIndices.Clear();
                _selectedParticleIndices.Add(newIndex);
            }

            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: newIndex);
            _statusLabel.Text = $"Added {_editorPage.ToString().TrimEnd('s').ToLowerInvariant()} {newIndex}.";
        });
    }

    private void OpenCurrentFile()
    {
        using var dialog = MakeOpenDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadCurrent(dialog.FileName);
    }

    private void OpenReferenceFile()
    {
        using var dialog = MakeOpenDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
            LoadReference(dialog.FileName);
    }

    private static OpenFileDialog MakeOpenDialog()
    {
        return new OpenFileDialog
        {
            Title = "Open Physics File",
            Filter = "Physics files|*.hkcl;*.bphcl;*.bphhb;*.json|HKCL|*.hkcl|BPHCL|*.bphcl|BPHHB helper bones|*.bphhb|JSON|*.json|All files|*.*",
            CheckFileExists = true
        };
    }

    private void LoadCurrent(string path)
    {
        RunGuarded(() =>
        {
            _current.Load(path);
            var extension = Path.GetExtension(path);
            _currentSavePath = extension.Equals(".hkcl", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bphcl", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase)
                ? path
                : null;
            _currentSavePlatform = HkclPlatform.WiiU;
            ClearUndoHistory();
            RefreshCurrentLists();
            _statusLabel.Text = $"Loaded {Path.GetFileName(path)}";
        });
    }

    private void LoadReference(string path)
    {
        RunGuarded(() =>
        {
            _reference.Load(path);
            RefreshReferenceLists();
            _statusLabel.Text = $"Loaded reference {Path.GetFileName(path)}";
        });
    }

    private void RefreshCurrentLists(int? preferredSelectionIndex = null)
    {
        _clothList.Items.Clear();
        foreach (var cloth in _current.GetClothSummaries())
            _clothList.Items.Add(cloth);

        if (_clothList.Items.Count > 0)
        {
            var selection = preferredSelectionIndex ?? 0;
            _clothList.SelectedIndex = Math.Clamp(selection, 0, _clothList.Items.Count - 1);
        }

        RefreshSelectedDetails();
        UpdateButtons();
    }

    private void RefreshReferenceLists()
    {
        _referenceClothList.Items.Clear();
        foreach (var cloth in _reference.GetClothSummaries())
            _referenceClothList.Items.Add(cloth);

        if (_referenceClothList.Items.Count > 0)
            _referenceClothList.SelectedIndex = 0;

        UpdateButtons();
    }

    private void RefreshSelectedDetails()
    {
        _boneList.Items.Clear();
        _detailsBox.Clear();

        if (!_current.HasDocument || _clothList.SelectedIndex < 0)
        {
            RefreshParticleGrid();
            return;
        }

        foreach (var bone in _current.GetSkeletonBones(_clothList.SelectedIndex))
            _boneList.Items.Add(bone);

        _detailsBox.Text = _current.GetClothDetails(_clothList.SelectedIndex);
        RefreshParticleGrid();
        UpdateButtons();
    }

    private void RefreshParticleGrid(bool resetCamera = true, int? selectedListIndex = null)
    {
        UpdatePreviewPickKind();
        _editorDetailGrid.ReadOnly = _current.IsReadOnlyExternal;
        if (!_directEditMode)
        {
            _particlePreview.SetData(null);
            _particleRows.Clear();
            _boneRows.Clear();
            _colliderRows.Clear();
            _editorIndexList.Items.Clear();
            _editorDetailGrid.Rows.Clear();
            _particleRelationshipGrid.Rows.Clear();
            return;
        }

        _updatingParticleGrid = true;
        try
        {
            var previousIndex = selectedListIndex ?? _editorIndexList.SelectedIndex;
            _particleRows.Clear();
            _boneRows.Clear();
            _colliderRows.Clear();
            _editorIndexList.Items.Clear();
            _editorDetailGrid.Rows.Clear();
            _particleRelationshipGrid.Rows.Clear();
            if (!_current.HasDocument || _clothList.SelectedIndex < 0)
            {
                _particlePreview.SetData(null);
                return;
            }

            _particleRows = _current.GetParticleRows(_clothList.SelectedIndex).ToList();
            _boneRows = _current.GetBoneRows(_clothList.SelectedIndex).ToList();
            _colliderRows = _current.GetColliderRows(_clothList.SelectedIndex).ToList();
            RefreshParticleBindingPanel();
            if (_editorPage == EditorPage.Bones)
            {
                foreach (var bone in _boneRows)
                    _editorIndexList.Items.Add($"{bone.Index}: {bone.Name}");
            }
            else if (_editorPage == EditorPage.Colliders)
            {
                foreach (var collider in _colliderRows)
                    _editorIndexList.Items.Add($"{collider.Index}: {collider.Name}");
            }
            else
            {
                foreach (var particle in _particleRows)
                    _editorIndexList.Items.Add($"#{particle.Index}");
            }

            _particlePreview.SetData(_current.GetParticlePreview(_clothList.SelectedIndex), resetCamera);
            _particlePreview.SetSelectedParticleIndices(_selectedParticleIndices);
            if (_editorIndexList.Items.Count > 0 && previousIndex >= 0)
                _editorIndexList.SelectedIndex = Math.Clamp(previousIndex, 0, _editorIndexList.Items.Count - 1);
            else
                _editorIndexList.ClearSelected();
        }
        finally
        {
            _updatingParticleGrid = false;
        }

        RefreshSelectedEditorItem();
    }

    private void RefreshSelectedEditorItem()
    {
        if (_updatingParticleGrid || !_directEditMode)
            return;

        _particleRelationshipGrid.Rows.Clear();
        _editorDetailGrid.Rows.Clear();
        _particlePreview.SelectedParticleIndex = -1;
        _particlePreview.SelectedBoneIndex = -1;
        _particlePreview.SelectedColliderIndex = -1;
        if (!_current.HasDocument || _clothList.SelectedIndex < 0 || _editorIndexList.SelectedIndex < 0)
            return;

        if (_relationshipGroup != null)
            _relationshipGroup.Visible = _editorPage == EditorPage.Particles;
        if (_particleBindGroup != null)
            _particleBindGroup.Enabled = _editorPage == EditorPage.Particles && !_current.IsReadOnlyExternal;

        if (_editorPage == EditorPage.Bones)
        {
            var bone = _boneRows.ElementAtOrDefault(_editorIndexList.SelectedIndex);
            if (bone == null)
                return;

            FillBoneDetailGrid(bone);
            _particlePreview.SelectedBoneIndex = bone.Index;
            _particlePreview.Invalidate();
            return;
        }

        if (_editorPage == EditorPage.Colliders)
        {
            var collider = _colliderRows.ElementAtOrDefault(_editorIndexList.SelectedIndex);
            if (collider == null)
                return;

            FillColliderDetailGrid(collider);
            _particlePreview.SelectedColliderIndex = collider.Index;
            _particlePreview.Invalidate();
            return;
        }

        var particle = _particleRows.ElementAtOrDefault(_editorIndexList.SelectedIndex);
        if (particle == null)
            return;

        var particleIndex = particle.Index;
        if (_selectedParticleIndices.Count == 0 || !_selectedParticleIndices.Contains(particleIndex))
        {
            _selectedParticleIndices.Clear();
            _selectedParticleIndices.Add(particleIndex);
        }

        FillParticleDetailGrid(particle);
        _particlePreview.SelectedParticleIndex = particleIndex;
        _particlePreview.SetSelectedParticleIndices(_selectedParticleIndices);
        _particlePreview.Invalidate();
        RefreshParticleBindingPanel();
        foreach (var relation in _current.GetParticleRelationships(_clothList.SelectedIndex, particleIndex))
            _particleRelationshipGrid.Rows.Add(relation.Kind, relation.Name, relation.Particles, relation.Details);
    }

    private void ApplyParticleGridEdits()
    {
        if (_current.IsReadOnlyExternal || _updatingParticleGrid || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        RunGuarded(() =>
        {
            _editorDetailGrid.EndEdit();
            if (_editorPage == EditorPage.Bones)
            {
                ApplySelectedBoneDetailsToCache();
                _current.UpdateBoneRows(_clothList.SelectedIndex, _boneRows);
            }
            else if (_editorPage == EditorPage.Colliders)
            {
                ApplySelectedColliderDetailsToCache();
                _current.UpdateColliderRows(_colliderRows);
            }
            else
            {
                ApplySelectedParticleDetailsToCache();
                _current.UpdateParticleRows(_clothList.SelectedIndex, _particleRows);
            }
            RefreshSelectedDetails();
            _statusLabel.Text = _editorPage switch
            {
                EditorPage.Bones => "Applied bone edits.",
                EditorPage.Colliders => "Applied collider edits.",
                _ => "Applied particle edits."
            };
        });
    }

    private void CommitEditorDetailChange()
    {
        if (_current.IsReadOnlyExternal || _updatingParticleGrid || _applyingSnapshot || _pendingEditSnapshot == null || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        RunGuarded(() =>
        {
            var snapshot = _pendingEditSnapshot;
            _pendingEditSnapshot = null;
            ApplySelectedDetailsToCache();
            ApplyCurrentRowsToDocument();
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshPreview(resetCamera: false);
            _statusLabel.Text = "Updated value.";
        });
    }

    private void ApplySelectedDetailsToCache()
    {
        if (_editorPage == EditorPage.Bones)
            ApplySelectedBoneDetailsToCache();
        else if (_editorPage == EditorPage.Colliders)
            ApplySelectedColliderDetailsToCache();
        else
            ApplySelectedParticleDetailsToCache();
    }

    private void ApplyCurrentRowsToDocument()
    {
        if (_clothList.SelectedIndex < 0)
            return;

        if (_editorPage == EditorPage.Bones)
            _current.UpdateBoneRows(_clothList.SelectedIndex, _boneRows);
        else if (_editorPage == EditorPage.Colliders)
            _current.UpdateColliderRows(_colliderRows);
        else
            _current.UpdateParticleRows(_clothList.SelectedIndex, _particleRows);
    }

    private void RefreshPreview(bool resetCamera)
    {
        if (!_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        _particlePreview.SetData(_current.GetParticlePreview(_clothList.SelectedIndex), resetCamera);
        RefreshSelectedEditorItem();
    }

    private EditorSnapshot? CaptureCurrentEditorSnapshot()
    {
        if (!_current.HasDocument || _clothList.SelectedIndex < 0)
            return null;

        return CaptureEditorSnapshot(_editorPage, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
    }

    private EditorSnapshot CaptureFullEditorSnapshot(EditorPage page, int clothIndex, int selectedIndex)
    {
        return new EditorSnapshot
        {
            ClothIndex = clothIndex,
            Page = page,
            SelectedIndex = selectedIndex,
            RawState = _current.CaptureState()
        };
    }

    private EditorSnapshot CaptureEditorSnapshot(EditorPage page, int clothIndex, int selectedIndex)
    {
        return new EditorSnapshot
        {
            ClothIndex = clothIndex,
            Page = page,
            SelectedIndex = selectedIndex,
            Particles = page == EditorPage.Particles ? _current.GetParticleRows(clothIndex).Select(CloneParticle).ToList() : null,
            Bones = page == EditorPage.Bones ? _current.GetBoneRows(clothIndex).Select(CloneBone).ToList() : null,
            Colliders = page == EditorPage.Colliders ? _current.GetColliderRows(clothIndex).Select(CloneCollider).ToList() : null
        };
    }

    private void PushUndo(EditorSnapshot snapshot)
    {
        _undoStack.Push(snapshot);
        while (_undoStack.Count > 50)
        {
            var trimmed = _undoStack.Reverse().Take(50).Reverse().ToArray();
            _undoStack.Clear();
            foreach (var item in trimmed)
                _undoStack.Push(item);
        }
    }

    private void UndoEditorChange()
    {
        if (_undoStack.Count == 0 || !_current.HasDocument)
            return;

        RunGuarded(() =>
        {
            var previous = _undoStack.Pop();
            _redoStack.Push(previous.RawState != null
                ? CaptureFullEditorSnapshot(previous.Page, previous.ClothIndex, previous.SelectedIndex)
                : CaptureEditorSnapshot(previous.Page, previous.ClothIndex, previous.SelectedIndex));
            ApplyEditorSnapshot(previous);
            _statusLabel.Text = "Undid editor change.";
        });
    }

    private void RedoEditorChange()
    {
        if (_redoStack.Count == 0 || !_current.HasDocument)
            return;

        RunGuarded(() =>
        {
            var next = _redoStack.Pop();
            _undoStack.Push(next.RawState != null
                ? CaptureFullEditorSnapshot(next.Page, next.ClothIndex, next.SelectedIndex)
                : CaptureEditorSnapshot(next.Page, next.ClothIndex, next.SelectedIndex));
            ApplyEditorSnapshot(next);
            _statusLabel.Text = "Redid editor change.";
        });
    }

    private void ApplyEditorSnapshot(EditorSnapshot snapshot)
    {
        _applyingSnapshot = true;
        try
        {
            if (snapshot.RawState != null)
            {
                var camera = _particlePreview.CaptureCameraState();
                _current.RestoreState(snapshot.RawState);
                RefreshCurrentLists();
                _editorPage = snapshot.Page;
                _editorTabs.SelectedIndex = snapshot.Page switch
                {
                    EditorPage.Bones => 1,
                    EditorPage.Colliders => 2,
                    _ => 0
                };
                MoveEditorContentToSelectedTab();
                RefreshParticleGrid(resetCamera: false, selectedListIndex: snapshot.SelectedIndex);
                _particlePreview.RestoreCameraState(camera);
                return;
            }

            if (snapshot.Particles != null)
                _current.UpdateParticleRows(snapshot.ClothIndex, snapshot.Particles.Select(CloneParticle));
            if (snapshot.Bones != null)
                _current.UpdateBoneRows(snapshot.ClothIndex, snapshot.Bones.Select(CloneBone));
            if (snapshot.Colliders != null)
                _current.UpdateColliderRows(snapshot.Colliders.Select(CloneCollider));

            _editorPage = snapshot.Page;
            _editorTabs.SelectedIndex = snapshot.Page switch
            {
                EditorPage.Bones => 1,
                EditorPage.Colliders => 2,
                _ => 0
            };
            MoveEditorContentToSelectedTab();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: snapshot.SelectedIndex);
        }
        finally
        {
            _applyingSnapshot = false;
        }
    }

    private void ClearUndoHistory()
    {
        _pendingEditSnapshot = null;
        _viewportMoveSnapshot = null;
        _viewportTransformChanged = false;
        _selectedParticleIndices.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void FillParticleDetailGrid(ParticleEditRow particle)
    {
        _editorDetailGrid.Rows.Clear();
        AddParticleDetail("Index", particle.Index.ToString(CultureInfo.InvariantCulture), true);
        AddParticleBoolDetail("Fixed", particle.Fixed);
        AddParticleDetail("X", FormatFloat(particle.X), false);
        AddParticleDetail("Y", FormatFloat(particle.Y), false);
        AddParticleDetail("Z", FormatFloat(particle.Z), false);
        AddParticleDetail("Mass", FormatFloat(particle.Mass), false);
        AddParticleDetail("Inv Mass", FormatFloat(particle.InverseMass), false);
        AddParticleDetail("Radius", FormatFloat(particle.Radius), false);
        AddParticleDetail("Friction", FormatFloat(particle.Friction), false);
        AddParticleDetail("Mask", particle.CollisionMask.ToString(CultureInfo.InvariantCulture), false);
    }

    private void AddParticleDetail(string field, string value, bool readOnly)
    {
        var rowIndex = _editorDetailGrid.Rows.Add(field, value);
        _editorDetailGrid.Rows[rowIndex].Cells["Value"].ReadOnly = readOnly;
    }

    private void AddParticleBoolDetail(string field, bool value)
    {
        var rowIndex = _editorDetailGrid.Rows.Add(field, value);
        _editorDetailGrid.Rows[rowIndex].Cells["Value"] = new DataGridViewCheckBoxCell { Value = value };
    }

    private void ApplySelectedParticleDetailsToCache()
    {
        if (_editorIndexList.SelectedIndex < 0 || _editorIndexList.SelectedIndex >= _particleRows.Count)
            return;

        var particle = _particleRows[_editorIndexList.SelectedIndex];
        particle.Fixed = ReadDetailBool("Fixed");
        particle.X = ReadDetailFloat("X");
        particle.Y = ReadDetailFloat("Y");
        particle.Z = ReadDetailFloat("Z");
        particle.Mass = ReadDetailFloat("Mass");
        particle.InverseMass = ReadDetailFloat("Inv Mass");
        particle.Radius = ReadDetailFloat("Radius");
        particle.Friction = ReadDetailFloat("Friction");
        particle.CollisionMask = ReadDetailInt("Mask");
    }

    private void FillBoneDetailGrid(BoneEditRow bone)
    {
        _editorDetailGrid.Rows.Clear();
        AddParticleDetail("Index", bone.Index.ToString(CultureInfo.InvariantCulture), true);
        AddParticleDetail("Name", bone.Name, false);
        AddParticleDetail("Parent", bone.ParentIndex.ToString(CultureInfo.InvariantCulture), false);
        AddParticleDetail("X", FormatFloat(bone.X), false);
        AddParticleDetail("Y", FormatFloat(bone.Y), false);
        AddParticleDetail("Z", FormatFloat(bone.Z), false);
        AddParticleDetail("Rot X", FormatFloat(bone.RotationX), false);
        AddParticleDetail("Rot Y", FormatFloat(bone.RotationY), false);
        AddParticleDetail("Rot Z", FormatFloat(bone.RotationZ), false);
    }

    private void ApplySelectedBoneDetailsToCache()
    {
        if (_editorIndexList.SelectedIndex < 0 || _editorIndexList.SelectedIndex >= _boneRows.Count)
            return;

        var bone = _boneRows[_editorIndexList.SelectedIndex];
        bone.Name = ReadDetailText("Name");
        bone.ParentIndex = ReadDetailInt("Parent");
        bone.X = ReadDetailFloat("X");
        bone.Y = ReadDetailFloat("Y");
        bone.Z = ReadDetailFloat("Z");
        bone.RotationX = ReadDetailFloat("Rot X");
        bone.RotationY = ReadDetailFloat("Rot Y");
        bone.RotationZ = ReadDetailFloat("Rot Z");
    }

    private void FillColliderDetailGrid(ColliderEditRow collider)
    {
        _editorDetailGrid.Rows.Clear();
        AddParticleDetail("Index", collider.Index.ToString(CultureInfo.InvariantCulture), true);
        AddParticleDetail("Name", collider.Name, false);
        AddParticleDetail("Bone Index", collider.BoneIndex.ToString(CultureInfo.InvariantCulture), false);
        AddParticleDetail("Bone", collider.BoneName, true);
        AddParticleDetail("Start X", FormatFloat(collider.StartX), false);
        AddParticleDetail("Start Y", FormatFloat(collider.StartY), false);
        AddParticleDetail("Start Z", FormatFloat(collider.StartZ), false);
        AddParticleDetail("End X", FormatFloat(collider.EndX), false);
        AddParticleDetail("End Y", FormatFloat(collider.EndY), false);
        AddParticleDetail("End Z", FormatFloat(collider.EndZ), false);
        AddParticleDetail("Radius", FormatFloat(collider.Radius), false);
    }

    private void ApplySelectedColliderDetailsToCache()
    {
        if (_editorIndexList.SelectedIndex < 0 || _editorIndexList.SelectedIndex >= _colliderRows.Count)
            return;

        var collider = _colliderRows[_editorIndexList.SelectedIndex];
        collider.Name = ReadDetailText("Name");
        collider.BoneIndex = ReadDetailInt("Bone Index");
        collider.StartX = ReadDetailFloat("Start X");
        collider.StartY = ReadDetailFloat("Start Y");
        collider.StartZ = ReadDetailFloat("Start Z");
        collider.EndX = ReadDetailFloat("End X");
        collider.EndY = ReadDetailFloat("End Y");
        collider.EndZ = ReadDetailFloat("End Z");
        collider.Radius = ReadDetailFloat("Radius");
    }

    private string ReadDetailText(string field)
    {
        foreach (DataGridViewRow row in _editorDetailGrid.Rows)
        {
            if (string.Equals(Convert.ToString(row.Cells["Field"].Value), field, StringComparison.OrdinalIgnoreCase))
                return Convert.ToString(row.Cells["Value"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Empty;
    }

    private float ReadDetailFloat(string field)
    {
        var text = ReadDetailText(field);
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Invalid number in {field}: {text}");

        return value;
    }

    private int ReadDetailInt(string field)
    {
        var text = ReadDetailText(field);
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Invalid integer in {field}: {text}");

        return value;
    }

    private bool ReadDetailBool(string field)
    {
        foreach (DataGridViewRow row in _editorDetailGrid.Rows)
        {
            if (string.Equals(Convert.ToString(row.Cells["Field"].Value), field, StringComparison.OrdinalIgnoreCase)
                && row.Cells["Value"].Value is bool value)
                return value;
        }

        var text = ReadDetailText(field);
        return text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("1", StringComparison.OrdinalIgnoreCase)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static ParticleEditRow CloneParticle(ParticleEditRow row)
    {
        return new ParticleEditRow
        {
            Index = row.Index,
            Fixed = row.Fixed,
            X = row.X,
            Y = row.Y,
            Z = row.Z,
            W = row.W,
            Mass = row.Mass,
            InverseMass = row.InverseMass,
            Radius = row.Radius,
            Friction = row.Friction,
            CollisionMask = row.CollisionMask
        };
    }

    private static BoneEditRow CloneBone(BoneEditRow row)
    {
        return new BoneEditRow
        {
            Index = row.Index,
            Name = row.Name,
            ParentIndex = row.ParentIndex,
            X = row.X,
            Y = row.Y,
            Z = row.Z,
            RotationX = row.RotationX,
            RotationY = row.RotationY,
            RotationZ = row.RotationZ,
            RotationW = row.RotationW,
            ScaleX = row.ScaleX,
            ScaleY = row.ScaleY,
            ScaleZ = row.ScaleZ
        };
    }

    private static ColliderEditRow CloneCollider(ColliderEditRow row)
    {
        return new ColliderEditRow
        {
            Index = row.Index,
            Name = row.Name,
            BoneIndex = row.BoneIndex,
            BoneName = row.BoneName,
            StartX = row.StartX,
            StartY = row.StartY,
            StartZ = row.StartZ,
            EndX = row.EndX,
            EndY = row.EndY,
            EndZ = row.EndZ,
            Radius = row.Radius
        };
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static float ReadFloatCell(DataGridViewRow row, string columnName)
    {
        var text = Convert.ToString(row.Cells[columnName].Value, CultureInfo.InvariantCulture) ?? "0";
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Invalid number in {columnName}: {text}");

        return value;
    }

    private static int ReadIntCell(DataGridViewRow row, string columnName)
    {
        var text = Convert.ToString(row.Cells[columnName].Value, CultureInfo.InvariantCulture) ?? "0";
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Invalid integer in {columnName}: {text}");

        return value;
    }

    private static bool ReadBoolCell(DataGridViewRow row, string columnName)
    {
        return row.Cells[columnName].Value is bool value && value;
    }

    private void ExportJson()
    {
        if (!_current.HasDocument)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = _current.IsBphhb ? "Export readable BPHHB JSON" : _current.IsBphcl ? "Export readable BPHCL JSON" : "Export readable physics JSON",
            Filter = "JSON|*.json|All files|*.*",
            FileName = _current.SuggestFileName(".json")
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        RunGuarded(() =>
        {
            _current.ExportReadableJson(dialog.FileName);
            _statusLabel.Text = $"Exported {Path.GetFileName(dialog.FileName)}";
        });
    }

    private void SaveHkcl(HkclPlatform platform)
    {
        if (!_current.HasDocument)
            return;

        if (_current.IsReadOnlyExternal)
        {
            SaveCurrentAs();
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = platform == HkclPlatform.WiiU ? "Export Wii U physics" : "Export Switch physics",
            Filter = "HKCL|*.hkcl|All files|*.*",
            FileName = _current.SuggestFileName(_current.CurrentExtension)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        RunGuarded(() =>
        {
            _current.SaveHkcl(dialog.FileName, platform);
            _currentSavePath = dialog.FileName;
            _currentSavePlatform = platform;
            _statusLabel.Text = $"Exported {Path.GetFileName(dialog.FileName)}";
        });
    }

    private void SaveCurrent()
    {
        if (!_current.HasDocument)
            return;

        if (string.IsNullOrWhiteSpace(_currentSavePath))
        {
            SaveCurrentAs();
            return;
        }

        RunGuarded(() =>
        {
            _current.SaveHkcl(_currentSavePath, _currentSavePlatform);
            _statusLabel.Text = $"Saved {Path.GetFileName(_currentSavePath)}";
        });
    }

    private void SaveCurrentAs()
    {
        if (!_current.HasDocument)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = _current.IsBphhb ? "Save BPHHB As" : _current.IsBphcl ? "Save BPHCL As" : "Save Physics As",
            Filter = _current.IsBphhb ? "BPHHB helper bones|*.bphhb|All files|*.*" : _current.IsBphcl ? "BPHCL|*.bphcl|All files|*.*" : "Wii U HKCL|*.hkcl|Switch HKCL|*.hkcl|All files|*.*",
            FileName = _current.SuggestFileName(_current.CurrentExtension)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var platform = !_current.IsReadOnlyExternal && dialog.FilterIndex == 2 ? HkclPlatform.Switch : HkclPlatform.WiiU;
        RunGuarded(() =>
        {
            _current.SaveHkcl(dialog.FileName, platform);
            _currentSavePath = dialog.FileName;
            _currentSavePlatform = platform;
            _statusLabel.Text = $"Saved {Path.GetFileName(dialog.FileName)}";
        });
    }

    private void RemoveSelectedCloth()
    {
        if (!_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        var index = _clothList.SelectedIndex;
        var label = _clothList.SelectedItem?.ToString() ?? $"cloth {index}";

        if (MessageBox.Show(
                this,
                $"Remove {label}?",
                "Remove cloth",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        RunGuarded(() =>
        {
            _current.RemoveCloth(index);
            ClearUndoHistory();
            // Keep the same slot selected when possible. If the last cloth
            // was removed, this naturally selects the new last cloth.
            RefreshCurrentLists(index);
            _statusLabel.Text = $"Removed {label}";
        });
    }

    private void SwapCurrentAndReference()
    {
        if (!_current.HasDocument || !_reference.HasDocument || _directEditMode)
            return;

        (_current, _reference) = (_reference, _current);
        _currentSavePath = _current.SourcePath;
        ClearUndoHistory();
        RefreshCurrentLists();
        RefreshReferenceLists();
        _statusLabel.Text = "Swapped current and reference files.";
    }

    private void RenameSelectedCloth()
    {
        if (!_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        var index = _clothList.SelectedIndex;
        var currentName = GetSelectedClothName();
        var name = PromptForText("Rename cloth", "Name", currentName);
        if (name == null || string.Equals(name, currentName, StringComparison.Ordinal))
            return;

        RunGuarded(() =>
        {
            _current.RenameCloth(index, name);
            RefreshCurrentLists();
            _clothList.SelectedIndex = Math.Min(index, _clothList.Items.Count - 1);
            _statusLabel.Text = $"Renamed cloth to {name}";
        });
    }

    private string GetSelectedClothName()
    {
        var summary = _clothList.SelectedItem?.ToString() ?? string.Empty;
        var colon = summary.IndexOf(':');
        var separator = summary.IndexOf("  |", StringComparison.Ordinal);
        return colon >= 0 && separator > colon
            ? summary[(colon + 1)..separator].Trim()
            : summary;
    }

    private string? PromptForText(string title, string label, string value)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(410, 125),
            BackColor = Color.FromArgb(54, 54, 54),
            ForeColor = Color.Gainsboro
        };
        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Location = new Point(14, 16),
            ForeColor = Color.Gainsboro
        };
        var text = new TextBox
        {
            Text = value,
            Location = new Point(14, 40),
            Width = 382,
            BackColor = Color.FromArgb(42, 42, 42),
            ForeColor = Color.Gainsboro
        };
        var confirm = new Button { Text = "Rename", DialogResult = DialogResult.OK, Location = new Point(231, 82), Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(316, 82), Width = 80 };
        StyleButton(confirm);
        StyleButton(cancel);
        dialog.Controls.AddRange(new Control[] { caption, text, confirm, cancel });
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { text.Focus(); text.SelectAll(); };
        return dialog.ShowDialog(this) == DialogResult.OK ? text.Text.Trim() : null;
    }

    private void MergeSelectedReferenceCloth()
    {
        if (!_current.HasDocument || !_reference.HasDocument || _referenceClothList.SelectedIndex < 0)
            return;

        var isBphclToHkcl = !_current.IsBphcl && _reference.IsBphcl;
        if (isBphclToHkcl && _clothList.SelectedIndex < 0)
        {
            MessageBox.Show(
                this,
                "Select the HKCL cloth that should act as the conversion template first.",
                "Select template",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (isBphclToHkcl && MessageBox.Show(
                this,
                _current.DescribeMergePreflight(
                    _reference,
                    _referenceClothList.SelectedIndex,
                    Math.Max(0, _clothList.SelectedIndex)) +
                "\n\nContinue?",
                "BPHCL -> HKCL conversion",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information) != DialogResult.Yes)
        {
            return;
        }

        if (_current.IsBphcl && _reference.IsBphcl)
        {
            var preflight = _current.GetBphclMergePreflight(
                _reference,
                _referenceClothList.SelectedIndex);
            if (!preflight.CanAttemptExperimentalTypeUnion)
            {
                MessageBox.Show(
                    this,
                    preflight.ToDisplayText(),
                    "BPHCL merge blocked",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var mergeTitle = preflight.IsSafe
                ? "BPHCL merge"
                : "BPHCL merge - TYPE extension";
            var mergeIcon = preflight.IsSafe
                ? MessageBoxIcon.Information
                : MessageBoxIcon.Warning;
            if (MessageBox.Show(
                    this,
                    preflight.ToDisplayText() + "\n\nContinue?",
                    mergeTitle,
                    MessageBoxButtons.YesNo,
                    mergeIcon) != DialogResult.Yes)
            {
                return;
            }
        }
        else if (!isBphclToHkcl && MessageBox.Show(
                     this,
                     "Import the selected complete cloth package into the current file?\n\n" +
                     "This adds its cloth data, paired skeleton, and colliders referenced by that cloth. " +
                     "The reference file is not changed.",
                     "Confirm HKCL merge",
                     MessageBoxButtons.YesNo,
                     MessageBoxIcon.Information) != DialogResult.Yes)
        {
            return;
        }

        RunGuarded(() =>
        {
            var result = _current.MergeClothFrom(
                _reference,
                _referenceClothList.SelectedIndex,
                Math.Max(0, _clothList.SelectedIndex));
            ClearUndoHistory();
            RefreshCurrentLists();
            _statusLabel.Text = result;
        });
    }

    private void UpdateModeLayout()
    {
        var direct = _directEditMode;
        if (_referenceGroup != null)
            _referenceGroup.Visible = !direct;
        if (_directEditGroup != null)
            _directEditGroup.Visible = direct;

        if (_outerSplit != null)
            _outerSplit.Panel2Collapsed = direct;
        if (_fileSplit != null)
            _fileSplit.Panel1Collapsed = direct;

        UpdateAddButtonText();
        _directEditButton.Text = "Editor";
        _directEditButton.UseVisualStyleBackColor = false;
        _directEditButton.BackColor = direct ? Color.FromArgb(85, 105, 130) : Color.FromArgb(64, 64, 64);
        _directEditButton.ForeColor = Color.Gainsboro;
        _directEditButton.Font = direct
            ? new Font(Font, FontStyle.Bold)
            : new Font(Font, FontStyle.Regular);
        _statusLabel.Text = direct
            ? _current.IsBphhb
                ? "BPHHB inspector: helper-bone AAMP data is read-only until its native writer is ready."
                : _current.IsBphcl
                    ? "BPHCL viewer: particles, links, and skeleton data are currently read-only."
                    : "Direct edit mode: edit physics values, then save."
            : _current.IsBphhb
                ? "BPHHB mode: validated native helper-bone inspection with byte-preserving save."
                : _current.IsBphcl
                    ? "BPHCL mode: open/save and merge use the native BPHCL serializer."
                    : "Merge mode: open a reference physics file to copy/remove cloth entries.";
    }

    private void UpdateButtons()
    {
        var hasCurrent = _current.HasDocument;
        var direct = _directEditMode;
        var readOnlyExternal = _current.IsReadOnlyExternal;
        SetButtonEnabled(_directEditButton, hasCurrent && !_current.IsBphhb);
        SetButtonEnabled(_openReferenceButton, !direct);
        SetButtonEnabled(_swapFilesButton, !direct && hasCurrent && _reference.HasDocument);
        SetButtonEnabled(_exportJsonButton, hasCurrent);
        SetButtonEnabled(_saveWiiUButton, hasCurrent);
        SetButtonEnabled(_removeButton, hasCurrent && !_current.IsBphhb && _clothList.SelectedIndex >= 0);
        var supportsMerge = !_current.IsBphhb && !_reference.IsBphhb && (_current.IsBphcl == _reference.IsBphcl ||
            (!_current.IsBphcl && _reference.IsBphcl && _clothList.SelectedIndex >= 0));
        SetButtonEnabled(_mergeButton, !direct && hasCurrent && _reference.HasDocument && _referenceClothList.SelectedIndex >= 0 && supportsMerge);
        SetButtonEnabled(_particleApplyButton, direct && hasCurrent && !readOnlyExternal && _undoStack.Count > 0);
        SetButtonEnabled(_particleRefreshButton, direct && hasCurrent && !readOnlyExternal && _redoStack.Count > 0);
        SetButtonEnabled(_addEditorItemButton, direct && hasCurrent && !readOnlyExternal && _clothList.SelectedIndex >= 0);
    }

    private static void SetButtonEnabled(Button button, bool enabled)
    {
        button.Enabled = enabled;
        button.ForeColor = enabled ? Color.Gainsboro : Color.FromArgb(130, 130, 130);
        button.BackColor = enabled ? Color.FromArgb(64, 64, 64) : Color.FromArgb(58, 58, 58);
    }

    private void RunGuarded(Action action)
    {
        try
        {
            action();
            UpdateButtons();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.ToString(),
                "PhysicsTool error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            UndoEditorChange();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.Z))
        {
            RedoEditorChange();
            return true;
        }

        if (keyData == Keys.Delete)
        {
            if (!_directEditMode)
                RemoveSelectedCloth();
            return true;
        }

        if (keyData == Keys.M && !_directEditMode && _mergeButton.Enabled)
        {
            MergeSelectedReferenceCloth();
            return true;
        }

        if (keyData == (Keys.Control | Keys.S))
        {
            SaveCurrent();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.S))
        {
            SaveCurrentAs();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}



