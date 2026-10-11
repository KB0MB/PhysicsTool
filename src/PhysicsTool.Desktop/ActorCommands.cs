using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop.Rendering;
using System.Diagnostics;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private BotwActorScene? _actorScene;
    private VisualModelData? _savedEditorModel;
    private bool _savedBoneVisibility, _actorPlaying, _actorSimulating, _updatingTimeline;
    private double _actorFrame, _actorLastTick;
    private int _animationFps = 60;
    private readonly Stopwatch _actorClock = new();
    private readonly PreviewStepClock _actorSteps = new();
    private readonly DispatcherTimer _actorTimer = new() { Interval = TimeSpan.FromSeconds(1d / 60) };
    private readonly ComboBox _animations = new() { Name = "ActorAnimation", MinWidth = 200, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Slider _timeline = new() { Name = "ActorTimeline", Minimum = 0, Maximum = 1, Height = 48, MinWidth = 140 };
    private readonly CheckBox _loop = new() { Content = "Loop", IsChecked = true };
    private readonly TextBlock _frameLabel = new() { MinWidth = 105, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _actorBar = new() { IsVisible = false, RowDefinitions = new("Auto,Auto"), Margin = new Thickness(8) };
    private Button _actorPlay = null!;
    private CancellationTokenSource? _actorLoad;
    private GridLength _inspectorWidth;
    public bool IsActorMode => _actorScene != null;
    private ToolboxActorAnimation? ActorAnimation => _actorScene?.Animations.ElementAtOrDefault(_animations.SelectedIndex - 1);

    private void ConfigureActorBar(DockPanel center)
    {
        var row = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 12 };
        _actorPlay = ViewportTools.Icon("Play", "Play / pause animation", ToggleActorPlayback);
        row.Children.Add(_actorPlay); Grid.SetColumn(_animations, 1); row.Children.Add(_animations);
        Grid.SetColumn(_loop, 2); row.Children.Add(_loop); _actorBar.Children.Add(row);
        var time = new Grid { ColumnDefinitions = new("Auto,*,Auto"), ColumnSpacing = 8 };
        time.Children.Add(ViewportTools.Icon("SkipBack", "First frame", () => SeekActor(0)));
        Grid.SetColumn(_timeline, 1); time.Children.Add(_timeline); Grid.SetColumn(_frameLabel, 2); time.Children.Add(_frameLabel);
        Grid.SetRow(time, 1); _actorBar.Children.Add(time);
        DockPanel.SetDock(_actorBar, Dock.Bottom); center.Children.Add(_actorBar);
        _animations.SelectionChanged += (_, _) =>
        {
            if (_updatingTimeline) return;
            if (_actorScene == null) { SelectEditorAnimation(_animations.SelectedIndex - 1); return; }
            _actorPlaying = false; SeekActor(0); UpdateActorPlayback();
        };
        _timeline.ValueChanged += (_, _) => { if (!_updatingTimeline) SeekActor(_timeline.Value); };
        _actorTimer.Tick += (_, _) =>
        {
            try
            {
            if (_actorScene == null) return;
            var now = _actorClock.Elapsed.TotalSeconds;
            var count = _actorSteps.Advance(now - _actorLastTick, SimulationOptions.PlaybackSpeed); _actorLastTick = now;
            for (int i = 0; i < count; i++)
            {
                if (_actorPlaying && ActorAnimation is { } animation)
                {
                    _actorFrame += PreviewStepClock.FixedStep * ToolboxActorAnimation.BaseFramesPerSecond * _animationFps / 60d;
                    if (_actorFrame >= animation.FrameCount)
                    {
                        if (_loop.IsChecked == true) _actorFrame %= animation.FrameCount;
                        else { _actorFrame = animation.FrameCount; _actorPlaying = false; }
                    }
                }
                PoseActor(false, (float)PreviewStepClock.FixedStep);
            }
            UpdateActorPlayback();
            }
            catch (Exception ex)
            {
                _actorPlaying = _actorSimulating = false; _actorTimer.Stop(); UpdateActions(); UpdateActorPlayback();
                _status.Text = UserFacingExceptionFormatter.Format(ex);
            }
        };
    }
    private async Task OpenActorBrowser()
    {
        var browser = new ActorBrowserWindow();
        var entry = await browser.ShowDialog<BotwActorCatalogEntry?>(this);
        if (entry != null) await LoadActorPathAsync(entry.Path, browser.ResourceRoots.Prepend(entry.ResourceRoot), entry.ArchiveEntry);
    }
    public async Task LoadActorPathAsync(string path, IEnumerable<string>? roots = null, string? entry = null)
    {
        _actorLoad?.Cancel(); var cancel = _actorLoad = new CancellationTokenSource();
        var request = ++_modelRequest; _modelLoading = true; UpdateModelActions();
        var folders = roots?.ToArray(); PortableModelImporter.ConfigureBfres();
        try
        {
            var progress = new Progress<string>(message => { if (request == _modelRequest) _status.Text = message; });
            var scene = await Task.Run(() => BotwActorScene.Load(path, folders, progress, cancel.Token, entry), cancel.Token);
            if (request != _modelRequest || cancel.IsCancellationRequested) { scene.Dispose(); return; }
            PauseEditorAnimation(); StopSimulation();
            if (_actorScene == null)
            {
                _savedEditorModel = _viewport.Model; _savedBoneVisibility = _bonesToggle.IsChecked == true;
                _inspectorWidth = _workspace.ColumnDefinitions[2].Width;
            }
            else { _actorTimer.Stop(); _actorScene.Dispose(); }
            _actorScene = scene; _actorFrame = 0; _actorPlaying = _actorSimulating = false;
            _updatingTimeline = true;
            _animations.ItemsSource = new[] { "Rest pose" }.Concat(scene.Animations.Select(a => a.Name)).ToArray();
            _animations.SelectedIndex = 0; _updatingTimeline = false;
            _viewport.Data = scene.Overlay; _viewport.Selected.Clear(); _viewport.Simulated = null;
            _viewport.SetModel(scene.CurrentModel); _bonesToggle.IsChecked = false;
            _inspector.IsVisible = _workspaceSplit.IsVisible = _editorActions.IsVisible = false;
            _workspace.ColumnDefinitions[1].Width = new GridLength(0);
            _workspace.ColumnDefinitions[2].MinWidth = 0; _workspace.ColumnDefinitions[2].Width = new GridLength(0);
            _actorBar.IsVisible = true; PoseActor(true, 0); ShowEditor(); UpdateActions(); UpdateActorPlayback();
            Title = "PhysicsTool - " + scene.Resources.ActorName;
            _status.Text = $"{scene.Resources.ActorName}: {scene.ClothCount} cloths, {scene.SupportCount} helper groups, {scene.Animations.Count} animations, {scene.TextureCount} textures."
                + (scene.Resources.Warnings.Count == 0 ? "" : " " + string.Join(" ", scene.Resources.Warnings.Take(2)));
        }
        catch (OperationCanceledException) { }
        finally { if (request == _modelRequest) { _modelLoading = false; UpdateModelActions(); } cancel.Dispose(); if (ReferenceEquals(_actorLoad, cancel)) _actorLoad = null; }
    }
    private void ExitActor()
    {
        _actorLoad?.Cancel(); _actorTimer.Stop();
        if (_actorScene == null) return;
        _actorScene.Dispose(); _actorScene = null; _actorPlaying = _actorSimulating = false;
        _actorBar.IsVisible = false; _inspector.IsVisible = _workspaceSplit.IsVisible = _editorActions.IsVisible = true;
        _workspace.ColumnDefinitions[1].Width = new GridLength(5);
        _workspace.ColumnDefinitions[2].MinWidth = 300; _workspace.ColumnDefinitions[2].Width = _inspectorWidth;
        _bonesToggle.IsChecked = _savedBoneVisibility;
        _viewport.SetModel(_savedEditorModel); _savedEditorModel = null; Refresh(); ConfigureEditorTimeline(); UpdateModelActions();
    }
    private void SeekActor(double frame)
    {
        if (_actorScene == null) { SeekEditorAnimation(frame); return; }
        _actorFrame = Math.Clamp(frame, 0, ActorAnimation?.FrameCount ?? 0);
        PoseActor(true, 0); UpdateActorPlayback();
    }
    private void PoseActor(bool reset, float seconds)
    {
        if (_actorScene == null) return;
        _actorScene.WindEnabled = _wind.IsChecked == true; _actorScene.WindSpeed = SimulationOptions.WindSpeed;
        _actorScene.ConfigureSimulation(SimulationOptions.ApplyTo);
        _viewport.SetModelPose(_actorScene.Update(ActorAnimation, _actorFrame, _actorSimulating, seconds, reset));
        _viewport.Data = _actorScene.Overlay;
        _updatingTimeline = true;
        _timeline.Maximum = Math.Max(1, ActorAnimation?.FrameCount ?? 1); _timeline.Value = _actorFrame;
        _frameLabel.Text = $"{_actorFrame:0.0} / {ActorAnimation?.FrameCount ?? 0}";
        _updatingTimeline = false;
    }
    private void ToggleActorPlayback()
    {
        if (_actorScene == null) { ToggleEditorPlayback(); return; }
        if (ActorAnimation == null) return;
        if (!_actorPlaying && _actorFrame >= ActorAnimation.FrameCount) SeekActor(0);
        _actorPlaying = !_actorPlaying; UpdateActorPlayback();
    }
    private void UpdateActorPlayback()
    {
        var kind = _actorPlaying ? Lucide.Avalonia.LucideIconKind.Pause : Lucide.Avalonia.LucideIconKind.Play;
        if (_actorPlay.Content is not Lucide.Avalonia.LucideIcon icon || icon.Kind != kind)
            _actorPlay.Content = new Lucide.Avalonia.LucideIcon { Kind = kind, Size = 18 };
        _actorPlay.IsEnabled = ActorAnimation != null; _timeline.IsEnabled = ActorAnimation != null;
        if ((_actorPlaying || _actorSimulating) && !_actorTimer.IsEnabled)
        { _actorSteps.Reset(); _actorClock.Restart(); _actorLastTick = 0; _actorTimer.Start(); }
        else if (!_actorPlaying && !_actorSimulating) _actorTimer.Stop();
    }
}
