using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using SkiaSharp;

internal sealed class IconPropsChecks : Application
{
    internal static string Root = "", Output = "", BotwRoot = "";
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
                foreach (string name in new[] { "Armor_001_Upper", "Armor_005_Upper", "Armor_009_Upper", "Armor_006_Upper", "Armor_001_Lower", "Armor_009_Head", "Armor_001_Head", "Weapon_Sword_070" })
                {
                    var actor = actors.First(a => a.Name == name);
                    bool totk = actor.Path.EndsWith(".pack.zs", StringComparison.OrdinalIgnoreCase);
                    await view.LoadPathAsync(actor.Path, actor.ResourceRoot);
                    Check(view.Asset?.Name == name, "Load failed: " + name);
                    Check(view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconEnvironment").IsEnabled, "Lighting palette is disabled");
                    Check(view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconCastShadows").IsEnabled, "Cast shadows is disabled");
                    if (name.EndsWith("Head")) view.Asset!.ArmorPose!.Apply(new Dictionary<string, System.Numerics.Vector3>(), recoveredPose: true);
                    var model = view.CaptureModel!;
                    bool armor = name.StartsWith("Armor_");
                    var props = model.Meshes.Where(m => IconArmorProps.IsProp(m.Name)).ToArray();
                    Check(props.Length == (armor ? 13 : 0), "Wrong prop count: " + name + " " + string.Join(',', props.Select(m => m.Name)));
                    Check(props.Where(p => !IconArmorProps.MatchesArmorSlot(name, p.Name)).All(p => view.Viewport.HiddenMeshes.Contains(model.Meshes.IndexOf(p))), "Other-slot props were enabled by default");
                    var optionalSkin = model.Meshes.Select((m,i) => (m,i)).Where(p => IconArmorProps.IsRaulSkin(p.m.Name)).ToArray();
                    Check(optionalSkin.Length == (armor ? 3 : 0), "Incorrect RaulSkin availability");
                    Check(optionalSkin.All(p => view.Viewport.HiddenMeshes.Contains(p.i) == view.Asset!.IsMeshHiddenByDefault(p.m.Name)), "Incorrect default RaulSkin visibility");
                    if (totk && name.EndsWith("Upper"))
                    {
                        var visibleRaul = optionalSkin.Where(p=>!view.Viewport.HiddenMeshes.Contains(p.i)).Select(p=>p.m.Name[^1..]).ToHashSet();
                        Check(visibleRaul.SetEquals(name=="Armor_005_Upper" ? new[]{"A","B"} : new[]{"A"}), "Native right-hand coverage is wrong: "+name);
                    }
                    if (name == "Armor_001_Upper")
                    {
                        if (totk)
                        {
                            var shadowToggle = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconCastShadows");
                            shadowToggle.IsChecked = true;
                            var withShadow = await view.CapturePngAsync(256);
                            shadowToggle.IsChecked = false;
                            var withoutShadow = await view.CapturePngAsync(256);
                            Check(!withShadow.SequenceEqual(withoutShadow), "TotK armor shadow control has no visible effect");
                            File.WriteAllBytes(Path.Combine(Output, name + "-no-shadow.png"), withoutShadow);
                            shadowToggle.IsChecked = true;
                            var environment = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconEnvironment");
                            var savedEnvironment = environment.SelectedItem;
                            environment.SelectedIndex = environment.SelectedIndex == 0 ? 1 : 0;
                            var changedPalette = await view.CapturePngAsync(256);
                            Check(!withShadow.SequenceEqual(changedPalette), "TotK armor palette control has no visible effect");
                            environment.SelectedItem = savedEnvironment;
                        }
                        var skinIndex = model.Meshes.FindIndex(m => !IconArmorProps.IsProp(m.Name) && m.Name.EndsWith("__Mt_Upper_Skin"));
                        Check(skinIndex >= 0 && view.Viewport.HiddenMeshes.Contains(skinIndex), "Original 001 skin is still visible");
                        Check(model.Meshes.Select((m,i) => (m,i)).Where(p => IconArmorProps.IsProp(p.m.Name)).All(p => view.Viewport.HiddenMeshes.Contains(p.i) == view.Asset!.IsMeshHiddenByDefault(p.m.Name)), "Incorrect replacement 001 skin");
                        var visible = props.Where(p => !view.Viewport.HiddenMeshes.Contains(model.Meshes.IndexOf(p))).Select(p => p.Name).ToHashSet();
                        if (totk) Check(visible.SetEquals(new[] { "Props / Icon_Upper_Neck", "Props / Icon_Upper_Fingers_L", "Props / Upper_Skin_RaulSkin_A" }), "TotK 001 must use neck, left fingers and RaulSkin A");
                        File.WriteAllText(Path.Combine(Output, "neck-material.json"), System.Text.Json.JsonSerializer.Serialize(props.Single(p => p.Name.EndsWith("Upper_Neck")).Material.Botw));
                        if (!totk)
                        {
                            var nativePath = Path.Combine(actor.ResourceRoot, "Actor", "Pack", "Mannequin_001_Upper.sbactorpack");
                            var native = IconCaptureAsset.Load(nativePath, actor.ResourceRoot);
                            var torso = native.Model.Meshes.First(m => m.Name.StartsWith("Torso__Mt_Torso"));
                            File.WriteAllText(Path.Combine(Output, "native-mannequin-material.json"), System.Text.Json.JsonSerializer.Serialize(torso.Material.Botw));
                        }
                    }
                    foreach (var prop in props)
                    {
                        bool belt = prop.Name.EndsWith("Belt");
                        Check(prop.Texture?.Name == (belt ? "Link_Belt_A_Alb" : IconMannequinMaterial.TextureName), "Wrong prop albedo: " + prop.Name);
                        Check(belt || !prop.Material.UseSpm && !prop.Material.UseAmbientOcclusion, "Prop has sketch/AO enabled");
                        Check(prop.Vertices.All(float.IsFinite), "Non-finite prop geometry");
                        if (prop.Name.Contains("Fingers")) Check(prop.NormalTexture?.Name == "Link_Skin_Nrm", "Finger was assigned face material");
                        if (IconMannequinEyes.IsMesh(prop.Name))
                        {
                            Check(!prop.Material.UseNormal && !prop.Material.UseEyeGlint && prop.EyeGlintTexture == null, "Mannequin eyes retained normal/glint shading");
                            Check(prop.Indices.Length == 48 * 3, "Native eye geometry was changed");
                            var face = props.Single(p => p.Name.EndsWith("Head_Face"));
                            Check(face.Material.Totk is not null
                                ? prop.Material.Totk is not null && !ReferenceEquals(face.Material.Totk, prop.Material.Totk)
                                : prop.Material.Botw is not null && !ReferenceEquals(face.Material.Botw, prop.Material.Botw),
                                "Eye shader overrides mutate the face material");
                        }
                        if (!belt && prop.Material.Botw is { } nativeSkin)
                            Check(nativeSkin.Options["uking_albedo_color"] == 0 && nativeSkin.Options["uking_enable_calc_color0"] == 0, "Mannequin retained damage tint shading");
                    }
                    Check(!view.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "IconAddBelt"), "Standalone belt toggle remains");
                    var dialog = MeshVisibilityDialog.Create(view.Viewport, view.SetCaptureMeshVisible, armor ? IconArmorProps.IsProp : null);
                    var shown = dialog.ShowDialog(window);
                    var tabs = dialog.GetVisualDescendants().OfType<TabControl>().SingleOrDefault();
                    Check((tabs != null) == armor, "Wrong mesh tab visibility");
                    if (tabs != null)
                    {
                        Check(tabs.Items.Cast<TabItem>().Select(t => t.Header?.ToString()).SequenceEqual(new[] { "Armor", "Props" }), "Wrong mesh tab labels");
                        tabs.SelectedIndex = 1; dialog.UpdateLayout();
                        var rows = dialog.GetVisualDescendants().OfType<CheckBox>().ToArray();
                        Check(rows.Length == props.Length && rows.All(c => c.Padding.Left >= 10 && c.Padding.Top >= 7), "Wrong Props rows/padding");
                        foreach (var prop in props.Where(p => !IconArmorProps.MatchesArmorSlot(name, p.Name)))
                        {
                            int propIndex = model.Meshes.IndexOf(prop);
                            rows.Single(r => (int)r.Tag! == propIndex).IsChecked = true;
                            Check(!view.Viewport.HiddenMeshes.Contains(propIndex), "Other-slot prop cannot be enabled");
                            var enabled = view.SavePresetBytes("All props");
                            view.SetCaptureMeshVisible(propIndex, false); view.LoadPresetBytes(enabled);
                            Check(!view.Viewport.HiddenMeshes.Contains(propIndex), "Preset lost other-slot prop");
                            var legacy = System.Text.Json.Nodes.JsonNode.Parse(enabled)!.AsObject();
                            legacy.Remove("HasAllPropsVisibility");
                            view.LoadPresetBytes(System.Text.Encoding.UTF8.GetBytes(legacy.ToJsonString()));
                            Check(view.Viewport.HiddenMeshes.Contains(propIndex), "Old preset enabled newly available prop");
                        }
                        var row = rows[0]; int index = (int)row.Tag!;
                        row.IsChecked = false;
                        Check(view.Viewport.HiddenMeshes.Contains(index), "Prop checkbox did not hide mesh");
                        var saved = view.SavePresetBytes("Props visibility");
                        row.IsChecked = true; view.LoadPresetBytes(saved);
                        Check(view.Viewport.HiddenMeshes.Contains(index), "Preset lost hidden prop");
                        view.SetCaptureMeshVisible(index, true);
                        foreach (var (skin, meshIndex) in optionalSkin)
                        {
                            var optionalRow = rows.Single(r => (int)r.Tag! == meshIndex);
                            optionalRow.IsChecked = false;
                            optionalRow.IsChecked = true;
                            Check(!view.Viewport.HiddenMeshes.Contains(meshIndex), "RaulSkin checkbox did not enable mesh");
                            var enabledPreset = view.SavePresetBytes("RaulSkin enabled");
                            view.SetCaptureMeshVisible(meshIndex, false); view.LoadPresetBytes(enabledPreset);
                            Check(!view.Viewport.HiddenMeshes.Contains(meshIndex), "Preset lost enabled RaulSkin");
                            var legacy = System.Text.Json.Nodes.JsonNode.Parse(enabledPreset)!.AsObject();
                            legacy.Remove("HasRaulSkinVisibility");
                            view.LoadPresetBytes(System.Text.Encoding.UTF8.GetBytes(legacy.ToJsonString()));
                            Check(optionalSkin.Where(p => view.Asset!.IsMeshHiddenByDefault(p.m.Name)).All(p => view.Viewport.HiddenMeshes.Contains(p.i)), "Legacy preset enabled optional skin");
                            optionalRow.IsChecked = false;
                        }
                        using var shot = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)dialog.Bounds.Width, (int)dialog.Bounds.Height));
                        shot.Render(dialog); shot.Save(Path.Combine(Output, name + "-mesh-dialog.png"));
                    }
                    dialog.Close(); await shown;
                    for (int i = 0; i < model.Meshes.Count; i++) view.SetCaptureMeshVisible(i, !view.Asset!.IsMeshHiddenByDefault(model.Meshes[i].Name));
                    if (props.Length > 0)
                    {
                        var prop = props.Last(p => p.Name.StartsWith(IconArmorProps.Prefix + "Icon_" + name.Split('_').Last() + "_")); var before = prop.Vertices.ToArray();
                        string bone = name.EndsWith("Lower") ? "Waist" : name.EndsWith("Head") ? "Head" : "Neck";
                        view.SetPoseAdjustment(bone, new(8, 0, 0));
                        Check(!view.CaptureModel!.Meshes.Single(m => m.Name == prop.Name).Vertices.SequenceEqual(before), "Prop did not follow pose: " + prop.Name);
                        view.SetPoseAdjustment(bone, default);
                    }
                    if (optionalSkin.Length > 0)
                    {
                        view.SetPoseAdjustment("Spine_2", new(8, 0, 0));
                        foreach (var (skin, _) in optionalSkin)
                            Check(!view.CaptureModel!.Meshes.Single(m => m.Name == skin.Name).Vertices.SequenceEqual(skin.Vertices), "RaulSkin did not follow body pose");
                        view.SetPoseAdjustment("Spine_2", default);
                        var savedVisibility = view.SavePresetBytes("Default props");
                        // Head framing intentionally crops most of the arms below the image.
                        foreach (var (skin, meshIndex) in optionalSkin.Where(_ => name.EndsWith("Upper")))
                        {
                            for (int i = 0; i < view.CaptureModel!.Meshes.Count; i++) view.SetCaptureMeshVisible(i, i == meshIndex);
                            var pixels = await view.CapturePngAsync(256);
                            using var rendered = SKBitmap.Decode(pixels);
                            Check(rendered.Pixels.Count(p => p.Alpha > 32) > 20, "RaulSkin capture is blank: " + skin.Name);
                            File.WriteAllBytes(Path.Combine(Output, name + "-" + skin.Name.Split('/').Last().Trim() + ".png"), pixels);
                        }
                        view.LoadPresetBytes(savedVisibility);
                    }
                    var capture = await view.CapturePngAsync(576);
                    File.WriteAllBytes(Path.Combine(Output, name + "-capture.png"), capture);
                    if (name.EndsWith("Upper"))
                    {
                        var originalSkin = view.CaptureModel!.Meshes.Select((m, i) => (m, i)).Where(p => p.m.Texture?.Name == IconMannequinMaterial.TextureName && !IconArmorProps.IsProp(p.m.Name)).ToArray();
                        foreach (var (skin, i) in originalSkin) { Console.WriteLine("ARMOR SKIN " + skin.Name + " vertices=" + skin.Vertices.Length / 8); view.SetCaptureMeshVisible(i, false); }
                        File.WriteAllBytes(Path.Combine(Output, name + "-props-skin-only.png"), await view.CapturePngAsync(576));
                        foreach (var (_, i) in originalSkin) view.SetCaptureMeshVisible(i, name != "Armor_001_Upper");
                    }
                    if (view.Asset!.Reference is { } reference)
                    {
                        var bytes = IconToolView.Encode(reference.Pixels, reference.Width, reference.Height, false);
                        File.WriteAllBytes(Path.Combine(Output, name + "-reference.png"), bytes);
                        using var left = SKBitmap.Decode(bytes); using var right = SKBitmap.Decode(capture);
                        using var sheet = new SKBitmap(1152, 576); using var canvas = new SKCanvas(sheet);
                        canvas.Clear(new SKColor(36, 40, 41)); canvas.DrawBitmap(left, new SKRect(0, 0, 576, 576)); canvas.DrawBitmap(right, new SKRect(576, 0, 1152, 576));
                        using var image = SKImage.FromBitmap(sheet); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                        File.WriteAllBytes(Path.Combine(Output, name + "-compare.png"), png.ToArray());
                    }
                    Console.WriteLine("PASS props, material names, tabs, visibility/preset and posing: " + name);
                }
                Result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
