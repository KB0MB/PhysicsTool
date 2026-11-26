using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using SkiaSharp;
using HKCLTool;

internal sealed class IconCharacterChecks : Application
{
    internal static string Root = "", Output = "", Dump = "", Botw = "";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1150, Height = 850, ShowInTaskbar = false };
        Directory.CreateDirectory(Output);
        string folders = Path.Combine(Output, "actor-folders.json");
        File.WriteAllText(folders, System.Text.Json.JsonSerializer.Serialize(new ActorBrowserPaths { Totk = Dump, Botw = Botw }));
        var view = new IconToolView(window, actorFoldersPath: folders); window.Content = view; lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                var selector = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconCharacter");
                Check(((IconCharacterProfile)selector.SelectedItem!).IsLink, "Link is not the default");
                var riju = IconCharacterProfile.LoadAll().Single(p => p.Id == "Riju");
                await Task.Run(() => {
                var source = new BotwActorResources(Path.Combine(IconCaptureAsset.Root(Root), "Pack/Actor/Player.pack.zs"), [IconCaptureAsset.Root(Root), Dump]);
                source.PrepareTotkStrings();
                var original = new ToolboxBfresDocument(source.ReadModel("Link.Link_RaulSkin")!);
                foreach (dynamic model in original.Models)
                foreach (dynamic shape in ToolboxBfresDocument.Items((object)model.Shapes))
                    if (((string)shape.Name).Contains("Bra", StringComparison.OrdinalIgnoreCase))
                    {
                        dynamic material = ToolboxBfresDocument.Items((object)model.Materials).ElementAt((int)shape.MaterialIndex);
                        Console.WriteLine("ORIGINAL BRA " + shape.Name + " MATERIAL " + material.Name);
                        foreach (dynamic reference in ToolboxBfresDocument.Items((object)material.TextureRefs)) Console.WriteLine("BRA TEXTURE " + (reference is string text ? text : (string)reference.Name));
                    }
                });
                await view.SelectCharacterAsync(riju);
                var actors = IconCaptureAsset.Catalog(Root);
                foreach (string name in new[] { "Armor_001_Head", "Armor_001_Upper", "Armor_009_Upper", "Armor_008_Lower" })
                {
                    var actor = actors.First(a => a.Name == name);
                    await view.LoadPathAsync(actor.Path, actor.ResourceRoot);
                    if (view.Asset?.Name != name) Console.WriteLine(string.Join(" | ", view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t))));
                    Check(view.Asset?.Name == name && view.Asset.CharacterId == "Riju", "Riju load failed: " + name);
                    var props = view.CaptureModel!.Meshes.Where(m => IconArmorProps.IsProp(m.Name)).ToArray();
                    Check(props.Length == 9, "Unexpected props: " + string.Join(',', props.Select(p => p.Name)));
                    Check(props.Where(p => !IconArmorProps.MatchesArmorSlot(name, p.Name)).All(p => view.Viewport.HiddenMeshes.Contains(view.CaptureModel!.Meshes.IndexOf(p))), "Riju other-slot props enabled by default");
                    Check(props.All(p => p.Vertices.All(float.IsFinite)), "Invalid geometry");
                    Check(!props.Any(p => p.Name.Contains("Belt") || p.Name.Contains("Fingers")), "Missing parts filled with Link geometry");
                    foreach (var prop in props)
                        Console.WriteLine(name + " " + prop.Name + " normal=" + prop.NormalTexture?.Name);
                    if (name.EndsWith("Head"))
                    {
                        Check(view.Asset!.ArmorPose!.EarRotationDegrees == new System.Numerics.Vector3(0,50,0), "TotK hood lost ear rotation");
                        Check(props.Single(p => p.Name.EndsWith("Head_Bra")).Texture?.Name == "Link_RaulSkin_Alb", "Bra lost its original texture");
                        Check(props.Count(p => IconMannequinEyes.IsMesh(p.Name)) == 2, "Riju eyes missing");
                        Check(props.Single(p => p.Name.EndsWith("Head_Torso")).NormalTexture?.Name == "Link_RaulSkin_Nrm", "Riju texture alias not used");
                    }
                    File.WriteAllText(Path.Combine(Output, name + "-warnings.txt"), string.Join('\n', view.Asset!.Warnings));
                    byte[] png = await view.CapturePngAsync(512);
                    using var bitmap = SKBitmap.Decode(png);
                    Check(bitmap.Pixels.Count(p => p.Alpha > 32) > 1000, "Blank capture");
                    File.WriteAllBytes(Path.Combine(Output, name + ".png"), png);
                    if (name.EndsWith("Head"))
                    {
                        for (int i = 0; i < view.CaptureModel.Meshes.Count; i++) view.SetCaptureMeshVisible(i, IconArmorProps.IsProp(view.CaptureModel.Meshes[i].Name));
                        File.WriteAllBytes(Path.Combine(Output, "head-props.png"), await view.CapturePngAsync(512));
                        await view.LoadPathAsync(actor.Path, actor.ResourceRoot);
                    }
                    var saved = view.SavePresetBytes("Riju " + name);
                    var zoom = view.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "IconZoom");
                    zoom.Value = 137;
                    await view.SelectCharacterAsync(IconCharacterProfile.Link);
                    Check(view.Asset!.CharacterId == "Link" && zoom.Value == 137, "Switch changed camera settings");
                    await view.LoadPresetBytesAsync(saved);
                    Check(view.Asset!.CharacterId == "Riju", "Preset did not restore Riju");
                    if (view.Asset.WithDye != null)
                    {
                        Check(view.Asset.WithDye(view.Asset.Dyes!.Choices.Last()).CharacterId == "Riju", "Dye lost character");
                        Check(view.Asset.WithDye(view.Asset.Dyes!.Choices.Last()).ArmorPose!.EarRotationDegrees == view.Asset.ArmorPose!.EarRotationDegrees, "Dye lost ear rotation");
                    }
                }
                await Task.Delay(200);
                window.UpdateLayout();
                using var shot = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1150, 850));
                shot.Render(window); shot.Save(Path.Combine(Output, "character-ui.png"));
                Console.WriteLine("Character profiles: PASS"); Result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { window.Close(); lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
