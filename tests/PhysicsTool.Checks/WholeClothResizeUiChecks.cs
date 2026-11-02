using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HKCLTool;
using PhysicsTool.Core;
using PhysicsTool.Desktop;
using System.Numerics;

internal static class WholeClothResizeUiChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    internal static void Run(string path, string output)
    {
        var owner = new MainWindow(graphics: false); owner.Show();
        try
        {
            owner.LoadPath(path); owner.ShowEditor(); Dispatcher.UIThread.RunJobs();
            var session = owner.Session; var doc = session.Document; var view = owner.Viewport;
            var count = doc.GetClothSummaries().Count;
            Check(count > 0, "Resize UI fixture contains no cloths");
            var dropdown = owner.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "EditorCloths");
            Check(dropdown.ItemCount == count + 1 && Equals(dropdown.Items[0], "All"), "All must be first in the cloth dropdown");
            for (var i = 0; i < count; i++) Check(Equals(dropdown.Items[i + 1], doc.GetClothName(i)), "Normal cloth order changed");
            Check(dropdown.SelectedIndex == session.ClothIndex + 1, "Initial cloth selection was not offset past All");
            var original = doc.CaptureState();
            var positions = Enumerable.Range(0, count).SelectMany(i => doc.GetParticlePreview(i).Particles).Select(p => p.Position).ToArray();
            view.Orbit(.3f, -.2f); view.Pan(60, -35); view.ZoomAt(2, new(100, 140));
            var camera = view.CameraState; var matrix = view.CameraMatrix;
            void Camera() => Check(view.CameraState == camera && view.CameraMatrix == matrix, "All-cloth selection or resize changed the camera");
            if (session.CanSimulate) owner.ToggleSimulation();
            dropdown.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Check(session.AllClothsSelected && session.ClothIndex == EditorSession.AllClothsIndex && !session.HasSelectedEntry, "All was treated as a normal cloth");
            Check(!owner.EditorSimulating && !owner.EditorAnimationPlaying && !session.CanSimulate && !view.CanSelect, "All allows simulation, animation or selection");
            Check(!session.CanManageCloth && !session.CanEditClothSettings && !session.CanScaleMass, "All allows single-cloth session operations");
            Check(view.Data.Particles.Count == positions.Length && view.Data.Particles.Select(p => p.Index).Distinct().Count() == positions.Length, "Aggregate particle preview missing or has colliding IDs");
            Check(view.Data.Bones.Select(b => b.Index).Distinct().Count() == view.Data.Bones.Count && view.Data.Bones.All(b => !b.IsSelectable), "Aggregate bones are selectable or have colliding IDs");
            Check(view.Data.BoneBindings.Count == 0 && view.Data.ParticleAttachments.Count == 0, "Aggregate contains per-cloth simulation bindings");
            Camera();
            Button Named(string name) => owner.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
            foreach (var name in new[] { "MirrorCloth", "FitClothToBones", "MatchBonesToModel", "CopySelection", "PasteSelection", "MirrorSelection", "ResizeBoneBranch" })
                Check(!Named(name).IsEnabled, name + " is enabled for All");
            foreach (var label in new[] { "Mass scale", "Cloth settings", "Attach to Bone", "Add Particle", "Delete" })
                Check(!owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)).IsEnabled, label + " is enabled for All");
            owner.ToggleSimulation(); owner.ToggleEditorPlayback(); owner.SeekEditorAnimation(10); owner.AdvanceEditorPreview(1f / 60);
            Check(!owner.EditorSimulating && !owner.EditorAnimationPlaying && view.Simulated == null && doc.CaptureState() == original, "All invoked a per-cloth preview");
            var resize = Named("ResizeAllCloths"); Check(resize.IsVisible && resize.IsEnabled, "All resize action missing");

            BoneBranchResizeWindow Open()
            {
                Click(resize);
                var popup = owner.OwnedWindows.OfType<BoneBranchResizeWindow>().Single();
                Check(popup.Title == "Resize all cloths", "Whole-file popup title incorrect");
                Check(popup.GetVisualDescendants().OfType<NumericUpDown>().Count(n => n.Value == 1) == 3, "XYZ popup does not begin at baseline");
                return popup;
            }
            void Sample(BoneBranchResizeWindow popup, decimal factor)
            {
                foreach (var input in popup.GetVisualDescendants().OfType<NumericUpDown>()) input.Value = factor;
                Check(popup.PreviewNow(), "All-cloth preview failed"); Dispatcher.UIThread.RunJobs(); Camera();
                Check(view.Data.Particles.Select(p => p.Position).Zip(positions).All(p => Vector3.Distance(p.First, p.Second * (float)factor) < 3e-4f), "Preview accumulated scale or used per-cloth pivots");
                Check(!session.CanUndo, "Preview added an undo entry");
            }
            var popup = Open(); Sample(popup, .5m); Sample(popup, .7m);
            Directory.CreateDirectory(output);
            using (var frame = popup.CaptureRenderedFrame()!) frame.Save(Path.Combine(output, "resize-all-popup.png"));
            Click(popup.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
            Check(doc.CaptureState() == original && !session.CanUndo, "Cancel did not exactly restore the baseline"); Camera();
            popup = Open(); Sample(popup, .6m);
            popup.Close(); Dispatcher.UIThread.RunJobs();
            Check(doc.CaptureState() == original && !session.CanUndo, "Closing the popup did not cancel");
            popup = Open(); Sample(popup, .5m);
            Click(popup.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Reset")));
            Check(doc.CaptureState() == original && !session.CanUndo, "Reset did not restore baseline");
            Sample(popup, .7m);
            Click(popup.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyAllClothsResize"));
            var resized = doc.CaptureState();
            Check(resized != original && session.CanUndo && session.AllClothsSelected, "Apply did not commit resize"); Camera();
            session.Undo(); owner.Refresh();
            Check(doc.CaptureState() == original && !session.CanUndo && session.AllClothsSelected, "Apply was not a single undo step or lost All selection");
            session.Redo(); owner.Refresh();
            Check(doc.CaptureState() == resized && session.AllClothsSelected, "Redo did not restore All resize"); Camera();
            session.Undo(); owner.Refresh();
            using (var invalid = new WholeFileClothResizePreview(session))
            {
                invalid.Sample(new Vector3(.7f));
                try { invalid.Sample(new Vector3(float.NaN)); throw new Exception("Invalid scale accepted"); }
                catch (ArgumentOutOfRangeException) { }
                Check(doc.CaptureState() == original, "Invalid sample did not restore baseline");
                try { invalid.Commit(); throw new Exception("Invalid preview committed"); }
                catch (InvalidOperationException) { }
            }
            dropdown.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Check(session.ClothIndex == 0 && session.HasSelectedEntry && !session.AllClothsSelected, "Switching back changed normal cloth indices");
            Check(view.CanSelect && Named("MirrorCloth").IsEnabled && !resize.IsVisible, "Switching back did not restore per-cloth controls");
            Check(view.Data.Particles.Count == doc.GetParticlePreview(0).Particles.Count, "Switching back retained aggregate geometry"); Camera();
            dropdown.SelectedIndex = 0; Dispatcher.UIThread.RunJobs(); owner.ShowOverview(); owner.ShowEditor(); Dispatcher.UIThread.RunJobs();
            Check(session.AllClothsSelected, "Overview navigation lost All selection"); Camera();
            Console.WriteLine("PASS: All dropdown, aggregate read-only preview, disabled per-cloth actions, baseline samples, Cancel/close/Reset, one-step undo/redo, camera preservation and normal cloth switching.");
        }
        finally { owner.StopSimulation(); owner.Close(); }
    }
}
