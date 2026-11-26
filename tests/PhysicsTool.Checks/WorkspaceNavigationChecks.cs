using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Desktop;

internal static class WorkspaceNavigationChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Defaults()
    {
        var window = new MainWindow(graphics: false); window.Show(); Dispatcher.UIThread.RunJobs();
        CheckDefaults(window); window.Close();
        AxisHoverChecks.Run();
        Console.WriteLine("PASS: particles and colliders default off, emission defaults on; all three toggles update the viewport.");
    }
    private static void CheckDefaults(MainWindow window)
    {
        window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        var particles = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "ViewportParticles");
        var colliders = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "ViewportColliders");
        var emission = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "ViewportEmission");
        Require(particles.IsChecked == false && !window.Viewport.ShowParticles && colliders.IsChecked == false && !window.Viewport.ShowColliders,
            "Particles/colliders are not hidden by default");
        Require(emission.IsChecked == true && window.Viewport.ShowEmission, "Emission is not enabled by default");
        particles.IsChecked = colliders.IsChecked = true; emission.IsChecked = false;
        Require(window.Viewport.ShowParticles && window.Viewport.ShowColliders && !window.Viewport.ShowEmission, "Viewport toggles no longer update rendering");
        particles.IsChecked = colliders.IsChecked = false; emission.IsChecked = true;
        window.ShowOverview(); Dispatcher.UIThread.RunJobs();
    }
    internal static void Run(string output)
    {
        UiLanguage.Initialize("en");
        Directory.CreateDirectory(output);
        PlacementScreen primary = new(new(0, 0, 1920, 1040), 1, true);
        PlacementScreen secondary = new(new(1920, 0, 2560, 1400), 1.5, false);
        PlacementScreen left = new(new(-1920, 0, 1920, 1040), 1, false);
        var saved = new SavedWindowPlacement(2200, 100, 1200, 750);
        Require(WindowPlacement.Fit(saved, [primary, secondary]) == saved, "Available secondary placement lost");
        var restored = WindowPlacement.Fit(saved, [primary]);
        Require(restored.X >= 0 && restored.Y >= 0 && restored.X + restored.Width <= 1920 && restored.Y + restored.Height + 40 <= 1040, "Disconnected secondary leaves off-screen window");
        var negative = new SavedWindowPlacement(-1800, 80, 1100, 700);
        Require(WindowPlacement.Fit(negative, [primary, left]) == negative, "Negative monitor coordinates lost");
        Require(WindowPlacement.Fit(negative, [primary]).X >= 0, "Disconnected left monitor not handled");
        foreach (var invalid in new[] { new SavedWindowPlacement(int.MaxValue, int.MaxValue, double.NaN, 0), new(-99999, -99999, 100000, 100000), new(0, 1010, 1000, 700) })
        {
            var fitted = WindowPlacement.Fit(invalid, [primary]);
            Require(fitted.X >= 0 && fitted.Y >= 0 && fitted.Width <= 1920 && fitted.Height <= 1000, "Invalid placement escaped working area");
        }
        var small = WindowPlacement.Fit(saved, [new(new(0, 0, 1280, 680), 1.5, true)]);
        Require(small.Width <= 1280 / 1.5 && small.Height + 40 <= 680 / 1.5, "High-DPI small screen not accommodated");
        string prefs = Path.Combine(output, "preferences.json");
        (new DesktopPreferences(MaximizeOnStartup: true)).Save(prefs);
        Require(DesktopPreferences.Load(prefs).MaximizeOnStartup, "Maximize preference not saved");
        (DesktopPreferences.Load(prefs) with { MaximizeOnStartup = false }).Save(prefs);
        Require(!DesktopPreferences.Load(prefs).MaximizeOnStartup, "Maximize preference not cleared");
        var window = new MainWindow(graphics: false); window.Show(); Dispatcher.UIThread.RunJobs();
        CheckDefaults(window);
        var original = window.Content;
        Require(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "OpenUmii" || Equals(b.Content, "UMii")), "Unreleased appearance editor menu leaked into release");
        foreach (var assembly in new[] { typeof(MainWindow).Assembly, typeof(PhysicsTool.Core.EditorSession).Assembly })
            Require(!assembly.GetTypes().Any(t => t.Name.StartsWith("Umii", StringComparison.OrdinalIgnoreCase)), "Unreleased appearance editor types leaked into release");
        var mapButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "MapEditing");
        Require(Equals(mapButton.Content, "Map Editing"), "Map Editing entry point missing");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "main-map-entry.png"));
        window.ShowCollision(false); Dispatcher.UIThread.RunJobs();
        Require(window.IsToolWorkspaceVisible && window.GetVisualDescendants().OfType<CollisionView>().Count() == 1, "Collision not embedded");
        var view = window.CollisionWorkspace;
        Require(!view!.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() is "Shrine scene..." or "Map Editing"), "Map workspace is still nested in Collision");
        Require(view!.Viewport.GetVisualAncestors().OfType<Control>().Any(c => !c.IsVisible), "Empty collision viewport is visible");
        string mesh = Path.Combine(output, "collision.obj");
        File.WriteAllText(mesh, "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 1\nf 1 3 2\nf 1 2 4\nf 2 3 4\nf 3 1 4\n");
        var load = view.LoadPathAsync(mesh, true);
        while (!load.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        load.GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        Require(view.Viewport.Model != null && view.Viewport.GetVisualAncestors().OfType<Control>().All(c => c.IsVisible), "Loaded collision viewport stays hidden");
        view!.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "CollisionOptions").SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "CollisionScale").Value = 2;
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WorkspaceBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ReferenceEquals(window.Content, original), "Back lost original workspace");
        window.ShowCollision(false); Require(ReferenceEquals(view, window.CollisionWorkspace), "Collision state not retained");
        Dispatcher.UIThread.RunJobs();
        Require(view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "CollisionScale").Value == 2, "Back lost collision settings");
        window.ShowStaticCompoundAsync(null).GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        Require(window.GetVisualDescendants().OfType<BphscView>().Count() == 1, "BPHSC still opens separately");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WorkspaceBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(window.GetVisualDescendants().OfType<CollisionView>().Single() == view, "BPHSC Back lost collision view");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "collision-embedded.png"));
        void Back() => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WorkspaceBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Back();
        mapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var map = window.GetVisualDescendants().OfType<ShrineSceneView>().Single();
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (map.Busy && timeout.Elapsed < TimeSpan.FromSeconds(30)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Require(!map.Busy, "Map browser scan did not finish");
        Require(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Map scene"), "Map scene title missing");
        var search = map.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ShrineSceneSearch");
        search.Text = "I-4";
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "map-scene-browser.png"));
        map.OpenCompound!(null).GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        Back();
        Require(window.GetVisualDescendants().OfType<ShrineSceneView>().Single() == map, "Map compound Back did not return to map");
        Back(); Require(ReferenceEquals(window.Content, original), "Map Back returned to Collision instead of main workspace");
        window.ShowCollision(false); Dispatcher.UIThread.RunJobs();
        Require(ReferenceEquals(view, window.CollisionWorkspace) && view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "CollisionScale").Value == 2, "Map navigation lost collision state");
        view.OpenStaticCompound!(null).GetAwaiter().GetResult(); Dispatcher.UIThread.RunJobs();
        Back(); Require(window.GetVisualDescendants().OfType<CollisionView>().Single() == view, "Collision compound Back reused map destination");
        Back(); mapButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(window.GetVisualDescendants().OfType<ShrineSceneView>().Single() == map && search.Text == "I-4", "Map state was lost when switching workspaces");
        foreach (string language in new[] { "fr", "es", "ja" })
        {
            UiLanguage.Set(language);
            foreach (string label in new[] { "Map Editing", "Map scene", "Save scene", "Search map scenes", "Unsaved map scene" })
                Require(UiLanguage.Text(label) != label, $"Missing {language} translation: {label}");
            var topic = PhysicsTool.Core.ClothHelp.Topics.Single(t => t.Title == "Map scene");
            Require(UiLanguage.Text(topic.Body) != topic.Body, $"Map documentation translation missing: {language}");
        }
        UiLanguage.Set("en"); Back();
        window.ShowIconTool(); Dispatcher.UIThread.RunJobs();
        var icons = window.GetVisualDescendants().OfType<IconToolView>().Single();
        var iconTimeout = System.Diagnostics.Stopwatch.StartNew();
        while (icons.Busy && iconTimeout.Elapsed < TimeSpan.FromSeconds(30)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Require(!icons.Busy, "Icon catalog did not finish");
        Require(!icons.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "IconPresetSearch"), "Capture search bar was not removed");
        var presetList = icons.GetVisualDescendants().OfType<ComboBox>().Single(t => t.Name == "IconCaptureMode");
        Require(presetList.IsEffectivelyEnabled && presetList.IsTextSearchEnabled, "Capture keyboard navigation unavailable");
        presetList.Focus();
        presetList.RaiseEvent(new Avalonia.Input.TextInputEventArgs { RoutedEvent = Avalonia.Input.InputElement.TextInputEvent, Text = "070" });
        Dispatcher.UIThread.RunJobs();
        Require(presetList.SelectedItem is PhysicsTool.Core.IconCapturePreset { Name: "Weapon_Sword_070" }, "Typing 070 did not select the Master Sword preset");
        Require(presetList.Bounds.Height < 60 && presetList.MaxDropDownHeight == 360, "Capture picker is not a compact equipment-style dropdown");
        presetList.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs();
        Require(presetList.IsDropDownOpen, "Capture dropdown did not open");
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "icon-preset-keyboard-070.png"));
        presetList.IsDropDownOpen = false; Dispatcher.UIThread.RunJobs();
        Require(!icons.UsesViewport, "Selecting a visible preset did not activate it");
        presetList.SelectedItem = "Viewport";
        Require(presetList.Items.Count > 700, "Keyboard jumping removed capture presets");
        Require(icons.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CaptureIcon").Content?.ToString() == "Capture icon", "Capture command missing");
        string actorParent = Path.Combine(output, "nested-actors");
        foreach (string relative in new[] { "Update/content/Actor/Pack", "Switch/Actor/Pack", "TotK/romfs/Pack/Actor" })
        {
            string folder = Path.Combine(actorParent, relative); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "Weapon_Sword_070" + (relative.Contains("TotK") ? ".pack.zs" : ".sbactorpack")), []);
        }
        for (int attempt = 0; attempt < 2; attempt++)
        {
            icons.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Open actor")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var dialog = window.OwnedWindows.Single();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (icons.Busy && watch.Elapsed < TimeSpan.FromSeconds(30)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            Require(!icons.Busy, "Actor browser initial scan timed out");
            dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "IconActorRoot").Text = actorParent;
            dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Scan")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            watch.Restart();
            while (icons.Busy && watch.Elapsed < TimeSpan.FromSeconds(30)) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
            Require(!icons.Busy, "Nested actor scan timed out");
            var actorSearch = dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "IconActorSearch");
            actorSearch.Text = "070"; Dispatcher.UIThread.RunJobs();
            var results = dialog.GetVisualDescendants().OfType<ListBox>().Single(t => t.Name == "IconActorResults");
            Require(results.ItemCount == 3, "Parent folder scan lost nested game roots");
            Require(results.Items.OfType<PhysicsTool.Core.IconActor>().Select(a => a.ResourceRoot).Distinct().Count() == 3, "Actor sources mixed across dumps");
            using (var frame = dialog.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "icon-actor-browser.png"));
            actorSearch.Text = "nothing-matches"; Dispatcher.UIThread.RunJobs();
            Require(results.ItemCount == 0 && dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "IconActorStatus").Text == "No actors match this search.", "Actor no-match status missing");
            dialog.Close(); Dispatcher.UIThread.RunJobs();
        }
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "icon-tool-workspace.png"));
        Back(); Require(ReferenceEquals(window.Content, original), "Icon Tool Back did not return to main");
        window.ShowIconTool(); Dispatcher.UIThread.RunJobs();
        Require(ReferenceEquals(icons, window.GetVisualDescendants().OfType<IconToolView>().Single()), "Icon workspace state lost");
        Back();
        window.Close();
        string placement = Path.Combine(output, "placement.json");
        var host = new Window { Width = 1050, Height = 700 };
        WindowPlacement.Attach(host, placement, false); host.Show(); Dispatcher.UIThread.RunJobs(); host.Position = new(100, 80);
        Dispatcher.UIThread.RunJobs(); host.Close();
        Require(JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllBytes(placement)) is { X: 100, Y: 80 }, "Normal placement did not persist");
        var normal = JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllBytes(placement))!;
        var maximized = new Window { Width = 1200, Height = 850 };
        WindowPlacement.Attach(maximized, placement, true); maximized.Show(); Dispatcher.UIThread.RunJobs();
        Require(maximized.WindowState == WindowState.Maximized, "Maximize on startup not applied");
        maximized.Close();
        var afterMax = JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllBytes(placement))!;
        Require(afterMax.Width == normal.Width && afterMax.Height == normal.Height, "Maximized bounds overwrote normal restore size");
        var minimized = new Window(); WindowPlacement.Attach(minimized, placement, false); minimized.Show(); Dispatcher.UIThread.RunJobs();
        minimized.WindowState = WindowState.Minimized; minimized.Close();
        Require(JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllBytes(placement)) == afterMax, "Minimized bounds overwrote placement");
        Console.WriteLine("PASS: separate Map Editing and Collision navigation, contextual BPHSC Back, retained workspace state, map labels/translations, placement persistence, disconnected monitors, DPI and maximize preference.");
    }
}
