using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Security.Cryptography;

internal static class ConversionChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        var folder = Path.Combine(output, "conversion"); Directory.CreateDirectory(folder);
        foreach (var format in new[] { 0, 1, 2 })
        {
            var session = new EditorSession(); session.Load(Path.Combine(output, $"manual-{format}." + (format == 2 ? "bphcl" : "hkcl")));
            session.Edit((d, c) => { var p = d.GetParticleRows(c)[1]; p.Radius += .002f; d.UpdateParticleRows(c, [p]); });
            var before = session.Document.CaptureState(); var sourcePath = session.Path; var dirty = session.IsDirty; var undo = session.CanUndo;
            var conversion = new PhysicsConversion(session.Document, session.Path);
            foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
            {
                var file = Path.Combine(folder, $"manual-{format}-{platform}" + conversion.Extension);
                var result = conversion.Export(file, new(platform)); var check = new HkclService(); check.Load(file);
                Require(result.Entries == 1 && check.GetParticleRows(0).Count == session.Document.GetParticleRows(0).Count, "Converted draft count mismatch");
                Require(check.IsBphcl == (format != 2), "Converted draft format mismatch");
                if (format == 2) Require(HkclService.DetectHkclPlatform(file) == platform, "Converted HKCL has the wrong platform header");
                Require(Math.Abs(check.GetParticleRows(0)[1].Radius - session.Document.GetParticleRows(0)[1].Radius) < 1e-6, "Conversion ignored unsaved edits");
                var reverse = new PhysicsConversion(check, file); var back = Path.Combine(folder, $"back-{format}-{platform}" + reverse.Extension);
                reverse.Export(back, new(platform)); var restored = new HkclService(); restored.Load(back);
                Require(restored.GetParticleRows(0).Count == session.Document.GetParticleRows(0).Count, "Reverse conversion lost particles");
                var existing = SHA256.HashData(File.ReadAllBytes(file));
                try { conversion.Export(file, new(platform, new Dictionary<int, float> { [0] = float.NaN })); throw new Exception("Invalid scales accepted"); }
                catch (ArgumentException) { }
                Require(existing.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))), "Failed export replaced existing output");
            }
            Require(session.Document.CaptureState() == before && session.Path == sourcePath && session.IsDirty == dirty && session.CanUndo == undo, "Conversion mutated editor state");
            Require(!Directory.EnumerateFiles(folder, ".conversion-*").Any(), "Conversion left temporary files");
        }
        var profiled = new EditorSession(); profiled.New("Hire_006_Havok", 2);
        var profilePath = Path.Combine(folder, "Armor_006_Head.bphcl"); profiled.Save(profilePath);
        var profileConversion = new PhysicsConversion(profiled.Document, profilePath);
        Require(profileConversion.Scales.Single().DefaultMassScale > 9.9f && profileConversion.Scales.SequenceEqual(profiled.Document.GetBphclConversionScaleSuggestions()), "Measured filename profile was lost in conversion snapshot");
        var native = Path.Combine(output, "fixture-Armor_1106_RaulSkin_Upper.bphcl");
        if (File.Exists(native))
        {
            var d = new HkclService(); d.Load(native); var original = d.CaptureState(); var conversion = new PhysicsConversion(d, native);
            Require(conversion.Scales.SequenceEqual(d.GetBphclConversionScaleSuggestions()), "Snapshot lost conversion profile identity");
            if (conversion.FidelityWarnings.Count > 0)
            {
                var warningDialog = new ConversionWindow(conversion, HkclPlatform.WiiU) { Width = 760, Height = 470 };
                warningDialog.Show(); Dispatcher.UIThread.RunJobs();
                var warning = warningDialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ConversionFidelityWarnings");
                Require(warning.Text == string.Join("\n\n", conversion.FidelityWarnings) && warning.Bounds.Width <= 720 && warning.Bounds.Height > 0,
                    "Conversion fidelity warning missing or clipped horizontally");
                using (var image = warningDialog.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "conversion-fidelity.png"));
                warningDialog.Close();
            }
            foreach (var platform in new[] { HkclPlatform.WiiU, HkclPlatform.Switch })
            {
                var path = Path.Combine(folder, "native-" + platform + ".hkcl"); var result = conversion.Export(path, new(platform));
                Require(result.Entries == d.GetClothSummaries().Count, "Native conversion lost cloths");
            }
            Require(d.CaptureState() == original, "Native conversion changed source");
        }
        else Console.WriteLine("SKIP: optional native BPHCL conversion fixture.");
        var helper = Path.Combine(output, "fixture-Npc_Zelda_AncientHyrule.bphhb");
        if (File.Exists(helper))
        {
            var d = new HkclService(); d.Load(helper); var original = d.CaptureState(); var hash = SHA256.HashData(File.ReadAllBytes(helper));
            var first = Path.Combine(folder, "helper.bphyssb"); var second = Path.Combine(folder, "helper.bphhb");
            new PhysicsConversion(d, helper).Export(first, new()); var converted = new HkclService(); converted.Load(first);
            Require(converted.IsBphyssb && converted.GetSidecarDriverGroups().Count > 0, "Helper-to-support conversion failed");
            new PhysicsConversion(converted, first).Export(second, new()); var restored = new HkclService(); restored.Load(second);
            Require(restored.IsBphhb && restored.GetSidecarDriverGroups().Count == d.GetSidecarDriverGroups().Count, "Support-to-helper conversion failed");
            Require(d.CaptureState() == original && hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(helper))), "Helper conversion changed source");
        }
        else Console.WriteLine("SKIP: optional helper conversion fixture.");
        RunUi(output);
        Console.WriteLine("PASS: cloth conversion both directions/platforms, helper round-trips, snapshot/undo isolation, failure preservation, scale dialog validation and cancellation.");
    }
    private static void RunUi(string output)
    {
        var window = new MainWindow(graphics: false); window.Show(); Dispatcher.UIThread.RunJobs();
        var fileButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FileMenu");
        var button = fileButton.ContextMenu!.Items.OfType<MenuItem>().Single(b => b.Name == "ConvertPhysics");
        Require(!button.IsEnabled, "Empty document conversion enabled");
        window.LoadPath(Path.Combine(output, "manual-2.bphcl")); Dispatcher.UIThread.RunJobs();
        Require(button.IsEnabled && Equals(button.Header, "Convert to HKCL"), "BPHCL conversion menu missing");
        var before = window.Session.Document.CaptureState();
        button.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var dialog = (ConversionWindow)window.OwnedWindows.Single();
        T Find<T>(string name) where T : Control => dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        Find<TextBox>("ConversionMass0").Text = "NaN"; Dispatcher.UIThread.RunJobs();
        Require(!Find<Button>("ConfirmConversion").IsEnabled, "Invalid conversion scale enabled");
        Find<Button>("ConversionDefaults").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(Find<Button>("ConfirmConversion").IsEnabled, "Scale reset failed");
        dialog.Width = 760; dialog.Height = 470; Dispatcher.UIThread.RunJobs(); using (var image = dialog.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "conversion-dialog.png"));
        var diagnostics = Find<TextBlock>("ConversionDiagnostics0");
        var right = diagnostics.TranslatePoint(new Avalonia.Point(diagnostics.Bounds.Width, 0), dialog)!.Value.X;
        Require(right <= dialog.ClientSize.Width - 15, "Conversion diagnostics clipped at compact width");
        dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(!window.OwnedWindows.Any() && window.Session.Document.CaptureState() == before && !window.Session.IsDirty, "Canceled conversion changed source");
        var job = new PhysicsConversion(window.Session.Document, window.Session.Path); dialog = new ConversionWindow(job, HkclPlatform.WiiU);
        var task = dialog.ShowDialog<PhysicsConversionOptions?>(window); Dispatcher.UIThread.RunJobs();
        Find<ComboBox>("ConversionPlatform").SelectedIndex = 1; Find<TextBox>("ConversionMass0").Text = "2";
        Find<TextBox>("ConversionConstraint0").Text = "0.5"; Find<Button>("ConfirmConversion").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        Require(task.IsCompletedSuccessfully && task.Result is { Platform: HkclPlatform.Switch } o && o.MassScales![0] == 2 && o.ConstraintScales![0] == .5f, "Scale/platform values not returned");
        window.LoadPath(Path.Combine(output, "manual-0.hkcl")); window.ShowEditor(); Dispatcher.UIThread.RunJobs();
        Require(button.IsEnabled && Equals(button.Header, "Convert to BPHCL"), "HKCL conversion menu wrong");
        window.ToggleSimulation(); Require(!button.IsEnabled, "Simulation allows conversion"); window.StopSimulation(); Require(button.IsEnabled, "Stop failed to restore conversion");
        window.Width = 980; window.Height = 640; Dispatcher.UIThread.RunJobs(); using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "conversion-toolbar-980.png"));
        window.Close();
    }
}
