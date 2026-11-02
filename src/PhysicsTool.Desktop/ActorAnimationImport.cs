using HKCLTool;
using PhysicsTool.Desktop.Rendering;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private readonly List<ToolboxActorAnimation> _actorImportedAnimations = new();

    private async Task LoadActorAnimationsAsync(Func<IReadOnlyList<ModelAnimationClip>> read, string path)
    {
        var scene = _actorScene;
        if (scene == null || _modelLoading)
            throw new InvalidOperationException("Wait for the actor to finish loading before importing animations.");

        var request = ++_animationRequest;
        var modelRequest = _modelRequest;
        _animationLoading = true;
        UpdateModelActions();
        try
        {
            var loaded = await Task.Run(read);
            if (request != _animationRequest || modelRequest != _modelRequest || !ReferenceEquals(scene, _actorScene)) return;

            var boneNames = scene.RestModel.Bones.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var clips = loaded.Select(c => c.Native).OfType<ToolboxActorAnimation>()
                .Where(c => c.BoneNames.Any(boneNames.Contains)).ToArray();
            if (clips.Length == 0)
                throw new InvalidDataException("No animation tracks match this model's bone names.");

            CancelTimelineScrub();
            _actorTimer.Stop();
            _actorPlaying = false;
            MergeActorAnimations(scene.Animations, clips);
            MergeActorAnimations(_actorImportedAnimations, clips);
            _actorFrame = 0;
            _updatingTimeline = true;
            try
            {
                _animations.ItemsSource = new[] { "Rest pose" }.Concat(scene.Animations.Select(a => a.Name)).ToArray();
                _animations.SelectedIndex = scene.Animations.IndexOf(clips[0]) + 1;
            }
            finally { _updatingTimeline = false; }

            PoseActor(true, 0);
            ResetTimelineClocks();
            UpdateActorPlayback();
            _status.Text = $"Imported {clips.Length} animation(s) from {Path.GetFileName(path)}.";
        }
        finally
        {
            if (request == _animationRequest)
            {
                _animationLoading = false;
                UpdateModelActions();
            }
        }
    }

    private static void MergeActorAnimations(List<ToolboxActorAnimation> target, IEnumerable<ToolboxActorAnimation> clips)
    {
        foreach (var clip in clips)
        {
            var index = target.FindIndex(a => a.Name == clip.Name && a.ResourceName == clip.ResourceName);
            if (index < 0) target.Add(clip);
            else target[index] = clip;
        }
    }

    private void ClearActorAnimations()
    {
        ++_animationRequest;
        _animationLoading = false;
        _actorImportedAnimations.Clear();
    }
}
