using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PhysicsTool.Desktop;

public sealed partial class MainWindow
{
    private bool _timelineScrubbing;
    internal bool TimelineScrubbing => _timelineScrubbing;

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

        _timelineScrubbing = false;
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
        _timelineScrubbing = false;
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
