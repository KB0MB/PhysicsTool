using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using PhysicsTool.Desktop.Rendering;
using SkiaSharp;

internal sealed class IconWorkflowChecks : Application
{
    internal static string Root = "", Output = "";
    internal static string? BotwRoot;
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1150, Height = 850, ShowInTaskbar = false };
        var view = new IconToolView(window, botwMannequinRoot: BotwRoot); window.Content = view; lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                var actors = IconCaptureAsset.Catalog(Root);
                var pose = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconArmorPose");
                var gravity = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconSimulateGravity");
                var light = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconEnvironment");
                Require(view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconEdgeHighlights").IsChecked == true, "Edge highlights are not enabled by default");
                Require(Equals(light.SelectedItem, BotwLightingEnvironment.IconCapture), "Icon capture palette is not the default");
                var shadows = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconCastShadows");
                Require(!view.GetVisualDescendants().OfType<Expander>().Any(e => Equals(e.Header, "Capture details")), "Capture details remains");
                Require(!view.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Open pose")), "Open pose remains");
                foreach (string name in new[] { "Armor_001_Upper", "Armor_009_Upper", "Armor_001_Lower", "Armor_009_Lower", "Armor_001_Head", "Armor_009_Head" })
                {
                    var actor = actors.First(a => a.Name == name);
                    await view.LoadPathAsync(actor.Path, actor.ResourceRoot);
                    var asset = view.Asset!;
                    Require(asset.Name == name, "Actor load failed: " + name);
                    Require(pose.Items.Count == 1 && Equals(pose.SelectedItem, "Icon Pose"), "Built-in pose list is not Icon Pose only");
                    var initial = view.CaptureModel!;
                    Require(initial.Meshes.All(m => m.Vertices.All(float.IsFinite)), "Invalid geometry");
                    bool recovered = asset.ArmorPose!.UsesRecoveredIconPose;
                    Require(recovered == !name.EndsWith("Lower"), "Incorrect slot-specific pose");
                    Require(view.BeltVisible == asset.ArmorPose.HasBelt, "Available belt is not enabled by default");
                    var expected = asset.ArmorPose.Apply(new Dictionary<string, Vector3>(), recoveredPose: recovered, addBelt: true);
                    Require(SameGeometry(initial, expected), "Default did not use expected pose");
                    var original = await view.CapturePngAsync(256);
                    CheckPng(original);
                    if (name == "Armor_001_Head" && asset.Reference is { } framingReference)
                    {
                        using var framed = SKBitmap.Decode(original);
                        int top = Enumerable.Range(0, framed.Height).First(y => Enumerable.Range(0, framed.Width).Any(x => framed.GetPixel(x, y).Alpha >= 32));
                        float expectedTop = (1 - IconCaptureAsset.ReferenceCoverage(framingReference, true)) / 2;
                        Require(Math.Abs(top / (float)framed.Height - expectedTop) < .04f, "Hood framing shrank to fit the cape instead of the vanilla head crop");
                        Console.WriteLine($"PASS hood top margin: {top / (float)framed.Height:P1}, reference {expectedTop:P1}");
                    }
                    File.WriteAllBytes(Path.Combine(Output, name + "-Icon capture palette.png"), original);
                    if (name == "Armor_001_Upper") await CheckInteraction(view, window, original);
                    if (name is "Armor_001_Upper" or "Armor_009_Head")
                        File.WriteAllBytes(Path.Combine(Output, name + "-detail.png"), await view.CapturePngAsync(1024));
                    if (shadows.IsEnabled)
                    {
                        var shadowPreset = view.SavePresetBytes("Shadow roundtrip");
                        shadows.IsChecked = false;
                        var withoutShadows = await view.CapturePngAsync(256);
                        File.WriteAllBytes(Path.Combine(Output, name + "-no-shadows.png"), withoutShadows);
                        if (name == "Armor_001_Upper") File.WriteAllBytes(Path.Combine(Output, name + "-no-shadows-detail.png"), await view.CapturePngAsync(1024));
                        Require(!SameBytes(original, withoutShadows), "Shadows did not affect capture: " + name);
                        using var a = SKBitmap.Decode(original); using var b = SKBitmap.Decode(withoutShadows);
                        Require(a.Pixels.Zip(b.Pixels).All(p => p.First.Alpha == p.Second.Alpha), "Shadows changed silhouette alpha");
                        shadows.IsChecked = true;
                        Require(SameBytes(original, await view.CapturePngAsync(256)), "Shadow toggle did not restore pixels");
                        shadows.IsChecked = false; view.LoadPresetBytes(shadowPreset);
                        Require(shadows.IsChecked == true && SameBytes(original, await view.CapturePngAsync(256)), "Shadow preset did not restore capture");
                        if (name == "Armor_001_Upper")
                        {
                            var direction = view.Surface!.KeyLightDirection;
                            view.Surface.KeyLightDirection = new Vector3(1, 1, 1);
                            var changedLight = await view.CapturePngAsync(256);
                            view.Surface.CastShadows = false;
                            var changedLightUnshadowed = await view.CapturePngAsync(256);
                            using var c = SKBitmap.Decode(changedLight); using var d = SKBitmap.Decode(changedLightUnshadowed);
                            var firstMask = a.Pixels.Zip(b.Pixels).Select(p => p.First != p.Second).ToArray();
                            var movedMask = c.Pixels.Zip(d.Pixels).Select(p => p.First != p.Second).ToArray();
                            Require(!firstMask.SequenceEqual(movedMask), "Shadow mask did not follow the light");
                            view.Surface.KeyLightDirection = direction; view.Surface.CastShadows = true;
                            Require(SameBytes(original, await view.CapturePngAsync(256)), "Light/shadow reset did not restore capture");
                        }
                    }
                    var skin = initial.Meshes.Where(m => m.Name.EndsWith("__Mt_Upper_Skin") || m.Name.EndsWith("__Mt_Lower_Skin")).ToArray();
                    foreach (var mesh in skin) Require(mesh.Texture?.Name == IconMannequinMaterial.TextureName, "Armor skin was not replaced: " + mesh.Name);
                    if (asset.Reference is { } reference)
                        File.WriteAllBytes(Path.Combine(Output, name + "-reference.png"), IconToolView.Encode(reference.Pixels, reference.Width, reference.Height, false));
                    foreach (var environment in BotwLightingEnvironment.All.Where(e => e != BotwLightingEnvironment.IconCapture))
                    {
                        light.SelectedItem = environment;
                        var image = await view.CapturePngAsync(256);
                        CheckPng(image);
                        File.WriteAllBytes(Path.Combine(Output, name + "-" + environment.Name + ".png"), image);
                        if (light.IsEnabled) Require(!original.SequenceEqual(image), "Lighting did not change pixels");
                    }
                    light.SelectedItem = BotwLightingEnvironment.Preview;
                    var previewPreset = view.SavePresetBytes("Saved preview lighting");
                    light.SelectedItem = BotwLightingEnvironment.IconCapture;
                    view.LoadPresetBytes(previewPreset);
                    Require(Equals(light.SelectedItem, BotwLightingEnvironment.Preview), "Saved preset lighting was overridden by the default");
                    light.SelectedItem = BotwLightingEnvironment.IconCapture;
                    Require(SameBytes(original, await view.CapturePngAsync(256)), "Lighting did not reset exactly");
                    if (name.EndsWith("Lower"))
                    {
                        Require(asset.ArmorPose.HasBelt, "Trimmed belt not loaded: " + string.Join(";", asset.Warnings));
                        var waistSkin = initial.Meshes.Single(m => IconArmorProps.IsWaistSkin(m.Name));
                        Require(waistSkin.Texture?.Name == IconMannequinMaterial.TextureName, "Waist skin is not charcoal");
                        Require(waistSkin.Vertices.Length / 8 == 50 && waistSkin.Indices.Length / 3 == 66, "Wrong waist skin geometry");
                        var skinBefore = waistSkin.Vertices.ToArray();
                        view.SetBeltVisible(false);
                        Require(!view.BeltVisible && view.Viewport.HiddenMeshes.Count == 1, "Belt must hide exactly one mesh");
                        view.SetBeltVisible(true);
                        Require(view.BeltVisible && view.Viewport.HiddenMeshes.Count == 0, "Belt must restore exactly one mesh");
                        var mesh = view.CaptureModel!.Meshes.Single(m => IconArmorProps.IsBelt(m.Name));
                        Require(mesh.Vertices.Length / 8 == 256 && mesh.Indices.Length / 3 == 290, "Wrong trimmed belt geometry");
                        Require(mesh.Texture?.Name == "Link_Belt_A_Alb", "Wrong belt albedo");
                        File.WriteAllBytes(Path.Combine(Output, name + "-belt.png"), await view.CapturePngAsync(256));
                        File.WriteAllBytes(Path.Combine(Output, name + "-belt-detail.png"), await view.CapturePngAsync(1024));
                        if (shadows.IsEnabled)
                        {
                            var litBelt = await view.CapturePngAsync(256);
                            shadows.IsChecked = false;
                            var unshadowedBelt = await view.CapturePngAsync(256);
                            using var a = SKBitmap.Decode(litBelt); using var b = SKBitmap.Decode(unshadowedBelt);
                            Require(a.Pixels.Zip(b.Pixels).All(p => p.First.Alpha == p.Second.Alpha), "Belt shadows changed the silhouette");
                            int top = Enumerable.Range(0,a.Height).First(y => Enumerable.Range(0,a.Width).Any(x => a.GetPixel(x,y).Alpha > 127));
                            int differences = Enumerable.Range(top, Math.Min(a.Height-top, 40)).Sum(y => Enumerable.Range(0,a.Width).Count(x => a.GetPixel(x,y) != b.GetPixel(x,y)));
                            Require(differences > 0, "No belt/waist contact shadow");
                            shadows.IsChecked = true;
                            view.Surface!.AmbientIntensity = .5f;
                            Require(!SameBytes(litBelt, await view.CapturePngAsync(256)), "Icon ambient fill did not affect the capture");
                            view.Surface.AmbientIntensity = 1;
                            Require(SameBytes(litBelt, await view.CapturePngAsync(256)), "Icon fill/shadow reset changed pixels");
                            Console.WriteLine($"PASS lower waist shadows: {differences} changed pixels; ambient response and exact reset");
                        }
                        var before = mesh.Vertices.ToArray();
                        view.SetPoseAdjustment("Waist", new(12, 0, 0));
                        Require(!view.CaptureModel!.Meshes.Single(m => IconArmorProps.IsBelt(m.Name)).Vertices.SequenceEqual(before), "Belt does not follow waist");
                        Require(!view.CaptureModel.Meshes.Single(m => IconArmorProps.IsWaistSkin(m.Name)).Vertices.SequenceEqual(skinBefore), "Skin does not follow waist");
                        view.SetPoseAdjustment("Waist", Vector3.Zero);
                        var preset = view.SavePresetBytes("Belt roundtrip");
                        var withBelt = await view.CapturePngAsync(256);
                        view.SetBeltVisible(false); view.LoadPresetBytes(preset);
                        Require(view.BeltVisible && SameBytes(withBelt, await view.CapturePngAsync(256)), "Belt preset roundtrip failed");
                    }
                    if (name.EndsWith("Head"))
                    {
                        var mannequin = initial.Meshes.Where(m => m.Texture?.Name == IconMannequinMaterial.TextureName).ToArray();
                        Require(mannequin.Length >= 3, "Head mannequin must contain three charcoal meshes: " + string.Join(";",asset.Warnings));
                        foreach (var mesh in mannequin)
                        {
                            Require(mesh.Name.EndsWith("__Mt_Face") || mesh.Name.EndsWith("__Mt_Head") || mesh.Name.EndsWith("__Mt_Upper_Skin") || mesh.Name.StartsWith(IconArmorProps.Prefix + "Icon_Head_"), "Unexpected mannequin material: " + mesh.Name);
                            Require(mesh.Texture!.Pixels.SequenceEqual(new byte[] { 40, 39, 37, 255 }) && mesh.Texture.IsSrgb, "Mannequin albedo is not sRGB charcoal");
                        }
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        gravity.IsChecked = true;
                        var settledPng = await view.CapturePngAsync(256);
                        await view.Settlement;
                        Require(gravity.IsChecked == true, "Gravity failed: " + string.Join(" | ",view.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text)));
                        var settled = view.CaptureModel!;
                        Require(settled.TriangleCount == initial.TriangleCount && settled.Meshes.Count == initial.Meshes.Count, "Simulation changed topology");
                        Require(!SameGeometry(initial, settled), "Gravity did not change any geometry");
                        foreach (string fixedBone in new[] { "Root", "Waist", "Spine_2", "Neck", "Head" })
                        {
                            var a = initial.Bones.FirstOrDefault(b => b.Name == fixedBone);
                            var b = settled.Bones.FirstOrDefault(b => b.Name == fixedBone);
                            if (a != null && b != null) Require(Vector3.Distance(a.Position,b.Position) < .0001f, "Gravity moved fixed pose: " + fixedBone);
                        }
                        CheckPng(settledPng);
                        File.WriteAllBytes(Path.Combine(Output, name + "-gravity.png"), settledPng);
                        gravity.IsChecked = false;
                        Require(SameBytes(original, await view.CapturePngAsync(256)), "Gravity off did not restore capture");
                        gravity.IsChecked = true;
                        Require(SameBytes(settledPng, await view.CapturePngAsync(256)), "Gravity reset is not deterministic");
                        var saved = view.SavePresetBytes("Gravity roundtrip"); gravity.IsChecked = false;
                        view.LoadPresetBytes(saved);
                        Require(SameBytes(settledPng, await view.CapturePngAsync(256)), "Gravity preset roundtrip failed");
                        gravity.IsChecked = false;
                        Console.WriteLine($"PASS fixed-pose, no-wind gravity; deterministic reset and preset: {name} ({watch.Elapsed.TotalSeconds:F1}s for three runs)");
                    }
                    Console.WriteLine($"PASS Icon Pose, lighting, topology: {name}; {initial.Meshes.Count} meshes");
                }
                Result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static bool SameGeometry(VisualModelData a, VisualModelData b) => a.Meshes.Count == b.Meshes.Count &&
        a.Meshes.Zip(b.Meshes).All(p => p.First.Vertices.SequenceEqual(p.Second.Vertices));
    private static async Task CheckInteraction(IconToolView view, Window window, byte[] original)
    {
        var preview = view.GetVisualDescendants().OfType<Viewbox>().Single(c => c.Name == "IconCapturePreview");
        void Key(Avalonia.Input.Key key) => preview.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key });
        var camera = view.Surface!.FixedCamera;
        var model = view.Viewport.Model!;
        var originalVertex = model.Meshes[0].Vertices.ToArray();
        view.Viewport.TrackTransformPointer(new Point(200, 200));
        Key(Avalonia.Input.Key.R); Key(Avalonia.Input.Key.Y);
        Require(view.Viewport.TransformActive, "R/Y did not start model rotation");
        view.Viewport.TrackTransformPointer(new Point(230, 200));
        Require(!view.Viewport.Model!.Meshes[0].Vertices.SequenceEqual(originalVertex), "Rotation did not move vertices");
        Require(view.Surface.FixedCamera!.Value.M11 == camera!.Value.M11, "Model rotation changed camera framing");
        Key(Avalonia.Input.Key.Escape);
        Require(SameBytes(original, await view.CapturePngAsync(256)), "Canceled rotation changed capture");
        view.Viewport.TrackTransformPointer(new Point(200, 200));
        Key(Avalonia.Input.Key.G); Key(Avalonia.Input.Key.X);
        view.Viewport.TrackTransformPointer(new Point(220, 200)); Key(Avalonia.Input.Key.Enter);
        var positionX = view.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "IconActorPosition0");
        Require(positionX.Value != 0, "G/X did not commit translation");
        var moved = await view.CapturePngAsync(256);
        Require(!SameBytes(original, moved), "Model translation did not affect PNG");
        var saved = view.SavePresetBytes("Moved armor"); positionX.Value = 0; view.LoadPresetBytes(saved);
        Require(SameBytes(moved, await view.CapturePngAsync(256)), "Model placement preset did not roundtrip");
        positionX.Value = 0;
        var button = view.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "IconMeshes");
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        var dialog = window.OwnedWindows.Single(w => w.Title == "Meshes");
        var checks = dialog.GetVisualDescendants().OfType<CheckBox>().ToArray();
        Require(checks.Length == model.Meshes.Count(m => !IconArmorProps.IsProp(m.Name)), "Armor tab does not list armor meshes");
        checks[0].IsChecked = false; dialog.Close();
        Require(view.Viewport.HiddenMeshes.Contains(0), "Mesh menu did not hide mesh");
        var hidden = await view.CapturePngAsync(256);
        Require(!SameBytes(original, hidden), "Hidden mesh still appears in capture");
        view.SetPoseAdjustment("Neck", new(2, 0, 0)); view.SetPoseAdjustment("Neck", Vector3.Zero);
        Require(view.Viewport.HiddenMeshes.Contains(0), "Pose rebuild lost mesh visibility");
        var visibility = view.SavePresetBytes("Hidden mesh"); view.SetCaptureMeshVisible(0, true); view.LoadPresetBytes(visibility);
        Require(SameBytes(hidden, await view.CapturePngAsync(256)), "Mesh visibility preset did not roundtrip");
        var initiallyHidden = view.Viewport.HiddenMeshes.ToHashSet();
        for (int i = 0; i < model.Meshes.Count; i++) view.SetCaptureMeshVisible(i, false);
        using (var blank = SKBitmap.Decode(await view.CapturePngAsync(256)))
            Require(blank.Pixels.All(p => p.Alpha == 0), "Hidden meshes still render or cast onto empty capture");
        for (int i = 0; i < model.Meshes.Count; i++) view.SetCaptureMeshVisible(i, !initiallyHidden.Contains(i));
        Require(SameBytes(hidden, await view.CapturePngAsync(256)), "Restoring meshes changed hidden capture");
        view.SetCaptureMeshVisible(0, true);
        Console.WriteLine("PASS shared Meshes dialog, R/Y cancel, G/X commit, source isolation, model placement and visibility presets.");
    }
    private static bool SameBytes(byte[] a, byte[] b) => a.SequenceEqual(b);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void CheckPng(byte[] png)
    {
        using var image = SKBitmap.Decode(png);
        Require(image.Width == 256 && image.Height == 256, "Wrong capture size");
        Require(image.Pixels.Count(p => p.Alpha > 128) > 100 && image.Pixels.Any(p => p.Alpha == 0), "Blank or opaque capture");
    }
}
