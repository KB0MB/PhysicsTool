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
    private readonly Button _duplicateButton = new();
    private readonly Button _mergeButton = new();
    private readonly Button _particleApplyButton = new();
    private readonly Button _particleRefreshButton = new();
    private readonly Button _particleMassScaleButton = new();
    private readonly Button _clothSettingsButton = new();
    private readonly Button _mirrorClothButton = new();
    private readonly Button _directEditButton = new();
    private readonly Button _convertButton = new();
    private readonly Button _addEditorItemButton = new();
    private readonly Button _mirrorModeButton = new();
    private readonly Button _simulationButton = new();
    private readonly Button _windSimulationButton = new();
    private readonly Button _simulationOptionsButton = new();
    private readonly System.Windows.Forms.Timer _simulationTimer = new() { Interval = 16 };
    private readonly ContextMenuStrip _exportMenu = new();
    private readonly ContextMenuStrip _clothMenu = new();
    private readonly ContextMenuStrip _addColliderMenu = new();
    private string? _currentSavePath;
    private HkclPlatform _currentSavePlatform = HkclPlatform.WiiU;

    private readonly DataGridView _particleGrid = new();
    private readonly ListBox _particleIndexList = new();
    private readonly DataGridView _particleDetailGrid = new();
    private readonly DataGridView _particleRelationshipGrid = new();
    private readonly DataGridView _relationshipDetailGrid = new();
    private readonly Button _removeRelationshipButton = new();
    private readonly ListBox _editorIndexList = new();
    private readonly DataGridView _editorDetailGrid = new();
    private readonly TabControl _editorTabs = new();
    private readonly Panel _editorContentPanel = new();
    private readonly ListBox _helperBoneList = new();
    private readonly DataGridView _helperBoneDetailGrid = new();
    private readonly Button _helperUndoButton = new();
    private readonly Button _helperRedoButton = new();
    private readonly Button _helperAddBoneButton = new();
    private readonly Button _helperDuplicateBoneButton = new();
    private readonly Button _helperMirrorXButton = new();
    private readonly Button _helperMoveUpButton = new();
    private readonly Button _helperMoveDownButton = new();
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
    private SplitContainer? _editorMainSplit;
    private SplitContainer? _editorSideSplit;
    private Control? _physicsEditorPanel;
    private Control? _helperBoneEditorPanel;
    private FlowLayoutPanel? _editorActionPanel;
    private TableLayoutPanel? _directEditorLayout;
    private bool _updatingParticleGrid;
    private bool _loadingCurrentDocument;
    private bool _previewRefreshQueued;
    private bool _committingEditorDetail;
    private bool _committingHelperBoneDetail;
    private bool _committingRelationshipEdit;
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
    private EditorSnapshot? _pendingHelperBoneSnapshot;
    private EditorSnapshot? _pendingRelationshipSnapshot;
    private EditorSnapshot? _viewportMoveSnapshot;
    private List<ParticleEditRow>? _viewportParticleRowsBeforeTransform;
    private bool _viewportTransformChanged;
    // Viewport gestures are evaluated from this baseline instead of repeatedly
    // transforming parent-local data. This keeps world-space edits stable.
    private System.Numerics.Vector3 _viewportWorldTranslation;
    private ParticleEditRow? _clipboardParticle;
    private BoneEditRow? _clipboardBone;
    private ColliderEditRow? _clipboardCollider;
    private readonly Dictionary<int, int> _mirrorPairs = new();
    private readonly Dictionary<Button, bool> _simulationButtonStates = new();
    private HkclPreviewSimulator? _simulation;
    private bool _simulationWindEnabled;
    private bool _simulationRandomWindDirections = true;
    private System.Numerics.Vector3 _simulationWindDirection = System.Numerics.Vector3.UnitX;
    private float _simulationWindSpeed = 2.2f;
    private float _simulationWindGustiness = 0.35f;
    private float _simulationGravityScale = 1.0f;
    private float _simulationPlaybackSpeed = 1.0f;
    private int _simulationSolverIterations = 7;

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
        StyleEditorGrid(_relationshipDetailGrid);
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
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));

        var toolbarButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoSize = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var openButton = MakeButton("Open Physics", 125);
        openButton.Click += (_, _) => OpenCurrentFile();

        _openReferenceButton.Text = "Open Donor";
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
        ConfigureColliderAddMenu();

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

        _convertButton.Text = "Convert";
        _convertButton.Width = 94;
        StyleButton(_convertButton);
        _convertButton.Click += (_, _) => ExportFreshHkclDocumentFromBphcl();
        toolbarButtons.Controls.Add(_convertButton);

        UpdateAddButtonText();
        _directEditButton.Text = "Editor";
        _directEditButton.Width = 82;
        StyleButton(_directEditButton);
        _directEditButton.Margin = new Padding(0, 0, 0, 0);

        var editorButtonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
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

            // The helper grid and GL viewport are built eagerly, but Windows
            // normally delays their native handles/context until first shown.
            // Warm both after the first paint so opening either editor is smooth.
            BeginInvoke(new Action(PrewarmEditorSurfaces));
        };
    }

    private void PrewarmEditorSurfaces()
    {
        _helperBoneList.CreateControl();
        _helperBoneDetailGrid.CreateControl();
        _particlePreview.CreateControl();
        _particlePreview.Invalidate();
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

        _duplicateButton.Text = "Duplicate selected";
        _duplicateButton.Width = 135;
        StyleButton(_duplicateButton);
        buttons.Controls.Add(_duplicateButton);

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
        _directEditorLayout = layout;
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var editorHost = new Panel { Dock = DockStyle.Fill };
        _physicsEditorPanel = BuildParticleEditor();
        _helperBoneEditorPanel = BuildHelperBoneEditor();
        _helperBoneEditorPanel.Visible = false;
        editorHost.Controls.Add(_physicsEditorPanel);
        editorHost.Controls.Add(_helperBoneEditorPanel);
        layout.Controls.Add(editorHost, 0, 0);

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

        _particleMassScaleButton.Text = "Mass scale";
        _particleMassScaleButton.Width = 104;
        StyleButton(_particleMassScaleButton);

        _clothSettingsButton.Text = "Cloth settings";
        _clothSettingsButton.Width = 116;
        StyleButton(_clothSettingsButton);

        _mirrorClothButton.Text = "Mirror X cloth";
        _mirrorClothButton.Width = 122;
        StyleButton(_mirrorClothButton);

        buttons.Controls.Add(_particleApplyButton);
        buttons.Controls.Add(_particleRefreshButton);
        buttons.Controls.Add(_particleMassScaleButton);
        buttons.Controls.Add(_clothSettingsButton);
        buttons.Controls.Add(_mirrorClothButton);
        _editorActionPanel = buttons;
        layout.Controls.Add(buttons, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildHelperBoneEditor()
    {
        _helperBoneList.Dock = DockStyle.Fill;
        _helperBoneList.IntegralHeight = false;

        _helperBoneDetailGrid.Dock = DockStyle.Fill;
        _helperBoneDetailGrid.AllowUserToAddRows = false;
        _helperBoneDetailGrid.AllowUserToDeleteRows = false;
        _helperBoneDetailGrid.RowHeadersVisible = false;
        _helperBoneDetailGrid.ReadOnly = false;
        _helperBoneDetailGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _helperBoneDetailGrid.MultiSelect = false;
        _helperBoneDetailGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _helperBoneDetailGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _helperBoneDetailGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Field", ReadOnly = true, FillWeight = 80 });
        _helperBoneDetailGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 145 });
        StyleEditorGrid(_helperBoneDetailGrid);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 260,
            SplitterDistance = 320
        };

        var listGroup = new GroupBox { Text = "Helper bones", Dock = DockStyle.Fill, Padding = new Padding(6) };
        listGroup.Controls.Add(_helperBoneList);
        var valuesGroup = new GroupBox { Text = "Selected helper bone", Dock = DockStyle.Fill, Padding = new Padding(6) };
        valuesGroup.Controls.Add(_helperBoneDetailGrid);
        split.Panel1.Controls.Add(listGroup);
        split.Panel2.Controls.Add(valuesGroup);

        split.SizeChanged += (_, _) =>
        {
            if (split.ClientSize.Width <= split.Panel1MinSize + 180 + split.SplitterWidth)
                return;

            split.SplitterDistance = Math.Clamp(320, split.Panel1MinSize,
                split.ClientSize.Width - 180 - split.SplitterWidth);
        };
        _helperBoneList.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingParticleGrid)
                RefreshSelectedHelperBone();
        };

        _helperBoneDetailGrid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && !IsHelperBoneFieldReadOnly(e.RowIndex))
                _pendingHelperBoneSnapshot ??= CaptureFullEditorSnapshot(EditorPage.Bones, 0, _helperBoneList.SelectedIndex);
        };
        _helperBoneDetailGrid.CellEndEdit += (_, _) => CommitHelperBoneDetailChange();
        _helperBoneDetailGrid.CellMouseClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                !string.Equals(_helperBoneDetailGrid.Columns[e.ColumnIndex].Name, "Value", StringComparison.Ordinal) ||
                IsHelperBoneFieldReadOnly(e.RowIndex))
            {
                return;
            }

            _helperBoneDetailGrid.CurrentCell = _helperBoneDetailGrid[e.ColumnIndex, e.RowIndex];
            BeginInvoke(new Action(() =>
            {
                if (!_helperBoneDetailGrid.IsDisposed && !_committingHelperBoneDetail)
                    _helperBoneDetailGrid.BeginEdit(true);
            }));
        };

        _helperUndoButton.Text = "Undo";
        _helperRedoButton.Text = "Redo";
        _helperAddBoneButton.Text = "Add bone";
        _helperDuplicateBoneButton.Text = "Duplicate bone";
        _helperMirrorXButton.Text = "Mirror X";
        _helperMoveUpButton.Text = "Move up";
        _helperMoveDownButton.Text = "Move down";
        foreach (var button in new[] { _helperUndoButton, _helperRedoButton, _helperAddBoneButton, _helperDuplicateBoneButton, _helperMirrorXButton, _helperMoveUpButton, _helperMoveDownButton })
        {
            button.Width = 112;
            StyleButton(button);
        }

        _helperUndoButton.Click += (_, _) => UndoEditorChange();
        _helperRedoButton.Click += (_, _) => RedoEditorChange();
        _helperAddBoneButton.Click += (_, _) => AddHelperBone();
        _helperDuplicateBoneButton.Click += (_, _) => DuplicateSelectedHelperBone();
        _helperMirrorXButton.Click += (_, _) => MirrorHelperBonesAcrossX();
        _helperMoveUpButton.Click += (_, _) => MoveSelectedHelperBone(-1);
        _helperMoveDownButton.Click += (_, _) => MoveSelectedHelperBone(1);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(6, 5, 6, 5),
            WrapContents = false
        };
        actions.Controls.Add(_helperUndoButton);
        actions.Controls.Add(_helperRedoButton);
        actions.Controls.Add(new Label { Width = 14 });
        actions.Controls.Add(_helperAddBoneButton);
        actions.Controls.Add(_helperDuplicateBoneButton);
        actions.Controls.Add(_helperMirrorXButton);
        actions.Controls.Add(_helperMoveUpButton);
        actions.Controls.Add(_helperMoveDownButton);

        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(split);
        panel.Controls.Add(actions);
        return panel;
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
        _editorMainSplit = split;

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
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var previewToolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 4)
        };
        previewToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
        previewToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144.0f));

        var leftToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _addEditorItemButton.Text = "Add Particle";
        _addEditorItemButton.Width = 116;
        StyleButton(_addEditorItemButton);
        leftToolbar.Controls.Add(_addEditorItemButton);

        _mirrorModeButton.Width = 136;
        StyleButton(_mirrorModeButton);
        UpdateMirrorModeButton();
        var rightToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        rightToolbar.Controls.Add(_mirrorModeButton);
        previewToolbar.Controls.Add(leftToolbar, 0, 0);
        previewToolbar.Controls.Add(rightToolbar, 1, 0);

        var simulationToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 0)
        };
        _simulationButton.Text = "Run Simulation";
        _simulationButton.Width = 118;
        StyleButton(_simulationButton);
        _windSimulationButton.Text = "Wind";
        _windSimulationButton.Width = 86;
        StyleButton(_windSimulationButton);
        _simulationOptionsButton.Text = "Options";
        _simulationOptionsButton.Width = 82;
        StyleButton(_simulationOptionsButton);
        simulationToolbar.Controls.Add(_simulationButton);
        simulationToolbar.Controls.Add(_windSimulationButton);
        simulationToolbar.Controls.Add(_simulationOptionsButton);

        _particlePreview.Dock = DockStyle.Fill;
        previewLayout.Controls.Add(previewToolbar, 0, 0);
        previewLayout.Controls.Add(_particlePreview, 0, 1);
        previewLayout.Controls.Add(simulationToolbar, 0, 2);
        previewGroup.Controls.Add(previewLayout);
        split.Panel1.Controls.Add(previewGroup);

        var sideSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };
        _editorSideSplit = sideSplit;

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
        var relationshipSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            Panel2MinSize = 126
        };
        relationshipSplit.Panel1.Controls.Add(_particleRelationshipGrid);

        var relationshipDetailLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        relationshipDetailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        relationshipDetailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        relationshipDetailLayout.Controls.Add(_relationshipDetailGrid, 0, 0);
        _removeRelationshipButton.Text = "Remove constraint";
        _removeRelationshipButton.Dock = DockStyle.Right;
        _removeRelationshipButton.Width = 132;
        StyleButton(_removeRelationshipButton);
        relationshipDetailLayout.Controls.Add(_removeRelationshipButton, 0, 1);
        relationshipSplit.Panel2.Controls.Add(relationshipDetailLayout);
        relationshipSplit.SizeChanged += (_, _) =>
        {
            if (relationshipSplit.ClientSize.Height <= relationshipSplit.Panel2MinSize + relationshipSplit.SplitterWidth + 70)
                return;

            relationshipSplit.Panel1MinSize = 70;
            var maximum = relationshipSplit.ClientSize.Height - relationshipSplit.Panel2MinSize - relationshipSplit.SplitterWidth;
            relationshipSplit.SplitterDistance = Math.Clamp(
                (int)(relationshipSplit.ClientSize.Height * 0.56),
                relationshipSplit.Panel1MinSize,
                maximum);
        };
        _relationshipGroup.Controls.Add(relationshipSplit);
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
            Text = "Attach to Bone",
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
        _particleBindStatusLabel.Text = "Select an item, then choose a bone.";

        _particleBindBoneCombo.Dock = DockStyle.Fill;
        _particleBindBoneCombo.DropDownStyle = ComboBoxStyle.DropDownList;

        _particleBindButton.Text = "Attach";
        _particleBindButton.Dock = DockStyle.Fill;
        StyleButton(_particleBindButton);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Attach moves the selected item to the chosen bone. Collider reassignment alone preserves its current position.",
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
        // Value cells enter edit mode explicitly from the mouse handler below.
        // Keeping the grid programmatic prevents Enter from immediately
        // reopening the same TextBox after a successful commit.
        _editorDetailGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _editorDetailGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _editorDetailGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Field", ReadOnly = true, FillWeight = 85 });
        _editorDetailGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 130 });
    }

    private void ConfigureParticleRelationshipGrid()
    {
        _particleRelationshipGrid.Dock = DockStyle.Fill;
        _particleRelationshipGrid.AllowUserToAddRows = false;
        _particleRelationshipGrid.AllowUserToDeleteRows = false;
        _particleRelationshipGrid.RowHeadersVisible = false;
        _particleRelationshipGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _particleRelationshipGrid.MultiSelect = false;
        _particleRelationshipGrid.ReadOnly = true;
        _particleRelationshipGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _particleRelationshipGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _particleRelationshipGrid.ScrollBars = ScrollBars.Vertical;
        _particleRelationshipGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Constraint", HeaderText = "Constraint", ReadOnly = true, FillWeight = 115 });
        _particleRelationshipGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Other", HeaderText = "Particle(s)", ReadOnly = true, FillWeight = 85 });
        _particleRelationshipGrid.SelectionChanged += (_, _) => PopulateSelectedRelationshipDetails();

        _relationshipDetailGrid.Dock = DockStyle.Fill;
        _relationshipDetailGrid.AllowUserToAddRows = false;
        _relationshipDetailGrid.AllowUserToDeleteRows = false;
        _relationshipDetailGrid.RowHeadersVisible = false;
        _relationshipDetailGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _relationshipDetailGrid.MultiSelect = false;
        _relationshipDetailGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _relationshipDetailGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _relationshipDetailGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Property", ReadOnly = true, FillWeight = 90 });
        _relationshipDetailGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 110 });
        _relationshipDetailGrid.CellBeginEdit += RelationshipDetailGridCellBeginEdit;
        _relationshipDetailGrid.CellEndEdit += RelationshipDetailGridCellEndEdit;
        _removeRelationshipButton.Click += (_, _) => RemoveSelectedParticleRelationship();
    }

    private void RelationshipDetailGridCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        var valueColumn = _relationshipDetailGrid.Columns["Value"];
        if (!_current.CanEditConstraintValues || _updatingParticleGrid || _committingRelationshipEdit || _applyingSnapshot
            || e.RowIndex < 0 || valueColumn == null || e.ColumnIndex != valueColumn.Index
            || _clothList.SelectedIndex < 0 || GetSelectedRelationship() is not { IsEditable: true })
        {
            e.Cancel = true;
            return;
        }

        if (_relationshipDetailGrid.Rows[e.RowIndex].Tag is not string)
        {
            e.Cancel = true;
            return;
        }

        _pendingRelationshipSnapshot ??= CaptureFullEditorSnapshot(
            EditorPage.Particles,
            _clothList.SelectedIndex,
            _editorIndexList.SelectedIndex);
    }

    private void RelationshipDetailGridCellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (_pendingRelationshipSnapshot == null || _committingRelationshipEdit || _updatingParticleGrid
            || e.RowIndex < 0 || !_current.CanEditConstraintValues
            || _relationshipDetailGrid.Rows[e.RowIndex].Tag is not string property
            || GetSelectedRelationship() is not { IsEditable: true } relation)
        {
            return;
        }

        var text = Convert.ToString(_relationshipDetailGrid.Rows[e.RowIndex].Cells["Value"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        CommitParticleRelationshipEdit(relation, property, text);
    }

    private void RemoveSelectedParticleRelationship()
    {
        var relation = GetSelectedRelationship();
        if (_current.IsReadOnlyExternal || relation is not { IsEditable: true } || _clothList.SelectedIndex < 0)
            return;

        var result = MessageBox.Show(
            this,
            "Remove this constraint? Only this relationship will be removed.",
            "Remove constraint",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
            return;

        RunGuarded(() =>
        {
            var snapshot = CaptureFullEditorSnapshot(EditorPage.Particles, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            _current.DeleteParticleRelationship(_clothList.SelectedIndex, relation);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: _editorIndexList.SelectedIndex);
            _statusLabel.Text = "Removed constraint relationship.";
            UpdateButtons();
        });
    }

    private void CommitParticleRelationshipEdit(ParticleRelationshipRow relation, string property, string text)
    {
        var snapshot = _pendingRelationshipSnapshot;
        if (snapshot == null)
            return;

        _committingRelationshipEdit = true;
        try
        {
            var value = ReadRelationshipFloat(text, property);
            switch (property)
            {
                case "RestLength": relation.RestLength = value; break;
                case "BendMinLength": relation.BendMinLength = value; break;
                case "StretchMaxLength": relation.StretchMaxLength = value; break;
                case "MaximumDistance": relation.MaximumDistance = value; break;
                case "Stiffness": relation.Stiffness = value; break;
                case "BendStiffness": relation.BendStiffness = value; break;
                case "StretchStiffness": relation.StretchStiffness = value; break;
                default: return;
            }

            _current.UpdateParticleRelationshipRows(_clothList.SelectedIndex, new[] { relation });
            _pendingRelationshipSnapshot = null;
            PushUndo(snapshot);
            _redoStack.Clear();
            // CellEndEdit is still changing this grid's active cell. Rebuilding the
            // particle/relationship grids here re-enters DataGridView selection code.
            // The row already contains the committed value; a later selection or undo
            // refreshes it from the document normally.
            QueuePreviewRefresh();
            _statusLabel.Text = "Updated constraint value.";
            UpdateButtons();
        }
        catch (FormatException ex)
        {
            _pendingRelationshipSnapshot = snapshot;
            MessageBox.Show(this, ex.Message, "PhysicsTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _committingRelationshipEdit = false;
        }
    }

    private static float ReadRelationshipFloat(string text, string property)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException($"Please enter a value for {property}.");
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
            throw new FormatException($"Please enter a valid number for {property}.");
        return value;
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
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.Padding = new Padding(4, 0, 4, 0);
        button.UseCompatibleTextRendering = false;
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Color.FromArgb(64, 64, 64);
        button.ForeColor = Color.Gainsboro;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(165, 165, 165);
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
        var bphysics = MakeExportMenuItem("BPHYSICS sidecar", ExportBphysics);
        _exportMenu.Items.Add(json);
        _exportMenu.Items.Add(wiiU);
        _exportMenu.Items.Add(switchItem);
        _exportMenu.Items.Add(new ToolStripSeparator());
        _exportMenu.Items.Add(bphysics);
    }

    private void ConfigureClothMenu()
    {
        _clothMenu.BackColor = Color.FromArgb(48, 48, 48);
        _clothMenu.ForeColor = Color.Gainsboro;
        _clothMenu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable());
        _clothMenu.Items.Add(MakeExportMenuItem("Rename", RenameSelectedCloth));
        _clothMenu.Items.Add(MakeExportMenuItem("Duplicate", DuplicateSelectedCloth));
    }

    private void ConfigureColliderAddMenu()
    {
        _addColliderMenu.BackColor = Color.FromArgb(48, 48, 48);
        _addColliderMenu.ForeColor = Color.Gainsboro;
        _addColliderMenu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColorTable());
        _addColliderMenu.Items.Add(MakeExportMenuItem("Capsule", () => AddEditorItemForCurrentTab("hclCapsuleShape")));
        _addColliderMenu.Items.Add(MakeExportMenuItem("Sphere", () => AddEditorItemForCurrentTab("hclSphereShape")));
        _addColliderMenu.Items.Add(MakeExportMenuItem("Plane", () => AddEditorItemForCurrentTab("hclPlaneShape")));
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
        _duplicateButton.Click += (_, _) => DuplicateSelectedCloth();
        _mergeButton.Click += (_, _) => MergeSelectedReferenceCloth();
        _particleApplyButton.Click += (_, _) => UndoEditorChange();
        _particleRefreshButton.Click += (_, _) => RedoEditorChange();
        _particleMassScaleButton.Click += (_, _) => ScaleCurrentClothParticleMass();
        _clothSettingsButton.Click += (_, _) => EditCurrentClothSimulationSettings();
        _mirrorClothButton.Click += (_, _) => MirrorCurrentClothAcrossX();
        _particleBindButton.Click += (_, _) => AttachSelectedItemsToChosenBone();
        _addEditorItemButton.Click += (_, _) => AddEditorItemForCurrentTab();
        _simulationButton.Click += (_, _) => ToggleSimulation();
        _windSimulationButton.Click += (_, _) => ToggleSimulationWind();
        _simulationOptionsButton.Click += (_, _) => ShowSimulationOptions();
        _simulationTimer.Tick += (_, _) => AdvanceSimulation();
        _editorIndexList.SelectedIndexChanged += (_, _) =>
        {
            var wasUserListSelection = _editorIndexList.Focused;
            RefreshSelectedEditorItem();
            if (wasUserListSelection)
                BeginInvoke(new Action(() => _particlePreview.Focus()));
        };
        _editorTabs.SelectedIndexChanged += (_, _) =>
        {
            if (_applyingSnapshot)
                return;

            // Helper-bone files expose only a small graph-derived bone subset.
            // Do not let the normal physics tabs imply particles or colliders exist.
            if (_current.IsBphhb && _editorTabs.SelectedIndex != 1)
            {
                _editorTabs.SelectedIndex = 1;
                return;
            }

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
            if (CanEditActiveEditorValues())
                _pendingEditSnapshot ??= CaptureCurrentEditorSnapshot();
        };
        _editorDetailGrid.CellEndEdit += (_, _) => CommitEditorDetailChange();
        _editorDetailGrid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is not TextBox textBox)
                return;

            textBox.KeyDown -= EditorDetailTextBoxKeyDown;
            textBox.KeyDown += EditorDetailTextBoxKeyDown;
        };
        _editorDetailGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                _editorDetailGrid.Columns[e.ColumnIndex].Name != "Value" ||
                _editorDetailGrid.Rows[e.RowIndex].Cells[e.ColumnIndex] is not DataGridViewCheckBoxCell)
            {
                return;
            }

            CommitEditorDetailChange();
        };
        _editorDetailGrid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 &&
                _editorDetailGrid.Columns[e.ColumnIndex].Name == "Value" &&
                string.Equals(GetDetailField(e.RowIndex), "Colliders", StringComparison.OrdinalIgnoreCase))
            {
                EditSelectedParticleColliders();
            }
        };
        _editorDetailGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_editorDetailGrid.IsCurrentCellDirty)
                _editorDetailGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _editorDetailGrid.CellMouseClick += (_, e) =>
        {
            if (!CanEditActiveEditorValues())
                return;

            var valueColumn = _editorDetailGrid.Columns["Value"];
            if (e.RowIndex >= 0 && valueColumn != null && e.ColumnIndex == valueColumn.Index)
            {
                if (string.Equals(GetDetailField(e.RowIndex), "Colliders", StringComparison.OrdinalIgnoreCase))
                    return;

                // Commit the old cell before putting the newly clicked cell into edit mode.
                // This makes a click into another field behave like an explicit Enter.
                if (_editorDetailGrid.IsCurrentCellInEditMode)
                    _editorDetailGrid.EndEdit();

                _pendingEditSnapshot ??= CaptureCurrentEditorSnapshot();
                _editorDetailGrid.CurrentCell = _editorDetailGrid[e.ColumnIndex, e.RowIndex];
                var screenClickLocation = Cursor.Position;
                BeginInvoke(new Action(() => BeginEditorDetailEditAt(e.ColumnIndex, e.RowIndex, screenClickLocation)));
            }
        };
        _particlePreview.ItemPicked += (_, e) => HandlePreviewPick(e);
        _particlePreview.ParticlesSelected += (_, e) => SelectParticlesFromViewport(e.ParticleIndices, e.SelectionOperation);
        _particlePreview.ParticleMoveStarted += (_, _) => BeginViewportParticleMove();
        _particlePreview.ParticlesMoved += (_, e) => MoveSelectedParticles(e.Delta, e.LocalAxis);
        _particlePreview.ParticlesScaled += (_, e) => ScaleSelectedParticles(e.Factor, e.Axis, e.LocalAxis, e.RadiusOnly);
        _particlePreview.ParticlesRotated += (_, e) => RotateSelectedParticles(e.Radians, e.Axis, e.LocalAxis);
        _particlePreview.MirrorRequested += (_, e) => MirrorSelectedParticles(e.Axis, e.Local);
        _particlePreview.MirrorModeChanged += (_, _) =>
        {
            UpdateMirrorModeButton();
            _statusLabel.Text = _particlePreview.MirrorModeEnabled
                ? "Mirror X enabled: matching opposite-side items follow transforms."
                : "Mirror X disabled.";
        };
        _particlePreview.CopyRequested += (_, _) => CopySelectedEditorItem();
        _particlePreview.PasteRequested += (_, _) => PasteEditorItem();
        _particlePreview.PasteMirroredRequested += (_, _) => PasteEditorItem(mirrorX: true);
        _mirrorModeButton.Click += (_, _) =>
        {
            _particlePreview.MirrorModeEnabled = !_particlePreview.MirrorModeEnabled;
            BeginInvoke(new Action(() => _particlePreview.Focus()));
        };
        _particlePreview.LinkRequested += (_, _) => LinkSelectedParticles();
        _particlePreview.DeleteRequested += (_, _) => DeleteSelectedEditorItem();
        _particlePreview.ParticleMoveEnded += (_, _) => EndViewportParticleMove();
        _particlePreview.ParticleMoveCanceled += (_, _) => CancelViewportParticleMove();
        _directEditButton.Click += (_, _) =>
        {
            if (_current.IsBphhb)
                return;

            _directEditMode = !_directEditMode;
            if (!_directEditMode)
                StopSimulation();
            else if (_current.IsBphhb)
                SelectHelperBonePage();
            UpdateModeLayout();
            RefreshParticleGrid(resetCamera: false);
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
        _clothList.SelectedIndexChanged += (_, _) =>
        {
            // Navigating between cloth entries is not an edit. Discard any
            // unfinished detail-grid snapshot instead of carrying it across
            // cloths and turning navigation into an undoable change.
            _pendingEditSnapshot = null;
            _pendingRelationshipSnapshot = null;
            RefreshSelectedDetails();
        };
        _clothList.DoubleClick += (_, _) => OpenSelectedClothInEditor();
        _clothList.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            OpenSelectedClothInEditor();
        };

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

    private void OpenSelectedClothInEditor()
    {
        if (_simulationTimer.Enabled || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        if (!_directEditMode)
        {
            _directEditMode = true;
            if (_current.IsBphhb)
                SelectHelperBonePage();
            UpdateModeLayout();
        }

        RefreshParticleGrid(resetCamera: false);
        UpdateButtons();
        BeginInvoke(new Action(() => _particlePreview.Focus()));
    }

    private void SelectHelperBonePage()
    {
        _editorPage = EditorPage.Bones;
        if (_editorTabs.SelectedIndex != 1)
            _editorTabs.SelectedIndex = 1;
    }

    private void MoveEditorContentToSelectedTab()
    {
        if (_editorTabs.SelectedTab != null && !_editorTabs.SelectedTab.Controls.Contains(_editorContentPanel))
            _editorTabs.SelectedTab.Controls.Add(_editorContentPanel);
    }

    private bool CanRunSimulation()
    {
        return _directEditMode && _current.HasDocument && !_current.IsReadOnlyExternal
            && _clothList.SelectedIndex >= 0 && _particleRows.Count > 0;
    }

    private void ToggleSimulation()
    {
        if (_simulationTimer.Enabled)
        {
            StopSimulation(resetPreview: true);
            _statusLabel.Text = "Simulation stopped and reset to the file pose.";
            return;
        }

        if (!CanRunSimulation())
            return;

        var preview = _current.GetParticlePreview(_clothList.SelectedIndex);
        if (preview.Particles.All(particle => particle.Fixed))
        {
            _statusLabel.Text = "This cloth has no dynamic particles to simulate.";
            return;
        }

        _simulation = new HkclPreviewSimulator(preview);
        ApplySimulationOptions(_simulation);
        _simulationTimer.Start();
        _simulationButton.Text = "Stop Simulation";
        _statusLabel.Text = "Running HKCL preview simulation. This does not change the file.";
        UpdateButtons();
        SetSimulationButtonLock(true);
    }

    private void ToggleSimulationWind()
    {
        _simulationWindEnabled = !_simulationWindEnabled;
        if (_simulation != null)
            ApplySimulationOptions(_simulation);
        UpdateWindSimulationButton();
        _statusLabel.Text = _simulationWindEnabled
            ? "Preview wind enabled. It affects only the viewport simulation."
            : "Preview wind disabled.";
    }

    private void AdvanceSimulation()
    {
        if (_simulation == null || !CanRunSimulation())
        {
            StopSimulation();
            return;
        }

        _simulation.Step((1.0f / 60.0f) * _simulationPlaybackSpeed);
        _particlePreview.UpdateSimulatedPose(_simulation.GetPositions(), _simulation.GetBonePoses());
    }

    private void StopSimulation(bool resetPreview = true)
    {
        _simulationTimer.Stop();
        _simulation = null;
        _simulationButton.Text = "Run Simulation";
        SetSimulationButtonLock(false);
        if (resetPreview && _current.HasDocument && _clothList.SelectedIndex >= 0)
        {
            _particlePreview.SetData(_current.GetParticlePreview(_clothList.SelectedIndex), resetCamera: false);
            _particlePreview.SetSelectedParticleIndices(_selectedParticleIndices);
        }
        UpdateButtons();
    }

    private void SetSimulationButtonLock(bool locked)
    {
        if (locked)
        {
            SetSimulationEditorLock(true);
            _simulationButtonStates.Clear();
            foreach (var button in EnumerateControls(this).OfType<Button>())
            {
                if (ReferenceEquals(button, _simulationButton) ||
                    ReferenceEquals(button, _windSimulationButton) ||
                    ReferenceEquals(button, _simulationOptionsButton))
                    continue;

                _simulationButtonStates[button] = button.Enabled;
                SetButtonEnabled(button, false);
            }
            return;
        }

        SetSimulationEditorLock(false);
        foreach (var entry in _simulationButtonStates)
        {
            if (!entry.Key.IsDisposed)
                // Restore the same enabled styling used by the rest of the UI.
                // Restoring only Enabled left buttons functionally active but
                // permanently painted as disabled after a simulation stopped.
                SetButtonEnabled(entry.Key, entry.Value);
        }
        _simulationButtonStates.Clear();
    }

    private void SetSimulationEditorLock(bool locked)
    {
        // The viewport remains live for previewing, but simulation must never
        // race a value edit, selection change, or constraint modification.
        if (_editorValueGroup != null)
            _editorValueGroup.Enabled = !locked;
        if (_relationshipGroup != null)
            _relationshipGroup.Enabled = !locked;
    }

    private static IEnumerable<Control> EnumerateControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in EnumerateControls(child))
                yield return descendant;
        }
    }

    private void ApplySimulationOptions(HkclPreviewSimulator simulation)
    {
        simulation.WindEnabled = _simulationWindEnabled;
        simulation.RandomWindDirections = _simulationRandomWindDirections;
        simulation.WindDirection = _simulationWindDirection;
        simulation.WindSpeed = _simulationWindSpeed;
        simulation.WindGustiness = _simulationWindGustiness;
        simulation.GravityScale = _simulationGravityScale;
        simulation.SolverIterations = _simulationSolverIterations;
    }

    private void UpdateWindSimulationButton()
    {
        _windSimulationButton.Text = "Wind";
        if (!_windSimulationButton.Enabled)
            return;

        _windSimulationButton.BackColor = _simulationWindEnabled
            ? Color.FromArgb(30, 95, 160)
            : Color.FromArgb(64, 64, 64);
        _windSimulationButton.ForeColor = Color.Gainsboro;
        _windSimulationButton.FlatAppearance.BorderColor = _simulationWindEnabled
            ? Color.FromArgb(110, 185, 255)
            : Color.FromArgb(165, 165, 165);
        _windSimulationButton.FlatAppearance.MouseOverBackColor = _simulationWindEnabled
            ? Color.FromArgb(43, 112, 183)
            : Color.FromArgb(78, 78, 78);
    }

    private void ShowSimulationOptions()
    {
        var originalDirection = _simulationWindDirection;
        var originalRandomWindDirections = _simulationRandomWindDirections;
        var originalWindSpeed = _simulationWindSpeed;
        var originalWindGustiness = _simulationWindGustiness;
        var originalGravityScale = _simulationGravityScale;
        var originalPlaybackSpeed = _simulationPlaybackSpeed;
        var originalSolverIterations = _simulationSolverIterations;

        using var dialog = new Form
        {
            Text = "Simulation options",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(580, 500),
            BackColor = Color.FromArgb(48, 48, 48),
            ForeColor = Color.Gainsboro
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 7
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        for (var row = 0; row < 5; row++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        void ApplyLiveOptions()
        {
            if (_simulation != null)
                ApplySimulationOptions(_simulation);
        }

        var directionGroup = new GroupBox
        {
            Text = "Wind direction (none = random)",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            Padding = new Padding(8)
        };
        var directionLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        for (var column = 0; column < 3; column++)
            directionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f / 3.0f));
        directionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        directionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var directions = new[]
        {
            ("+X", System.Numerics.Vector3.UnitX),
            ("+Y", System.Numerics.Vector3.UnitY),
            ("+Z", System.Numerics.Vector3.UnitZ),
            ("-X", -System.Numerics.Vector3.UnitX),
            ("-Y", -System.Numerics.Vector3.UnitY),
            ("-Z", -System.Numerics.Vector3.UnitZ)
        };
        var directionButtons = new List<(Button Button, System.Numerics.Vector3 Direction)>();

        void RefreshDirectionButtons()
        {
            foreach (var entry in directionButtons)
            {
                var selected = !_simulationRandomWindDirections && entry.Direction == _simulationWindDirection;
                entry.Button.BackColor = selected ? Color.FromArgb(30, 95, 160) : Color.FromArgb(64, 64, 64);
                entry.Button.FlatAppearance.BorderColor = selected
                    ? Color.FromArgb(110, 185, 255)
                    : Color.FromArgb(165, 165, 165);
            }
            directionGroup.Text = _simulationRandomWindDirections
                ? "Wind direction (random breeze)"
                : "Wind direction (click selected axis for random)";
        }

        void SelectWindDirection(System.Numerics.Vector3 direction)
        {
            if (!_simulationRandomWindDirections && _simulationWindDirection == direction)
                _simulationRandomWindDirections = true;
            else
            {
                _simulationRandomWindDirections = false;
                _simulationWindDirection = direction;
            }
            RefreshDirectionButtons();
            ApplyLiveOptions();
        }

        for (var index = 0; index < directions.Length; index++)
        {
            var direction = directions[index];
            var button = new Button
            {
                Text = direction.Item1,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gainsboro,
                BackColor = Color.FromArgb(64, 64, 64),
                Tag = direction.Item2,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Margin = new Padding(3),
                Padding = Padding.Empty
            };
            StyleButton(button);
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(3);
            button.Padding = Padding.Empty;
            button.Font = new Font("Segoe UI", 10.0f, FontStyle.Bold);
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(165, 165, 165);
            button.Click += (_, _) => SelectWindDirection((System.Numerics.Vector3)button.Tag);
            directionButtons.Add((button, direction.Item2));
            directionLayout.Controls.Add(button, index % 3, index / 3);
        }
        RefreshDirectionButtons();
        directionGroup.Controls.Add(directionLayout);
        layout.Controls.Add(directionGroup, 0, 0);

        TrackBar MakeSlider(int minimum, int maximum, int value)
        {
            return new TrackBar
            {
                Dock = DockStyle.Fill,
                Minimum = minimum,
                Maximum = maximum,
                Value = Math.Clamp(value, minimum, maximum),
                TickStyle = TickStyle.None,
                SmallChange = 1,
                LargeChange = Math.Max(1, (maximum - minimum) / 10),
                BackColor = Color.FromArgb(48, 48, 48)
            };
        }

        void AddSliderRow(int row, string name, TrackBar slider, int resetValue, Func<int, string> format, Action<int> changed)
        {
            var rowLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
            rowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 154));
            rowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            rowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            var label = new Label
            {
                Text = name,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AutoSize = false
            };
            var value = new Label { Text = format(slider.Value), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(142, 198, 255) };
            slider.ValueChanged += (_, _) =>
            {
                value.Text = format(slider.Value);
                changed(slider.Value);
                ApplyLiveOptions();
            };
            var reset = new Button { Text = "Reset", Dock = DockStyle.Fill, Margin = new Padding(3, 7, 0, 7) };
            StyleButton(reset);
            reset.Dock = DockStyle.Fill;
            reset.Margin = new Padding(3, 7, 0, 7);
            reset.Padding = Padding.Empty;
            reset.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            reset.Click += (_, _) => slider.Value = Math.Clamp(resetValue, slider.Minimum, slider.Maximum);
            rowLayout.Controls.Add(label, 0, 0);
            rowLayout.Controls.Add(slider, 1, 0);
            rowLayout.Controls.Add(value, 2, 0);
            rowLayout.Controls.Add(reset, 3, 0);
            layout.Controls.Add(rowLayout, 0, row);
        }

        AddSliderRow(1, "Wind strength", MakeSlider(0, 500, (int)MathF.Round(_simulationWindSpeed * 10.0f)), 22, value => (value / 10.0f).ToString("0.0"), value => _simulationWindSpeed = value / 10.0f);
        AddSliderRow(2, "Wind gustiness", MakeSlider(0, 100, (int)MathF.Round(_simulationWindGustiness * 100.0f)), 35, value => $"{value}%", value => _simulationWindGustiness = value / 100.0f);
        AddSliderRow(3, "Gravity scale", MakeSlider(0, 300, (int)MathF.Round(_simulationGravityScale * 100.0f)), 100, value => $"{value / 100.0f:0.00}x", value => _simulationGravityScale = value / 100.0f);
        AddSliderRow(4, "Playback speed", MakeSlider(10, 400, (int)MathF.Round(_simulationPlaybackSpeed * 100.0f)), 100, value => $"{value / 100.0f:0.00}x", value => _simulationPlaybackSpeed = value / 100.0f);
        AddSliderRow(5, "Solver iterations", MakeSlider(1, 24, _simulationSolverIterations), 7, value => value.ToString(CultureInfo.InvariantCulture), value => _simulationSolverIterations = value);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0)
        };
        var apply = new Button { Text = "Close", Width = 78, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Width = 78, DialogResult = DialogResult.Cancel };
        StyleButton(apply);
        StyleButton(cancel);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 6);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _statusLabel.Text = "Updated preview simulation options.";
            return;
        }

        _simulationWindDirection = originalDirection;
        _simulationRandomWindDirections = originalRandomWindDirections;
        _simulationWindSpeed = originalWindSpeed;
        _simulationWindGustiness = originalWindGustiness;
        _simulationGravityScale = originalGravityScale;
        _simulationPlaybackSpeed = originalPlaybackSpeed;
        _simulationSolverIterations = originalSolverIterations;
        ApplyLiveOptions();
        _statusLabel.Text = "Restored previous preview simulation options.";
    }

    private void HandlePreviewPick(PreviewPickEventArgs e)
    {
        if (!_directEditMode || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        if (!IsPickAllowedForCurrentTab(e.Kind))
            return;

        if (e.Index < 0)
        {
            if (e.SelectionOperation == ParticleSelectionOperation.Replace)
                ClearEditorSelection();
            return;
        }

        if (e.Kind == PreviewPickKind.Particle)
        {
            SelectParticlesFromViewport(new[] { e.Index }, e.SelectionOperation);
            return;
        }

        SelectEditorItem(e.Kind, e.Index, e.AddToSelection);
    }

    private void SelectParticlesFromViewport(IReadOnlyList<int> particleIndices, ParticleSelectionOperation selectionOperation)
    {
        if (!_directEditMode || !_current.HasDocument || _editorPage != EditorPage.Particles)
            return;

        if (particleIndices.Count == 0)
        {
            if (selectionOperation == ParticleSelectionOperation.Replace)
                ClearEditorSelection();
            return;
        }

        var previousListIndex = _editorIndexList.SelectedIndex;
        if (selectionOperation == ParticleSelectionOperation.Replace)
        {
            _selectedParticleIndices.Clear();
            foreach (var index in particleIndices)
                _selectedParticleIndices.Add(index);
        }
        else
        {
            foreach (var index in particleIndices)
            {
                if (selectionOperation == ParticleSelectionOperation.Remove)
                    _selectedParticleIndices.Remove(index);
                else if (selectionOperation == ParticleSelectionOperation.Add)
                    _selectedParticleIndices.Add(index);
                else if (!_selectedParticleIndices.Add(index))
                    _selectedParticleIndices.Remove(index);
            }
        }

        var firstSelectedParticle = previousListIndex >= 0 && previousListIndex < _particleRows.Count
            ? _particleRows[previousListIndex].Index
            : -1;
        var firstListIndex = _selectedParticleIndices.Contains(firstSelectedParticle)
            ? previousListIndex
            : _particleRows.FindIndex(x => _selectedParticleIndices.Contains(x.Index));
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
        if (!CanTransformViewportItems() || !HasActiveEditorSelection() || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        if (_viewportMoveSnapshot == null)
        {
            _viewportMoveSnapshot = _editorPage == EditorPage.Particles
                ? CaptureFullEditorSnapshot(_editorPage, _clothList.SelectedIndex, _editorIndexList.SelectedIndex)
                : CaptureEditorSnapshot(_editorPage, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            _viewportParticleRowsBeforeTransform = _editorPage == EditorPage.Particles
                ? _particleRows.Select(CloneParticle).ToList()
                : null;
        }
        _viewportWorldTranslation = System.Numerics.Vector3.Zero;
        BuildMirrorPairs();
        if (SnapMirrorPairsToSources())
        {
            _viewportTransformChanged = true;
            UpdateViewportTransformPreview();
        }
    }

    private void MoveSelectedParticles(System.Numerics.Vector3 delta, bool _)
    {
        if (!CanTransformViewportItems() || !HasActiveEditorSelection() || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        if (_editorPage == EditorPage.Bones)
        {
            var bone = GetSelectedBone();
            var sourceBones = _viewportMoveSnapshot?.Bones;
            var baseline = sourceBones?.FirstOrDefault(candidate => candidate.Index == bone?.Index);
            if (bone == null || baseline == null || sourceBones == null)
                return;

            // The viewport sends small mouse deltas. Accumulate them in world
            // space, then derive the parent-local value from the untouched pose.
            _viewportWorldTranslation += delta;
            var boneWorld = GetBoneWorldMatrix(baseline.Index, sourceBones);
            boneWorld.M41 += _viewportWorldTranslation.X;
            boneWorld.M42 += _viewportWorldTranslation.Y;
            boneWorld.M43 += _viewportWorldTranslation.Z;

            var parentWorld = GetBoneWorldMatrix(baseline.ParentIndex, sourceBones);
            if (!System.Numerics.Matrix4x4.Invert(parentWorld, out var parentInverse))
                parentInverse = System.Numerics.Matrix4x4.Identity;
            var local = boneWorld * parentInverse;
            bone.X = Math.Clamp(local.M41, -30.0f, 30.0f);
            bone.Y = Math.Clamp(local.M42, -30.0f, 30.0f);
            bone.Z = Math.Clamp(local.M43, -30.0f, 30.0f);
            ApplyMirrorToBone(bone);
            UpdateViewportTransformPreview();
            _viewportTransformChanged = true;
            return;
        }

        if (_editorPage == EditorPage.Colliders)
        {
            var collider = GetSelectedCollider();
            var baseline = _viewportMoveSnapshot?.Colliders?.FirstOrDefault(candidate => candidate.Index == collider?.Index);
            if (collider == null || baseline == null)
                return;

            _viewportWorldTranslation += delta;
            collider.StartX = baseline.StartX + _viewportWorldTranslation.X;
            collider.StartY = baseline.StartY + _viewportWorldTranslation.Y;
            collider.StartZ = baseline.StartZ + _viewportWorldTranslation.Z;
            collider.EndX = baseline.EndX + _viewportWorldTranslation.X;
            collider.EndY = baseline.EndY + _viewportWorldTranslation.Y;
            collider.EndZ = baseline.EndZ + _viewportWorldTranslation.Z;
            var transform = baseline.Transform;
            transform.M41 += _viewportWorldTranslation.X;
            transform.M42 += _viewportWorldTranslation.Y;
            transform.M43 += _viewportWorldTranslation.Z;
            collider.Transform = transform;
            ApplyMirrorToCollider(collider);
            UpdateViewportTransformPreview();
            _viewportTransformChanged = true;
            return;
        }

        foreach (var particle in _particleRows.Where(p => _selectedParticleIndices.Contains(p.Index)))
        {
            particle.X += delta.X;
            particle.Y += delta.Y;
            particle.Z += delta.Z;
        }

        _viewportTransformChanged = true;
        ApplyMirrorToParticles();
        _particlePreview.UpdateParticlePreviewRows(_particleRows);
    }

    private void ScaleSelectedParticles(float factor, System.Numerics.Vector3? axis, bool localAxis, bool radiusOnly)
    {
        if (!CanTransformViewportItems() || !HasActiveEditorSelection() || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        if (_editorPage == EditorPage.Bones)
        {
            var bone = GetSelectedBone();
            if (bone == null)
                return;

            ScaleBoneTranslation(bone, factor, axis);
            ApplyMirrorToBone(bone);
            UpdateViewportTransformPreview();
            _viewportTransformChanged = true;
            return;
        }

        if (_editorPage == EditorPage.Colliders)
        {
            var collider = GetSelectedCollider();
            if (collider == null)
                return;

            if (radiusOnly)
                collider.Radius = Math.Clamp(collider.Radius * factor, 0.0001f, 10.0f);
            else
                ScaleCollider(collider, factor, axis);
            ApplyMirrorToCollider(collider);
            UpdateViewportTransformPreview();
            _viewportTransformChanged = true;
            return;
        }

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
        ApplyMirrorToParticles();
        _particlePreview.UpdateParticlePreviewRows(_particleRows);
    }

    private void RotateSelectedParticles(float radians, System.Numerics.Vector3 axis, bool localAxis)
    {
        if (!CanTransformViewportItems() || !HasActiveEditorSelection() || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        if (_editorPage == EditorPage.Bones)
        {
            var bone = GetSelectedBone();
            if (bone == null)
                return;

            RotateBone(bone, radians, axis, localAxis);
            ApplyMirrorToBone(bone);
            UpdateViewportTransformPreview();
            _viewportTransformChanged = true;
            return;
        }

        if (_editorPage == EditorPage.Colliders)
        {
            var collider = GetSelectedCollider();
            if (collider == null)
                return;

            RotateCollider(collider, radians, axis);
            ApplyMirrorToCollider(collider);
            UpdateViewportTransformPreview();
            _viewportTransformChanged = true;
            return;
        }

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
        ApplyMirrorToParticles();
        _particlePreview.UpdateParticlePreviewRows(_particleRows);
    }

    private void MirrorSelectedParticles(char axisName, bool local)
    {
        if (!CanTransformViewportItems() || !HasActiveEditorSelection() || !_current.HasDocument || _clothList.SelectedIndex < 0)
        {
            _statusLabel.Text = "Select an editor item before mirroring.";
            return;
        }

        if (_editorPage == EditorPage.Bones && GetSelectedBone() is { } selectedBone)
        {
            var boneSnapshot = CaptureEditorSnapshot(EditorPage.Bones, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            var boneAxis = GetMirrorAxis(axisName, local);
            var reflected = ReflectPoint(new System.Numerics.Vector3(selectedBone.X, selectedBone.Y, selectedBone.Z), local ? new System.Numerics.Vector3(selectedBone.X, selectedBone.Y, selectedBone.Z) : System.Numerics.Vector3.Zero, boneAxis);
            selectedBone.X = reflected.X; selectedBone.Y = reflected.Y; selectedBone.Z = reflected.Z;
            _current.UpdateBoneRows(_clothList.SelectedIndex, _boneRows);
            PushUndo(boneSnapshot);
            _redoStack.Clear();
            RefreshPreview(resetCamera: false);
            _statusLabel.Text = $"Mirrored selected bone on {(local ? "local" : "global")} {axisName}.";
            return;
        }

        if (_editorPage == EditorPage.Colliders && GetSelectedCollider() is { } selectedCollider)
        {
            var colliderSnapshot = CaptureEditorSnapshot(EditorPage.Colliders, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            var colliderAxis = GetMirrorAxis(axisName, local);
            var colliderPivot = local ? ColliderCenter(selectedCollider) : System.Numerics.Vector3.Zero;
            var start = ReflectPoint(new System.Numerics.Vector3(selectedCollider.StartX, selectedCollider.StartY, selectedCollider.StartZ), colliderPivot, colliderAxis);
            var end = ReflectPoint(new System.Numerics.Vector3(selectedCollider.EndX, selectedCollider.EndY, selectedCollider.EndZ), colliderPivot, colliderAxis);
            selectedCollider.StartX = start.X; selectedCollider.StartY = start.Y; selectedCollider.StartZ = start.Z;
            selectedCollider.EndX = end.X; selectedCollider.EndY = end.Y; selectedCollider.EndZ = end.Z;
            _current.UpdateColliderRows(GetActiveColliderRowsForWrite());
            PushUndo(colliderSnapshot);
            _redoStack.Clear();
            RefreshPreview(resetCamera: false);
            _statusLabel.Text = $"Mirrored selected collider on {(local ? "local" : "global")} {axisName}.";
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
            var reflected = ReflectPoint(new System.Numerics.Vector3(particle.X, particle.Y, particle.Z), pivot, axis);
            particle.X = reflected.X;
            particle.Y = reflected.Y;
            particle.Z = reflected.Z;
        }

        _current.UpdateParticleRows(_clothList.SelectedIndex, _particleRows);
        _particlePreview.UpdateParticlePreviewRows(_particleRows);
        PushUndo(snapshot);
        _redoStack.Clear();
        RefreshSelectedEditorItem();
        _statusLabel.Text = $"Mirrored selected particles on {(local ? "local" : "global")} {axisName}.";
    }

    private void MirrorCurrentClothAcrossX()
    {
        if (!_directEditMode || !_current.HasDocument || _current.IsBphhb ||
            !CanTransformViewportItems() || _clothList.SelectedIndex < 0)
            return;

        RunGuarded(() =>
        {
            var clothIndex = _clothList.SelectedIndex;
            var snapshot = CaptureFullEditorSnapshot(_editorPage, clothIndex, _editorIndexList.SelectedIndex);
            var sourceBphclDocument = _current.CaptureBphclNativeDocument();
            var reflection = System.Numerics.Matrix4x4.CreateScale(-1.0f, 1.0f, 1.0f);

            if (_current.IsBphcl)
            {
                MirrorBphclBoneRowsForGameSkeleton();
            }
            else
            {
            // First mirror every bone in model space, then recover local poses
            // from the reflected parent world matrix. This preserves hierarchy
            // rotations instead of merely negating a local X translation.
            var originalWorlds = _boneRows.ToDictionary(
                bone => bone.Index,
                bone => GetBoneWorldMatrix(bone.Index, _boneRows));
            var reflectedWorlds = originalWorlds.ToDictionary(
                pair => pair.Key,
                pair => reflection * pair.Value * reflection);
            foreach (var bone in _boneRows.OrderBy(bone => BoneDepth(bone, _boneRows)))
            {
                var parentWorld = bone.ParentIndex >= 0 && reflectedWorlds.TryGetValue(bone.ParentIndex, out var value)
                    ? value
                    : System.Numerics.Matrix4x4.Identity;
                if (!System.Numerics.Matrix4x4.Invert(parentWorld, out var inverseParentWorld))
                    throw new InvalidOperationException($"Cannot mirror bone '{bone.Name}' because its parent transform is singular.");

                var local = reflectedWorlds[bone.Index] * inverseParentWorld;
                if (!System.Numerics.Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation))
                    throw new InvalidOperationException($"Cannot decompose the mirrored transform for bone '{bone.Name}'.");

                bone.X = translation.X;
                bone.Y = translation.Y;
                bone.Z = translation.Z;
                bone.RotationX = rotation.X;
                bone.RotationY = rotation.Y;
                bone.RotationZ = rotation.Z;
                bone.RotationW = rotation.W;
                bone.ScaleX = scale.X;
                bone.ScaleY = scale.Y;
                bone.ScaleZ = scale.Z;
                bone.Name = SwapSideTokens(bone.Name);
            }
            }

            var mirroredBoneNames = _boneRows.ToDictionary(bone => bone.Index, bone => bone.Name);
            foreach (var particle in _particleRows)
                particle.X = -particle.X;

            foreach (var collider in _colliderRows)
            {
                collider.StartX = -collider.StartX;
                collider.EndX = -collider.EndX;
                collider.PlaneNormalX = -collider.PlaneNormalX;
                collider.Transform = reflection * collider.Transform * reflection;
                collider.Name = SwapSideTokens(collider.Name);
                if (mirroredBoneNames.TryGetValue(collider.BoneIndex, out var boneName))
                    collider.BoneName = boneName;
            }

            _current.UpdateBoneRows(clothIndex, _boneRows);
            _current.UpdateParticleRows(clothIndex, _particleRows);
            // Reflection preserves every distance, so constraint rest lengths
            // remain valid. BPHCL also mirrors its uncompressed skin/output
            // matrices; HKCL has no corresponding native payload here.
            _current.FlipTriangleWinding(clothIndex);
            _current.MirrorBphclClothGeometryAcrossX(clothIndex, sourceBphclDocument);
            _current.UpdateColliderRows(_colliderRows);

            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: _editorIndexList.SelectedIndex);
            _statusLabel.Text = "Mirrored the current cloth across global X, including side-name swaps.";
        });
    }

    private void MirrorBphclBoneRowsForGameSkeleton()
    {
        // TotK BPHCL skeleton poses use the actor's bone-space conventions,
        // not the viewport's axes. Vanilla L/R pairs show that translation
        // reflects through X while the quaternion basis reflects through Z.
        // Keeping these two authored conventions separate reproduces the raw
        // Clavicle_L -> Clavicle_R records found in multiple vanilla files.
        var sourceRows = _boneRows.Select(CloneBone).ToList();
        var mirroredRows = sourceRows.Select(CloneBone).ToList();
        var completed = new HashSet<int>();
        var visiting = new HashSet<int>();
        var mirroredBranches = new Dictionary<int, bool>();
        var positionReflection = System.Numerics.Matrix4x4.CreateScale(-1.0f, 1.0f, 1.0f);
        var rotationReflection = System.Numerics.Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f);

        bool MirrorBone(int boneIndex)
        {
            if (completed.Contains(boneIndex))
                return mirroredBranches.TryGetValue(boneIndex, out var previous) && previous;

            var source = sourceRows.FirstOrDefault(bone => bone.Index == boneIndex);
            var target = mirroredRows.FirstOrDefault(bone => bone.Index == boneIndex);
            if (source == null || target == null)
                return false;

            if (!visiting.Add(boneIndex))
                return false;

            var inheritsMirror = source.ParentIndex >= 0 && MirrorBone(source.ParentIndex);
            visiting.Remove(boneIndex);

            var mirroredName = SwapSideTokens(source.Name);
            var mirrorsThisBranch = inheritsMirror || !string.Equals(mirroredName, source.Name, StringComparison.Ordinal);
            completed.Add(boneIndex);
            mirroredBranches[boneIndex] = mirrorsThisBranch;
            if (!mirrorsThisBranch)
                return false;

            var parentWorld = GetBoneWorldMatrix(target.ParentIndex, mirroredRows);
            if (!System.Numerics.Matrix4x4.Invert(parentWorld, out var inverseParent))
                throw new InvalidOperationException($"Cannot mirror bone '{source.Name}' because its parent transform is singular.");

            var sourceWorld = GetBoneWorldMatrix(source.Index, sourceRows);
            var mirroredPositionWorld = positionReflection * sourceWorld * positionReflection;
            var mirroredRotationWorld = rotationReflection * sourceWorld * rotationReflection;
            if (!System.Numerics.Matrix4x4.Decompose(mirroredPositionWorld * inverseParent, out var scale, out _, out var translation) ||
                !System.Numerics.Matrix4x4.Decompose(mirroredRotationWorld * inverseParent, out _, out var rotation, out _))
            {
                throw new InvalidOperationException($"Cannot decompose the mirrored transform for bone '{source.Name}'.");
            }

            target.Name = mirroredName;
            target.X = translation.X;
            target.Y = translation.Y;
            target.Z = translation.Z;
            target.RotationX = rotation.X;
            target.RotationY = rotation.Y;
            target.RotationZ = rotation.Z;
            target.RotationW = rotation.W;
            target.ScaleX = scale.X;
            target.ScaleY = scale.Y;
            target.ScaleZ = scale.Z;
            return true;
        }

        foreach (var bone in sourceRows)
            MirrorBone(bone.Index);

        _boneRows = mirroredRows;
    }

    private static int BoneDepth(BoneEditRow bone, IReadOnlyList<BoneEditRow> bones)
    {
        var depth = 0;
        var current = bone.ParentIndex;
        var visited = new HashSet<int>();
        while (current >= 0 && visited.Add(current))
        {
            var parent = bones.FirstOrDefault(candidate => candidate.Index == current);
            if (parent == null)
                break;
            depth++;
            current = parent.ParentIndex;
        }
        return depth;
    }

    private static string SwapSideTokens(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var characters = name.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (characters[index] is not ('L' or 'R'))
                continue;

            var startsToken = index == 0 || IsSideTokenSeparator(characters[index - 1]);
            var endsToken = index == characters.Length - 1 || IsSideTokenSeparator(characters[index + 1]);
            if (startsToken && endsToken)
                characters[index] = characters[index] == 'L' ? 'R' : 'L';
        }
        return new string(characters);
    }

    private static bool IsSideTokenSeparator(char value) =>
        value is '_' or ':' or '-' or '.';

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
            SynchronizeParticleConstraintGeometry(_viewportParticleRowsBeforeTransform);
            ApplyCurrentRowsToDocument();
            PushUndo(_viewportMoveSnapshot);
            _redoStack.Clear();
            RefreshSelectedEditorItem();
        }
        _viewportMoveSnapshot = null;
        _viewportParticleRowsBeforeTransform = null;
        _viewportTransformChanged = false;
        _viewportWorldTranslation = System.Numerics.Vector3.Zero;
        _mirrorPairs.Clear();
        UpdateButtons();
        _statusLabel.Text = "Transformed selected editor item(s).";
    }

    private void CancelViewportParticleMove()
    {
        if (_viewportMoveSnapshot == null)
            return;

        ApplyEditorSnapshot(_viewportMoveSnapshot);
        _viewportMoveSnapshot = null;
        _viewportParticleRowsBeforeTransform = null;
        _viewportTransformChanged = false;
        _viewportWorldTranslation = System.Numerics.Vector3.Zero;
        _mirrorPairs.Clear();
        _statusLabel.Text = "Canceled transform.";
    }

    // Native BPHCL documents can update particle positions, skeleton poses, and
    // collider transforms in-place. Other external formats stay inspection-only.
    private bool CanTransformViewportItems() => !_current.IsReadOnlyExternal || _current.IsBphcl;

    private bool HasActiveEditorSelection() => _editorPage switch
    {
        EditorPage.Particles => _selectedParticleIndices.Count > 0,
        EditorPage.Bones => GetSelectedBone() != null,
        EditorPage.Colliders => GetSelectedCollider() != null,
        _ => false
    };

    private BoneEditRow? GetSelectedBone() =>
        _editorIndexList.SelectedIndex >= 0 && _editorIndexList.SelectedIndex < _boneRows.Count
            ? _boneRows[_editorIndexList.SelectedIndex]
            : null;

    private ColliderEditRow? GetSelectedCollider() =>
        _editorIndexList.SelectedIndex >= 0 && _editorIndexList.SelectedIndex < _colliderRows.Count
            ? _colliderRows[_editorIndexList.SelectedIndex]
            : null;

    private void UpdateLivePreview()
    {
        ApplyCurrentRowsToDocument();
        RefreshPreview(resetCamera: false);
    }

    private void UpdateViewportTransformPreview()
    {
        // During modal transforms keep the full document untouched. The
        // viewport can update its cached geometry directly at mouse speed;
        // EndViewportParticleMove commits the rows once on confirmation.
        switch (_editorPage)
        {
            case EditorPage.Bones:
                _particlePreview.UpdateBonePreviewRows(_boneRows);
                break;
            case EditorPage.Colliders:
                _particlePreview.UpdateColliderPreviewRows(_colliderRows);
                break;
            default:
                _particlePreview.UpdateParticlePreviewRows(_particleRows);
                break;
        }
    }

    private static void TranslateCollider(ColliderEditRow collider, System.Numerics.Vector3 delta)
    {
        collider.StartX += delta.X;
        collider.StartY += delta.Y;
        collider.StartZ += delta.Z;
        collider.EndX += delta.X;
        collider.EndY += delta.Y;
        collider.EndZ += delta.Z;
        var transform = collider.Transform;
        transform.M41 += delta.X;
        transform.M42 += delta.Y;
        transform.M43 += delta.Z;
        collider.Transform = transform;
    }

    private void ScaleBoneTranslation(BoneEditRow bone, float factor, System.Numerics.Vector3? axis)
    {
        var translation = new System.Numerics.Vector3(bone.X, bone.Y, bone.Z);
        if (!axis.HasValue)
        {
            translation *= factor;
            bone.X = Math.Clamp(translation.X, -30.0f, 30.0f);
            bone.Y = Math.Clamp(translation.Y, -30.0f, 30.0f);
            bone.Z = Math.Clamp(translation.Z, -30.0f, 30.0f);
            return;
        }

        // A bone translation is expressed in the coordinate system of its parent.
        var direction = NormalizeOrDefault(
            System.Numerics.Vector3.Transform(axis.Value, System.Numerics.Quaternion.Inverse(GetBoneWorldRotation(bone.ParentIndex))),
            System.Numerics.Vector3.UnitX);
        var alongAxis = System.Numerics.Vector3.Dot(translation, direction);
        translation += direction * (alongAxis * (factor - 1.0f));
        bone.X = Math.Clamp(translation.X, -30.0f, 30.0f);
        bone.Y = Math.Clamp(translation.Y, -30.0f, 30.0f);
        bone.Z = Math.Clamp(translation.Z, -30.0f, 30.0f);
    }

    private void RotateBone(BoneEditRow bone, float radians, System.Numerics.Vector3 axis, bool localAxis)
    {
        var existing = new System.Numerics.Quaternion(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW);
        if (existing.LengthSquared() < 0.000001f)
            existing = System.Numerics.Quaternion.Identity;
        else
            existing = System.Numerics.Quaternion.Normalize(existing);

        var parentRotation = GetBoneWorldRotation(bone.ParentIndex);
        var parentAxis = NormalizeOrDefault(
            System.Numerics.Vector3.Transform(axis, System.Numerics.Quaternion.Inverse(parentRotation)),
            System.Numerics.Vector3.UnitY);
        var delta = System.Numerics.Quaternion.CreateFromAxisAngle(parentAxis, radians);
        // World axis rotations are expressed in parent space, while a repeated
        // X/Y/Z uses the bone's own local axis and composes on the other side.
        var result = localAxis
            ? System.Numerics.Quaternion.Normalize(existing * delta)
            : System.Numerics.Quaternion.Normalize(delta * existing);
        bone.RotationX = result.X;
        bone.RotationY = result.Y;
        bone.RotationZ = result.Z;
        bone.RotationW = result.W;
    }

    private static System.Numerics.Matrix4x4 GetBoneWorldMatrix(int boneIndex, IReadOnlyList<BoneEditRow> bones)
    {
        if (boneIndex < 0)
            return System.Numerics.Matrix4x4.Identity;

        var chain = new Stack<BoneEditRow>();
        var visited = new HashSet<int>();
        var currentIndex = boneIndex;
        while (currentIndex >= 0 && visited.Add(currentIndex))
        {
            var bone = bones.FirstOrDefault(candidate => candidate.Index == currentIndex);
            if (bone == null)
                break;

            chain.Push(bone);
            currentIndex = bone.ParentIndex;
        }

        var world = System.Numerics.Matrix4x4.Identity;
        while (chain.Count > 0)
            world = CreateBoneLocalMatrix(chain.Pop()) * world;
        return world;
    }

    private static System.Numerics.Matrix4x4 CreateBoneLocalMatrix(BoneEditRow bone)
    {
        var rotation = new System.Numerics.Quaternion(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW);
        if (rotation.LengthSquared() < 0.000001f)
            rotation = System.Numerics.Quaternion.Identity;
        else
            rotation = System.Numerics.Quaternion.Normalize(rotation);

        return System.Numerics.Matrix4x4.CreateScale(bone.ScaleX, bone.ScaleY, bone.ScaleZ)
            * System.Numerics.Matrix4x4.CreateFromQuaternion(rotation)
            * System.Numerics.Matrix4x4.CreateTranslation(bone.X, bone.Y, bone.Z);
    }

    private System.Numerics.Quaternion GetBoneWorldRotation(int boneIndex)
        => GetBoneWorldRotation(boneIndex, _boneRows);

    private static System.Numerics.Quaternion GetBoneWorldRotation(int boneIndex, IReadOnlyList<BoneEditRow> bones)
    {
        if (boneIndex < 0)
            return System.Numerics.Quaternion.Identity;

        var chain = new Stack<BoneEditRow>();
        var visited = new HashSet<int>();
        var currentIndex = boneIndex;
        while (currentIndex >= 0 && visited.Add(currentIndex))
        {
            var bone = bones.FirstOrDefault(candidate => candidate.Index == currentIndex);
            if (bone == null)
                break;

            chain.Push(bone);
            currentIndex = bone.ParentIndex;
        }

        var world = System.Numerics.Quaternion.Identity;
        while (chain.Count > 0)
        {
            var bone = chain.Pop();
            var local = new System.Numerics.Quaternion(bone.RotationX, bone.RotationY, bone.RotationZ, bone.RotationW);
            if (local.LengthSquared() < 0.000001f)
                local = System.Numerics.Quaternion.Identity;
            else
                local = System.Numerics.Quaternion.Normalize(local);
            world = System.Numerics.Quaternion.Normalize(local * world);
        }

        return world;
    }

    private static void ScaleCollider(ColliderEditRow collider, float factor, System.Numerics.Vector3? axis)
    {
        // A plane is infinite: it has neither dimensions nor a radius to scale.
        // Leave it alone instead of scaling the editor's representative point.
        if (collider.IsPlane)
            return;

        var center = new System.Numerics.Vector3(
            (collider.StartX + collider.EndX) * 0.5f,
            (collider.StartY + collider.EndY) * 0.5f,
            (collider.StartZ + collider.EndZ) * 0.5f);
        var start = ScalePoint(new System.Numerics.Vector3(collider.StartX, collider.StartY, collider.StartZ), center, factor, axis);
        var end = ScalePoint(new System.Numerics.Vector3(collider.EndX, collider.EndY, collider.EndZ), center, factor, axis);
        const float minimumCapsuleLength = 0.002f;
        var direction = end - start;
        if (direction.LengthSquared() < minimumCapsuleLength * minimumCapsuleLength)
        {
            var originalDirection = new System.Numerics.Vector3(
                collider.EndX - collider.StartX,
                collider.EndY - collider.StartY,
                collider.EndZ - collider.StartZ);
            direction = NormalizeOrDefault(originalDirection, System.Numerics.Vector3.UnitY);
            start = center - direction * (minimumCapsuleLength * 0.5f);
            end = center + direction * (minimumCapsuleLength * 0.5f);
        }
        collider.StartX = start.X; collider.StartY = start.Y; collider.StartZ = start.Z;
        collider.EndX = end.X; collider.EndY = end.Y; collider.EndZ = end.Z;
        if (!axis.HasValue)
            collider.Radius = Math.Clamp(collider.Radius * factor, 0.0001f, 2.0f);
    }

    private static void RotateCollider(ColliderEditRow collider, float radians, System.Numerics.Vector3 axis)
    {
        // hclCollidable stores its shape points in local space. Keep those points
        // unchanged and rotate the hkTransform basis around the collider center.
        // Rotating only the displayed endpoints causes the next serialize/undo pass
        // to rebake a different local shape and makes the collider drift.
        var oldTransform = collider.Transform;
        var startWorld = new System.Numerics.Vector3(collider.StartX, collider.StartY, collider.StartZ);
        var endWorld = new System.Numerics.Vector3(collider.EndX, collider.EndY, collider.EndZ);
        var localStart = InverseTransformColliderPoint(oldTransform, startWorld);
        var localEnd = InverseTransformColliderPoint(oldTransform, endWorld);
        var localCenter = (localStart + localEnd) * 0.5f;
        var center = (startWorld + endWorld) * 0.5f;
        var rotation = System.Numerics.Matrix4x4.CreateFromAxisAngle(NormalizeOrDefault(axis, System.Numerics.Vector3.UnitY), radians);

        var transform = oldTransform;
        var xAxis = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(oldTransform.M11, oldTransform.M12, oldTransform.M13), rotation);
        var yAxis = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(oldTransform.M21, oldTransform.M22, oldTransform.M23), rotation);
        var zAxis = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(oldTransform.M31, oldTransform.M32, oldTransform.M33), rotation);
        transform.M11 = xAxis.X; transform.M12 = xAxis.Y; transform.M13 = xAxis.Z;
        transform.M21 = yAxis.X; transform.M22 = yAxis.Y; transform.M23 = yAxis.Z;
        transform.M31 = zAxis.X; transform.M32 = zAxis.Y; transform.M33 = zAxis.Z;

        var transformedLocalCenter = TransformColliderDirection(transform, localCenter);
        transform.M41 = center.X - transformedLocalCenter.X;
        transform.M42 = center.Y - transformedLocalCenter.Y;
        transform.M43 = center.Z - transformedLocalCenter.Z;
        collider.Transform = transform;

        var start = TransformColliderPoint(transform, localStart);
        var end = TransformColliderPoint(transform, localEnd);
        collider.StartX = start.X; collider.StartY = start.Y; collider.StartZ = start.Z;
        collider.EndX = end.X; collider.EndY = end.Y; collider.EndZ = end.Z;
        if (collider.IsPlane)
        {
            var normal = System.Numerics.Vector3.Transform(
                new System.Numerics.Vector3(collider.PlaneNormalX, collider.PlaneNormalY, collider.PlaneNormalZ),
                rotation);
            normal = NormalizeOrDefault(normal, System.Numerics.Vector3.UnitY);
            collider.PlaneNormalX = normal.X;
            collider.PlaneNormalY = normal.Y;
            collider.PlaneNormalZ = normal.Z;
        }
    }

    private static System.Numerics.Vector3 TransformColliderPoint(System.Numerics.Matrix4x4 transform, System.Numerics.Vector3 local)
    {
        var direction = TransformColliderDirection(transform, local);
        return direction + new System.Numerics.Vector3(transform.M41, transform.M42, transform.M43);
    }

    private static System.Numerics.Vector3 TransformColliderDirection(System.Numerics.Matrix4x4 transform, System.Numerics.Vector3 local) => new(
        transform.M11 * local.X + transform.M21 * local.Y + transform.M31 * local.Z,
        transform.M12 * local.X + transform.M22 * local.Y + transform.M32 * local.Z,
        transform.M13 * local.X + transform.M23 * local.Y + transform.M33 * local.Z);

    private static System.Numerics.Vector3 InverseTransformColliderPoint(System.Numerics.Matrix4x4 transform, System.Numerics.Vector3 world)
    {
        // Only the 3x3 rotation basis participates in an hkTransform point
        // conversion. The fourth column holds non-homogeneous Havok data.
        var basis = new System.Numerics.Matrix4x4(
            transform.M11, transform.M12, transform.M13, 0.0f,
            transform.M21, transform.M22, transform.M23, 0.0f,
            transform.M31, transform.M32, transform.M33, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);
        if (!System.Numerics.Matrix4x4.Invert(basis, out var inverse))
            throw new InvalidOperationException("Collider transform cannot be inverted.");

        var translated = world - new System.Numerics.Vector3(transform.M41, transform.M42, transform.M43);
        return new System.Numerics.Vector3(
            inverse.M11 * translated.X + inverse.M21 * translated.Y + inverse.M31 * translated.Z,
            inverse.M12 * translated.X + inverse.M22 * translated.Y + inverse.M32 * translated.Z,
            inverse.M13 * translated.X + inverse.M23 * translated.Y + inverse.M33 * translated.Z);
    }

    private static System.Numerics.Vector3 ScalePoint(System.Numerics.Vector3 point, System.Numerics.Vector3 center, float factor, System.Numerics.Vector3? axis)
    {
        var offset = point - center;
        if (!axis.HasValue)
            return center + offset * factor;

        var direction = NormalizeOrDefault(axis.Value, System.Numerics.Vector3.UnitX);
        return center + offset + direction * (System.Numerics.Vector3.Dot(offset, direction) * (factor - 1.0f));
    }

    private void BuildMirrorPairs()
    {
        _mirrorPairs.Clear();
        if (!_particlePreview.MirrorModeEnabled)
            return;

        if (_editorPage == EditorPage.Particles)
        {
            foreach (var source in _particleRows.Where(particle => _selectedParticleIndices.Contains(particle.Index)))
                AddMirrorPair(source.Index, new System.Numerics.Vector3(source.X, source.Y, source.Z),
                    _particleRows.Where(candidate => !_selectedParticleIndices.Contains(candidate.Index))
                        .Select(candidate => (candidate.Index, Position: new System.Numerics.Vector3(candidate.X, candidate.Y, candidate.Z))));
            return;
        }

        if (_editorPage == EditorPage.Bones && GetSelectedBone() is { } bone)
        {
            AddMirrorPair(bone.Index, new System.Numerics.Vector3(bone.X, bone.Y, bone.Z),
                _boneRows.Where(candidate => candidate.Index != bone.Index)
                    .Select(candidate => (candidate.Index, Position: new System.Numerics.Vector3(candidate.X, candidate.Y, candidate.Z))));
            return;
        }

        if (_editorPage == EditorPage.Colliders && GetSelectedCollider() is { } collider)
        {
            var center = ColliderCenter(collider);
            AddMirrorPair(collider.Index, center,
                _colliderRows.Where(candidate => candidate.Index != collider.Index)
                    .Select(candidate => (candidate.Index, Position: ColliderCenter(candidate))));
        }
    }

    private void AddMirrorPair(int sourceIndex, System.Numerics.Vector3 sourcePosition, IEnumerable<(int Index, System.Numerics.Vector3 Position)> candidates)
    {
        var mirrored = MirrorPoint(sourcePosition);
        var partner = candidates
            .Where(candidate => MathF.Sign(candidate.Position.X) != MathF.Sign(sourcePosition.X) || MathF.Abs(sourcePosition.X) < 0.0001f)
            .OrderBy(candidate => System.Numerics.Vector3.DistanceSquared(candidate.Position, mirrored))
            .Select(candidate => (int?)candidate.Index)
            .FirstOrDefault();
        if (partner.HasValue)
            _mirrorPairs[sourceIndex] = partner.Value;
    }

    private bool SnapMirrorPairsToSources()
    {
        return _editorPage switch
        {
            EditorPage.Particles => ApplyMirrorToParticles(),
            EditorPage.Bones when GetSelectedBone() is { } bone => ApplyMirrorToBone(bone),
            EditorPage.Colliders when GetSelectedCollider() is { } collider => ApplyMirrorToCollider(collider),
            _ => false
        };
    }

    private bool ApplyMirrorToParticles()
    {
        if (!_particlePreview.MirrorModeEnabled)
            return false;

        var changed = false;

        foreach (var pair in _mirrorPairs)
        {
            var source = _particleRows.FirstOrDefault(particle => particle.Index == pair.Key);
            var partner = _particleRows.FirstOrDefault(particle => particle.Index == pair.Value);
            if (source == null || partner == null)
                continue;

            var mirrored = MirrorPoint(new System.Numerics.Vector3(source.X, source.Y, source.Z));
            partner.X = mirrored.X;
            partner.Y = mirrored.Y;
            partner.Z = mirrored.Z;
            changed = true;
        }

        return changed;
    }

    private bool ApplyMirrorToBone(BoneEditRow source)
    {
        if (!_particlePreview.MirrorModeEnabled || !_mirrorPairs.TryGetValue(source.Index, out var partnerIndex))
            return false;

        var partner = _boneRows.FirstOrDefault(bone => bone.Index == partnerIndex);
        if (partner == null)
            return false;

        if (_current.IsBphcl)
        {
            // See MirrorBphclBoneRowsForGameSkeleton: BPHCL's authored
            // translation and quaternion bases mirror on different axes.
            var bphclSourceWorld = GetBoneWorldMatrix(source.Index, _boneRows);
            var bphclParentWorld = GetBoneWorldMatrix(partner.ParentIndex, _boneRows);
            if (!System.Numerics.Matrix4x4.Invert(bphclParentWorld, out var bphclInverseParent))
                return false;

            var positionReflection = System.Numerics.Matrix4x4.CreateScale(-1.0f, 1.0f, 1.0f);
            var rotationReflection = System.Numerics.Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f);
            if (!System.Numerics.Matrix4x4.Decompose(positionReflection * bphclSourceWorld * positionReflection * bphclInverseParent, out var bphclScale, out _, out var bphclTranslation) ||
                !System.Numerics.Matrix4x4.Decompose(rotationReflection * bphclSourceWorld * rotationReflection * bphclInverseParent, out _, out var bphclRotation, out _))
            {
                return false;
            }

            partner.X = bphclTranslation.X;
            partner.Y = bphclTranslation.Y;
            partner.Z = bphclTranslation.Z;
            partner.RotationX = bphclRotation.X;
            partner.RotationY = bphclRotation.Y;
            partner.RotationZ = bphclRotation.Z;
            partner.RotationW = bphclRotation.W;
            partner.ScaleX = bphclScale.X;
            partner.ScaleY = bphclScale.Y;
            partner.ScaleZ = bphclScale.Z;
            return true;
        }

        // Mirror the complete world transform, then return it to the paired
        // bone's parent space. Mirroring a local quaternion directly works
        // only for root bones and leaves child bones twisting incorrectly.
        var reflection = System.Numerics.Matrix4x4.CreateScale(-1.0f, 1.0f, 1.0f);
        var sourceWorld = GetBoneWorldMatrix(source.Index, _boneRows);
        var mirroredWorld = reflection * sourceWorld * reflection;
        var partnerParentWorld = GetBoneWorldMatrix(partner.ParentIndex, _boneRows);
        if (!System.Numerics.Matrix4x4.Invert(partnerParentWorld, out var inverseParent) ||
            !System.Numerics.Matrix4x4.Decompose(mirroredWorld * inverseParent, out var scale, out var rotation, out var translation))
        {
            return false;
        }

        partner.X = translation.X;
        partner.Y = translation.Y;
        partner.Z = translation.Z;
        partner.RotationX = rotation.X;
        partner.RotationY = rotation.Y;
        partner.RotationZ = rotation.Z;
        partner.RotationW = rotation.W;
        partner.ScaleX = scale.X;
        partner.ScaleY = scale.Y;
        partner.ScaleZ = scale.Z;
        return true;
    }

    private bool ApplyMirrorToCollider(ColliderEditRow source)
    {
        if (!_particlePreview.MirrorModeEnabled || !_mirrorPairs.TryGetValue(source.Index, out var partnerIndex))
            return false;

        var partner = _colliderRows.FirstOrDefault(collider => collider.Index == partnerIndex);
        if (partner == null)
            return false;

        var start = MirrorPoint(new System.Numerics.Vector3(source.StartX, source.StartY, source.StartZ));
        var end = MirrorPoint(new System.Numerics.Vector3(source.EndX, source.EndY, source.EndZ));
        partner.StartX = start.X; partner.StartY = start.Y; partner.StartZ = start.Z;
        partner.EndX = end.X; partner.EndY = end.Y; partner.EndZ = end.Z;
        partner.Radius = source.Radius;
        return true;
    }

    private static System.Numerics.Vector3 MirrorPoint(System.Numerics.Vector3 point) => new(-point.X, point.Y, point.Z);

    private static System.Numerics.Vector3 ReflectPoint(System.Numerics.Vector3 point, System.Numerics.Vector3 pivot, System.Numerics.Vector3 axis)
    {
        var offset = point - pivot;
        return pivot + offset - 2.0f * axis * System.Numerics.Vector3.Dot(offset, axis);
    }

    private static System.Numerics.Vector3 ColliderCenter(ColliderEditRow collider) => new(
        (collider.StartX + collider.EndX) * 0.5f,
        (collider.StartY + collider.EndY) * 0.5f,
        (collider.StartZ + collider.EndZ) * 0.5f);

    private void AttachSelectedItemsToChosenBone()
    {
        if (_current.IsReadOnlyExternal || _particleBindBoneCombo.SelectedItem is not BoneComboItem item)
            return;

        switch (_editorPage)
        {
            case EditorPage.Colliders:
                AttachSelectedColliderToBone(item.Index);
                break;
            case EditorPage.Bones:
                AttachSelectedBoneToBone(item.Index);
                break;
            default:
            {
                var indices = _selectedParticleIndices.Count > 0
                    ? _selectedParticleIndices.ToArray()
                    : TryGetSelectedParticle(out var activeParticle)
                        ? new[] { activeParticle.Index }
                        : Array.Empty<int>();
                if (indices.Length > 0)
                    AttachParticlesToBone(indices, item.Index);
                break;
            }
        }
    }

    private void RefreshParticleBindingPanel()
    {
        var attachedIndex = GetAttachmentBoneIndex();
        var previousIndex = attachedIndex >= 0
            ? attachedIndex
            : _particleBindBoneCombo.SelectedItem is BoneComboItem selected ? selected.Index : -1;
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

        var selectedCount = _editorPage == EditorPage.Particles ? _selectedParticleIndices.Count : 0;
        if (_editorPage == EditorPage.Particles && selectedCount == 0 && TryGetSelectedParticle(out _))
            selectedCount = 1;

        var itemLabel = _editorPage switch
        {
            EditorPage.Bones => "bone",
            EditorPage.Colliders => "collider",
            _ => "particle"
        };
        var hasSelection = _editorPage switch
        {
            EditorPage.Bones => GetSelectedBone() != null,
            EditorPage.Colliders => GetSelectedCollider() != null,
            _ => selectedCount > 0
        };
        var countLabel = _editorPage == EditorPage.Particles && selectedCount > 1
            ? $"Selected: {selectedCount} particles"
            : hasSelection ? $"Selected: 1 {itemLabel}" : $"Select a {itemLabel}.";

        _particleBindGroup!.Text = "Attach to Bone";
        _particleBindStatusLabel.Text = countLabel;
        _particleBindButton.Text = "Attach";
        _particleBindButton.Enabled = !_current.IsReadOnlyExternal && hasSelection && _particleBindBoneCombo.Items.Count > 0;
    }

    private int GetAttachmentBoneIndex() => _editorPage switch
    {
        EditorPage.Bones => GetSelectedBone()?.ParentIndex ?? -1,
        EditorPage.Colliders => GetSelectedCollider()?.BoneIndex ?? -1,
        _ => -1
    };

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

    private void AttachParticleToBone(int particleIndex, int boneIndex)
    {
        AttachParticlesToBone(new[] { particleIndex }, boneIndex);
    }

    private void AttachParticlesToBone(IReadOnlyCollection<int> particleIndices, int boneIndex)
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
                ? $"Attached particle {particleIndices.First()} to bone {bone.Name}."
                : $"Snapped {particleIndices.Count} selected particles to bone {bone.Name} using particle {anchorIndex} as the anchor.";
        });
    }

    private void AttachSelectedColliderToBone(int boneIndex)
    {
        var collider = GetSelectedCollider();
        if (collider == null)
            return;

        RunGuarded(() =>
        {
            var preview = _current.GetParticlePreview(_clothList.SelectedIndex);
            var bone = preview.Bones.FirstOrDefault(candidate => candidate.Index == boneIndex);
            var boneRow = _boneRows.FirstOrDefault(candidate => candidate.Index == boneIndex);
            if (bone == null || boneRow == null)
                return;

            var snapshot = CaptureEditorSnapshot(EditorPage.Colliders, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            TranslateCollider(collider, bone.Position - ColliderCenter(collider));
            collider.BoneIndex = boneIndex;
            collider.BoneName = boneRow.Name;
            _current.UpdateColliderRows(GetActiveColliderRowsForWrite());
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: _editorIndexList.SelectedIndex);
            _statusLabel.Text = $"Attached collider {collider.Index} to bone {boneRow.Name}.";
        });
    }

    private void AttachSelectedBoneToBone(int boneIndex)
    {
        var selectedBone = GetSelectedBone();
        if (selectedBone == null || selectedBone.Index == boneIndex)
            return;

        RunGuarded(() =>
        {
            var preview = _current.GetParticlePreview(_clothList.SelectedIndex);
            var target = preview.Bones.FirstOrDefault(candidate => candidate.Index == boneIndex);
            var targetRow = _boneRows.FirstOrDefault(candidate => candidate.Index == boneIndex);
            if (target == null || targetRow == null)
                return;

            var snapshot = CaptureEditorSnapshot(EditorPage.Bones, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            var parentWorld = GetBoneWorldMatrix(selectedBone.ParentIndex, _boneRows);
            if (!System.Numerics.Matrix4x4.Invert(parentWorld, out var inverseParentWorld))
                throw new InvalidOperationException("Selected bone parent transform cannot be inverted.");

            var local = System.Numerics.Vector3.Transform(target.Position, inverseParentWorld);
            selectedBone.X = Math.Clamp(local.X, -30.0f, 30.0f);
            selectedBone.Y = Math.Clamp(local.Y, -30.0f, 30.0f);
            selectedBone.Z = Math.Clamp(local.Z, -30.0f, 30.0f);
            _current.UpdateBoneRows(_clothList.SelectedIndex, _boneRows);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: _editorIndexList.SelectedIndex);
            _statusLabel.Text = $"Moved bone {selectedBone.Name} to bone {targetRow.Name}.";
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

        var deletionMessage = _editorPage == EditorPage.Particles
            ? $"Delete {label}? Any links, local ranges, and virtual triangles that use {(_selectedParticleIndices.Count == 1 ? "it" : "them")} will also be removed."
            : $"Delete {label}?";
        if (MessageBox.Show(this, deletionMessage, "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
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

    private void UpdateMirrorModeButton()
    {
        var enabled = _particlePreview.MirrorModeEnabled;
        _mirrorModeButton.Text = enabled ? "Mirror X: On" : "Mirror X";
        _mirrorModeButton.UseVisualStyleBackColor = false;
        _mirrorModeButton.BackColor = enabled ? Color.FromArgb(45, 105, 170) : Color.FromArgb(64, 64, 64);
        _mirrorModeButton.ForeColor = Color.Gainsboro;
        _mirrorModeButton.Font = Font;
    }

    private void AddEditorItemForCurrentTab(string? colliderShapeType = null)
    {
        if (!_directEditMode || _current.IsReadOnlyExternal || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        if (_editorPage == EditorPage.Colliders && colliderShapeType == null)
        {
            _addColliderMenu.Show(_addEditorItemButton, new Point(0, _addEditorItemButton.Height));
            return;
        }

        RunGuarded(() =>
        {
            var snapshot = CaptureFullEditorSnapshot(_editorPage, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            int newIndex;
            if (_editorPage == EditorPage.Bones)
            {
                newIndex = _current.AddBone(_clothList.SelectedIndex);
            }
            else if (_editorPage == EditorPage.Colliders)
            {
                newIndex = _current.AddCollider(
                    _clothList.SelectedIndex,
                    targetBone: _boneRows.FirstOrDefault(),
                    shapeTypeName: colliderShapeType ?? "hclCapsuleShape");
            }
            else
            {
                newIndex = _current.AddParticle(_clothList.SelectedIndex);
                _selectedParticleIndices.Clear();
                _selectedParticleIndices.Add(newIndex);
            }

            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: newIndex);
            _statusLabel.Text = $"Added {_editorPage.ToString().TrimEnd('s').ToLowerInvariant()} {newIndex}.";
        });
    }

    private void CopySelectedEditorItem()
    {
        if (!_directEditMode || _current.IsReadOnlyExternal || !HasActiveEditorSelection())
            return;

        if (_editorPage == EditorPage.Bones && GetSelectedBone() is { } bone)
        {
            _clipboardBone = CloneBone(bone);
            _statusLabel.Text = $"Copied bone {bone.Index}.";
        }
        else if (_editorPage == EditorPage.Colliders && GetSelectedCollider() is { } collider)
        {
            _clipboardCollider = CloneCollider(collider);
            _statusLabel.Text = $"Copied collider {collider.Index}.";
        }
        else if (_editorPage == EditorPage.Particles)
        {
            var selected = _particleRows.FirstOrDefault(particle => _selectedParticleIndices.Contains(particle.Index));
            if (selected != null)
            {
                _clipboardParticle = CloneParticle(selected);
                _statusLabel.Text = $"Copied particle {selected.Index}.";
            }
        }
    }

    private void PasteEditorItem(bool mirrorX = false)
    {
        if (!_directEditMode || _current.IsReadOnlyExternal || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        RunGuarded(() =>
        {
            var snapshot = CaptureFullEditorSnapshot(_editorPage, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            int newIndex;
            if (_editorPage == EditorPage.Bones && _clipboardBone != null)
            {
                var copy = CloneBone(_clipboardBone);
                if (mirrorX)
                    copy.X = -copy.X;
                newIndex = _current.AddBone(_clothList.SelectedIndex, copy);
            }
            else if (_editorPage == EditorPage.Colliders && _clipboardCollider != null)
            {
                var copy = CloneCollider(_clipboardCollider);
                if (mirrorX)
                {
                    copy.StartX = -copy.StartX;
                    copy.EndX = -copy.EndX;
                }
                newIndex = _current.AddCollider(_clothList.SelectedIndex, copy, _boneRows.FirstOrDefault(bone => bone.Index == copy.BoneIndex));
            }
            else if (_editorPage == EditorPage.Particles && _clipboardParticle != null)
            {
                var copy = CloneParticle(_clipboardParticle);
                if (mirrorX)
                    copy.X = -copy.X;
                newIndex = _current.AddParticle(_clothList.SelectedIndex, copy);
            }
            else
            {
                _statusLabel.Text = $"Copy a {_editorPage.ToString().TrimEnd('s').ToLowerInvariant()} first.";
                return;
            }

            PushUndo(snapshot);
            _redoStack.Clear();
            if (_editorPage == EditorPage.Particles)
            {
                _selectedParticleIndices.Clear();
                _selectedParticleIndices.Add(newIndex);
            }
            RefreshParticleGrid(resetCamera: false, selectedListIndex: newIndex);
            _statusLabel.Text = $"Pasted {(mirrorX ? "X-flipped " : string.Empty)}{_editorPage.ToString().TrimEnd('s').ToLowerInvariant()} {newIndex}.";
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
            // Load into a fresh service first. This prevents a failed open from
            // disturbing the current document, and avoids retaining BPHCL-only
            // state while the editor is rebuilt for an HKCL document.
            var loaded = new HkclService();
            loaded.Load(path);

            ResetCurrentDocumentEditorState();
            try
            {
                _current = loaded;
                // Helper files have their own dedicated inspector and never use
                // the merge/cloth workflow. Opening a real cloth restores it.
                _directEditMode = _current.IsBphhb;
                if (_current.IsBphhb)
                    SelectHelperBonePage();
                UpdateModeLayout();
                var extension = Path.GetExtension(path);
                _currentSavePath = extension.Equals(".hkcl", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bphcl", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bphhb", StringComparison.OrdinalIgnoreCase)
                    ? path
                    : null;
                _currentSavePlatform = extension.Equals(".hkcl", StringComparison.OrdinalIgnoreCase)
                    ? HkclService.DetectHkclPlatform(path)
                    : HkclPlatform.WiiU;
                ClearUndoHistory();
                _loadingCurrentDocument = false;
                RefreshCurrentLists();
                var platformLabel = extension.Equals(".hkcl", StringComparison.OrdinalIgnoreCase)
                    ? $" ({(_currentSavePlatform == HkclPlatform.Switch ? "Switch" : "Wii U")})"
                    : string.Empty;
                _statusLabel.Text = $"Loaded {Path.GetFileName(path)}{platformLabel}";
            }
            finally
            {
                _loadingCurrentDocument = false;
            }
        });
    }

    private void ResetCurrentDocumentEditorState()
    {
        _loadingCurrentDocument = true;
        _updatingParticleGrid = true;
        _previewRefreshQueued = false;
        _committingEditorDetail = false;
        _pendingEditSnapshot = null;
        _pendingHelperBoneSnapshot = null;
        _viewportMoveSnapshot = null;
        _viewportTransformChanged = false;
        _selectedParticleIndices.Clear();
        _mirrorPairs.Clear();
        _particleRows.Clear();
        _boneRows.Clear();
        _colliderRows.Clear();
        _clipboardParticle = null;
        _clipboardBone = null;
        _clipboardCollider = null;

        _clothList.Items.Clear();
        _boneList.Items.Clear();
        _detailsBox.Clear();
        _editorIndexList.Items.Clear();
        _editorDetailGrid.Rows.Clear();
        _particleRelationshipGrid.Rows.Clear();
        _helperBoneList.Items.Clear();
        _helperBoneDetailGrid.Rows.Clear();
        _particlePreview.SetData(null);
        _updatingParticleGrid = false;
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
        if (_loadingCurrentDocument)
            return;

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
        if (_loadingCurrentDocument || _committingEditorDetail)
            return;

        if (_simulationTimer.Enabled)
        {
            _simulationTimer.Stop();
            _simulation = null;
            _simulationButton.Text = "Run Simulation";
            SetSimulationButtonLock(false);
        }

        UpdatePreviewPickKind();
        _editorDetailGrid.ReadOnly = !CanEditActiveEditorValues();
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
            var previousIndex = selectedListIndex ?? (_current.IsBphhb
                ? _helperBoneList.SelectedIndex
                : _editorIndexList.SelectedIndex);
            _particleRows.Clear();
            _boneRows.Clear();
            _colliderRows.Clear();
            _editorIndexList.Items.Clear();
            _editorDetailGrid.Rows.Clear();
            _particleRelationshipGrid.Rows.Clear();
            _helperBoneList.Items.Clear();
            _helperBoneDetailGrid.Rows.Clear();
            if (!_current.HasDocument || _clothList.SelectedIndex < 0)
            {
                _particlePreview.SetData(null);
                return;
            }

            _particleRows = _current.GetParticleRows(_clothList.SelectedIndex).ToList();
            _boneRows = _current.GetBoneRows(_clothList.SelectedIndex).ToList();
            _colliderRows = _current.GetColliderRows(_clothList.SelectedIndex).ToList();
            if (_current.IsBphhb)
            {
                foreach (var bone in _boneRows)
                    _helperBoneList.Items.Add($"{bone.Index}: {bone.Name}");

                if (_helperBoneList.Items.Count > 0)
                    _helperBoneList.SelectedIndex = Math.Clamp(previousIndex < 0 ? 0 : previousIndex, 0, _helperBoneList.Items.Count - 1);

                _particlePreview.SetData(null);
                RefreshSelectedHelperBone();
                return;
            }

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
        if (_loadingCurrentDocument || _committingEditorDetail || _updatingParticleGrid || !_directEditMode)
            return;

        _particleRelationshipGrid.Rows.Clear();
        _editorDetailGrid.Rows.Clear();
        _particlePreview.SelectedParticleIndex = -1;
        _particlePreview.SelectedBoneIndex = -1;
        _particlePreview.SelectedColliderIndex = -1;
        if (_current.IsBphhb)
        {
            RefreshSelectedHelperBone();
            return;
        }
        if (_relationshipGroup != null)
            _relationshipGroup.Text = "Particle constraints";
        if (!_current.HasDocument || _clothList.SelectedIndex < 0 || _editorIndexList.SelectedIndex < 0)
        {
            RefreshParticleBindingPanel();
            return;
        }

        if (_relationshipGroup != null)
            _relationshipGroup.Visible = _editorPage == EditorPage.Particles;
        if (_particleBindGroup != null)
        {
            _particleBindGroup.Visible = !_current.IsBphhb;
            _particleBindGroup.Enabled = !_current.IsReadOnlyExternal;
        }

        if (_editorPage == EditorPage.Bones)
        {
            var bone = _boneRows.ElementAtOrDefault(_editorIndexList.SelectedIndex);
            if (bone == null)
                return;

            FillBoneDetailGrid(bone);
            _particlePreview.SelectedBoneIndex = bone.Index;
            _particlePreview.Invalidate();
            RefreshParticleBindingPanel();
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
            RefreshParticleBindingPanel();
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
        PopulateParticleRelationshipGrid(_current.GetParticleRelationships(_clothList.SelectedIndex, particleIndex), particleIndex);
    }

    private void RefreshSelectedHelperBone()
    {
        _helperBoneDetailGrid.Rows.Clear();
        if (!_current.IsBphhb || _helperBoneList.SelectedIndex < 0 || _helperBoneList.SelectedIndex >= _boneRows.Count)
            return;

        var bone = _boneRows[_helperBoneList.SelectedIndex];
        AddHelperBoneDetail("Index", bone.Index.ToString(CultureInfo.InvariantCulture), true);
        AddHelperBoneDetail("Name", bone.Name);
        AddHelperBoneDetail("Base bone index", bone.ParentIndex.ToString(CultureInfo.InvariantCulture));
        AddHelperBoneDetail("Base X", FormatFloat(bone.X));
        AddHelperBoneDetail("Base Y", FormatFloat(bone.Y));
        AddHelperBoneDetail("Base Z", FormatFloat(bone.Z));
        AddHelperBoneDetail("Rotation X", FormatFloat(bone.RotationX));
        AddHelperBoneDetail("Rotation Y", FormatFloat(bone.RotationY));
        AddHelperBoneDetail("Rotation Z", FormatFloat(bone.RotationZ));
        AddHelperBoneDetail("Rotation W", FormatFloat(bone.RotationW));
    }

    private void AddHelperBoneDetail(string field, string value, bool readOnly = false)
    {
        var row = _helperBoneDetailGrid.Rows.Add(field, value);
        _helperBoneDetailGrid.Rows[row].Cells["Value"].ReadOnly = readOnly;
    }

    private bool IsHelperBoneFieldReadOnly(int rowIndex) =>
        rowIndex < 0 || rowIndex >= _helperBoneDetailGrid.Rows.Count ||
        _helperBoneDetailGrid.Rows[rowIndex].Cells["Value"].ReadOnly;

    private void CommitHelperBoneDetailChange()
    {
        if (_committingHelperBoneDetail || _updatingParticleGrid || _applyingSnapshot ||
            _pendingHelperBoneSnapshot == null || !_current.IsBphhb ||
            _helperBoneList.SelectedIndex < 0 || _helperBoneList.SelectedIndex >= _boneRows.Count)
        {
            return;
        }

        var snapshot = _pendingHelperBoneSnapshot;
        _committingHelperBoneDetail = true;
        try
        {
            var bone = _boneRows[_helperBoneList.SelectedIndex];
            bone.Name = ReadHelperBoneDetailText("Name");
            bone.ParentIndex = ReadHelperBoneDetailInt("Base bone index");
            bone.X = ReadHelperBoneDetailFloat("Base X");
            bone.Y = ReadHelperBoneDetailFloat("Base Y");
            bone.Z = ReadHelperBoneDetailFloat("Base Z");
            bone.RotationX = ReadHelperBoneDetailFloat("Rotation X");
            bone.RotationY = ReadHelperBoneDetailFloat("Rotation Y");
            bone.RotationZ = ReadHelperBoneDetailFloat("Rotation Z");
            bone.RotationW = ReadHelperBoneDetailFloat("Rotation W");

            _current.UpdateBoneRows(0, new[] { bone });
            _pendingHelperBoneSnapshot = null;
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: _helperBoneList.SelectedIndex);
            _statusLabel.Text = "Updated helper-bone value.";
            UpdateButtons();
        }
        catch (FormatException ex)
        {
            _pendingHelperBoneSnapshot = snapshot;
            MessageBox.Show(this, ex.Message, "PhysicsTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _pendingHelperBoneSnapshot = snapshot;
            MessageBox.Show(this, ex.ToString(), "PhysicsTool error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _committingHelperBoneDetail = false;
        }
    }

    private string ReadHelperBoneDetailText(string field)
    {
        foreach (DataGridViewRow row in _helperBoneDetailGrid.Rows)
        {
            if (string.Equals(Convert.ToString(row.Cells["Field"].Value), field, StringComparison.OrdinalIgnoreCase))
                return Convert.ToString(row.Cells["Value"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Empty;
    }

    private float ReadHelperBoneDetailFloat(string field)
    {
        var text = ReadHelperBoneDetailText(field);
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException($"Please enter a value for {field}.");
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
            throw new FormatException($"Please enter a valid number for {field}.");
        return value;
    }

    private int ReadHelperBoneDetailInt(string field)
    {
        var text = ReadHelperBoneDetailText(field);
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Please enter a whole number for {field}.");
        return value;
    }

    private void AddHelperBone()
    {
        if (!_current.IsBphhb)
            return;

        RunGuarded(() =>
        {
            var sourceIndex = Math.Max(0, _helperBoneList.SelectedIndex);
            var snapshot = CaptureFullEditorSnapshot(EditorPage.Bones, 0, sourceIndex);
            _current.AddHelperBone(sourceIndex);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: _boneRows.Count);
            _statusLabel.Text = "Added a helper-bone clone. Rename it and adjust its base transform as needed.";
        });
    }

    private void DuplicateSelectedHelperBone()
    {
        if (!_current.IsBphhb || _helperBoneList.SelectedIndex < 0)
            return;

        RunGuarded(() =>
        {
            var sourceIndex = _helperBoneList.SelectedIndex;
            var snapshot = CaptureFullEditorSnapshot(EditorPage.Bones, 0, sourceIndex);
            _current.DuplicateHelperBone(sourceIndex);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: _boneRows.Count);
            _statusLabel.Text = "Duplicated helper-bone graph record.";
        });
    }

    private void MirrorHelperBonesAcrossX()
    {
        if (!_current.IsBphhb || _helperBoneList.SelectedIndex < 0)
            return;

        RunGuarded(() =>
        {
            var selectedIndex = _helperBoneList.SelectedIndex;
            var snapshot = CaptureFullEditorSnapshot(EditorPage.Bones, 0, selectedIndex);
            _current.MirrorHelperBoneAcrossX(selectedIndex);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: selectedIndex);
            _statusLabel.Text = "Mirrored the selected helper-bone base pose across X.";
        });
    }

    private void MoveSelectedHelperBone(int direction)
    {
        if (!_current.IsBphhb || _helperBoneList.SelectedIndex < 0)
            return;

        var sourceIndex = _helperBoneList.SelectedIndex;
        var destinationIndex = sourceIndex + direction;
        if (destinationIndex < 0 || destinationIndex >= _boneRows.Count)
            return;

        RunGuarded(() =>
        {
            var snapshot = CaptureFullEditorSnapshot(EditorPage.Bones, 0, sourceIndex);
            _current.MoveHelperBone(sourceIndex, destinationIndex);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: destinationIndex);
            _statusLabel.Text = direction < 0
                ? "Moved the helper-bone graph entry up."
                : "Moved the helper-bone graph entry down.";
        });
    }

    private void PopulateParticleRelationshipGrid(IReadOnlyList<ParticleRelationshipRow> relationships, int selectedParticleIndex)
    {
        _updatingParticleGrid = true;
        _particleRelationshipGrid.Rows.Clear();
        _relationshipDetailGrid.Rows.Clear();
        if (_relationshipGroup != null)
            _relationshipGroup.Text = $"Particle #{selectedParticleIndex} constraints";

        try
        {
            foreach (var relation in relationships)
            {
                var rowIndex = _particleRelationshipGrid.Rows.Add(
                    FormatRelationshipName(relation),
                    FormatRelationshipOtherParticle(relation, selectedParticleIndex));
                _particleRelationshipGrid.Rows[rowIndex].Tag = relation;
            }

            var preferredRow = relationships.ToList().FindIndex(relation => relation.IsEditable);
            if (preferredRow < 0 && _particleRelationshipGrid.Rows.Count > 0)
                preferredRow = 0;
            if (preferredRow >= 0)
                _particleRelationshipGrid.Rows[preferredRow].Selected = true;
        }
        finally
        {
            _updatingParticleGrid = false;
        }

        PopulateSelectedRelationshipDetails();
    }

    private void PopulateSelectedRelationshipDetails()
    {
        if (_updatingParticleGrid || _committingRelationshipEdit)
            return;

        _relationshipDetailGrid.Rows.Clear();
        var relation = GetSelectedRelationship();
        _removeRelationshipButton.Enabled = relation is { IsEditable: true } && !_current.IsReadOnlyExternal;
        if (relation == null)
            return;

        AddRelationshipDetail("Type", FormatRelationshipName(relation));
        var selectedParticleIndex = _particleRows.ElementAtOrDefault(_editorIndexList.SelectedIndex)?.Index ?? -1;
        if (relation.Kind == "Triangle")
        {
            AddRelationshipDetail("Particles", FormatRelationshipOtherParticle(relation, selectedParticleIndex));
        }
        else if (relation.ParticleA >= 0 && relation.ParticleB >= 0)
        {
            AddRelationshipDetail("Connected particle", FormatRelationshipOtherParticle(relation, selectedParticleIndex));
        }
        AddRelationshipFloatDetail("Rest length", "RestLength", relation.RestLength);
        AddRelationshipFloatDetail("Bend minimum", "BendMinLength", relation.BendMinLength);
        AddRelationshipFloatDetail("Stretch maximum", "StretchMaxLength", relation.StretchMaxLength);
        AddRelationshipFloatDetail("Maximum distance", "MaximumDistance", relation.MaximumDistance);
        AddRelationshipFloatDetail("Stiffness", "Stiffness", relation.Stiffness);
        AddRelationshipFloatDetail("Bend stiffness", "BendStiffness", relation.BendStiffness);
        AddRelationshipFloatDetail("Stretch stiffness", "StretchStiffness", relation.StretchStiffness);
    }

    private void AddRelationshipFloatDetail(string label, string property, float? value)
    {
        if (!value.HasValue)
            return;

        var rowIndex = _relationshipDetailGrid.Rows.Add(label, FormatFloat(value.Value));
        var row = _relationshipDetailGrid.Rows[rowIndex];
        var editable = GetSelectedRelationship() is { IsEditable: true } && _current.CanEditConstraintValues;
        row.Tag = editable ? property : null;
        row.Cells["Value"].ReadOnly = !editable;
    }

    private void AddRelationshipDetail(string label, string value)
    {
        var rowIndex = _relationshipDetailGrid.Rows.Add(label, value);
        _relationshipDetailGrid.Rows[rowIndex].Cells["Value"].ReadOnly = true;
    }

    private ParticleRelationshipRow? GetSelectedRelationship()
    {
        return _particleRelationshipGrid.SelectedRows
            .Cast<DataGridViewRow>()
            .FirstOrDefault()?.Tag as ParticleRelationshipRow;
    }

    private static string FormatRelationshipName(ParticleRelationshipRow relation)
    {
        if (relation.Kind == "Local")
            return "Local range";
        if (relation.Kind == "State")
            return relation.Name;
        if (relation.Kind == "Triangle")
            return "Surface triangle";

        return relation.Name
            .Replace("hcl", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("ConstraintSet", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Link", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string FormatRelationshipOtherParticle(ParticleRelationshipRow relation, int selectedParticleIndex)
    {
        if (relation.ParticleA >= 0 && relation.ParticleB >= 0)
        {
            var other = relation.ParticleA == selectedParticleIndex ? relation.ParticleB : relation.ParticleA;
            return $"#{other}";
        }

        return relation.Kind switch
        {
            "Triangle" => string.Join(", ", relation.Particles
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(index => $"#{index}")),
            "State" => relation.Name,
            _ => relation.Particles
        };
    }

    private void ApplyParticleGridEdits()
    {
        if ((_current.IsReadOnlyExternal && !_current.IsBphcl) || _updatingParticleGrid || !_current.HasDocument || _clothList.SelectedIndex < 0)
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
                _current.UpdateColliderRows(GetActiveColliderRowsForWrite());
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
        if (!CanEditActiveEditorValues() || _committingEditorDetail || _updatingParticleGrid || _applyingSnapshot || _pendingEditSnapshot == null || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        var snapshot = _pendingEditSnapshot;
        _committingEditorDetail = true;
        try
        {
            ApplySelectedDetailsToCache();
            if (_editorPage == EditorPage.Particles)
                SynchronizeParticleConstraintGeometry(snapshot.Particles);
            ApplyCurrentRowsToDocument();
            _pendingEditSnapshot = null;
            PushUndo(snapshot);
            _redoStack.Clear();
            UpdatePreviewAfterCommittedEditorEdit();
            _committingEditorDetail = false;
            _statusLabel.Text = "Updated value.";
            UpdateButtons();
        }
        catch (FormatException ex)
        {
            _committingEditorDetail = false;
            _pendingEditSnapshot = snapshot;
            MessageBox.Show(this, ex.Message, "PhysicsTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _committingEditorDetail = false;
            _pendingEditSnapshot = snapshot;
            MessageBox.Show(this, ex.ToString(), "PhysicsTool error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool CommitActiveEditorDetail()
    {
        if (!_directEditMode || !CanEditActiveEditorValues() ||
            !_editorDetailGrid.IsCurrentCellInEditMode ||
            _editorDetailGrid.CurrentCell is not { } cell ||
            !string.Equals(cell.OwningColumn?.Name, "Value", StringComparison.Ordinal))
        {
            return false;
        }

        _pendingEditSnapshot ??= CaptureCurrentEditorSnapshot();
        if (_editorDetailGrid.EditingControl is TextBox textBox)
            cell.Value = textBox.Text;

        _editorDetailGrid.EndEdit();
        CommitEditorDetailChange();
        return true;
    }

    // DataGridView's hosted TextBox consumes Enter before the form-level
    // shortcut handler sees it. Commit here so native BPHCL bone names use
    // the exact same Enter behavior as numeric fields.
    private void EditorDetailTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || !_editorDetailGrid.IsCurrentCellInEditMode)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        _editorDetailGrid.EndEdit();
    }

    // The hosted TextBox does not exist until after CellMouseDown has fired.
    // Restore the click's intended caret position once the control is ready,
    // so users can edit a value with one click rather than keyboard arrows.
    private void BeginEditorDetailEditAt(int columnIndex, int rowIndex, Point screenClickLocation)
    {
        if (_editorDetailGrid.IsDisposed || _committingEditorDetail ||
            !_editorDetailGrid.BeginEdit(false) || _editorDetailGrid.EditingControl is not TextBox textBox)
        {
            return;
        }

        // The hosted control receives its final bounds on the next UI turn.
        // Measuring it immediately makes ClientSize.Width zero in some grids,
        // which maps every click to the first character.
        textBox.BeginInvoke(new Action(() =>
        {
            if (_editorDetailGrid.IsDisposed || textBox.IsDisposed ||
                _editorDetailGrid.EditingControl != textBox)
            {
                return;
            }

            var pointInTextBox = textBox.PointToClient(screenClickLocation);
            var localPoint = new Point(
                Math.Clamp(pointInTextBox.X, 0, Math.Max(0, textBox.ClientSize.Width - 1)),
                Math.Clamp(pointInTextBox.Y, 0, Math.Max(0, textBox.ClientSize.Height - 1)));
            textBox.Focus();
            var characterIndex = textBox.GetCharIndexFromPosition(localPoint);
            if (characterIndex < textBox.TextLength)
            {
                var characterPosition = textBox.GetPositionFromCharIndex(characterIndex);
                var characterWidth = TextRenderer.MeasureText(
                    textBox.Text[characterIndex].ToString(),
                    textBox.Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                if (localPoint.X >= characterPosition.X + characterWidth / 2)
                    characterIndex++;
            }

            textBox.SelectionStart = characterIndex;
            textBox.SelectionLength = 0;
        }));
    }

    private bool CommitActiveHelperBoneDetail()
    {
        if (!_current.IsBphhb || !_helperBoneDetailGrid.IsCurrentCellInEditMode ||
            _helperBoneDetailGrid.CurrentCell is not { } cell ||
            !string.Equals(cell.OwningColumn?.Name, "Value", StringComparison.Ordinal) ||
            IsHelperBoneFieldReadOnly(cell.RowIndex))
        {
            return false;
        }

        _pendingHelperBoneSnapshot ??= CaptureFullEditorSnapshot(EditorPage.Bones, 0, _helperBoneList.SelectedIndex);
        if (_helperBoneDetailGrid.EditingControl is TextBox textBox)
            cell.Value = textBox.Text;

        _helperBoneDetailGrid.EndEdit();
        CommitHelperBoneDetailChange();
        return true;
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
        {
            var selected = GetSelectedBone();
            if (selected != null)
                _current.UpdateBoneRows(_clothList.SelectedIndex, new[] { selected });
        }
        else if (_editorPage == EditorPage.Colliders)
            _current.UpdateColliderRows(GetActiveColliderRowsForWrite());
        else
            _current.UpdateParticleRows(_clothList.SelectedIndex, _particleRows);
    }

    private void SynchronizeParticleConstraintGeometry(IReadOnlyList<ParticleEditRow>? previousRows)
    {
        if (previousRows == null || _current.IsReadOnlyExternal || _clothList.SelectedIndex < 0)
            return;

        _current.SynchronizeParticleConstraintGeometry(
            _clothList.SelectedIndex,
            previousRows,
            _particleRows);
    }

    private bool CanEditActiveEditorValues() =>
        !_current.IsReadOnlyExternal || _current.IsBphcl;

    private IReadOnlyList<ColliderEditRow> GetActiveColliderRowsForWrite()
    {
        var selected = GetSelectedCollider();
        if (selected == null)
            return _colliderRows;

        var rows = new List<ColliderEditRow> { selected };
        if (_mirrorPairs.TryGetValue(selected.Index, out var partnerIndex))
        {
            var partner = _colliderRows.FirstOrDefault(row => row.Index == partnerIndex);
            if (partner != null)
                rows.Add(partner);
        }

        return rows;
    }

    private void RefreshPreview(bool resetCamera)
    {
        if (_loadingCurrentDocument || !_current.HasDocument || _clothList.SelectedIndex < 0)
            return;

        var camera = resetCamera ? null : _particlePreview.CaptureCameraState();
        _particlePreview.SetData(_current.GetParticlePreview(_clothList.SelectedIndex), resetCamera);
        if (camera != null)
            _particlePreview.RestoreCameraState(camera);
        RefreshSelectedEditorItem();
    }

    // A DataGridView cannot have its rows rebuilt while it is finishing an
    // edit. Queue the viewport refresh after that WinForms operation ends.
    private void QueuePreviewRefresh()
    {
        if (_previewRefreshQueued || IsDisposed || Disposing)
            return;

        _previewRefreshQueued = true;
        BeginInvoke(new Action(() =>
        {
            _previewRefreshQueued = false;
            if (!IsDisposed && !Disposing && !_loadingCurrentDocument)
                RefreshPreview(resetCamera: false);
        }));
    }

    private void UpdatePreviewAfterCommittedEditorEdit()
    {
        switch (_editorPage)
        {
            case EditorPage.Bones:
                _particlePreview.UpdateBonePreviewRows(_boneRows);
                if (_editorIndexList.SelectedIndex >= 0 && _editorIndexList.SelectedIndex < _boneRows.Count)
                {
                    var bone = _boneRows[_editorIndexList.SelectedIndex];
                    _editorIndexList.Items[_editorIndexList.SelectedIndex] = $"{bone.Index}: {bone.Name}";
                }
                break;
            case EditorPage.Colliders:
                _particlePreview.UpdateColliderPreviewRows(_colliderRows);
                break;
            default:
                _particlePreview.UpdateParticlePreviewRows(_particleRows);
                break;
        }
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

    private void ScaleCurrentClothParticleMass()
    {
        if (!_directEditMode || !_current.CanEditParticleValues || _editorPage != EditorPage.Particles ||
            _clothList.SelectedIndex < 0 || !_particleRows.Any(row => !row.Fixed))
        {
            return;
        }

        var useSelection = _selectedParticleIndices.Count > 0;
        var targets = _particleRows
            .Where(row => !row.Fixed && (!useSelection || _selectedParticleIndices.Contains(row.Index)))
            .ToArray();
        if (targets.Length == 0)
        {
            _statusLabel.Text = "Select at least one dynamic particle before scaling its mass.";
            return;
        }

        if (!ParticleMassScaleDialog.TryGetScale(this, targets.Length, useSelection, out var scale))
            return;

        RunGuarded(() =>
        {
            var selectedIndex = _editorIndexList.SelectedIndex;
            var snapshot = CaptureEditorSnapshot(EditorPage.Particles, _clothList.SelectedIndex, selectedIndex);
            foreach (var particle in targets)
            {
                particle.Mass *= scale;
                if (!float.IsFinite(particle.Mass) || particle.Mass <= float.Epsilon)
                    throw new InvalidOperationException("The selected mass scale produces an invalid particle mass.");

                particle.InverseMass = 1.0f / particle.Mass;
            }

            _current.UpdateParticleRows(_clothList.SelectedIndex, _particleRows);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: selectedIndex);
            _statusLabel.Text = useSelection
                ? $"Scaled mass for {targets.Length} selected dynamic particle(s) by {scale:G7}."
                : $"Scaled all {targets.Length} dynamic particle masses by {scale:G7}.";
        });
    }

    private void EditCurrentClothSimulationSettings()
    {
        if (!_directEditMode || !_current.HasDocument || _current.IsBphcl || _current.IsReadOnlyExternal ||
            _clothList.SelectedIndex < 0)
        {
            return;
        }

        var clothIndex = _clothList.SelectedIndex;
        var settings = _current.GetSimulationSettings(clothIndex);
        if (!ClothSimulationSettingsDialog.TryEdit(this, settings, out var edited))
            return;

        RunGuarded(() =>
        {
            var snapshot = CaptureFullEditorSnapshot(_editorPage, clothIndex, _editorIndexList.SelectedIndex);
            _current.UpdateSimulationSettings(clothIndex, edited);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshParticleGrid(resetCamera: false, selectedListIndex: _editorIndexList.SelectedIndex);
            if (_simulationTimer.Enabled)
            {
                _simulation = new HkclPreviewSimulator(_current.GetParticlePreview(clothIndex));
                ApplySimulationOptions(_simulation);
            }
            _statusLabel.Text = "Updated cloth simulation settings.";
        });
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
        var camera = _particlePreview.CaptureCameraState();
        var activeClothIndex = _clothList.SelectedIndex;
        var activeEditorIndex = _editorIndexList.SelectedIndex;
        var restoreSnapshotSelection = activeClothIndex == snapshot.ClothIndex;
        _applyingSnapshot = true;
        try
        {
            if (snapshot.RawState != null)
            {
                _current.RestoreState(snapshot.RawState);
                RefreshCurrentLists(activeClothIndex);
                _editorPage = snapshot.Page;
                _editorTabs.SelectedIndex = snapshot.Page switch
                {
                    EditorPage.Bones => 1,
                    EditorPage.Colliders => 2,
                    _ => 0
                };
                MoveEditorContentToSelectedTab();
                RefreshParticleGrid(
                    resetCamera: false,
                    selectedListIndex: restoreSnapshotSelection ? snapshot.SelectedIndex : activeEditorIndex);
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
            RefreshParticleGrid(
                resetCamera: false,
                selectedListIndex: restoreSnapshotSelection ? snapshot.SelectedIndex : activeEditorIndex);
        }
        finally
        {
            _particlePreview.RestoreCameraState(camera);
            _applyingSnapshot = false;
        }
    }

    private void ClearUndoHistory()
    {
        _pendingEditSnapshot = null;
        _viewportMoveSnapshot = null;
        _viewportTransformChanged = false;
        _viewportWorldTranslation = System.Numerics.Vector3.Zero;
        _selectedParticleIndices.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void FillParticleDetailGrid(ParticleEditRow particle)
    {
        var bphcl = _current.IsBphcl;
        _editorDetailGrid.Rows.Clear();
        AddParticleDetail("Index", particle.Index.ToString(CultureInfo.InvariantCulture), true);
        AddParticleBoolDetail("Fixed", particle.Fixed, bphcl);
        AddParticleDetail("X", FormatFloat(particle.X), false);
        AddParticleDetail("Y", FormatFloat(particle.Y), false);
        AddParticleDetail("Z", FormatFloat(particle.Z), false);
        AddParticleDetail("Mass", FormatFloat(particle.Mass), false);
        AddParticleDetail("Inv Mass", FormatFloat(particle.InverseMass), bphcl);
        AddParticleDetail("Radius", FormatFloat(particle.Radius), false);
        AddParticleDetail("Friction", FormatFloat(particle.Friction), false);
        if (bphcl)
            AddParticleDetail("Collision mask", $"0x{particle.CollisionMask:X8}", true);
        else
            AddParticleColliderDetail(particle);
    }

    private void AddParticleDetail(string field, string value, bool readOnly)
    {
        var rowIndex = _editorDetailGrid.Rows.Add(field, value);
        _editorDetailGrid.Rows[rowIndex].Cells["Value"].ReadOnly = readOnly;
    }

    private void AddParticleBoolDetail(string field, bool value, bool readOnly = false)
    {
        var rowIndex = _editorDetailGrid.Rows.Add(field, value);
        _editorDetailGrid.Rows[rowIndex].Cells["Value"] = new DataGridViewCheckBoxCell { Value = value };
        _editorDetailGrid.Rows[rowIndex].Cells["Value"].ReadOnly = readOnly;
    }

    private void ApplySelectedParticleDetailsToCache()
    {
        if (_editorIndexList.SelectedIndex < 0 || _editorIndexList.SelectedIndex >= _particleRows.Count)
            return;

        var particle = _particleRows[_editorIndexList.SelectedIndex];
        if (!_current.IsBphcl)
            particle.Fixed = ReadDetailBool("Fixed");
        particle.X = ReadDetailFloat("X");
        particle.Y = ReadDetailFloat("Y");
        particle.Z = ReadDetailFloat("Z");
        particle.Mass = ReadDetailFloat("Mass");
        if (!_current.IsBphcl)
            particle.InverseMass = ReadDetailFloat("Inv Mass");
        particle.Radius = ReadDetailFloat("Radius");
        particle.Friction = ReadDetailFloat("Friction");
    }

    private void AddParticleColliderDetail(ParticleEditRow particle)
    {
        var options = _clothList.SelectedIndex < 0
            ? Array.Empty<ParticleColliderOption>()
            : _current.GetParticleColliderOptions(_clothList.SelectedIndex);
        var selectedCount = options.Count(option => (particle.CollisionMask & (1u << option.BitIndex)) != 0);
        var rowIndex = _editorDetailGrid.Rows.Add("Colliders", string.Empty);
        _editorDetailGrid.Rows[rowIndex].Cells["Value"] = new DataGridViewButtonCell
        {
            Value = options.Count == 0 ? "No cloth colliders" : $"{selectedCount} selected...",
            FlatStyle = FlatStyle.Flat,
            Style = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(64, 64, 64),
                ForeColor = Color.Gainsboro
            }
        };
    }

    private void EditSelectedParticleColliders()
    {
        if (_current.IsReadOnlyExternal || _editorPage != EditorPage.Particles ||
            !TryGetSelectedParticle(out var particle) || _clothList.SelectedIndex < 0)
        {
            return;
        }

        var options = _current.GetParticleColliderOptions(_clothList.SelectedIndex);
        if (options.Count == 0)
        {
            MessageBox.Show(this, "This cloth has no colliders available to its particle simulation.", "Particle colliders", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ParticleColliderDialog.TryChoose(this, $"particle {particle.Index}", options, particle.CollisionMask, out var selectedColliderBits))
            return;

        uint availableColliderBits = 0;
        foreach (var option in options)
            availableColliderBits |= 1u << option.BitIndex;

        var updatedMask = (particle.CollisionMask & ~availableColliderBits) | selectedColliderBits;
        if (updatedMask == particle.CollisionMask)
            return;

        RunGuarded(() =>
        {
            var snapshot = CaptureEditorSnapshot(EditorPage.Particles, _clothList.SelectedIndex, _editorIndexList.SelectedIndex);
            particle.CollisionMask = updatedMask;
            _current.UpdateParticleRows(_clothList.SelectedIndex, _particleRows);
            PushUndo(snapshot);
            _redoStack.Clear();
            RefreshSelectedEditorItem();
            _statusLabel.Text = $"Updated colliders for particle {particle.Index}.";
        });
    }

    private void FillBoneDetailGrid(BoneEditRow bone)
    {
        _editorDetailGrid.Rows.Clear();
        AddParticleDetail("Index", bone.Index.ToString(CultureInfo.InvariantCulture), true);
        AddParticleDetail("Name", bone.Name, false);
        AddParticleDetail("Parent", bone.ParentIndex.ToString(CultureInfo.InvariantCulture), _current.IsBphcl);
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
        if (!_current.IsBphcl)
        {
            bone.ParentIndex = ReadDetailInt("Parent");
        }
        bone.X = ReadBonePosition("X");
        bone.Y = ReadBonePosition("Y");
        bone.Z = ReadBonePosition("Z");
        bone.RotationX = ReadDetailFloat("Rot X");
        bone.RotationY = ReadDetailFloat("Rot Y");
        bone.RotationZ = ReadDetailFloat("Rot Z");
    }

    private void FillColliderDetailGrid(ColliderEditRow collider)
    {
        _editorDetailGrid.Rows.Clear();
        AddParticleDetail("Index", collider.Index.ToString(CultureInfo.InvariantCulture), true);
        AddParticleDetail("Shape", collider.ShapeType, true);
        AddParticleDetail("Name", collider.Name, false);
        AddColliderBoneDetail(collider);
        if (collider.IsPlane)
        {
            AddParticleDetail("Position X", FormatFloat(collider.StartX), false);
            AddParticleDetail("Position Y", FormatFloat(collider.StartY), false);
            AddParticleDetail("Position Z", FormatFloat(collider.StartZ), false);
            AddParticleDetail("Normal X", FormatFloat(collider.PlaneNormalX), true);
            AddParticleDetail("Normal Y", FormatFloat(collider.PlaneNormalY), true);
            AddParticleDetail("Normal Z", FormatFloat(collider.PlaneNormalZ), true);
            return;
        }
        // The viewport uses world space. Shape endpoints are stored in the
        // collider's own space, so expose those authored values here instead
        // of forcing a position edit to bake a fresh world-space shape.
        var localStart = InverseTransformColliderPoint(
            collider.Transform,
            new System.Numerics.Vector3(collider.StartX, collider.StartY, collider.StartZ));
        var localEnd = InverseTransformColliderPoint(
            collider.Transform,
            new System.Numerics.Vector3(collider.EndX, collider.EndY, collider.EndZ));
        AddParticleDetail("Start X (local)", FormatFloat(localStart.X), false);
        AddParticleDetail("Start Y (local)", FormatFloat(localStart.Y), false);
        AddParticleDetail("Start Z (local)", FormatFloat(localStart.Z), false);
        AddParticleDetail("End X (local)", FormatFloat(localEnd.X), false);
        AddParticleDetail("End Y (local)", FormatFloat(localEnd.Y), false);
        AddParticleDetail("End Z (local)", FormatFloat(localEnd.Z), false);
        AddParticleDetail("Radius", FormatFloat(collider.Radius), false);
    }

    private void ApplySelectedColliderDetailsToCache()
    {
        if (_editorIndexList.SelectedIndex < 0 || _editorIndexList.SelectedIndex >= _colliderRows.Count)
            return;

        var collider = _colliderRows[_editorIndexList.SelectedIndex];
        collider.Name = ReadDetailText("Name");
        if (!_current.IsBphcl)
        {
            collider.BoneIndex = ReadDetailBoneIndex("Bone");
            collider.BoneName = _boneRows.FirstOrDefault(bone => bone.Index == collider.BoneIndex)?.Name ?? string.Empty;
        }
        if (collider.IsPlane)
        {
            var target = new System.Numerics.Vector3(
                ReadDetailFloat("Position X"),
                ReadDetailFloat("Position Y"),
                ReadDetailFloat("Position Z"));
            var current = new System.Numerics.Vector3(collider.StartX, collider.StartY, collider.StartZ);
            TranslateCollider(collider, target - current);
            return;
        }
        var localStart = new System.Numerics.Vector3(
            ReadDetailFloat("Start X (local)"),
            ReadDetailFloat("Start Y (local)"),
            ReadDetailFloat("Start Z (local)"));
        var localEnd = new System.Numerics.Vector3(
            ReadDetailFloat("End X (local)"),
            ReadDetailFloat("End Y (local)"),
            ReadDetailFloat("End Z (local)"));
        var start = TransformColliderPoint(collider.Transform, localStart);
        var end = TransformColliderPoint(collider.Transform, localEnd);
        collider.StartX = start.X;
        collider.StartY = start.Y;
        collider.StartZ = start.Z;
        collider.EndX = end.X;
        collider.EndY = end.Y;
        collider.EndZ = end.Z;
        collider.Radius = ReadDetailFloat("Radius");
    }

    private void AddColliderBoneDetail(ColliderEditRow collider)
    {
        if (_current.IsBphcl)
        {
            AddParticleDetail("Bone", FormatBoneOption(collider.BoneIndex, collider.BoneName), true);
            return;
        }

        var rowIndex = _editorDetailGrid.Rows.Add("Bone", string.Empty);
        var combo = new DataGridViewComboBoxCell
        {
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            DisplayStyleForCurrentCellOnly = false
        };

        foreach (var bone in _boneRows)
            combo.Items.Add(FormatBoneOption(bone.Index, bone.Name));

        var selected = FormatBoneOption(collider.BoneIndex, collider.BoneName);
        if (!combo.Items.Contains(selected))
            combo.Items.Insert(0, selected);
        combo.Value = selected;
        _editorDetailGrid.Rows[rowIndex].Cells["Value"] = combo;
    }

    private static string FormatBoneOption(int index, string name) =>
        index < 0 ? "None" : $"{index}: {name}";

    private string ReadDetailText(string field)
    {
        foreach (DataGridViewRow row in _editorDetailGrid.Rows)
        {
            if (string.Equals(Convert.ToString(row.Cells["Field"].Value), field, StringComparison.OrdinalIgnoreCase))
                return Convert.ToString(row.Cells["Value"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return string.Empty;
    }

    private string GetDetailField(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _editorDetailGrid.Rows.Count)
            return string.Empty;

        return Convert.ToString(_editorDetailGrid.Rows[rowIndex].Cells["Field"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private float ReadDetailFloat(string field)
    {
        var text = ReadDetailText(field);
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException($"Please enter a value for {field}.");
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
            throw new FormatException($"Please enter a valid number for {field}.");

        return value;
    }

    private float ReadBonePosition(string field)
    {
        var value = ReadDetailFloat(field);
        if (value < -30.0f || value > 30.0f)
            throw new FormatException($"Bone {field} must be between -30 and 30.");

        return value;
    }

    private int ReadDetailInt(string field)
    {
        var text = ReadDetailText(field);
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException($"Please enter a value for {field}.");
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Please enter a whole number for {field}.");

        return value;
    }

    private uint ReadDetailUInt(string field)
    {
        var text = ReadDetailText(field);
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException($"Please enter a value for {field}.");
        if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Please enter a value from 0 to {uint.MaxValue} for {field}.");

        return value;
    }

    private int ReadDetailBoneIndex(string field)
    {
        var text = ReadDetailText(field);
        if (string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
            return -1;

        var separator = text.IndexOf(':');
        var indexText = separator >= 0 ? text[..separator] : text;
        if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new FormatException("Please choose a valid bone.");

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
            ClothIndex = row.ClothIndex,
            Index = row.Index,
            Name = row.Name,
            ShapeType = row.ShapeType,
            BoneIndex = row.BoneIndex,
            BoneName = row.BoneName,
            StartX = row.StartX,
            StartY = row.StartY,
            StartZ = row.StartZ,
            EndX = row.EndX,
            EndY = row.EndY,
            EndZ = row.EndZ,
            Radius = row.Radius,
            PlaneNormalX = row.PlaneNormalX,
            PlaneNormalY = row.PlaneNormalY,
            PlaneNormalZ = row.PlaneNormalZ,
            Transform = row.Transform
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
            Title = _current.IsBphhb ? "Export BPHHB JSON summary" : _current.IsBphcl ? "Export BPHCL JSON summary" : "Export HKCL JSON",
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

    private void ExportBphysics()
    {
        if (!_current.HasDocument || _current.IsReadOnlyExternal)
        {
            MessageBox.Show(
                this,
                "Open an HKCL file before exporting its BotW BPHYSICS runtime sidecar.",
                "BPHYSICS export",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var hkclFileName = Path.GetFileNameWithoutExtension(_current.SuggestFileName(".hkcl"));
        var suggestedHkclPath = BuildSuggestedBphysicsHkclPath(hkclFileName);
        if (!BphysicsExportDialog.TryConfigure(this, suggestedHkclPath, out var hkclPath, out var supportBonePath))
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "Export BotW BPHYSICS sidecar",
            Filter = "BotW BPHYSICS|*.bphysics|All files|*.*",
            FileName = Path.GetFileNameWithoutExtension(_current.SuggestFileName(".hkcl")) + ".bphysics"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        RunGuarded(() =>
        {
            var sidecar = _current.CreateBphysicsDocument(hkclPath);
            sidecar.UseSupportBone = supportBonePath != null;
            sidecar.SupportBonePath = supportBonePath;
            BphysicsService.Save(dialog.FileName, sidecar);
            _statusLabel.Text = $"Exported BPHYSICS sidecar: {Path.GetFileName(dialog.FileName)}";
            PlaySaveSound();
        });
    }

    private static string BuildSuggestedBphysicsHkclPath(string hkclFileName)
    {
        var firstSeparator = hkclFileName.IndexOf('_');
        var secondSeparator = firstSeparator < 0 ? -1 : hkclFileName.IndexOf('_', firstSeparator + 1);
        var folder = secondSeparator > 0 ? hkclFileName[..secondSeparator] : hkclFileName;
        return $"{folder}/{hkclFileName}.hkcl";
    }

    private void ExportFreshHkclFromBphcl()
    {
        if (!_current.IsBphcl || _clothList.SelectedIndex < 0)
        {
            MessageBox.Show(
                this,
                "Open a BPHCL file and select one cloth first.",
                "Fresh HKCL export",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var selectedName = _current.GetClothName(_clothList.SelectedIndex);
        var warning =
            "This creates a brand-new HKCL document from the selected BPHCL cloth.\n\n" +
            "It does not clone an HKCL template. It rebuilds the cloth's skeleton, buffers, " +
            "operators, state access, and colliders from BPHCL data. This is still experimental, " +
            "but this output is intended for focused in-game testing.\n\n" +
            $"Create a fresh HKCL for {selectedName}?";
        if (MessageBox.Show(this, warning, "Experimental fresh HKCL export", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "Export fresh HKCL structural test",
            Filter = "Wii U HKCL|*.hkcl|All files|*.*",
            FileName = MakeSafeFileName(selectedName) + ".hkcl"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        RunGuarded(() =>
        {
            var fresh = _current.CreateFreshHkclFromCurrentBphcl(_clothList.SelectedIndex);
            fresh.SaveHkcl(dialog.FileName, HkclPlatform.WiiU);

            // Verify the bytes by returning through the normal HKCL reader.
            var verification = new HkclService();
            verification.Load(dialog.FileName);
            if (!verification.HasDocument || verification.IsReadOnlyExternal)
                throw new InvalidOperationException("The fresh HKCL file was written but could not be reopened by PhysicsTool.");

            _statusLabel.Text = $"Exported and reopened fresh HKCL: {Path.GetFileName(dialog.FileName)}";
            PlaySaveSound();
        });
    }

    private void ExportFreshHkclDocumentFromBphcl()
    {
        if (!_current.IsBphcl || !_current.HasDocument)
        {
            MessageBox.Show(
                this,
                "Open a BPHCL file first.",
                "HKCL conversion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var scaleSuggestions = _current.GetBphclConversionScaleSuggestions();
        if (!BphclConversionScaleDialog.TryGetScales(this, scaleSuggestions, out var solverScales))
            return;

        var warning =
            "This creates one brand-new HKCL document containing every BPHCL cloth unit.\n\n" +
            "Colliders are shared in the outer container, while each cloth receives its own skeleton, " +
            "particle simulation, constraints, states, and ordered collider references. This is the first " +
            "full-file standalone conversion pass, so keep the original files untouched for comparison.\n\n" +
            "Create the converted HKCL?";
        if (MessageBox.Show(this, warning, "Experimental full HKCL export", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        using var dialog = new SaveFileDialog
        {
            Title = "Export converted HKCL",
            Filter = "Wii U HKCL|*.hkcl|All files|*.*",
            FileName = Path.GetFileNameWithoutExtension(_current.SuggestFileName(".bphcl")) + ".hkcl"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        RunGuarded(() =>
        {
            var fresh = _current.CreateFreshHkclFromCurrentBphclDocument(solverScales);
            fresh.SaveHkcl(dialog.FileName, HkclPlatform.WiiU);

            var verification = new HkclService();
            verification.Load(dialog.FileName);
            if (!verification.HasDocument || verification.IsReadOnlyExternal)
                throw new InvalidOperationException("The full fresh HKCL was written but could not be reopened by PhysicsTool.");
            var expectedClothCount = _current.GetClothSummaries().Count;
            var actualClothCount = verification.GetClothSummaries().Count;
            if (actualClothCount != expectedClothCount)
            {
                throw new InvalidOperationException(
                    $"The full fresh HKCL reopened with {actualClothCount} cloths; expected {expectedClothCount}.");
            }

            _statusLabel.Text = $"Exported and reopened converted HKCL: {Path.GetFileName(dialog.FileName)}";
            PlaySaveSound();
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
            PlaySaveSound();
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
            PlaySaveSound();
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
            FileName = _current.SuggestFileName(_current.CurrentExtension),
            FilterIndex = !_current.IsReadOnlyExternal && _currentSavePlatform == HkclPlatform.Switch ? 2 : 1
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
            PlaySaveSound();
        });
    }

    private static void PlaySaveSound() => System.Media.SystemSounds.Asterisk.Play();

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

    private void DuplicateSelectedCloth()
    {
        if (!_current.HasDocument || _current.IsBphhb || _clothList.SelectedIndex < 0 || _directEditMode)
            return;

        var sourceName = GetSelectedClothName();
        RunGuarded(() =>
        {
            var duplicateIndex = _current.DuplicateCloth(_clothList.SelectedIndex);
            ClearUndoHistory();
            RefreshCurrentLists(duplicateIndex);
            _statusLabel.Text = $"Duplicated {sourceName} as {_current.GetClothName(duplicateIndex)}.";
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

        if (isBphclToHkcl)
        {
            var preflight = _current.GetBphclToHkclConversionPreflight(
                _reference,
                _referenceClothList.SelectedIndex,
                _clothList.SelectedIndex);
            if (!preflight.IsEligible)
            {
                MessageBox.Show(
                    this,
                    preflight.ToDisplayText(),
                    "BPHCL -> HKCL conversion blocked",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(
                    this,
                    preflight.ToDisplayText() + "\n\nConvert now?",
                    "BPHCL -> HKCL conversion",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) != DialogResult.Yes)
            {
                return;
            }
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

        var helperInspector = _current.IsBphhb;
        if (_physicsEditorPanel != null)
            _physicsEditorPanel.Visible = !helperInspector;
        if (_helperBoneEditorPanel != null)
            _helperBoneEditorPanel.Visible = helperInspector;
        if (_editorActionPanel != null)
            _editorActionPanel.Visible = !helperInspector;
        if (_directEditorLayout != null && _directEditorLayout.RowStyles.Count > 1)
            _directEditorLayout.RowStyles[1].Height = helperInspector ? 0 : 48;
        if (_editorMainSplit != null)
            _editorMainSplit.Panel1Collapsed = helperInspector;
        if (_editorSideSplit != null)
            _editorSideSplit.Panel2Collapsed = helperInspector;
        if (_directEditGroup != null)
            _directEditGroup.Text = helperInspector ? "Helper-bone inspection" : "Physics direct editing";
        if (_editorValueGroup != null)
            _editorValueGroup.Text = helperInspector ? "Helper bone values" : "Editor values";
        if (_editorTabs.TabPages.Count >= 3)
        {
            _editorTabs.TabPages[0].Enabled = !helperInspector;
            _editorTabs.TabPages[2].Enabled = !helperInspector;
        }
        if (helperInspector && direct)
            SelectHelperBonePage();

        UpdateAddButtonText();
        _directEditButton.Text = "Editor";
        _directEditButton.Visible = !helperInspector;
        _directEditButton.UseVisualStyleBackColor = false;
        _directEditButton.BackColor = direct ? Color.FromArgb(85, 105, 130) : Color.FromArgb(64, 64, 64);
        _directEditButton.ForeColor = Color.Gainsboro;
        _directEditButton.Font = direct
            ? new Font(Font, FontStyle.Bold)
            : new Font(Font, FontStyle.Regular);
        _statusLabel.Text = direct
            ? _current.IsBphhb
                ? "Helper-bone editor: edit graph base poses, clone helper bones, or mirror the selected helper bone across X. This file does not contain the full actor skeleton."
                : _current.IsBphcl
                    ? "BPHCL editor: particle values, bone poses, and existing collider transforms are native-editable. Structural changes remain inspection-only."
                    : "Direct edit mode: edit physics values, then save."
            : _current.IsBphhb
                ? "BPHHB mode: open Editor to inspect the helper-bone subset and its base transforms."
                : _current.IsBphcl
                    ? "BPHCL mode: open/save and merge use the native BPHCL serializer."
                    : "Merge mode: open a reference physics file to copy/remove cloth entries.";
    }

    private void UpdateButtons()
    {
        var hasCurrent = _current.HasDocument;
        var direct = _directEditMode;
        var readOnlyExternal = _current.IsReadOnlyExternal;
        var nativeViewportEditable = !readOnlyExternal || _current.IsBphcl;
        SetButtonEnabled(_directEditButton, hasCurrent);
        SetButtonEnabled(_openReferenceButton, !direct);
        SetButtonEnabled(_swapFilesButton, !direct && hasCurrent && _reference.HasDocument);
        SetButtonEnabled(_exportJsonButton, hasCurrent);
        SetButtonEnabled(_saveWiiUButton, hasCurrent);
        SetButtonEnabled(_convertButton, hasCurrent && _current.IsBphcl);
        SetButtonEnabled(_removeButton, hasCurrent && !_current.IsBphhb && _clothList.SelectedIndex >= 0);
        SetButtonEnabled(_duplicateButton, !direct && hasCurrent && !_current.IsBphhb && _clothList.SelectedIndex >= 0);
        var supportsMerge = !_current.IsBphhb && !_reference.IsBphhb && (_current.IsBphcl == _reference.IsBphcl ||
            (!_current.IsBphcl && _reference.IsBphcl && _clothList.SelectedIndex >= 0));
        SetButtonEnabled(_mergeButton, !direct && hasCurrent && _reference.HasDocument && _referenceClothList.SelectedIndex >= 0 && supportsMerge);
        var canEditParticleValues = _current.CanEditParticleValues && _editorPage == EditorPage.Particles;
        SetButtonEnabled(_particleApplyButton, direct && hasCurrent && canEditParticleValues && _undoStack.Count > 0);
        SetButtonEnabled(_particleRefreshButton, direct && hasCurrent && canEditParticleValues && _redoStack.Count > 0);
        SetButtonEnabled(_particleMassScaleButton,
            direct && hasCurrent && canEditParticleValues &&
            _clothList.SelectedIndex >= 0 && _particleRows.Any(row => !row.Fixed));
        SetButtonEnabled(_clothSettingsButton,
            direct && hasCurrent && !_current.IsBphcl && !readOnlyExternal && _clothList.SelectedIndex >= 0);
        SetButtonEnabled(_mirrorClothButton,
            direct && hasCurrent && !_current.IsBphhb && nativeViewportEditable && _clothList.SelectedIndex >= 0);
        SetButtonEnabled(_helperAddBoneButton, direct && _current.IsBphhb && hasCurrent && _boneRows.Count > 0);
        SetButtonEnabled(_helperDuplicateBoneButton, direct && _current.IsBphhb && hasCurrent && _helperBoneList.SelectedIndex >= 0);
        SetButtonEnabled(_helperMirrorXButton, direct && _current.IsBphhb && hasCurrent && _boneRows.Count > 0);
        SetButtonEnabled(_helperMoveUpButton, direct && _current.IsBphhb && hasCurrent && _helperBoneList.SelectedIndex > 0);
        SetButtonEnabled(_helperMoveDownButton, direct && _current.IsBphhb && hasCurrent &&
            _helperBoneList.SelectedIndex >= 0 && _helperBoneList.SelectedIndex < _boneRows.Count - 1);
        SetButtonEnabled(_helperUndoButton, direct && _current.IsBphhb && _undoStack.Count > 0);
        SetButtonEnabled(_helperRedoButton, direct && _current.IsBphhb && _redoStack.Count > 0);
        SetButtonEnabled(_addEditorItemButton, direct && hasCurrent && !readOnlyExternal && _clothList.SelectedIndex >= 0);
        SetButtonEnabled(_mirrorModeButton, direct && hasCurrent && nativeViewportEditable && _clothList.SelectedIndex >= 0);
        var simulationAvailable = direct && hasCurrent && !readOnlyExternal && _clothList.SelectedIndex >= 0 && _particleRows.Count > 0;
        if (!simulationAvailable && _simulationTimer.Enabled)
        {
            _simulationTimer.Stop();
            _simulation = null;
            _simulationButton.Text = "Run Simulation";
            SetSimulationButtonLock(false);
        }
        SetButtonEnabled(_simulationButton, simulationAvailable);
        SetButtonEnabled(_windSimulationButton, simulationAvailable);
        SetButtonEnabled(_simulationOptionsButton, simulationAvailable);
        UpdateWindSimulationButton();
        if (_mirrorModeButton.Enabled)
            UpdateMirrorModeButton();
    }

    private static void SetButtonEnabled(Button button, bool enabled)
    {
        button.Enabled = enabled;
        button.ForeColor = enabled ? Color.Gainsboro : Color.FromArgb(130, 130, 130);
        button.BackColor = enabled ? Color.FromArgb(64, 64, 64) : Color.FromArgb(58, 58, 58);
    }

    private static string MakeSafeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(name.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray())
            .Trim()
            .TrimEnd('.');

        return string.IsNullOrWhiteSpace(safeName) ? "Physics" : safeName;
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
        if (keyData == Keys.Space && _particlePreview.Focused && CanRunSimulation())
        {
            ToggleSimulation();
            return true;
        }

        if (keyData == Keys.Enter && CommitActiveEditorDetail())
            return true;

        if (keyData == Keys.Enter && CommitActiveHelperBoneDetail())
            return true;

        // Avoid the last-used toolbar button consuming Enter after a file dialog.
        // A loaded file with a selected cloth always treats Enter as "open in Editor".
        if (keyData == Keys.Enter && !_simulationTimer.Enabled && _current.HasDocument &&
            _clothList.SelectedIndex >= 0)
        {
            OpenSelectedClothInEditor();
            return true;
        }

        if (keyData == (Keys.Control | Keys.C) && !_editorDetailGrid.IsCurrentCellInEditMode)
        {
            CopySelectedEditorItem();
            return true;
        }

        if (keyData == (Keys.Control | Keys.V) && !_editorDetailGrid.IsCurrentCellInEditMode)
        {
            PasteEditorItem();
            return true;
        }

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
            if (_editorDetailGrid.IsCurrentCellInEditMode)
                return base.ProcessCmdKey(ref msg, keyData);

            if (_directEditMode)
                DeleteSelectedEditorItem();
            else
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



