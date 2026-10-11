using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class SelectionConstraintChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output)
    {
        foreach (var format in new[] { 0, 2 })
        {
            var window = new MainWindow(graphics: false); window.Show();
            window.LoadPath(Path.Combine(output, $"manual-{format}." + (format == 2 ? "bphcl" : "hkcl")));
            window.ShowEditor(); Dispatcher.UIThread.RunJobs();
            var tabs = window.GetVisualDescendants().OfType<TabStrip>().Single();
            void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            Button Constraint() => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add constraint"));
            ListBox Items() => window.GetVisualDescendants().OfType<ListBox>().Single(l => l.SelectionMode.HasFlag(SelectionMode.Multiple));
            void Select(params int[] indices)
            {
                var list = Items(); list.SelectedItems!.Clear();
                foreach (var index in indices) list.SelectedItems.Add(list.Items[index]);
            }
            Select(0); Require(!Constraint().IsEnabled, "One selected particle allowed constraint creation");
            window.Viewport.Focus(); window.KeyPress(Key.F, RawInputModifiers.None, PhysicalKey.None, null);
            Require(window.OwnedWindows.Count == 0, "F bypassed single-particle constraint guard");
            Select(2, 0); Require(Constraint().IsEnabled, "Explicit particle pair disabled constraints");
            Click(Constraint());
            var dialog = window.OwnedWindows.Single();
            var numbers = dialog.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
            var a = numbers.Single(n => n.Name == "ConstraintParticleA"); var b = numbers.Single(n => n.Name == "ConstraintParticleB");
            var stiffness = numbers.Single(n => n.Name == "ConstraintStiffness");
            var apply = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Add"));
            var error = dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ConstraintValidation");
            Require(a.Value == 2 && b.Value == 0, "Constraint endpoints did not preserve explicit selection order");
            b.Value = a.Value; Require(!apply.IsEnabled, "Self-link was accepted");
            b.Value = .5m; Require(!apply.IsEnabled, "Fractional particle index was accepted");
            b.Value = null; Require(!apply.IsEnabled, "Missing endpoint was accepted");
            b.Value = 0;
            var kinds = dialog.GetVisualDescendants().OfType<ComboBox>().Single(); kinds.SelectedIndex = 1;
            Require(apply.IsEnabled && error.Text!.Contains("Particle B is fixed"), "Fixed directed endpoint warning missing");
            kinds.SelectedIndex = 0; stiffness.Value = 0;
            Require(apply.IsEnabled && error.Text!.Contains("Zero stiffness"), "Zero-stiffness warning missing");
            kinds.SelectedIndex = 3; Require(!apply.IsEnabled, "Inspect-only one-particle constraint became authorable");
            Click(dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
            Require(!window.Session.IsDirty, "Canceled constraint changed file");

            if (format == 0)
                window.Session.Edit((doc, cloth) => { doc.AddBone(cloth); doc.AddBone(cloth); });
            window.Refresh();
            foreach (var mode in format == 0 ? new[] { 0, 1, 2 } : new[] { 0 })
            {
                if (mode == 2)
                {
                    var colliderFixture = Path.Combine(output, "native-animation", "animation-test.hkcl");
                    if (!File.Exists(colliderFixture)) continue;
                    window.LoadPath(colliderFixture);
                }
                tabs.SelectedIndex = mode; Dispatcher.UIThread.RunJobs();
                Select(1, 0); var activeIndex = Items().SelectedIndex;
                HashSet<int> ViewSelection() => mode == 0 ? window.Viewport.Selected : mode == 1 ? window.Viewport.SelectedBones : window.Viewport.SelectedColliders;
                var selected = ViewSelection().ToHashSet();
                var before = window.Session.Document.CaptureState();
                window.Session.Edit((doc, cloth) => SelectionTransforms.Apply(doc, cloth, (PhysicsSelectionKind)mode, selected, Matrix4x4.CreateTranslation(.1f, .2f, 0), false));
                var after = window.Session.Document.CaptureState(); window.Refresh();
                Require(ViewSelection().SetEquals(selected) && Items().SelectedIndex == activeIndex,
                    $"Refresh lost multi-selection or active item (format {format}, mode {mode}, selected {string.Join(',', ViewSelection())})");
                window.Viewport.Focus(); window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
                Require(window.Session.Document.CaptureState() == before && ViewSelection().SetEquals(selected), "Undo lost selection");
                window.KeyPress(Key.Z, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
                Require(window.Session.Document.CaptureState() == after && ViewSelection().SetEquals(selected), "Redo lost selection");
                Require(Items().SelectedIndex == activeIndex, "Redo lost the active item");
                if (mode != 0) Require(!Constraint().IsEnabled, "Non-particle selection enabled constraints");
                Select(); window.Refresh(); Require(ViewSelection().Count == 0, "Refresh replaced empty selection with a particle");
            }
            tabs.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Select(2, 0); window.LoadPath(Path.Combine(output, "manual-1.hkcl"));
            Require(window.Viewport.Selected.SetEquals(new[] { 0 }), "Old selection leaked into another document");
            window.Session.Edit((doc, cloth) => doc.AddParticle(cloth)); window.Refresh(); Select(0, 1, 2, 3);
            Require(!Constraint().IsEnabled, "Constraint silently ignored excess selected particles");
            Select(0, 1, 2); Require(Constraint().IsEnabled, "Triangle selection unavailable");
            window.Session.Clear(); window.Close();
        }
        Console.WriteLine("PASS: multi-selection/active item on refresh, undo/redo across item types and formats; explicit constraint endpoints, warnings, arity, cancellation and document isolation.");
    }
}
