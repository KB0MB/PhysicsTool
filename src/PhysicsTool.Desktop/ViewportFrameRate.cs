using Avalonia.Threading;

namespace PhysicsTool.Desktop;

// Presentation cadence is independent of clip frame rates and fixed physics steps.
internal static class ViewportFrameRate
{
    private static readonly List<WeakReference<DispatcherTimer>> Timers = [];
    private static int _framesPerSecond = 60;
    internal static int FramesPerSecond
    {
        get => _framesPerSecond;
        set
        {
            if (value is not (30 or 60)) throw new ArgumentOutOfRangeException(nameof(value));
            _framesPerSecond = value;
            Timers.RemoveAll(reference =>
            {
                if (!reference.TryGetTarget(out var timer)) return true;
                timer.Interval = Interval;
                return false;
            });
        }
    }
    internal static TimeSpan Interval => TimeSpan.FromSeconds(1d / _framesPerSecond);
    internal static DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = Interval };
        Timers.Add(new(timer));
        return timer;
    }
}
