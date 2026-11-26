using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using SkiaSharp;
using System.Numerics;

internal sealed class IconDyeChecks : Application
{
    internal static string Root = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1150, Height = 850, ShowInTaskbar = false };
        var view = new IconToolView(window, botwMannequinRoot: Root, preferencesPath: Path.Combine(Output, "test-icon-preferences.json")); window.Content = view; lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                var actors = IconCaptureAsset.Catalog(Root);
                foreach (string name in new[] { "Armor_001_Upper", "Armor_001_Lower", "Armor_001_Head" })
                {
                    var actor = actors.First(a => a.Name == name);
                    await view.LoadPathAsync(actor.Path, actor.ResourceRoot);
                    var asset = view.Asset ?? throw new Exception("No asset loaded");
                    if (name == "Armor_001_Upper")
                    {
                        Check(!view.GetVisualDescendants().OfType<ComboBox>().Any(c => c.Name == "IconExportFormat"), "Format is still in the sidebar");
                        Check(!view.GetVisualDescendants().OfType<ComboBox>().Any(c => c.Name == "IconCaptureSize"), "Size is still in the sidebar");
                        Check(!view.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "CaptureIconDyes"), "Separate Capture dyes command remains");
                        var capture = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CaptureIcon");
                        Check(capture.Parent is Grid grid && Grid.GetColumn(capture) == 1 && grid.ColumnDefinitions.Count == 2, "Capture command is not in the right toolbar column");
                        capture.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        var dialog = lifetime.Windows.OfType<IconCaptureDialog>().Single();
                        var format = dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconExportFormat");
                        var allDyes = dialog.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconExportAllDyes");
                        Check(dialog.Native && format.SelectedIndex == 0 && format.Items.Count == (asset.BntxTemplate == null ? 2 : 4), "Native format is not first/default");
                        var size = dialog.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconCaptureSize");
                        Check(dialog.CaptureSize == 256 && Equals(size.SelectedItem, 256), "Capture size default is not 256");
                        Check(dialog.Content is StackPanel panel && panel.Children.IndexOf(size) == panel.Children.IndexOf(format) + 2, "Size is not directly below Format");
                        Check(allDyes.IsEnabled && !dialog.ExportAllDyes, "Batch checkbox default or availability is wrong");
                        dialog.UpdateLayout();
                        var actions = new[] { "CancelIconCapture", "FolderIconCapture", "ConfirmIconCapture" }.Select(n => dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == n)).ToArray();
                        Check(actions.Select(b => b.Bounds.Width).Max() - actions.Select(b => b.Bounds.Width).Min() < 1, "Capture buttons are not equal width");
                        Check(actions.Select(b => b.Content?.ToString()).SequenceEqual(new[] { "Cancel", "Export to Folder", "Export to Mod" }), "Capture action labels/order changed");
                        using (var screenshot = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)dialog.Bounds.Width, (int)dialog.Bounds.Height)))
                        {
                            screenshot.Render(dialog);
                            screenshot.Save(Path.Combine(Output, "capture-dialog.png"));
                        }
                        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelIconCapture")
                            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        Check(!dialog.IsVisible && !view.Busy && ReferenceEquals(view.Asset, asset), "Cancel modified the loaded actor or started capture");
                        var options = new IconCaptureDialog(true, true);
                        var result = options.ShowDialog<bool>(window);
                        options.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconExportFormat").SelectedIndex = 1;
                        options.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconCaptureSize").SelectedItem = 512;
                        options.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
                        Check(!options.Native && options.ExportAllDyes && options.CaptureSize == 512, "PNG/size/batch options are not read from the dialog");
                        options.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmIconCapture")
                            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        Check(await result, "Capture confirmation was not returned");
                        var unavailable = new IconCaptureDialog(false, false);
                        var unavailableResult = unavailable.ShowDialog<bool>(window);
                        Check(!unavailable.Native && !unavailable.ExportAllDyes && !unavailable.GetVisualDescendants().OfType<CheckBox>().Single().IsEnabled, "Unavailable native/dye exports are enabled");
                        unavailable.Close(false);
                        Check(!await unavailableResult, "Closing capture options did not cancel");
                        var savedOptions = new IconCaptureDialog(true, true, bntxAvailable: true, lastFormat: IconImageFormat.BntxZs);
                        Check(savedOptions.Format == IconImageFormat.BntxZs, "Last chosen format was not restored");
                        var preferences = new IconToolPreferences { BotwMod = "botw-mod", TotkMod = "totk-mod", Format = IconImageFormat.BntxZs };
                        string preferencesPath = Path.Combine(Output, "roundtrip-preferences.json"); preferences.Save(preferencesPath);
                        var restoredPreferences = IconToolPreferences.Read(preferencesPath);
                        Check(restoredPreferences.BotwMod == preferences.BotwMod && restoredPreferences.TotkMod == preferences.TotkMod && restoredPreferences.Format == preferences.Format, "Separate mod folders/format did not persist");
                        try { IconToolPreferences.ModExportDirectory(Root, false, asset.IsSwitch, IconImageFormat.Sbitemico, [Root]); throw new Exception("Export into dump was accepted"); }
                        catch (InvalidOperationException) { }
                        if (asset.BntxTemplate != null)
                        {
                            var plain = await view.CaptureIconBytesAsync("BntxRoundtrip", IconImageFormat.Bntx);
                            var compressed = await view.CaptureIconBytesAsync("BntxRoundtrip", IconImageFormat.BntxZs);
                            using var decoder = new ZstdSharp.Decompressor(); var raw = decoder.Unwrap(compressed).ToArray();
                            Check(raw.AsSpan().StartsWith("BNTX"u8), "Compressed icon has no BNTX payload");
                            var a = TexToGoTexture.DecodeBntx(plain, "BntxRoundtrip"); var b = TexToGoTexture.DecodeBntx(raw, "BntxRoundtrip");
                            Check(a.Width == 256 && a.Height == 256 && a.Pixels.SequenceEqual(b.Pixels), "BNTX/Zstd pixel roundtrip mismatch");
                            File.WriteAllBytes(Path.Combine(Output, "BntxRoundtrip.bntx.zs"), compressed);
                            Console.WriteLine("PASS raw BNTX and .bntx.zs native decode/pixel equivalence");
                        }
                        Console.WriteLine("PASS capture popup, native default, format/batch selection, right toolbar placement and cancel");
                    }
                    if (name.EndsWith("_Head", StringComparison.Ordinal))
                    {
                        var resources = new BotwActorResources(actor.Path, [actor.ResourceRoot]);
                        Check(resources.ArmorEarRotationDegrees == new Vector3(0, 50, 0), "Hood EarRotate was not read from GParamUser");
                        Check(asset.ArmorPose!.EarRotationDegrees == resources.ArmorEarRotationDegrees, "Hood lost actor ear rotation");
                        var link = IconHeadMannequin.Load(resources, Root, default)!;
                        var posed = asset.ArmorPose.Apply(new Dictionary<string, Vector3>(), restPose: true);
                        foreach (var earName in new[] { "Ear_L", "Ear_R" })
                        {
                            var ear = link.RestPose.Bones.Single(b => b.Name == earName);
                            var parent = link.RestPose.Bones.Single(b => b.Index == ear.ParentIndex);
                            Matrix4x4.Invert(parent.WorldMatrix, out var parentInverse);
                            var local = ear.WorldMatrix * parentInverse;
                            var actual = posed.Bones.Single(b => b.Name == earName).WorldMatrix;
                            var expected = Matrix4x4.CreateRotationY(50 * MathF.PI / 180) * local * parent.WorldMatrix;
                            foreach (var point in new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
                                Check(Vector3.Distance(Vector3.Transform(point, actual), Vector3.Transform(point, expected)) < .00001f, "Ear modifier order/mirroring mismatch: " + earName);
                        }
                    }
                    var dyes = asset.Dyes!.Choices.Where(d => d.Animation.Length > 0).ToArray();
                    Check(dyes.Length == 16 && dyes.Select(d => d.Frame).SequenceEqual(Enumerable.Range(0, 16)), "Wrong dye frames: " + string.Join(',', dyes.Select(d => d.Frame)));
                    Check(dyes[0].ToString() == "0 - Default" && dyes[1].ToString() == "1 - Blue" && dyes[7].ToString() == "7 - Green" && dyes[15].ToString() == "15 - Gray", "Incorrect dye labels");
                    if (name is "Armor_001_Upper" or "Armor_001_Head")
                        view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconSimulateGravity").IsChecked = true;
                    var settlement = view.Settlement;
                    await view.SelectDyeAsync(dyes[0]);
                    Check(ReferenceEquals(settlement, view.Settlement) && settlement.IsCompleted, "Dye did not reuse the pending gravity result");
                    view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconEdgeHighlights").IsChecked = true;
                    var original = await view.CapturePngAsync(256);
                    CheckAlpha(original);
                    var before = view.CaptureModel!.Meshes.SelectMany(m => m.Vertices).ToArray();
                    var buffers = view.CaptureModel.Meshes.Select(m => m.Vertices).ToArray();
                    await view.SelectDyeAsync(dyes[1]);
                    Check(ReferenceEquals(settlement, view.Settlement), "Dye queued another gravity simulation");
                    Check(buffers.Zip(view.CaptureModel!.Meshes).All(p => ReferenceEquals(p.First, p.Second.Vertices)), "Dye replaced settled geometry buffers");
                    Check(view.Asset!.ArmorPose!.EarRotationDegrees == asset.ArmorPose!.EarRotationDegrees, "Dye lost ear modifier");
                    var colored = await view.CapturePngAsync(256);
                    CheckAlpha(colored);
                    Check(!original.SequenceEqual(colored), "Dye did not change captured pixels");
                    Check(before.SequenceEqual(view.CaptureModel!.Meshes.SelectMany(m => m.Vertices)), "Dye changed geometry");
                    foreach (var mesh in view.CaptureModel.Meshes.Where(m => m.Texture?.Name == IconMannequinMaterial.TextureName))
                    {
                        Check(!mesh.Material.UseAmbientOcclusion && mesh.AmbientOcclusionTexture == null && !mesh.Material.UseSpm, "Mannequin retained AO/specular maps");
                        if (mesh.Material.Botw is { } material)
                            Check(material.Options["uking_specmask_scaler"] == 400 && material.Options["uking_specular_channel"] == 0 && material.Options["uking_enable_ao"] == 0, "Mannequin retained native sketch/AO shading");
                    }
                    foreach (var mesh in view.CaptureModel.Meshes.Where(m => m.Name.EndsWith("__Mt_Upper_Skin") || m.Name.EndsWith("__Mt_Lower_Skin") || m.Name.StartsWith("Icon waist skin")))
                        Check(mesh.Texture?.Name == IconMannequinMaterial.TextureName, "Dye replaced charcoal skin");
                    File.WriteAllBytes(Path.Combine(Output, name + ".01.png"), colored);
                    var native = await view.CaptureIconBytesAsync(name + ".01", true);
                    File.WriteAllBytes(Path.Combine(Output, name + ".01.sbitemico"), native);
                    var decoded = Read(native);
                    Check(decoded.Name == name + ".01" && decoded.Width == 256 && decoded.Height == 256, "Native icon metadata mismatch");
                    using var image = SKImage.FromEncodedData(colored);
                    using var bitmap = new SKBitmap(256, 256, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    Check(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0), "Read PNG pixels");
                    var pixels = new byte[256 * 256 * 4]; System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
                    Check(pixels.SequenceEqual(decoded.Pixels), "Native icon pixel/alpha roundtrip mismatch");
                    // Test non-power-of-two native dimensions and independent channel/alpha patterns too.
                    foreach (int size in new[] { 96, 144, 256 })
                    {
                        byte[] pattern = new byte[size * size * 4];
                        for (int i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i * 17 + i / (size * 4));
                        var bytes = IconItemTexture.Encode(File.ReadAllBytes(asset.IconTemplate!), "Roundtrip", pattern, size, size);
                        Check(Read(bytes).Pixels.SequenceEqual(pattern), "Tiling/channel mismatch at " + size);
                    }
                    await view.SelectDyeAsync(IconDye.Original);
                    var restored = await view.CapturePngAsync(256);
                    File.WriteAllBytes(Path.Combine(Output, name + "-default-before.png"), original);
                    File.WriteAllBytes(Path.Combine(Output, name + "-default-after.png"), restored);
                    Check(before.SequenceEqual(view.CaptureModel!.Meshes.SelectMany(m => m.Vertices)), "Restoring default changed geometry");
                    Check(Same(original, restored), "Original dye did not restore exactly: " + name);
                    var selector = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconDye");
                    Check(selector.Focus(), "Dye selector could not receive keyboard focus");
                    void DyeKey(Avalonia.Input.Key key) => selector.RaiseEvent(new Avalonia.Input.KeyEventArgs
                    { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = key });
                    DyeKey(Avalonia.Input.Key.Down);
                    Check(Equals(selector.SelectedItem, dyes[1]), "Down arrow did not select the next dye");
                    Check(view.Busy && selector.IsEffectivelyEnabled && selector.IsKeyboardFocusWithin, "Dye loading disabled or unfocused the selector");
                    DyeKey(Avalonia.Input.Key.Down);
                    DyeKey(Avalonia.Input.Key.Down);
                    DyeKey(Avalonia.Input.Key.Up);
                    Check(Equals(selector.SelectedItem, dyes[2]), "Rapid arrow selections were dropped");
                    await view.DyeSelection;
                    Check(!view.Busy && selector.IsKeyboardFocusWithin && Equals(selector.SelectedItem, dyes[2]), "Dye completion changed keyboard focus or reverted selection");
                    Check(ReferenceEquals(settlement, view.Settlement), "Keyboard selection restarted gravity");
                    var queued = await view.CapturePngAsync(256);
                    await view.SelectDyeAsync(dyes[2]);
                    Check(Same(queued, await view.CapturePngAsync(256)), "Queued dye did not match the final selected color");
                    DyeKey(Avalonia.Input.Key.Up);
                    DyeKey(Avalonia.Input.Key.Up);
                    await view.DyeSelection;
                    Check(Same(original, await view.CapturePngAsync(256)), "Arrow keys did not restore Default");
                    selector.IsDropDownOpen = true;
                    selector.SelectedItem = dyes[3];
                    Check(selector.IsDropDownOpen && selector.IsEffectivelyEnabled, "Dye loading closed or disabled the open popup");
                    await view.DyeSelection;
                    Check(selector.IsDropDownOpen, "Dye completion closed the popup");
                    selector.IsDropDownOpen = false;
                    await view.SelectDyeAsync(dyes[0]);
                    Check(selector.Focus(), "Dye selector lost focus after closing the popup");
                    var invalidDye = new IconDye("missing-pattern", 0);
                    var superseded = view.SelectDyeAsync(invalidDye);
                    var latest = view.SelectDyeAsync(dyes[1]);
                    await Task.WhenAll(superseded, latest);
                    Check(!view.Busy && Equals(selector.SelectedItem, dyes[1]), "An obsolete failed dye blocked the latest selection");
                    bool dyeRejected = false;
                    try { await view.SelectDyeAsync(invalidDye); } catch (ArgumentException) { dyeRejected = true; }
                    Check(dyeRejected && !view.Busy && selector.IsKeyboardFocusWithin && Equals(selector.SelectedItem, dyes[1]), "Failed dye did not preserve selection and focus");
                    DyeKey(Avalonia.Input.Key.Up);
                    await view.DyeSelection;
                    Check(Same(original, await view.CapturePngAsync(256)), "Dye keys stopped working after a failed request");
                    if (name == "Armor_001_Upper")
                    {
                        int mesh = 0; view.SetCaptureMeshVisible(mesh, false);
                        var hidden = await view.CapturePngAsync(256);
                        string batch = Path.Combine(Output, "dyes");
                        await view.ExportDyesAsync(batch, true);
                        Check(ReferenceEquals(settlement, view.Settlement), "Batch restarted gravity simulation");
                        Check(Directory.GetFiles(batch, "*.sbitemico").Length == 16, "Batch count mismatch");
                        Check(view.Viewport.HiddenMeshes.Contains(mesh), "Batch lost hidden mesh");
                        Check(Same(hidden, await view.CapturePngAsync(256)), "Batch did not restore capture");
                        Check(File.Exists(Path.Combine(batch, name + ".sbitemico")) && File.Exists(Path.Combine(batch, name + ".15.sbitemico")), "Wrong native dye filenames");
                        bool rejected = false;
                        try { await view.ExportDyesAsync(batch, true); } catch (IOException) { rejected = true; }
                        Check(rejected && !view.Busy, "Existing files were not protected");
                        using var canceled = new CancellationTokenSource(); canceled.Cancel();
                        await view.ExportDyesAsync(Path.Combine(Output, "canceled"), false, canceled.Token);
                        Check(!view.Busy && Same(hidden, await view.CapturePngAsync(256)), "Canceled batch did not restore capture");
                        view.SetCaptureMeshVisible(mesh, true);
                    }
                    Check(selector.IsVisible && selector.Items.Count == 16 && Equals(selector.SelectedItem, dyes[0]), "Dye picker is unavailable or has duplicate default entries");
                    Console.WriteLine("PASS dye keyboard focus, rapid selection, popup, failure recovery, geometry, skin, native pixel/alpha roundtrip: " + name);
                }
                Result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }

    private static void CheckAlpha(byte[] png)
    {
        using var image = SKBitmap.Decode(png);
        Check(image.GetPixel(0, 0).Alpha == 0 && image.GetPixel(image.Width - 1, 0).Alpha == 0, "Icon background is not transparent");
        Check(image.Pixels.Any(p => p.Alpha > 0), "Empty icon capture");
    }
    private static VisualModelTexture Read(byte[] bytes)
    {
        var textures = new Dictionary<string, VisualModelTexture>(); var warnings = new List<string>();
        new ToolboxBfresDocument(bytes).ReadTextures(textures, warnings);
        Check(warnings.Count == 0 && textures.Count == 1, string.Join(';', warnings));
        return textures.Values.Single();
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static bool Same(byte[] a, byte[] b) => a.SequenceEqual(b);
}
