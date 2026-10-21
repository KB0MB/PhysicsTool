using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using System.Diagnostics;

internal sealed class OverlayOrbitChecks : Application
{
    public static string ActorPath = "";
    public static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var viewport = new PhysicsViewport { ShowBones = false, ReadOnlyActorView = true };
        var surface = new ModelGlControl(viewport);
        var window = new Window { Width = 1600, Height = 900, ShowInTaskbar = false, Title = "PhysicsTool orbit rendering check",
            Content = new Grid { Children = { surface, viewport } } };
        lifetime.MainWindow = window;
        BotwActorScene? scene = null; bool finished = false; int stage = 0, frame = 0;
        var times = new List<double>(); var clock = new Stopwatch(); double previous = 0;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        timeout.Tick += (_, _) => Finish("Native orbit rendering timed out"); timeout.Start();
        surface.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try
            {
                PortableModelImporter.ConfigureBfres();
                scene = await Task.Run(() => BotwActorScene.Load(ActorPath));
                viewport.Data = scene.Overlay; viewport.SetModel(scene.CurrentModel); viewport.ShowPhysics = false;
                clock.Start(); window.RequestAnimationFrame(Tick);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        void Tick(TimeSpan _)
        {
            if (finished) return;
            try
            {
                var now = clock.Elapsed.TotalMilliseconds;
                if (frame > 20) times.Add(now - previous); previous = now;
                viewport.Orbit(.012f, 0); frame++;
                if (frame >= 120)
                {
                    if (!viewport.GpuReady) throw new Exception("GPU model rendering did not initialize");
                    times.Sort();
                    Console.WriteLine($"Native orbit {(stage == 0 ? "hidden" : stage == 1 ? "cloth" : "cloth+colliders")}: callback mean={times.Average():F2} ms, p95={times[(int)(times.Count * .95)]:F2} ms (1600x900, no simulation)");
                    stage++; frame = 0; times.Clear();
                    if (stage == 3) { Result = 0; Finish(null); return; }
                    viewport.ShowPhysics = true; viewport.ShowColliders = stage == 2;
                }
                window.RequestAnimationFrame(Tick);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }
        void Finish(string? error)
        {
            if (finished) return; finished = true; timeout.Stop();
            scene?.Dispose(); if (error != null) Console.Error.WriteLine(error);
            lifetime.Shutdown(Result);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
