using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using System.Numerics;

internal sealed class ViewportDepthChecks : Application
{
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var viewport = new PhysicsViewport { FlyNavigation = true, ShowPhysics = false };
        var surface = new ModelGlControl(viewport);
        var window = new Window { Width = 640, Height = 480, Content = surface, ShowInTaskbar = false };
        lifetime.MainWindow = window;
        var model = new VisualModelData { BoundsMinimum = new(-10, -10, -50), BoundsMaximum = new(10, 10, -49.998f) };
        model.Meshes.Add(Quad(-49.998f, new(1, 0, 0, 1)));
        model.Meshes.Add(Quad(-50, new(0, 1, 0, 1)));
        viewport.SetModel(model); viewport.StartFlyAt(Vector3.Zero);
        int stage = 0;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timeout.Tick += (_, _) => Finish("Depth test timed out"); timeout.Start();
        surface.RenderFailed += Finish;
        window.Opened += (_, _) => Request();
        surface.FrameCaptured += (pixels, width, height) =>
        {
            try
            {
                if (surface.DepthBits < 24) throw new Exception("Scene depth buffer is below 24 bits");
                int p = ((height / 2) * width + width / 2) * 4;
                bool red = pixels[p] > pixels[p + 1] + 40, green = pixels[p + 1] > pixels[p] + 40;
                if (stage == 0 && !red) throw new Exception($"Rear surface leaked through a 2mm separation at 50m: RGB {pixels[p]}/{pixels[p+1]}/{pixels[p+2]}");
                if (stage == 1 && !green) throw new Exception("Hidden foreground still occludes background");
                if (stage == 2 && !red) throw new Exception("Depth target did not survive resize");
                if (stage == 3 && !(pixels[p] > 50 && pixels[p + 2] > 50 && pixels[p + 1] < 40))
                    throw new Exception($"Coplanar seal did not blend over its wall: RGB {pixels[p]}/{pixels[p+1]}/{pixels[p+2]}");
                if (stage == 4 && !(pixels[p + 2] > 100 && pixels[p] < 20)) throw new Exception("Transparent seal erased its wall");
                if (stage == 5 && !green) throw new Exception("Seal bias leaked through a foreground object");
                if (stage == 0) viewport.HiddenMeshes.Add(0);
                else if (stage == 1) { viewport.HiddenMeshes.Clear(); window.Width = 800; window.Height = 600; }
                else if (stage < 5)
                {
                    var layered = new VisualModelData { BoundsMinimum = model.BoundsMinimum, BoundsMaximum = model.BoundsMaximum };
                    // Overlay first deliberately: the scene renderer must order it after the wall.
                    layered.Meshes.Add(TotkMaterialChecks.SealFixture(Quad(-50, Vector4.One), stage == 3 ? (byte)0 : (byte)128));
                    layered.Meshes.Add(Quad(-50, new(0, 0, 1, 1)));
                    if (stage == 4) layered.Meshes.Add(Quad(-49, new(0, 1, 0, 1)));
                    viewport.SetModel(layered); viewport.StartFlyAt(Vector3.Zero);
                }
                else { Result = 0; Console.WriteLine("PASS: 2mm layer separation at 50m, hide/resize, coplanar seal blending, transparent coverage and foreground occlusion."); Finish(null); return; }
                stage++; Dispatcher.UIThread.Post(Request, DispatcherPriority.Background);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        void Request() { surface.CaptureNextFrame = true; surface.RequestNextFrameRendering(); }
        void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
    private static VisualModelMesh Quad(float z, Vector4 color) => new()
    {
        Vertices = [-10,-10,z,0,0,1,0,0, 10,-10,z,0,0,1,1,0, 10,10,z,0,0,1,1,1, -10,10,z,0,0,1,0,1],
        Indices = [0,1,2,0,2,3], Material = new() { Tint = color }
    };
}
