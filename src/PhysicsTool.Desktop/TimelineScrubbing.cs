using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private bool _timelineScrubbing;
    private double? _pendingTimelineFrame;
    private bool _timelineSeekQueued;
    private int _timelineSeekGeneration;
    private bool _timelinePhysicsDeferred;
    internal bool TimelineScrubbing => _timelineScrubbing;

    private void RequestTimelineSeek(double frame)
    {
        if (!_timelineScrubbing) { SeekActor(frame); return; }
        _pendingTimelineFrame = frame;
        _frameLabel.Text = $"{frame:0.0} / {_timeline.Maximum:0}";
        if (_timelineSeekQueued) return;
        _timelineSeekQueued = true;
        var generation = _timelineSeekGeneration;
        // Drain pending pointer input first; evaluate only its latest frame.
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _timelineSeekGeneration) return;
            _timelineSeekQueued = false;
            FlushTimelineSeek();
        }, DispatcherPriority.Background);
    }

    private void FlushTimelineSeek()
    {
        if (_pendingTimelineFrame is not { } frame) return;
        _pendingTimelineFrame = null;
        SeekActor(frame);
    }

    private void ConfigureTimelineScrubbing()
    {
        _timeline.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(_timeline).Properties.IsLeftButtonPressed)
                BeginTimelineScrub();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        _timeline.AddHandler(Thumb.DragStartedEvent, (_, _) => BeginTimelineScrub(), handledEventsToo: true);
        _timeline.AddHandler(Thumb.DragCompletedEvent, (_, _) => EndTimelineScrub(), handledEventsToo: true);
        _timeline.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => EndTimelineScrub(), handledEventsToo: true);
        AddHandler(InputElement.PointerReleasedEvent, (_, _) => EndTimelineScrub(), handledEventsToo: true);
        Deactivated += (_, _) => EndTimelineScrub();
        Closed += (_, _) => CancelTimelineScrub();
    }

    private void BeginTimelineScrub()
    {
        if (_timelineScrubbing || !_timeline.IsEnabled)
            return;

        _timelineScrubbing = true;
        _actorTimer.Stop();
        _timer.Stop();
        ResetTimelineClocks();
    }

    private void EndTimelineScrub()
    {
        if (!_timelineScrubbing)
            return;

        FlushTimelineSeek();
        _timelineSeekGeneration++; _timelineSeekQueued = false;
        _timelineScrubbing = false;
        if (_timelinePhysicsDeferred && IsActorMode) PoseActor(true, 0);
        _timelinePhysicsDeferred = false;
        ResetTimelineClocks();
        // Preserve playback/simulation choices, but never catch up the time spent
        // dragging. Each seek already seeded a stationary pose at the chosen frame.
        if (IsActorMode)
            UpdateActorPlayback();
        else
            UpdateEditorPlayback();
    }

    private void CancelTimelineScrub()
    {
        var refreshPhysics = _timelinePhysicsDeferred && IsActorMode;
        _pendingTimelineFrame = null;
        _timelineSeekGeneration++; _timelineSeekQueued = false;
        _timelinePhysicsDeferred = false;
        _timelineScrubbing = false;
        if (refreshPhysics) PoseActor(true, 0);
        ResetTimelineClocks();
    }

    private void ResetTimelineClocks()
    {
        _actorSteps.Reset();
        _previewStepClock.Reset();
        _actorLastTick = _actorClock.Elapsed.TotalSeconds;
        _lastTick = _clock.Elapsed.TotalSeconds;
    }
}
