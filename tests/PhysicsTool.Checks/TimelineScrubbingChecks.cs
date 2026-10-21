using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;
using System.Numerics;
using System.Reflection;

internal static class TimelineScrubbingChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static void Pump(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); } while (Environment.TickCount64 < until);
    }
    internal static void Check(MainWindow window, bool actor)
    {
        var timeline = window.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "ActorTimeline");
        var play = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), "Play / pause animation"));
        double Frame() => actor ? Field<double>(window, "_actorFrame") : window.EditorAnimationFrame;
        bool Playing() => actor ? Field<bool>(window, "_actorPlaying") : window.EditorAnimationPlaying;
        bool Simulating() => actor ? Field<bool>(window, "_actorSimulating") : window.EditorSimulating;
        Vector3[] Points() => actor ? window.Viewport.Data.Particles.Select(p => p.Position).ToArray() : window.Viewport.Simulated!.OrderBy(p => p.Key).Select(p => p.Value).ToArray();
        void Begin()
        {
            // Deliver input before draining background simulation jobs: unlike the
            // real event loop, RunJobs ignores pending OS input and can starve it.
            if (actor) typeof(MainWindow).GetMethod("BeginTimelineScrub", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            var thumb = timeline.GetVisualDescendants().OfType<Thumb>().Single();
            var point = thumb.TranslatePoint(new(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Require(window.TimelineScrubbing, "Timeline pointer press did not suspend preview");
            Require(!Field<DispatcherTimer>(window, "_actorTimer").IsEnabled && !Field<DispatcherTimer>(window, "_timer").IsEnabled, "A preview clock kept running during drag");
        }
        Dispatcher.UIThread.RunJobs();
        var camera = window.Viewport.CameraState;
        window.ToggleSimulation();
        Require(Simulating(), "Scrub fixture cannot simulate");
        foreach (var playing in new[] { false, true })
        {
            if (Playing() != playing) play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Begin();
            var origin = timeline.TranslatePoint(new(0, timeline.Bounds.Height / 2), window)!.Value;
            foreach (var fraction in new[] { .85, .12, .65, .02, .98, .3 })
            {
                window.MouseMove(origin + new Point(timeline.Bounds.Width * fraction, 0), RawInputModifiers.LeftMouseButton);
                // Exercise exact seeks as well as actual slider pointer movement.
                timeline.Value = fraction * timeline.Maximum;
                var frame = Frame(); var points = Points();
                Pump(70);
                Require(window.TimelineScrubbing && Frame() == frame, "Playback fought the dragged timeline");
                Require(points.SequenceEqual(Points()), "Cloth integrated between timeline drag events");
                Require(Playing() == playing && Simulating(), "Dragging changed playback/simulation selection");
                window.AdvanceEditorPreview(1f / 60);
                Require(points.SequenceEqual(Points()), "Queued preview step escaped scrubbing guard");
                Require(window.Viewport.Model!.Meshes.SelectMany(m => m.Vertices).All(float.IsFinite), "Scrubbing made model geometry nonfinite");
            }
            var selected = Frame();
            var clock = Field<DispatcherTimer>(window, actor ? "_actorTimer" : "_timer");
            var ticks = 0;
            void FirstTick(object? sender, EventArgs e) { ticks++; clock.Stop(); }
            clock.Tick += FirstTick;
            window.MouseUp(new Point(1, 1), MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Require(!window.TimelineScrubbing && Simulating() && Playing() == playing, "Release outside slider lost playback state");
            Require(clock.IsEnabled || ticks > 0, "Release did not restart preview clock");
            Pump(80);
            // Headless RunJobs does not drive the platform timer on every host.
            if (ticks == 0) typeof(DispatcherTimer).GetMethod("FireTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(clock, null);
            clock.Tick -= FirstTick;
            Require(ticks == 1 && (playing ? Frame() > selected : Frame() == selected), $"Playback did not resume at selected frame: playing={playing}, ticks={ticks}, {selected}->{Frame()}");
            Require(Frame() - selected < 10, "Drag wall time became catch-up animation frames");
            Require(Points().All(p => float.IsFinite(p.LengthSquared()) && p.Length() < 100), "Release produced spiking particles");
            Require(window.Viewport.CameraState == camera, "Timeline drag moved camera");
        }
        if (Playing()) play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.StopSimulation();
        Require(!Simulating(), "Could not stop simulation after scrub");
        Begin();
        timeline.Value = timeline.Maximum * .4;
        window.MouseUp(new Point(1, 1), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(!Simulating() && !Playing() && !window.TimelineScrubbing, "Scrubbing enabled paused simulation");
        Begin(); window.ShowOverview();
        window.MouseUp(new Point(1, 1), MouseButton.Left); Dispatcher.UIThread.RunJobs();
        Require(!window.TimelineScrubbing && !Field<DispatcherTimer>(window, "_actorTimer").IsEnabled && !Field<DispatcherTimer>(window, "_timer").IsEnabled,
            "Leaving preview during a drag left its clock running");
        Console.WriteLine($"PASS: {(actor ? "actor" : "editor")} pointer scrubbing, backward/forward seeks, held-pose stability, playback state, outside release and navigation cleanup.");
    }
    internal static void Native(string path)
    {
        var window = new MainWindow(graphics: false); window.Show();
        window.Viewport.ShowPhysics = false;
        var load = window.LoadActorPathAsync(path);
        while (!load.IsCompleted) Pump(10);
        load.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        Console.WriteLine("SCRUB LOADED " + path);
        var scene = Field<BotwActorScene>(window, "_actorScene");
        var animations = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ActorAnimation");
        var clip = scene.Animations.FirstOrDefault(a => a.Name == "Assassin_DotonCharge") ?? scene.Animations.FirstOrDefault(a => a.Name == "Act_Accordion") ?? scene.Animations.First(a => a.FrameCount > 20);
        animations.SelectedIndex = scene.Animations.IndexOf(clip) + 1;
        Check(window, true);
        window.Close();
    }
}
