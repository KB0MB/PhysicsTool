using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;

internal sealed class IconPresetChecks : Application
{
    internal static string Root = "", Output = "";
    internal static int Result = 1;
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = new Window { Width = 1150, Height = 850, ShowInTaskbar = false };
        var view = new IconToolView(window, graphics: false, botwMannequinRoot: Root);
        window.Content = view; lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Output);
                var all = IconCaptureLibrary.Load();
                Check(all.All(p => !string.IsNullOrWhiteSpace(p.DisplayName) && !p.DisplayName.Contains('<')), "Preset display names are missing or contain message markup");
                Check(all.Single(p => p.Name == "Weapon_Shield_030").ToString() == "Hylian Shield", "Hylian Shield label is incorrect");
                Check(all.Single(p => p.Name == "Weapon_Sword_070").ToString() == "Master Sword", "Master Sword label is incorrect");
                Check(all.All(p => !string.IsNullOrEmpty(p.ModelKey)), "Missing model identities");
                var armor = IconCaptureLibrary.ForActor(all, "Armor_001_Upper");
                var weapons = IconCaptureLibrary.ForActor(all, "Weapon_Sword_070");
                Check(armor.Count < all.Count(p => p.Name.StartsWith("Armor_")) / 2, "Armor upgrades were not collapsed");
                Check(armor.All(p => p.Name.StartsWith("Armor_")) && weapons.All(p => p.Name.StartsWith("Weapon_")), "Cross-category presets leaked");
                Check(weapons.Count == all.Count(p => p.Name.StartsWith("Weapon_")), "Weapon presets were accidentally removed");
                Check(armor.Any(p => p.Name == "Armor_001_Head") && armor.Any(p => p.Name == "Armor_001_Head_B"), "Distinct hood variants collapsed");
                var upgraded = all.Single(p => p.Name == "Armor_002_Upper");
                Check(IconCaptureLibrary.Resolve(armor, upgraded)?.Name == "Armor_001_Upper", "Upgrade did not resolve to base preset");
                var basis = all.Single(p => p.Name == "Armor_001_Upper");
                var changedLight = basis with { Name = "Armor_002_Upper", LightDirection = Vector3.UnitY };
                var changedModel = basis with { Name = "Armor_003_Upper", ModelKey = "different" };
                var missingModel = basis with { Name = "Armor_004_Upper", ModelKey = "" };
                var changedCamera = basis with { Name = "Armor_005_Upper", CameraPosition = Vector3.One };
                Check(IconCaptureLibrary.ForActor([basis, changedLight, changedModel, missingModel, changedCamera], basis.Name).Count == 5, "Distinct/unknown captures were merged");
                var catalog = IconCaptureAsset.Catalog(Root);
                var dropdown = view.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "IconCaptureMode");
                var gravity = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "IconSimulateGravity");
                Check(!gravity.IsVisible, "Gravity should be hidden before loading an actor");
                var capabilities = new HashSet<bool>();
                foreach (string name in new[] { "Weapon_Sword_070", "Weapon_Bow_023", "Armor_002_Upper", "Armor_044_Upper", "Weapon_Sword_070", "Armor_001_Head" })
                {
                    var before = view.Asset == null ? null : CaptureSettings(view);
                    var actor = catalog.First(a => a.Name == name);
                    await view.LoadPathAsync(actor.Path, actor.ResourceRoot);
                    Check(view.Asset?.Name == name, "Actor failed to load: " + name);
                    capabilities.Add(view.Asset!.HasSimulationData);
                    Check(gravity.IsVisible == view.Asset.HasSimulationData, "Gravity visibility does not match cloth/helper data: " + name);
                    Console.WriteLine($"SIMULATION {name}: {view.Asset.HasSimulationData}");
                    if (before != null && name is "Weapon_Bow_023" or "Armor_044_Upper")
                        Check(before == CaptureSettings(view), "Unmatched actor switch reset capture settings: " + name);
                    if (name == "Weapon_Sword_070")
                        Check(dropdown.SelectedItem is IconCapturePreset { Name: "Weapon_Sword_070" }, "Master Sword preset did not load automatically");
                    string category = IconCaptureLibrary.Category(name);
                    Check(dropdown.Items.OfType<IconCapturePreset>().All(p => IconCaptureLibrary.Category(p.Name) == category), "UI retained presets from previous actor category");
                    Check(dropdown.SelectedItem != null && dropdown.Items.Contains(dropdown.SelectedItem), "Selected preset is not in dropdown");
                    if (name == "Weapon_Bow_023") Check(dropdown.SelectedItem is IconCapturePreset retained && retained.Name == "Weapon_Sword_070", "Unlisted weapon lost previous weapon preset");
                    if (name == "Armor_044_Upper") Check(dropdown.SelectedItem?.ToString() == "Custom: Armor custom test", "Unlisted armor lost previous custom preset");
                    Check(dropdown.Items.Contains("Viewport") && dropdown.IsTextSearchEnabled, "Viewport or keyboard jumping was lost");
                    var sections = dropdown.Items.OfType<ComboBoxItem>().ToArray();
                    Check(sections.Length >= 3 && sections.All(s => !s.IsEnabled && !s.Focusable), "Section headings should not be selectable");
                    string? section = null;
                    var sectionLabels = new List<string>();
                    foreach (var item in dropdown.Items)
                    {
                        if (item is ComboBoxItem heading)
                        {
                            Check(sectionLabels.SequenceEqual(sectionLabels.Order(StringComparer.OrdinalIgnoreCase)), "Section labels are not alphabetized");
                            sectionLabels.Clear(); section = ((TextBlock)heading.Content!).Text;
                        }
                        else if (item is IconCapturePreset entry)
                        {
                            Check(section == IconCaptureLibrary.ActorType(entry.Name).Label, "Preset is under the wrong actor-type heading");
                            sectionLabels.Add(entry.ToString());
                        }
                    }
                    Check(sectionLabels.SequenceEqual(sectionLabels.Order(StringComparer.OrdinalIgnoreCase)), "Last section is not alphabetized");
                    if (name == "Armor_002_Upper")
                    {
                        Check(dropdown.SelectedItem is IconCapturePreset { Name: "Armor_001_Upper" }, "Matching upgrade preset did not load automatically");
                        dropdown.SelectedItem = IconCaptureLibrary.Resolve(dropdown.Items.OfType<IconCapturePreset>(), upgraded);
                        Check(((IconCapturePreset)dropdown.SelectedItem!).Name == "Armor_001_Upper", "Upgrade UI selected a missing alias");
                        view.LoadPresetBytes(view.SavePresetBytes("Armor custom test"));
                        Check(dropdown.Items.Cast<object>().Any(p => p.ToString() == "Custom: Armor custom test"), "Custom armor preset missing");
                    }
                    if (category == "Weapon") Check(!dropdown.Items.Cast<object>().Any(p => p.ToString() == "Custom: Armor custom test"), "Custom armor preset shown for weapon");
                    if (name == "Armor_001_Head") Check(dropdown.Items.Cast<object>().Any(p => p.ToString() == "Custom: Armor custom test"), "Custom armor preset not restored after weapon");
                    Console.WriteLine($"PASS {name}: {dropdown.Items.Count} choices, selected {dropdown.SelectedItem}");
                    foreach (var input in view.GetVisualDescendants().OfType<NumericUpDown>())
                    {
                        if (input.Name == "IconActorPosition0") input.Value = .27m;
                        if (input.Name == "IconActorRotation0") input.Value = 23;
                        if (input.Name == "IconLightDirection0") input.Value = -.3m;
                        if (input.Name == "IconZoom") input.Value = 143;
                        if (input.Name == "IconBrightness") input.Value = 2.1m;
                    }
                }
                dropdown.SelectedItem = "Viewport";
                var viewport = view.GetVisualDescendants().OfType<PhysicsViewport>().Single();
                viewport.RestoreCaptureCamera(new(new Vector3(.2f, .4f, .6f), 4, .7f, -.2f, 1.8, 13, -17));
                var viewportSettings = CaptureSettings(view);
                var nextActor = catalog.First(a => a.Name == "Weapon_Bow_023");
                await view.LoadPathAsync(nextActor.Path, nextActor.ResourceRoot);
                Check(viewportSettings == CaptureSettings(view), "Actor switch reset the interactive viewport camera");
                Console.WriteLine("PASS interactive viewport camera retained across actor categories");
                var oneHanded = dropdown.Items.OfType<IconCapturePreset>().Where(p => IconCaptureLibrary.ActorType(p.Name).Label == "One-handed weapons").ToArray();
                var twoHanded = dropdown.Items.OfType<IconCapturePreset>().First(p => IconCaptureLibrary.ActorType(p.Name).Label == "Two-handed weapons");
                dropdown.SelectedItem = oneHanded.Last();
                dropdown.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Down });
                Check(Equals(dropdown.SelectedItem, twoHanded), "Arrow keys did not skip the section header");
                dropdown.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Up });
                Check(Equals(dropdown.SelectedItem, oneHanded.Last()), "Up arrow selected a section header");
                dropdown.RaiseEvent(new Avalonia.Input.TextInputEventArgs { RoutedEvent = Avalonia.Input.InputElement.TextInputEvent, Text = "Hylian Shield" });
                Check(dropdown.SelectedItem is IconCapturePreset { Name: "Weapon_Shield_030" }, "Keyboard name jumping failed across actor sections");
                dropdown.IsDropDownOpen = true;
                await System.Threading.Tasks.Task.Delay(150);
                Check(dropdown.IsDropDownOpen, "Grouped dropdown failed to open");
                dropdown.IsDropDownOpen = false;
                Console.WriteLine("PASS section navigation and keyboard name jumping");
                Check(capabilities.SetEquals([false, true]), "Tests did not cover actors with and without simulation");
                Check(!view.Asset!.HasSimulationData && !gravity.IsVisible, "Rigid weapon unexpectedly exposes gravity");
                gravity.IsChecked = true;
                Check(view.Settlement.IsCompletedSuccessfully, "Hidden gravity setting started unsupported simulation");
                var failed = view.RunCommandAsync("Capture icon", () => throw new IOException("Test export failure: destination is unavailable."));
                var error = lifetime.Windows.Single(w => w.Title == "Icon Tool - Error");
                Check(error.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Text!.Contains("destination is unavailable")), "Error dialog omitted failure reason");
                error.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close")).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await failed;
                Check(!error.IsVisible && !view.Busy, "Error dialog left the editor locked");
                Console.WriteLine($"PASS armor presets {all.Count(p => p.Name.StartsWith("Armor_"))} -> {armor.Count}; weapons {weapons.Count}; aliases, category switching and custom presets");
                Result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { lifetime.Shutdown(Result); }
        };
        base.OnFrameworkInitializationCompleted();
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string CaptureSettings(IconToolView view)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(view.SavePresetBytes("State"))!.AsObject();
        foreach (string field in new[] { "ActorName", "Pose", "HiddenMeshes" }) json.Remove(field);
        return json.ToJsonString();
    }
}
