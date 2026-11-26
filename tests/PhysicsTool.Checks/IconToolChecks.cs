using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using SkiaSharp;
using System.Numerics;
using HKCLTool;

internal sealed class IconToolChecks : Application
{
    internal static string Root = "", Output = "";
    private static bool Botw => Directory.Exists(Path.Combine(Root, "Actor", "Pack"));
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1280, Height = 900, ShowInTaskbar = false };
        var view = new IconToolView(window); window.Content = view; lifetime.MainWindow = window;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(150) };
        timeout.Tick += (_, _) => Finish("Timed out"); timeout.Start();
        view.Surface!.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                var initialBrightness = view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "IconBrightness");
                if (initialBrightness.Value != 3 || view.CaptureSize != 256 || view.Surface.Brightness != 3)
                    throw new Exception("Brightness/size defaults are wrong");
                initialBrightness.Value = 1;
                var initialEdges = view.GetVisualDescendants().OfType<CheckBox>().Single(n => n.Name == "IconEdgeHighlights");
                if (initialEdges.IsChecked != true) throw new Exception("Edge highlights default is off");
                initialEdges.IsChecked = false;
                while (view.Busy) await Task.Delay(100);
                var catalog = IconCaptureAsset.Catalog(Root);
                var parent = new DirectoryInfo(Root);
                while (parent.Parent != null && !Directory.Exists(Path.Combine(parent.FullName, "Switch"))) parent = parent.Parent;
                var bfskaFixtures = new List<(string Path, bool SameSource)>();
                foreach (var animationRoot in new[] { Root, Path.Combine(parent.FullName, "Switch") }.Distinct())
                {
                    var actor = IconCaptureAsset.Catalog(animationRoot).FirstOrDefault(a => a.Name == "Armor_009_Upper");
                    if (actor == null) continue;
                    var resources = new BotwActorResources(actor.Path, [actor.ResourceRoot]);
                    if (resources.ReadModel("Player_Animation") is not { } bytes) continue;
                    var document = new ToolboxBfresDocument(bytes);
                    dynamic animation = document.SkeletalAnimations.First(a => string.Equals((string)a.Name, "Nml_Wait", StringComparison.OrdinalIgnoreCase));
                    string fixture = Path.Combine(Output, document.IsSwitch ? "switch-wait.bfska" : "wiiu-wait.bfska");
                    animation.Export(fixture, (dynamic)document.Resource);
                    bfskaFixtures.Add((fixture, animationRoot == Root));
                }
                if (Directory.Exists(Path.Combine(parent.FullName, "Switch")))
                {
                    var nested = IconCaptureAsset.Catalog(parent.FullName);
                    var source = nested.Single(a => a.Name == "Weapon_Sword_070" && a.ResourceRoot.Equals(IconCaptureAsset.Root(Root), StringComparison.OrdinalIgnoreCase));
                    await view.LoadPathAsync(source.Path, source.ResourceRoot);
                    if (view.Asset?.Name != "Weapon_Sword_070") throw new Exception("Parent folder actor did not load");
                    Console.WriteLine($"PASS parent folder scan: {nested.Length} actors, 070 loaded from its own resource root.");
                }
                foreach (var name in new[] { "Weapon_Sword_070", "Armor_009_Lower", "Armor_009_Head", "Armor_009_Upper" })
                {
                    if (!catalog.Any(a => a.Name == name)) throw new Exception("Missing catalog entry: " + name);
                    await view.LoadPathAsync(catalog.Single(a => a.Name == name).Path, Root);
                    var asset = view.Asset ?? throw new Exception("Actor did not load");
                    if (asset.Name != name) throw new Exception("Wrong actor loaded");
                    if (name.StartsWith("Armor_") && Botw)
                    {
                        if (asset.PoseName != "Nml_Wait" || asset.RestModel == null) throw new Exception("Armor idle pose unavailable");
                        if (!asset.Model.Meshes.All(m => m.Vertices.All(float.IsFinite))) throw new Exception("Invalid posed armor geometry");
                        if (asset.Model.Meshes.Count != asset.RestModel.Meshes.Count || asset.Model.TriangleCount != asset.RestModel.TriangleCount)
                            throw new Exception("Armor pose changed mesh topology");
                        if (asset.Model.Meshes.Zip(asset.RestModel.Meshes).All(p => p.First.Vertices.SequenceEqual(p.Second.Vertices)))
                            throw new Exception("Armor pose did not change geometry");
                    }
                    if (name.StartsWith("Weapon_") && asset.PoseName != null) throw new Exception("Armor pose applied to a weapon");
                    if (Botw && !asset.Model.Meshes.Any(m => m.Material.Botw != null)) throw new Exception("BotW shader was not selected");
                    var preset = IconCaptureLibrary.Load().Single(p => p.Name == name);
                    if (preset.Size != 576 || preset.CameraPosition == null) throw new Exception("Capture target inheritance failed");
                    if (preset.FieldOfView != 5) throw new Exception("Native capture FOV default was lost");
                    if (name.StartsWith("Armor_") && preset.LightDirection != new Vector3(.5f, -.5f, -.5f))
                        throw new Exception("Native capture light default was lost");
                    if (name == "Weapon_Sword_070" && (Vector3.Distance(preset.Rotation, new(-134.64f, 165.6f, -59)) > .001f ||
                        preset.LightDirection != new Vector3(.5f, 0, -.375f))) throw new Exception("Master Sword settings mismatch");
                    if (asset.Reference is not { } reference) throw new Exception("No reference icon");
                    File.WriteAllBytes(Path.Combine(Output, name + "-reference.png"), IconToolView.Encode(reference.Pixels, reference.Width, reference.Height, false));
                    await Task.Delay(200);
                    var png = await view.CapturePngAsync(576);
                    File.WriteAllBytes(Path.Combine(Output, name + "-preview.png"), png);
                    using var image = SKBitmap.Decode(png);
                    if (image.Width != 576 || image.Height != 576) throw new Exception("Wrong export size");
                    var pixels = image.Pixels;
                    var framedZoom = view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "IconZoom").Value!.Value;
                    if (name == "Weapon_Sword_070" && framedZoom <= 100) throw new Exception("Master Sword retained fused-weapon framing");
                    var occupied = pixels.Select((p, i) => (p, i)).Where(p => p.p.Alpha > 128).Select(p => (X: p.i % 576, Y: p.i / 576)).ToArray();
                    int span = Math.Max(occupied.Max(p => p.X) - occupied.Min(p => p.X), occupied.Max(p => p.Y) - occupied.Min(p => p.Y));
                    if (span < 360 || occupied.Min(p => p.X) < 12 || occupied.Max(p => p.X) > 564 || occupied.Min(p => p.Y) < 12 || !name.EndsWith("_Head") && occupied.Max(p => p.Y) > 564)
                        throw new Exception("Preset framing is too distant or clipped: " + name);
                    Console.WriteLine($"PASS fitted preset zoom {framedZoom:F1}%: {name}, span {span}/576, clear margins.");
                    int solid = pixels.Count(p => p.Alpha > 128), transparent = pixels.Count(p => p.Alpha == 0);
                    if (solid < 500 || transparent < pixels.Length / 5) throw new Exception("Blank or opaque export");
                    if (pixels.Distinct().Take(32).Count() < 16) throw new Exception("No shaded geometry");
                    Console.WriteLine($"PASS {name}: {asset.Model.Meshes.Count} meshes, {solid} opaque pixels; authored camera {preset.CameraPosition}; alpha PNG.");
                    if (view.CaptureSize != 256) throw new Exception("Capture preset changed the 256 output default");
                    var edges = view.GetVisualDescendants().OfType<CheckBox>().Single(n => n.Name == "IconEdgeHighlights");
                    edges.IsChecked = true;
                    var edgePng = await view.CapturePngAsync(576);
                    using (var edgeImage = SKBitmap.Decode(edgePng))
                    {
                        if (!pixels.Select(p => p.Alpha).SequenceEqual(edgeImage.Pixels.Select(p => p.Alpha))) throw new Exception("Edge highlights changed alpha");
                        if (edgePng.SequenceEqual(png)) throw new Exception("Edge highlights did not affect capture");
                    }
                    File.WriteAllBytes(Path.Combine(Output, name + "-edges.png"), edgePng);
                    edges.IsChecked = false;
                    var edgesReset = await view.CapturePngAsync(576);
                    if (!png.SequenceEqual(edgesReset)) throw new Exception("Edge highlights off did not restore capture");
                    Console.WriteLine("PASS edge highlight toggle: changed PNG, unchanged alpha, exact disable restoration.");
                    var smallPng = await view.CapturePngAsync(view.CaptureSize);
                    using (var small = SKBitmap.Decode(smallPng))
                    {
                        if (small.Width != 256 || small.Height != 256) throw new Exception("Default capture is not 256 pixels");
                        edges.IsChecked = true;
                        using var smallEdges = SKBitmap.Decode(await view.CapturePngAsync(256));
                        if (!small.Pixels.Select(p => p.Alpha).SequenceEqual(smallEdges.Pixels.Select(p => p.Alpha)))
                            throw new Exception("256-pixel edge highlights changed alpha");
                        edges.IsChecked = false;
                    }
                    var brightness = view.GetVisualDescendants().OfType<Slider>().Single(n => n.Name == "IconBrightnessSlider");
                    foreach (double value in new[] { 0d, .5d, 2d, 3d })
                    {
                        brightness.Value = value;
                        var exposed = await view.CapturePngAsync(576);
                        using var adjusted = SKBitmap.Decode(exposed);
                        var changed = adjusted.Pixels;
                        if (!pixels.Select(p => p.Alpha).SequenceEqual(changed.Select(p => p.Alpha)))
                            throw new Exception("Brightness changed transparency");
                        long before = pixels.Where(p => p.Alpha == 255).Sum(p => (long)p.Red + p.Green + p.Blue);
                        long after = changed.Where(p => p.Alpha == 255).Sum(p => (long)p.Red + p.Green + p.Blue);
                        if (value < 1 ? after >= before : after <= before) throw new Exception("Brightness did not change in the requested direction");
                        if (value == 0 && after != 0) throw new Exception("Zero brightness left lit pixels");
                        File.WriteAllBytes(Path.Combine(Output, name + "-brightness-" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png"), exposed);
                    }
                    brightness.Value = 1;
                    var brightnessRestored = await view.CapturePngAsync(576);
                    if (!png.SequenceEqual(brightnessRestored)) throw new Exception("Brightness reset changed default render");
                    Console.WriteLine("PASS 256-pixel capture and live brightness: 0/0.5/2/3, unchanged alpha, exact restoration.");
                    if (name == "Armor_009_Upper" && Botw)
                    {
                        var fixedCamera = view.Surface.FixedCamera;
                        var sameModel = view.Viewport.Model;
                        view.Surface.FixedCamera = IconCaptureAsset.View(preset.CameraPosition!.Value, Vector3.Zero) *
                            Matrix4x4.CreatePerspectiveFieldOfView(5 * MathF.PI / 180, 1, .01f, 50000);
                        var oldDepth = await view.CapturePngAsync(1152);
                        view.Surface.FixedCamera = fixedCamera;
                        var fittedDepth = await view.CapturePngAsync(1152);
                        File.WriteAllBytes(Path.Combine(Output, "belt-old-depth.png"), oldDepth);
                        File.WriteAllBytes(Path.Combine(Output, "belt-fitted-depth.png"), fittedDepth);
                        if (!ReferenceEquals(sameModel, view.Viewport.Model)) throw new Exception("Depth correction changed geometry");
                        Console.WriteLine("PASS model-fitted capture depth leaves geometry unchanged.");
                        edges.IsChecked = true;
                        var edgeNear = await view.CapturePngAsync(1152);
                        File.WriteAllBytes(Path.Combine(Output, "armor-soft-lit-edges.png"), edgeNear);
                        edges.IsChecked = false;
                        File.WriteAllText(Path.Combine(Output, "Armor_009_Upper-materials.json"), System.Text.Json.JsonSerializer.Serialize(
                            asset.Model.Meshes.Select(m => new { m.Name, m.Material.Botw?.Options, m.Material.Botw?.Parameters }),
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                        foreach (var mesh in asset.Model.Meshes) Console.WriteLine("POSE TEXTURE " + mesh.Name + " = " + mesh.Texture?.Name);
                        if (asset.Model.Meshes.Any(m => m.Texture == null)) throw new Exception("Armor shared texture unresolved");
                        var poses = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconArmorPose");
                        if (poses.SelectedIndex != 0 || poses.Items.Count != 1 || !Equals(poses.SelectedItem, "Icon Pose"))
                            throw new Exception("Icon Pose is not the only built-in pose");
                        Console.WriteLine("PASS single slot-specific Icon Pose default.");
                        foreach (string bone in new[] { "Arm_1_L", "Clavicle_L", "Finger_A_1_L", "Spine_2" })
                        {
                            view.SetPoseAdjustment(bone, new Vector3(15, 10, 5));
                            var adjustedPose = await view.CapturePngAsync(576);
                            if (png.SequenceEqual(adjustedPose)) throw new Exception("Pose adjustment did not change " + bone);
                            view.SetPoseAdjustment(bone, Vector3.Zero);
                            var resetPose = await view.CapturePngAsync(576);
                            if (!png.SequenceEqual(resetPose)) throw new Exception("Pose adjustment did not reset " + bone);
                        }
                        Console.WriteLine("PASS arm, shoulder, finger and spine editing; exact pose reset.");
                        foreach (var (bfska, sameSource) in bfskaFixtures)
                        {
                            if (!File.Exists(bfska)) continue;
                            view.ImportPoseBytes(File.ReadAllBytes(bfska));
                            var importedPng = await view.CapturePngAsync(576);
                            if (sameSource && png.SequenceEqual(importedPng)) throw new Exception("Imported Nml_Wait did not replace recovered upper pose");
                            var frame = view.GetLogicalDescendants().OfType<NumericUpDown>().Single(n => n.Name == "IconPoseFrame");
                            frame.Value = Math.Min(10, frame.Maximum);
                            var framePng = await view.CapturePngAsync(576);
                            if (framePng.SequenceEqual(importedPng)) throw new Exception("Imported frame did not update pose");
                            var saved = view.SavePresetBytes("Imported pose test");
                            poses.SelectedIndex = 0;
                            view.LoadPresetBytes(saved);
                            var restoredFrame = await view.CapturePngAsync(576);
                            if (!framePng.SequenceEqual(restoredFrame)) throw new Exception("Imported pose preset did not restore exact frame");
                            poses.SelectedIndex = 0;
                        }
                        Console.WriteLine("PASS BFSKA pose/frame import and embedded preset round trip.");
                    }
                    var savedPreset = view.SavePresetBytes("Capture round trip");
                    var presetBefore = await view.CapturePngAsync(256);
                    brightness.Value = .25; edges.IsChecked = true;
                    view.LoadPresetBytes(savedPreset);
                    var restoredPreset = await view.CapturePngAsync(256);
                    if (!presetBefore.SequenceEqual(restoredPreset)) throw new Exception("Custom preset did not restore exact capture");
                    try { view.LoadPresetBytes("{\"Version\":999}"u8.ToArray()); throw new Exception("Invalid preset accepted"); }
                    catch (InvalidDataException) { }
                    Console.WriteLine("PASS custom capture preset exact round trip and invalid version rejection.");
                    if (name == "Weapon_Sword_070" && Botw)
                    {
                        var captureZoom = view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "IconZoom");
                        var unchangedModel = view.Viewport.Model;
                        captureZoom.Value = Math.Min(400, framedZoom * 1.15m);
                        var zoomCapture = await view.CapturePngAsync(576);
                        if (png.SequenceEqual(zoomCapture) || !ReferenceEquals(unchangedModel, view.Viewport.Model))
                            throw new Exception("Capture zoom failed or changed model geometry");
                        using (var zoomImage = SKBitmap.Decode(zoomCapture))
                            if (zoomImage.Pixels.Count(p => p.Alpha > 128) <= solid) throw new Exception("Zoom-in did not enlarge the captured actor");
                        captureZoom.Value = framedZoom;
                        var zoomReset = await view.CapturePngAsync(576);
                        if (!png.SequenceEqual(zoomReset)) throw new Exception("Zoom reset changed capture");
                        Console.WriteLine("PASS fixed-camera capture zoom, unchanged geometry, exact zoom reset.");
                        var direct = view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "IconDirectLight");
                        direct.Value = .2m;
                        var dim = await view.CapturePngAsync(576);
                        if (png.SequenceEqual(dim)) throw new Exception("Light intensity control did not change pixels");
                        direct.Value = 1;
                        var yaw = view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "IconCameraAngle1");
                        yaw.Value = .25m;
                        var angled = await view.CapturePngAsync(576);
                        if (png.SequenceEqual(angled)) throw new Exception("Camera angle control did not change pixels");
                        yaw.Value = 0;
                        var restored = await view.CapturePngAsync(576);
                        if (!png.SequenceEqual(restored)) throw new Exception("Camera/light reset changed the render");
                        Console.WriteLine("PASS live camera and light controls; exact image restoration.");
                        var modes = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconCaptureMode");
                        modes.SelectedItem = "Viewport";
                        if (!view.Viewport.IsHitTestVisible || view.Surface.FixedCamera != null || view.Surface.CameraPosition != null)
                            throw new Exception("Viewport mode retained a fixed camera");
                        var model = view.Viewport.Model;
                        var free = await view.CapturePngAsync(576);
                        captureZoom.Value = 50;
                        var freeZoom = await view.CapturePngAsync(576);
                        if (free.SequenceEqual(freeZoom)) throw new Exception("Viewport capture zoom did not affect PNG");
                        captureZoom.Value = 100;
                        view.Viewport.Orbit(.4f, .1f);
                        var orbited = await view.CapturePngAsync(576);
                        if (free.SequenceEqual(orbited)) throw new Exception("Viewport orbit did not affect export");
                        if (!ReferenceEquals(model, view.Viewport.Model)) throw new Exception("Camera orbit modified the actor");
                        File.WriteAllBytes(Path.Combine(Output, "viewport-orbit.png"), orbited);
                        view.Viewport.Pan(30, -20);
                        var panned = await view.CapturePngAsync(576);
                        view.Viewport.ZoomAt(1, new Point(288, 288));
                        var zoomed = await view.CapturePngAsync(576);
                        if (orbited.SequenceEqual(panned) || panned.SequenceEqual(zoomed))
                            throw new Exception("Viewport pan/zoom did not affect export");
                        var viewportPreset = view.SavePresetBytes("Viewport camera");
                        view.Viewport.Orbit(.7f, -.2f); view.Viewport.Pan(15, 15);
                        view.LoadPresetBytes(viewportPreset);
                        var restoredViewport = await view.CapturePngAsync(576);
                        if (!zoomed.SequenceEqual(restoredViewport) || !view.UsesViewport || !view.Viewport.IsHitTestVisible)
                            throw new Exception("Custom preset lost the editable viewport camera");
                        modes.SelectedItem = modes.Items.OfType<IconCapturePreset>().Single(p => p.Name == name);
                        if (view.Viewport.IsHitTestVisible || view.Surface.FixedCamera == null)
                            throw new Exception("Capture preset did not restore fixed camera");
                        var presetRestored = await view.CapturePngAsync(576);
                        if (!png.SequenceEqual(presetRestored)) throw new Exception("Switching camera mode changed the native preset");
                        Console.WriteLine("PASS Viewport orbit/pan/zoom export, actor isolation, native preset restoration.");
                    }
                }
                var last = view.Asset;
                await view.LoadPathAsync(Path.Combine(Root, "missing.pack.zs"), Root);
                if (!ReferenceEquals(last, view.Asset)) throw new Exception("Failed load destroyed previous actor");
                Result = 0; Finish(null);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
}
