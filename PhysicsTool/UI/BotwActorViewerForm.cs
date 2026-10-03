using System.Diagnostics;
using System.Drawing;

namespace HKCLTool;

internal sealed class BotwActorViewerForm : LocalizedForm
{
    private readonly ParticlePreviewControl _preview = new() { Dock = DockStyle.Fill, ReadOnlyPreview = true, BoneOverlayVisible = false, ReflectSceneX = false };
    private readonly Button _open = Button("Open actor");
    private readonly Button _folders = Button("Resource folders");
    private readonly Button _info = Button("Resources");
    private readonly Button _meshes = Button("Meshes");
    private readonly Button _play = Icon("\uE768", "Play");
    private readonly Button _reset = Icon("\uE72C", "Reset pose");
    private readonly CheckBox _simulate = new() { Text = "Run simulation", Appearance = Appearance.Button, AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
    private readonly CheckBox _hide = new() { Text = "Hide physics", Appearance = Appearance.Button, AutoSize = true, Padding = new Padding(8, 3, 8, 3) };
    private readonly CheckBox _bones = Check("Bones");
    private readonly CheckBox _wind = Check("Wind");
    private readonly NumericUpDown _windStrength = new() { Width = 64, Minimum = 0, Maximum = 50, DecimalPlaces = 1,
        Increment = 1, Value = (decimal)HkclPreviewSimulator.DefaultWindSpeed, Anchor = AnchorStyles.Left };
    private readonly CheckBox _loop = Check("Loop", true);
    private readonly ComboBox _animations = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoCompleteSource = AutoCompleteSource.ListItems,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend, Width = 340, IntegralHeight = false, DropDownHeight = 400 };
    private readonly AnimationTimelineControl _timeline = new() { Dock = DockStyle.Fill, Height = 52, MinimumSize = new Size(120, 52), FrameScale = 0.1 };
    private readonly NumericUpDown _frameInput = new() { Width = 82, DecimalPlaces = 1, Increment = 1, Minimum = 0, Maximum = 1 };
    private readonly Label _frameCount = new() { AutoSize = true, Text = "/ 0", Anchor = AnchorStyles.Left, Margin = new Padding(4, 7, 8, 4) };
    private readonly Label _status = new() { AutoSize = true, Text = "Open an actor pack", Padding = new Padding(8, 5, 8, 5), Dock = DockStyle.Fill };
    private readonly ToolTip _toolTips = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<string> _resourceFolders = new();
    private readonly BotwActorCatalog _catalog = new();
    private readonly CancellationTokenSource _closing = new();
    private BotwActorScene? _scene;
    private BotwActorMeshesForm? _meshesWindow;
    private string? _path;
    private string? _actorEntry;
    private bool _busy, _playing, _updating;
    private double _frame, _lastTime;
    private double _simulationAccumulator;
    private ToolboxActorAnimation? _selectedAnimation, _hoverAnimation;
    private bool _animationBrowsing, _animationClosing;
    private int _dropdownGeneration;
    private ToolboxActorAnimation? Animation => _selectedAnimation;
    private ToolboxActorAnimation? DisplayedAnimation => _hoverAnimation ?? Animation;

    internal BotwActorScene? Scene => _scene;
    internal ParticlePreviewControl Preview => _preview;
    private IWin32Window DialogOwner => TopLevelControl ?? this;

    internal void SetViewActive(bool active)
    {
        _lastTime = _clock.Elapsed.TotalSeconds;
        _simulationAccumulator = 0;
        _timer.Enabled = active;
        if (!active)
        {
            SetPlaying(false);
            _animations.DroppedDown = false;
            _meshesWindow?.Close();
        }
    }

