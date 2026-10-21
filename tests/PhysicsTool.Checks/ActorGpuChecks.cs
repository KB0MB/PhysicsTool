using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal sealed class ActorGpuChecks : Application
{
    public static string PathToActor = "", Output = "";
    public static string[] ResourceRoots = [];
    public static string PathToModel = "", PathToAnimation = "", PathToPhysics = "";
    public static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new MainWindow { ShowInTaskbar = false }; lifetime.MainWindow = window;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
        timeout.Tick += (_, _) => Finish("Actor GPU check timed out."); timeout.Start();
        window.ModelSurface!.RenderFailed += Finish;
        window.Opened += async (_, _) =>
        {
            try
            {
                if (PathToModel.Length > 0)
                {
                    if (PathToPhysics.Length > 0) window.LoadPath(PathToPhysics);
                    await window.LoadModelPathAsync(PathToModel);
                    await window.LoadAnimationPathAsync(PathToAnimation);
                    window.SelectEditorAnimation(-1);
                }
                else await window.LoadActorPathAsync(PathToActor, ResourceRoots);
                window.Viewport.ShowPhysics = false;
                var first = await Capture("actor-rest.png");
                var lenses = window.Viewport.Model!.Meshes.Select((m, i) => (Mesh: m, Index: i))
                    .Where(p => p.Mesh.Name.Contains("__Mt_Lens") && p.Mesh.Material.Totk != null).ToArray();
                if (lenses.Length > 0)
                {
                    // Isolate the eyes from the separate, unsupported procedural glass pass.
                    foreach (var lens in lenses) window.Viewport.SetMeshVisible(lens.Index, false);
                    await Capture("actor-eyes-without-lenses.png");
                    foreach (var lens in lenses) window.Viewport.SetMeshVisible(lens.Index, true);
                }
                var emission = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "ViewportEmission");
                if (emission.IsChecked == true || window.Viewport.ShowEmission) throw new Exception("Emission must start disabled");
                if (window.Viewport.Model!.Meshes.Any(m => m.Material.Totk?.Integers.GetValueOrDefault("totkEmissionEnabled") == 1))
                {
                    emission.IsChecked = true;
                    var glowing = await Capture("actor-emission-enabled.png");
                    emission.IsChecked = false;
                    var restoredEmission = await Capture("actor-emission-disabled.png");
                    if (SHA256.HashData(glowing).SequenceEqual(SHA256.HashData(first))) throw new Exception("Emission checkbox did not affect the render");
                    if (!SHA256.HashData(restoredEmission).SequenceEqual(SHA256.HashData(first))) throw new Exception("Emission toggle did not restore base shading");
                    Console.WriteLine("PASS: emission starts disabled, toggles live, and restores base shading exactly.");
                }
                var missingMeshes = window.Viewport.Model!.Meshes.Select((m, i) => (Mesh: m, Index: i))
                    .Where(p => p.Mesh.Name.StartsWith("Band_A__Mt_Band_Miasma") || p.Mesh.Name.StartsWith("Botom_Back__Mt_Body_Miasma_Lower")).ToArray();
                if (missingMeshes.Length > 0)
                {
                    for (int i = 0; i < window.Viewport.Model.Meshes.Count; i++) window.Viewport.SetMeshVisible(i, false);
                    var empty = await Capture("actor-hidden.png");
                    foreach (var mesh in missingMeshes)
                    {
                        window.Viewport.SetMeshVisible(mesh.Index, true);
                        var rendered = await Capture("visible-" + mesh.Index + ".png");
                        int changed = Enumerable.Range(0, rendered.Length / 4).Count(i => !rendered.AsSpan(i * 4, 4).SequenceEqual(empty.AsSpan(i * 4, 4)));
                        if (changed < 32) throw new Exception(mesh.Mesh.Name + " still invisible");
                        Console.WriteLine($"PASS: {mesh.Mesh.Name} renders {changed} pixels in isolation.");
                        window.Viewport.SetMeshVisible(mesh.Index, false);
                    }
                    for (int i = 0; i < window.Viewport.Model.Meshes.Count; i++) window.Viewport.SetMeshVisible(i, true);
                }
                var totkMaterials = window.Viewport.Model!.Meshes.Select(m => m.Material).Where(m => m.Totk != null).Distinct().ToArray();
                if (totkMaterials.Length > 0)
                {
                    var enabled = totkMaterials.SelectMany(m => m.Totk!.Integers.Where(p => p.Key.StartsWith("totkTextureEnabled")).Select(p => (Material: m.Totk, p.Key, p.Value))).ToArray();
                    foreach (var item in enabled) item.Material!.Integers[item.Key] = 0;
                    var untextured = await Capture("actor-textures-disabled.png");
                    foreach (var item in enabled) item.Material!.Integers[item.Key] = item.Value;
                    var restored = await Capture("actor-textures-restored.png");
                    if (SHA256.HashData(untextured).SequenceEqual(SHA256.HashData(restored))) throw new Exception("TexToGo bindings did not affect rendered pixels.");
                    if (!SHA256.HashData(first).SequenceEqual(SHA256.HashData(restored))) throw new Exception("TotK sampler restoration changed rendering.");
                    var normalMaterials = totkMaterials.Where(m => m.UseNormal).ToArray();
                    foreach (var m in normalMaterials) m.UseNormal = false;
                    var flat = await Capture("actor-normals-disabled.png");
                    foreach (var m in normalMaterials) m.UseNormal = true;
                    if (normalMaterials.Length > 0 && SHA256.HashData(flat).SequenceEqual(SHA256.HashData(restored))) throw new Exception("TotK normal maps did not affect rendered pixels.");
                    Console.WriteLine("PASS: TexToGo sampling, exact restoration, and normal-map GPU pixel differences.");
                }
                var previewMaterials = window.Viewport.Model!.Meshes.Select(m => m.Material).Distinct()
                    .Where(m => m.UseBotwPreviewShader && (int)m.ShaderModel < 2).ToArray();
                if (previewMaterials.Length > 0)
                {
                    foreach (var material in previewMaterials) material.UseBotwPreviewShader = false;
                    var fallback = await Capture("actor-standard-comparison.png");
                    foreach (var material in previewMaterials) material.UseBotwPreviewShader = true;
                    var restored = await Capture("actor-botw-restored.png");
                    if (SHA256.HashData(fallback).SequenceEqual(SHA256.HashData(restored))) throw new Exception("BotW material program did not change rendered pixels.");
                    if (!SHA256.HashData(first).SequenceEqual(SHA256.HashData(restored))) throw new Exception("BotW material program switching did not restore rendering.");
                }
                var animations = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ActorAnimation");
                var armorButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PlayerArmor");
                if (System.IO.Path.GetFileName(PathToActor) == "Player.pack.zs")
                {
                    if (!armorButton.IsVisible) throw new Exception("Player armor controls are hidden");
                    animations.SelectedIndex = 1;
                    var armorTimeline = window.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "ActorTimeline");
                    armorTimeline.Value = Math.Min(8, armorTimeline.Maximum);
                    var frame = armorTimeline.Value; var clip = animations.SelectedItem;
                    window.Viewport.Orbit(.15f, .05f); var camera = window.Viewport.CameraState;
                    await window.SwapPlayerArmorAsync(Enum.GetValues<HKCLTool.PlayerArmorSlot>().ToDictionary(s => s, s => "Armor_001_" + s), default);
                    if (window.Viewport.CameraState != camera || armorTimeline.Value != frame || !Equals(animations.SelectedItem, clip))
                        throw new Exception($"Armor swap reset camera={window.Viewport.CameraState != camera}, frame={frame}->{armorTimeline.Value}, clip={clip}->{animations.SelectedItem}");
                    if (window.Viewport.Model!.Meshes.Any(m => m.Name.Contains("Hair_999")) || !window.Viewport.Model.Meshes.Any(m => m.Name.Contains("Hood_A_001")))
                        throw new Exception("Head armor was not replaced");
                    await Capture("player-hylian-armor.png");
                    var defaults = Enum.GetValues<HKCLTool.PlayerArmorSlot>().ToDictionary(s => s, HKCLTool.PlayerArmorCatalog.DefaultName);
                    var tiedHair = new Dictionary<HKCLTool.PlayerArmorSlot, string>(defaults) { [HKCLTool.PlayerArmorSlot.Head] = "Armor_1151_Head" };
                    await window.SwapPlayerArmorAsync(tiedHair, default);
                    await Capture("player-1151-head.png");
                    tiedHair[HKCLTool.PlayerArmorSlot.Upper] = "Armor_116_Upper";
                    tiedHair[HKCLTool.PlayerArmorSlot.Lower] = "Armor_001_Lower";
                    await window.SwapPlayerArmorAsync(tiedHair, default);
                    var scene = (HKCLTool.BotwActorScene)typeof(MainWindow).GetField("_actorScene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                    var reportedClip = scene.Animations.First(a => a.Name == "Assassin_DotonCharge");
                    window.Viewport.SetModelPose(scene.Update(reportedClip, 24, false, 0, true));
                    await Capture("player-116-1151-animated.png");
                    for (int f = 0; f <= 24; f++) scene.Update(reportedClip, f, true, 1f / 60, f == 0);
                    window.Viewport.SetModelPose(scene.CurrentModel);
                    await Capture("player-116-1151-simulated.png");
                    var beforeFailure = window.Viewport.Model;
                    try { await window.SwapPlayerArmorAsync(new Dictionary<HKCLTool.PlayerArmorSlot, string>(defaults) { [HKCLTool.PlayerArmorSlot.Upper] = "Armor_001_Head" }, default); throw new Exception("Wrong-slot armor accepted"); }
                    catch (System.IO.InvalidDataException) { }
                    if (!ReferenceEquals(window.Viewport.Model, beforeFailure)) throw new Exception("Failed armor load destroyed current outfit");
                    using (var canceled = new CancellationTokenSource())
                    {
                        canceled.Cancel();
                        try { await window.SwapPlayerArmorAsync(defaults, canceled.Token); throw new Exception("Canceled outfit was applied"); }
                        catch (OperationCanceledException) { }
                    }
                    if (!ReferenceEquals(window.Viewport.Model, beforeFailure)) throw new Exception("Canceled armor load changed current outfit");
                    await window.SwapPlayerArmorAsync(defaults, default);
                    animations.SelectedIndex = 0;
                    await Capture("player-default-restored.png");
                    foreach (var (label, equipment) in new[] { ("stowed", PlayerWeaponChecks.Outfit()), ("sword", PlayerWeaponChecks.Outfit(true)), ("bow", PlayerWeaponChecks.Outfit(bow: true)) })
                    {
                        var equipmentCamera = window.Viewport.CameraState;
                        await window.SwapPlayerEquipmentAsync(tiedHair, equipment, default);
                        if (window.Viewport.CameraState != equipmentCamera) throw new Exception("Equipment swap changed camera");
                        var equippedScene = (HKCLTool.BotwActorScene)typeof(MainWindow).GetField("_actorScene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                        PlayerWeaponChecks.Verify(equippedScene);
                        await Capture("player-equipment-" + label + ".png");
                        window.Viewport.Orbit(3.14f, 0);
                        await Capture("player-equipment-" + label + "-back.png");
                        window.Viewport.Orbit(-3.14f, 0);
                    }
                    await window.SwapPlayerEquipmentAsync(defaults, new Dictionary<HKCLTool.PlayerWeaponKind, HKCLTool.PlayerWeaponSelection>(), default);
                    await Capture("player-equipment-cleared.png");
                    Console.WriteLine("PASS: Player default/equipped/1151 armor rendering, frame and camera preservation, cancellation and failed-load rollback.");
                }
                else if (armorButton.IsVisible) throw new Exception("Armor controls visible for a non-Player actor");
                if (animations.ItemCount >= 2)
                {
                animations.SelectedIndex = 1;
                var timeline = window.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "ActorTimeline");
                timeline.Value = timeline.Maximum / 2;
                var posed = await Capture("actor-posed.png");
                if (SHA256.HashData(first).SequenceEqual(SHA256.HashData(posed))) throw new Exception("Actor pose did not update GPU geometry.");
                var play = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(ToolTip.GetTip(b), "Play / pause animation"));
                var before = timeline.Value; play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(250);
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (timeline.Value == before) throw new Exception("Actor playback clock did not advance.");
                window.ToggleSimulation(); play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(250);
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.StopSimulation();
                await Capture("actor-simulated.png");
                }
                else
                {
                    window.ToggleSimulation(); await Task.Delay(250); window.StopSimulation();
                    await Capture("actor-simulated.png");
                    Console.WriteLine("PASS: static actor fixture renders with simulation; no animation clips supplied.");
                }
                window.Width = 980; window.Height = 640; await Task.Delay(50); await Capture("actor-compact.png");
                window.ShowOverview();
                if (window.IsActorMode) throw new Exception("Actor was not released on Back.");
                if (armorButton.IsVisible) throw new Exception("Armor controls remained visible after leaving Player");
                Result = 0; Console.WriteLine("PASS: actor GPU rendering, simulation, compact viewport and Back cleanup."); Finish(null);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        };
        async Task<byte[]> Capture(string name)
        {
            var source = new TaskCompletionSource<byte[]>();
            void Receive(byte[] pixels, int width, int height)
            {
                window.ModelSurface.FrameCaptured -= Receive;
                try
                {
                    var colors = Enumerable.Range(0, pixels.Length / 4).Select(i => BitConverter.ToUInt32(pixels, i * 4)).Distinct().Take(32).Count();
                    if (colors < 8 && name != "actor-hidden.png") throw new Exception("Actor model is blank.");
                    using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    for (int y = 0; y < height; y++) Marshal.Copy(pixels, y * width * 4, bitmap.GetPixels() + (height - 1 - y) * bitmap.RowBytes, width * 4);
                    using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(System.IO.Path.Combine(Output, name), png.ToArray()); source.SetResult(pixels);
                }
                catch (Exception ex) { source.SetException(ex); }
            }
            window.ModelSurface.FrameCaptured += Receive; window.ModelSurface.CaptureNextFrame = true; window.ModelSurface.RequestNextFrameRendering();
            return await source.Task;
        }
        void Finish(string? error) { timeout.Stop(); if (error != null) Console.Error.WriteLine(error); lifetime.Shutdown(Result); }
        base.OnFrameworkInitializationCompleted();
    }
}
