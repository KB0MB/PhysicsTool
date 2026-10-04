using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PhysicsTool.Core;
using PhysicsTool.Desktop;

internal static class TuningWindowChecks
{
    public static void Run(MainWindow window, string output)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static Button Button(Window host, string name) => host.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, name));
        static NumericUpDown Number(Window host, string name) => host.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == name);
        static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
        var before = window.Session.Document.CaptureState();
        window.Viewport.SelectRectangle(new Rect(-100000, -100000, 200000, 200000), SelectionOperation.Replace);
        Click(Button(window, "Mass scale")); var dialog = window.OwnedWindows.Single();
        Number(dialog, "MassScale").Value = 2;
        using (var frame = dialog.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "mass-scale.png"));
        Click(Button(dialog, "Apply"));
        Require(window.Session.Document.GetParticleRows(0)[1].Mass == .2f && window.Viewport.Selected.Count == 3, "Mass dialog lost selection or edit");
        window.Session.Undo(); window.Refresh();
        window.Viewport.SelectRectangle(new Rect(-100000, -100000, 200000, 200000), SelectionOperation.Replace);
        Click(Button(window, "Mass scale")); dialog = window.OwnedWindows.Single(); Number(dialog, "MassScale").Value = 3; Click(Button(dialog, "Cancel"));
        Require(window.Session.Document.CaptureState() == before, "Canceled mass scale changed data");
        Click(Button(window, "Cloth settings")); dialog = window.OwnedWindows.Single();
        Number(dialog, "Damping").Value = .6m; Number(dialog, "GravityY").Value = -4;
        Number(dialog, "MinTranslationSpeed").Value = 99; Number(dialog, "MaxTranslationSpeed").Value = 1;
        Click(Button(dialog, "Apply"));
        Require(window.OwnedWindows.Count == 1 && window.Session.Document.CaptureState() == before, "Invalid settings closed or partially applied");
        Number(dialog, "MaxTranslationSpeed").Value = 100;
        using (var frame = dialog.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "cloth-settings.png"));
        Click(Button(dialog, "Apply"));
        Require(window.Session.Document.GetSimulationSettings(0).DampingPerSecond == .6f, "Cloth settings dialog did not apply");
        window.Session.Undo(); window.Refresh();
        Click(Button(window, "Cloth settings")); dialog = window.OwnedWindows.Single(); Number(dialog, "GravityY").Value = -20; Click(Button(dialog, "Cancel"));
        Require(window.Session.Document.CaptureState() == before, "Canceled cloth settings changed data");
        var original = window.SimulationOptions;
        Click(Button(window, "Options")); dialog = window.OwnedWindows.Single();
        dialog.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "WindSpeedSlider").Value = 20;
        Require(Number(dialog, "WindSpeed").Value == 20 && window.SimulationOptions.WindSpeed == 20, "Preview slider not wired");
        Number(dialog, "PlaybackSpeed").Value = 4; Number(dialog, "WindSpeed").Value = 25;
        Require(dialog.GetVisualDescendants().OfType<Slider>().Single(s => s.Name == "WindSpeedSlider").Value == 25, "Numeric input did not update slider");
        dialog.GetVisualDescendants().OfType<ComboBox>().Single().SelectedIndex = (int)WindAxis.NegativeZ;
        Require(window.SimulationOptions.PlaybackSpeed == 4 && window.SimulationOptions.Direction == WindAxis.NegativeZ, "Preview did not update live");
        using (var frame = dialog.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "preview-options.png"));
        Click(Button(dialog, "Cancel")); Require(window.SimulationOptions == original, "Preview cancel did not restore values");
        Click(Button(window, "Options")); dialog = window.OwnedWindows.Single(); Number(dialog, "PlaybackSpeed").Value = .5m; Click(Button(dialog, "Close"));
        Require(window.SimulationOptions.PlaybackSpeed == .5f, "Preview Close discarded values");
        window.ToggleSimulation(); Dispatcher.UIThread.RunJobs();
        Require(!Button(window, "Mass scale").IsEnabled && !Button(window, "Cloth settings").IsEnabled && Button(window, "Options").IsEnabled, "Simulation tuning guards incorrect");
        Click(Button(window, "Options")); dialog = window.OwnedWindows.Single(); Number(dialog, "GravityScale").Value = 2; dialog.Close(); Dispatcher.UIThread.RunJobs();
        Require(window.SimulationOptions.GravityScale == original.GravityScale, "Title-bar close did not cancel preview changes");
        window.StopSimulation();
        Require(Button(window, "Mass scale").IsEnabled && Button(window, "Cloth settings").IsEnabled, "Stop left tuning disabled");
        Require(window.Session.Document.CaptureState() == before && !window.Session.IsDirty, "Preview contaminated native document");
        window.LoadPath(Path.Combine(output, "manual-2.bphcl"));
        Require(!Button(window, "Cloth settings").IsEnabled && !Button(window, "Options").IsEnabled && Button(window, "Mass scale").IsEnabled, "BPHCL tuning capability mismatch");
        window.LoadPath(Path.Combine(output, "manual-0.hkcl"));
        var items = window.GetVisualDescendants().OfType<ListBox>().Single(list => list.SelectionMode == Avalonia.Controls.SelectionMode.Multiple);
        items.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<CheckBox>().Single(check => Equals(check.Content, "Fixed")).IsChecked = true;
        Require(window.Session.Document.GetParticleRows(0).Count(row => row.Fixed) == 2, "Inspector Fixed checkbox removed another anchor");
        window.Session.Undo(); window.Refresh(); items.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
        Number(window, "EditorX").Value = 3; Click(Button(window, "Apply"));
        Require(window.Session.Document.GetParticleRows(0)[0].Fixed && window.Session.Document.GetParticleRows(0)[1].X == 3, "Inspector Apply lost an anchor or particle edit");
        window.Session.Undo(); window.Refresh();
        Require(window.Session.Document.CaptureState() == before, "Inspector undo failed");
        Console.WriteLine("PASS: mass/cloth dialogs, validation, selection retention, live preview cancel/close, simulation guards and BPHCL restrictions.");
    }
}
