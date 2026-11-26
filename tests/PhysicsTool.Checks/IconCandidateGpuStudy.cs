using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using PhysicsTool.Core;
using PhysicsTool.Desktop;

internal sealed class IconCandidateGpuStudy : Application
{
    internal static string Root = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1100, Height = 800, ShowInTaskbar = false };
        var view = new IconToolView(window); window.Content = view; lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            await Task.Yield();
            try
            {
                Directory.CreateDirectory(Output);
                bool totk = Directory.Exists(Path.Combine(Root, "Pack", "Actor"));
                foreach (string name in new[] { "Mannequin_001_Head", "Mannequin_001_Upper", "Mannequin_003_Head", "Mannequin_009_Upper" })
                {
                    string path = Path.Combine(Root, totk ? "Pack/Actor" : "Actor/Pack", name + (totk ? ".pack.zs" : ".sbactorpack"));
                    if (!File.Exists(path)) continue;
                    await view.LoadPathAsync(path, Root);
                    if (view.Asset?.Name != name) throw new Exception("Could not load " + name);
                    if (view.Asset.Reference is { } icon)
                        File.WriteAllBytes(Path.Combine(Output, name + "-reference.png"), IconToolView.Encode(icon.Pixels, icon.Width, icon.Height, false));
                    var model = IconCaptureAsset.Rotate(view.Asset.Model, new(0, -25, 0));
                    view.Viewport.SetModelPose(model);
                    var center = (model.BoundsMinimum + model.BoundsMaximum) * .5f;
                    var eye = center + new Vector3(0, 0, Vector3.Distance(model.BoundsMinimum, model.BoundsMaximum) * 2);
                    var camera = IconCaptureAsset.View(eye, Vector3.Zero);
                    var range = IconCaptureAsset.DepthRange(model, camera);
                    var fov = IconCaptureAsset.FitFieldOfView(model, eye);
                    view.Surface!.FixedCamera = camera * Matrix4x4.CreatePerspectiveFieldOfView(fov * MathF.PI / 180, 1, range.Near, range.Far);
                    view.Surface.CameraPosition = eye; view.Surface.FixedViewDirection = Vector3.UnitZ;
                    File.WriteAllBytes(Path.Combine(Output, name + ".png"), await view.CapturePngAsync(512));
                    Console.WriteLine("RENDERED " + name);
                }
                Result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }
}
