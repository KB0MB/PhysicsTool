using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Core;
using PhysicsTool.Desktop;

internal static class FileWorkflowChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    public static void Run(string output)
    {
        UiLanguage.Set("en");
        var window = new MainWindow(graphics: false); window.Show(); Dispatcher.UIThread.RunJobs();
        Require(window.Title?.StartsWith("PhysicsTool", StringComparison.Ordinal) == true && !window.Title.Contains("Avalonia") && typeof(MainWindow).Assembly.GetName().Name == "PhysicsTool", "Release product name regressed");
        var portableRoot = Path.Combine(output, "portable-settings-" + Guid.NewGuid().ToString("N"));
        var userRoot = Path.Combine(output, "user-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(portableRoot);
        new DesktopPreferences("es").Save(Path.Combine(portableRoot, "settings.json"));
        Require(AppDataPaths.ResolveDirectory(portableRoot, userRoot, true) == portableRoot, "Windows ZIP settings are not portable");
        var migrated = AppDataPaths.ResolveDirectory(portableRoot, userRoot, false);
        Require(migrated == Path.Combine(userRoot, "PhysicsTool") && DesktopPreferences.Load(Path.Combine(migrated, "settings.json")).Language == "es", "Per-user settings migration failed");
        File.WriteAllText(Path.Combine(portableRoot, "portable.txt"), "");
        Require(AppDataPaths.ResolveDirectory(portableRoot, userRoot, false) == portableRoot, "Portable marker ignored");
        Button Named(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
        Button Label(string text) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, text));
        var open = Named("OpenDonor"); var swap = Named("SwapFiles"); var merge = Label("Merge selected (M)"); var clear = Label("Clear donor");
        Require(!open.IsVisible && !swap.IsVisible && !merge.IsVisible && !clear.IsVisible, "Empty document exposes donor commands");
        var file = Named("FileMenu"); var menu = file.ContextMenu!.Items.OfType<MenuItem>().ToArray();
        Require(menu.Length == 4 && menu.Any(m => Equals(m.Header, "Save as...")), "File menu missing save/export/convert commands");
        Require(Label("Open Actor").IsVisible, "Actor command not renamed");
        window.LoadPath(Path.Combine(output, "manual-0.hkcl")); Dispatcher.UIThread.RunJobs();
        Require(open.IsVisible && !merge.IsVisible && !clear.IsVisible && !swap.IsVisible, "Open donor state incorrect");
        window.LoadDonorPath(Path.Combine(output, "manual-1.hkcl")); Dispatcher.UIThread.RunJobs();
        Require(!open.IsVisible && merge.IsVisible && clear.IsVisible && swap.IsVisible, "Loaded donor state incorrect");
        Require(ReferenceEquals(clear.Parent, swap.Parent), "Swap is not beside Clear");
        var donor = window.DonorSession.Path; Click(clear);
        Require(open.IsVisible && !merge.IsVisible && !swap.IsVisible && !clear.IsVisible && !window.DonorSession.Document.HasDocument, "Clear did not restore Open Donor");
        window.LoadDonorPath(donor!);
        window.Width = 980; window.Height = 640; Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "file-donor-layout.png"));
        var editable = new TextBox { Text = "File" };
        foreach (var code in new[] { "fr", "es", "ja", "en" })
        {
            UiLanguage.Set(code); Dispatcher.UIThread.RunJobs();
            Require(UiLanguage.Text("File") == (code switch { "fr" => "Fichier", "es" => "Archivo", "ja" => "ファイル", _ => "File" }), "Supplemental language catalog missing");
            Require(Equals(file.Content, UiLanguage.Text("File")), "Existing toolbar did not change language");
            Require(Equals(menu[0].Header, UiLanguage.Text("Save")), "Closed File menu did not change language");
            Require(editable.Text == "File", "Language changed editable asset text");
            if (code != "en")
                Require(UiLanguage.Text("Choose different particles.") != "Choose different particles.", "Constraint warning translation missing");
            var dynamicLabel = new TextBlock { Text = "Open Actor" };
            Require(dynamicLabel.Text == UiLanguage.Text("Open Actor"), "New controls did not use current language");
            if (code == "fr")
            {
                using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, "file-donor-french.png"));
                var settings = Label(UiLanguage.Text("Settings")); Click(settings);
                var dialog = window.OwnedWindows.Single();
                Require(dialog.GetVisualDescendants().OfType<ComboBox>().Any(c => c.Name == "InterfaceLanguage"), "Language picker missing");
                using var shot = dialog.CaptureRenderedFrame()!; shot.Save(Path.Combine(output, "settings-french.png"));
                dialog.Close();
            }
        }
        Require(Equals(file.Content, "File"), "English language restoration failed");
        var prefsPath = Path.Combine(output, "preferences-test.json"); File.WriteAllText(prefsPath, "{\"Unrelated\":7}");
        new DesktopPreferences("fr", false, 30).Save(prefsPath);
        Require(DesktopPreferences.Load(prefsPath) == new DesktopPreferences("fr", false, 30) && File.ReadAllText(prefsPath).Contains("Unrelated"), "Preference save/load discarded values");
        File.WriteAllText(prefsPath, "invalid"); Require(DesktopPreferences.Load(prefsPath) == new DesktopPreferences(), "Invalid settings did not use defaults");
        window.ShowEditor(); Dispatcher.UIThread.RunJobs(); window.Viewport.Focus();
        window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
        Require(window.Viewport.Selected.Count == window.Session.Document.GetParticleRows(0).Count, "A failed to select particles");
        window.KeyPress(Key.A, RawInputModifiers.Alt, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
        Require(window.Viewport.Selected.Count == 0, "Alt+A failed to deselect particles");
        window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.None, null); Dispatcher.UIThread.RunJobs();
        var help = window.OwnedWindows.OfType<DocumentationWindow>().Single();
        Require(help.GetVisualDescendants().OfType<TextBlock>().Any(t => t.FontSize == 24 && t.Text == "Start here"), "Initial documentation heading missing");
        Require(!ClothHelp.Topics.Any(t => (t.Title + t.Body).Contains("migration", StringComparison.OrdinalIgnoreCase)), "In-app documentation still contains migration notes");
        var intro = ClothHelp.Topics.Single(t => t.Title == "Start here").Body;
        Require(intro.Contains("Make sure to keep an original copy as a backup!") && !intro.Contains("Save through File"), "Documentation backup wording regressed");
        using (var frame = help.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "documentation-editing.png"));
        help.Close(); window.Close();
        DocumentationChecks.Run(output);
        Console.WriteLine("PASS: File menu, donor visibility/clear, translated controls, portable preferences, select-all shortcuts and documentation.");
    }
}
