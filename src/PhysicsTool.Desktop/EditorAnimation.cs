using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HKCLTool;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private Button _importAnimation = null!;
    private IReadOnlyList<ModelAnimationClip> _editorClips = [];
    private int _editorClipIndex = -1, _animationRequest;
    private double _editorFrame;
    private bool _editorPlaying, _animationLoading, _posingAnimation;
    private HkclPreviewSimulator? _animationPoseSolver;
    private ParticlePreviewData? _animationSource;
    private ModelAnimationClip? EditorClip => _editorClips.ElementAtOrDefault(_editorClipIndex);
    internal double EditorAnimationFrame => _editorFrame;
    internal bool EditorAnimationPlaying => _editorPlaying;
    internal bool EditorSimulating => _solver != null;
    internal int AnimationPlaybackFps
    {
        get => _animationFps;
        set { if (value is not 30 and not 60) throw new ArgumentOutOfRangeException(nameof(value)); _animationFps = value; }
    }

    private async Task OpenEditorAnimation()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import animation",
            FileTypeFilter = [new("BFRES skeletal animations") { Patterns = ["*.bfres", "*.sbfres"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await LoadAnimationPathAsync(path);
    }
    public async Task LoadAnimationPathAsync(string path)
    {
        RequireBfres(path);
        await LoadAnimationsAsync(() => ModelAnimationClip.Load(path), path);
    }
    internal async Task LoadAnimationsAsync(Func<IReadOnlyList<ModelAnimationClip>> read, string path)
    {
        if (_importedModel == null || IsActorMode) throw new InvalidOperationException("Load an editor model before importing animations.");
        var rig = _importedModel; var request = ++_animationRequest;
        _animationLoading = true; UpdateModelActions();
        try
        {
            var clips = await Task.Run(read);
            if (request != _animationRequest || !ReferenceEquals(rig, _importedModel) || IsActorMode) return;
            var names = rig.Rest.Bones.Select(b => ModelAnimationClip.Normalize(b.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            clips = clips.Where(c => c.BoneNames.Any(n => names.Contains(ModelAnimationClip.Normalize(n)))).ToArray();
            if (clips.Count == 0) throw new InvalidDataException("No animation tracks match this model's bone names.");
            PauseEditorAnimation(); StopSimulation();
            _editorClips = clips; _editorClipIndex = 0; _editorFrame = 0;
            ConfigureEditorTimeline(); PoseEditorAnimation(0, true, false);
            _status.Text = $"Imported {clips.Count} animation(s) from {System.IO.Path.GetFileName(path)}.";
        }
        finally { if (request == _animationRequest) { _animationLoading = false; UpdateModelActions(); } }
    }
    private void ConfigureEditorTimeline()
    {
        if (IsActorMode) return;
        CancelTimelineScrub();
        _updatingTimeline = true;
        _animations.ItemsSource = new[] { "Rest pose" }.Concat(_editorClips.Select(c => c.Name)).ToArray();
        _animations.SelectedIndex = _editorClipIndex + 1;
        _updatingTimeline = false;
        _actorBar.IsVisible = _importedModel != null;
        UpdateEditorPlayback();
    }
    internal void SelectEditorAnimation(int index)
    {
        if (IsActorMode) return;
        if (_ragdollSolver != null) StopSimulation();
        PauseEditorAnimation(); _editorClipIndex = Math.Clamp(index, -1, _editorClips.Count - 1); _editorFrame = 0;
        _updatingTimeline = true; _animations.SelectedIndex = _editorClipIndex + 1; _updatingTimeline = false;
        _animationPoseSolver = null; _animationSource = null;
        if (EditorClip != null) PoseEditorAnimation(0, true, false);
        else
        {
            if (_solver != null) { _solver.SetInputBoneDeltas(new Dictionary<int, Matrix4x4>()); _solver.Reset(); }
            _viewport.Simulated = null; _viewport.SimulatedBones = null; _viewport.SimulatedColliders = null;
            UpdateImportedModelPose();
        }
        UpdateEditorPlayback(); UpdateActions();
    }
    internal void SeekEditorAnimation(double frame)
    {
        if (EditorClip == null || IsActorMode) return;
        if (!_timelineScrubbing) PauseEditorAnimation();
        _editorFrame = Math.Clamp(frame, 0, EditorClip.EndFrame);
        PoseEditorAnimation(0, true, false); ResetTimelineClocks(); UpdateEditorPlayback();
    }
    internal void ToggleEditorPlayback()
    {
        if (EditorClip == null || IsActorMode) return;
        if (!_editorPlaying && _editorFrame >= EditorClip.EndFrame) SeekEditorAnimation(0);
        _editorPlaying = !_editorPlaying; UpdateEditorPlayback(); UpdateActions();
    }
    private void PauseEditorAnimation() { _editorPlaying = false; UpdateEditorPlayback(); }
    private void ClearEditorAnimation()
    {
        ++_animationRequest; _animationLoading = false; _editorPlaying = false; _editorClips = []; _editorClipIndex = -1; _editorFrame = 0;
        _animationSource = null; _animationPoseSolver = null;
        ConfigureEditorTimeline();
    }
    private void UpdateEditorPlayback()
    {
        if (IsActorMode || _actorPlay == null) return;
        var kind = _editorPlaying ? Lucide.Avalonia.LucideIconKind.Pause : Lucide.Avalonia.LucideIconKind.Play;
        if (_actorPlay.Content is not Lucide.Avalonia.LucideIcon icon || icon.Kind != kind)
            _actorPlay.Content = new Lucide.Avalonia.LucideIcon { Kind = kind, Size = 18 };
        _actorPlay.IsEnabled = _timeline.IsEnabled = EditorClip != null;
        _updatingTimeline = true;
        _timeline.Maximum = Math.Max(1, EditorClip?.EndFrame ?? 1); _timeline.Value = _editorFrame;
        _frameLabel.Text = $"{_editorFrame:0.0} / {EditorClip?.EndFrame ?? 0:0.0}";
        _updatingTimeline = false;
        if (_timelineScrubbing) { _timer.Stop(); return; }
        if ((_editorPlaying || _solver != null || _ragdollSolver != null) && !_timer.IsEnabled)
        { _clock.Restart(); _lastTick = 0; _previewStepClock.Reset(); _timer.Start(); }
        else if (!_editorPlaying && _solver == null && _ragdollSolver == null) _timer.Stop();
    }
    internal void AdvanceEditorPreview(float seconds)
    {
        if (_timelineScrubbing) return;
        if (_ragdollSolver != null)
        {
            _ragdollSolver.WindEnabled = _wind.IsChecked == true; _ragdollSolver.Step(seconds);
            _viewport.SimulatedBones = _ragdollSolver.GetBonePoses(); _viewport.SimulatedColliders = _ragdollSolver.GetColliderPoses();
            UpdateImportedModelPose(); _viewport.InvalidateVisual(); return;
        }
        var looped = false;
        if (_editorPlaying && EditorClip is { } clip)
        {
            _editorFrame += seconds * clip.FramesPerSecond * _animationFps / 60d;
            if (_editorFrame >= clip.EndFrame)
            {
                if (_loop.IsChecked == true) { _editorFrame %= clip.EndFrame; looped = true; }
                else { _editorFrame = clip.EndFrame; _editorPlaying = false; }
            }
        }
        if (EditorClip != null && _importedModel != null) PoseEditorAnimation(seconds, false, looped);
        else if (_solver != null)
        {
            _solver.WindEnabled = _wind.IsChecked == true; _solver.Step(seconds);
            _viewport.Simulated = _solver.GetPositions(); _viewport.SimulatedBones = _solver.GetBonePoses(); _viewport.SimulatedColliders = _solver.GetColliderPoses();
            UpdateImportedModelPose(); _viewport.InvalidateVisual();
        }
    }
    private void PoseEditorAnimation(float seconds, bool reset, bool looped)
    {
        if (_posingAnimation || IsActorMode || _importedModel == null || EditorClip == null) return;
        _posingAnimation = true;
        try
        {
            if (Session.Document.IsHelperBoneSidecar)
            {
                PoseHelperModel(_importedModel.AnimationSkeleton(EditorClip, _editorFrame));
                return;
            }
            var source = _viewport.Data;
            var rig = BoundImportedModel;
            if (!ReferenceEquals(source, _animationSource))
            {
                _animationSource = source;
                _animationPoseSolver = !Session.Document.IsReadOnlyExternal && source.Bones.Count > 0 ? new HkclPreviewSimulator(source) : null;
            }
            var solver = _solver ?? _animationPoseSolver;
            var suppressed = source.Bones.Where(b => solver?.DrivenBoneIndices.Contains(b.Index) == true)
                .Select(b => ModelAnimationClip.Normalize(b.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var clean = rig.AnimationSkeleton(EditorClip, _editorFrame, suppressed);
            var rest = rig.Rest.Bones.GroupBy(b => ModelAnimationClip.Normalize(b.Name), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var animated = clean.Bones.GroupBy(b => ModelAnimationClip.Normalize(b.Name), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Matrix4x4 Matrix(VisualModelBone b) => Matrix4x4.CreateScale(b.Scale) * Matrix4x4.CreateFromQuaternion(b.Rotation) * Matrix4x4.CreateTranslation(b.Position);
            if (solver != null)
            {
                var deltas = new Dictionary<int, Matrix4x4>();
                var bones = source.Bones.ToDictionary(b => b.Index);
                Matrix4x4 Delta(int index, HashSet<int> visited)
                {
                    if (deltas.TryGetValue(index, out var value)) return value;
                    if (!bones.TryGetValue(index, out var bone) || !visited.Add(index)) return Matrix4x4.Identity;
                    var name = ModelAnimationClip.Normalize(bone.Name);
                    return deltas[index] = rest.TryGetValue(name, out var r) && animated.TryGetValue(name, out var a) && Matrix4x4.Invert(Matrix(r), out var inverse)
                        ? inverse * Matrix(a) : Delta(bone.ParentIndex, visited);
                }
                foreach (var bone in source.Bones) Delta(bone.Index, new());
                solver.SetInputBoneDeltas(deltas);
                if (_solver == null || reset) solver.Reset();
                else if (looped) solver.RebaseInputPose();
                if (_solver != null && seconds > 0)
                {
                    solver.WindEnabled = _wind.IsChecked == true; solver.Step(seconds);
                }
                _viewport.Simulated = solver.GetPositions(); _viewport.SimulatedBones = solver.GetBonePoses(); _viewport.SimulatedColliders = solver.GetColliderPoses();
            }
            var visual = _solver != null ? clean : rig.AnimationSkeleton(EditorClip, _editorFrame);
            var overrides = visual.Bones.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,
                g => new VisualBoneOverride(g.Key, g.First().Position, g.First().Rotation, g.First().Scale), StringComparer.OrdinalIgnoreCase);
            if (_solver != null)
                foreach (var pose in _modelBinding.Overrides(rig.Rest, source, _viewport.SimulatedBones, MatchedModelReferences)) overrides[pose.Name] = pose;
            _viewport.SetModelPose(rig.Sample(overrides.Values.ToArray()));
            if (_solver == null)
            {
                var displayed = _viewport.Model!.Bones.GroupBy(b => ModelAnimationClip.Normalize(b.Name), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                _viewport.SimulatedBones = source.Bones.ToDictionary(b => b.Index, b => displayed.TryGetValue(ModelAnimationClip.Normalize(b.Name), out var v)
                    ? new SimulatedBonePreviewPose { Position = v.Position, AxisX = v.AxisX, AxisY = v.AxisY, AxisZ = v.AxisZ }
                    : new SimulatedBonePreviewPose { Position = b.Position, AxisX = b.AxisX, AxisY = b.AxisY, AxisZ = b.AxisZ });
            }
        }
        finally { _posingAnimation = false; }
    }
}