    public BotwActorViewerForm(bool promptOnOpen = true)
    {
        _resourceFolders.AddRange(AppLocalization.ActorResourceFolders);
        Text = "Actor viewer";
        BackColor = Color.FromArgb(48, 48, 48); ForeColor = Color.Gainsboro;
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(1150, 800); MinimumSize = new Size(720, 500);
        AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(5), Margin = Padding.Empty };
        var fit = Button("Frame model");
        toolbar.Controls.AddRange(new Control[] { _open, _folders, _info, _meshes, fit, _simulate, _hide, _bones, _wind, _windStrength });
        _windStrength.AccessibleName = AppLocalization.Translate("Wind strength");
        _toolTips.SetToolTip(_windStrength, AppLocalization.Translate("Wind strength"));
        var playback = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, RowCount = 2, ColumnCount = 1, Padding = new Padding(6, 0, 6, 0), Margin = Padding.Empty };
        playback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        playback.RowStyles.Add(new RowStyle(SizeType.AutoSize)); playback.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var transport = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 4) };
        transport.Controls.AddRange(new Control[] { _play, _reset, _animations, _frameInput, _frameCount, _loop });
        void FitTransport()
        {
            // A nested auto-sized flow panel otherwise measures at its minimum
            // width and reserves several wrapped rows even in a wide window.
            var width = Math.Max(1, playback.ClientSize.Width - playback.Padding.Horizontal);
            var height = transport.GetPreferredSize(new Size(width, 0)).Height;
            if (Math.Abs(playback.RowStyles[1].Height - height) > 0.5f) playback.RowStyles[1].Height = height;
        }
        playback.SizeChanged += (_, _) => FitTransport(); transport.Layout += (_, _) => FitTransport();
        playback.Controls.Add(_timeline, 0, 0); playback.Controls.Add(transport, 0, 1);
        layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(_preview, 0, 1); layout.Controls.Add(playback, 0, 2); layout.Controls.Add(_status, 0, 3);
        Controls.Add(layout);
        foreach (var toggle in new[] { _simulate, _hide })
        {
            toggle.AutoSize = false; toggle.FlatStyle = FlatStyle.Flat;
            void FitToggle() => toggle.Size = new Size(TextRenderer.MeasureText(toggle.Text, toggle.Font, Size.Empty, TextFormatFlags.SingleLine).Width + 30,
                Math.Max(36 * toggle.DeviceDpi / 96, toggle.Font.Height + toggle.Padding.Vertical + 10));
            toggle.TextChanged += (_, _) => FitToggle(); toggle.FontChanged += (_, _) => FitToggle(); toggle.DpiChangedAfterParent += (_, _) => FitToggle(); FitToggle();
        }
        _toolTips.SetToolTip(_play, AppLocalization.Translate("Play")); _toolTips.SetToolTip(_reset, AppLocalization.Translate("Reset pose"));
        _open.Click += async (_, _) => await OpenAsync();
        _folders.Click += async (_, _) => await AddResourceFolderAsync();
        _info.Click += (_, _) => ShowResources(); fit.Click += (_, _) => _preview.FitCameraToVisualModel();
        _meshes.Click += (_, _) => ShowMeshes();
        _hide.CheckedChanged += (_, _) => { _preview.PhysicsOverlayVisible = !_hide.Checked; _preview.Invalidate(); };
        _bones.CheckedChanged += (_, _) => { _preview.BoneOverlayVisible = _bones.Checked; _preview.Invalidate(); };
        _play.Click += (_, _) => { if (Animation == null) return; if (!_playing && _frame >= Animation.FrameCount) Seek(0); SetPlaying(!_playing); };
        _reset.Click += (_, _) => { SetPlaying(false); _simulate.Checked = false; _selectedAnimation = _hoverAnimation = null; _updating = true; _animations.SelectedIndex = -1; _updating = false; _frame = 0; Render(true); UpdateTransport(); };
        _simulate.CheckedChanged += (_, _) => { if (_busy) return; _hoverAnimation = null; _simulationAccumulator = 0; _lastTime = _clock.Elapsed.TotalSeconds; UpdateTransport(); Render(true); };
        _wind.CheckedChanged += (_, _) => { if (_scene != null) _scene.WindEnabled = _wind.Checked; };
        _windStrength.ValueChanged += (_, _) => { if (_scene != null) _scene.WindSpeed = (float)_windStrength.Value; };
        _animations.DropDown += (_, _) => { _animationBrowsing = true; _animationClosing = false; _dropdownGeneration++; };
        _animations.DropDownClosed += (_, _) => FinishAnimationBrowse();
        _animations.SelectionChangeCommitted += (_, _) => CommitAnimation(_animations.SelectedItem as ToolboxActorAnimation);
        _animations.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating && !_animationBrowsing && !_animationClosing) CommitAnimation(_animations.SelectedItem as ToolboxActorAnimation);
        };
        _timeline.Scroll += (_, _) => Seek(_timeline.Value / 10.0);
        _frameInput.ValueChanged += (_, _) => { if (!_updating) Seek((double)_frameInput.Value); };
        _timer.Tick += (_, _) => TickPlayback();
        Shown += async (_, _) => { if (promptOnOpen && _path == null) await OpenAsync(); };
        FormClosing += (_, _) => { _closing.Cancel(); _timer.Stop(); };
        _timer.Start(); UpdateTransport();
    }
    internal async Task LoadActorAsync(string path, string? actorEntry = null)
    {
        if (_busy) return;
        _busy = true; _path = path; _actorEntry = actorEntry; SetPlaying(false); _simulate.Checked = false; UpdateTransport();
        var progress = new Progress<string>(message => { if (!IsDisposed && !_closing.IsCancellationRequested) _status.Text = message; });
        try
        {
            var scene = await Task.Run(() => BotwActorScene.Load(path, _resourceFolders.ToArray(), progress, _closing.Token, actorEntry));
            if (_closing.IsCancellationRequested) { scene.Dispose(); return; }
            ShowScene(scene, path);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _status.Text = AppLocalization.Translate("Actor could not be loaded.");
            if (!_closing.IsCancellationRequested) MessageBox.Show(DialogOwner, UserFacingExceptionFormatter.Format(ex), AppLocalization.Translate("Actor viewer"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _busy = false; if (!IsDisposed) UpdateTransport(); }
    }
    internal void ShowScene(BotwActorScene scene, string path)
    {
        _meshesWindow?.Close();
        _selectedAnimation = _hoverAnimation = null; _animationBrowsing = _animationClosing = false; _dropdownGeneration++;
        var previous = _scene; _scene = scene; _path = path; _actorEntry = scene.Resources.SourceEntry; _frame = 0;
        scene.WindEnabled = _wind.Checked;
        scene.WindSpeed = (float)_windStrength.Value;
        _preview.SetData(scene.Overlay); _preview.SetVisualModel(scene.CurrentModel);
        // BFRES and Havok share native actor space. Neither receives the
        // legacy editor's X reflection; model and overlays remain aligned.
        _preview.SetVisualModelDisplaySettings(new VisualModelDisplaySettings());
        _preview.FitCameraToVisualModel(); previous?.Dispose();
        _updating = true; _animations.Items.Clear(); _animations.Items.AddRange(scene.Animations.ToArray()); _animations.SelectedIndex = -1; _updating = false;
        Text = $"{AppLocalization.Translate("Actor viewer")} - {scene.Resources.ActorName} ({(scene.Resources.IsSwitch ? "Switch" : "Wii U")})";
        _status.Text = scene.Resources.ActorName + " | " + string.Format(AppLocalization.Translate("{0} meshes | {1} cloths | {2} support groups | {3} animations"),
            scene.CurrentModel.Meshes.Count, scene.ClothCount, scene.SupportCount, scene.Animations.Count) +
            (scene.Resources.Warnings.Count > 0 ? " | " + string.Format(AppLocalization.Translate("{0} resource warnings"), scene.Resources.Warnings.Count) : "");
        UpdateTransport();
    }
    internal async Task OpenAsync()
    {
        if (_busy || _closing.IsCancellationRequested) return;
        BotwActorCatalogEntry? selected = null;
        _busy = true; SetPlaying(false); UpdateTransport();
        try
        {
            var folders = AppLocalization.ActorResourceFolders.Count > 0
                ? AppLocalization.ActorResourceFolders : _scene?.Resources.Roots ?? _resourceFolders;
            using var dialog = new BotwActorBrowserDialog(_catalog, folders);
            if (dialog.ShowDialog(DialogOwner) == DialogResult.OK && dialog.SelectedActor is { } actor)
            {
                selected = actor;
                _resourceFolders.Clear();
                _resourceFolders.AddRange(new[] { actor.ResourceRoot }.Concat(dialog.MatchingResourceRoots)
                    .Concat(dialog.ResourceFolders).Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }
        finally { _busy = false; if (!IsDisposed) UpdateTransport(); }
        if (selected != null && !_closing.IsCancellationRequested) await LoadActorAsync(selected.Path, selected.ArchiveEntry);
    }
    private async Task AddResourceFolderAsync()
    {
        using var dialog = new FolderBrowserDialog { Description = AppLocalization.Translate("Select the matching game content or romfs folder"), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(DialogOwner) != DialogResult.OK) return;
        if (!_resourceFolders.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase)) _resourceFolders.Add(dialog.SelectedPath);
        if (!AppLocalization.TrySetActorResourceFolders(AppLocalization.ActorResourceFolders.Append(dialog.SelectedPath)))
            MessageBox.Show(DialogOwner, AppLocalization.Translate("Game folders could not be saved."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (_path != null) await LoadActorAsync(_path, _actorEntry);
    }
    private void SetPlaying(bool playing)
    {
        _playing = playing; _lastTime = _clock.Elapsed.TotalSeconds;
        _play.Text = playing ? "\uE769" : "\uE768";
        _play.AccessibleName = AppLocalization.Translate(playing ? "Pause" : "Play"); _toolTips.SetToolTip(_play, _play.AccessibleName);
    }
    private void CommitAnimation(ToolboxActorAnimation? animation)
    {
        _selectedAnimation = animation; _hoverAnimation = null;
        SetPlaying(false); _frame = 0; _simulationAccumulator = 0;
        UpdateTransport(); Render(true);
    }
    private void PreviewHoveredAnimation()
    {
        if (_simulate.Checked || !_animationBrowsing) return;
        var candidate = _animations.SelectedItem as ToolboxActorAnimation;
        var preview = candidate == Animation ? null : candidate;
        if (_hoverAnimation == preview) return;
        _hoverAnimation = preview; UpdateTransport(); Render(false);
    }
    private void FinishAnimationBrowse()
    {
        _animationBrowsing = false; _animationClosing = true;
        if (_closing.IsCancellationRequested || !IsHandleCreated) return;
        var generation = _dropdownGeneration;
        // Native combo selection is provisional while its popup is open. Wait
        // for the commit notification before restoring a canceled highlight.
        BeginInvoke(new Action(() =>
        {
            if (IsDisposed || generation != _dropdownGeneration) return;
            var hadPreview = _hoverAnimation != null;
            _hoverAnimation = null; _animationClosing = false;
            _updating = true;
            try { _animations.SelectedItem = Animation; } finally { _updating = false; }
            _lastTime = _clock.Elapsed.TotalSeconds; UpdateTransport();
            if (hadPreview) Render(false);
        }));
    }
    private void Seek(double frame)
    {
        SetPlaying(false); _frame = Math.Clamp(frame, 0, Animation?.FrameCount ?? 0); _simulationAccumulator = 0; Render(true); UpdateTransport();
    }
    private void TickPlayback()
    {
        var time = _clock.Elapsed.TotalSeconds; var dt = Math.Max(time - _lastTime, 0); _lastTime = time;
        AdvancePlayback(dt);
    }
    internal void AdvancePlayback(double dt)
    {
        if (_busy || _scene == null) return;
        if ((_animationBrowsing || _animationClosing) && !_simulate.Checked) { PreviewHoveredAnimation(); return; }
        if (!_playing && !_simulate.Checked) return;
        void AdvanceAnimation(double seconds)
        {
            if (!_playing || Animation is not { } animation) return;
            _frame += seconds * AppLocalization.GetAnimationPlaybackFrameRate(ToolboxActorAnimation.BaseFramesPerSecond);
            if (_frame >= animation.FrameCount)
            {
                if (_loop.Checked) _frame %= animation.FrameCount;
                else { _frame = animation.FrameCount; SetPlaying(false); }
            }
        }
        try
        {
            if (_simulate.Checked)
            {
                _simulationAccumulator += Math.Min(dt, 0.1);
                var steps = 0;
                while (_simulationAccumulator >= 1.0 / 60 && steps++ < 6)
                {
                    // Advance and wrap at each actual physics step, never at
                    // the UI tick before its outstanding steps have run.
                    AdvanceAnimation(1.0 / 60);
                    _scene.Update(Animation, _frame, true, 1f / 60);
                    _simulationAccumulator -= 1.0 / 60;
                }
                _preview.SetVisualModelPose(_scene.CurrentModel);
            }
            else { _simulationAccumulator = 0; AdvanceAnimation(dt); Render(false); }
            UpdateFrame();
        }
        catch (Exception ex)
        {
            SetPlaying(false); _busy = true;
            try { _simulate.Checked = false; } finally { _busy = false; }
            _status.Text = AppLocalization.Translate("Preview stopped.") + " " + UserFacingExceptionFormatter.Format(ex);
        }
    }
    private void Render(bool reset)
    {
        if (_scene == null || _busy) return;
        var hover = !_simulate.Checked && _hoverAnimation != null;
        _preview.SetVisualModelPose(_scene.Update(hover ? _hoverAnimation : Animation, hover ? 0 : _frame, _simulate.Checked, 0, reset));
        _preview.Invalidate();
    }
    private void UpdateFrame()
    {
        _updating = true;
        var frame = _hoverAnimation != null ? 0 : _frame;
        _timeline.Value = (int)Math.Round(frame * 10); _frameInput.Value = Math.Clamp((decimal)frame, _frameInput.Minimum, _frameInput.Maximum);
        _updating = false;
    }
    private void UpdateTransport()
    {
        var loaded = !_busy && _scene != null; var animated = loaded && DisplayedAnimation != null;
        _open.Enabled = !_busy; _folders.Enabled = !_busy; _info.Enabled = loaded; _animations.Enabled = loaded && _scene!.Animations.Count > 0;
        _meshes.Enabled = loaded;
        _wind.Enabled = loaded && _scene!.HasCloth; _windStrength.Enabled = _wind.Enabled;
        if (_meshesWindow != null) _meshesWindow.Enabled = loaded;
        _play.Enabled = loaded && Animation != null; _reset.Enabled = loaded; _simulate.Enabled = loaded && _scene!.HasCloth;
        _frameInput.Enabled = animated; _timeline.Enabled = animated; _loop.Enabled = loaded;
        _timeline.Maximum = Math.Max(1, (DisplayedAnimation?.FrameCount ?? 1) * 10); _timeline.SmallChange = 10; _timeline.LargeChange = 100;
        _updating = true; _frameInput.Maximum = Math.Max(1, DisplayedAnimation?.FrameCount ?? 1); _updating = false;
        _frameCount.Text = $"/ {DisplayedAnimation?.FrameCount ?? 0}"; UpdateFrame();
    }
    private void ShowMeshes()
    {
        if (_scene == null) return;
        if (_meshesWindow != null) { _meshesWindow.Activate(); return; }
        _meshesWindow = new BotwActorMeshesForm(_scene, () =>
        {
            if (_scene != null) _preview.SetVisualModel(_scene.CurrentModel, resetCamera: false);
        });
        _meshesWindow.FormClosed += (_, _) => _meshesWindow = null;
        _meshesWindow.Show(DialogOwner);
    }
    private void ShowResources()
    {
        if (_scene == null) return;
        using var dialog = new LocalizedForm { Text = AppLocalization.Translate("Actor resources"), ClientSize = new Size(660, 420), MinimumSize = new Size(460, 300), StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor };
        var close = Button("Close"); close.Dock = DockStyle.Bottom; close.DialogResult = DialogResult.OK;
        var text = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false, BackColor = BackColor, ForeColor = ForeColor,
            Text = string.Join(Environment.NewLine, new[] { Path.GetFileName(_scene.Resources.SourcePath) + (_scene.Resources.SourceEntry is { } entry ? " -> " + entry : ""), "" }
                .Concat(_scene.Resources.Models.Select(m => $"Model/{m.Resource}.sbfres -> {m.Model}")).Concat(_scene.Resources.PhysicsFiles)
                .Concat(_scene.Resources.AnimationResources.Select(a => $"Model/{a}.sbfres")).Concat(new[] { "", AppLocalization.Translate("Warnings:") }).Concat(_scene.Resources.Warnings)) };
        dialog.Controls.Add(text); dialog.Controls.Add(close); dialog.CancelButton = close; dialog.ShowDialog(DialogOwner);
    }
    private static CheckBox Check(string text, bool value = false) => new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(8, 7, 8, 3) };
    private static Button Button(string text) => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(82, 30), Padding = new Padding(6, 2, 6, 2), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(62, 62, 62), ForeColor = Color.Gainsboro };
    private static Button Icon(string glyph, string name) => new() { Text = glyph, AccessibleName = name, Font = new Font("Segoe MDL2 Assets", 12), Size = new Size(36, 30), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(62, 62, 62), ForeColor = Color.Gainsboro };
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _closing.Cancel(); _timer.Dispose(); _meshesWindow?.Dispose(); _scene?.Dispose(); _toolTips.Dispose(); }
        base.Dispose(disposing);
    }
}
