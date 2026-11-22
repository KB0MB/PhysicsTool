using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using SkiaSharp;
using System.Runtime.InteropServices;

internal sealed class HelperPoseGpuChecks : Application
{
    internal static string Physics = "", Model = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        try
        {
            Directory.CreateDirectory(Output);
            var doc = new HkclService(); doc.Load(Physics);
            var original = doc.CaptureState();
            var modelPath = Model;
            if (Model.EndsWith(".mc", StringComparison.OrdinalIgnoreCase))
            {
                modelPath = Path.Combine(Output, Path.GetFileNameWithoutExtension(Model));
                File.WriteAllBytes(modelPath, Task.Run(() => TotkMeshCodec.Decode(File.ReadAllBytes(Model))).GetAwaiter().GetResult());
                foreach (var relative in new[] { "Shader/ExternalBinaryString.bfres.zs", "Pack/ZsDic.pack.zs" })
                {
                    var source = TotkResourceCompression.Roots(Model).Select(root => Path.Combine(root, relative)).FirstOrDefault(File.Exists);
                    var destination = Path.Combine(Output, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (source != null) File.Copy(source, destination, true);
                    else
                    {
                        source = TotkResourceCompression.Roots(Model).Select(root => Path.Combine(root, relative.Replace(".zs", ".mc"))).First(File.Exists);
                        File.WriteAllBytes(destination, Task.Run(() => TotkMeshCodec.Decode(File.ReadAllBytes(source))).GetAwaiter().GetResult());
                    }
                }
            }
            var rig = PortableModelImporter.LoadRig(modelPath);
            var supported = new List<int>();
            foreach (var group in doc.GetSidecarDriverGroups())
            {
                try
                {
                    foreach (var frame in doc.GetHelperPoseFrames(group.Index, true).Concat(doc.GetHelperPoseFrames(group.Index, false)))
                    {
                        var values = frame.Measure(rig.Rest.Bones);
                        var pose = frame.Pose(rig.Rest.Bones, values);
                        var bone = rig.Rest.Bones.Single(b => b.Name == pose.Name);
                        if (System.Numerics.Vector3.Distance(bone.Position, pose.Position) > .001f ||
                            Math.Abs(System.Numerics.Quaternion.Dot(bone.Rotation, pose.Rotation)) < .9999f)
                            throw new Exception("Native reference round trip failed: " + frame.Name);
                    }
                    supported.Add(group.Index);
                }
                catch (NotSupportedException) { }
            }
            var chosen = supported.First(i => doc.GetHelperPoseFrames(i, false).Count > 0);
            var window = new HelperPoseWindow(doc, chosen, rig) { ShowInTaskbar = false };
            lifetime.MainWindow = window;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            timeout.Tick += (_, _) => Finish("Pose preview timed out"); timeout.Start();
            var surface = window.GetLogicalDescendants().OfType<ModelGlControl>().Single();
            int stage = 0; byte[]? first = null;
            surface.RenderFailed += Finish;
            window.Opened += (_, _) => Request();
            surface.FrameCaptured += (pixels, width, height) => {
                try
                {
                    using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (height - 1 - y) * bitmap.RowBytes, width * 4);
                    using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(Output, $"helper-pose-{stage}.png"), png.ToArray());
                    if (stage++ == 0)
                    {
                        first = pixels;
                        var rotation = window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Rotation")); rotation.IsChecked = true;
                        Find<Slider>("PoseDriverValue").Value += .3;
                        Find<NumericUpDown>("HelperPoseValue1").Value += .25m;
                        Find<Button>("CaptureHelperPose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Find<CheckBox>("PoseTest").IsChecked = true;
                        Dispatcher.UIThread.Post(Request, DispatcherPriority.Background);
                    }
                    else
                    {
                        if (first!.SequenceEqual(pixels)) throw new Exception("Real model did not respond to posed bones");
                        if (doc.CaptureState() != original) throw new Exception("Pose preview changed source data");
                        Console.WriteLine($"PASS: {supported.Count} native groups round-trip against BFRES, live pose changes rendered pixels, source unchanged.");
                        Result = 0; Finish(null);
                    }
                }
                catch (Exception ex) { Finish(ex.ToString()); }
            };
            T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            void Request() { surface.CaptureNextFrame = true; surface.RequestNextFrameRendering(); }
            void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); Dispatcher.UIThread.Post(() => lifetime.Shutdown(1)); }
        base.OnFrameworkInitializationCompleted();
    }
}
